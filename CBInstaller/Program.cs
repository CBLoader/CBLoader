using CBLoader;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Security.AccessControl;

namespace CBInstaller
{
    class Program
    {
        static readonly string appdata = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CBLoader");
        static readonly string custom = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ddi", "CBLoader");
        private static Version installed;

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                if (Utils.GetInstallPath() != null)
                    LCB.MaybeUninstall();
            }
            catch (Exception) { }
            if (string.IsNullOrWhiteSpace(Utils.GetInstallPath()))
                LCB.Install();
            Utils.ConfigureTLS12();
            if (Directory.Exists(appdata) && File.Exists(Path.Combine(appdata, "CBLoader.exe")))
            {
                string logfile = Path.Combine(appdata, "CBLoader.log");
                if (File.Exists(logfile))
                {
                    try
                    {
                        string[] array = File.ReadAllLines(logfile);
                        installed = Version.Parse(Regex.Match(array[0], "CBLoader version ([0-9a-z\\.]+)").Groups[1].Value);
                        CheckForUpdate(installed);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            else
                CheckForUpdate(new Version());
            if (!File.Exists("WotC.index"))
                GetIndex("WotC");
            Directory.CreateDirectory(custom);
            foreach (var index in Directory.GetFiles(Environment.CurrentDirectory, "*.index"))
            {
                string path = Path.Combine(custom, Path.GetFileName(index));
                if (!File.Exists(path))
                    File.Copy(index, path);
            }
            Environment.CurrentDirectory = appdata;
            Process.Start(new ProcessStartInfo(Path.Combine(appdata, "CBLoader.exe")));

            var installPath = Path.Combine(appdata, "CBInstaller.exe");
            if (Assembly.GetExecutingAssembly().Location != installPath)
            {
                InstallSelf(installPath);
            }
        }

        private static void CheckForUpdate(Version installed)
        {
            var update = Utils.CheckForUpdates(installed);
            if (update != null)
                Download(update);
        }


        private static void Download(Utils.ReleaseInfo update)
        {
            var wc = new WebClient();
            string zip = Path.GetFileName(update.DownloadUrl);
            wc.DownloadFile(update.DownloadUrl, zip);
            var security = new DirectorySecurity();
            Directory.CreateDirectory(appdata);
            using (var zipfile = ZipFile.OpenRead(zip))
            {
                foreach (var e in zipfile.Entries)
                {
                    var path = Path.Combine(appdata, e.FullName);
                    if (e.FullName.EndsWith("/"))
                        Directory.CreateDirectory(path);
                    else
                    {
                        try
                        {
                            e.ExtractToFile(path, true);
                        }
                        catch (IOException) {
                            e.ExtractToFile(path + ".update", true);
                            NativeMethods.MoveFileEx(path + ".update", path, MoveFileFlags.DelayUntilReboot);
                        }
                    }
                }
            }
            wc.DownloadFile(update.InstallerUrl, $"CBInstaller {update.Version}.exe");
            NativeMethods.MoveFileEx($"CBInstaller {update.Version}.exe", Path.Combine(appdata, "CBInstaller.exe"), MoveFileFlags.DelayUntilReboot);

            installed = update.Version;
        }

        

        private static void InstallSelf(string self)
        {
            try
            {
                File.Copy(Assembly.GetExecutingAssembly().Location, self, true);
            }
            catch (IOException) { }
            IShellLink link = (IShellLink)new ShellLink();
            link.SetDescription($"Character Builder with CBLoader {installed?.ToString() ?? ""}");
            link.SetPath(self);
            link.SetIconLocation(Path.Combine(appdata, "CBLoader.exe"), 0);

            IPersistFile file = (IPersistFile)link;
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            file.Save(Path.Combine(desktopPath, "CBLoader.lnk"), false);
        }

        public static void GetIndex(string name)
        {
            var wc = new WebClient();

            try
            {
                var tmp = Path.GetTempFileName();
                wc.DownloadFile($"https://cbloader.vorpald20.com/indexes/{name}.index", tmp);
                XDocument.Load(tmp);
                File.Copy(tmp, name + ".index");
            }
            catch (Exception c)
            {
                Console.WriteLine(c);
            }
        }
    }
}
