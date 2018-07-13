using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net;

namespace CBLoader
{
    [XmlType(Namespace = "http://cbloader.github.io/CBLoader/ns/MergeInfo/v1")]
    [XmlRoot(Namespace = "http://cbloader.github.io/CBLoader/ns/MergeInfo/v1")]
    public sealed class MergedFileInfo
    {
        [XmlAttribute] public string Filename;
        [XmlAttribute] public DateTime LastModified;

        public MergedFileInfo() { }
        public MergedFileInfo(string filename, DateTime lastModified)
        {
            Filename = filename;
            LastModified = lastModified;
        }

        public override bool Equals(object obj)
        {
            var info = obj as MergedFileInfo;
            return info != null && Filename == info.Filename && LastModified == info.LastModified;
        }
        public override int GetHashCode() => 0; // We won't use this.
    }

    [XmlType(Namespace = "http://cbloader.github.io/CBLoader/ns/MergeInfo/v1")]
    [XmlRoot(Namespace = "http://cbloader.github.io/CBLoader/ns/MergeInfo/v1")]
    public sealed class MergeInfo
    {
        [XmlElement(IsNullable = false)]
        public KeyStore EncryptionData;

        [XmlArray(IsNullable = false)]
        [XmlArrayItem(IsNullable = false)]
        public List<MergedFileInfo> PartFiles;

        [NonSerialized]
        private HashSet<string> addedParts;

        public bool SameMergeInfo(MergeInfo info)
        {
            return EncryptionData == info.EncryptionData && PartFiles.SequenceEqual(info.PartFiles);
        }
        public void AddFile(FileInfo info)
        {
            if (addedParts == null) {
                if (PartFiles != null)
                    throw new Exception("Do not call AddFiles on a MergeInfo acquired via serialization.");
                PartFiles = new List<MergedFileInfo>();
                addedParts = new HashSet<string>();
            }

            if (!info.Exists) throw new Exception($"{info.FullName} does not exist!");
            
            if (addedParts.Contains(info.FullName)) return;
            addedParts.Add(info.FullName);
            PartFiles.Add(new MergedFileInfo(info.FullName, info.LastWriteTime));
        }
        public void AddFile(string filename) => AddFile(new FileInfo(filename));
        public void DoMerge(Action<string> callback)
        {
            foreach (var file in PartFiles) callback.Invoke(file.Filename);
        }
    }

    internal sealed class PartStatus
    {
        public bool wasMerged = false;
        public bool wasAdded = false;
        public bool wasObsoleted = false;
        public bool wasUpdated = false;

        public string FullName;
        public string Name;

        public string FirstFoundVersion;
        public string Version;
        public string Changelog;

        public PartStatus(string filename)
        {
            FullName = filename;
            Name = Path.GetFileName(filename);
        }
    }

    /// <summary>
    /// Manages interactions with the files on the disc
    /// </summary>
    internal sealed class PartManager
    {
        private static XmlSerializer SERIALIZER = new XmlSerializer(typeof(MergeInfo));

        private readonly LoaderOptions options;
        private readonly CryptoInfo cryptoInfo;

        private List<string> mergeOrder = new List<string>();
        private Dictionary<string, PartStatus> partStatus = new Dictionary<string, PartStatus>();

        public string EncryptedPath { get => Path.Combine(options.CBPath, "combined.dnd40.encrypted"); }
        public string MergedPath { get => Path.Combine(options.CachePath, "combined.dnd40.encrypted"); }
        public string MergedStatePath { get => Path.Combine(options.CachePath, "merge_state.xml"); }
        
        public PartManager(LoaderOptions options, CryptoInfo cryptoInfo)
        {
            this.options = options;
            this.cryptoInfo = cryptoInfo;

            if (!Directory.Exists(options.CachePath))
                Directory.CreateDirectory(options.CachePath);
        }

        private static FileInfo[] getFromDirectory(string fn, string glob) =>
            Directory.Exists(fn) ? new DirectoryInfo(fn).GetFiles(glob) : new FileInfo[0];
        private static FileInfo[] collectFromDirectories(IEnumerable<string> directories, params string[] glob) =>
            directories.SelectMany(dir => glob.SelectMany(x => getFromDirectory(dir, x))).OrderBy(x => x.Name).ToArray();

        private PartStatus initPartStatus(string filename, XDocument sources)
        {
            filename = Path.GetFullPath(filename);

            if (!partStatus.ContainsKey(filename))
            {
                var status = new PartStatus(filename);
                var updateInfo = sources.Element("UpdateInfo");
                if (updateInfo != null) status.FirstFoundVersion = sources.Element("Version").Value;
                partStatus[filename] = status;
            }

            {
                var status = partStatus[filename];
                var updateInfo = sources.Element("UpdateInfo");
                status.Version = updateInfo != null ? updateInfo.Element("Version").Value : null;
                var changelog = sources.Element("Changelog");
                status.Changelog = changelog != null ? changelog.Value : null;
                return status;
            }
        }

        /// <summary>
        /// Merges the combined.dnd4e.encrypted and .part file(s) into the final combined output
        /// file. If any have changed, they will be remerged.
        /// <param name="forced">Whether to ignore the current merge state.</param>
        /// </summary>
        public void MergeFiles(bool forced)
        {
            Log.Info("Updating merged game data.");
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            Log.Debug(" - Collecting .part files");
            MergeInfo currentMergeInfo = new MergeInfo();
            currentMergeInfo.EncryptionData = cryptoInfo.keyStore;
            currentMergeInfo.AddFile(EncryptedPath);
            foreach (var part in collectFromDirectories(options.PartDirectories, "*.part"))
                currentMergeInfo.AddFile(part);

            Log.Debug(" - Checking merge data file");
            var doMerge = true;
            try
            {
                using (var sr = new StreamReader(File.Open(MergedStatePath, FileMode.Open), Encoding.UTF8))
                {
                    var mergeInfo = (MergeInfo) SERIALIZER.Deserialize(sr);
                    if (mergeInfo.SameMergeInfo(currentMergeInfo)) {
                        Log.Debug(" - Same files already merged.");
                        doMerge = false;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Debug("   - Error occurred reading merge data file.", e);
                try
                {
                    File.Delete(MergedStatePath);
                }
                catch (Exception e2)
                {
                    Log.Debug("   - Error occurred deleting merge data file.", e2);
                }
            }

            if (doMerge)
            {
                MergeFiles(currentMergeInfo);

                Log.Debug(" - Writing merge data file.");
                using (var sw = new StreamWriter(File.Open(MergedStatePath, FileMode.Create), Encoding.UTF8))
                    SERIALIZER.Serialize(sw, currentMergeInfo);
            }

            stopwatch.Stop();
            Log.Debug($"Finished in {stopwatch.ElapsedMilliseconds} ms");
            Log.Debug();
        }
        
        private void addToMerger(string filename, PartMerger merger, XDocument document)
        {
            initPartStatus(filename, document).wasMerged = true;
            merger.ProcessDocument(document);
        }
        private void MergeFiles(MergeInfo mergeInfo)
        {
            var merger = new PartMerger("D&D4E");
            // TODO: Check for an already patched combined.dnd40.encrypted
            mergeInfo.DoMerge(filename =>
            {
                Log.Info($" - Adding rules from {Path.GetFileName(filename)}");
                mergeOrder.Add(filename);
                switch (Path.GetExtension(filename).ToLower())
                {
                    case ".encrypted":
                        using (var stream = cryptoInfo.OpenEncryptedFile(filename))
                        {
                            var data = new StreamReader(stream, Encoding.UTF8).ReadToEnd();
                            if (CryptoUtils.IsXmlPatched(data))
                                Log.Warn("   - Warning: This file has already been patched by CBLoader!!");
                            addToMerger(filename, merger, XDocument.Load(new StringReader(data)));
                        }
                        break;
                    case ".part":
                        addToMerger(filename, merger, XDocument.Load(filename));
                        break;
                    default:
                        Log.Warn($" - Attempt to merge file with unknown extension: {filename}");
                        break;
                }
            });
            Log.Info(" - Saving rules data to disk");
            cryptoInfo.SaveRulesFile(merger.MakeDocument(), MergedPath);
        }

        private void checkMetadata(HashSet<string> obsoleteList, FileInfo fi, WebClient wc)
        {
            Log.Debug($" - Checking metadata for {fi.FullName}");

            {
                var customContent = XDocument.Load(fi.FullName);
                var metadata = customContent.Root.Element("UpdateInfo");
                if (metadata != null)
                    try
                    {
                        var address = metadata.Element("VersionAddress");
                        if (address != null)
                        {
                            var targetFilename = metadata.Element("Filename");
                            if (targetFilename != null && targetFilename.Value.ToLower() != fi.Name.ToLower())
                                Log.Warn($" - {fi.Name} uses the <Filename> element to rename, which is supported.");

                            var webVersion = wc.DownloadString(address.Value);
                            var localVersion = metadata.Element("Version").Value;
                            if (localVersion != webVersion)
                            {
                                Log.Info($" - Downloading update for {fi.Name} (v{webVersion})");
                                wc.DownloadFile(metadata.Element("PartAddress").Value, Path.Combine(fi.DirectoryName, fi.FullName));
                                initPartStatus(fi.FullName, XDocument.Load(fi.FullName)).wasUpdated = true;
                            }
                        }
                    }
                    catch (WebException e)
                    {
                        Log.Error($"Failed to download {fi.Name}", e);
                    }
            }

            {
                var customContent = XDocument.Load(fi.FullName);
                var obsolete = customContent.Root.Element("Obsolete");
                if (obsolete != null)
                {
                    if (!Utils.IsFilenameValid(obsolete.Value))
                    {
                        Log.Warn($" - {obsolete.Value} is not a valid filename in {fi.Name}! Skipping.");
                        return;
                    }

                    string path = Path.Combine(fi.DirectoryName, obsolete.Value);
                    obsoleteList.Add(Path.GetFullPath(path));
                }
            }
        }
        
        private void checkIndex(HashSet<string> obsoleteList, FileInfo fi, WebClient wc, bool forced)
        {
            Log.Debug($" - Checking index {fi.FullName}");

            checkMetadata(obsoleteList, fi, wc);

            var partIndex = XDocument.Load(fi.FullName);
            foreach (var part in partIndex.Root.Elements("Part"))
            {
                var partName = part.Element("Filename").Value;

                if (!Utils.IsFilenameValid(partName))
                {
                    Log.Warn($" - {partName} is not a valid filename in {fi.Name}! Skipping.");
                    continue;
                }

                try
                {
                    string outputFile = Path.Combine(fi.Directory.FullName, partName);
                    if (options.IsPartIgnored(partName)) continue;
                    if (!File.Exists(outputFile) || forced)
                    {
                        var partAddress = part.Element("PartAddress").Value;
                        Log.Info($" - Downloading {partName}");
                        var xmlString = Utils.StripBOM(Encoding.UTF8.GetString(wc.DownloadData(partAddress)));
                        var xmlObj = XDocument.Load(XmlReader.Create(new StringReader(xmlString)));

                        File.WriteAllText(outputFile, xmlString, Encoding.UTF8);
                        initPartStatus(outputFile, xmlObj).wasAdded = true;
                        
                        if (Path.GetExtension(outputFile).ToLower() == ".part")
                            checkIndex(obsoleteList, new FileInfo(outputFile), wc, forced);
                    }
                }
                catch (WebException e)
                {
                    Log.Error($"Failed to download {partName}", e);
                }
            }
            foreach (var obsolete in partIndex.Root.Elements("Obsolete"))
            {
                foreach (var filename in obsolete.Elements("Filename"))
                {
                    if (!Utils.IsFilenameValid(filename.Value))
                    {
                        Log.Warn($" - {filename.Value} is not a valid filename in {fi.Name}! Skipping.");
                        continue;
                    }

                    string path = Path.Combine(fi.Directory.FullName, filename.Value);
                    obsoleteList.Add(Path.GetFullPath(path));
                }
            }
        }

        private void deleteObsolete(string filename)
        {
            if (!File.Exists(filename)) return;

            Log.Info($" - Deleting obsoleted file {filename}");
            initPartStatus(filename, XDocument.Load(filename)).wasObsoleted = true;
            File.Delete(filename);
        }
        
        public void DoUpdates(bool forced, bool background)
        {
            try
            {
                Log.Info($"Updating rules files.{(background ? " (In the background)" : "")}");
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                if (!NetworkInterface.GetIsNetworkAvailable())
                {
                    Log.Info(" - ERROR: Your computer is not connected to the internet.");
                    return;
                }

                var wc = new WebClient();
                var customFiles = collectFromDirectories(options.PartDirectories, "*.part");
                var indexes = collectFromDirectories(options.PartDirectories, "*.index");

                var obsoleteList = new HashSet<string>();
                foreach (var fi in indexes)
                    checkIndex(obsoleteList, fi, wc, forced);
                foreach (var fi in customFiles)
                    checkMetadata(obsoleteList, fi, wc);
                foreach (var obsolete in obsoleteList)
                    deleteObsolete(obsolete);

                stopwatch.Stop();
                Log.Debug($"Finished in {stopwatch.ElapsedMilliseconds} ms");
                Log.Debug();
            }
            catch (Exception e)
            {
                Log.Error("Failed to update rules files.", e);
            }
        }
    }
}
