using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.Win32;

namespace CharacterBuilderLoader
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                FileManager fm = new FileManager();
                bool loadExec = true;
                bool forcedReload = false;
                bool patchFile = false;
                bool mergelater = false;

                if (args != null && args.Length > 0)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (args[i] == "-e")
                            forcedReload = true;
                        else if (args[i] == "-n")
                            loadExec = false;
                        else if (args[i] == "-v")
                            Log.VerboseMode = true;
                        else if (args[i] == "-p")
                            patchFile = true;
                        else if (args[i] == "-u")
                            FileManager.BasePath = getArgString(args, ref i);
                        else if (args[i] == "-a")
                            UpdateRegistry();
                        else if (args[i] == "-r")
                            ExtractKeyFile(getArgString(args, ref i));
                        else if (args[i] == "-k")
                        {
                            fm.KeyFile = getArgString(args, ref i);
                            fm.ForceUseKeyFile = true;
                        }
                        else if (args[i] == "-f")
                            fm.CustomFolders.Add(getArgString(args, ref i));
                        else if (File.Exists(args[i])) // Otherwise we lose the quotes, and Character builder can't find the file. (if there's whitespace in the path)
                            ProcessManager.EXECUTABLE_ARGS += " \"" + args[i] + "\"";
                        else if (args[i] == "-?" || args[i] == "-h")
                        {
                            displayHelp();
                            return;
                        }
                        else if (args[i] == "-F" && File.Exists(FileManager.MergedPath))
                            mergelater = true;  // Fast Mode
                        else
                        {
                            ProcessManager.EXECUTABLE_ARGS += " " + args[i];   // character Builder has args as well.  Lets pass unknown ones along.
                        }
                    }
                }
                CheckWorkingDirectory();
                Log.Debug("Checking for merge and extract.");
                if (!mergelater)
                    fm.ExtractAndMerge(forcedReload);
                if (loadExec)
                {
                    if (patchFile)
                        ProcessManager.StartProcessAndPatchFile();
                    else
                        ProcessManager.StartProcessAndPatchMemory();
                }
                if (mergelater)
                    fm.ExtractAndMerge(forcedReload);

            }
            catch (Exception e)
            {
                Log.Error(String.Empty, e);
            }
            if (Log.ErrorLogged)
            {
                Log.Info("Errors Encountered While Loading. Would you like to open the log file? (y/n)");
                if (Console.ReadKey().Key == ConsoleKey.Y)
                {
                    Process p = new Process();
                    p.StartInfo = new ProcessStartInfo("notepad.exe", Log.LogFile);
                    p.Start();
                }
            }
        }

        /// <summary>
        /// Sometimes CBLoader is launched in the wrong Working Directory.
        /// This can happen through badly made shortcuts
        /// And from loading CBLoader through a .dnd4e file.
        /// This method brings the WD back to where it should be.
        /// </summary>
        private static void CheckWorkingDirectory()
        {
            Log.Debug("Working Directory is: " + Environment.CurrentDirectory);
            if (!File.Exists("CharacterBuilder.exe"))
            {
                Log.Debug("Character Builder not found.  Resetting Working Directory");
                Environment.CurrentDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                Log.Debug("Working Directory Changed to: " + Environment.CurrentDirectory);
                if (!File.Exists("CharacterBuilder.exe"))
                    throw new FormatException("CharacterBuilder.exe not found.  Make sure you installed CBLoader in the correct folder.");
            }
        }

        private static string getArgString(string[] args, ref int i)
        {
            if (args.Length > i + 1)
                return args[++i];
            else {
                displayHelp();
                throw new FormatException("Invalid Arguments Specified");
            }
        }

        private static void displayHelp()
        {
            Log.Info("Usage: CBLoader.exe [-p] [-e] [-n] [-v] [-a] [-r keyFile] [-k keyFile] [-u userFolder] [-f customFolder] [-h]");
            Log.Info("\t-p\tUse Hard Patch mode.");
            Log.Info("\t-e\tRe-Extract and Re-Merge the xml files");
            Log.Info("\t-n\tDo not load the executable");
            Log.Info("\t-v\tRuns CBLoader in verbose mode. Useful for debugging.");
            Log.Info("\t-a\tAssociate CBLoader with .dnd4e character files.");
            Log.Info("\t-r\tExtracts a keyfile from you local registry.");
            Log.Info("\t-k\tUse a keyfile instead of the registry for decryption.");
            Log.Info("\t-u\tSpecifies the directory used to hold working files Defaults to the user directory.");
            Log.Info("\t-f\tSpecifies a folder containing custom rules files. This switch can be specified multiple times");
            Log.Info("\t-h\tDisplay this help.");
        }

        private static void ExtractKeyFile(string filename)
        {
            RegistryKey rk = Registry.LocalMachine.OpenSubKey("SOFTWARE").OpenSubKey("Wizards of the Coast")
                .OpenSubKey(FileManager.APPLICATION_ID);
            string currentVersion = rk.GetValue(null).ToString();
            string encryptedKey = rk.OpenSubKey(currentVersion).GetValue(null).ToString();
            byte[] stuff = new byte[] { 0x19, 0x25, 0x49, 0x62, 12, 0x41, 0x55, 0x1c, 0x15, 0x2f };
            byte[] base64Str = Convert.FromBase64String(encryptedKey);
            string realKey = Convert.ToBase64String(ProtectedData.Unprotect(base64Str, stuff, DataProtectionScope.LocalMachine));
            XDocument xd = new XDocument();
            XElement applications = new XElement(XName.Get("Applications"));
            XElement application = new XElement(XName.Get("Application"));
            application.Add(createAttribute("ID", FileManager.APPLICATION_ID));
            application.Add(createAttribute("CurrentUpdate", currentVersion));
            application.Add(createAttribute("InProgress", "true"));
            application.Add(createAttribute("InstallStage", "Complete"));
            application.Add(createAttribute("InstallDate", DateTime.Now.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss.fffffffK")));
            XElement update = new XElement(XName.Get("Update" + currentVersion));
            update.Add(realKey);
            application.Add(update);
            applications.Add(application);
            xd.Add(applications);
            xd.Save(filename);
        }

        private static XAttribute createAttribute(string name, string value)
        {
            return new XAttribute(XName.Get(name), value);
        }


        /// <summary>
        /// Sets .dnd4e File Association to CBLoader.
        /// This means that the user can double-click a character file and launch CBLoader.
        /// </summary>
        public static void UpdateRegistry()
        { // I'm not going to bother explaining File Associations. Either look it up yourself, or trust me that it works.
            try // Changing HKCL needs admin permissions
            {
                Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.ClassesRoot.CreateSubKey(".dnd4e");
                k.SetValue("", ".dnd4e");
                k = k.CreateSubKey("shell");
                k = k.CreateSubKey("open");
                k = k.CreateSubKey("command");
                k.SetValue("", (Environment.CurrentDirectory.ToString() + "\\CBLoader.exe \"%1\""));
            }
            catch (UnauthorizedAccessException ua)
            {
                Log.Error("There was a problem setting file associations", ua);
            }
        }
    }
}
