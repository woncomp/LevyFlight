using System;
using System.IO;
using System.Text;
using TreeSitterSharp;

namespace LevyFlight
{
    internal static class TreeSitterDiagnostics
    {
        public static void SaveParse(string filePath, string sourceText, TSTree tree, string caller)
        {
            if (!LevyFlightOptions.Diagnostic || tree == null)
                return;

            ExtensionErrorHandler.Execute(() =>
            {
                string directory = GetDiagnosticDirectory();
                Directory.CreateDirectory(directory);

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                string safeName = MakeSafeName(Path.GetFileName(filePath));
                string prefix = Path.Combine(directory, stamp + "-" + safeName);

                File.WriteAllText(prefix + ".source.txt", sourceText ?? string.Empty, Encoding.UTF8);
                File.WriteAllText(prefix + ".tree.txt", BuildTreeDump(filePath, caller, sourceText, tree), Encoding.UTF8);
            }, "Save Tree-sitter diagnostics");
        }

        public static string GetDiagnosticDirectory()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "LevyFlight", "TreeSitterDiagnostics");
        }

        private static string BuildTreeDump(string filePath, string caller, string sourceText, TSTree tree)
        {
            var builder = new StringBuilder();
            builder.AppendLine("File: " + (filePath ?? "<unknown>"));
            builder.AppendLine("Caller: " + (caller ?? "<unknown>"));
            builder.AppendLine("Captured: " + DateTime.Now.ToString("O"));
            builder.AppendLine();

            AppendNode(builder, tree.root_node(), sourceText ?? string.Empty, 0);
            return builder.ToString();
        }

        private static void AppendNode(StringBuilder builder, TSNode node, string sourceText, int depth)
        {
            if (node.is_null())
                return;

            var start = node.start_point();
            var end = node.end_point();
            builder.Append(' ', depth * 2);
            builder.Append(node.type());
            builder.Append(" [");
            builder.Append(start.row + 1);
            builder.Append(':');
            builder.Append(start.column);
            builder.Append('-');
            builder.Append(end.row + 1);
            builder.Append(':');
            builder.Append(end.column);
            builder.Append(']');

            uint length = node.end_offset() - node.start_offset();
            if (length > 0 && length <= 80)
            {
                builder.Append(" \"");
                builder.Append(node.text(sourceText).Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\""));
                builder.Append('"');
            }
            builder.AppendLine();

            uint count = node.child_count();
            for (uint i = 0; i < count; i++)
            {
                AppendNode(builder, node.child(i), sourceText, depth + 1);
            }
        }

        private static string MakeSafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = "unknown";

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }
            return name;
        }
    }
}
