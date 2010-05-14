using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CharacterBuilderLoader
{
    public class Utils
    {
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
