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
    public class LastMergedFileInfo
    {
        public string FileName { get; set; }
        public DateTime LastTouched { get; set; }
    }

    /// <summary>
    /// Manages interactions with the files on the disc
    /// </summary>
    internal class FileManager
    {
        public const string ENCRYPTED_FILENAME = "combined.dnd40.encrypted";
        private const string GENERAL_EXTRACT_ERROR = 
            "Unknown error extracting combined.dnd40.encrypted. " +
            "Please confirm that the .encrypted file exists, that you have enough disk space and you have appropriate permissions.";
        private static XmlSerializer SERIALIZER = new XmlSerializer(typeof(List<LastMergedFileInfo>));

        private readonly LoaderOptions options;
        private readonly CryptoInfo cryptoInfo;

        private List<LastMergedFileInfo> currentlyMerged = new List<LastMergedFileInfo>();
        
        public string MergedPath { get => Path.Combine(options.CachePath, "combined.dnd40.encrypted"); }
        public string MergedFileInfo { get => Path.Combine(options.CachePath, "cbloader.merged"); }
        public string CoreFileName { get => Path.Combine(options.CachePath, "combined.dnd40.main"); }
        
        public FileManager(LoaderOptions options, CryptoInfo cryptoInfo)
        {
            this.options = options;
            this.cryptoInfo = cryptoInfo;

            if (!Directory.Exists(options.CachePath))
                Directory.CreateDirectory(options.CachePath);

            if (File.Exists(MergedFileInfo))
                using (StreamReader sr = new StreamReader(MergedFileInfo, Encoding.Default))
                    currentlyMerged = (List<LastMergedFileInfo>) SERIALIZER.Deserialize(sr);
        }
        
        /// <summary>
        /// If necessary extracts the unencrypted data from the zip file. And merges the .main and .part file(s) into the
        /// final file name. 
        /// <param name="forced">Indicates whether the extract and merge should hapen regardless of the state of the filesystem.
        /// If this is false, the encrypted file will only be extracted if it is updated and the .main and .part files will only
        /// be merged if one has been touched. If forced is true, the files will be re-extracted and remerged regardless</param>
        /// </summary>
        public void ExtractAndMerge(bool forced)
        {
            Log.Info("Updating merged game data.");
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            ExtractFile(forced);
            MergeFiles(forced);

            stopwatch.Stop();
            Log.Debug($"Finished in {stopwatch.ElapsedMilliseconds} ms");
            Log.Debug();
        }

        /// <summary>
        /// Check indexes for new files.
        /// </summary>
        /// <param name="forced"></param>
        public bool CheckIndexes(bool forced)
        {
            List<FileInfo> Indexes = options.PartDirectories.SelectMany(
                GetIndexesFromDirectory).OrderBy(f => f.Name).ToList();
            bool NewFiles = false;

            System.Net.WebClient wc = new System.Net.WebClient();
            foreach (FileInfo index in Indexes)
            {
                NewFiles = CheckIndex(forced, NewFiles, wc, index);
            }
            return NewFiles;
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

        /// <summary>
        /// Merges the .main file with all known .part files
        /// <param name="forced">if true, the .main and .part files will always be merged. Otherwise they are only merged if
        /// one has been touched.</param>
        /// </summary>
        private void MergeFiles(bool forced)
        {
            if (!File.Exists(CoreFileName))
                throw new Exception("Error, could not find file: " + CoreFileName);

            List<FileInfo> customFiles = options.PartDirectories.SelectMany(
                GetPartsFromDirectory).OrderBy(f => f.Name).ToList();
            customFiles.RemoveAll(f => options.IsPartIgnored(f.Name.ToLower().Trim()));

            // bail out if nothing is modified
            if (!forced && File.Exists(MergedPath) && currentlyMerged.Count > 0)
            {
                if (customFiles.TrueForAll(FileWasMerged) && currentlyMerged.TrueForAll(MergedFileExists(customFiles)))
                    return;
            }

            // construct the custom rules file
            MergeFiles(customFiles);
        }

        /// <summary>
        /// Merges the specified files
        /// </summary>
        private void MergeFiles(List<FileInfo> customFiles)
        {
            var merger = new PartMerger("D&D4E");
            Log.Info($" - Adding rules from core file");
            merger.ProcessDocument(CoreFileName);
            foreach (var file in customFiles)
            {
                Log.Info($" - Adding rules from {file}");
                merger.ProcessDocument(file.FullName);
                updateMergedList(file.FullName, file.LastWriteTime);
            }
            Log.Info($" - Saving rules data to disk");
            cryptoInfo.SaveRulesFile(merger.MakeDocument(), MergedPath);
            Log.Trace($" - Updating merged list.");
            using (StreamWriter sw = new StreamWriter(MergedFileInfo, false))
                SERIALIZER.Serialize(sw, currentlyMerged);
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

        private void updateMergedList(String fileName, DateTime lasttouched)
        {
            int index = currentlyMerged.FindIndex(lmf => lmf.FileName == fileName);
            LastMergedFileInfo lmfi = new LastMergedFileInfo()
                {
                    FileName = fileName,
                    LastTouched = lasttouched,
                };
            if (index == -1)
                currentlyMerged.Add(lmfi);
            else
                currentlyMerged[index] = lmfi;
        }
        
        /// <summary>
        /// Writing this out via an xmlwriter, XDocument.Save overrites important line-ending information
        /// </summary>
        /// <param name="xw"></param>
        /// <param name="parent"></param>
        private void SaveDocument(XmlWriter xw, XElement parent)
        {
            xw.WriteStartElement(parent.Name.LocalName);
            foreach (XAttribute xa in parent.Attributes())
                xw.WriteAttributeString(xa.Name.LocalName, xa.Value);
            foreach (XNode xe in parent.Nodes())
            {
                if (xe.NodeType == System.Xml.XmlNodeType.Text)
                {
                    XText xt = (XText)xe;
                    if (!String.IsNullOrEmpty(xt.Value))
                        xw.WriteString(xt.Value.Replace('\n', '\r'));
                }
                else if (xe.NodeType == System.Xml.XmlNodeType.Element)
                {
                    XElement el = (XElement)xe;
                    SaveDocument(xw, el);
                }
            }
            xw.WriteEndElement();
        }

        private static FileInfo[] GetPartsFromDirectory(string fn)
        {
            if (Directory.Exists(fn))
                return new DirectoryInfo(fn).GetFiles("*.part");
            else
                return new FileInfo[0];
        }

        private static FileInfo[] GetIndexesFromDirectory(string fn)
        {
            if (Directory.Exists(fn))
                return new DirectoryInfo(fn).GetFiles("*.index");
            else
                return new FileInfo[0];
        }

        private static Predicate<LastMergedFileInfo> MergedFileExists(List<FileInfo> customFiles)
        {
            return lmf => customFiles.Any(fi => fi.FullName.ToLower() == lmf.FileName.ToLower());
        }

        private bool FileWasMerged(FileInfo fi)
        {
            LastMergedFileInfo lmf = currentlyMerged.FirstOrDefault(lmfi => lmfi.FileName.ToLower() == fi.FullName.ToLower());
            if (lmf != null)
                return lmf.LastTouched.Equals(fi.LastWriteTime) || lmf.LastTouched == DateTime.MinValue;
            else return false;
        }

        /// <summary>
        /// Extracts the .encrypted file into a .main file and creates a .part file if necessary
        /// <param name="forced">If true, the .encrypted file will always be extracted. Otherwise it is only extracted
        /// if it is updated.</param>
        /// </summary>
        private void ExtractFile(bool forced)
        {
            var rulesFile = Path.Combine(options.CBPath, ENCRYPTED_FILENAME);
            if (forced || !File.Exists(CoreFileName) || File.GetLastWriteTime(rulesFile) > File.GetLastWriteTime(CoreFileName))
            {
                Log.Info(" - Extracting " + CoreFileName);
                try
                {
                    using (StreamReader sr = new StreamReader(cryptoInfo.OpenEncryptedFile(rulesFile)))
                    using (StreamWriter sw = new StreamWriter(CoreFileName))
                    {
                        string xmlData = sr.ReadToEnd();
                        sw.Write(xmlData);
                    }
                }
                catch (Exception e)
                {
                    throw new Exception(GENERAL_EXTRACT_ERROR, e);
                }
            }
        }
        
        public bool DoUpdates(bool forced)
        {
            List<FileInfo> customFiles = options.PartDirectories.SelectMany(
                GetPartsFromDirectory).OrderBy(f => f.Name).ToList();
            bool newUpdates = false;
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
