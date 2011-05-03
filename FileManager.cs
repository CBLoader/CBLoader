using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml.Linq;
using ApplicationUpdate.Client;
using System.Xml.Serialization;
using System.Xml;
using System.Security.Cryptography;

namespace CharacterBuilderLoader
{
    /// <summary>
    /// Manages interactions with the files on the disc
    /// </summary>
    public class FileManager
    {
        public const string APPLICATION_ID = "2a1ddbc4-4503-4392-9548-d0010d1ba9b1";
        public const string ENCRYPTED_FILENAME = "combined.dnd40.encrypted";
        private const string GENERAL_EXTRACT_ERROR = "General Error extracting file. Please confirm that the .encrypted file exists, that you have enough disk space and you have appropriate write permissions.";
        private const string DECRYPT_ERROR = "Error decrypting the rules file. This usually indicates a key problem. Check into using a keyfile!";
            
        public readonly Guid applicationID = new Guid(APPLICATION_ID);


        private List<string> customFolders;
        private static XmlSerializer mergedSerializer = new XmlSerializer(typeof(List<LastMergedFileInfo>));
        private List<LastMergedFileInfo> currentlyMerged;

        public string KeyFile { get; set; }

        private static string basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\CBLoader\\";
        public static string BasePath
        {
            get { return basePath; }
            set { basePath = value; }
        }
        public static string MergedPath
        {
            get { return BasePath + "combined.dnd40"; }
        }
        private static string MergedFileInfo
        {
            get { return  BasePath + "cbloader.merged"; }
        }
        private static string CoreFileName
        {
            get { return BasePath + "combined.dnd40.main"; }
        }

        private static string PartFileName
        {
            get { return BasePath + "combined.dnd40.part"; }
        }


        public FileManager()
        {
            KeyFile = BasePath +  "cbloader.keyfile";
            if (!Directory.Exists(BasePath))
                Directory.CreateDirectory(BasePath);

            if (File.Exists(MergedFileInfo))
            {
                using (StreamReader sr = new StreamReader(MergedFileInfo, Encoding.Default))
                {
                    currentlyMerged = (List<LastMergedFileInfo>)mergedSerializer.Deserialize(sr);
                }
            }
            else
                currentlyMerged = new List<LastMergedFileInfo>();
             customFolders = new List<string>() { "custom" };
        }

        /// <summary>
        /// Gets the list of custom folders.
        /// </summary>
        public List<string> CustomFolders
        {
            get { return customFolders; }
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
            Log.Debug("Checking for merge and extract.");
            ExtractFile(forced);
            MergeFiles(forced);
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

            List<FileInfo> customFiles = customFolders.SelectMany(
                GetPartsFromDirectory).OrderBy(f => f.Name).ToList();
            customFiles.Add(new FileInfo(PartFileName));

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
            var files = customFiles.GroupBy(FileWasMerged).OrderBy(a => a.Key).Reverse();
            string fileName = GetIntermediaryFilename(files);
            XDocument main = GetBaseDocument(fileName);

            // Save the unchanged files in a temp document for next time.
            if (!File.Exists(fileName)) {
                foreach (FileInfo fi in files.First())
                    MergeFile(main, fi);
                SaveDocument(main, fileName);
            }

            // Merge in the modified files
            if(files.Count() > 1)
                foreach (FileInfo fi in files.Skip(1).First())
                    MergeFile(main, fi);

            SaveDocument(main, MergedPath);

            using (StreamWriter sw = new StreamWriter(MergedFileInfo, false))
            {
                mergedSerializer.Serialize(sw, currentlyMerged);
            }
        }

        /// <summary>
        /// Merges the specified file into the main document
        /// </summary>
        private void MergeFile(XDocument main, FileInfo fi)
        {
            try
            {
                Log.Info("Merging " + fi.Name + "...");
                XDocument customContent = (XDocument)XDocument.Load(fi.FullName, LoadOptions.PreserveWhitespace);
                if (CheckMetaData(fi, customContent))
                {
                    customContent = (XDocument)XDocument.Load(fi.FullName, LoadOptions.PreserveWhitespace);
                }
                MergePart(customContent, main);
                updateMergedList(fi.FullName, fi.LastWriteTime);
            }
            catch (Exception e)
            {
                updateMergedList(fi.FullName, DateTime.MinValue);
                Log.Error("ERROR LOADING FILE: ", e);
            }
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
                    Log.Error("Failed getting update for " + fi.Name, v);
                }
            }
            if ((metadata = customContent.Root.Element("Obselete")) != null)
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
        /// Gets the base document to merge from. Attempt to use an intermediary document which should contain all
        /// merged files that have not been changed. This is useful if 1 document is actively being changed while
        /// all other documents remain the same.
        /// </summary>
        /// <param name="customFiles"></param>
        /// <returns></returns>
        private XDocument GetBaseDocument(String fileName)
        {
            if (!File.Exists(fileName))
            {
                // clear out any old tmp files
                foreach (string file in Directory.GetFiles(FileManager.BasePath, "*.tmp"))
                    File.Delete(file);
                fileName = CoreFileName;
            }
            return (XDocument)XDocument.Load(fileName, LoadOptions.PreserveWhitespace);
        }

        private static string GetIntermediaryFilename(IEnumerable<IGrouping<bool, FileInfo>> files)
        {
            StringBuilder mergeName = new StringBuilder();
            foreach (var mergedFile in files.First())
                mergeName.Append(mergedFile.FullName + "**");
            string fileName = Convert.ToBase64String(
                new SHA1CryptoServiceProvider()
                 .ComputeHash(
                     Encoding.ASCII.GetBytes(
                         mergeName.ToString()))).Replace("+", "-").Replace("/", "_");
            fileName = fileName + ".tmp";
            return FileManager.BasePath + fileName;
        }

        /// <summary>
        /// Saves the specified document to the specified filename
        /// </summary>
        private void SaveDocument(XDocument main, string filename)
        {
            using (XmlTextWriter xw = new XmlTextWriter(filename, Encoding.UTF8))
            {
                xw.Formatting = Formatting.Indented;
                SaveDocument(xw, main.Root);
            }
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
        /// Merges one document into another.
        /// </summary>
        /// <param name="part"></param>
        /// <param name="main"></param>
        private void MergePart(XDocument part, XDocument main)
        {
            foreach(XElement partElement in part.Root.Elements()) {
                string id = getID(partElement);
                if (id != null)
                {
                    XElement mainElement = main.Root.Descendants("RulesElement").FirstOrDefault(xe => getID(xe) == id);
                    if(mainElement != null) {
                        switch(partElement.Name.LocalName) { 
                            case "RulesElement": mainElement.ReplaceWith(partElement); break;
                            case "RemoveNodes": removeElement(partElement, mainElement); break;
                            case "AppendNodes": appendToElement(partElement, mainElement); break;
                            case "DeleteElement": deleteElement(partElement, mainElement); break;
                        }
                    }
                    else if(partElement.Name == "RulesElement") 
                        main.Root.Add(partElement);
                }
            }
       }

        /// <summary>
        /// Removes the element
        /// </summary>
        /// <param name="partElement"></param>
        /// <param name="mainElement"></param>
        private static void removeElement(XElement partElement, XElement mainElement)
        {
            foreach (XElement el in partElement.Descendants())  // Find the node(s) to be removed.
            {
                String elName = getName(el);
                XElement e2 = mainElement.Descendants().FirstOrDefault(xe => getName(xe) == elName);
                if (e2 != null)
                    e2.Remove();
            }
        }

        /// <summary>
        /// Takes elements from the first node, and adds them to the second.
        /// </summary>
        /// <param name="partRule"></param>
        /// <param name="mainRule"></param>
        private static void appendToElement(XElement partRule, XElement mainRule)
        { 
            // this is the recursive guts of <AppendNodes>
            foreach (XNode node in partRule.Nodes())
            {
                if (node is XElement) // Fix for Issue 48
                {
                    XElement partChild = node as XElement;

                    // What we do depends on the node in question.
                    String id = getID(partChild);
                    String name = getName(partChild);

                    if (partChild.Name == "rules")
                    {
                        // It's the <rules> tag.  Stuff goes inside.
                        XElement e2 = mainRule.Element("rules");
                        if (e2 == null)
                            mainRule.Add(e2 = new XElement("rules"));
                        appendToElement(partChild, e2);
                    }
                    else if (partChild.Name == "Category")
                    {
                        // <Category> contains a CSV string.  Append to that string.  CB doesn't care about duplicate entries, so don't bother checking.
                        XElement e2 = mainRule.Element("Category");
                        if (e2 == null)
                            mainRule.Add(e2 = new XElement("Category"));

                        // remove any spaces or commas at the start or end.
                        e2.Value = e2.Value.Trim(' ', ',');
                        //shove a comma, then the new values (also cleaned up) onto the end.
                        e2.Value = e2.Value + "," + partChild.Value.Trim(' ', ',');
                        // now we put spaces at the start.  Becuase that's how we found it.  This line can probably be safely removed.
                        e2.Value = " " + e2.Value.Trim(' ', ',') + " ";
                    }
                    else
                    {
                        XElement e2 = new XElement(partChild.Name);
                        foreach (XAttribute a in partChild.Attributes())
                            e2.Add(a);
                        mainRule.Add(e2);
                        appendToElement(partChild, e2);
                    }
                }
                else if (node is XText)
                {
                    XText text = node as XText;
                    text.Value = text.Value.Trim();
                    if (text.Value != "") // Don't go sprinkling "   " throughout the xml, please.
                        mainRule.Add(text);
                }
                else
                {
                    mainRule.Add(node);
                }
            }
        }

        /// <summary>
        /// Removes the RulesElement.
        /// </summary>
        /// <param name="partRule"></param>
        /// <param name="mainRule"></param>
        private static void deleteElement(XElement partRule, XElement mainRule)
        { // I can't even remember why I implemented this anymore, or what the difference between this and RemoveNodes is. 
            mainRule.Remove();
        }

        /// <summary>
        /// Gets the ID element from a rule element if available
        /// </summary>
        /// <param name="customRule">The rule element</param>
        /// <returns>The internal id, or null if none is found</returns>
        private static string getID(XElement customRule)
        {
            XAttribute attrib = customRule.Attribute("internal-id");
            string id = null;
            if (attrib != null)
            {
                id = attrib.Value;
                //try to find this id in main
            }
            return id;
        }

        /// <summary>
        /// Gets the ID element from a rule element if available
        /// </summary>
        /// <param name="customRule">The rule element</param>
        /// <returns>The internal id, or null if none is found</returns>
        private static string getName(XElement customRule)
        {
            XAttribute attrib = customRule.Attribute("name");
            string id = null;
            if (attrib != null)
            {
                id = attrib.Value;
                //try to find this id in main
            }
            return id;
        }

        /// <summary>
        /// Extracts the .encrypted file into a .main file and creates a .part file if necessary
        /// <param name="forced">If true, the .encrypted file will always be extracted. Otherwise it is only extracted
        /// if it is updated.</param>
        /// </summary>
        private void ExtractFile(bool forced)
        {
            if (forced || !File.Exists(CoreFileName) || File.GetLastWriteTime(ENCRYPTED_FILENAME) > File.GetLastWriteTime(CoreFileName))
            {
                Log.Info("Extracting " + CoreFileName);
                if (!File.Exists(KeyFile))
                {
                    try
                    {
                        TryExtract();
                    }
                    catch (CryptographicException)
                    {
                        ExtractWithKeyFile();
                    }
                    catch (ArgumentException)
                    {
                        ExtractWithKeyFile();
                    }
                    catch (Exception e)
                    {
                        throw new Exception(GENERAL_EXTRACT_ERROR, e);
                    }
                }
                else
                    ExtractWithKeyFile();
                if (!File.Exists(PartFileName))
                {
                    using (StreamWriter sw = new StreamWriter(PartFileName))
                    {
                        sw.WriteLine("<D20Rules game-system=\"D&amp;D4E\">");
                        sw.WriteLine("</D20Rules>");
                    }
                }
            }
        }

        private void TryExtract()
        {
            using (StreamReader sr = new StreamReader(CommonMethods.GetDecryptedStream(applicationID, ENCRYPTED_FILENAME)))
            {
                using (StreamWriter sw = new StreamWriter(CoreFileName))
                {
                    sw.Write(sr.ReadToEnd());
                }
            }
        }

        private void ExtractWithKeyFile()
        {
            Log.Debug("Not using registry key, trying on-disk key: " + KeyFile);
            CommonMethods.KeyStore = new FileKeyInformationStore(KeyFile);
            try
            {
                TryExtract();
            }
            catch (CryptographicException ce)
            {
                throw new Exception(DECRYPT_ERROR, ce);
            }
            catch(ArgumentException ae) {
                throw new Exception(DECRYPT_ERROR, ae);
            }
            catch (Exception ex)
            {
                throw new Exception(GENERAL_EXTRACT_ERROR, ex);
            }
        }
   }
}
