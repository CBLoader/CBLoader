using System;
using System.Xml.Linq;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Permissions;

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

    /// <summary>
    /// Normally objects with MarshalByRefObject time out after 6 minutes (!!). This prevents that
    /// from happening. Since we only ever create two remote objects, this isn't an issue.
    /// </summary>
    internal abstract class PersistantRemoteObject : MarshalByRefObject
    {
        [SecurityPermissionAttribute(SecurityAction.Demand, Flags = SecurityPermissionFlag.Infrastructure)]
        public override object InitializeLifetimeService()
        {
            return null;
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

        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private static void createAssociation(string uniqueId, string extension, string flags, string friendlyName)
        {
            var cuClasses = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("Classes");

            {
                if (cuClasses.OpenSubKey(uniqueId) != null)
                    cuClasses.DeleteSubKeyTree(uniqueId);

                var progIdKey = cuClasses.CreateSubKey(uniqueId);
                progIdKey.SetValue("", friendlyName);
                progIdKey.SetValue("FriendlyTypeName", friendlyName);
                progIdKey.CreateSubKey("CurVer").SetValue("", uniqueId);
                addAction(uniqueId, "open", $"\"{Path.GetFullPath(Assembly.GetEntryAssembly().Location)}\" {flags} \"%1\"");
            }

            {
                if (cuClasses.OpenSubKey(extension) != null)
                    cuClasses.DeleteSubKeyTree(extension);

                var progIdKey = cuClasses.CreateSubKey(extension);
                progIdKey.SetValue("", uniqueId);
                progIdKey.SetValue("PerceivedType", "Document");
            }
        }
        private static void addAction(string uniqueId, string actionName, string invoke)
        {
            var cuClasses = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("Classes");
            var progIdKey = cuClasses.CreateSubKey(uniqueId);
            progIdKey
                .CreateSubKey("shell").CreateSubKey(actionName).CreateSubKey("command")
                .SetValue("", invoke);
        }

        /// <summary>
        /// Sets .dnd4e File Association to CBLoader.
        /// This means that the user can double-click a character file and launch CBLoader.
        /// </summary>
        public static void UpdateRegistry()
        {
            if (!IS_WINDOWS) return;

            Log.Info("Setting file associations.");
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            createAssociation("CBLoader.dnd4e.1", ".dnd4e", "--",
                              "D&D Insider Character Builder file");
            createAssociation("CBLoader.cbconfig.1", ".cbconfig", "-c",
                              "CBLoader configuration file");
            addAction("CBLoader.cbconfig.1", "Edit CBLoader Configuration", 
                      "notepad.exe \"%1\"");
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);

            stopwatch.Stop();
            Log.Debug($"Finished in {stopwatch.ElapsedMilliseconds} ms");
            Log.Debug();
        }

        public static bool IsFilenameValid(string filename) =>
            filename != "." && filename != ".." &&
            filename.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        public static string NormalizeLineEndings(string data) =>
            Regex.Replace(data, @"\r\n?|\n", "\r\n");
        public static string StripBOM(string data) =>
            data.Length > 0 && data[0] == '\uFEFF' ? data.Substring(1, data.Length - 1) : data;
        public static string ParseUTF8(byte[] data) =>
            StripBOM(Encoding.UTF8.GetString(data));
        public static string HashFile(string filename) =>
            Convert.ToBase64String(SHA256.Create().ComputeHash(File.ReadAllBytes(filename)));
    }
}
