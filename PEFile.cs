using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CharacterBuilderLoader
{
    public enum MetaDataTokens
    {
        MethodDef = 0x06
    }
    public class PEFile
    {
        private int fileHeaderOffset;
        public PEFileHeader FileHeader { get; set; }
        public List<PESection> Sections { get; set; }
        public PEOptionalHeader OptionalHeader { get; set; }

        public CLRDirectory CLRDirectory { get; set; }

        public byte[] GetMetaDataToken(MethodDef md)
        {
            return BitConverter.GetBytes(MethodDef.TOKEN_BIT << 24 | md.Index);
        }

        public int RVAToFileOffset(int RVA)
        {
            return FindSectionForRVA(RVA).CalculateFileOffset(RVA);
        }

        public PESection FindSectionForRVA(int RVA)
        {
            return Sections.FirstOrDefault(s => s.IsInThisSection(RVA));

        }

        public PEFile(string fileName)
        {
            using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read))
            {

                fs.Seek(0x3C, SeekOrigin.Begin);
                fileHeaderOffset = Utils.ReadInt(fs);
                fs.Seek(fileHeaderOffset, SeekOrigin.Begin);
                FileHeader = new PEFileHeader(fileHeaderOffset, fs);
                OptionalHeader = new PEOptionalHeader(fs);

                // read the sections
                Sections = new List<PESection>();
                for (int i = 0; i < FileHeader.NumSections; i++)
                    Sections.Add(new PESection(fs));

                // Find .text section and load the data
                PESection textSection = Sections.FirstOrDefault(s => s.SectionName.StartsWith(".text"));
                textSection.LoadData(fs);

                CLRDirectory = new CLRDirectory(fs, this);
            }
        }

        public MethodDef FindCodeForMethodName(String methodName)
        {
            return ((PoundTildeStream)CLRDirectory.Tables.Data).Methods
                .FirstOrDefault(md => md.Name == methodName);
        }



    }
}
