using CBLoader;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Linq;

namespace CBInstaller
{
    class Program
    {
        static readonly string progdata = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CBLoader");
        static readonly string custom = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ddi", "CBLoader");

        [STAThread]
        static void Main(string[] args)
        {
            if (Utils.GetInstallPath() != null)
                MaybeUninstall();
            if (string.IsNullOrWhiteSpace(Utils.GetInstallPath()))
                Install();
            Version installed = new Version();
            if (Directory.Exists(progdata))
            {
                string logfile = Path.Combine(progdata, "CBLoader.log");
                if (File.Exists(logfile))
                {
                    try
                    {
                        string[] array = File.ReadAllLines(logfile);
                        installed = Version.Parse(Regex.Match(array[0], "CBLoader version ([0-9a-z\\.]+)").Groups[1].Value);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            Utils.ConfigureTLS12();
            var update = Utils.CheckForUpdates(installed);
            if (update != null)
                Download(update);
            if (!File.Exists("WotC.index"))
                GetIndex("WotC");
            foreach (var index in Directory.GetFiles(Environment.CurrentDirectory, "*.index"))
            {
                string path = Path.Combine(custom, Path.GetFileName(index));
                if (!File.Exists(path))
                    File.Copy(index, path);
            }
            Process.Start(new ProcessStartInfo(Path.Combine(progdata, "CBLoader.exe")));
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
            string configreadable = UTF8Encoding.UTF8.GetString(config);
            configreadable = configreadable.Replace(@"C:\Program Files\Wizards of the Coast\Character Builder", cbpath.Trim('\\'));
            config = UTF8Encoding.UTF8.GetBytes(configreadable);
            File.WriteAllBytes(PatchedPath, sfx.Concat(config).Concat(zip).ToArray());
            Run(new ProcessStartInfo(PatchedPath, $"-y"));

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
                catch (System.ComponentModel.Win32Exception c)
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
}
