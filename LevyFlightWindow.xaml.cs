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

            this.Owner = HwndSource.FromHwnd(CMD.GetActiveIDE().MainWindow.HWnd).RootVisual as System.Windows.Window;

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
                LoadWindowSettings();
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
