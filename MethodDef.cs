using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CharacterBuilderLoader
{
    public class MethodDef
    {
        public int RVA { get; set; }
        public short ImplFlags { get; set; }
        public short Flags { get; set; }
        public int NameIndex { get; set; }
        public int SigIndex { get; set; }
        public int ParamIndex { get; set; }

        public const int TOKEN_BIT = 0x06;

        public string Name { get; set; }
        public Method Method { get; set; }
        public int Index { get; set; }
        
        public MethodDef(FileStream fs, int stringIndexSize, int blobIndexSize, int paramIndexSize, int index)
        {
            RVA = Utils.ReadInt(fs);
            ImplFlags = Utils.ReadShort(fs);
            Flags = Utils.ReadShort(fs);
            if (stringIndexSize == 2)
                NameIndex = Utils.ReadShort(fs);
            else
                NameIndex = Utils.ReadInt(fs);
            if (blobIndexSize == 2)
                SigIndex = Utils.ReadShort(fs);
            else
                SigIndex = Utils.ReadInt(fs);
            if (paramIndexSize == 2)
                ParamIndex = Utils.ReadShort(fs);
            else
                ParamIndex = Utils.ReadInt(fs);
            this.Index = index;
        }

        public void Populate(FileStream fs, MetaDataStream Strings, PEFile file)
        {
            this.Name = Utils.ReadString((byte[])Strings.Data, NameIndex);
            if (RVA != 0)
            {
                int offset = file.FindSectionForRVA(RVA).CalculateFileOffset(RVA);
                fs.Seek(offset, SeekOrigin.Begin);
                Method = new Method(fs);
            }

        }

        public int GetByteOffset(PEFile file, int codeOffset)
        {
            return RVA + Method.HeaderSizeInDWords * 4 + codeOffset;
        }

        public void ChangeByte(FileStream fs, PEFile file, int codeIndex, byte newByte)
        {
            int fileOffset = file.RVAToFileOffset(RVA + Method.HeaderSizeInDWords * 4 + codeIndex);
            fs.Seek(fileOffset, SeekOrigin.Begin);
            fs.WriteByte(newByte);
            
        }

    }
}
