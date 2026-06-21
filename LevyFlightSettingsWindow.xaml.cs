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
            EngineComboBox.SelectedIndex = LevyFlightOptions.TreeSitterEngine == TreeSitter.TreeSitterEngine.Managed ? 1 : 0;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ExtensionErrorHandler.Execute(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                LevyFlightOptions.Diagnostic = DiagnosticCheckBox.IsChecked == true;
                LevyFlightOptions.TreeSitterEngine = EngineComboBox.SelectedIndex == 1
                    ? TreeSitter.TreeSitterEngine.Managed
                    : TreeSitter.TreeSitterEngine.Native;
                DialogResult = true;
            }, "Save Levy Flight settings");
        }
    }
}
