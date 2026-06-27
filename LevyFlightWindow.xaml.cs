using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace LevyFlight
{
    using CMD = LevyFlightWindowCommand;

    /// <summary>
    /// Lightweight window shell that displays a loading indicator while the real
    /// quick-open UI (LevyFlightQuickOpenControl) is constructed and initialized.
    /// </summary>
    public partial class LevyFlightWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public bool IsLoading
        {
            get { return isLoading; }
            set
            {
                isLoading = value;
                OnPropertyChanged();
            }
        }

        public bool HasError
        {
            get { return hasError; }
            set
            {
                hasError = value;
                OnPropertyChanged();
            }
        }

        public string ErrorMessage
        {
            get { return errorMessage; }
            set
            {
                errorMessage = value;
                OnPropertyChanged();
            }
        }

        private bool isLoading = true;
        private bool hasError;
        private string errorMessage;

        internal LevyFlightWindow(CMD cmd)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            InitializeComponent();

            // VS's main window is hosted in a shell hwnd whose RootVisual is NOT a
            // System.Windows.Window, so the cast frequently yields null. Walk up the
            // visual tree to find the nearest Window; if none is found, the
            // WindowStartupLocation (CenterScreen) set in XAML still positions us.
            System.Windows.Window ownerWindow = null;
            var hwnd = CMD.GetActiveIDE().MainWindow.HWnd;
            var ownerVisual = hwnd != IntPtr.Zero ? HwndSource.FromHwnd(hwnd)?.RootVisual : null;
            if (ownerVisual != null)
            {
                ownerWindow = ownerVisual as System.Windows.Window
                              ?? System.Windows.Media.VisualTreeHelper.GetParent(ownerVisual)
                                 as System.Windows.Window;
            }
            this.Owner = ownerWindow;
            // Intentionally no Topmost: a modal dialog that gets mispositioned must
            // stay Alt+Tab-reachable instead of becoming an invisible topmost ghost
            // that locks the UI thread.

            // Apply persisted size before ShowDialog so WindowStartupLocation centers
            // using the correct size rather than the XAML defaults.
            LoadWindowSettings();

            DataContext = this;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = ExtensionErrorHandler.ExecuteAsync(InitializeAsync, "Initialize Levy Flight window");
        }

        private async Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            await Task.Yield();
            try
            {
                var control = new LevyFlightQuickOpenControl();
                control.RequestClose += (_, __) => Close();
                await control.InitializeAsync();
                contentHost.Content = control;
                IsLoading = false;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to load Levy Flight: {ex.Message}";
                HasError = true;
                IsLoading = false;
            }
        }

        /// <summary>
        /// Restores the window's previous size. Positioning is left to WPF via
        /// WindowStartupLocation (CenterScreen) so that we never mix Screen.Bounds
        /// (physical pixels) with WPF Left/Top (DIP) — which under high DPI scaling
        /// placed the window off-screen and froze VS.
        /// </summary>
        private void LoadWindowSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var settings = CMD.Instance.SettingsStore;
            const string COLL = CMD.SettingsCollectionName;

            // Default to a sensible fraction of the owner (or primary screen) size,
            // expressed entirely in DIP so it stays correct under any DPI scaling.
            double fallbackWidth = Owner != null ? Owner.Width * 2 / 3 : SystemParameters.PrimaryScreenWidth * 2 / 3;
            double fallbackHeight = Owner != null ? Owner.Height * 1 / 2 : SystemParameters.PrimaryScreenHeight * 1 / 2;

            this.Width = settings.GetInt32(COLL, "WindowWidth", (int)fallbackWidth);
            this.Height = settings.GetInt32(COLL, "WindowHeight", (int)fallbackHeight);

            this.WindowState = WindowState.Normal;
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

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            ExtensionErrorHandler.Execute(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                // Size is applied in the constructor; WPF positions the window via
                // WindowStartupLocation. Nothing to do here.
            }, "Quick-open window source initialized");
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            ExtensionErrorHandler.Execute(() =>
            {
                SaveWindowSettings();
                Filter.Instance.Reset();
            }, "Quick-open window closing");
        }
    }
}
