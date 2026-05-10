using Microsoft.VisualStudio.Shell;
using System;
using System.Windows;

namespace LevyFlight
{
    public partial class LevyFlightSettingsWindow : Window
    {
        public LevyFlightSettingsWindow()
        {
            InitializeComponent();
            DiagnosticCheckBox.IsChecked = LevyFlightOptions.Diagnostic;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ExtensionErrorHandler.Execute(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                LevyFlightOptions.Diagnostic = DiagnosticCheckBox.IsChecked == true;
                DialogResult = true;
            }, "Save Levy Flight settings");
        }
    }
}
