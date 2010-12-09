using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CharacterBuilderLoader
{
    public class PESection
    {
        public string SectionName { get; set; }
        public int VirtualSize { get; set; }
        public int VirtualAddress { get; set; }
        public int SizeOfRawData {get;set;}
        public int PointerToRawData { get; set; }
        public byte[] Data { get; private set; }
        public PESection(FileStream fs)
        {
            SectionName = Utils.ReadString(fs, 8);
            VirtualSize = Utils.ReadInt(fs);
            VirtualAddress = Utils.ReadInt(fs);
            SizeOfRawData = Utils.ReadInt(fs);
            PointerToRawData = Utils.ReadInt(fs);

            // skips
            fs.Seek(16, SeekOrigin.Current);
        }

        public void LoadData(FileStream fs)
        {
            Data = new byte[SizeOfRawData];
            fs.Seek(PointerToRawData, SeekOrigin.Begin);
            fs.Read(Data, 0, SizeOfRawData);
        }
        public bool FileOffsetIsInThisSection(int fileOffset)
        {
            return PointerToRawData <= fileOffset && PointerToRawData + SizeOfRawData > fileOffset;
        }

        public bool IsInThisSection(int RVA)
        {
            return RVA >= VirtualAddress && RVA <= VirtualAddress + VirtualSize;
        }

        public int CalculateFileOffset(int RVA)
        {
            return RVA - VirtualAddress + PointerToRawData;
        }

        public int CalculateRVA(int fileOffset)
        {
            return (fileOffset - PointerToRawData) + VirtualAddress;
        }
    }
}
