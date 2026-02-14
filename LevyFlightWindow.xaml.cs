using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;

namespace LevyFlight
{
    using CMD = LevyFlightWindowCommand;

    /// <summary>
    /// Interaction logic for LevyFlightWindow.xaml
    /// </summary>
    public partial class LevyFlightWindow : Window, INotifyPropertyChanged
    {
        public static readonly Key[] QuickOpenKeys = new Key[]
        {
            Key.D1,
            Key.D2,
            Key.D3,
            Key.D4,
            Key.D5,
            Key.D6,
            Key.D7,
            Key.D8,
            Key.D9,
            Key.Q,
            Key.W,
            Key.E,
            Key.R,
            Key.T,
            Key.Y,
        };

        public event PropertyChangedEventHandler PropertyChanged;

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

        private CMD cmd;
        private DispatcherTimer filterUpdateTimer;

        private string debugString;
        private string selectedItemFullPath;

        private Dictionary<Key, System.Func<bool>> windowsKeyBindings = new Dictionary<Key, Func<bool>>();

        internal LevyFlightWindow(CMD cmd)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            InitializeComponent();

            this.cmd = cmd;
            this.Owner = HwndSource.FromHwnd(CMD.GetActiveIDE().MainWindow.HWnd).RootVisual as System.Windows.Window;

            AllJumpItems = new ObservableCollection<JumpItem>();

            ViewSource = new CollectionViewSource();
            ViewSource.Source = AllJumpItems;
            ViewSource.Filter += ViewSource_Filter;
            ViewSource.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));

            DebugString = "";

            DataContext = this;

            filterUpdateTimer = new DispatcherTimer();
            filterUpdateTimer.Interval = TimeSpan.FromSeconds(0.3);
            filterUpdateTimer.Tick += FilterUpdateTimer_Tick;

            SetupKeyBindings();
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
            // Compute average item height from visible realized containers
            double avgItemHeight = 18; // default fallback
            int firstVisibleIndex = lstFiles.SelectedIndex >= 0 ? lstFiles.SelectedIndex : 0;
            // Try to refine: search for first realized container above or at selection
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

            // Align scrolling with target so target appears roughly half-page from previous position
            if (sv != null)
            {
                bool logicalScrolling = sv.CanContentScroll; // if true, VerticalOffset is in items, else pixels
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

            var currentFile = cmd.GetCurrentFile();
            var knownFiles = new HashSet<string>();
            {// Ignore active file
                if (!string.IsNullOrEmpty(currentFile))
                {
                    knownFiles.Add(currentFile);
                }
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
            var activeFiles = cmd.GetActiveFiles();
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
            foreach (var jumpItem in cmd.Bookmarks)
            {
                AllJumpItems.Add(jumpItem);
            }

            // Kick off tree-sitter C++ parse of the active document
            if (!string.IsNullOrEmpty(currentFile))
            {
                _ = TreeSitterCodeParser.ParseAndListFunctionsAsync(currentFile).ContinueWith(async t =>
                {
                    var items = t.Result;
                    if (items.Count > 0)
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        using (ViewSource.DeferRefresh())
                        {
                            foreach (var item in items)
                            {
                                AllJumpItems.Add(item);
                            }
                        }
                        RefreshQuickOpenIndices();
                    }
                }, TaskScheduler.Default);
            }

            // Start scaning the entire solution a little later
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(0.1);
            timer.Tick += (_, e2) =>
            {
                timer.Stop();
                _ = StartDiscoverFilesAsync(knownFiles);
            };
            timer.Start();
        }

        private async Task StartDiscoverFilesAsync(HashSet<string> knownFiles)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            List<JumpItem> stagingList = new List<JumpItem>();
            foreach (var (category, filePath) in cmd.EnumerateSolutionFiles(knownFiles))
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
            };
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
            // QuickOpenKeys[0] maps to view item at position 1 (second item)
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
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var IDE = CMD.GetActiveIDE();
            //IDE.ItemOperations.OpenFile(jumpItem.FullPath);

            var doc = IDE.Documents.Open(jumpItem.FullPath);

            while (IDE.ActiveDocument.FullName != jumpItem.FullPath)
            {
                Debug.WriteLine("GoToAsync Wait: " + IDE.ActiveDocument.FullName);
                await Task.Yield();
            }

            if (jumpItem.LineNumber >= 0)
            {
                var textView = cmd.GetTextView();
                textView.GetTextStream(0, 0, 13, 0, out string text);
                Debug.WriteLine("GoToAsync: " + text);
                textView.SetCaretPos(jumpItem.LineNumber, jumpItem.CaretColumn);
                textView.CenterLines(jumpItem.LineNumber, 0);
            }

            Close();
        }

        /// <summary>
        /// Restores the window's previous size and position
        /// </summary>
        private void LoadWindowSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var settings = CMD.Instance.SettingsStore;

            const string COLL = CMD.SettingsCollectionName;

            double width = settings.GetInt32(COLL, "WindowWidth", (int)(Owner.Width * 2 / 3));
            double height = settings.GetInt32(COLL, "WindowHeight", (int)(Owner.Height * 1 / 2));

            var screenBounds = System.Windows.Forms.Screen.FromHandle(CMD.GetActiveIDE().MainWindow.HWnd).Bounds;

            this.Left = screenBounds.Left + screenBounds.Width / 2 - width / 2;
            this.Top = screenBounds.Top + screenBounds.Height / 2 - height / 2;
            this.Width = width;
            this.Height = height;

            this.WindowState = (WindowState)settings.GetInt32(COLL, "WindowState", 0);
        }

        /// <summary>
        /// Saves the window's current size and position
        /// </summary>
        private void SaveWindowSettings()
        {
            var settings = CMD.Instance.SettingsStore;

            const string COLL = CMD.SettingsCollectionName;

            settings.SetInt32(COLL, "WindowWidth", (int)this.Width);
            settings.SetInt32(COLL, "WindowHeight", (int)this.Height);
            settings.SetInt32(COLL, "WindowState", (int)this.WindowState);
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
        }

        private void txtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            Filter.Instance.UpdateFilterString((sender as TextBox).Text);

            filterUpdateTimer.Stop();
            filterUpdateTimer.Start();
        }

        private void FilterUpdateTimer_Tick(object sender, EventArgs e)
        {
            filterUpdateTimer.Stop();
            foreach (var jumpItem in AllJumpItems)
            {
                jumpItem.UpdateScore();
            }
            ViewSource.View.Refresh();
            RefreshQuickOpenIndices();
        }

        /// <summary>
        /// Assigns QuickOpenIndex to the first 16 visible items in the sorted view.
        /// Index 0 = first item (no hotkey), 1..15 = hotkey labels.
        /// All other items get -1.
        /// Must be called after every ViewSource.View.Refresh().
        /// </summary>
        private readonly List<JumpItem> _previousQuickOpenItems = new List<JumpItem>();
        private void RefreshQuickOpenIndices()
        {
            // Reset previously assigned items
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
        }

        private void lstFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var selectedJumpItem = lstFiles.SelectedItem as JumpItem;
            if (selectedJumpItem != null)
            {
                _ = GoToAsync(selectedJumpItem);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
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
                Close();
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
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // Intercept before controls like TextBox process (e.g., Ctrl+Y = Redo)
                var qi = GetQuickOpenItemForKey(e.Key);
                if (qi != null)
                {
                    e.Handled = true;
                    _ = GoToAsync(qi);
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            txtFilter.Focus();
            StartDiscoverFiles();
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            LoadWindowSettings();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveWindowSettings();
            Filter.Instance.Reset();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var jumpItem = cmd.AddBookmarkFromCurrentPosition();
            AllJumpItems.Add(jumpItem);
        }
    }
}
