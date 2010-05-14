using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CharacterBuilderLoader
{
    public class Method
    {
        public int HeaderFlags1 { get; set; }
        public int HeaderSizeInDWords { get; set; }
        public short MaxStack { get; set; }
        public int CodeSize { get; set; }
        public int LocalVarSigTok { get; set; }
        public byte[] Code { get; set; }
        public Method(FileStream fs)
        {
            HeaderFlags1 = fs.ReadByte();
            // only use the lower 4 bits for the size
            int b = fs.ReadByte();
            HeaderSizeInDWords =  b >> 4;

            // I can't handle small headers yet
            if (HeaderFlags1 != 0x03 && HeaderFlags1 != 0x0B)
                return;
            MaxStack = Utils.ReadShort(fs);
            CodeSize = Utils.ReadInt(fs);
            LocalVarSigTok = Utils.ReadInt(fs);
            Code = new byte[CodeSize];
            fs.Read(Code, 0, CodeSize);
        }
    }
}
