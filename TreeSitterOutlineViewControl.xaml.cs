using EnvDTE;
using GitHub.TreeSitter;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LevyFlight
{
    /// <summary>
    /// Code-behind for the Bird's Eye View (Document Outline) tool window.
    /// 
    /// Responsibilities:
    ///  • Listen for active-document changes → full re-parse
    ///  • Listen for text-buffer edits → incremental re-parse (5 s debounce)
    ///  • Maintain a hierarchical symbol tree via <see cref="TreeSitterOutlineCollector"/>
    ///  • Filter / sort / follow-cursor / expand-collapse toggles
    ///  • Click-to-navigate
    /// </summary>
    public partial class TreeSitterOutlineViewControl : UserControl, IVsRunningDocTableEvents
    {
        // ─── Symbol data ───────────────────────────────────────────────────
        private readonly ObservableCollection<OutlineSymbolItem> _rootSymbols = new ObservableCollection<OutlineSymbolItem>();

        // ─── Tree-sitter state (owned, disposed on document switch) ────────
        private TSParser _parser;
        private TSTree _currentTree;
        private string _currentSourceText;
        private string _currentFilePath;

        // ─── VS services ───────────────────────────────────────────────────
        private IVsRunningDocumentTable _rdt;
        private uint _rdtCookie;
        private IVsEditorAdaptersFactoryService _editorAdaptersFactory;

        // ─── Text buffer subscription ──────────────────────────────────────
        private ITextBuffer _subscribedBuffer;
        private IWpfTextView _currentTextView;

        // ─── Debounce timer (5 seconds after last edit) ────────────────────
        private readonly DispatcherTimer _debounceTimer;
        private const int DebounceDelaySeconds = 5;

        // ─── Filter debounce (300ms) ───────────────────────────────────────
        private readonly DispatcherTimer _filterDebounceTimer;

        // ─── Pending edits for incremental parsing ─────────────────────────
        private readonly List<TSInputEdit> _pendingEdits = new List<TSInputEdit>();

        // ─── Follow-cursor suppression flag ────────────────────────────────
        private bool _suppressFollowCursor;
        private bool _navigating;

        // ─── Supported file extensions ─────────────────────────────────────
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cpp", ".cxx", ".cc", ".c", ".h", ".hpp", ".hxx", ".hh", ".inl", ".ipp", ".tpp"
        };

        public TreeSitterOutlineViewControl()
        {
            InitializeComponent();
            OutlineTreeView.DataContext = _rootSymbols;

            // 5-second debounce for re-parse after edits
            _debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(DebounceDelaySeconds)
            };
            _debounceTimer.Tick += DebounceTimer_Tick;

            // 300ms debounce for filter text
            _filterDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _filterDebounceTimer.Tick += FilterDebounceTimer_Tick;

            // Wire up when the control is loaded / unloaded
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // ════════════════════════════════════════════════════════════════════
        //  Lifecycle
        // ════════════════════════════════════════════════════════════════════

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                // Get VS services
                _rdt = (IVsRunningDocumentTable)Package.GetGlobalService(typeof(SVsRunningDocumentTable));
                _rdt?.AdviseRunningDocTableEvents(this, out _rdtCookie);

                var componentModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
                _editorAdaptersFactory = componentModel?.GetService<IVsEditorAdaptersFactoryService>();

                // Initialize parser
                _parser = new TSParser();
                using (var lang = TSParser.CppLanguage())
                {
                    _parser.set_language(lang);
                }

                // Parse the currently active document right away
                RefreshForActiveDocument();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[BirdsEye] OnLoaded error: " + ex.Message);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _debounceTimer.Stop();
            _filterDebounceTimer.Stop();

            UnsubscribeFromBuffer();

            if (_rdt != null && _rdtCookie != 0)
            {
                _rdt.UnadviseRunningDocTableEvents(_rdtCookie);
                _rdtCookie = 0;
            }

            _currentTree?.Dispose();
            _currentTree = null;
            _parser?.Dispose();
            _parser = null;
        }

        // ════════════════════════════════════════════════════════════════════
        //  IVsRunningDocTableEvents — detect active document switches
        // ════════════════════════════════════════════════════════════════════

        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                // Get the moniker (file path) from the RDT
                _rdt.GetDocumentInfo(docCookie, out uint flags, out uint readLocks, out uint editLocks,
                    out string moniker, out IVsHierarchy hierarchy, out uint itemId, out IntPtr docData);

                if (!string.IsNullOrEmpty(moniker) &&
                    !string.Equals(moniker, _currentFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    // Active document changed — schedule a full re-parse
                    Dispatcher.BeginInvoke(new Action(() => SwitchToDocument(moniker)), DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[BirdsEye] OnBeforeDocumentWindowShow error: " + ex.Message);
            }
            return VSConstants.S_OK;
        }

        // Unused events — must still be implemented
        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;
        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;
        public int OnAfterSave(uint docCookie) => VSConstants.S_OK;
        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs) => VSConstants.S_OK;
        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame) => VSConstants.S_OK;

        // ════════════════════════════════════════════════════════════════════
        //  Document switching
        // ════════════════════════════════════════════════════════════════════

        private void RefreshForActiveDocument()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                if (dte?.ActiveDocument != null)
                {
                    SwitchToDocument(dte.ActiveDocument.FullName);
                }
                else
                {
                    ShowNoOutline("No active document.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[BirdsEye] RefreshForActiveDocument error: " + ex.Message);
            }
        }

        private void SwitchToDocument(string filePath)
        {
            if (string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
                return;

            _currentFilePath = filePath;
            _debounceTimer.Stop();
            _pendingEdits.Clear();

            // Unsubscribe from old buffer
            UnsubscribeFromBuffer();

            // Dispose old tree
            _currentTree?.Dispose();
            _currentTree = null;
            _currentSourceText = null;

            string ext = Path.GetExtension(filePath);
            if (!SupportedExtensions.Contains(ext))
            {
                ShowNoOutline("No outline available for this file type.");
                return;
            }

            // Subscribe to the new buffer
            SubscribeToBuffer();

            // Full parse
            FullParseAsync(filePath);
        }

        private void ShowNoOutline(string message)
        {
            _rootSymbols.Clear();
            NoOutlineMessage.Text = message;
            NoOutlineMessage.Visibility = Visibility.Visible;
            OutlineTreeView.Visibility = Visibility.Collapsed;
            StatusText.Text = "";
        }

        private void ShowOutline()
        {
            NoOutlineMessage.Visibility = Visibility.Collapsed;
            OutlineTreeView.Visibility = Visibility.Visible;
        }

        // ════════════════════════════════════════════════════════════════════
        //  Buffer subscription (for edit tracking)
        // ════════════════════════════════════════════════════════════════════

        private void SubscribeToBuffer()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null) return;

                textManager.GetActiveView(1, null, out IVsTextView vsTextView);
                if (vsTextView == null || _editorAdaptersFactory == null) return;

                _currentTextView = _editorAdaptersFactory.GetWpfTextView(vsTextView);
                if (_currentTextView == null) return;

                _subscribedBuffer = _currentTextView.TextBuffer;
                _subscribedBuffer.Changed += TextBuffer_Changed;

                // Also track caret for follow-cursor
                _currentTextView.Caret.PositionChanged += Caret_PositionChanged;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[BirdsEye] SubscribeToBuffer error: " + ex.Message);
            }
        }

        private void UnsubscribeFromBuffer()
        {
            if (_subscribedBuffer != null)
            {
                _subscribedBuffer.Changed -= TextBuffer_Changed;
                _subscribedBuffer = null;
            }
            if (_currentTextView != null)
            {
                _currentTextView.Caret.PositionChanged -= Caret_PositionChanged;
                _currentTextView = null;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Text buffer change → build TSInputEdits, reset 5s debounce
        // ════════════════════════════════════════════════════════════════════

        private void TextBuffer_Changed(object sender, TextContentChangedEventArgs e)
        {
            if (_currentTree == null) return;

            foreach (var change in e.Changes)
            {
                try
                {
                    // Build TSInputEdit (byte offsets = char offsets × 2 for UTF-16)
                    var edit = new TSInputEdit();
                    edit.start_byte = (uint)(change.OldPosition * 2);
                    edit.old_end_byte = (uint)(change.OldEnd * 2);
                    edit.new_end_byte = (uint)((change.OldPosition + change.NewLength) * 2);

                    // Start point (in old snapshot)
                    var oldStartLine = e.Before.GetLineFromPosition(change.OldPosition);
                    edit.start_point = new TSPoint(
                        (uint)oldStartLine.LineNumber,
                        (uint)(change.OldPosition - oldStartLine.Start.Position));

                    // Old end point
                    var oldEndLine = e.Before.GetLineFromPosition(change.OldEnd);
                    edit.old_end_point = new TSPoint(
                        (uint)oldEndLine.LineNumber,
                        (uint)(change.OldEnd - oldEndLine.Start.Position));

                    // New end point (in new snapshot)
                    int newEndPos = change.NewPosition + change.NewLength;
                    var newEndLine = e.After.GetLineFromPosition(newEndPos);
                    edit.new_end_point = new TSPoint(
                        (uint)newEndLine.LineNumber,
                        (uint)(newEndPos - newEndLine.Start.Position));

                    // Apply edit to tree (keeps it in sync for future incremental parse)
                    _currentTree.edit(edit);
                    _pendingEdits.Add(edit);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[BirdsEye] TSInputEdit error: " + ex.Message);
                }
            }

            // Reset the 5-second debounce timer
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void DebounceTimer_Tick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            IncrementalParseAsync();
        }

        // ════════════════════════════════════════════════════════════════════
        //  Parsing (full + incremental)
        // ════════════════════════════════════════════════════════════════════

        private async void FullParseAsync(string filePath)
        {
            try
            {
                string sourceText;
                try
                {
                    sourceText = File.ReadAllText(filePath);
                }
                catch
                {
                    // Try to read from the text buffer instead
                    if (_subscribedBuffer != null)
                        sourceText = _subscribedBuffer.CurrentSnapshot.GetText();
                    else
                        return;
                }

                // Parse on background thread
                var symbols = await System.Threading.Tasks.Task.Run(() =>
                {
                    var tree = _parser.parse_string(null, sourceText);
                    if (tree == null) return null;

                    var root = tree.root_node();
                    var result = TreeSitterOutlineCollector.Collect(root, sourceText);

                    // Store tree for incremental updates (pass ownership to UI thread)
                    return new ParseResult { Tree = tree, Symbols = result, SourceText = sourceText };
                });

                if (symbols == null) return;

                // Update on UI thread
                _currentTree?.Dispose();
                _currentTree = symbols.Tree;
                _currentSourceText = symbols.SourceText;
                _pendingEdits.Clear();

                RebuildOutlineTree(symbols.Symbols);
                ShowOutline();
                StatusText.Text = Path.GetFileName(filePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[BirdsEye] FullParseAsync error: " + ex.Message);
            }
        }

        private async void IncrementalParseAsync()
        {
            try
            {
                if (_parser == null || _currentTree == null) return;

                // Get current text from the buffer
                string newSourceText;
                if (_subscribedBuffer != null)
                    newSourceText = _subscribedBuffer.CurrentSnapshot.GetText();
                else if (!string.IsNullOrEmpty(_currentFilePath))
                    newSourceText = File.ReadAllText(_currentFilePath);
                else
                    return;

                var oldTree = _currentTree;

                // Incremental parse on background thread
                var symbols = await System.Threading.Tasks.Task.Run(() =>
                {
                    // parse_string with old tree → incremental
                    var newTree = _parser.parse_string(oldTree, newSourceText);
                    if (newTree == null) return null;

                    var root = newTree.root_node();
                    var result = TreeSitterOutlineCollector.Collect(root, newSourceText);

                    return new ParseResult { Tree = newTree, Symbols = result, SourceText = newSourceText };
                });

                if (symbols == null) return;

                // Swap trees
                _currentTree?.Dispose();
                _currentTree = symbols.Tree;
                _currentSourceText = symbols.SourceText;
                _pendingEdits.Clear();

                // Preserve expand/collapse state across rebuild
                RebuildOutlineTree(symbols.Symbols);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[BirdsEye] IncrementalParseAsync error: " + ex.Message);
            }
        }

        private class ParseResult
        {
            public TSTree Tree;
            public List<OutlineSymbolItem> Symbols;
            public string SourceText;
        }

        // ════════════════════════════════════════════════════════════════════
        //  Rebuild the ObservableCollection, preserving expand/collapse state
        // ════════════════════════════════════════════════════════════════════

        private void RebuildOutlineTree(List<OutlineSymbolItem> newSymbols)
        {
            // Capture current expand/collapse state
            var expandState = new Dictionary<string, bool>();
            CaptureExpandState(_rootSymbols, "", expandState);

            // Apply sort if needed
            if (SortAlphaButton.IsChecked == true)
                SortSymbolsAlpha(newSymbols);

            // Apply filter if needed
            string filter = FilterTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(filter))
                ApplyFilter(newSymbols, filter);

            _rootSymbols.Clear();
            foreach (var item in newSymbols)
                _rootSymbols.Add(item);

            // Restore expand/collapse state
            RestoreExpandState(_rootSymbols, "", expandState);

            // If follow-cursor is on, sync selection
            if (FollowCursorButton.IsChecked == true && _currentTextView != null)
            {
                SyncSelectionToCaret();
            }
        }

        private void CaptureExpandState(IEnumerable<OutlineSymbolItem> items, string prefix, Dictionary<string, bool> state)
        {
            foreach (var item in items)
            {
                string key = prefix + item.Kind + ":" + item.Name;
                state[key] = item.IsExpanded;
                if (item.Children.Count > 0)
                    CaptureExpandState(item.Children, key + "/", state);
            }
        }

        private void RestoreExpandState(IEnumerable<OutlineSymbolItem> items, string prefix, Dictionary<string, bool> state)
        {
            foreach (var item in items)
            {
                string key = prefix + item.Kind + ":" + item.Name;
                if (state.TryGetValue(key, out bool expanded))
                    item.IsExpanded = expanded;
                // else: new items default to expanded (true)

                if (item.Children.Count > 0)
                    RestoreExpandState(item.Children, key + "/", state);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Sorting
        // ════════════════════════════════════════════════════════════════════

        private static void SortSymbolsAlpha(List<OutlineSymbolItem> items)
        {
            items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var item in items)
            {
                if (item.Children.Count > 0)
                {
                    var childList = item.Children.ToList();
                    SortSymbolsAlpha(childList);
                    item.Children.Clear();
                    foreach (var c in childList)
                        item.Children.Add(c);
                }
            }
        }

        private void SortAlphaButton_Changed(object sender, RoutedEventArgs e)
        {
            // Re-parse with current data to apply/unapply sorting
            if (_currentTree != null && _currentSourceText != null)
            {
                var root = _currentTree.root_node();
                var symbols = TreeSitterOutlineCollector.Collect(root, _currentSourceText);
                RebuildOutlineTree(symbols);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Filtering
        // ════════════════════════════════════════════════════════════════════

        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Debounce filter input
            _filterDebounceTimer.Stop();
            _filterDebounceTimer.Start();
        }

        private void FilterDebounceTimer_Tick(object sender, EventArgs e)
        {
            _filterDebounceTimer.Stop();
            ApplyCurrentFilter();
        }

        private void ApplyCurrentFilter()
        {
            if (_currentTree == null || _currentSourceText == null) return;

            var root = _currentTree.root_node();
            var symbols = TreeSitterOutlineCollector.Collect(root, _currentSourceText);
            RebuildOutlineTree(symbols);
        }

        /// <summary>
        /// Filters a symbol tree in-place.  Parent nodes that contain matching
        /// descendants stay visible even if they don't match themselves.
        /// Non-matching leaf nodes are removed.
        /// </summary>
        private static bool ApplyFilter(List<OutlineSymbolItem> items, string filter)
        {
            bool anyVisible = false;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                bool selfMatch = item.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

                bool childMatch = false;
                if (item.Children.Count > 0)
                {
                    var childList = item.Children.ToList();
                    childMatch = ApplyFilter(childList, filter);
                    item.Children.Clear();
                    foreach (var c in childList)
                        item.Children.Add(c);
                }

                if (selfMatch || childMatch)
                {
                    item.IsExpanded = true; // auto-expand to show matches
                    anyVisible = true;
                }
                else
                {
                    items.RemoveAt(i);
                }
            }
            return anyVisible;
        }

        // ════════════════════════════════════════════════════════════════════
        //  Expand / Collapse all
        // ════════════════════════════════════════════════════════════════════

        private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
        {
            SetExpandedRecursive(_rootSymbols, true);
        }

        private void CollapseAllButton_Click(object sender, RoutedEventArgs e)
        {
            SetExpandedRecursive(_rootSymbols, false);
        }

        private static void SetExpandedRecursive(IEnumerable<OutlineSymbolItem> items, bool expanded)
        {
            foreach (var item in items)
            {
                item.IsExpanded = expanded;
                if (item.Children.Count > 0)
                    SetExpandedRecursive(item.Children, expanded);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Follow Cursor
        // ════════════════════════════════════════════════════════════════════

        private void FollowCursorButton_Changed(object sender, RoutedEventArgs e)
        {
            if (FollowCursorButton.IsChecked == true && _currentTextView != null)
                SyncSelectionToCaret();
        }

        private void Caret_PositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            if (_navigating) return;
            if (FollowCursorButton.IsChecked != true) return;
            if (_suppressFollowCursor) return;

            // Dispatch to avoid reentry
            Dispatcher.BeginInvoke(new Action(SyncSelectionToCaret), DispatcherPriority.Background);
        }

        private void SyncSelectionToCaret()
        {
            if (_currentTextView == null || _rootSymbols.Count == 0) return;

            try
            {
                var caretPosition = _currentTextView.Caret.Position.BufferPosition;
                int caretLine = caretPosition.GetContainingLine().LineNumber + 1; // 1-based

                // Find the deepest symbol containing the caret line
                var best = FindDeepestContaining(_rootSymbols, caretLine);
                if (best != null)
                {
                    _suppressFollowCursor = true;
                    try
                    {
                        // Deselect everything, then select the target
                        ClearSelection(_rootSymbols);
                        best.IsSelected = true;

                        // Expand parents to make it visible
                        ExpandParents(_rootSymbols, best);

                        // Scroll into view via TreeViewItem
                        BringItemIntoView(best);
                    }
                    finally
                    {
                        _suppressFollowCursor = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[BirdsEye] SyncSelectionToCaret error: " + ex.Message);
            }
        }

        private static OutlineSymbolItem FindDeepestContaining(IEnumerable<OutlineSymbolItem> items, int line)
        {
            OutlineSymbolItem best = null;
            foreach (var item in items)
            {
                if (line >= item.StartLine && line <= item.EndLine)
                {
                    best = item;
                    // Try to find a deeper match among children
                    var deeper = FindDeepestContaining(item.Children, line);
                    if (deeper != null)
                        best = deeper;
                }
            }
            return best;
        }

        private static bool ExpandParents(IEnumerable<OutlineSymbolItem> items, OutlineSymbolItem target)
        {
            foreach (var item in items)
            {
                if (item == target) return true;
                if (item.Children.Count > 0 && ExpandParents(item.Children, target))
                {
                    item.IsExpanded = true;
                    return true;
                }
            }
            return false;
        }

        private static void ClearSelection(IEnumerable<OutlineSymbolItem> items)
        {
            foreach (var item in items)
            {
                item.IsSelected = false;
                if (item.Children.Count > 0)
                    ClearSelection(item.Children);
            }
        }

        /// <summary>
        /// Attempts to bring a data item into view in the TreeView.
        /// </summary>
        private void BringItemIntoView(OutlineSymbolItem target)
        {
            // Walk the visual tree to find the TreeViewItem and call BringIntoView
            var tvi = FindTreeViewItem(OutlineTreeView, target);
            if (tvi != null)
            {
                tvi.BringIntoView();
            }
        }

        private static TreeViewItem FindTreeViewItem(ItemsControl parent, object dataItem)
        {
            if (parent == null) return null;

            for (int i = 0; i < parent.Items.Count; i++)
            {
                var item = parent.Items[i];
                var container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (container == null) continue;

                if (item == dataItem)
                    return container;

                // Recurse
                container.ApplyTemplate();
                var itemsPresenter = container.Template?.FindName("ItemsHost", container) as ItemsPresenter;
                if (itemsPresenter != null)
                {
                    itemsPresenter.ApplyTemplate();
                }
                else
                {
                    container.UpdateLayout();
                }

                var result = FindTreeViewItem(container, dataItem);
                if (result != null)
                    return result;
            }
            return null;
        }

        // ════════════════════════════════════════════════════════════════════
        //  Click-to-Navigate
        // ════════════════════════════════════════════════════════════════════

        private void OutlineTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Nothing to do here — navigation happens on mouse click
        }

        private void OutlineTreeView_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Navigate on single click (like the real Document Outline)
            if (OutlineTreeView.SelectedItem is OutlineSymbolItem selected)
            {
                NavigateToSymbol(selected);
            }
        }

        private void NavigateToSymbol(OutlineSymbolItem symbol)
        {
            if (symbol == null || string.IsNullOrEmpty(_currentFilePath)) return;

            _navigating = true;
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                if (dte == null) return;

                // Open the file (it may already be open)
                var window = dte.ItemOperations.OpenFile(_currentFilePath);
                if (window != null)
                {
                    window.Activate();

                    // Set caret and center
                    var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                    if (textManager != null)
                    {
                        textManager.GetActiveView(1, null, out IVsTextView vsTextView);
                        if (vsTextView != null)
                        {
                            int line = symbol.StartLine - 1; // 0-based for VS
                            vsTextView.SetCaretPos(line, symbol.StartColumn);
                            vsTextView.CenterLines(line, 1);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[BirdsEye] NavigateToSymbol error: " + ex.Message);
            }
            finally
            {
                _navigating = false;
            }
        }
    }
}
