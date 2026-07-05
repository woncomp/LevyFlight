using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;

namespace LevyFlight
{
    /// <summary>
    /// Dockable tool window that displays a Bird's Eye View (Document Outline)
    /// of the active C++ source file, powered by tree-sitter.
    /// </summary>
    [Guid("a1f3c4d5-6e7b-8a9c-0d1e-2f3a4b5c6d7e")]
    public class TreeSitterOutlineView : ToolWindowPane
    {
        public TreeSitterOutlineView() : base(null)
        {
            this.Caption = "Bird's Eye View";
            this.BitmapImageMoniker = KnownMonikers.DocumentOutline;
            this.Content = new TreeSitterOutlineViewControl();
        }
    }
}
