using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CharacterBuilderLoader
{
    public class PoundTildeStream
    {
        public byte MajorVersion { get; set; }
        public byte MinorVersion { get; set; }
        public int HeapOffsetSizes { get; set; }
        public long Valid { get; set; }
        public long Sorted { get; set; }
        public List<MethodDef> Methods { get; set; }


        private int stringIndexSize = 2;
        private int guidIndexSize = 2;
        private int blobIndexSize = 2;

        private const int MODULE_BIT        = 0x01;
        private const int TYPE_REF_BIT      = 0x02;
        private const int TYPE_DEF_BIT      = 0x04;
        private const int FIELD_BIT         = 0x10;
        private const int METHOD_DEF_BIT    = 0x40;

        public PoundTildeStream(FileStream fs)
        {
            readHeader(fs);

            readTable(fs);
        }

        private void readTable(FileStream fs)
        {
            // doing just enough here to get to the method defs
            int moduleRows = getRowCount(fs, MODULE_BIT);
            int typeRefRows = getRowCount(fs, TYPE_REF_BIT);
            int typeDefRows = getRowCount(fs, TYPE_DEF_BIT);
            int fieldRows = getRowCount(fs, FIELD_BIT);
            int methodDefRows = getRowCount(fs, METHOD_DEF_BIT);

            // count off the rest of the fields and seek past
            int bitCount = 0;
            long validCopy = Valid >> 7;
            while (validCopy > 0)
            {
                if ((validCopy & 1) > 0)
                    bitCount++;
                validCopy >>= 1;
            }
            // now read off the rest of the counts
            fs.Seek(bitCount * 4, SeekOrigin.Current);

            // read past the non-method rows
            fs.Seek(getModuleSize() * moduleRows, SeekOrigin.Current);
            fs.Seek(getTypeRefSize() * typeRefRows, SeekOrigin.Current);
            fs.Seek(getTypeDefSize() * typeDefRows, SeekOrigin.Current);
            fs.Seek(getFieldSize() * fieldRows, SeekOrigin.Current);
            // now read the methods
            Methods = new List<MethodDef>();
            for (int i = 0; i < methodDefRows; i++)
            {
                // TODO: the paramIndexSize shouldn't be a constant 2
                Methods.Add(new MethodDef(fs,stringIndexSize,blobIndexSize,2, i+1));
            }
        }

        public void FillMethods(FileStream fs,MetaDataStream Strings, PEFile file)
        {
            // now fill in the methods
            foreach (MethodDef md in Methods)
                md.Populate(fs, Strings,file );
        }

        private int getModuleSize()
        {
            return 2 + stringIndexSize + guidIndexSize + guidIndexSize + guidIndexSize;
        }

        private int getTypeRefSize()
        {
            // TODO: Cheating here, this should be a resolutionscope size, not a hardcoded 2
            return 2 + stringIndexSize + stringIndexSize;
        }

        private int getTypeDefSize()
        {
            // TODO: more cheating 
            //  the first '2' should be a TypeDefOrRef index 
            //  the 2nd '2' should be a Field table index
            //  the 3rd '2' should be a MethodDef index
            return 4 + stringIndexSize + stringIndexSize + 2 + 2 + 2;
        }

        private int getFieldSize()
        {
            return 2 + stringIndexSize + blobIndexSize;
        }

        private void readHeader(FileStream fs)
        {
            //reserved
            fs.Seek(4, SeekOrigin.Current);

            MajorVersion = (byte)fs.ReadByte();
            MinorVersion = (byte)fs.ReadByte();

            HeapOffsetSizes = fs.ReadByte();
            stringIndexSize = (HeapOffsetSizes & 1) != 0 ? 4 : 2;
            guidIndexSize = (HeapOffsetSizes & 2) != 0 ? 4 : 2;
            blobIndexSize = (HeapOffsetSizes & 4) != 0 ? 4 : 2;

            //reserved
            fs.Seek(1, SeekOrigin.Current);
            Valid = Utils.ReadLong(fs);
            Sorted = Utils.ReadLong(fs);
        }

        private int getRowCount(FileStream fs, int bit)
        {
            if ((Valid & bit) > 0)
                return Utils.ReadInt(fs);
            else return 0;
        }
    }
}
