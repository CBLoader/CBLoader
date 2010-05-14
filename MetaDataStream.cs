using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CharacterBuilderLoader
{
    public class MetaDataStream
    {
        public int Offset { get; set; }
        public int Size { get; set; }
        public string Name { get; set; }
        public object Data { get; set; }
        public MetaDataStream(FileStream fs)
        {
            Offset = Utils.ReadInt(fs);
            Size = Utils.ReadInt(fs);
            Name = Utils.ReadString(fs);
            // we need to align to the next 4-byte boundary
            fs.Seek((4 - (fs.Position % 4)) % 4, SeekOrigin.Current);
        }
    }
}
