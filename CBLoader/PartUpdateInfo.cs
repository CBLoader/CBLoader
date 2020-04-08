using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

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
            Version semVer;
            if (System.Version.TryParse(version, out semVer))
                SemVer = semVer;
        }
    }

    internal sealed class PartUpdateInfo
    {
        private Dictionary<string, UpdateVersionInfo> files =
            new Dictionary<string, UpdateVersionInfo>();

        public UpdateVersionInfo Get(string partName) => files[partName];
        public void AddFile(string filename, string version) =>
            files[Path.GetFileName(filename)] = new UpdateVersionInfo(Utils.HashFile(filename), version);

        public void Parse(string data)
        {
            if (!data.StartsWith("CBLoader Version File v2"))
                throw new Exception("Wrong header!");

            files.Clear();
            foreach (var line in data.Trim().Split('\n').Skip(1).Select(x => x.Trim()).Where(x => x != ""))
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
        private readonly WebClient wc;

        private HashSet<string> downloaded =
            new HashSet<string>();
        private Dictionary<string, PartUpdateInfo> newFormatVersions =
            new Dictionary<string, PartUpdateInfo>();
        private Dictionary<string, string> oldFormatVersions =
            new Dictionary<string, string>();

        public UpdateChecker(WebClient wc)
        {
            this.wc = wc;
        }

        private void downloadVersion(string updateUrl)
        {
            if (downloaded.Contains(updateUrl)) return;
            downloaded.Add(updateUrl);

            try {
                Log.Debug($" - Checking for updates at {updateUrl}");

                var data = wc.DownloadString(updateUrl);
                if (data.StartsWith("CBLoader Version File v2"))
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
            downloadVersion(updateUrl);
            if (newFormatVersions.ContainsKey(updateUrl))
                return newFormatVersions[updateUrl].Get(partFile);
            if (oldFormatVersions.ContainsKey(updateUrl))
                return new UpdateVersionInfo(null, oldFormatVersions[updateUrl]);
            return null;
        }
        public bool CheckRequiresUpdate(string filename, string currentVersion, string updateUrl)
        {
            Version semver;
            var partFile = Path.GetFileName(filename);
            var remoteVersion = getRemoteVersion(updateUrl, partFile);
            if (Version.TryParse(currentVersion, out semver))
            {
                if (remoteVersion.SemVer == null)
                {
                    Log.Warn($" - {partFile} has a semantic version, but the remote version doesn't.");
                    return false;
                }
                return remoteVersion.SemVer > semver;
            }
            if (remoteVersion == null)
                return false;
            if (remoteVersion.Version != currentVersion)
                return true;
            if (remoteVersion.Hash != null && remoteVersion.Hash != Utils.HashFile(filename))
                return true;
            return false;
        }
    }
}
