using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace UpdatePackager
{
    class Program
    {
        static void Main(string[] args)
        {
            string configfile = Path.Combine(Environment.CurrentDirectory, "packagerconfig.xml");
            if (args.Length > 0)
            {
                if (File.Exists(args[0]))
                    configfile = args[0];
                else if (Directory.Exists(args[0]))
                    configfile = Path.Combine(args[0], "packagerconfig.xml");
            }
            
            //if (!File.Exists(configfile) && File.Exists(Path.Combine(Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), "packagerconfig.xml")))
              //  configfile = Path.Combine(Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), "packagerconfig.xml");

            PackagerConfig config = PackagerConfig.Load(configfile);
            config.Save(configfile);

            if (config.CreateUpdateInfo || true)
            {
                String DropboxPath = Environment.ExpandEnvironmentVariables( Path.Combine(config.ExportPath, config.ProjectNameWebFriendly));
                Directory.CreateDirectory(DropboxPath);

                String indexFile = Path.Combine(DropboxPath, config.ProjectName + ".index");
                XDocument index = File.Exists(indexFile) ? XDocument.Load(indexFile) : new XDocument(new XElement("PartIndex"));
                XElement UpdateInfo = index.Root.Element("UpdateInfo");
                if (UpdateInfo == null)
                    index.Root.AddFirst(UpdateInfo =
                        new XElement("UpdateInfo",
                            new XElement("Version", File.Exists(indexFile.Replace(".index", ".txt")) ? File.ReadAllText(indexFile.Replace(".index", ".txt")) : "0"),
                            new XElement("Filename", Path.GetFileName(indexFile)),
                            new XElement("PartAddress", config.Url + config.ProjectNameWebFriendly + "/" + Path.GetFileName(indexFile)),
                            new XElement("VersionAddress", config.Url + config.ProjectNameWebFriendly + "/" + Path.GetFileNameWithoutExtension(Path.GetFileName(indexFile)) + ".txt")
                        )
                    );
                List<string> Parts = new List<string>();
                foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(configfile), "*.part"))
                {
                    String VersionlessName = Path.GetFileName(file);
                    if (VersionlessName.Contains("(v"))
                    {
                        VersionlessName = VersionlessName.Substring(0, VersionlessName.IndexOf("(v")).Trim();
                        VersionlessName += ".part";
                    }
                    Parts.Add(VersionlessName);
                    String DropboxFile = Path.Combine(DropboxPath, VersionlessName);
                    if (File.Exists(DropboxFile) && File.ReadAllText(DropboxFile) == File.ReadAllText(file))
                        continue;
                    Console.WriteLine("Updating " + VersionlessName);

                    XDocument part = XDocument.Load(file);
                    UpdateInfo = part.Root.Element("UpdateInfo");
                    if (UpdateInfo == null)
                        part.Root.AddFirst(UpdateInfo =
                            new XElement("UpdateInfo",
                                new XElement("Version", File.Exists(DropboxFile.Replace(".part", ".txt")) ? File.ReadAllText(DropboxFile.Replace(".part", ".txt")) : "0"),
                                new XElement("Filename", VersionlessName),
                                new XElement("PartAddress", config.Url + config.ProjectNameWebFriendly + "/" + VersionlessName),
                                new XElement("VersionAddress", config.Url + config.ProjectNameWebFriendly + "/" + Path.GetFileNameWithoutExtension(VersionlessName) + ".txt")
                            )
                        );
                    if (File.Exists(DropboxFile))
                        try
                        {
                            XDocument dbver = XDocument.Load(DropboxFile);
                            XElement obsolete;
                            if ((obsolete = dbver.Root.Element("Obselete")) != null)
                            {
                                obsolete.Name = "Obsolete";
                                Console.WriteLine("Corrected misspelt Obsolete within " + VersionlessName);
                            }
                            if ((obsolete = dbver.Root.Element("Obsolete")) != null)
                                UpdateInfo.AddAfterSelf(obsolete);
                        }
                        catch (Exception)
                        {  } // Meh.

                    UpdateInfo.Element("Version").Value = (int.Parse(UpdateInfo.Element("Version").Value) + 1).ToString();
                    part.Save(file);
                    part.Save(DropboxFile);
                    File.WriteAllText(DropboxFile.Replace(".part", ".txt"), UpdateInfo.Element("Version").Value);
                    if ((index.Root.Elements().FirstOrDefault(xe => xe.Element("Filename").Value == UpdateInfo.Element("Filename").Value)) == null)
                        index.Root.Add(new XElement("Part", UpdateInfo.Element("Filename"), UpdateInfo.Element("PartAddress")));
                }
                foreach (XElement part in index.Root.Elements("Part"))
                {
                    if (!Parts.Contains(part.Element("Filename").Value))
                        part.Name = "Obsolete";
                }
                foreach (XElement part in index.Root.Elements("Obselete"))
                {
                    part.Name = "Obsolete";
                    Console.WriteLine("Corrected misspelt Obsolete for " + part.Element("Filename").Value + " in index.");
                }
                if (!File.Exists(indexFile) || XDocument.Load(indexFile).ToString() != index.ToString())
                {
                    UpdateInfo = index.Root.Element("UpdateInfo");
                    UpdateInfo.Element("Version").Value = (int.Parse(UpdateInfo.Element("Version").Value) + 1).ToString();
                    File.WriteAllText(indexFile.Replace(".index", ".txt"), UpdateInfo.Element("Version").Value);
                    index.Save(indexFile);
                }
            }
            #region DCupdater
            /*
            if (config.CreateDCUpdaterFile)
            {
                String VersionInfoFile;
                XDocument VersionInfo = new XDocument();

                if (File.Exists(VersionInfoFile = Environment.ExpandEnvironmentVariables(Path.Combine(config.ExportPath, config.ProjectNameWebFriendly + "Version.xml"))))
                    VersionInfo = XDocument.Load(VersionInfoFile);
                else
                    VersionInfo = XDocument.Parse(@"<root>
<Program_Version>0</Program_Version>
<Program_Release_Month></Program_Release_Month>
<Program_Release_Day></Program_Release_Day>
<Program_Release_Year></Program_Release_Year>
</root>");
                XElement node;
                node = VersionInfo.Root.Element("Program_Version");
                int VersionNum = int.Parse(node.Value);
                VersionNum++;
                node.Value = VersionNum.ToString();
                node = VersionInfo.Root.Element("Program_Release_Month");
                node.Value = DateTime.UtcNow.Date.Month.ToString();
                node = VersionInfo.Root.Element("Program_Release_Day");
                node.Value = DateTime.UtcNow.Date.Day.ToString();
                node = VersionInfo.Root.Element("Program_Release_Year");
                node.Value = DateTime.UtcNow.Date.Year.ToString();
                VersionInfo.Save(VersionInfoFile);

                String wdpath = Environment.ExpandEnvironmentVariables(Path.Combine("%temp%", new Random().Next().ToString()));
                String wdinner;
                Directory.CreateDirectory(wdpath);
                Directory.CreateDirectory(wdinner = Path.Combine(wdpath, config.FolderName));
                foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(configfile), "*.part"))
                {
                    String VersionlessName = Path.GetFileName(file);
                    if (VersionlessName.Contains("(v"))
                    {
                        VersionlessName = VersionlessName.Substring(0, VersionlessName.IndexOf("(v")).Trim();
                        VersionlessName += ".part";
                    }
                    File.Copy(file, Path.Combine(wdinner, VersionlessName));
                }
                foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(configfile), "*.dcupdate"))
                {
                    String VersionlessName = Path.GetFileName(file);
                    File.Copy(file, Path.Combine(wdpath, VersionlessName));
                }
                foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(configfile), "*.bat"))
                {
                    String VersionlessName = Path.GetFileName(file);
                    File.Copy(file, Path.Combine(wdinner, VersionlessName));
                }

                String path = Environment.ExpandEnvironmentVariables(Path.Combine(config.ExportPath, config.ProjectNameWebFriendly, config.ProjectName + ".index"));
                if (File.Exists(path))
                    File.Copy(path, Path.Combine(wdinner, config.ProjectName + ".index"));
                XmlDocument dcupdate = new XmlDocument();
                dcupdate.LoadXml("<?xml version=\"1.0\" ?><Local><Label>" + config.ProjectName
                    + " </Label><IconFile>CBLoader.exe</IconFile><Version>" + VersionNum
                    + "</Version><VersionFileRemote>" + config.Url + config.ProjectNameWebFriendly + "Version.xml"
                    + "</VersionFileRemote><WebPage>" + (config.Webpage != "" ? config.Webpage : config.Url) + "</WebPage><UpdateMethod>unzip</UpdateMethod><CloseForUpdate>CBLoader.exe</CloseForUpdate><UpdateFile>" + config.Url + config.ProjectNameWebFriendly + ".zip" + "</UpdateFile></Local>");
                dcupdate.Save(Path.Combine(wdpath, config.ProjectNameWebFriendly + ".dcupdate"));
                f.CreateZip(Path.Combine(Environment.ExpandEnvironmentVariables(config.ExportPath), config.ProjectNameWebFriendly + ".zip"), wdpath, true, "");

                Directory.Delete(wdpath, true);
            }
            */
            #endregion
            Console.Write("Done.");
            Console.ReadKey();
        }
    }
}
