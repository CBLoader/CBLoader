using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml.Linq;
using ApplicationUpdate.Client;
using System.Xml.Serialization;

namespace CharacterBuilderLoader
{
    /// <summary>
    /// Manages interactions with the files on the disc
    /// </summary>
    public class FileManager
    {
        public const string ENCRYPTED_FILENAME = "combined.dnd40.encrypted";
        public const string CORE_FILENAME = "combined.dnd40.main";
        public const string PART_FILENAME = "combined.dnd40.part";
        public const string FINAL_FILENAME = "combined.dnd40";
        private const string MERGED_FILEINFO = "cbloader.merged";
        private readonly Guid applicationID = new Guid("2a1ddbc4-4503-4392-9548-d0010d1ba9b1");
        private List<string> customFolders = new List<string>() { "custom" };
        private static XmlSerializer mergedSerializer = new XmlSerializer(typeof(List<LastMergedFileInfo>));
        private List<LastMergedFileInfo> currentlyMerged;

        public FileManager()
        {
            if (File.Exists(MERGED_FILEINFO))
            {
                using(StreamReader sr = new StreamReader(MERGED_FILEINFO,Encoding.Default)) {
                    currentlyMerged = (List<LastMergedFileInfo>)mergedSerializer.Deserialize(sr);
                }
            }
            else
                currentlyMerged = new List<LastMergedFileInfo>();
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
            if (!File.Exists(CORE_FILENAME))
                throw new Exception("Error, could not find file: " + CORE_FILENAME);

            List<FileInfo> customFiles = customFolders.SelectMany(
                GetPartsFromDirectory).OrderBy(f => f.Name).ToList();
            customFiles.Add(new FileInfo(PART_FILENAME));
            if (!forced && File.Exists(FINAL_FILENAME) && currentlyMerged.Count > 0)
            {
                if (customFiles.TrueForAll(FileWasMerged))
                    return;
            }
            currentlyMerged.Clear();
            // construct the custom rules file
            XDocument main = (XDocument)XDocument.Load(CORE_FILENAME);
            foreach (FileInfo fi in customFiles)
            {
                Console.WriteLine("Merging " + fi.FullName + "...");
                try
                {

                    XDocument customContent = (XDocument)XDocument.Load(fi.FullName);
                    MergePart(customContent, main);
                    currentlyMerged.Add(new LastMergedFileInfo()
                    {
                        FileName = fi.FullName,
                        LastTouched = fi.LastWriteTime
                    });
                }
                catch (Exception e)
                {
                    Console.WriteLine("ERROR LOADING FILE! (" + e.Message + ")");
                }
            }

            main.Save(FINAL_FILENAME,SaveOptions.DisableFormatting);
            using (StreamWriter sw = new StreamWriter(MERGED_FILEINFO, false))
            {
                mergedSerializer.Serialize(sw, currentlyMerged);
            }
        }

        private static FileInfo[] GetPartsFromDirectory(string fn)
        {
            if(Directory.Exists(fn))
                return new DirectoryInfo(fn).GetFiles("*.part");
            else
                return new FileInfo[0];
        }

        private bool FileWasMerged(FileInfo fi)
        {
            LastMergedFileInfo lmf = currentlyMerged.FirstOrDefault(lmfi => lmfi.FileName.ToLower() == fi.FullName.ToLower());
            if (lmf != null)
                return lmf.LastTouched.Equals(fi.LastWriteTime);
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
        /// Extracts the .encrypted file into a .main file and creates a .part file if necessary
        /// <param name="forced">If true, the .encrypted file will always be extracted. Otherwise it is only extracted
        /// if it is updated.</param>
        /// </summary>
        private void ExtractFile(bool forced)
        {
            if (forced || !File.Exists(CORE_FILENAME) || File.GetLastWriteTime(ENCRYPTED_FILENAME) > File.GetLastWriteTime(CORE_FILENAME))
            {
                Console.WriteLine("Extracting " + CORE_FILENAME);
                try
                {
                    using (StreamReader sr = new StreamReader(CommonMethods.GetDecryptedStream(applicationID, ENCRYPTED_FILENAME)))
                    {
                        using (StreamWriter sw = new StreamWriter(CORE_FILENAME))
                        {
                            sw.Write(sr.ReadToEnd());
                        }
                    }
                }
                catch(ArgumentException)
                {
                    throw new Exception("Error decrypting file. Do you have a valid decryption key for character builder installed?");
                }
                if (!File.Exists(PART_FILENAME))
                {
                    using (StreamWriter sw = new StreamWriter(PART_FILENAME))
                    {
                        sw.WriteLine("<D20Rules game-system=\"D&amp;D4E\">");
                        sw.WriteLine("</D20Rules>");
                    }
                }
            }
        }




    }
}
