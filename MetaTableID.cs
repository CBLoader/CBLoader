using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CharacterBuilderLoader
{
    public enum MetaTableID
    {
        Module = 0,
        TypeRef = 01,
        TypeDef = 02,
        Field = 04,
        MethodDef = 06,
        Param = 08,
        InterfaceImpl = 09,
        MemberRef = 10,
        Constant = 11,
        CustomAttribute = 12,
        FieldMarshal = 13,
        DeclSecurity = 14,
        ClassLayout = 15,
        FieldLayout = 16,
        StandAloneSig = 17,
        EventMap = 18,
        Event = 20,
        PropertyMap = 21,
        Property = 23,
        MethodSemantics = 24,
        MethodImpl = 25,
        ModuleRef = 26,
        TypeSpec = 27,
        ImplMap = 28,
        FieldRVA = 29,
        Assembly = 32,
        AssemblyProcessor = 33,
        AssemblyOS = 34,
        AssemblyRef = 35,
        AssemblyRefProcessor = 36,
        AssemblyRefOS = 37,
        File = 38,
        ExportedType = 39,
        ManifestResource = 40,
        NestedClass = 41,
        GenericParam = 42,
        MethodSpec = 43,
        GenericParamConstraint = 44
    }
}
