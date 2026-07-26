using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
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

        public List<PresetViewModel> PresetViewModels { get; private set; }

        private bool isCtrlPressed;
        public bool IsCtrlPressed
        {
            get => isCtrlPressed;
            set
            {
                isCtrlPressed = value;
                OnPropertyChanged();
                UpdateKeyTips(value);
            }
        }

        private void UpdateKeyTips(bool show)
        {
            if (show)
            {
                ShowKeyTips();
            }
            else
            {
                HideKeyTips();
            }
        }

        private void ShowKeyTips()
        {
            if (PresetItemsControl == null || PresetViewModels == null)
            {
                return;
            }

            foreach (var preset in PresetViewModels)
            {
                if (activeKeyTips.ContainsKey(preset))
                {
                    continue;
                }

                var radioButton = FindRadioButtonForPreset(preset);
                if (radioButton == null)
                {
                    continue;
                }

                var adornerLayer = AdornerLayer.GetAdornerLayer(radioButton);
                if (adornerLayer == null)
                {
                    continue;
                }

                var adorner = new KeyTipAdorner(radioButton, preset.ShortcutLetter);
                adornerLayer.Add(adorner);
                activeKeyTips[preset] = adorner;
            }
        }

        private void HideKeyTips()
        {
            foreach (var pair in activeKeyTips)
            {
                var adornerLayer = AdornerLayer.GetAdornerLayer(pair.Value.Target);
                if (adornerLayer != null)
                {
                    adornerLayer.Remove(pair.Value);
                }
            }
            activeKeyTips.Clear();
        }

        private RadioButton FindRadioButtonForPreset(PresetViewModel preset)
        {
            var container = PresetItemsControl.ItemContainerGenerator.ContainerFromItem(preset);
            if (container == null)
            {
                return null;
            }
            return FindVisualChild<RadioButton>(container);
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private PresetViewModel currentPreset;

        private DispatcherTimer filterUpdateTimer;
        private DispatcherTimer previewLoadTimer;
        private CancellationTokenSource previewLoadCts;
        private TargetLineRenderer targetLineRenderer;

        private string debugString;
        private string selectedItemFullPath;
        private Visibility diagnosticOverlayVisibility = Visibility.Collapsed;
        private JumpItem pendingPreviewItem;

        private Dictionary<Key, System.Func<bool>> windowsKeyBindings = new Dictionary<Key, Func<bool>>();

        private readonly Dictionary<PresetViewModel, KeyTipAdorner> activeKeyTips = new Dictionary<PresetViewModel, KeyTipAdorner>();

        private const long MaxPreviewFileSizeBytes = 2 * 1024 * 1024; // 2 MB
        private const int PreviewLoadDebounceMs = 75;

        public LevyFlightQuickOpenControl()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            InitializeComponent();
            Loaded += UserControl_Loaded;
            Unloaded += UserControl_Unloaded;
            PreviewKeyDown += UserControl_PreviewKeyDown;
            KeyDown += UserControl_KeyDown;
            KeyUp += UserControl_KeyUp;
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
            InitializePresets();

            await Task.Yield();

            await StartDiscoverFilesAsync();
        }

        private void ViewSource_Filter(object sender, FilterEventArgs e)
        {
            JumpItem jumpItem = e.Item as JumpItem;
            if (jumpItem.Score <= 0)
            {
                e.Accepted = false;
                return;
            }

            if (currentPreset != null && !currentPreset.IncludeAll &&
                !currentPreset.IncludedScanners.Contains(jumpItem.Scanner))
            {
                e.Accepted = false;
                return;
            }

            e.Accepted = true;
        }

        private void SetupKeyBindings()
        {
            windowsKeyBindings[Key.J] = () => { MoveSelection(lstFiles.SelectedIndex + 1); return true; };
            windowsKeyBindings[Key.K] = () => { MoveSelection(lstFiles.SelectedIndex - 1); return true; };
            windowsKeyBindings[Key.D] = () => { FastMove(+1); return true; }; // Ctrl+D half page down
            windowsKeyBindings[Key.U] = () => { FastMove(-1); return true; }; // Ctrl+U half page up
        }

        private void InitializePresets()
        {
            PresetViewModels = QuickOpenPreset.DefaultPresets.Select(p => new PresetViewModel
            {
                Name = p.Name,
                ShortcutKey = p.ShortcutKey,
                ShortcutLetter = p.ShortcutLetter,
                IncludedScanners = p.IncludedScanners,
                IsActive = p.Name == "All In One",
            }).ToList();

            currentPreset = PresetViewModels.First(p => p.Name == "All In One");
        }

        private void ActivatePreset(PresetViewModel preset)
        {
            if (currentPreset == preset) return;

            currentPreset.IsActive = false;
            preset.IsActive = true;
            currentPreset = preset;

            using (ViewSource.DeferRefresh())
            {
                ViewSource.View.Refresh();
            }
            RefreshQuickOpenIndices();
            txtFilter.Focus();
        }

        private void SelectPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PresetViewModel preset)
            {
                ActivatePreset(preset);
            }
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

        private async Task StartDiscoverFilesAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var context = new ScannerContext(CMD.Instance.GetCurrentFile());
            ScannerDump scannerDump = LevyFlightOptions.DumpScanners ? ScannerDump.StartNew(context) : null;
            if (scannerDump != null)
            {
                context.EnableClaimTracking();
                context.DumpSession = scannerDump;
            }
            await AddScannerItemsAsync(ScannerCatalog.RecentEdit, context, false);
            await AddScannerItemsAsync(ScannerCatalog.HotFile, context, false);
            await AddScannerItemsAsync(ScannerCatalog.Transition, context, false);
            await AddScannerItemsAsync(ScannerCatalog.OpenFile, context, false);
            await AddScannerItemsAsync(ScannerCatalog.RecentFile, context, false);
            await AddScannerItemsAsync(ScannerCatalog.ActiveDirectoryFile, context, false);
            await AddScannerItemsAsync(ScannerCatalog.Bookmark, context, false);
            await AddScannerItemsAsync(ScannerCatalog.FavoriteFile, context, false);
            RefreshQuickOpenIndices();

            if (!string.IsNullOrEmpty(context.CurrentFile))
            {
                _ = ExtensionErrorHandler.ExecuteAsync(() => AddScannerItemsAsync(ScannerCatalog.TreeSitter, context, false, true), "Add Tree-sitter quick-open items");
            }

            // Start scanning the entire solution a little later
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(0.1);
            timer.Tick += (_, e2) =>
            {
                ExtensionErrorHandler.Execute(() =>
                {
                    timer.Stop();
                    _ = ExtensionErrorHandler.ExecuteAsync(() => StartProjectScannerItemsAsync(context), "Discover solution files");
                }, "Start delayed solution discovery");
            };
            timer.Start();
        }

        private void AddScannerItems(IEnumerable<JumpItem> items)
        {
            using (ViewSource.DeferRefresh())
            {
                foreach (var item in items)
                {
                    AllJumpItems.Add(item);
                }
            }
        }

        private async Task AddScannerItemsAsync(Scanner scanner, ScannerContext context, bool batchResults, bool discardIfHidden = false)
        {
            ScannerDumpSection dump = context.DumpSession?.BeginScanner(scanner) ?? ScannerDumpSection.Null;
            try
            {
                IEnumerable<JumpItem> items = await scanner.ScanAsync(context, dump);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (discardIfHidden && !IsVisible)
                {
                    return;
                }

                List<JumpItem> stagingList = new List<JumpItem>();
                foreach (JumpItem item in items)
                {
                    stagingList.Add(item);
                    if (batchResults && stagingList.Count >= 2000)
                    {
                        AddScannerItems(stagingList);
                        stagingList.Clear();
                        DebugString = $"Files:{AllJumpItems.Count}";
                        RefreshQuickOpenIndices();
                        await Task.Yield();
                    }
                    if (discardIfHidden && !IsVisible)
                    {
                        return;
                    }
                }
                if (stagingList.Count > 0)
                {
                    AddScannerItems(stagingList);
                    DebugString = $"Files:{AllJumpItems.Count}";
                }
                RefreshQuickOpenIndices();
            }
            finally
            {
                dump.Complete();
            }
        }

        private async Task StartProjectScannerItemsAsync(ScannerContext context)
        {
            await AddScannerItemsAsync(ScannerCatalog.CurrentProjectFile, context, true, true);
            if (!IsVisible)
            {
                return;
            }

            await AddScannerItemsAsync(ScannerCatalog.ActiveProjectFile, context, true, true);
            if (!IsVisible)
            {
                return;
            }

            await AddScannerItemsAsync(ScannerCatalog.SolutionFile, context, true, true);
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
                    await Task.Yield();
                }

                if (jumpItem.LineNumber >= 0)
                {
                    var textView = CMD.Instance.GetTextView();
                    if (textView != null)
                    {
                        textView.GetTextStream(0, 0, 13, 0, out string text);
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

            // For files open in VS, preview the live buffer (including unsaved edits) so the
            // text matches the edit-region coordinates; other files are read from disk.
            var openSnapshot = RecentEditCollector.GetOpenDocumentSnapshot(path);
            List<RecentEditCollector.EditRegion> fileRegions = null;
            string text;

            if (openSnapshot != null)
            {
                if (openSnapshot.Length > MaxPreviewFileSizeBytes)
                {
                    ShowPlaceholder($"// File too large to preview: {path}");
                    return;
                }
                text = openSnapshot.GetText();
                fileRegions = RecentEditCollector.GetRegionsForFile(path);
            }
            else
            {
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
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            codePreview.Text = text;
            codePreview.SyntaxHighlighting = CodePreviewManager.GetHighlightingDefinition(path);

            // Highlight every recent edit region of the file, not just the item's own region.
            targetLineRenderer.Ranges.Clear();
            if (fileRegions != null)
            {
                foreach (var region in fileRegions)
                {
                    int rangeStart = Math.Min(region.StartLine + 1, codePreview.Document.LineCount);
                    int rangeEnd = Math.Min(region.EndLine + 1, codePreview.Document.LineCount);
                    if (rangeStart <= rangeEnd)
                    {
                        targetLineRenderer.Ranges.Add((rangeStart, rangeEnd));
                    }
                }
            }

            // LineNumber is a 0-based VS line; AvalonEdit lines are 1-based.
            int targetLine = jumpItem.LineNumber >= 0 ? jumpItem.LineNumber + 1 : -1;
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
            targetLineRenderer.Ranges.Clear();
            HighlightTargetLine(-1);
        }

        private void ShowPlaceholder(string message)
        {
            codePreview.Text = message;
            codePreview.SyntaxHighlighting = null;
            targetLineRenderer.Ranges.Clear();
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
                    HighlightTargetLine(jumpItem.LineNumber >= 0 ? jumpItem.LineNumber + 1 : -1);
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

                // Update Ctrl state for shortcut overlay
                IsCtrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

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
                else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    // Check preset shortcuts first
                    var preset = PresetViewModels?.FirstOrDefault(p => p.ShortcutKey == e.Key);
                    if (preset != null)
                    {
                        ActivatePreset(preset);
                        e.Handled = true;
                        return;
                    }

                    // Then check window key bindings (J/K/D/U)
                    if (windowsKeyBindings.ContainsKey(e.Key))
                    {
                        e.Handled = windowsKeyBindings[e.Key]();
                        return;
                    }

                    // Then check quick open item keys
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

                // Update Ctrl state for shortcut overlay
                IsCtrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    // Check preset shortcuts first
                    var preset = PresetViewModels?.FirstOrDefault(p => p.ShortcutKey == e.Key);
                    if (preset != null)
                    {
                        ActivatePreset(preset);
                        e.Handled = true;
                        return;
                    }

                    // Then check quick open item keys
                    var qi = GetQuickOpenItemForKey(e.Key);
                    if (qi != null)
                    {
                        e.Handled = true;
                        _ = GoToAsync(qi);
                    }
                }
            }, "Quick-open control preview key down");
        }

        private void UserControl_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
            {
                IsCtrlPressed = false;
            }
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
                HideKeyTips();
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
                if (window.ShowDialog() == true)
                {
                    foreach (JumpItem jumpItem in AllJumpItems)
                    {
                        jumpItem.UpdateScore();
                    }

                    ViewSource.View.Refresh();
                    RefreshQuickOpenIndices();
                }
                UpdateDiagnosticOverlayVisibility();
            }, "Open Levy Flight settings");
        }
    }
}
