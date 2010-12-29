using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CharacterBuilderLoader
{
    public class CLRDirectory
    {
        public PEDataDirectory MetDataDirectory { get; set; }
        public int MetaDataOffset { get; set; }
        public int MetaDataHeaderRVF { get; set; }
        public MetaDataHeader MetaDataHeader { get; set; }
        public List<MetaDataStream> MetaDataStreams { get; private set; }
        public MetaDataStream Tables { get; set; }
        public MetaDataStream Strings { get; set; }
        public CLRDirectory(FileStream fs, PEFile parent)
        {
            // Find the .NET metadata and load it
            PEDataDirectory clrDir =
                parent.OptionalHeader.DataDirectories.FirstOrDefault(
                    p => p.DirectoryName == PEOptionalHeader.CLR_RUNTIME_HEADER);
            PESection section = parent.FindSectionForRVA(clrDir.VirtualAddress);

            MetaDataOffset = section.CalculateFileOffset(clrDir.VirtualAddress);
            // Skip to the start of the structure
            fs.Seek(MetaDataOffset, SeekOrigin.Begin);
            fs.Seek(8, SeekOrigin.Current);
            MetDataDirectory = new PEDataDirectory(fs, "MetaData");

            MetaDataHeaderRVF = 
                parent.FindSectionForRVA(MetDataDirectory.VirtualAddress)
                    .CalculateFileOffset(MetDataDirectory.VirtualAddress);
            fs.Seek(MetaDataHeaderRVF,SeekOrigin.Begin);
            // load the metadata itself
            MetaDataHeader = new MetaDataHeader(fs);
            MetaDataStreams = new List<MetaDataStream>();
            for(int i =0; i< MetaDataHeader.NumberOfStreams; i++)  {
                MetaDataStreams.Add(new MetaDataStream(fs));
            }
            // we should now be aligned at the start of the first section
            foreach (MetaDataStream ms in MetaDataStreams)
            {
                fs.Seek(ms.Offset + MetaDataHeaderRVF, SeekOrigin.Begin);

                if (ms.Name == "#~")
                {
                    ms.Data = new PoundTildeStream(fs);
                    Tables = ms;
                }
                else
                {
                    if (ms.Name == "#Strings")
                        Strings = ms;
                    ms.Data = new byte[ms.Size];
                    fs.Read((byte[])ms.Data, 0, ms.Size);
                }
            }
            ((PoundTildeStream)Tables.Data).FillMethods(fs, Strings, parent);

        }

    }
}
