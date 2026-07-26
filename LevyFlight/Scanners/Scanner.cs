using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LevyFlight
{
    /// <summary>
    /// Describes a quick-open source and produces its jump items.
    /// </summary>
    public abstract class Scanner
    {
        public abstract string Id { get; }

        public abstract string DisplayName { get; }

        /// <summary>
        /// Higher values sort before lower values in quick-open results.
        /// </summary>
        public int Priority { get; internal set; }

        internal virtual ImageMoniker GetIconMoniker(string filePath)
        {
            return GetFileIconMoniker(filePath);
        }

        internal virtual Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context)
        {
            return Task.FromResult<IEnumerable<JumpItem>>(Array.Empty<JumpItem>());
        }

        internal JumpItem CreateJumpItem(string filePath)
        {
            return new JumpItem(this, filePath);
        }

        private static ImageMoniker GetFileIconMoniker(string filePath)
        {
            string extension = System.IO.Path.GetExtension(filePath)?.ToLowerInvariant() ?? string.Empty;
            switch (extension)
            {
                case ".cpp":
                case ".cxx":
                case ".cc":
                case ".c":
                    return KnownMonikers.CPPSourceFile;
                case ".h":
                case ".hpp":
                case ".hxx":
                case ".hh":
                case ".inl":
                case ".ipp":
                case ".tpp":
                    return KnownMonikers.CPPHeaderFile;
                case ".cs":
                    return KnownMonikers.CSFileNode;
                case ".xaml":
                    return KnownMonikers.PhoneXAML;
                case ".py":
                    return KnownMonikers.PYFileNode;
                case ".json":
                    return KnownMonikers.JSONScript;
                case ".xml":
                    return KnownMonikers.XMLFile;
                case ".js":
                    return KnownMonikers.JSScript;
                case ".txt":
                case ".md":
                case ".log":
                    return KnownMonikers.TextFile;
                default:
                    return KnownMonikers.Document;
            }
        }
    }
}
