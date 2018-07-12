using Mono.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace CBLoader
{
    [XmlRoot("Settings", IsNullable = false)]
    public sealed class OptionsFileSchema
    {
        // Current list options
        [XmlArrayItem("Custom", IsNullable = false)]
        public string[] Folders { get; set; }
        [XmlArrayItem("Part", IsNullable = false)]
        public string[] Ignore { get; set; }

        // Current string options
        public string CachePath { get; set; }
        public string CharacterBuilderPath { get; set; }

        // Current boolean options
        public bool VerboseMode { get; set; }
        public bool AlwaysRemerge { get; set; }
        public bool LaunchBuilder { get; set; }
        public bool CheckForUpdates { get; set; }

        [XmlIgnore] public bool VerboseModeSpecified { get; set; }
        [XmlIgnore] public bool AlwaysRemergeSpecified { get; set; }
        [XmlIgnore] public bool LaunchBuilderSpecified { get; set; }
        [XmlIgnore] public bool CheckForUpdatesSpecified { get; set; }

        // Deprecated options
        public string KeyFile { get; set; }
        public string BasePath { get; set; }

        public bool FastMode { get; set; }
        public bool UpdateFirst { get; set; }
        public bool NewMergeLogic { get; set; }
        public bool ShowChangelog { get; set; }

        [XmlIgnore] public bool FastModeSpecified { get; set; }
        [XmlIgnore] public bool UpdateFirstSpecified { get; set; }
        [XmlIgnore] public bool NewMergeLogicSpecified { get; set; }
        [XmlIgnore] public bool ShowChangelogSpecified { get; set; }
    }

    internal sealed class LoaderOptions
    {
        private static readonly XmlSerializer SERIALIER = new XmlSerializer(typeof(OptionsFileSchema));

        public List<string> PartDirectories = new List<string>();
        public List<string> IgnoreParts = new List<string>();
        public List<string> ExecArgs = new List<string>();

        private List<Regex> ignoreGlobs = new List<Regex>();

        public bool HasWarnings = false;

        public string LoadedConfigPath = null;
        public string CBPath = null;
        public string CachePath = null;

        public bool VerboseMode = false;
        public bool ForceRemerge = false;
        public bool LaunchBuilder = true;
        public bool CheckForUpdates = true;

        private void deprecatedCheck(string filename, bool exists, string tag, string extra = null)
        {
            if (!exists) return;
            if (extra != null) extra = $"\n{extra}";
            else extra = "";
            Log.Warn($"Configuration file {filename} has option <{tag}>. This option is no longer supported.{extra}");
            HasWarnings = true;
        }
        private void replacementCheck(bool exists, string tag, string newTag)
        {
            if (!exists) return;
            Log.Warn($"Configuration option <{tag}> has been renamed <>. Please use it instead.");
            HasWarnings = true;
        }
        private string processPath(string configRoot, string relative)
        {
            return Path.Combine(configRoot, Environment.ExpandEnvironmentVariables(relative.Trim()));
        }
        private void AddOptionFile(string filename, string configRoot, OptionsFileSchema data)
        {
            if (LoadedConfigPath != null)
                throw new Exception("Internal error: Attempted to load two configuration files.");

            if (data.Folders != null) PartDirectories.AddRange(data.Folders.Select(x => processPath(configRoot, x)));
            if (data.Ignore != null) IgnoreParts.AddRange(data.Ignore.Select(x => x.Trim()));

            if (data.BasePath != null) CachePath = processPath(configRoot, data.BasePath);
            if (data.CachePath != null) CachePath = processPath(configRoot, data.CachePath);
            if (data.CharacterBuilderPath != null) CBPath = processPath(configRoot, data.CharacterBuilderPath);

            if (CachePath == null) CachePath = configRoot;

            if (data.VerboseModeSpecified) VerboseMode = data.VerboseMode;
            if (data.AlwaysRemergeSpecified) ForceRemerge = data.AlwaysRemerge;
            if (data.LaunchBuilderSpecified) LaunchBuilder = data.LaunchBuilder;
            if (data.CheckForUpdatesSpecified) CheckForUpdates = data.CheckForUpdates;

            LoadedConfigPath = filename;

            replacementCheck(data.BasePath != null, "BasePath", "CachePath");
            deprecatedCheck(filename, data.KeyFile != null, "KeyFile",
                            "CBLoader can now pull keys from files guaranteed to exist.");
            deprecatedCheck(filename, data.FastModeSpecified, "FastMode",
                            "The new merge logic should be fast enough to not require it.");
            deprecatedCheck(filename, data.UpdateFirstSpecified, "UpdateFirst");
            deprecatedCheck(filename, data.NewMergeLogicSpecified, "NewMergeLogic",
                            "A faster merge algorithm is always used now.");
            deprecatedCheck(filename, data.ShowChangelog, "ShowChangelog");
        }
        public bool AddOptionFile(string filename)
        {
            if (!File.Exists(filename)) return false;

            Log.Info($"Using config file: {filename}");

            OptionsFileSchema data;
            try
            {
                using (StreamReader sr = new StreamReader(filename))
                    data = (OptionsFileSchema)SERIALIER.Deserialize(sr);
            }
            catch (Exception e)
            {
                Log.Error($"Failed to parse configuration file in {filename}", e);
                return false;
            }

            AddOptionFile(filename, Directory.GetParent(filename).FullName, data);
            return true;
        }

        private bool invalidOptions(string result)
        {
            Log.Error(result);
            return false;
        }
        public void FinalOptionsCheck()
        {
            if (CachePath == null) throw new CBLoaderException("Internal error: Cache path not set?");
            if (Path.GetFullPath(CBPath).Equals(Path.GetFullPath(CachePath)))
                throw new CBLoaderException("The cache path cannot be the Character Builder directory.");
            if (File.Exists(Path.Combine(CachePath, "CharacterBuilder.exe")))
                throw new CBLoaderException("The cache path cannot contain an installation of Character Builder.");

            foreach (var ignorePart in IgnoreParts)
            {
                var pattern = Regex.Escape(ignorePart).Replace(@"\*\*", @".*").Replace(@"\*", @"[^/\\]*").Replace(@"\?", @".");
                ignoreGlobs.Add(new Regex($"^{pattern}$"));
            }
        }

        public bool IsPartIgnored(string partName)
        {
            return ignoreGlobs.Any(x => x.IsMatch(partName));
        }
    }

    /// <summary>
    /// Exceptions of this type are printed directly to console with no stack trace or message type.
    /// </summary>
    internal sealed class CBLoaderException : Exception
    {
        public CBLoaderException(string message) : base(message) { }
    }

    internal static class Program
    {
        public static string Version;
        static Program()
        {
            var ver = typeof(Program).Assembly.GetName().Version;
            Version = $"{ver.Major}.{ver.Minor}.{ver.Revision}";
        }
        
        private static void setUniqueString(ref string target, string flag, string value)
        {
            if (target != null)
                throw new OptionException($"Cannot specify more than one {flag} flag.", flag);
            target = value;
        }
        private static LoaderOptions processOptions(string[] args)
        {
            var options = new LoaderOptions();

            var showHelp = false;
            var noConfig = false;

            string configFile = null;
            string cbPath = null;
            string cachePath = null;

            bool? verboseMode = null;
            bool? forceRemerge = null;
            bool? launchBuilder = null;
            bool? checkForUpdates = null;

            var opts = new OptionSet() {
                "Usage: CBLoader [-c <config file>]",
                "A homebrew rules loader for the D&D Insider Character Builder",
                "",
                { "c|config=", "A config file to use rather than the default.",
                    value => setUniqueString(ref configFile, "-c", value) },
                { "no-config", "Do not use a configuration file.",
                    value => noConfig = true },
                { "h|?|help", "Shows this help message.",
                    value => showHelp = true },
                "",
                "Path management:",
                { "u|cache-path=", "Sets where to write temporary files.",
                    value => setUniqueString(ref cachePath, "-u", value) },
                { "cb-path=", "Sets where character builder is installed.",
                    value => setUniqueString(ref cbPath, "--cb-path", value) },
                { "f|folder=", "Adds a directory to search for custom rules in.",
                    value => options.PartDirectories.Add(value) },
                { "ignore-part=", "Adds a part file to ignore.",
                    value => options.IgnoreParts.Add(value) },
                "",
                "Execution options:",
                { "v|verbose", "Enables additional debugging output.",
                    value => verboseMode = true },
                { "e|force-remerge", "Always regenerate merged rules file.",
                    value => forceRemerge = true },
                { "n|no-run", "Do not actually launch the character builder.",
                    value => launchBuilder = false },
                { "d|no-update", "Do not check for updates.",
                    value => checkForUpdates = false },
            };

            var execArgs = opts.Parse(args);
            
            if (showHelp)
            {
                opts.WriteOptionDescriptions(Console.Out);
                return null;
            }

            // Load a configation file.
            if (!noConfig)
                if (configFile == null)
                    options.AddOptionFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "default.cbconfig"));
                else
                    options.AddOptionFile(configFile);

            // Copy configuration options from console.
            if (cbPath != null) options.CBPath = cbPath;
            if (cachePath != null) options.CachePath = cachePath;

            if (verboseMode != null) options.VerboseMode = (bool) verboseMode;
            if (forceRemerge != null) options.ForceRemerge = (bool) forceRemerge;
            if (launchBuilder != null) options.LaunchBuilder = (bool) launchBuilder;
            if (checkForUpdates != null) options.CheckForUpdates = (bool) checkForUpdates;

            // Set various derived configuration options.
            if (options.CBPath == null && (options.CBPath = Utils.GetInstallPath()) == null)
                throw new CBLoaderException(
                    "CBLoader could not find an installation of Character Builder.\n" +
                    "Please specify its path with <CBPath>path/to/builder</CBPath> in the configuration " +
                    "or reinstall Character Builder.");
            if (options.PartDirectories.Count == 0 && options.LoadedConfigPath == null)
                options.PartDirectories.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Custom"));

            // Check configuration option consistancy.
            options.FinalOptionsCheck();
            return options;
        }

        private static void main(string[] args)
        {
            var options = processOptions(args);
            if (options == null) return;

            CryptoInfo cryptoInfo = new CryptoInfo(options);
            FileManager fileManager = new FileManager(options, cryptoInfo);

            if (options.CheckForUpdates)
                fileManager.DoUpdates(options.ForceRemerge);
            fileManager.ExtractAndMerge(options.ForceRemerge);
            if (options.LaunchBuilder)
                ProcessLauncher.StartProcess(options, options.ExecArgs.ToArray(), fileManager.MergedPath);

            Utils.CheckIfUserAssoc();
        }

        [STAThread]
        [LoaderOptimization(LoaderOptimization.MultiDomain)]
        internal static void Main(string[] args)
        {
            Log.InitLogging();
            Log.Info(String.Format("CBLoader version {0}", Version));
            Log.Info();

            try
            {
                Environment.SetEnvironmentVariable("CBLOADER", Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));
                main(args);
            }
            catch (OptionException e)
            {
                Log.Info(e.Message);
                Log.Info("Type in 'CBLoader --help' for more information.");
            }
            catch (CBLoaderException e)
            {
                Log.Error(e.Message);
            }
            catch (Exception e)
            {
                Log.Error(null, e);
            }

            if (Log.ErrorLogged && ConsoleWindow.IsInIndependentConsole)
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
    }
}
