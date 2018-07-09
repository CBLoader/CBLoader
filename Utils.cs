using System;
using System.Xml.Linq;
using Microsoft.Win32;
using System.Security.Cryptography;

namespace CharacterBuilderLoader
{
    public class Utils
    {
        public static void ExtractKeyFile(string filename)
        {
            RegistryKey rk = Registry.LocalMachine.OpenSubKey("SOFTWARE").OpenSubKey("Wizards of the Coast")
                .OpenSubKey(CryptoUtils.CB_APP_ID.ToString());
            string currentVersion = rk.GetValue(null).ToString();
            string encryptedKey = rk.OpenSubKey(currentVersion).GetValue(null).ToString();
            byte[] stuff = new byte[] { 0x19, 0x25, 0x49, 0x62, 12, 0x41, 0x55, 0x1c, 0x15, 0x2f };
            byte[] base64Str = Convert.FromBase64String(encryptedKey);
            string realKey = Convert.ToBase64String(ProtectedData.Unprotect(base64Str, stuff, DataProtectionScope.LocalMachine));
            XDocument xd = new XDocument();
            XElement applications = new XElement("Applications");
            XElement application = new XElement("Application");
            application.Add(new XAttribute("ID", CryptoUtils.CB_APP_ID.ToString()));
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

        public static void CheckIfUserAssoc()
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software").OpenSubKey("Classes").OpenSubKey(".dnd4e");
            if (key == null || key.OpenSubKey("shell") == null)
                UpdateRegistry(true);
        }

        /// <summary>
        /// Sets .dnd4e File Association to CBLoader.
        /// This means that the user can double-click a character file and launch CBLoader.
        /// </summary>
        public static void UpdateRegistry(bool silent = false)
        { // I'm not going to bother explaining File Associations. Either look it up yourself, or trust me that it works.
            try // Changing HKCL needs admin permissions
            {
                
                Microsoft.Win32.RegistryKey cuClasses = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("Classes");
                var k = cuClasses.CreateSubKey(".dnd4e");
                k.SetValue("", ".dnd4e");
                k = k.CreateSubKey("shell");
                k = k.CreateSubKey("open");
                k = k.CreateSubKey("command");
                k.SetValue("", "\"" + (Environment.CurrentDirectory.ToString() + "\\CBLoader.exe\" \"%1\""));
                // All Users
                k = Microsoft.Win32.Registry.ClassesRoot.CreateSubKey(".dnd4e");
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
                if (!silent)
                Log.Error("There was a problem setting file associations", ua);
            }
        }
    }
}
