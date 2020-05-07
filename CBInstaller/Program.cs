using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace CBInstaller
{
    class Program
    {
        static void Main(string[] args)
        {
            if (!IsAdministrator())
            {
                // Restart program and run as admin
                var exeName = Process.GetCurrentProcess().MainModule.FileName;
                ProcessStartInfo startInfo = new ProcessStartInfo(exeName)
                {
                    Verb = "runas"
                };
                Process.Start(startInfo);
                return;
            }

            var ddiSetup = "ddisetup2009April.exe";
            if (!File.Exists(ddiSetup))
                ddiSetup = "ddisetup.exe";
            if (!File.Exists(ddiSetup))
            {
                Console.WriteLine("Can't find ddisetup2009April.exe, aborting");
                Environment.Exit(1);
            }

            var update = "Character_Builder_Update_Oct_2010.exe";
            if (!File.Exists(update))
            {
                Console.WriteLine($"Can't find {update}, aborting");
                Environment.Exit(2);
            }

            Run(new ProcessStartInfo(ddiSetup, "/S"));

            var cbpath = GetInstallPath();
            Console.WriteLine($"Installed to {cbpath}");

            Run(new ProcessStartInfo(update, $"-o\"{cbpath}\""));

            ZipFile.ExtractToDirectory(update, cbpath);

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

        private static readonly string CB_INSTALL_ID = "{626C034B-50B8-47BD-AF93-EEFD0FA78FF4}";

        public static string GetInstallPath()
        {
            var reg = Registry.LocalMachine
                .OpenSubKey($@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{CB_INSTALL_ID}");
            if (reg == null) return null;
            else return reg.GetValue("InstallLocation").ToString();
        }
    }
}
