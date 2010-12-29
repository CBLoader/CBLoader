using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CharacterBuilderLoader
{
    public class PEFileHeader
    {
        public int Offset { get; set; }
        public short NumSections { get; set; }
        public int OptionalHeaderSize { get; set; }

        public PEFileHeader(int offset,FileStream fs)
        {
            this.Offset = offset;
            // skip past header items we don't care about
            fs.Seek(6, SeekOrigin.Current);
            NumSections = Utils.ReadShort(fs);
            // find the size of the optional header from the file header
            fs.Seek(12, SeekOrigin.Current);
            OptionalHeaderSize = Utils.ReadShort(fs);
            fs.Seek(2, SeekOrigin.Current);
        }
    }
}
