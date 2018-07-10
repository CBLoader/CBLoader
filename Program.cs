using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace CBLoader
{
    class Program
    {
        public static string Version = "1.3.2b4";
        
        [STAThread]
        [LoaderOptimization(LoaderOptimization.MultiDomain)]
        static void Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "__init") return;

            Log.InitLogging();

            try
            {
                Environment.SetEnvironmentVariable("CBLOADER", Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));

                FileManager fm = new FileManager();
                StartupFlags sf = new StartupFlags();
                sf.LoadFromConfig(fm); // Don't require the config file. (Especially before checking we're in the right directory)
                if (!sf.ParseCmdArgs(args, fm))
                    return;
                Log.Info(String.Format("CBLoader version {0}", Version));
                Log.Info();

                string basePath = Utils.GetInstallPath();
                CheckDirectory(basePath);

                FileManager.BasePath = AppDomain.CurrentDomain.BaseDirectory;
                if (File.Exists(Path.Combine(FileManager.BasePath, "CharacterBuilder.exe")))
                    throw new Exception("CBLoader should be installed in its own folder, and not copied into the Character Builder folder.");

                Log.Debug($"Working directory is: {Environment.CurrentDirectory}");
                Log.Debug($"CBLoader base directory is: {FileManager.BasePath}");
                Log.Debug($"Characer Builder directory is: {basePath}");

                CryptoInfo ci = new CryptoInfo(basePath);

                if (sf.CheckForUpdates && sf.UpdateFirst)
                    fm.DoUpdates(sf.ForcedReload);
                if (!sf.Mergelater)
                    fm.ExtractAndMerge(sf.ForcedReload, basePath, ci);
                if (sf.LoadExec)
                    ProcessLauncher.StartProcess(basePath, sf.Args.ToArray(), FileManager.MergedPath);
                if (sf.Mergelater)
                    fm.ExtractAndMerge(sf.ForcedReload, basePath, ci);

                Utils.CheckIfUserAssoc();
                // And here's the better alternated to DCUpdater.
                if (sf.CheckForUpdates && fm.DoUpdates(sf.ForcedReload))
                {
                    Log.Info("Character Builder has already been launched.\n  The following merges are not a bug, and not slowing down the loading of CB.");
                    fm.ExtractAndMerge(sf.ForcedReload, basePath, ci);
                }
            }
            catch (Exception e)
            {
                Log.Error(String.Empty, e);
            }

            if (Log.ErrorLogged)
            {
                ConsoleWindow.SetConsoleShown(true);
                Console.Write("Error encountered. Would you like to open the log file? (y/n) ");
                if (Console.ReadKey().Key == ConsoleKey.Y)
                {
                    Process p = new Process();
                    p.StartInfo.UseShellExecute = true;
                    p.StartInfo.FileName = Log.LogFileLocation;
                    p.Start();
                }
            }
        }

        private static void CheckDirectory(string baseDirectory)
        {
            if (!File.Exists(Path.Combine(baseDirectory, "CharacterBuilder.exe")))
                throw new Exception("CharacterBuilder.exe not found. Make sure it has been properly installed.");
        }

        /// <summary>
        /// Displays the help text for the commandline arguments.
        /// </summary>
        public static void DisplayHelp()
        {
            Log.Info("Usage: CBLoader.exe [-p] [-e] [-n] [-v] [-a] [-r keyFile] [-k keyFile] [-u userFolder] [-f customFolder] [-fm] [-h] [CBArgs]");
            Log.Info("\t-e\tRe-Extract and Re-Merge the xml files");
            Log.Info("\t-n\tDo not load the executable");
            Log.Info("\t-v\tRuns CBLoader in verbose mode. Useful for debugging.");
            Log.Info("\t-a\tAssociate CBLoader with .dnd4e character files.");
            Log.Info("\t-r\tExtracts a keyfile from you local registry.");
            Log.Info("\t-k\tUse a keyfile instead of the registry for decryption.");
            Log.Info("\t-u\tSpecifies the directory used to hold working files Defaults to the user directory.");
            Log.Info("\t-f\tSpecifies a folder containing custom rules files. This switch can be specified multiple times");
            Log.Info("\tCBArgs\tAny arguments int the list not recognized by cbloader will be passed on to the character builder application.");            
            Log.Info("\t+fm\tLaunches Character builder first, then merges files. The merged files will not show until you restart character builder.");
            Log.Info("\t-h\tDisplay this help.");
        }
    }
}
