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
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace CBInstaller
{
    class Program
    {
        static readonly string progdata = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CBLoader");
        static readonly string custom = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ddi", "CBLoader");
        private static Version installed;

        [STAThread]
        static void Main(string[] args)
        {
            if (Utils.GetInstallPath() != null)
                MaybeUninstall();
            if (string.IsNullOrWhiteSpace(Utils.GetInstallPath()))
                Install();
            Utils.ConfigureTLS12();
            if (Directory.Exists(progdata) && File.Exists(Path.Combine(progdata, "CBLoader.exe")))
            {
                string logfile = Path.Combine(progdata, "CBLoader.log");
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
            foreach (var index in Directory.GetFiles(Environment.CurrentDirectory, "*.index"))
            {
                string path = Path.Combine(custom, Path.GetFileName(index));
                if (!File.Exists(path))
                    File.Copy(index, path);
            }
            Environment.CurrentDirectory = progdata;
            Process.Start(new ProcessStartInfo(Path.Combine(progdata, "CBLoader.exe")));

            var installPath = Path.Combine(progdata, "CBInstaller.exe");
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

        private static void MaybeUninstall()
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(File.ReadAllBytes(Path.Combine(Utils.GetInstallPath(), "CharacterBuilder.exe")));
                var expected = new byte[] { 20, 200, 83, 194, 101, 209, 133, 89, 219, 57, 93, 31, 22, 16, 47, 20 };
                if (!expected.SequenceEqual(hash))
                {
                    MessageBox.Show("Please uninstall Character Builder and run me again.");
                    Environment.Exit(0);
                }
            }
        }

        private static void Download(Utils.ReleaseInfo update)
        {
            var wc = new WebClient();
            string zip = Path.GetFileName(update.DownloadUrl);
            wc.DownloadFile(update.DownloadUrl, zip);
            Directory.CreateDirectory(progdata);
            using (var zipfile = ZipFile.OpenRead(zip))
            {
                foreach (var e in zipfile.Entries)
                {
                    var path = Path.Combine(progdata, e.FullName);
                    if (e.FullName.EndsWith("/"))
                        Directory.CreateDirectory(path);
                    else
                        e.ExtractToFile(path, true);

                }
            }
            installed = update.Version;
        }

        private static void Install()
        {
            Elevate();

            var ddiSetup = "ddisetup2009April.exe";
            if (!File.Exists(ddiSetup))
                ddiSetup = "ddisetup.exe";
            if (!File.Exists(ddiSetup))
            {
                var openFileDialog = new OpenFileDialog { Filter = "ddisetup2009April.exe|ddisetup2009April.exe;ddisetup.exe" };
                openFileDialog.ShowDialog();
                ddiSetup = openFileDialog.FileName;
            }

            if (!File.Exists(ddiSetup))
            {
                Console.WriteLine("Can't find ddisetup2009April.exe, aborting");
                Environment.Exit(1);
            }

            var update = "Character_Builder_Update_Oct_2010.exe";
            if (!File.Exists(update))
            {
                var openFileDialog = new OpenFileDialog { Filter = "Character_Builder_Update_Oct_2010.exe|Character_Builder_Update_Oct_2010.exe" };
                openFileDialog.ShowDialog();
                update = openFileDialog.FileName;
            }
            if (!File.Exists(update))
            {
                Console.WriteLine($"Can't find {update}, aborting");
                Environment.Exit(2);
            }

            Run(new ProcessStartInfo(ddiSetup, "-v\"INSTALLDIR=C:\\CharacterBuilder -q\""));

            var cbpath = Utils.GetInstallPath();
            Console.WriteLine($"Installed to {cbpath}");

            const string PatchedPath = "Patched.Oct.2010.exe";
            byte[] sfx = new byte[0x23E03];
            byte[] config = new byte[0x1C6];
            byte[] zip = new byte[0x309FE96];

            using (var file = File.OpenRead(update))
            {
                file.Read(sfx, 0, 0x23E03);
                if (sfx.Last() != 0xBF)
                    throw new ArgumentException("Corrupted Oct2010 patch");
                file.Read(config, 0, 0x1C6);
                if (config.First() != ';' || config.Last() != '!')
                    throw new ArgumentException("Corrupted Oct2010 patch");
                file.Read(zip, 0, 0x309FE96);
            }
            string configreadable = Encoding.UTF8.GetString(config);
            configreadable = configreadable.Replace(@"C:\Program Files\Wizards of the Coast\Character Builder", cbpath.Trim('\\'));
            config = Encoding.UTF8.GetBytes(configreadable);
            File.WriteAllBytes(PatchedPath, sfx.Concat(config).Concat(zip).ToArray());
            Run(new ProcessStartInfo(PatchedPath, $"-y"));
        }

        private static void InstallSelf(string self)
        {
            File.Copy(Assembly.GetExecutingAssembly().Location, self, true);
            IShellLink link = (IShellLink)new ShellLink();
            link.SetDescription($"Character Builder with CBLoader {installed?.ToString() ?? ""}");
            link.SetPath(self);
            link.SetIconLocation(Path.Combine(progdata, "CBLoader.exe"), 0);

            IPersistFile file = (IPersistFile)link;
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            file.Save(Path.Combine(desktopPath, "CBLoader.lnk"), false);
        }

        private static void Elevate()
        {
            if (!IsAdministrator())
            {
                // Restart program and run as admin
                var exeName = Process.GetCurrentProcess().MainModule.FileName;
                ProcessStartInfo startInfo = new ProcessStartInfo(exeName)
                {
                    Verb = "runas"
                };
                try
                {
                    Process process = Process.Start(startInfo);
                    process.WaitForExit();
                    Environment.Exit(process.ExitCode);
                    return;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    Console.WriteLine("Unable to Elevate.  Please run me as Admin");
                    if (!Debugger.IsAttached)
                        Environment.Exit(10);
                }
            }
        }

        private static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void Run(ProcessStartInfo psi)
        {
            var p = Process.Start(psi);
            Console.WriteLine($"Running {psi.FileName} {psi.Arguments} ...");
            p.WaitForExit();
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

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
