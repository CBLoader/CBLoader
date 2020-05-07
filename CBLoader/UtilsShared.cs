using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Text;

namespace CBLoader
{
    partial class Utils
    {
        public const SslProtocols _Tls12 = (SslProtocols)0x00000C00;
        public const SecurityProtocolType Tls12 = (SecurityProtocolType)_Tls12;
        public static void ConfigureTLS12()
        {
            ServicePointManager.SecurityProtocol = Tls12;
        }

        internal static bool IS_WINDOWS =
            Environment.OSVersion.Platform == PlatformID.Win32NT ||
            Environment.OSVersion.Platform == PlatformID.Win32S ||
            Environment.OSVersion.Platform == PlatformID.Win32Windows;

        internal static readonly string CB_INSTALL_ID = "{626C034B-50B8-47BD-AF93-EEFD0FA78FF4}";

        public static string GetInstallPath()
        {
            if (IS_WINDOWS)
            {
                var reg = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{CB_INSTALL_ID}");
                if (reg != null) return reg.GetValue("InstallLocation").ToString();
            }

            var searchPath = new string[] {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Character Builder"), // Subdir
                AppDomain.CurrentDomain.BaseDirectory, // Same dir
                @"C:\Users\WDAGUtilityAccount\Desktop\Character Builder", // Windows Sandbox
            };
            foreach (var local in searchPath)
            {
                if (File.Exists(Path.Combine(local, "CharacterBuilder.exe")))
                    return local;
            }
            return null;
        }

        internal class ReleaseInfo
        {
            public Version Version { get; }
            public string HtmlUrl { get; }
            public string DownloadUrl { get; }

            public ReleaseInfo(Version remote, string htmlUrl, string downlaodUrl)
            {
                this.Version = remote;
                this.HtmlUrl = htmlUrl;
                this.DownloadUrl = downlaodUrl;
            }
        }

        public static ReleaseInfo CheckForUpdates(Version ver)
        {

            var wc = new WebClient();
            wc.Headers["User-Agent"] = "CBLoader-Update-Checker";
            try
            {
                var json = wc.DownloadString("http://api.github.com/repos/CBLoader/CBLoader/releases");
                var releases = SimpleJSON.JSON.Parse(json);
                foreach (var rel in releases.Children)
                {
                    if (rel["prerelease"] == true)
                        continue;
                    var rs = rel["tag_name"].Value.Trim('v');
                    var remote = new Version(rs);
                    if (remote > ver)
                    {
                        Console.WriteLine("A new version of CBLoader is available.");
                        Console.WriteLine(rel["html_url"].Value);
                        return new ReleaseInfo(remote, rel["html_url"].Value, FindCBLoaderAsset(rel)["browser_download_url"].Value);
                    }
                    return null;
                }
            }
            catch (WebException c)
            {
                Console.WriteLine(c);
                return null;
            }
            return null;

            SimpleJSON.JSONNode FindCBLoaderAsset(SimpleJSON.JSONNode rel)
            {
                return rel["assets"].Children.FirstOrDefault(a => a["name"].Value == "CBLoader.zip");
            }
        }

    }
}
