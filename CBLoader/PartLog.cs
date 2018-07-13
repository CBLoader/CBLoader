using System.Collections.Generic;
using System.Security;
using System.Text;

namespace CBLoader
{
    internal sealed class PartLog
    {
        // TODO: Show changelogs

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
        private void appendPart(StringBuilder sb, PartStatus status)
        {
            sb.Append($"<li>{pickTarget(status)}{Escape(status.Name)}{Escape(versionString(status))}</li>");
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
            </style>
        ";
        public string Generate() => $@"<html>
            <head>{HEAD_HTML}</head>
            <body>
                CBLoader version {Program.Version} loaded
                <br>
                {generateSection("Loaded Modules", mergedModules)}
                {generateSection("Deleted Modules", obsoletedModules)}
            </body>
        </html>";
	}
}
