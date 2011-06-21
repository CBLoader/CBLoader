using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Xml.Serialization;
using System.Xml;

namespace UpdatePackager
{
    public class PackagerConfig
    {
        [Category("Config")]
        [DisplayName("Index Name")]
        [Description("Used as the name for the index file")]
        public String ProjectName { get; set; }
        
        private String projectNameWebFriendly;
        [Category("Config")]
        [DisplayName("Folder Name")]
        [Description("The name of the folder.  Must be something that works as a URL.\n" +
            "The Packager will create a folder within the Export Path with this name, and place your parts in there.")]
        public String ProjectNameWebFriendly
        {
            get
            {
                if (projectNameWebFriendly != "" && projectNameWebFriendly != null)
                    return projectNameWebFriendly;
                if (ProjectName == null)
                    return "";
                return ProjectName.Replace(" ", "-");
            }
            set { projectNameWebFriendly = value; }
        }

        [Category("Config")]
        [Description("The Local Directory to save the outputted files to.  Somewhere in your dropbox is recommended")]
        [DefaultValue(@"%userprofile%\Documents\My Dropbox\Public\")]
        public String ExportPath { get; set; }
        
        [Category("Depreciated")]
        [Description("If using a DCUpdater setup (depreciated), the folder in which to place the files.")]
        [Browsable(false)]
        [DefaultValue("")]
        public String FolderName { get; set; }

        [Category("Config")][Description("The URL that corrosponds to the Export Path specified above.")]
        [DefaultValue("http://dl.dropbox.com/u/DropBoxID/")]
        public String Url { get; set; }

        [Category("Depreciated")]
        [Browsable(false)]
        [DefaultValue("")]
        public String FancyName { get; set; }

        [Category("Depreciated")]
        [Browsable(false)]
        [DefaultValue("")]
        public String Webpage { get; set; }

        [Category("Depreciated")]
        [Browsable(false)]
        [DefaultValue(null)]
        public bool CreateDCUpdaterFile { get; set; }

        [Category("Depreciated")]
        [Browsable(false)]
        [DefaultValue(null)]
        public bool CreateUpdateInfo { get; set; }

        public static PackagerConfig Load(string Path)
        {
            XmlReader xr = null;
            try
            {
                xr = XmlReader.Create(Path);
                XmlSerializer xs = new XmlSerializer(typeof(PackagerConfig));
                PackagerConfig config = xs.Deserialize(xr) as PackagerConfig;
                xr.Close();
                return config;
            }
            catch (Exception v)
            {
                if (xr != null)
                    xr.Close();
               // Console.WriteLine(v.ToString());
                PackagerConfig conf = new PackagerConfig();
                new PropGrid(conf).ShowDialog();
                return conf;
            }
        }

        internal void Save(string configfile)
        {
            XmlSerializer xs = new XmlSerializer(typeof(PackagerConfig));
            XmlWriter xw = XmlWriter.Create(configfile);
            xs.Serialize(xw, this);
            xw.Close();
        }
    }
}
