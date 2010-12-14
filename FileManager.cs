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
        public readonly Guid applicationID = new Guid(APPLICATION_ID);

        private List<string> customFolders;
        private static XmlSerializer mergedSerializer = new XmlSerializer(typeof(List<LastMergedFileInfo>));
        private List<LastMergedFileInfo> currentlyMerged;

        public bool ForceUseKeyFile { get; set; }
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
            KeyFile = "ApplicationKey.update";
            if (!Directory.Exists(BasePath))
            {
                Directory.CreateDirectory(BasePath);
            }
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
            if (!forced && File.Exists(MergedPath) && currentlyMerged.Count > 0)
            {
                if (customFiles.TrueForAll(FileWasMerged))
                    return;
            }
            currentlyMerged.Clear();
            // construct the custom rules file
            XDocument main = (XDocument)XDocument.Load(CoreFileName, LoadOptions.PreserveWhitespace);
            foreach (FileInfo fi in customFiles)

            {
                Log.Info("Merging " + fi.Name + "...");
                try
                {
                    XDocument customContent = (XDocument)XDocument.Load(fi.FullName, LoadOptions.PreserveWhitespace);
                    MergePart(customContent, main);
                    currentlyMerged.Add(new LastMergedFileInfo()
                    {
                        FileName = fi.FullName,
                        LastTouched = fi.LastWriteTime
                    });
                }
                catch (Exception e)
                {
                    currentlyMerged.Add(new LastMergedFileInfo()
                    {
                        FileName = fi.FullName,
                        LastTouched = DateTime.MinValue
                    });
                    Log.Error("ERROR LOADING FILE: ", e);
                }
            }
            using (XmlTextWriter xw = new XmlTextWriter(MergedPath, Encoding.UTF8))
            {
                xw.Formatting = Formatting.Indented;
                SaveDocument(xw, main.Root);
            }
            //            main.Save(FINAL_FILENAME,SaveOptions.DisableFormatting);
            using (StreamWriter sw = new StreamWriter(MergedFileInfo, false))
            {
                mergedSerializer.Serialize(sw, currentlyMerged);
            }
        }

        /// <summary>
        /// Writing this out via an xmlwriter, XDocument.Save overrides important line-ending information
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
            // replace all main elements with part elements having the same internal-id
            foreach (XElement customRule in part.Root.Descendants(XName.Get("RulesElement")))
            {

                string id = getID(customRule);
                if (id != null)
                {
                    XElement el = main.Root.Descendants(XName.Get("RulesElement")).FirstOrDefault(xe => getID(xe) == id);
                    if (el == null)
                        main.Root.Add(customRule);
                    else
                        el.ReplaceWith(customRule);
                }
            }
            
            foreach (XElement customRule in part.Root.Descendants(XName.Get("RemoveNodes"))) // What it says on the box; removes the selected nodes.  NOTE: Do not include <rules> here, because it'll remove all the rules.  Just mention the sub-element.
            {  // Be careful with this one.  Very Careful.  We probably want to improve the logic here as well.
                String id = getID(customRule);
                XElement rule = main.Root.Descendants(XName.Get("RulesElement")).FirstOrDefault(xe => getID(xe) == id); // match the RulesElement.
                if (rule != null) //No point removing something that doesn't exist.  Note to Self: Load Order Matters.
                {
                    foreach (XElement el in customRule.Descendants())  // Find the node(s) to be removed.
                    {
                        String elName = getName(el);
                        XElement e2 = rule.Descendants().FirstOrDefault(xe => getName(xe) == elName);
                        if (e2 != null)
                            e2.Remove();
                    }
                }
            }
            foreach (XElement customRule in part.Root.Descendants(XName.Get("AppendNodes")))
            {  // This one isn't anywhere as dangerous as the one above.  Infact, it's safer than using a <RulesElement> to update a rule. (unless order matters.   Note to self: add append-after attribute. 
                String id = getID(customRule);
                XElement rule = main.Root.Descendants(XName.Get("RulesElement")).FirstOrDefault(xe => getID(xe) == id);
                if (rule != null) //There's nothing to append this data to, so just ignore it.  Note to Self: Load Order Matters.
                {
                    appendToElement(customRule, rule);
                }
            }

       }

        /// <summary>
        /// Takes elements from the first node, and adds them to the second.
        /// </summary>
        /// <param name="customRule"></param>
        /// <param name="rule"></param>
        private static void appendToElement(XElement customRule, XElement rule)
        { // this is the recursive guts of <AppendNodes>
            foreach (XElement el in customRule.Elements())
            {  // What we do depends on the node in question.
                String id = getID(el);
                String name = getName(el);

                if (el.Name == "rules")
                { // It's the <rules> tag.  Stuff goes inside.
                    XElement e2 = rule.Element(XName.Get("rules"));
                    if (e2 == null)
                        rule.Add(e2 = new XElement(XName.Get("rules")));
                    appendToElement(el, e2);
                }
                else if (el.Name == "Category")
                { // <Category> contains a CSV string.  Append to that string.  CB doesn't care about duplicate entries, so don't bother checking.
                    XElement e2 = rule.Element(XName.Get("Category"));
                    if (e2 == null)
                        rule.Add(e2 = new XElement(XName.Get("Category")));
                    e2.Value = e2.Value.Trim(' ', ','); // remove any spaces or commas at the start or end.
                    e2.Value = e2.Value + "," + el.Value.Trim(' ', ',');  //shove a comma, then the new values (also cleaned up) onto the end.
                    e2.Value = " " + e2.Value.Trim(' ', ',') + " "; // now we put spaces at the start.  Becuase that's how we found it.  This line can probably be safely removed.
                }
                else
                {
                    XElement e2;
                    rule.Add(e2 = new XElement(el.Name, new XAttribute("internal-id", id)));
                    appendToElement(el, e2);
                }
            }
        }

        /// <summary>
        /// Gets the ID element from a rule element if available
        /// </summary>
        /// <param name="customRule">The rule element</param>
        /// <returns>The internal id, or null if none is found</returns>
        private static string getID(XElement customRule)
        {
            XAttribute attrib = customRule.Attribute(XName.Get("internal-id"));
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
            XAttribute attrib = customRule.Attribute(XName.Get("name"));
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
                try
                {
                    if (ForceUseKeyFile)
                        ExtractWithKeyFile();
                    else
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
                catch(Exception e) {
                    throw new Exception("General Error extracting file. Please confirm that the .encrypted file exists, that you have enough disk space and you have appropriat write permissions.",e);
                }
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
                throw new Exception("Error decrypting the rules file. THis usually indicates a key problem. Check into using a keyfile!", ce);
            }
            catch(ArgumentException ae) {
                throw new Exception("Error decrypting the rules file. THis usually indicates a key problem. Check into using a keyfile!", ae);
            }
            catch (Exception ex)
            {
                throw new Exception("General Error extracting file. Please confirm that the .encrypted file exists, that you have enough disk space and you have appropriat write permissions.", ex);
            }
        }
   }
}
