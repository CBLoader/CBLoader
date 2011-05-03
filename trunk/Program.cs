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

                StartupFlags sf = new StartupFlags();
                if(!sf.LoadFromConfig(fm) || !sf.ParseCmdArgs(args, fm))
                    return;
                
                CheckWorkingDirectory();
                if (!sf.Mergelater)
                    fm.ExtractAndMerge(sf.ForcedReload);
                if (sf.LoadExec)
                {
                    if (sf.PatchFile)
                        ProcessManager.StartProcessAndPatchFile();
                    else
                        ProcessManager.StartProcessAndPatchMemory();
                }
                if (sf.Mergelater)
                    fm.ExtractAndMerge(sf.ForcedReload);

                // From Jeff: this is kinda creepy to have. We'll leave it for now though.
                //   Stephen:   My only issue is the fact that it pops up as an additional cmd window for a quarter of a second. I'll find a better way of doing it.
                //     I've now made a better way of doing it. (See FileManager.CheckMetaData).  This should now be obselete, and therefore removable.
                // Checks for updates.  http://www.donationcoder.com/Software/Mouser/Updater/help/index.html for details.
                if (File.Exists("dcuhelper.exe"))
                {
                    // Eventually I want to keep CBLoader up to date with it; for now, leave it there for the sole 
                    // purpose of sharing my homebrew with my players.  The useful thing about DCUpdater is that it can handle multiple update files with no extra effort, just put them into the folder.
                    ProcessStartInfo dcuhelper = new ProcessStartInfo("dcuhelper.exe", "-ri \"CBLoader\" \".\" \".\" -shownew -check -nothingexit");
                    dcuhelper.WindowStyle = ProcessWindowStyle.Minimized;
                    Process.Start(dcuhelper);
                }
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


        /// <summary>
        /// Displays the help text for the commandline arguments.
        /// </summary>
        public static void DisplayHelp()
        {
            Log.Info("Usage: CBLoader.exe [-p] [-e] [-n] [-v] [-a] [-r keyFile] [-k keyFile] [-u userFolder] [-f customFolder] [-fm] [-h] [CBArgs]");
            Log.Info("\t-p\tUse Hard Patch mode.");
            Log.Info("\t-e\tRe-Extract and Re-Merge the xml files");
            Log.Info("\t-n\tDo not load the executable");
            Log.Info("\t-v\tRuns CBLoader in verbose mode. Useful for debugging.");
            Log.Info("\t-a\tAssociate CBLoader with .dnd4e character files.");
            Log.Info("\t-r\tExtracts a keyfile from you local registry.");
            Log.Info("\t-k\tUse a keyfile instead of the registry for decryption.");
            Log.Info("\t-u\tSpecifies the directory used to hold working files Defaults to the user directory.");
            Log.Info("\t-f\tSpecifies a folder containing custom rules files. This switch can be specified multiple times");
            Log.Info("\tCBArgs\tAny arguments int the list not recognized by cbloader will be passed on to the character builder application.");            
            Log.Info("\t-f\tLaunches Character builder first, then merges files. The merged files will not show until you restart character builder.");
            Log.Info("\t-h\tDisplay this help.");
        }

    }
}
