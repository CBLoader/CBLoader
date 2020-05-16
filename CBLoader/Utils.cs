using System;
using System.Linq;
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
using System.Net;
using System.Security.Authentication;
using System.Windows.Media;

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

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint GetConsoleProcessList(uint[] ProcessList, uint ProcessCount);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private static bool ownsConsole()
        {
            var console = GetConsoleWindow();
            if (console.ToInt64() == 0) return false;
            var process = GetProcessHandleFromHwnd(console);
            if (process.ToInt64() == 0) return false;
            uint[] procIDs = new uint[16];
            var count = GetConsoleProcessList(procIDs, 16);
            Log.Debug($"Console has {count} processes.");
            return count == 1;
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

    internal static partial class Utils
    {
      

        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private static bool MaybeSetValue(this RegistryKey key, string name, object value)
        {
            var v = key.GetValue(name);
            if (!value.Equals(v))
            {
                key.SetValue(name, value);
                return true;
            }
            return false;

        }
        private static bool createAssociation(string uniqueId, string extension, string flags, string friendlyName)
        {
            var cuClasses = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("Classes");
            var dirty = false;

            {
                var progIdKey = cuClasses.CreateSubKey(uniqueId);
                dirty = progIdKey.MaybeSetValue("", friendlyName) || dirty;
                dirty = progIdKey.MaybeSetValue("FriendlyTypeName", friendlyName) || dirty;
                dirty = progIdKey.CreateSubKey("CurVer").MaybeSetValue("", uniqueId) || dirty;
                dirty = addAction(uniqueId, "open", $"\"{Path.GetFullPath(Assembly.GetEntryAssembly().Location)}\" {flags} \"%1\"") || dirty;
            }

            {
                var progIdKey = cuClasses.CreateSubKey(extension);
                dirty = progIdKey.MaybeSetValue("", uniqueId) || dirty;
                dirty = progIdKey.MaybeSetValue("PerceivedType", "Document") || dirty;
            }
            return dirty;
        }

        private static void DeleteAssociation(string extension)
        {
            var cuClasses = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("Classes");
            var progIdKey = cuClasses.OpenSubKey(extension);
            if (progIdKey != null)
            {
                cuClasses.DeleteSubKeyTree(extension);
            }

        }
        private static bool addAction(string uniqueId, string actionName, string invoke)
        {
            var cuClasses = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("Classes");
            var progIdKey = cuClasses.CreateSubKey(uniqueId);
            return progIdKey
                .CreateSubKey("shell").CreateSubKey(actionName).CreateSubKey("command")
                .MaybeSetValue("", invoke);
        }

        /// <summary>
        /// Sets .dnd4e File Association to CBLoader.
        /// This means that the user can double-click a character file and launch CBLoader.
        /// </summary>
        public static void UpdateRegistry()
        {
#if DO_ASSOCIATIONS
            if (!IS_WINDOWS) return;

            Log.Info("Setting file associations.");
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var dirty = false;
            dirty = createAssociation("CBLoader.dnd4e.1", ".dnd4e", "--",
                              "D&D Insider Character Builder file") || dirty;
            dirty = createAssociation("CBLoader.cbconfig.1", ".cbconfig", "-c",
                              "CBLoader configuration file") || dirty;
            var editor = "notepad.exe";
            if (ExistsOnPath("code")) // VS Code
                editor = GetFullPath("code");
            dirty = addAction("CBLoader.cbconfig.1", "Edit CBLoader Configuration", 
                              $"{editor} \"%1\"") || dirty;
            if (dirty)
                SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);

            stopwatch.Stop();
            Log.Debug($"Finished in {stopwatch.ElapsedMilliseconds} ms");
            Log.Debug();
#endif
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
        public static string HashFile(string filename)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return Convert.ToBase64String(sha256.ComputeHash(File.ReadAllBytes(filename)));
            }
        }

        public static bool ExistsOnPath(string fileName) => 
            GetFullPath(fileName) != null;

        public static string GetFullPath(string fileName)
        {
            if (File.Exists(fileName))
                return Path.GetFullPath(fileName);

            var values = Environment.GetEnvironmentVariable("PATH");
            foreach (var path in values.Split(Path.PathSeparator))
            {
                var fullPath = Path.Combine(path, fileName);
                if (File.Exists(fullPath))
                    return fullPath;
            }
            return null;
        }

        public static void CopyAll(string source, string target, bool overwrite = false) =>
            CopyAll(new DirectoryInfo(source), new DirectoryInfo(target), overwrite);

        public static void CopyAll(DirectoryInfo source, DirectoryInfo target, bool overwrite = false)
        {
            Directory.CreateDirectory(target.FullName);

            // Copy each file into the new directory.
            foreach (FileInfo fi in source.GetFiles())
            {
                var targetFile = new FileInfo(Path.Combine(target.FullName, fi.Name));

                if (overwrite || !targetFile.Exists)
                {
                    fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
                }
            }

            // Copy each subdirectory using recursion.
            foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
            {
                var nextTargetSubDir =
                    target.CreateSubdirectory(diSourceSubDir.Name);
                CopyAll(diSourceSubDir, nextTargetSubDir);
            }
        }
    }
}
