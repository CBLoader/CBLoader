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
        private List<string> ignoredParts;
        private static XmlSerializer mergedSerializer = new XmlSerializer(typeof(List<LastMergedFileInfo>));
        private static List<LastMergedFileInfo> currentlyMerged;

        public static string KeyFile { get; set; }

        private static string basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\CBLoader\\";
        public static string BasePath
        {
            get { return basePath; }
            set
            {
                basePath = value;
                KeyFile = BasePath + "cbloader.keyfile";
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
            }
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

        public bool UseNewMergeLogic { get; set; }

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
             customFolders = new List<string>();
             ignoredParts = new List<string>();
             UseNewMergeLogic = true;
            string ddi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ddi");
            if (Directory.Exists(ddi))
                AddCustomFolder(Directory.CreateDirectory(Path.Combine(ddi, "CBLoader")).FullName);
        }

        /// <summary>
        /// Gets the list of custom folders.
        /// </summary>
        public List<string> CustomFolders
        {
            get { return customFolders; }
        }

        /// <summary>
        /// Gets the list of ignored parts.
        /// </summary>
        public List<string> IgnoredParts
        {
            get { return ignoredParts; }
        }

        public bool AddCustomFolder(string folder)
        {
            folder = Environment.ExpandEnvironmentVariables(folder).Trim(); // Expand any Environmental Variables.
            DirectoryInfo di = new DirectoryInfo(folder);
            if (!di.Exists)
                di = new DirectoryInfo(Path.Combine(basePath, folder));
            if (!di.Exists)
                di = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), folder));
            if (!di.Exists)
                return false;
            folder = di.FullName; // No longer relative, so we can safely change the WD to CharacterBuilder.
            if (customFolders.Contains(folder)) // We weren't actually checking if a folder got added twice.
                return false;
            customFolders.Add(folder);
            return true;
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
        /// Check indexes for new files.
        /// </summary>
        /// <param name="forced"></param>
        public bool CheckIndexes(bool forced)
        {
            List<FileInfo> Indexes = customFolders.SelectMany(
                GetIndexesFromDirectory).OrderBy(f => f.Name).ToList();
            bool NewFiles = false;

            System.Net.WebClient wc = new System.Net.WebClient();
            foreach (FileInfo index in Indexes)
            {
                XDocument PartIndex = XDocument.Load(index.FullName);
                CheckMetaData(index, PartIndex);
                foreach (XElement Part in PartIndex.Root.Elements("Part"))
                {
                    try
                    {
                        string filename = Path.Combine(index.Directory.FullName, Part.Element("Filename").Value);
                        if (ignoredParts.Contains(Part.Element("Filename").Value.ToLower().Trim()))
                            continue;
                        if (!File.Exists(filename) || forced)
                        {
                            Log.Info("Downloading " + Part.Element("Filename").Value + " from " + index.Name);
                            wc.DownloadFile(Part.Element("PartAddress").Value, filename);
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

            List<FileInfo> customFiles = customFolders.SelectMany(
                GetPartsFromDirectory).OrderBy(f => f.Name).ToList();
            customFiles.RemoveAll(f => IgnoredParts.Contains(f.Name.ToLower().Trim()));
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
            
            if (UseNewMergeLogic) // New merge logic is equally as fast when doing the entire thing, so let's just do that.
                files = customFiles.GroupBy(fi => false).OrderBy(a => a.Key).Reverse();
               
            string fileName = GetIntermediaryFilename(files);
            XDocument main = GetBaseDocument(fileName);

            Dictionary<string, XElement> idDictionary = new Dictionary<string, XElement>();

            // Save the unchanged files in a temp document for next time.
            if (!File.Exists(fileName)) {
                foreach (FileInfo fi in files.First())
                    MergeFile(main, fi, idDictionary);
                DeQueueMerges(main, idDictionary);
                SaveDocument(main, fileName);
            }

            // Merge in the modified files
            if(files.Count() > 1)
                foreach (FileInfo fi in files.Skip(1).First())
                    MergeFile(main, fi,idDictionary);
            if (UseNewMergeLogic && idDictionary.Keys.FirstOrDefault() != null) // There is no first element, therefore there's nothing in there.  Skip the enumeration.
                DeQueueMerges(main, idDictionary);
            SaveDocument(main, MergedPath);

            using (StreamWriter sw = new StreamWriter(MergedFileInfo, false))
            {
                mergedSerializer.Serialize(sw, currentlyMerged);
            }
        }

        /// <summary>
        /// Merges the specified file into the main document
        /// </summary>
        private void MergeFile(XDocument main, FileInfo fi, Dictionary<string, XElement> idDictionary)
        {
            try
            {
                if (UseNewMergeLogic)
                    Log.Info("Loading " + fi.Name + "...");
                else
                    Log.Info("Merging " + fi.Name + "...");
                XDocument customContent = (XDocument)XDocument.Load(fi.FullName, LoadOptions.PreserveWhitespace);
                if (UseNewMergeLogic)
                {
                    QueuePart(customContent, idDictionary);
                }
                else
                    MergePart(customContent, main);
                updateMergedList(fi.FullName, fi.LastWriteTime);
            }
            catch (Exception e)
            {
                updateMergedList(fi.FullName, DateTime.MinValue);
                Log.Error("ERROR LOADING FILE: ", e);
            }
        }

        private void QueuePart(XDocument part, Dictionary<string, XElement> idDictionary)
        {
            foreach (XElement partElement in part.Root.Elements())
            {
                string id = getID(partElement);
                if (id != null)
                {
                    if (idDictionary.ContainsKey(id))
                        idDictionary[id].Add(partElement);
                    else
                        idDictionary.Add(id, new XElement("MergeContainer", partElement));
                }
                else if (partElement.Name == "MassAppend")
                    if (idDictionary.ContainsKey("nullID"))
                        idDictionary["nullID"].Add(partElement);
                    else
                        idDictionary.Add("nullID", new XElement("MergeContainer", partElement));
            }
        }

        private void DeQueueMerges(XDocument main, Dictionary<string, XElement> idDictionary)
        {
            if (idDictionary.Keys.FirstOrDefault() == null)
                return; // We're not needed here.
            Log.Info("Applying base elements.");
            XElement mainElement = main.Root.Elements("RulesElement").First();
            XNode NextElement;
            do
            {
                XNode prev = mainElement.PreviousNode;
                NextElement = mainElement.NextNode;
                while (NextElement != null && !(NextElement is XElement))
                    NextElement = NextElement.NextNode;
                string id = getID(mainElement);
                if (idDictionary.ContainsKey(id))
                {
                    foreach (XElement partElement in idDictionary[id].Elements())
                    {
                        switch (partElement.Name.LocalName)
                        {
                            case "RulesElement": mainElement.ReplaceWith(partElement); break;
                            case "RemoveNodes": removeElement(partElement, mainElement); break;
                            case "AppendNodes": appendToElement(partElement, mainElement); break;
                            case "DeleteElement": deleteElement(partElement, mainElement); break;
                        }
                        mainElement = prev.NextNode as XElement; // Lost Parent
                    }
                    idDictionary.Remove(id);
                }
                if (idDictionary.ContainsKey("nullID"))
                {
                    XElement[] MassAppends = idDictionary["nullID"].Elements().ToArray();
                    for (int i = 0; i < MassAppends.Length; i++)
                    {
                        XElement partElement = MassAppends[i];
                        string[] ids = partElement.Attribute("ids").Value.Trim().Split(',');
                        if (ids.Contains(id))
                            appendToElement(partElement, mainElement);
                    }
                }
                if (idDictionary.Keys.FirstOrDefault() == null)
                    return; // Quick way of aborting if we're done.  Anything more complex isn't really worth it.
            } while ((mainElement = NextElement as XElement) != null);
            Log.Info("Applying new elements");
            foreach (String id in idDictionary.Keys.ToArray())
            {
                XElement RulesCollection = idDictionary[id];
                mainElement = new XElement("RulesElement", new XAttribute("internal-id","deleteme"));
                main.Root.Add(mainElement);
                
                XNode prev = mainElement.PreviousNode;
                foreach (XElement partElement in RulesCollection.Elements())
                {
                    switch (partElement.Name.LocalName)
                    {
                        case "RulesElement": mainElement.ReplaceWith(partElement); break;
                        case "RemoveNodes": removeElement(partElement, mainElement); break;
                        case "AppendNodes": appendToElement(partElement, mainElement); break;
                        case "DeleteElement": deleteElement(partElement, mainElement); break;
                    }
                    mainElement = prev.NextNode as XElement; // Lost Parent
                }
                if (getID(mainElement) == "deleteme")
                    mainElement.Remove(); // It was only an append.
                idDictionary.Remove(id);
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
                mergeName.Append(mergedFile.FullName + mergedFile.LastWriteTime.ToString() + "**");
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
                    if (mainElement != null)
                    {
                        switch (partElement.Name.LocalName)
                        {
                            case "RulesElement": mainElement.ReplaceWith(partElement); break;
                            case "RemoveNodes": removeElement(partElement, mainElement); break;
                            case "AppendNodes": appendToElement(partElement, mainElement); break;
                            case "DeleteElement": deleteElement(partElement, mainElement); break;
                        }
                    }
                    else if (partElement.Name == "RulesElement")
                        main.Root.Add(partElement);
                }
                else if (partElement.Name == "MassAppend")
                    massAppend(partElement, main);
            }
       }

        /// <summary>
        /// Appends the contents to multiple elements
        /// </summary>
        /// <param name="partElement"></param>
        private static void massAppend(XElement partElement, XDocument main)
        {
            string[] ids = partElement.Attribute("ids").Value.Trim().Split(',');
            IEnumerable<XElement> elements = main.Root.Descendants("RulesElement").Where(xe => ids.Contains(getID(xe)));
            partElement.Attribute("ids").Remove(); // Don't pass the Attribute around.
            foreach (XElement mainRule in elements)
            {
                appendToElement(partElement, mainRule);
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
        { // Removes the Element completely, whereas RemoveNodes gets rid of contents.
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
                    catch (IOException e)
                    {
                        if (e.Message.Contains("assembly 'ApplicationUpdate.Client"))
                        {
                            Log.Info("Copying ApplicationUpdate.Client.dll to CBLoader folder.  The following error is expected.  Just relaunch CBLoader.");
                            File.Copy("ApplicationUpdate.Client.dll", Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "ApplicationUpdate.Client.dll")); // Get the DLL from the Character Builder Folder.
                            ExtractWithKeyFile();
                        }
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

        public bool DoUpdates(bool forced)
        {
            List<FileInfo> customFiles = customFolders.SelectMany(
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
