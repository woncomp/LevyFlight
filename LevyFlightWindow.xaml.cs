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
            get { return selectedItemFullPath ?? "Ctrl+J: Move Down | Ctrl+K: Move Up"; }
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
        }

        private void StartDiscoverFiles()
        {
            var knownFiles = new HashSet<string>();
            {// Ignore active file
                var activeFile = cmd.GetActiveFile();
                if (activeFile != null)
                {
                    knownFiles.Add(activeFile);
                }
            }

            // Add recent files
            var recentFiles1 = cmd.GetRecentFiles(10);
            foreach (var filePath in recentFiles1)
            {
                if (!knownFiles.Contains(filePath))
                {
                    var jumpItem = new JumpItem(Category.RecentFile, filePath);
                    AllJumpItems.Add(jumpItem);
                    knownFiles.Add(filePath);
                }
            }

            // Add active files
            var activeFiles = cmd.GetActiveFiles();
            foreach (var filePath in activeFiles)
            {
                if (!knownFiles.Contains(filePath))
                {
                    var jumpItem = new JumpItem(Category.ActiveFile, filePath);
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
                    if (knownFolders.Contains(currentFolder))
                    {
                        continue;
                    }
                    knownFolders.Add(currentFolder);
                    foreach (var filePath in Directory.GetFiles(currentFolder))
                    {
                        if (!knownFiles.Contains(filePath))
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
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
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
