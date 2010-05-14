using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CharacterBuilderLoader
{
    public class MetaDataHeader
    {
        public int Signature { get; set; }
        public short MajorVersion { get; set; }
        public short MinorVersion { get; set; }
        public int Reserved { get; set; }
        public int VersionLength { get; set; }
        public string VersionString { get; set; }
        public short Flags { get; set; }
        public short NumberOfStreams { get; set; }
        public MetaDataHeader(FileStream fs)
        {
            Signature = Utils.ReadInt(fs);
            MajorVersion = Utils.ReadShort(fs);
            MinorVersion = Utils.ReadShort(fs);
            Reserved = Utils.ReadInt(fs);
            VersionLength = Utils.ReadInt(fs);
            VersionString = Utils.ReadString(fs, VersionLength);
            Flags = Utils.ReadShort(fs);
            NumberOfStreams = Utils.ReadShort(fs);

        }
    }
}
