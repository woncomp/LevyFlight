using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;

namespace LevyFlight
{
    /// <summary>
    /// Tag type used to mark bookmarked lines in the editor.
    /// </summary>
    internal class BookmarkTag : IGlyphTag
    {
    }

    /// <summary>
    /// Tagger that produces BookmarkTag tags for lines that have a LevyFlight bookmark.
    /// </summary>
    internal class BookmarkTagger : ITagger<BookmarkTag>
    {
        private readonly ITextBuffer _buffer;
        private readonly ITextDocument _document;

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public BookmarkTagger(ITextBuffer buffer, ITextDocument document)
        {
            _buffer = buffer;
            _document = document;

            // Subscribe to the static BookmarksChanged event so we can refresh glyphs
            LevyFlightWindowCommand.BookmarksChanged += OnBookmarksChanged;
        }

        private void OnBookmarksChanged(object sender, EventArgs e)
        {
            // Raise TagsChanged for the entire buffer so the editor re-queries all tags
            var snapshot = _buffer.CurrentSnapshot;
            var span = new SnapshotSpan(snapshot, 0, snapshot.Length);
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(span));
        }

        public IEnumerable<ITagSpan<BookmarkTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0)
                yield break;

            var cmd = LevyFlightWindowCommand.Instance;
            if (cmd == null || cmd.Bookmarks == null)
                yield break;

            string filePath = _document?.FilePath;
            if (string.IsNullOrEmpty(filePath))
                yield break;

            // Get all bookmarks for this file
            var fileBookmarks = cmd.Bookmarks
                .Where(b => b.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (fileBookmarks.Count == 0)
                yield break;

            var snapshot = spans[0].Snapshot;

            foreach (var bookmark in fileBookmarks)
            {
                int lineNumber = bookmark.LineNumber;
                if (lineNumber < 0 || lineNumber >= snapshot.LineCount)
                    continue;

                var line = snapshot.GetLineFromLineNumber(lineNumber);

                // Check if this line intersects any of the requested spans
                foreach (var span in spans)
                {
                    if (line.Extent.IntersectsWith(span))
                    {
                        yield return new TagSpan<BookmarkTag>(
                            new SnapshotSpan(line.Start, line.End),
                            new BookmarkTag());
                        break; // Only one tag per bookmark line
                    }
                }
            }
        }
    }

    /// <summary>
    /// MEF-exported tagger provider that creates BookmarkTagger instances for text views.
    /// </summary>
    [Export(typeof(IViewTaggerProvider))]
    [ContentType("text")]
    [TagType(typeof(BookmarkTag))]
    internal class BookmarkTaggerProvider : IViewTaggerProvider
    {
        [Import]
        internal ITextDocumentFactoryService TextDocumentFactoryService { get; set; }

        public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (textView == null || buffer == null)
                return null;

            // Only provide tags for the top-level buffer
            if (buffer != textView.TextBuffer)
                return null;

            ITextDocument document = null;
            TextDocumentFactoryService?.TryGetTextDocument(buffer, out document);

            if (document == null)
                return null;

            return new BookmarkTagger(buffer, document) as ITagger<T>;
        }
    }
}
