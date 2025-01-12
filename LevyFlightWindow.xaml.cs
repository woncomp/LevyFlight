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

namespace LevyFlight
{
    using CMD = LevyFlightWindowCommand;

    /// <summary>
    /// Interaction logic for LevyFlightWindow.xaml
    /// </summary>
    public partial class LevyFlightWindow : Window, INotifyPropertyChanged
    {
        private static readonly char[] FILTER_SEPERATOR = { ' ' };

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<JumpItem> AllJumpItems { get; set; }

        public ObservableCollection<JumpItem> DisplayJumpItems { get; set; }

        public string SelectedItemFullPath
        {
            get { return selectedItemFullPath ?? "Ctrl+J: Move Down | Ctrl+K: Move Up"; }
            set
            {
                selectedItemFullPath = value;
                OnPropertyChanged();
            }
        }

        //private List<JumpItem> allJumpItems;

        private CMD cmd;

        private string[] filterStrings;
        private string[] filterStringsI;
        private string selectedItemFullPath;
        private bool initialScanning;
        private int activeFilteringTaskId;
        private JumpItem cachedLastValidItem;

        private Dictionary<Key, System.Func<bool>> windowsKeyBindings = new Dictionary<Key, Func<bool>>();

        internal LevyFlightWindow(CMD cmd)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            InitializeComponent();

            this.cmd = cmd;
            this.Owner = HwndSource.FromHwnd(CMD.GetActiveIDE().MainWindow.HWnd).RootVisual as System.Windows.Window;

            AllJumpItems = new ObservableCollection<JumpItem>();
            DisplayJumpItems = new ObservableCollection<JumpItem>();

            DataContext = this;

            initialScanning = true;
            activeFilteringTaskId = 0;

            SetupKeyBindings();
        }

        private void SetupKeyBindings()
        {
            windowsKeyBindings[Key.J] = () => { MoveSelection(lstFiles.SelectedIndex + 1); return true; };
            windowsKeyBindings[Key.K] = () => { MoveSelection(lstFiles.SelectedIndex - 1); return true; };
        }

        private async Task StartDiscoverFilesAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            foreach(var jumpItem in cmd.Bookmarks)
            {
                AllJumpItems.Add(jumpItem);
                DisplayJumpItems.Add(jumpItem);
            }
            await cmd.FindFilesAsync((filePath) =>
            {
                string fileName = System.IO.Path.GetFileName(filePath);
                var jumpItem = new JumpItem(fileName, filePath);
                AllJumpItems.Add(jumpItem);
                if (activeFilteringTaskId == 0)
                {
                    AddFileWithFilterMatching(jumpItem);
                }
            });
            initialScanning = false;
        }

        private async Task StartFilteringAsync()
        {
            int taskId = ++activeFilteringTaskId;
            DisplayJumpItems.Clear();

            var prevJumpItem = cachedLastValidItem;

            int currFileIndex = 0;
            while (activeFilteringTaskId == taskId)
            {
                if (currFileIndex < AllJumpItems.Count)
                {
                    var jumpItem = AllJumpItems[currFileIndex];

                    bool _passFilter = AddFileWithFilterMatching(jumpItem);
                    //if (jumpItem == prevJumpItem)
                    //{
                    //    if (passFilter)
                    //    {
                    //        lstFiles.SelectedItem = jumpItem;
                    //    }
                    //    else
                    //    {
                    //        prevJumpItem = null;
                    //    }
                    //}

                    currFileIndex++;
                    //if (currFileIndex % 2 == 0)
                    {
                        await Task.Yield();
                    }
                }
                else if (initialScanning)
                {
                    await Task.Yield();
                }
                else
                {
                    break;
                }
            }

            if (prevJumpItem == null && lstFiles.Items.Count > 0)
            {
                lstFiles.SelectedIndex = 0;
            }
        }

        private bool AddFileWithFilterMatching(JumpItem jumpItem)
        {
            if (filterStringsI == null || filterStringsI.Length == 0)
            {
                DisplayJumpItems.Add(jumpItem);
                return true;
            }

            int numMatchKeywordsCaseInsensitive = 0;
            int matchCharPercentCaseInsensitive = 0;
            {
                string itemNameI = jumpItem.Name.ToLower();
                foreach (var keyword in filterStringsI)
                {
                    if (itemNameI.Contains(keyword))
                    {
                        numMatchKeywordsCaseInsensitive++;
                        matchCharPercentCaseInsensitive += keyword.Length;
                    }
                }
                matchCharPercentCaseInsensitive = Math.Max(0, matchCharPercentCaseInsensitive * 20 / itemNameI.Length - 10); // Map 50%~100% to 0~10
            }

            int numMatchKeywordsCaseSensitive = 0;
            int matchCharPercentCaseSensitive = 0;
            {
                string itemName = jumpItem.Name;
                foreach (var keyword in filterStrings)
                {
                    if (itemName.Contains(keyword))
                    {
                        numMatchKeywordsCaseSensitive++;
                        matchCharPercentCaseSensitive += keyword.Length;
                    }
                }
                matchCharPercentCaseSensitive = Math.Max(0, matchCharPercentCaseSensitive * 20 / itemName.Length - 10); // Map 50%~100% to 0~10
            }

            int numFullPathMatchKeywordsCaseInsensitive = 0;
            int matchFullPathCharPercentCaseInsensitive = 0;
            {
                string fullPath = jumpItem.FullPath.ToLower();
                foreach (var keyword in filterStringsI)
                {
                    if (fullPath.Contains(keyword))
                    {
                        numFullPathMatchKeywordsCaseInsensitive++;
                        matchFullPathCharPercentCaseInsensitive += keyword.Length;
                    }
                }
                matchFullPathCharPercentCaseInsensitive = matchFullPathCharPercentCaseInsensitive * 100 / fullPath.Length;
            }

            // Build weight and multiplier pairs;
            var scorePairs = new List<(int, int)>
            {
                // Match whole keywords on item name, case sensitive or insensitive
                (10000, numMatchKeywordsCaseInsensitive),
                (1000, matchCharPercentCaseInsensitive),
                (100, numMatchKeywordsCaseSensitive),
                (10, matchCharPercentCaseSensitive),

                // Match whole keywords on item full path, case insensitive
                (10, numFullPathMatchKeywordsCaseInsensitive),
                (1, matchFullPathCharPercentCaseInsensitive),
            };

            int score = scorePairs.Select(x => x.Item1 * x.Item2).Sum();
            jumpItem.Score = score;

            if (score <= 0)
            {
                return false;
            }

            int insertIndex = 0;
            while (insertIndex < DisplayJumpItems.Count && DisplayJumpItems[insertIndex].Score >= score)
            {
                insertIndex++;
            }
            DisplayJumpItems.Insert(insertIndex, jumpItem);
            return true;
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

            while(IDE.ActiveDocument.FullName != jumpItem.FullPath)
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
            }
            else
            {
                SelectedItemFullPath = jumpItem.FullPath;
                //cachedLastValidItem = jumpItem;
            }
        }

        private void txtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            filterStrings = (sender as TextBox).Text.Split(FILTER_SEPERATOR, StringSplitOptions.RemoveEmptyEntries).Select(str => str.Trim()).ToArray();
            filterStringsI = filterStrings.Select(str => str.ToLower()).ToArray();

            _ = StartFilteringAsync();
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

            _ = StartDiscoverFilesAsync();
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            LoadWindowSettings();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveWindowSettings();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var jumpItem = cmd.AddBookmarkFromCurrentPosition();
            AllJumpItems.Add(jumpItem);
            DisplayJumpItems.Insert(0, jumpItem);
        }
    }
}
