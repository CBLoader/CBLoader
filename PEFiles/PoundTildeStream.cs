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
        public List<FieldDef> FieldRVAs { get; set; }


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
            long[] tableKeys = tableNums.Select(i => (long)1 << i).ToArray();
            List<TableInfo> tableInfo = new List<TableInfo>();

            // get the count of each object
            for(int i = 0; i < tableNums.Length; i++)
            {
                tableInfo.Add(new TableInfo(){
                   ID = (MetaTableID)tableNums[i],
                   RowCount = getRowCount(fs,tableKeys[i])
                });
            }
            int count = tableInfo.Count(ti => ti.RowCount > 0);
            tableInfoLkp = tableInfo.ToDictionary(ti => ti.ID);
            // add the size function for each table.
            foreach(TableInfo table in tableInfo) {
                table.tableCounts = tableInfoLkp;
                table.SetRowSize(stringIndexSize, guidIndexSize, blobIndexSize);

                if (table.ID == MetaTableID.MethodDef)
                {
                    Methods = new List<MethodDef>();
                    for (int i = 0; i < table.RowCount; i++)
                    {
                        // TODO: the paramIndexSize shouldn't be a constant 2
                        Methods.Add(new MethodDef(fs, stringIndexSize, blobIndexSize, table.GetRefSize(MetaTableID.Param), i + 1));
                    }
                }
                else if (table.ID == MetaTableID.FieldRVA)
                {
                    FieldRVAs  = new List<FieldDef>();
                    for(int i = 0; i < table.RowCount; i++) {
                        FieldRVAs.Add(new FieldDef(fs, table));
                    }
                }
                else
                    fs.Seek(table.RowCount*table.RowSize, SeekOrigin.Current);
            }
        }



        public void FillMethods(FileStream fs,MetaDataStream Strings, PEFile file)
        {
            // now fill in the methods
            foreach (MethodDef md in Methods)
                md.Populate(fs, Strings,file );
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

        private int getRowCount(FileStream fs, long bit)
        {
            if ((Valid & bit) > 0)
                return Utils.ReadInt(fs);
            else return 0;
        }
    }
}
