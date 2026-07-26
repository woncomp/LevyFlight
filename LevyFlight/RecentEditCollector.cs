using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Differencing;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LevyFlight
{
    using CMD = LevyFlightWindowCommand;

    /// <summary>
    /// Collects recent edit regions of open text documents when the quick-open window
    /// is created. The editor platform does not expose a buffer's change history
    /// retroactively, so a baseline ITextSnapshot is pinned per document as it opens
    /// (or at package initialization for already-open documents). At collection time
    /// the version chain from that baseline to the current snapshot is walked and
    /// every recorded change is mapped forward onto current line coordinates.
    /// No per-edit event handling, persistence, or cross-session state is involved.
    /// </summary>
    internal static class RecentEditCollector
    {
        public class EditRegion
        {
            public string FilePath;
            public int StartLine;   // 0-based, inclusive
            public int EndLine;     // 0-based, inclusive
            public int JumpLine;    // line of the last modification inside the region
        }

        private class TrackedDocument
        {
            public string FilePath;
            public ITextBuffer Buffer;
            public ITextSnapshot Baseline; // pins the whole version chain forward from document open
        }

        /// <summary>Maximum number of regions returned, most relevant first.</summary>
        private const int MaxRegions = 40;

        /// <summary>
        /// Number of untouched lines still allowed inside a single region. Zero so that
        /// regions are exactly maximal runs of diff lines, matching VS's change bars.
        /// </summary>
        private const int MergeGap = 0;

        private static readonly List<TrackedDocument> Tracked = new List<TrackedDocument>();

        private static ITextDifferencingSelectorService _differSelector;
        private static bool _initialized;

        public static void Initialize(IComponentModel componentModel, Package package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_initialized || componentModel == null || package == null)
                return;

            var docService = componentModel.GetService<ITextDocumentFactoryService>();
            if (docService == null)
                return;

            var adapters = componentModel.GetService<IVsEditorAdaptersFactoryService>();
            ITextDifferencingSelectorService differSelector = null;
            try
            {
                differSelector = componentModel.GetService<ITextDifferencingSelectorService>();
            }
            catch (Exception ex)
            {
                ExtensionErrorHandler.Log("Acquire text differencing selector", ex);
            }

            // Catch documents that were opened before this initialization.
            var rdt = new RunningDocumentTable(package);
            foreach (var info in rdt)
            {
                ExtensionErrorHandler.Execute(() =>
                {
                    uint ignoreFlags = (uint)(_VSRDTFLAGS.RDT_ProjSlnDocument | _VSRDTFLAGS.RDT_VirtualDocument);
                    if ((info.Flags & ignoreFlags) != 0)
                        return;

                    var lines = info.DocData as IVsTextLines;
                    var buffer = lines != null ? adapters?.GetDocumentBuffer(lines) : null;
                    if (buffer != null)
                        Register(buffer, info.Moniker);
                }, "Register open document for edit tracking");
            }

            _differSelector = differSelector;
            docService.TextDocumentCreated += OnTextDocumentCreated;
            docService.TextDocumentDisposed += OnTextDocumentDisposed;
            _initialized = true;
        }

        private static void OnTextDocumentCreated(object sender, TextDocumentEventArgs e)
        {
            ExtensionErrorHandler.Execute(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Register(e.TextDocument.TextBuffer, e.TextDocument.FilePath);
            }, "Track created text document");
        }

        private static void OnTextDocumentDisposed(object sender, TextDocumentEventArgs e)
        {
            ExtensionErrorHandler.Execute(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Tracked.RemoveAll(t => ReferenceEquals(t.Buffer, e.TextDocument.TextBuffer));
            }, "Untrack disposed text document");
        }

        private static void Register(ITextBuffer buffer, string filePath)
        {
            if (Tracked.Any(t => ReferenceEquals(t.Buffer, buffer)))
                return;

            Tracked.Add(new TrackedDocument
            {
                Buffer = buffer,
                FilePath = filePath,
                Baseline = buffer.CurrentSnapshot,
            });
        }

        public static List<EditRegion> Collect(ScannerDumpSection dump = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            dump?.Detail("Collecting edit regions from " + Tracked.Count + " tracked documents");

            var ordered = new List<EditRegion>();
            foreach (var doc in OrderDocuments())
            {
                ExtensionErrorHandler.Execute(() =>
                {
                    if (string.IsNullOrEmpty(doc.FilePath) || !File.Exists(doc.FilePath))
                        return;

                    dump?.Detail("Document: " + doc.FilePath);
                    foreach (var region in CollectDocumentRegions(doc, dump))
                    {
                        region.FilePath = doc.FilePath;
                        ordered.Add(region);
                    }
                }, "Collect recent edit regions");
            }

            return ordered.Take(MaxRegions).ToList();
        }

        /// <summary>
        /// Returns the current snapshot of a tracked open document, or null when the
        /// file is not open as a text document.
        /// </summary>
        public static ITextSnapshot GetOpenDocumentSnapshot(string filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return FindTracked(filePath)?.Buffer.CurrentSnapshot;
        }

        /// <summary>
        /// Returns all recent edit regions of a tracked open document, without the
        /// global cap used for jump items. Empty when the file is not open.
        /// </summary>
        public static List<EditRegion> GetRegionsForFile(string filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var regions = new List<EditRegion>();
            var doc = FindTracked(filePath);
            if (doc == null)
                return regions;

            ExtensionErrorHandler.Execute(() =>
            {
                foreach (var region in CollectDocumentRegions(doc, null))
                {
                    region.FilePath = doc.FilePath;
                    regions.Add(region);
                }
            }, "Collect edit regions for preview");
            return regions;
        }

        private static TrackedDocument FindTracked(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;
            return Tracked.FirstOrDefault(t => string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Active document first, then documents in recently-active order.
        /// There is no cross-buffer edit timestamp, so activation order approximates recency.
        /// </summary>
        private static IEnumerable<TrackedDocument> OrderDocuments()
        {
            string activeFile = CMD.Instance.GetCurrentFile() ?? "";
            var recents = TransitionStore.Instance?.Recents ?? new List<string>();
            var recentIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < recents.Count; i++)
            {
                if (!recentIndex.ContainsKey(recents[i]))
                    recentIndex[recents[i]] = i;
            }

            return Tracked
                .OrderBy(t => string.Equals(t.FilePath, activeFile, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(t => recentIndex.TryGetValue(t.FilePath, out int idx) ? idx : int.MaxValue)
                .ToList();
        }

        /// <summary>
        /// Computes the set of 0-based lines whose current content differs from the pinned
        /// baseline, using VS's own text differ. Reverted edits cancel out by construction.
        /// Null when the differ service is unavailable (caller falls back to the
        /// version-chain union).
        /// </summary>
        private static HashSet<int> GetChangedLines(TrackedDocument doc, ITextSnapshot snapshot)
        {
            if (ReferenceEquals(doc.Baseline, snapshot))
                return new HashSet<int>();

            var differ = GetDifferencingService(doc.Buffer);
            if (differ == null)
                return null;

            var lines = new HashSet<int>();
            try
            {
                var diff = differ.DiffSnapshotSpans(
                    new SnapshotSpan(doc.Baseline, 0, doc.Baseline.Length),
                    new SnapshotSpan(snapshot, 0, snapshot.Length),
                    default(StringDifferenceOptions));

                // Difference.Right is in the coordinate space of the input right span,
                // i.e. our current snapshot.
                foreach (var difference in diff.Differences)
                {
                    var right = difference.Right;
                    int startLine = snapshot.GetLineFromPosition(right.Start).LineNumber;
                    int endLine = right.Length > 0
                        ? snapshot.GetLineFromPosition(right.End - 1).LineNumber
                        : startLine; // Pure deletion: mark the junction line.
                    for (int line = startLine; line <= endLine; line++)
                    {
                        lines.Add(line);
                    }
                }
            }
            catch (Exception ex)
            {
                ExtensionErrorHandler.Log("Diff document against baseline", ex);
                return null;
            }
            return lines;
        }

        private static ITextDifferencingService GetDifferencingService(ITextBuffer buffer)
        {
            if (_differSelector == null || buffer == null)
                return null;

            try
            {
                return _differSelector.GetTextDifferencingService(buffer.ContentType)
                    ?? _differSelector.DefaultTextDifferencingService;
            }
            catch (Exception ex)
            {
                ExtensionErrorHandler.Log("Select text differencing service", ex);
                return null;
            }
        }

        private static List<EditRegion> CollectDocumentRegions(TrackedDocument doc, ScannerDumpSection dump)
        {
            var regions = new List<EditRegion>();
            var snapshot = doc.Buffer.CurrentSnapshot;
            if (snapshot.Length == 0)
                return regions;

            // Recency information from the version chain: last edit order (one per buffer
            // version) that touched each 0-based line, plus the line holding the last
            // modified character of that change (the temporal jump target).
            var lastOrder = new Dictionary<int, int>();
            var lineJumpTarget = new Dictionary<int, int>();
            int order = 0;
            for (var version = doc.Baseline.Version.Next; version != null; version = version.Next)
            {
                var changes = version.Changes;
                if (changes == null || changes.Count == 0)
                    continue;

                order++;

                // Ignore whole-document replacements (file reload, branch switch) for
                // recency: they are not user edits.
                if (changes.Count == 1 && changes[0].NewLength > snapshot.Length * 0.8)
                    continue;

                foreach (var change in changes)
                {
                    int startLine, endLine, jumpLine;
                    try
                    {
                        var currentSpan = version
                            .CreateTrackingSpan(change.NewSpan, SpanTrackingMode.EdgeExclusive)
                            .GetSpan(snapshot);
                        startLine = snapshot.GetLineFromPosition(currentSpan.Start.Position).LineNumber;
                        if (currentSpan.Length > 0)
                        {
                            // The span is half-open: when it ends exactly at a line start,
                            // that next line contains no modified character. The last touched
                            // character is at End-1.
                            endLine = snapshot.GetLineFromPosition(currentSpan.End.Position - 1).LineNumber;
                            jumpLine = endLine;
                        }
                        else
                        {
                            // Pure deletion: mark the junction line.
                            endLine = startLine;
                            jumpLine = startLine;
                        }

                        dump?.Detail(string.Format(
                            "  version {0} change: NewSpan={1}..{2} (length {3}) -> current lines {4}..{5}, jump={6}",
                            order,
                            change.NewSpan.Start,
                            change.NewSpan.End,
                            change.NewSpan.Length,
                            startLine,
                            endLine,
                            jumpLine));
                    }
                    catch (Exception)
                    {
                        dump?.Detail("  version " + order + " change: NewSpan=" + change.NewSpan + " -> mapping failed");
                        continue;
                    }

                    for (int line = startLine; line <= endLine; line++)
                    {
                        lastOrder[line] = order;
                        lineJumpTarget[line] = jumpLine;
                    }
                }
            }

            // The changed line set comes from the content diff (same semantics as VS's
            // track-changes margin); fall back to the version-chain union without a differ.
            var diffLines = GetChangedLines(doc, snapshot);
            var touchedLines = (diffLines ?? new HashSet<int>(lastOrder.Keys)).ToList();
            touchedLines.Sort();

            int i = 0;
            while (i < touchedLines.Count)
            {
                int start = touchedLines[i];
                int j = i + 1;
                while (j < touchedLines.Count && touchedLines[j] - touchedLines[j - 1] - 1 <= MergeGap)
                {
                    j++;
                }

                int end = touchedLines[j - 1];
                int jumpLine = -1;
                int bestOrder = -1;
                for (int k = i; k < j; k++)
                {
                    int line = touchedLines[k];
                    if (lastOrder.TryGetValue(line, out int lineOrder) && lineOrder >= bestOrder) // ties resolve to the later line
                    {
                        bestOrder = lineOrder;
                        jumpLine = lineJumpTarget[line];
                    }
                }
                if (jumpLine < 0)
                {
                    jumpLine = end; // No tracked edit (e.g. external reload): use the region end.
                }

                regions.Add(new EditRegion
                {
                    StartLine = start,
                    EndLine = end,
                    JumpLine = jumpLine,
                });
                i = j;
            }

            // Most recently touched region first; regions without tracked edits last.
            regions.Sort((a, b) => RegionOrder(b).CompareTo(RegionOrder(a)));
            return regions;

            int RegionOrder(EditRegion r) => lastOrder.TryGetValue(r.JumpLine, out int o) ? o : -1;
        }
    }
}
