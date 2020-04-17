using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml.Serialization;

namespace CBLoader
{
    internal sealed class UpdateVersionInfo
    {
        public readonly string Hash, Version;
        public readonly Version SemVer;

        public UpdateVersionInfo(string hash, string version)
        {
            Hash = hash;
            Version = version;
            try
            {
                SemVer = new Version(version);
            }
            catch (ArgumentException)
            {
            }
        }
    }

    internal sealed class PartUpdateInfo
    {
        private readonly Dictionary<string, UpdateVersionInfo> files =
                     new Dictionary<string, UpdateVersionInfo>();

        public UpdateVersionInfo Get(string partName) => files[partName];
        public void AddFile(string filename, string version) =>
            files[Path.GetFileName(filename)] = new UpdateVersionInfo(Utils.HashFile(filename), version);

        public void Parse(string data)
        {
            if (!data.StartsWith("CBLoader Version File v2"))
                throw new Exception("Wrong header!");

            files.Clear();
            foreach (var line in data.Trim().Split('\n').Skip(1).Select(x => x.Trim()).Where(x => x != "" && !x.StartsWith("#")))
            {
                var components = line.Split(new char[] { ':' }, 3);
                if (components.Length != 3) throw new Exception("Invalid update version file.");
                files[components[0].Trim()] = new UpdateVersionInfo(components[1].Trim(), components[2].Trim());
            }
        }
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("CBLoader Version File v2");
            foreach (var file in files)
                sb.AppendLine($"{file.Key}:{file.Value.Hash}:{file.Value.Version}");
            return sb.ToString();
        }
    }

    /// <summary>
    /// A class that checks for updates to part files.
    /// </summary>
    internal sealed class UpdateChecker
    {
        internal readonly WebClient wc;

        private readonly HashSet<string> downloaded =
            new HashSet<string>();
        private readonly Dictionary<string, PartUpdateInfo> newFormatVersions =
            new Dictionary<string, PartUpdateInfo>();
        private readonly Dictionary<string, string> oldFormatVersions =
            new Dictionary<string, string>();
        private readonly Dictionary<string, Redirect> redirects =
            new Dictionary<string, Redirect>();

        public UpdateChecker(WebClient wc, List<Redirect> redirects)
        {
            this.wc = wc ?? new WebClient();
            foreach (var r in redirects)
                this.redirects[r.From] = r;
        }

        private void downloadVersion(string updateUrl)
        {
            if (downloaded.Contains(updateUrl)) return;
            downloaded.Add(updateUrl);

            try {
                Log.Debug($" - Checking for updates at {updateUrl}");

                var data = wc.DownloadString(updateUrl);
                if (data.StartsWith("CBLoader Version File v2", StringComparison.InvariantCultureIgnoreCase))
                {
                    var info = new PartUpdateInfo();
                    info.Parse(data);
                    newFormatVersions[updateUrl] = info;
                }
                else oldFormatVersions[updateUrl] = data.Trim();
            }
            catch (Exception e)
            {
                Log.Debug($"Failed to load update info at {updateUrl}.", e);
            }
        }
        private UpdateVersionInfo getRemoteVersion(string updateUrl, string partFile)
        {
            if (updateUrl == null)
                return null;
            updateUrl = CheckForRedirect(updateUrl);
            downloadVersion(updateUrl);
            if (newFormatVersions.ContainsKey(updateUrl))
                return newFormatVersions[updateUrl].Get(partFile);
            if (oldFormatVersions.ContainsKey(updateUrl))
                return new UpdateVersionInfo(null, oldFormatVersions[updateUrl]);
            return null;
        }

        public bool CheckRequiresUpdate(string filename, string currentVersion, string updateUrl, string UpdateUrl2)
        {
            var partFile = Path.GetFileName(filename);
            var remoteVersion = getRemoteVersion(UpdateUrl2, partFile) ?? getRemoteVersion(updateUrl, partFile);
            Version semver = null;
            try
            {
                semver = new Version(currentVersion);
            }
            catch (ArgumentException) { }
            if (semver != null)
            {
                if (remoteVersion.SemVer == null)
                {
                    Log.Warn($" - {partFile} has a semantic version, but the remote version doesn't.");
                    return false;
                }
                if (remoteVersion.SemVer > semver)
                    return true;
                if (remoteVersion.SemVer == semver && remoteVersion.Hash != null && remoteVersion.Hash != Utils.HashFile(filename))
                    return true;
                return false;
            }
            if (remoteVersion == null)
                return false;
            if (remoteVersion.Version != currentVersion)
                return true;
            if (remoteVersion.Hash != null && remoteVersion.Hash != Utils.HashFile(filename))
                return true;
            return false;
        }

        internal void AddRedirect(string from, string to)
        {
            if (redirects.ContainsKey(from))
                return;
            redirects.Add(from, new Redirect() { From = from, To = to });

        }

        internal string CheckForRedirect(string updateUrl)
        {
            foreach (var prefix in redirects.Keys)
            {
                if (!updateUrl.StartsWith(prefix))
                    continue;
                var redirect = redirects[prefix];
                var dest = updateUrl.Replace(prefix, redirect.To);
                if (!redirect.Confirmed.HasValue)
                {
                    if (System.Windows.MessageBox.Show($"Allow Redirect from {updateUrl} to {dest}?", "CBLoader", System.Windows.MessageBoxButton.YesNo) == System.Windows.MessageBoxResult.Yes)
                    {
                        redirect.Confirmed = true;
                        SaveRedirect(redirect);
                    }
                    else
                        redirect.Confirmed = false;
                }
                if (redirect.Confirmed == false)
                    continue;
                downloadVersion(dest);
                if (oldFormatVersions.ContainsKey(dest) || newFormatVersions.ContainsKey(dest))
                    return dest;
            }
            return updateUrl;
        }

        internal string ApplyRedirect(string updateUrl)
        {
            foreach (var prefix in redirects.Keys)
            {
                if (!updateUrl.StartsWith(prefix))
                    continue;
                var redirect = redirects[prefix];
                var dest = updateUrl.Replace(prefix, redirect.To);
                if (redirect.Confirmed == true)
                    return dest;
            }
            return updateUrl;
        }

        private void SaveRedirect(Redirect redirect)
        {
            var baseconfigpath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "default.cbconfig");
            if (File.Exists(baseconfigpath)) // Point the config file at this for future launches
            {
                var doc = System.Xml.Linq.XDocument.Load(baseconfigpath);
                var rds = doc.Root.Element("Redirects");
                if (rds == null)
                {
                    rds = new System.Xml.Linq.XElement("Redirects");
                    doc.Root.Add(rds);
                }
                rds.Add(new System.Xml.Linq.XElement("Redirect", new System.Xml.Linq.XAttribute("from", redirect.From), new System.Xml.Linq.XAttribute("to", redirect.To)));
                doc.Save(baseconfigpath, System.Xml.Linq.SaveOptions.DisableFormatting);
            }
        }
    }
}
