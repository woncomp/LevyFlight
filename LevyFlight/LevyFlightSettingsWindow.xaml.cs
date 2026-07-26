using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace LevyFlight
{
    public partial class LevyFlightSettingsWindow : Window
    {
        private ObservableCollection<Scanner> scannerOrder;

        public LevyFlightSettingsWindow()
        {
            InitializeComponent();
            DiagnosticCheckBox.IsChecked = LevyFlightOptions.Diagnostic;
            EngineComboBox.SelectedIndex = LevyFlightOptions.TreeSitterEngine == TreeSitter.TreeSitterEngine.Managed ? 1 : 0;
            scannerOrder = new ObservableCollection<Scanner>(LevyFlightOptions.ScannerOrder);
            ScannerOrderListBox.ItemsSource = scannerOrder;
            ScannerOrderListBox.SelectedIndex = scannerOrder.Count > 0 ? 0 : -1;
            UpdateMoveButtons();
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
                LevyFlightOptions.SetScannerOrder(scannerOrder);
                DialogResult = true;
            }, "Save Levy Flight settings");
        }

        private void ScannerOrderListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateMoveButtons();
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedScanner(-1);
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedScanner(1);
        }

        private void ResetOrderButton_Click(object sender, RoutedEventArgs e)
        {
            scannerOrder.Clear();
            foreach (Scanner scanner in ScannerCatalog.DefaultPriorityOrder)
            {
                scannerOrder.Add(scanner);
            }

            ScannerOrderListBox.SelectedIndex = scannerOrder.Count > 0 ? 0 : -1;
            UpdateMoveButtons();
        }

        private void MoveSelectedScanner(int offset)
        {
            int oldIndex = ScannerOrderListBox.SelectedIndex;
            int newIndex = oldIndex + offset;
            if (oldIndex < 0 || newIndex < 0 || newIndex >= scannerOrder.Count)
            {
                return;
            }

            Scanner scanner = scannerOrder[oldIndex];
            scannerOrder.RemoveAt(oldIndex);
            scannerOrder.Insert(newIndex, scanner);
            ScannerOrderListBox.SelectedIndex = newIndex;
            ScannerOrderListBox.ScrollIntoView(scanner);
            UpdateMoveButtons();
        }

        private void UpdateMoveButtons()
        {
            int index = ScannerOrderListBox?.SelectedIndex ?? -1;
            if (MoveUpButton != null)
            {
                MoveUpButton.IsEnabled = index > 0;
            }

            if (MoveDownButton != null)
            {
                MoveDownButton.IsEnabled = index >= 0 && index < scannerOrder?.Count - 1;
            }
        }
    }
}
