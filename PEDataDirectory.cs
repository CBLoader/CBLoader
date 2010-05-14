using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CharacterBuilderLoader
{
    public class PEDataDirectory
    {
        public string DirectoryName { get; set; }
        public int VirtualAddress { get; set; }
        public int Size { get; set; }
        public PEDataDirectory(FileStream fs, string name)
        {
            this.DirectoryName = name;
            VirtualAddress = Utils.ReadInt(fs);
            Size = Utils.ReadInt(fs);
        }
    }
}
