using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace CharacterBuilderLoader
{
    [Flags]
    public enum AllocationType
    {
        Commit = 0x1000,
        Reserve = 0x2000,
        Decommit = 0x4000,
        Release = 0x8000,
        Reset = 0x80000,
        Physical = 0x400000,
        TopDown = 0x100000,
        WriteWatch = 0x200000,
        LargePages = 0x20000000
    }

    [Flags]
    public enum MemoryProtection
    {
        Execute = 0x10,
        ExecuteRead = 0x20,
        ExecuteReadWrite = 0x40,
        ExecuteWriteCopy = 0x80,
        NoAccess = 0x01,
        ReadOnly = 0x02,
        ReadWrite = 0x04,
        WriteCopy = 0x08,
        GuardModifierflag = 0x100,
        NoCacheModifierflag = 0x200,
        WriteCombineModifierflag = 0x400
    }
    public class Utils
    {
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress,
           uint dwSize, AllocationType flAllocationType, MemoryProtection flProtect);

        private static byte[] buffer = new byte[1024];

        /// <summary>
        /// Reads a null terminated string
        /// </summary>
        /// <param name="fs"></param>
        /// <returns></returns>
        public static string ReadString(FileStream fs)
        {
            char c;
            String retVal = "";
            while ((c = (char)fs.ReadByte()) != '\0')
            {
                retVal += c;
            }
            return retVal;
        }

        public static string ReadString(byte[] data, int offset)
        {
            char c;
            String retVal = "";
            while (offset < data.Length && (c = (char)data[offset]) != '\0')
            {
                retVal += c;
                offset++;
            }
            return retVal;
        }

        public static string ReadString(FileStream fs, int length)
        {
            fs.Read(buffer, 0, length);
            return System.Text.ASCIIEncoding.ASCII.GetString(buffer, 0, 8);

        }

        public static short ReadShort(FileStream fs)
        {
            fs.Read(buffer, 0, 2);
            return BitConverter.ToInt16(buffer, 0);
        }

        public static int ReadInt(FileStream fs)
        {
            fs.Read(buffer, 0, 4);
            return BitConverter.ToInt32(buffer, 0);
        }
        public static long ReadLong(FileStream fs)
        {
            fs.Read(buffer, 0, 8);
            return BitConverter.ToInt64(buffer, 0);
        }

    }
}
