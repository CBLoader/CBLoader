using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;

namespace CBLoader
{
    internal sealed class PartLog
    {
        private int unqiueId = 1;
        private string createId() => $"part_id_{unqiueId++}";

        private HashSet<string> processedModules = new HashSet<string>();

        private StringBuilder mergedModules = new StringBuilder();
        private StringBuilder obsoletedModules = new StringBuilder();

        private static string Escape(string text) => SecurityElement.Escape(text);
        private string versionString(PartStatus status)
        {
            if (status.Version == null) return "";
            if (status.FirstFoundVersion == null ||
                status.FirstFoundVersion == status.Version) return $" v{status.Version}";
            return $" v{status.FirstFoundVersion} -> v{status.Version}";
        }
        private string pickTarget(PartStatus status)
        {
            if (status.wasObsoleted) return "";
            if (status.wasAdded) return "<b>(New)</b> ";
            if (status.wasUpdated) return "<b>(Updated)</b> ";
            return "";
        }
        private string renderChangelog(PartStatus status)
        {
            if (status.Changelog == null) return "<ul><li><i>No changelog available</i></li></ul>";

            var sb = new StringBuilder();
            var split = status.Changelog.Split('\n');
            Array.Reverse(split);
            foreach (var line in split.Select(x => x.Trim()).Where(x => x != ""))
                sb.Append($"<li>{Escape(line)}</li>");
            return $"<ul>{sb}</ul>";
        }
        private void appendPart(StringBuilder sb, PartStatus status)
        {
            var id = createId();
            sb.Append($@"<li>
                <a id=""{id}_button"" href=""#{id}"" onClick=""showHideChangelog('{id}'); return false;"">+</a>
                {pickTarget(status)}{Escape(status.Name)}{Escape(versionString(status))}
                <span id=""{id}_changelog"" style=""display: none;"">{renderChangelog(status)}</span>
            </li>");
        }

        public void AddModule(PartStatus status)
        {
            if (processedModules.Contains(status.FullName)) return;
            processedModules.Add(status.FullName);

            if (status.wasMerged) appendPart(mergedModules, status);
            else if (status.wasObsoleted) appendPart(obsoletedModules, status);
        }

        private static string generateSection(string title, StringBuilder builder)
        {
            if (builder.Length != 0) return $@"
                <h3>{title}</h3>
                <ul>{builder}</ul>
            ";
            return "";
        }

        private const string HEAD_HTML = @"
            <meta http-equiv=""X-UA-Compatible"" content=""IE=Edge""/>
            <style>
                body { 
                    /* This replicates the default Windows UI font. */
                    font-family: 'Segoe UI', 'Tahoma', 'Microsoft Sans Serif', 'sans'; 
                    font-size: 12px;

                    /* Background to match the outer box. */
                    background: linear-gradient(to bottom, #d7cda7 11.5%, #ffffff 80.5%);
					background-repeat: no-repeat;
					background-position: right top;
					background-attachment: fixed;
                }
                a {
                    /* Keep the size of the button consistant between its two states. */
                    font-family: 'Lucida Console', 'Lucida Sans Typewriter', monaco, 'Bitstream Vera Sans Mono', monospace;
                }
            </style>
            <script>
                function showHideChangelog(id) {
                    button = document.getElementById(id + '_button');
                    changelog = document.getElementById(id + '_changelog');
                    if (button.innerHTML == '+') {
                        changelog.style.display = 'block';
                        button.innerHTML = '-';
                    } else {
                        changelog.style.display = 'none';
                        button.innerHTML = '+';
                    }
                }
            </script>
        ";
        public string Generate() => $@"<html>
            <head>{HEAD_HTML}</head>
            <body>
                CBLoader version {Program.VersionString} loaded
                <br>
                {generateSection("Loaded Modules", mergedModules)}
                {generateSection("Deleted Modules", obsoletedModules)}
            </body>
        </html>";
	}
}
