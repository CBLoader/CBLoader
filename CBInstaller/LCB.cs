using CBLoader;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CBInstaller
{
    /// <summary>
    /// Legacy Character Builder Installer
    /// </summary>
    static class LCB
    {
        private const string Oct2010Url = "https://archive.org/download/ddi_charbuilder/Character_Builder_Update_Oct_2010.exe";
        private const string ddiSetupUrl = "https://archive.org/download/ddi_charbuilder/ddisetup.exe";
        public static string ProgramsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CharacterBuilder");

        internal static void MaybeUninstall()
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(File.ReadAllBytes(Path.Combine(Utils.GetInstallPath(), "CharacterBuilder.exe")));
                var expected = new byte[] { 20, 200, 83, 194, 101, 209, 133, 89, 219, 57, 93, 31, 22, 16, 47, 20 };
                if (!expected.SequenceEqual(hash))
                {
                    Console.Write("You don't have the 2010 update?");
                    //Environment.Exit(0);
                }
            }
        }

        internal static void Install()
        {
            Elevate();

            var ddiSetup = "ddisetup2009April.exe";
            if (!File.Exists(ddiSetup))
                ddiSetup = "ddisetup.exe";
            if (!File.Exists(ddiSetup))
            {
                try
                {
                    Console.WriteLine($"Downloading {ddiSetupUrl}");
                    var wc = new WebClient();
                    wc.DownloadFile(ddiSetupUrl, ddiSetup);
                }
                catch (WebException c)
                {
                    MessageBox.Show(c.Message);
                    if (File.Exists(ddiSetup))
                        File.Delete(ddiSetup);
                }
            }
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
                try
                {
                    Console.WriteLine($"Downloading {Oct2010Url}");
                    var wc = new WebClient();
                    wc.DownloadFile(Oct2010Url, update);
                }
                catch (WebException c)
                {
                    MessageBox.Show(c.Message);
                    if (File.Exists(update))
                        File.Delete(update);
                }
            }
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

            if (ProgramsFolder.Contains(" ")) // We can't have spaces in the path.
                ProgramsFolder = "C:\\CharacterBuilder";

            Run(new ProcessStartInfo(ddiSetup, $"-v\"INSTALLDIR={ProgramsFolder} -q\""));

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
            if (IsAdministrator())
                Environment.Exit(0);
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
                    if (process.ExitCode > 0)
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
    }
}
