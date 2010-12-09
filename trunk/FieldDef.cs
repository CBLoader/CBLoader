using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CharacterBuilderLoader
{
    public class FieldDef
    {
        public const int TOKEN_BIT = 0x04;
        public long MetaDataFileLocation { get; set; }
        public int FieldIndex { get; set; }
        public int RVA { get; set; }
        internal FieldDef(FileStream fs, TableInfo table)
        {
            MetaDataFileLocation = fs.Position;
            RVA = Utils.ReadInt(fs);
            
            // skip past the actual field index
            int fieldSize = table.GetRefSize(MetaTableID.Field);
            FieldIndex = fieldSize == 2 ? Utils.ReadShort(fs) : Utils.ReadInt(fs);
        }
    }
}
