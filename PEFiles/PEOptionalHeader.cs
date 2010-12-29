using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CharacterBuilderLoader
{
    public class PEOptionalHeader
    {
        public const short MAGIC_NUMBER_PE32 = 0x10b;
        public const short MAGIC_NUMBER_PE32_PLUS = 0x20b;


        public short MagicNumber { get; set; }
        public int NumberOfRvAndSizes { get; set; }
        public List<PEDataDirectory> DataDirectories { get; private set; }

        public PEOptionalHeader(FileStream fs) {
            MagicNumber = Utils.ReadShort(fs);

            //Skips
            if (MagicNumber == MAGIC_NUMBER_PE32)
                fs.Seek(90, SeekOrigin.Current);
            else
                fs.Seek(106, SeekOrigin.Current);

            NumberOfRvAndSizes = Utils.ReadInt(fs);
            DataDirectories = new List<PEDataDirectory>();
            if (NumberOfRvAndSizes < 16)
                throw new Exception("Bad format, expected at least 16 directories");
            DataDirectories.Add(new PEDataDirectory(fs,EXPORT_TABLE));
            DataDirectories.Add(new PEDataDirectory(fs,IMPORT_TABLE));
            DataDirectories.Add(new PEDataDirectory(fs,RESOURCE_TABLE));
            DataDirectories.Add(new PEDataDirectory(fs,EXCEPTION_TABLE));
            DataDirectories.Add(new PEDataDirectory(fs,CERTIFICATE_TABLE));
            DataDirectories.Add(new PEDataDirectory(fs,BASE_RELOCATION_TABLE));
            DataDirectories.Add(new PEDataDirectory(fs,DEBUG));
            DataDirectories.Add(new PEDataDirectory(fs,ARCHITECTURE));
            DataDirectories.Add(new PEDataDirectory(fs,GLOBAL_PTR));
            DataDirectories.Add(new PEDataDirectory(fs,TLS_TABLE));
            DataDirectories.Add(new PEDataDirectory(fs,LOAD_CONFIG_TABLE));
            DataDirectories.Add(new PEDataDirectory(fs,BOUND_IMPORT));
            DataDirectories.Add(new PEDataDirectory(fs,IAT));
            DataDirectories.Add(new PEDataDirectory(fs,DELAY_IMPORT_DESCRIPTOR));
            DataDirectories.Add(new PEDataDirectory(fs,CLR_RUNTIME_HEADER));
            DataDirectories.Add(new PEDataDirectory(fs,RESERVED));
            for (int i = 16; i < NumberOfRvAndSizes; i++)
                DataDirectories.Add(new PEDataDirectory(fs, "Unknown"));
        }
        public const string EXPORT_TABLE = "Export Table";
        public const string IMPORT_TABLE = "Import Table";
        public const string RESOURCE_TABLE = "Resource Table";
        public const string EXCEPTION_TABLE = "Exception Table";
        public const string CERTIFICATE_TABLE = "Certificate Table";
        public const string BASE_RELOCATION_TABLE = "Base Relocation Table";
        public const string DEBUG = "Debug";
        public const string ARCHITECTURE = "Architecture";
        public const string GLOBAL_PTR = "Global Ptr";
        public const string TLS_TABLE = "TLS Table";
        public const string LOAD_CONFIG_TABLE = "Load Config Table";
        public const string BOUND_IMPORT = "Bound Import";
        public const string IAT = "IAT";
        public const string DELAY_IMPORT_DESCRIPTOR = "Delay Import Descriptor";
        public const string CLR_RUNTIME_HEADER = "CLR Runtime Header";
        public const string RESERVED = "Reserved";
    }
}
