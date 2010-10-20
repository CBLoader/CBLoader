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
        private Dictionary<MetaTableID,TableInfo> tableInfoLkp;


        public PoundTildeStream(FileStream fs)
        {
            readHeader(fs);

            readTable(fs);
        }

        private void readTable(FileStream fs)
        {
            // determine the number of rows in each field
            int[] tableNums = ((int[])Enum.GetValues(typeof(MetaTableID)));
            int[] tableKeys = tableNums.Select(i => 1 << i).ToArray();
            List<TableInfo> tableInfo = new List<TableInfo>();
            for(int i = 0; i < tableNums.Length; i++)
            {
                tableInfo.Add(new TableInfo(){
                   ID = (MetaTableID)tableNums[i],
                   RowCount = getRowCount(fs,tableKeys[i])
                });
            }
            tableInfoLkp = tableInfo.ToDictionary(ti => ti.ID);
            
            foreach(TableInfo table in tableInfo) {

            }
            // read past the non-method rows
            //fs.Seek(getModuleSize() * moduleRows, SeekOrigin.Current);
            //fs.Seek(getTypeRefSize() * typeRefRows, SeekOrigin.Current);
            //fs.Seek(getTypeDefSize() * typeDefRows, SeekOrigin.Current);
            //fs.Seek(getFieldSize() * fieldRows, SeekOrigin.Current);
            // now read the methods
        //    Methods = new List<MethodDef>();
        //    for (int i = 0; i < methodDefRows; i++)
        //    {
        //        // TODO: the paramIndexSize shouldn't be a constant 2
        //        Methods.Add(new MethodDef(fs,stringIndexSize,blobIndexSize,2, i+1));
        //    }
        }

        private int getComboRefSize(params MetaTableID[] ids)
        {
            int bitsUsed = ids.Length;
            return 0;
        }

        private int getRefSize(MetaTableID id)
        {
            return tableInfoLkp[id].RowCount > 0xFFFF ? 4 : 2;
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
