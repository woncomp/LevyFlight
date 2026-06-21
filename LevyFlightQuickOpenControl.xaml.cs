using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LevyFlight
{
    using CMD = LevyFlightWindowCommand;

    /// <summary>
    /// The real quick-open UI and logic, loaded lazily by LevyFlightWindow.
    /// </summary>
    public partial class LevyFlightQuickOpenControl : UserControl, INotifyPropertyChanged
    {
        public static readonly Key[] QuickOpenKeys = new Key[]
        {
            Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9,
            Key.Q, Key.W, Key.E, Key.R, Key.T, Key.Y,
        };

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Fired when the control wants its parent window to close (e.g. user selected an item).
        /// </summary>
        public event EventHandler RequestClose;

        public ObservableCollection<JumpItem> AllJumpItems { get; set; }

        public CollectionViewSource ViewSource { get; set; }

        public string DebugString
        {
            get { return debugString ?? ""; }
            set
            {
                debugString = value;
                OnPropertyChanged();
            }
        }

        public string SelectedItemFullPath
        {
            get { return selectedItemFullPath ?? "Ctrl+J: Move Down | Ctrl+K: Move Up | Ctrl+D: Half Page Down | Ctrl+U: Half Page Up"; }
            set
            {
                selectedItemFullPath = value;
                OnPropertyChanged();
            }
        }

        public Visibility DiagnosticOverlayVisibility
        {
            get { return diagnosticOverlayVisibility; }
            set
            {
                diagnosticOverlayVisibility = value;
                OnPropertyChanged();
            }
        }

        private DispatcherTimer filterUpdateTimer;
        private DispatcherTimer previewLoadTimer;
        private CancellationTokenSource previewLoadCts;
        private TargetLineRenderer targetLineRenderer;

        private string debugString;
        private string selectedItemFullPath;
        private Visibility diagnosticOverlayVisibility = Visibility.Collapsed;
        private JumpItem pendingPreviewItem;

        private Dictionary<Key, System.Func<bool>> windowsKeyBindings = new Dictionary<Key, Func<bool>>();

        private const long MaxPreviewFileSizeBytes = 2 * 1024 * 1024; // 2 MB
        private const int PreviewLoadDebounceMs = 75;

        public LevyFlightQuickOpenControl()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            InitializeComponent();
            DataContext = this;
        }

        /// <summary>
        /// Initializes data structures, timers, theming and starts file discovery.
        /// Must be called on the UI thread.
        /// </summary>
        public async Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            AllJumpItems = new ObservableCollection<JumpItem>();

            ViewSource = new CollectionViewSource();
            ViewSource.Source = AllJumpItems;
            ViewSource.Filter += ViewSource_Filter;
            ViewSource.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));

            DebugString = "";

            filterUpdateTimer = new DispatcherTimer();
            filterUpdateTimer.Interval = TimeSpan.FromSeconds(0.3);
            filterUpdateTimer.Tick += FilterUpdateTimer_Tick;

            previewLoadTimer = new DispatcherTimer();
            previewLoadTimer.Interval = TimeSpan.FromMilliseconds(PreviewLoadDebounceMs);
            previewLoadTimer.Tick += PreviewLoadTimer_Tick;

            targetLineRenderer = new TargetLineRenderer();
            codePreview.TextArea.TextView.BackgroundRenderers.Add(targetLineRenderer);
            CodePreviewManager.ApplyThemeToEditor(codePreview);
            CodePreviewManager.ThemeChanged += OnCodePreviewThemeChanged;
            UpdateDiagnosticOverlayVisibility();

            SetupKeyBindings();

            await Task.Yield();

            StartDiscoverFiles();
        }

        private void ViewSource_Filter(object sender, FilterEventArgs e)
        {
            JumpItem jumpItem = e.Item as JumpItem;
            e.Accepted = jumpItem.Score > 0;
        }

        private void SetupKeyBindings()
        {
            windowsKeyBindings[Key.J] = () => { MoveSelection(lstFiles.SelectedIndex + 1); return true; };
            windowsKeyBindings[Key.K] = () => { MoveSelection(lstFiles.SelectedIndex - 1); return true; };
            windowsKeyBindings[Key.D] = () => { FastMove(+1); return true; }; // Ctrl+D half page down
            windowsKeyBindings[Key.U] = () => { FastMove(-1); return true; }; // Ctrl+U half page up
        }

        private void FastMove(int direction)
        {
            if (lstFiles.Items.Count == 0) return;
            ScrollViewer sv = FindDescendant<ScrollViewer>(lstFiles);
            double avgItemHeight = 18; // default fallback
            int firstVisibleIndex = lstFiles.SelectedIndex >= 0 ? lstFiles.SelectedIndex : 0;
            for (int i = Math.Max(0, firstVisibleIndex - 5); i <= firstVisibleIndex + 5 && i < lstFiles.Items.Count; i++)
            {
                if (lstFiles.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement fe && fe.ActualHeight > 0)
                {
                    avgItemHeight = fe.ActualHeight;
                    break;
                }
            }
            double visibleItemsApprox = Math.Max(1, Math.Floor(lstFiles.ActualHeight / avgItemHeight));
            int halfPageItems = (int)Math.Max(1, visibleItemsApprox / 2);

            int current = lstFiles.SelectedIndex;
            if (current < 0) current = 0;
            int target = current + direction * halfPageItems;
            if (target < 0) target = 0;
            if (target >= lstFiles.Items.Count) target = lstFiles.Items.Count - 1;
            MoveSelection(target);

            if (sv != null)
            {
                bool logicalScrolling = sv.CanContentScroll;
                if (logicalScrolling)
                {
                    double newOffset = sv.VerticalOffset + direction * halfPageItems;
                    if (newOffset < 0) newOffset = 0;
                    sv.ScrollToVerticalOffset(newOffset);
                }
                else
                {
                    double deltaPixels = halfPageItems * avgItemHeight;
                    double newOffset = sv.VerticalOffset + direction * deltaPixels;
                    if (newOffset < 0) newOffset = 0;
                    sv.ScrollToVerticalOffset(newOffset);
                }
            }
        }

        private T FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T wanted)
                    return wanted;
                var result = FindDescendant<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void StartDiscoverFiles()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var currentFile = CMD.Instance.GetCurrentFile();
            var knownFiles = new HashSet<string>();
            if (!string.IsNullOrEmpty(currentFile))
            {
                knownFiles.Add(currentFile);
            }

            // Add recent files
            var recentFiles = TransitionStore.Instance.Recents;
            var recentEnd = Math.Max(Math.Min(20, recentFiles.Count), recentFiles.Count * 3 / 4);
            var recentIdx = 0;
            for (int recentCount = 6; recentCount > 0 && recentIdx < recentEnd; recentIdx++)
            {
                string filePath = System.IO.Path.GetFullPath(recentFiles[recentIdx]);
                if (!knownFiles.Contains(filePath))
                {
                    var jumpItem = new JumpItem(Category.HotFile, filePath);
                    AllJumpItems.Add(jumpItem);
                    knownFiles.Add(filePath);
                    recentCount--;
                }
            }

            // Add transitions
            var transitions = TransitionStore.Instance.GetTransitionsForFile(currentFile);
            int trEnd = Math.Max(Math.Min(20, transitions.Count), transitions.Count * 3 / 4);
            int trIdx = 0;
            for (int trCount = 11; trCount > 0 && trIdx < trEnd; trIdx++)
            {
                TransitionRecord tr = transitions[trIdx];
                var filePath = CommonMixin.ToAbsolutePath(tr.Path);
                if (!knownFiles.Contains(filePath))
                {
                    var jumpItem = new JumpItem(Category.Transition, filePath);
                    AllJumpItems.Add(jumpItem);
                    knownFiles.Add(filePath);
                    trCount--;
                }
            }

            // Add active files
            var activeFiles = CMD.Instance.GetActiveFiles();
            foreach (var filePath in activeFiles)
            {
                if (!knownFiles.Contains(filePath))
                {
                    var jumpItem = new JumpItem(Category.OpenFile, filePath);
                    AllJumpItems.Add(jumpItem);
                    knownFiles.Add(filePath);
                }
            }

            // Add more transition files
            for (; trIdx < trEnd; trIdx++)
            {
                TransitionRecord tr = transitions[trIdx];
                var filePath = CommonMixin.ToAbsolutePath(tr.Path);
                if (!knownFiles.Contains(filePath))
                {
                    var jumpItem = new JumpItem(Category.RecentFile, filePath);
                    AllJumpItems.Add(jumpItem);
                    knownFiles.Add(filePath);
                }
            }

            // Add more recent files
            for (; recentIdx < recentEnd; recentIdx++)
            {
                string filePath = recentFiles[recentIdx];
                if (!knownFiles.Contains(filePath))
                {
                    var jumpItem = new JumpItem(Category.RecentFile, filePath);
                    AllJumpItems.Add(jumpItem);
                    knownFiles.Add(filePath);
                }
            }

            // Add files in the folders of active files
            {
                var knownFolders = new HashSet<string>();
                foreach (var activeFile in activeFiles)
                {
                    string currentFolder = System.IO.Path.GetDirectoryName(activeFile);
                    if (!Directory.Exists(currentFolder) || knownFolders.Contains(currentFolder))
                    {
                        continue;
                    }
                    knownFolders.Add(currentFolder);
                    foreach (var filePath in Directory.GetFiles(currentFolder))
                    {
                        if (!knownFiles.Contains(filePath) && !CommonMixin.IsExcluded(filePath))
                        {
                            var jumpItem = new JumpItem(Category.ActiveDirectoryFile, filePath);
                            AllJumpItems.Add(jumpItem);
                            knownFiles.Add(filePath);
                        }
                    }
                }
            }

            // Add bookmarks
            foreach (var jumpItem in CMD.Instance.Bookmarks)
            {
                AllJumpItems.Add(jumpItem);
            }

            // Kick off tree-sitter C++ parse of the active document
            if (!string.IsNullOrEmpty(currentFile))
            {
                _ = ExtensionErrorHandler.ExecuteAsync(() => AddTreeSitterItemsAsync(currentFile), "Add Tree-sitter quick-open items");
            }

            // Start scanning the entire solution a little later
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(0.1);
            timer.Tick += (_, e2) =>
            {
                ExtensionErrorHandler.Execute(() =>
                {
                    timer.Stop();
                    _ = ExtensionErrorHandler.ExecuteAsync(() => StartDiscoverFilesAsync(knownFiles), "Discover solution files");
                }, "Start delayed solution discovery");
            };
            timer.Start();
        }

        private async Task AddTreeSitterItemsAsync(string currentFile)
        {
            var items = await TreeSitterCodeParser.ParseAndListFunctionsAsync(currentFile);
            if (items.Count == 0)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (!IsVisible)
                return;

            using (ViewSource.DeferRefresh())
            {
                foreach (var item in items)
                {
                    AllJumpItems.Add(item);
                }
            }
            RefreshQuickOpenIndices();
        }

        private async Task StartDiscoverFilesAsync(HashSet<string> knownFiles)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            List<JumpItem> stagingList = new List<JumpItem>();
            foreach (var (category, filePath) in CMD.Instance.EnumerateSolutionFiles(knownFiles))
            {
                var jumpItem = new JumpItem(category, filePath);
                stagingList.Add(jumpItem);
                if (stagingList.Count >= 2000)
                {
                    using (ViewSource.DeferRefresh())
                    {
                        foreach (var item in stagingList)
                        {
                            AllJumpItems.Add(item);
                        }
                        stagingList.Clear();
                        DebugString = $"Files:{AllJumpItems.Count}";
                    }
                    RefreshQuickOpenIndices();
                    await Task.Yield();
                }
                if (!this.IsVisible)
                {
                    return;
                }
            }
            using (ViewSource.DeferRefresh())
            {
                foreach (var item in stagingList)
                {
                    AllJumpItems.Add(item);
                }
                DebugString = $"Files:{AllJumpItems.Count}";
            }
            RefreshQuickOpenIndices();
        }

        private JumpItem GetQuickOpenItemForKey(Key key)
        {
            int idx = Array.IndexOf(QuickOpenKeys, key);
            if (idx < 0) return null;
            int targetViewIndex = idx + 1;
            var view = ViewSource.View;
            if (view == null) return null;
            if (targetViewIndex >= view.Cast<object>().Count()) return null;
            return view.Cast<object>().ElementAt(targetViewIndex) as JumpItem;
        }

        private void MoveSelection(int index)
        {
            if (lstFiles.Items.Count > 0)
            {
                if (index < 0)
                {
                    lstFiles.SelectedIndex = lstFiles.Items.Count - 1;
                }
                else
                {
                    lstFiles.SelectedIndex = index % lstFiles.Items.Count;
                }
                lstFiles.ScrollIntoView(lstFiles.SelectedItem);
            }
        }

        private async Task GoToAsync(JumpItem jumpItem)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var IDE = CMD.GetActiveIDE();
                var doc = IDE.Documents.Open(jumpItem.FullPath);

                while (IDE.ActiveDocument.FullName != jumpItem.FullPath)
                {
                    Debug.WriteLine("GoToAsync Wait: " + IDE.ActiveDocument.FullName);
                    await Task.Yield();
                }

                if (jumpItem.LineNumber >= 0)
                {
                    var textView = CMD.Instance.GetTextView();
                    if (textView != null)
                    {
                        textView.GetTextStream(0, 0, 13, 0, out string text);
                        Debug.WriteLine("GoToAsync: " + text);
                        textView.SetCaretPos(jumpItem.LineNumber, jumpItem.CaretColumn);
                        textView.CenterLines(jumpItem.LineNumber, 0);
                    }
                }

                RequestClose?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ExtensionErrorHandler.Log("Navigate quick-open item", ex);
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async Task LoadCodePreviewAsync(JumpItem jumpItem, CancellationToken cancellationToken)
        {
            if (jumpItem == null || string.IsNullOrEmpty(jumpItem.FullPath))
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                ClearPreview();
                return;
            }

            string path = jumpItem.FullPath;
            if (!File.Exists(path))
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                ShowPlaceholder($"// File not found: {path}");
                return;
            }

            string text;
            try
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length > MaxPreviewFileSizeBytes)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                    ShowPlaceholder($"// File too large to preview: {path}");
                    return;
                }

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (await IsBinaryAsync(stream, cancellationToken))
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                        ShowPlaceholder("// Binary file not previewable");
                        return;
                    }

                    using (var reader = new StreamReader(stream))
                    {
                        text = await reader.ReadToEndAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                ShowPlaceholder($"// Failed to load file: {ex.Message}");
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            codePreview.Text = text;
            codePreview.SyntaxHighlighting = CodePreviewManager.GetHighlightingDefinition(path);

            int targetLine = jumpItem.LineNumber > 0 ? jumpItem.LineNumber : -1;
            if (targetLine > 0)
            {
                int line = Math.Min(targetLine, codePreview.Document.LineCount);
                int column = jumpItem.CaretColumn >= 0 ? jumpItem.CaretColumn + 1 : 1;
                codePreview.ScrollToLine(line);
                codePreview.TextArea.Caret.Line = line;
                codePreview.TextArea.Caret.Column = column;
                codePreview.TextArea.Caret.BringCaretToView();
                CenterLine(line);
                HighlightTargetLine(line);
            }
            else
            {
                HighlightTargetLine(-1);
            }
        }

        private static async Task<bool> IsBinaryAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[1024];
            int read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == 0)
                    return true;
            }
            stream.Position = 0;
            return false;
        }

        private void ClearPreview()
        {
            codePreview.Clear();
            codePreview.SyntaxHighlighting = null;
            HighlightTargetLine(-1);
        }

        private void ShowPlaceholder(string message)
        {
            codePreview.Text = message;
            codePreview.SyntaxHighlighting = null;
            HighlightTargetLine(-1);
        }

        private void CenterLine(int lineNumber)
        {
            var textView = codePreview.TextArea.TextView;
            textView.EnsureVisualLines();
            var visualLine = textView.GetVisualLine(lineNumber);
            if (visualLine == null)
                return;

            double lineTop = visualLine.VisualTop;
            double lineHeight = visualLine.Height;
            double viewportHeight = textView.ActualHeight;
            double desiredOffset = lineTop + lineHeight / 2.0 - viewportHeight / 2.0;
            codePreview.ScrollToVerticalOffset(Math.Max(0, desiredOffset));
        }

        private void HighlightTargetLine(int lineNumber)
        {
            targetLineRenderer.TargetLine = lineNumber;
            codePreview.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }

        private void lstFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var jumpItem = lstFiles.SelectedItem as JumpItem;
            if (jumpItem == null)
            {
                SelectedItemFullPath = null;
                DebugString = null;
            }
            else
            {
                SelectedItemFullPath = jumpItem.FullPath;
                DebugString = jumpItem.DebugString;
            }

            UpdateDiagnosticOverlayVisibility();
            SchedulePreviewLoad(jumpItem);
        }

        private void SchedulePreviewLoad(JumpItem jumpItem)
        {
            previewLoadTimer?.Stop();
            previewLoadCts?.Cancel();
            previewLoadCts?.Dispose();
            previewLoadCts = new CancellationTokenSource();
            pendingPreviewItem = jumpItem;
            previewLoadTimer?.Start();
        }

        private void PreviewLoadTimer_Tick(object sender, EventArgs e)
        {
            previewLoadTimer.Stop();
            var item = pendingPreviewItem;
            var cts = previewLoadCts;
            _ = ExtensionErrorHandler.ExecuteAsync(() => LoadCodePreviewAsync(item, cts.Token), "Load code preview");
        }

        private void UpdateDiagnosticOverlayVisibility()
        {
            DiagnosticOverlayVisibility = LevyFlightOptions.Diagnostic ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnCodePreviewThemeChanged(object sender, EventArgs e)
        {
            ExtensionErrorHandler.Execute(() =>
            {
                CodePreviewManager.ApplyThemeToEditor(codePreview);
                var jumpItem = lstFiles.SelectedItem as JumpItem;
                if (jumpItem != null)
                {
                    codePreview.SyntaxHighlighting = CodePreviewManager.GetHighlightingDefinition(jumpItem.FullPath);
                    HighlightTargetLine(jumpItem.LineNumber > 0 ? jumpItem.LineNumber : -1);
                }
            }, "Apply code preview theme");
        }

        private void txtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            Filter.Instance.UpdateFilterString((sender as TextBox).Text);
            filterUpdateTimer.Stop();
            filterUpdateTimer.Start();
        }

        private void FilterUpdateTimer_Tick(object sender, EventArgs e)
        {
            ExtensionErrorHandler.Execute(() =>
            {
                filterUpdateTimer.Stop();
                foreach (var jumpItem in AllJumpItems)
                {
                    jumpItem.UpdateScore();
                }
                ViewSource.View.Refresh();
                RefreshQuickOpenIndices();
            }, "Quick-open filter update");
        }

        private readonly List<JumpItem> _previousQuickOpenItems = new List<JumpItem>();
        private void RefreshQuickOpenIndices()
        {
            foreach (var item in _previousQuickOpenItems)
            {
                item.QuickOpenIndex = -1;
            }
            _previousQuickOpenItems.Clear();

            var view = ViewSource?.View;
            if (view == null) return;

            int idx = 0;
            foreach (var obj in view)
            {
                if (idx >= 16) break;
                if (obj is JumpItem item)
                {
                    item.QuickOpenIndex = idx;
                    _previousQuickOpenItems.Add(item);
                    idx++;
                }
            }
        }

        private void txtFilter_KeyDown(object sender, KeyEventArgs e)
        {
            ExtensionErrorHandler.Execute(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                if (e.Key == Key.Down)
                {
                    e.Handled = true;
                    MoveSelection(lstFiles.SelectedIndex + 1);
                }
                else if (e.Key == Key.Up)
                {
                    e.Handled = true;
                    MoveSelection(lstFiles.SelectedIndex - 1);
                }
            }, "Quick-open filter key down");
        }

        private void lstFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var selectedJumpItem = lstFiles.SelectedItem as JumpItem;
            if (selectedJumpItem != null)
            {
                _ = GoToAsync(selectedJumpItem);
            }
        }

        private void UserControl_KeyDown(object sender, KeyEventArgs e)
        {
            ExtensionErrorHandler.Execute(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (e.Key == Key.Enter || e.Key == Key.Return)
                {
                    e.Handled = true;
                    var selectedJumpItem = lstFiles.SelectedItem as JumpItem;
                    if (selectedJumpItem != null)
                    {
                        _ = GoToAsync(selectedJumpItem);
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    RequestClose?.Invoke(this, EventArgs.Empty);
                }
                else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && windowsKeyBindings.ContainsKey(e.Key))
                {
                    e.Handled = windowsKeyBindings[e.Key]();
                }
                else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    var qi = GetQuickOpenItemForKey(e.Key);
                    if (qi != null)
                    {
                        e.Handled = true;
                        _ = GoToAsync(qi);
                    }
                }
            }, "Quick-open control key down");
        }

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            ExtensionErrorHandler.Execute(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    var qi = GetQuickOpenItemForKey(e.Key);
                    if (qi != null)
                    {
                        e.Handled = true;
                        _ = GoToAsync(qi);
                    }
                }
            }, "Quick-open control preview key down");
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            ExtensionErrorHandler.Execute(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                txtFilter.Focus();
            }, "Quick-open control loaded");
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            ExtensionErrorHandler.Execute(() =>
            {
                filterUpdateTimer?.Stop();
                previewLoadTimer?.Stop();
                previewLoadCts?.Cancel();
                previewLoadCts?.Dispose();
                CodePreviewManager.ThemeChanged -= OnCodePreviewThemeChanged;
            }, "Quick-open control unloaded");
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ExtensionErrorHandler.Execute(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var ownerWindow = Window.GetWindow(this);
                var window = new LevyFlightSettingsWindow
                {
                    Owner = ownerWindow
                };
                window.ShowDialog();
                UpdateDiagnosticOverlayVisibility();
            }, "Open Levy Flight settings");
        }
    }
}
