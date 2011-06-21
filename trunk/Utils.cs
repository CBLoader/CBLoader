using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.Win32;
using System.Security.Cryptography;

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

        public static void ExtractKeyFile(string filename)
        {
            RegistryKey rk = Registry.LocalMachine.OpenSubKey("SOFTWARE").OpenSubKey("Wizards of the Coast")
                .OpenSubKey(FileManager.APPLICATION_ID);
            string currentVersion = rk.GetValue(null).ToString();
            string encryptedKey = rk.OpenSubKey(currentVersion).GetValue(null).ToString();
            byte[] stuff = new byte[] { 0x19, 0x25, 0x49, 0x62, 12, 0x41, 0x55, 0x1c, 0x15, 0x2f };
            byte[] base64Str = Convert.FromBase64String(encryptedKey);
            string realKey = Convert.ToBase64String(ProtectedData.Unprotect(base64Str, stuff, DataProtectionScope.LocalMachine));
            XDocument xd = new XDocument();
            XElement applications = new XElement("Applications");
            XElement application = new XElement("Application");
            application.Add(new XAttribute("ID", FileManager.APPLICATION_ID));
            application.Add(new XAttribute("CurrentUpdate", currentVersion));
            application.Add(new XAttribute("InProgress", "true"));
            application.Add(new XAttribute("InstallStage", "Complete"));
            application.Add(new XAttribute("InstallDate", DateTime.Now.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss.fffffffK")));
            XElement update = new XElement("Update" + currentVersion);
            update.Add(realKey);
            application.Add(update);
            applications.Add(application);
            xd.Add(applications);
            xd.Save(filename);
        }


        /// <summary>
        /// Sets .dnd4e File Association to CBLoader.
        /// This means that the user can double-click a character file and launch CBLoader.
        /// </summary>
        public static void UpdateRegistry()
        { // I'm not going to bother explaining File Associations. Either look it up yourself, or trust me that it works.
            try // Changing HKCL needs admin permissions
            {
                Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.ClassesRoot.CreateSubKey(".dnd4e");
                k.SetValue("", ".dnd4e");
                k = k.CreateSubKey("shell");
                k = k.CreateSubKey("open");
                k = k.CreateSubKey("command");
                k.SetValue("", "\"" + (Environment.CurrentDirectory.ToString() + "\\CBLoader.exe\" \"%1\""));
                // And the cbconfig files
                k = Microsoft.Win32.Registry.ClassesRoot.CreateSubKey(".cbconfig");
                k.SetValue("", ".cbconfig");
                k = k.CreateSubKey("shell");
                k = k.CreateSubKey("open");
                k = k.CreateSubKey("command");
                k.SetValue("", "\"" + (Environment.CurrentDirectory.ToString() + "\\CBLoader.exe\" -c \"%1\""));
            }
            catch (UnauthorizedAccessException ua)
            {
                Log.Error("There was a problem setting file associations", ua);
            }
        }
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
