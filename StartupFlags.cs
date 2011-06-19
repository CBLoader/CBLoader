using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml.Serialization;

namespace CharacterBuilderLoader
{
    public class StartupFlags
    {
        public const string CONFIG_FILENAME = "default.cbconfig";
 
        public bool LoadExec { get; set; }
        public bool ForcedReload { get; set; }
        public bool PatchFile { get; set; }
        public bool Mergelater { get; set; }
        public bool UpdateFirst { get; set; }

        private static readonly XmlSerializer configSerializer = new XmlSerializer(typeof(SettingsType));

        public StartupFlags()
        {
            LoadExec = true;
            ForcedReload = false;
            PatchFile = false;
            Mergelater = false;
            UpdateFirst = false;
        }



        /// Parses the command line arguments and sets the necessary state flags across the applicaiton.
        /// Returns a structure of startup flags to the caller.
        /// </summary>
        /// <returns>A structure containging flags important to how the application should load</returns>
        public bool ParseCmdArgs(string[] args, FileManager fm)
        {
            if (args != null && args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "-e": this.ForcedReload = true; break;
                        case "-n": this.LoadExec = false; break;
                        case "-v": Log.VerboseMode = true; break;
                        case "-p": this.PatchFile = true; break;
                        case "-a": Utils.UpdateRegistry(); break;
                        case "-u": 
                            FileManager.BasePath = getArgString(args, ref i);
                            if (!Directory.Exists(FileManager.BasePath))
                                Directory.CreateDirectory(FileManager.BasePath);
                            break;
                        case "-r":
                            fm.KeyFile = getArgString(args, ref i);
                            Utils.ExtractKeyFile(fm.KeyFile);
                            break;
                        case "-k":
                            fm.KeyFile = getArgString(args, ref i);
                            break;
                        case "-f":
                            fm.CustomFolders.Add(getArgString(args, ref i));
                            break;
                        case "-c": // Load a different config file.
                            LoadFromConfig(fm,getArgString(args, ref i));
                            break;
                        case "-?":
                        case "-h":
                            Program.DisplayHelp();
                            return false;
                        // Fast Mode
                        case "-fm":
                            this.Mergelater = File.Exists(FileManager.MergedPath); break;
                        default:
                            if (File.Exists(args[i])) // Otherwise we lose the quotes, and Character builder can't find the file. (if there's whitespace in the path)
                                ProcessManager.EXECUTABLE_ARGS += " \"" + args[i] + "\"";
                            else
                                ProcessManager.EXECUTABLE_ARGS += " " + args[i];   // character Builder has args as well.  Lets pass unknown ones along.
                            break;
                    }
                }
            }
            return true;
        }



        /// <summary>
        /// simple helper for safely pulling a string argument out of an args list
        /// </summary>
        private static string getArgString(string[] args, ref int i)
        {
            if (args.Length > i + 1)
                return args[++i];
            else
            {
                Program.DisplayHelp();
                throw new FormatException("Invalid Arguments Specified");
            }
        }

        public bool LoadFromConfig(FileManager fm)
        {
            return LoadFromConfig(fm, CONFIG_FILENAME);  
        }
        // The whole point of not using app.config was that we could have more than one.
        public bool LoadFromConfig(FileManager fm, string ConfigFile)
        {
            string fileName;
            if (File.Exists(ConfigFile))
                fileName = ConfigFile;
            else if (File.Exists(FileManager.BasePath + ConfigFile))
                fileName = FileManager.BasePath + ConfigFile;
            else
                return false;

            Log.Debug("Loading Config File: " + fileName);
            try
            {
                SettingsType settings;
                using (StreamReader sr = new StreamReader(fileName))
                {
                    settings = (SettingsType)configSerializer.Deserialize(sr);
                }
                if (settings.Folders != null)
                    foreach (string customFolder in settings.Folders)
                        fm.CustomFolders.Add(Environment.ExpandEnvironmentVariables(customFolder));
                if (settings.AlwaysRemergeSpecified)
                    this.ForcedReload = settings.AlwaysRemerge;
                if (!String.IsNullOrEmpty(settings.BasePath))
                {
                    FileManager.BasePath = Environment.ExpandEnvironmentVariables(settings.BasePath);
                    if (!Directory.Exists(FileManager.BasePath))
                        Directory.CreateDirectory(FileManager.BasePath);
                }
                if (!String.IsNullOrEmpty(settings.CBPath))
                    Environment.CurrentDirectory = Environment.ExpandEnvironmentVariables(settings.CBPath);
                if (settings.FastModeSpecified)
                    this.Mergelater = settings.FastMode;
                if (!String.IsNullOrEmpty(settings.KeyFile))
                    fm.KeyFile = Environment.ExpandEnvironmentVariables(settings.KeyFile);
                if (settings.VerboseModeSpecified)
                    Log.VerboseMode = settings.VerboseMode;
                if (settings.UpdateFirstSpecified)
                    this.UpdateFirst = settings.UpdateFirst;
                if (settings.LaunchBuilderSpecified)
                    this.LoadExec = settings.LaunchBuilder;
            }
            catch (Exception e)
            {
                Log.Error("Error Loading Config File", e);
                return false;
            }
            return true;
        }
    }
}
