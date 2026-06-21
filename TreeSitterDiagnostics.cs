using LevyFlight.TreeSitter;
using System;
using System.IO;
using System.Text;

namespace LevyFlight
{
    internal static class TreeSitterDiagnostics
    {
        public static void SaveParse(string filePath, string sourceText, SyntaxTree tree, string caller, string engineName)
        {
            if (!LevyFlightOptions.Diagnostic || tree == null)
                return;

            ExtensionErrorHandler.Execute(() =>
            {
                string directory = GetDiagnosticDirectory();
                Directory.CreateDirectory(directory);

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                string safeName = MakeSafeName(Path.GetFileName(filePath));
                string suffix = string.IsNullOrEmpty(engineName) ? "tree" : engineName.ToLowerInvariant();
                string prefix = Path.Combine(directory, stamp + "-" + safeName + "." + suffix);

                File.WriteAllText(prefix + ".source.txt", sourceText ?? string.Empty, Encoding.UTF8);
                File.WriteAllText(prefix + ".tree.txt", BuildTreeDump(filePath, caller, tree), Encoding.UTF8);
            }, "Save Tree-sitter diagnostics");
        }

        public static string GetDiagnosticDirectory()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "LevyFlight", "TreeSitterDiagnostics");
        }

        private static string BuildTreeDump(string filePath, string caller, SyntaxTree tree)
        {
            var builder = new StringBuilder();
            builder.AppendLine("File: " + (filePath ?? "<unknown>"));
            builder.AppendLine("Caller: " + (caller ?? "<unknown>"));
            builder.AppendLine("Captured: " + DateTime.Now.ToString("O"));
            builder.AppendLine();

            AppendNode(builder, tree.Root, 0);
            return builder.ToString();
        }

        private static void AppendNode(StringBuilder builder, SyntaxNode node, int depth)
        {
            if (node.IsNull)
                return;

            var start = node.Start;
            var end = node.End;
            builder.Append(' ', depth * 2);
            builder.Append(node.Type);
            builder.Append(" [");
            builder.Append(start.Row + 1);
            builder.Append(':');
            builder.Append(start.Column);
            builder.Append('-');
            builder.Append(end.Row + 1);
            builder.Append(':');
            builder.Append(end.Column);
            builder.Append(']');

            if (node.Text.Length > 0 && node.Text.Length <= 80)
            {
                builder.Append(" \"");
                builder.Append(node.Text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\""));
                builder.Append('"');
            }
            builder.AppendLine();

            for (int i = 0; i < node.Children.Count; i++)
            {
                AppendNode(builder, node.Children[i], depth + 1);
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
