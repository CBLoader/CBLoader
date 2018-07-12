using System;
using System.Xml.Linq;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace CBLoader
{
    internal static class IListRangeOps
    {
        public static void AddRange<T>(this IList<T> list, IEnumerable<T> items)
        {
            if (list is List<T>)
                ((List<T>)list).AddRange(items);
            else foreach (var item in items)
                list.Add(item);
        }

        public static void InsertRange<T>(this IList<T> list, int start, IEnumerable<T> items)
        {
            if (list is List<T>)
                ((List<T>)list).InsertRange(start, items);
            else foreach (var item in items)
            {
                list.Insert(start, item);
                start++;
            }
        }
    }

    internal static class ConsoleWindow
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("oleacc.dll")]
        private static extern IntPtr GetProcessHandleFromHwnd(IntPtr hWnd);

        [DllImport("kernel32.dll")]
        private static extern int GetProcessId(IntPtr process);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private static bool ownsConsole()
        {
            var console = GetConsoleWindow();
            if (console.ToInt64() == 0) return false;
            var process = GetProcessHandleFromHwnd(console);
            if (process.ToInt64() == 0) return false;
            return GetProcessId(process) == Process.GetCurrentProcess().Id;
        }

        public static readonly bool IsInIndependentConsole = 
            Utils.IS_WINDOWS && ownsConsole();

        public static void SetConsoleShown(bool show)
        {
            if (!Utils.IS_WINDOWS) return;
            if (!IsInIndependentConsole) return;

            var console = GetConsoleWindow();
            if (console.ToInt64() == 0) return;
            ShowWindow(console, show ? SW_SHOW : SW_HIDE);
        }
    }

    internal static class Utils
    {
        internal static bool IS_WINDOWS =
            Environment.OSVersion.Platform == PlatformID.Win32NT ||
            Environment.OSVersion.Platform == PlatformID.Win32S ||
            Environment.OSVersion.Platform == PlatformID.Win32Windows;

        private static string CB_INSTALL_ID = "{626C034B-50B8-47BD-AF93-EEFD0FA78FF4}";
        public static string GetInstallPath()
        {
            if (!IS_WINDOWS) return null;

            var reg = Registry.LocalMachine
                .OpenSubKey($@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{CB_INSTALL_ID}");
            if (reg == null) return null;
            else return reg.GetValue("InstallLocation").ToString();
        }

        public static void ExtractKeyFile(string filename)
        {
            /*
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
            */
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
