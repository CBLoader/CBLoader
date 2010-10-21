using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CharacterBuilderLoader
{
    class TableInfo
    {
        public MetaTableID ID { get; set; }
        public int RowCount { get; set; }
        public int RowSize { get; private set; }
        public Dictionary<MetaTableID, TableInfo> tableCounts {get;set;}
        /// <summary>
        /// This should be a set of subclasses. Too lazy.
        /// </summary>
        /// <param name="tableCounts"></param>
        public void SetRowSize(int stringIndexSize, int guidIndexSize, int blobIndexSize)
        {
            switch (ID)
            {
                case MetaTableID.Module:
                    RowSize = 2 + stringIndexSize + guidIndexSize + guidIndexSize + guidIndexSize;
                    break;
                case MetaTableID.TypeRef:
                    RowSize = getResolutionScopeSize() + stringIndexSize + stringIndexSize;
                    break;
                case MetaTableID.TypeDef:
                    RowSize = 4 + stringIndexSize + stringIndexSize + getTypeDefOrRefSize() + GetRefSize(MetaTableID.Field) + GetRefSize(MetaTableID.MethodDef);
                    break;
                case MetaTableID.Field:
                    RowSize = 2 + stringIndexSize + blobIndexSize;
                    break;
                case MetaTableID.MethodDef:
                    RowSize = 4 + 2 + 2 + stringIndexSize + blobIndexSize + GetRefSize(MetaTableID.Param);
                    break;
                case MetaTableID.Param:
                    RowSize = 2 + 2 + stringIndexSize;
                    break;
                case MetaTableID.InterfaceImpl:
                    RowSize = GetRefSize(MetaTableID.TypeDef) + getTypeDefOrRefSize();
                    break;
                case MetaTableID.MemberRef:
                    RowSize = getMemberRefParentSize() + stringIndexSize + blobIndexSize;
                    break;
                case MetaTableID.Constant:
                    RowSize = 1 + 1 + getHasConstantSize() + blobIndexSize;
                    break;
                case MetaTableID.CustomAttribute:
                    RowSize = getHasCustomAttributeSize() + getCustomTypeAttributeSize() + blobIndexSize;
                    break;
                case MetaTableID.FieldMarshal:
                    RowSize = getHasFieldMarshalSize() + blobIndexSize;
                    break;
                case MetaTableID.DeclSecurity:
                    RowSize = 2 + getHasDeclSecuritySize() + blobIndexSize;
                    break;
                case MetaTableID.ClassLayout:
                    RowSize = 2 + 4 + GetRefSize(MetaTableID.TypeDef);
                    break;
                case MetaTableID.FieldLayout:
                    RowSize = 4 + GetRefSize(MetaTableID.Field);
                    break;
                case MetaTableID.StandAloneSig:
                    RowSize = blobIndexSize;
                    break;
                case MetaTableID.EventMap:
                    RowSize = GetRefSize(MetaTableID.TypeDef) + GetRefSize(MetaTableID.Event);
                    break;
                case MetaTableID.Event:
                    RowSize = 2 + stringIndexSize + getTypeDefOrRefSize();
                    break;
                case MetaTableID.PropertyMap:
                    RowSize = GetRefSize(MetaTableID.TypeDef) + GetRefSize(MetaTableID.Property);
                    break;
                case MetaTableID.Property:
                    RowSize = 2 + stringIndexSize + blobIndexSize;
                    break;
                case MetaTableID.MethodSemantics:
                    RowSize = 2 + GetRefSize(MetaTableID.MethodDef) + getHasSemanticsSize();
                    break;
                case MetaTableID.MethodImpl:
                    RowSize = GetRefSize(MetaTableID.TypeDef) + getMethodDefOrRefSize() * 2;
                    break;
                case MetaTableID.ModuleRef:
                    RowSize = stringIndexSize;
                    break;
                case MetaTableID.TypeSpec:
                    RowSize = blobIndexSize;
                    break;
                case MetaTableID.ImplMap:
                    RowSize = 2 + getMemberForwardedSize() + stringIndexSize + GetRefSize(MetaTableID.ModuleRef);
                    break;
                case MetaTableID.FieldRVA:
                    RowSize = 4 + GetRefSize(MetaTableID.Field);
                    break;
                default:
                    // TODO: finish mapping out the rows.
                    break;            
            }
        }

        private int getMemberForwardedSize()
        {
            return getComboRefSize(1, MetaTableID.MethodDef, MetaTableID.Field);
        }
        private int getMethodDefOrRefSize()
        {
            return getComboRefSize(1, MetaTableID.MethodDef, MetaTableID.MemberRef);
        }

        private int getHasSemanticsSize()
        {
            return getComboRefSize(1, MetaTableID.Event, MetaTableID.Property);
        }
        private int getHasDeclSecuritySize()
        {
            return getComboRefSize(2, MetaTableID.TypeDef, MetaTableID.MethodDef, MetaTableID.Assembly);
        }

        private int getHasFieldMarshalSize()
        {
            return getComboRefSize(1, MetaTableID.Field, MetaTableID.Param);
        }


        private int getCustomTypeAttributeSize()
        {
            return getComboRefSize(3, MetaTableID.MethodDef, MetaTableID.MemberRef);
        }

        private int getHasCustomAttributeSize()
        {
            int max = 0;
            foreach (KeyValuePair<MetaTableID, TableInfo> table in tableCounts)
            {
                if (table.Key == MetaTableID.CustomAttribute)
                    continue;
                if (table.Value.RowCount > max)
                    max = table.Value.RowCount;
            }
            return UInt16.MaxValue >> 5 < max ? 4 : 2;
        }

        private int getHasConstantSize()
        {
            return getComboRefSize(2, MetaTableID.Param, MetaTableID.Field, MetaTableID.Property);
        }
        private int getMemberRefParentSize()
        {
            return getComboRefSize(3, MetaTableID.TypeRef, MetaTableID.ModuleRef, MetaTableID.MethodDef, MetaTableID.TypeSpec,
                MetaTableID.TypeDef);
        }

        private int getTypeDefOrRefSize()
        {
            return getComboRefSize(2, MetaTableID.TypeDef, MetaTableID.TypeRef, MetaTableID.TypeSpec);
        }

        private int getResolutionScopeSize()
        {
            return getComboRefSize(2, MetaTableID.Module, MetaTableID.ModuleRef, MetaTableID.AssemblyRef, MetaTableID.TypeRef);
        }

        private int getComboRefSize(int bitsUsed, params MetaTableID[] ids)
        {
            int maxVal = 0;
            foreach(MetaTableID id in ids)
                if(tableCounts.ContainsKey(id))
                    if(tableCounts[id].RowCount > maxVal)
                        maxVal = tableCounts[id].RowCount;
            return UInt16.MaxValue >> bitsUsed < maxVal ? 4 : 2;
        }

        public int GetRefSize(MetaTableID id)
        {
            return tableCounts[ID].RowCount > 0xFFFF ? 4 : 2;
        }


    }
}
