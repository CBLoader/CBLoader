using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using System.Diagnostics;

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

    /// <summary>
    /// Manages interactions with the files on the disc
    /// </summary>
    internal sealed class FileManager
    {
        private const string GENERAL_EXTRACT_ERROR = 
            "Unknown error extracting combined.dnd40.encrypted. " +
            "Please confirm that the .encrypted file exists, that you have enough disk space and you have appropriate permissions.";
        private static XmlSerializer SERIALIZER = new XmlSerializer(typeof(MergeInfo));

        private readonly LoaderOptions options;
        private readonly CryptoInfo cryptoInfo;

        public string EncryptedPath { get => Path.Combine(options.CBPath, "combined.dnd40.encrypted"); }
        public string MergedPath { get => Path.Combine(options.CachePath, "combined.dnd40.encrypted"); }
        public string MergedStatePath { get => Path.Combine(options.CachePath, "merge_state.xml"); }
        
        public FileManager(LoaderOptions options, CryptoInfo cryptoInfo)
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
            foreach (var part in collectFromDirectories(options.PartDirectories, "*.part", "*.encrypted"))
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

        /// <summary>
        /// Merges the specified files
        /// </summary>
        private void MergeFiles(MergeInfo mergeInfo)
        {
            var merger = new PartMerger("D&D4E");
            // TODO: Check for an already patched combined.dnd40.encrypted
            mergeInfo.DoMerge(filename =>
            {
                Log.Info($" - Adding rules from {Path.GetFileName(filename)}");
                switch (Path.GetExtension(filename).ToLower())
                {
                    case ".encrypted":
                        using (var stream = cryptoInfo.OpenEncryptedFile(filename))
                            merger.ProcessDocument(stream);
                        break;
                    case ".part":
                        merger.ProcessDocument(filename);
                        break;
                    default:
                        Log.Warn($" - Attempt to merge file with unknown extension: {filename}");
                        break;
                }
            });
            Log.Info(" - Saving rules data to disk");
            cryptoInfo.SaveRulesFile(merger.MakeDocument(), MergedPath);
        }

        private bool CheckMetaData(FileInfo fi, XDocument customContent)
        {
            XElement metadata;
            if ((metadata = customContent.Root.Element("UpdateInfo")) != null)
            {
                try
                {
                    System.Net.WebClient wc = new System.Net.WebClient();
                    string webVersion = wc.DownloadString(metadata.Element("VersionAddress").Value);
                    string localVersion = metadata.Element("Version").Value;
                    if (localVersion != webVersion)
                    {
                        Log.Info("Found update for " + fi.Name + " (Version " + webVersion + "). Downloading.");
                        wc.DownloadFile(metadata.Element("PartAddress").Value, Path.Combine(fi.DirectoryName, metadata.Element("Filename").Value));
                        return true;
                    }
                }
                catch (System.Net.WebException v)
                {
                    if (v.ToString().Contains("could not be resolved")) // No Internet
                        Log.Debug("No internet access");
                    else if (v.ToString().Contains("is denied"))
                        Log.Error("CBLoader could not save the updates to disk.\n\tCheck the part file is not read-only, \n\tand that CBLoader is installed to somewhere you have write permissions.", v);
                    else
                        Log.Error("Failed getting update for " + fi.Name, v);
                }
            }
            if ((metadata = customContent.Root.Element("Obsolete")) != null)
            {
                string path;
                if (metadata.Value != "" && File.Exists(path = Path.Combine(fi.DirectoryName, metadata.Value))) // File was renamed?
                    File.Delete(path);
                else if (metadata.Value == "")
                {
                    fi.Delete();
                    //return true;  // Actually, don't.
                }
            }
            return false;
        }

        /// <summary>
        /// Check indexes for new files.
        /// </summary>
        /// <param name="forced"></param>
        public bool CheckIndexes(bool forced)
        {
            var indexes = collectFromDirectories(options.PartDirectories, "*.index");
            var newFiles = false;

            System.Net.WebClient wc = new System.Net.WebClient();
            foreach (FileInfo index in indexes)
            {
                newFiles = CheckIndex(forced, newFiles, wc, index);
            }
            return newFiles;
        }

        /// <summary>
        /// Check an individual index for updates. Refactored out of CheckIndexes for the ability to recurse.
        /// </summary>
        private bool CheckIndex(bool forced, bool NewFiles, System.Net.WebClient wc, FileInfo index)
        {
            XDocument PartIndex = XDocument.Load(index.FullName);
            CheckMetaData(index, PartIndex);
            foreach (XElement Part in PartIndex.Root.Elements("Part"))
            {
                try
                {
                    string filename = Path.Combine(index.Directory.FullName, Part.Element("Filename").Value);
                    if (options.IsPartIgnored(Part.Element("Filename").Value.ToLower().Trim()))
                        continue;
                    if (!File.Exists(filename) || forced)
                    {
                        Log.Info("Downloading " + Part.Element("Filename").Value + " from " + index.Name);
                        wc.DownloadFile(Part.Element("PartAddress").Value, filename);
                        if (Path.GetExtension(filename) == ".index")
                            CheckIndex(forced, NewFiles, wc, new FileInfo(filename));
                        else
                            CheckMetaData(new FileInfo(filename), XDocument.Load(filename));
                        NewFiles = true;
                    }
                }
                catch (System.Net.WebException v)
                {
                    if (v.ToString().Contains("is denied"))
                        Log.Error("CBLoader could not save the updates to disk.\n\tCheck the index file is somewhere you have write permissions.\n\tWe recommend the My Documents\\ddi\\CBLoader folder", v);
                }
            }
            foreach (XElement Part in PartIndex.Root.Elements("Obsolete"))
            {
                string filename = Path.Combine(index.Directory.FullName, Part.Element("Filename").Value);
                if (File.Exists(filename))
                    File.Delete(filename);
            }
            return NewFiles;
        }

        public bool DoUpdates(bool forced)
        {
            var customFiles = collectFromDirectories(options.PartDirectories, "*.part");
            var newUpdates = false;
            foreach (FileInfo fi in customFiles)
            {
                XDocument customContent = (XDocument)XDocument.Load(fi.FullName, LoadOptions.PreserveWhitespace);
               newUpdates = CheckMetaData(fi, customContent) || newUpdates;
            }
            newUpdates = CheckIndexes(forced) || newUpdates;
            if (newUpdates)
                new UpdateLog().CreateAndShow(customFiles);
            return newUpdates;
        }
    }
}
