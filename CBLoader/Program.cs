using Mono.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Serialization;

namespace CBLoader
{
    [Serializable]
    public sealed class Redirect
    {
        [XmlAttribute("from")]
        public string From;
        [XmlAttribute("to")]
        public string To;
        public bool? Confirmed;
    }

    public sealed class PartFolders
    {
        [XmlAttribute] public bool NoUpdate;
        [XmlIgnore] public bool NoUpdateSpecified;

        [XmlText] public string Path;

        public PartFolders() { }
        public PartFolders(string path)
        {
            NoUpdate = false;
            NoUpdateSpecified = false;
            Path = path;
        }
    }

    [XmlRoot("Settings", IsNullable = false)]
    public sealed class OptionsFileSchema

    {
        // Current list options
        [XmlArrayItem("Custom", IsNullable = false)]
        public PartFolders[] Folders;
        [XmlArrayItem("Part", IsNullable = false)]
        public string[] Ignore;

        [XmlArrayItem("Redirect", IsNullable = false)]
        public Redirect[] Redirects;

        // Current string options
        public string KeyFile;
        public string BasePath;
        public string CharacterBuilderPath;

        // Current boolean options
        public bool WriteKeyFile;
        public bool VerboseMode;
        public bool AlwaysRemerge;
        public bool LaunchBuilder;
        public bool CheckForUpdates;
        public bool UpdateFirst;
        public bool SetFileAssociations;
        public bool DumpTemporaryFiles;
        public bool CreateUpdateIndexFiles;

        [XmlIgnore] public bool WriteKeyFileSpecified;
        [XmlIgnore] public bool VerboseModeSpecified;
        [XmlIgnore] public bool AlwaysRemergeSpecified;
        [XmlIgnore] public bool LaunchBuilderSpecified;
        [XmlIgnore] public bool CheckForUpdatesSpecified;
        [XmlIgnore] public bool UpdateFirstSpecified;
        [XmlIgnore] public bool SetFileAssociationsSpecified;
        [XmlIgnore] public bool DumpTemporaryFilesSpecified;
        [XmlIgnore] public bool CreateUpdateIndexFilesSpecified;

        // Deprecated options
        public bool FastMode;
        public bool NewMergeLogic;
        public bool ShowChangelog;

        [XmlIgnore] public bool FastModeSpecified;
        [XmlIgnore] public bool NewMergeLogicSpecified;
        [XmlIgnore] public bool ShowChangelogSpecified;
    }

    internal sealed class LoaderOptions
    {
        private static readonly XmlSerializer SERIALIER = new XmlSerializer(typeof(OptionsFileSchema));

        public List<string> MergeDirectories = new List<string>();
        public List<string> UpdateDirectories = new List<string>();
        public List<Redirect> Redirects = new List<Redirect>();

        public List<string> IgnoreParts = new List<string>();
        public List<string> ExecArgs = new List<string>();

        private readonly List<Regex> ignoreGlobs = new List<Regex>();

        public bool HasWarnings = false;

        public string KeyFile = null;
        public string LoadedConfigPath = null;
        public string CBPath = null;
        public string CachePath = null;

        public bool WriteKeyFile = false;
        public bool VerboseMode = false;
        public bool ForceUpdate = false;
        public bool ForceRemerge = false;
        public bool LaunchBuilder = true;
        public bool CheckForUpdates = true;
        public bool UpdateFirst = false;
        public bool SetFileAssociations = false;
        public bool DumpTemporaryFiles = false;
        public bool CreateUpdateIndexFiles = false;

        public void AddPath(string dir, bool update = true)
        {
            if (!Directory.Exists(dir))
                return;
            MergeDirectories.Add(dir);
            if (update) UpdateDirectories.Add(dir);
            foreach (var subdir in Directory.GetDirectories(dir))
                AddPath(subdir, update);
        }

        private void DeprecatedCheck(string filename, bool exists, string tag, string extra = null)
        {
            if (!exists) return;
            if (extra != null) extra = $"\n{extra}";
            else extra = "";
            Log.Warn($"Configuration file {filename} has option <{tag}>. This option is no longer supported.{extra}");
            HasWarnings = true;
        }
        private string ProcessPath(string configRoot, string relative)
        {
            string expanded = Environment.ExpandEnvironmentVariables(relative.Trim());
            if (Path.IsPathRooted(expanded))
                return expanded;
            return Path.Combine(configRoot, expanded);
        }
        private void AddOptionFile(string filename, string configRoot, OptionsFileSchema data)
        {
            if (LoadedConfigPath != null)
                throw new Exception("Internal error: Attempted to load two configuration files.");

            if (data.Folders != null)
                foreach (var folder in data.Folders)
                    AddPath(ProcessPath(configRoot, folder.Path),
                            !folder.NoUpdateSpecified || !folder.NoUpdate);
            if (data.Ignore != null) IgnoreParts.AddRange(data.Ignore.Select(x => x.Trim()));
            if (data.Redirects != null) Redirects.AddRange(data.Redirects.Select(r => new Redirect { From = r.From, To = r.To, Confirmed = true }));

            if (data.KeyFile != null) KeyFile = ProcessPath(configRoot, data.KeyFile);
            if (data.BasePath != null) CachePath = ProcessPath(configRoot, data.BasePath);
            if (data.CharacterBuilderPath != null) CBPath = ProcessPath(configRoot, data.CharacterBuilderPath);

            if (CachePath == null) CachePath = configRoot;

            if (data.WriteKeyFileSpecified) WriteKeyFile = data.WriteKeyFile;
            if (data.VerboseModeSpecified) VerboseMode = data.VerboseMode;
            if (data.AlwaysRemergeSpecified) ForceRemerge = data.AlwaysRemerge;
            if (data.LaunchBuilderSpecified) LaunchBuilder = data.LaunchBuilder;
            if (data.CheckForUpdatesSpecified) CheckForUpdates = data.CheckForUpdates;
            if (data.UpdateFirstSpecified) UpdateFirst = data.UpdateFirst;
            if (data.SetFileAssociationsSpecified) SetFileAssociations = data.SetFileAssociations;
            if (data.DumpTemporaryFilesSpecified) DumpTemporaryFiles = data.DumpTemporaryFiles;
            if (data.CreateUpdateIndexFilesSpecified) CreateUpdateIndexFiles = data.CreateUpdateIndexFiles;

            LoadedConfigPath = filename;
            
            DeprecatedCheck(filename, data.FastModeSpecified, "FastMode",
                            "The new merge logic should be fast enough to not require it.");
            DeprecatedCheck(filename, data.NewMergeLogicSpecified, "NewMergeLogic",
                            "A faster merge algorithm is always used now.");
            DeprecatedCheck(filename, data.ShowChangelog, "ShowChangelog",
                            "Changelogs are always shown on the Character Builder title page.");
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

        private bool InvalidOptions(string result)
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
                ignoreGlobs.Add(new Regex($"^{pattern}$", RegexOptions.IgnoreCase));
            }
        }

        public bool IsPartIgnored(string partName) =>
            ignoreGlobs.Any(x => x.IsMatch(partName));
    }

    /// <summary>
    /// Exceptions of this type are printed directly to console with no stack trace or message type.
    /// </summary>
    public sealed class CBLoaderException : Exception
    {
        public CBLoaderException(string message) : base(message) { }
    }

    internal static class Program
    {
        public static string Version;
        static Program()
        {
            var ver = typeof(Program).Assembly.GetName().Version;
            Version = $"{ver.Major}.{ver.Minor}.{ver.Build}";
#if DEBUG
            Version += " Beta";
#endif

        }
        
        private static void SetUniqueString(ref string target, string flag, string value)
        {
            if (target != null)
                throw new OptionException($"Cannot specify more than one {flag} flag.", flag);
            target = value;
        }
        private static LoaderOptions ProcessOptions(string[] args)
        {
            var options = new LoaderOptions();

            var showHelp = false;
            var noConfig = false;

            string configFile = null;
            string keyFile = null;
            string cbPath = null;
            string cachePath = null;

            bool? writeKeyFile = null;
            bool? verboseMode = null;
            bool? forceUpdate = null;
            bool? forceRemerge = null;
            bool? launchBuilder = null;
            bool? checkForUpdates = null;
            bool? updateFirst = null;
            bool? setFileAssociations = null;
            bool? dumpTemporaryFiles = null;
            bool? createUpdateIndexFiles = null;

            var opts = new OptionSet() {
                "Usage: CBLoader [-c <config file>]",
                "A homebrew rules loader for the D&D Insider Character Builder",
                "",
                { "h|?|help", "Shows this help message.",
                    value => showHelp = true },
                { "v|verbose", "Enables additional debugging output.",
                    value => verboseMode = true },
                { "c|config=", "A config file to use rather than the default.",
                    value => SetUniqueString(ref configFile, "-c", value) },
                { "no-config", "Do not use a configuration file.",
                    value => noConfig = true },
                { "a|set-assocations", "Associate .dnd4e and .cbconfig with CBLoader.",
                    value => setFileAssociations = false },
                "",
                "Path management:",
                { "u|cache-path=", "Sets where to write temporary files.",
                    value => SetUniqueString(ref cachePath, "-u", value) },
                { "cb-path=", "Sets where character builder is installed.",
                    value => SetUniqueString(ref cbPath, "--cb-path", value) }, 
                { "f|folder=", "Adds a directory to search for custom rules in.",
                    value => options.AddPath(value) },
                { "ignore-part=", "Adds a part file to ignore.",
                    value => options.IgnoreParts.Add(value) },
                "",
                "Keyfile options:",
                { "k|key-file=", "Uses the given keyfile.",
                    value => SetUniqueString(ref keyFile, "-k", value) },
                { "r=", "Updates a keyfile at the given path. Implies -k.",
                    value => {
                        SetUniqueString(ref keyFile, "-r", value);
                        writeKeyFile = true;
                    } },
                "",
                "Execution options:",
                { "update-first", "Update before merging, rather than after.",
                    value => updateFirst = true },
                { "force-update", "Redownload parts files.",
                    value => forceUpdate = true },
                { "e|force-remerge", "Always regenerate merged rules file.",
                    value => forceRemerge = true },
                { "d|no-update", "Do not check for updates.",
                    value => checkForUpdates = false },
                "",
                "Development options:",
                { "n|no-run", "Do not actually launch the character builder.",
                    value => launchBuilder = false },
                { "dump-temporary", "Dumps raw rules data to disk.",
                    value => dumpTemporaryFiles = true },
                { "create-update-indexes", "Create update version indexes.",
                    value => createUpdateIndexFiles = true },
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
            if (keyFile != null) options.KeyFile = keyFile;
            if (cbPath != null) options.CBPath = cbPath;
            if (cachePath != null) options.CachePath = cachePath;

            if (writeKeyFile != null) options.WriteKeyFile = (bool) writeKeyFile;
            if (verboseMode != null) options.VerboseMode = (bool) verboseMode;
            if (forceUpdate != null) options.ForceUpdate = (bool) forceUpdate;
            if (forceRemerge != null) options.ForceRemerge = (bool) forceRemerge;
            if (launchBuilder != null) options.LaunchBuilder = (bool) launchBuilder;
            if (checkForUpdates != null) options.CheckForUpdates = (bool) checkForUpdates; 
            if (updateFirst != null) options.UpdateFirst = (bool) updateFirst;
            if (setFileAssociations != null) options.SetFileAssociations = (bool) setFileAssociations;
            if (dumpTemporaryFiles != null) options.DumpTemporaryFiles = (bool) dumpTemporaryFiles;
            if (createUpdateIndexFiles != null) options.CreateUpdateIndexFiles = (bool) createUpdateIndexFiles;

            if (options.CBPath == null && (options.CBPath = Utils.GetInstallPath()) == null)
                throw new CBLoaderException(
                    "CBLoader could not find an installation of Character Builder.\n" +
                    "Please specify its path with <CBPath>path/to/builder</CBPath> in the configuration " +
                    "or reinstall Character Builder.");

            // Default cache Path
            if (options.CachePath == null) options.CachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache");

            // Default keyfile location.
            if (options.KeyFile == null) options.KeyFile = Path.Combine(options.CachePath, "cbloader.keyfile");

            // Default part directory.
            if (options.MergeDirectories.Count == 0 && options.UpdateDirectories.Count == 0)
            {
                options.AddPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Custom"));
            }
            options.AddPath(Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ddi"), "CBLoader"));

            // Update first anyway if LaunchBuilder isn't set -- we don't need this optimization.
            if (!options.LaunchBuilder) options.UpdateFirst = true;

            // Check configuration option consistancy.
            options.FinalOptionsCheck();
            return options;
        }

        private static void main(string[] args)
        {
            var options = ProcessOptions(args);
            if (options == null) return;

            if (options.VerboseMode) Log.VerboseMode = true;

            var cryptoInfo = new CryptoInfo(options);
            if (cryptoInfo.expectedDemoHash != ParsedD20RulesEngine.DemoHashOct2010)
            {
                Solve2009Version(options, ref cryptoInfo);
            }

            var fileManager = new PartManager(options, cryptoInfo);

            Thread programThread = null;

            if (options.SetFileAssociations)
                Utils.UpdateRegistry();
            var uc = new UpdateChecker(null, options.Redirects);
            fileManager.DoUpdates(options.ForceUpdate, false, true, uc);
            if (options.CheckForUpdates && options.UpdateFirst)
                fileManager.DoUpdates(options.ForceUpdate, false, false, uc);
            fileManager.MergeFiles();
            if (options.CreateUpdateIndexFiles)
                fileManager.GenerateUpdateIndexes();
            if (options.LaunchBuilder)
                programThread =  ProcessLauncher.StartProcess(options, options.ExecArgs.ToArray(), 
                                                              fileManager.MergedPath, fileManager.ChangelogPath);
            if (options.CheckForUpdates && !options.UpdateFirst)
                fileManager.DoUpdates(options.ForceUpdate, true, false, uc);
            if (!options.CheckForUpdates)
                Log.Warn("Updates are currently disabled");
            if (programThread != null)
                programThread.Join();
        }

        [LoaderOptimization(LoaderOptimization.MultiDomain)]
        internal static void Main(string[] args)
        {
            Utils.ConfigureTLS12();
            Console.WriteLine($"CBLoader version {Version}");
            Utils.CheckForUpdates(typeof(Program).Assembly.GetName().Version);
            Console.WriteLine();
            Log.InitLogging();
            Log.Trace($"CBLoader version {Version}");
            Log.Trace();

            try
            {
                Environment.SetEnvironmentVariable("CBLOADER", AppDomain.CurrentDomain.BaseDirectory);
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
#if !DEBUG
            catch (Exception e)
            {
                Log.Error(null, e);
            }
#endif
            if (Log.ErrorLogged)
            {
                if (ConsoleWindow.IsInIndependentConsole)
                    Console.Write("Error encountered. Would you like to open the log file? (y/n) ");

                if (!ConsoleWindow.IsInIndependentConsole || Console.ReadKey().Key == ConsoleKey.Y)
                {
                    using (var p = new Process())
                    {
                        p.StartInfo.UseShellExecute = true;
                        p.StartInfo.FileName = Log.LogFileLocation;
                        p.Start();
                    }
                }
            }
        }

        private static bool Solve2009Version(LoaderOptions options, ref CryptoInfo cryptoInfo)
        {
            Log.Trace("Trying to solve outdated version.");
            var destDirName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Character Builder");
            if (Directory.Exists(destDirName))
            {

                options.CBPath = destDirName;
                cryptoInfo = new CryptoInfo(options);
                Log.Trace($"Using pre-patched {destDirName}");
                return true;
            }

            var defaultPath = @"C:\Program Files\Wizards of the Coast\Character Builder".Replace('\\', Path.DirectorySeparatorChar);
            Log.Trace($"  - Looking in {defaultPath}");
            if (Directory.Exists(defaultPath))
            {
                Log.Trace($"  - Applying patch");
                var path = Patch(options.CBPath, defaultPath);
                if (!string.IsNullOrEmpty(path))
                {
                    Log.Trace($"  - Patch works. Moving to {destDirName}");
                    Utils.CopyAll(path, destDirName);
                    new DirectoryInfo(path).Delete(true);
                    options.CBPath = destDirName;
                    cryptoInfo = new CryptoInfo(options);
                    Log.Trace($"Patch successful");
                    var baseconfigpath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "default.cbconfig");
                    if (File.Exists(baseconfigpath)) // Point the config file at this for future launches
                    {
                        var doc = System.Xml.Linq.XDocument.Load(baseconfigpath);
                        doc.Root.Add(new System.Xml.Linq.XElement("CharacterBuilderPath", options.CBPath));
                        doc.Save(baseconfigpath);
                    }
                    Log.Warn("CBLoader has manually reapplied a failed April2010 patch install.");
                    return true;
                }
            }

            return false;
        }

        private static string Patch(string cBPath, string updatePath)
        {
            var tmp = Path.Combine(Path.GetTempPath(), nameof(CBLoader));
            Directory.CreateDirectory(tmp);

            Utils.CopyAll(cBPath, tmp, true);
            Utils.CopyAll(updatePath, tmp, true);
            var probe = new CryptoInfo(new LoaderOptions { CBPath = tmp });
            if (probe.expectedDemoHash == ParsedD20RulesEngine.DemoHashOct2010)
                return tmp;
            return null;
        }
    }
}
