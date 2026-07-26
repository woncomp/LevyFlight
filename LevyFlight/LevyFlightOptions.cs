using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LevyFlight
{
    internal static class LevyFlightOptions
    {
        private const string DiagnosticKey = "Diagnostic";
        private const string DumpScannersKey = "DumpScanners";
        private const string TreeSitterEngineKey = "TreeSitterEngine";
        private const string ScannerOrderKey = "ScannerOrder";
        private static bool diagnostic;
        private static bool dumpScanners;
        private static TreeSitter.TreeSitterEngine treeSitterEngine = TreeSitter.TreeSitterEngine.Native;
        private static IReadOnlyList<Scanner> scannerOrder = ScannerCatalog.DefaultPriorityOrder;

        static LevyFlightOptions()
        {
            ScannerCatalog.ApplyPriorityOrder(scannerOrder);
        }

        public static IReadOnlyList<Scanner> ScannerOrder => scannerOrder;

        public static TreeSitter.TreeSitterEngine TreeSitterEngine
        {
            get { return treeSitterEngine; }
            set
            {
                treeSitterEngine = value;
                ExtensionErrorHandler.Execute(() =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    var settings = LevyFlightWindowCommand.Instance?.SettingsStore;
                    if (settings == null)
                        return;

                    settings.SetInt32(LevyFlightWindowCommand.SettingsCollectionName, TreeSitterEngineKey, (int)value);
                }, "Save TreeSitter engine option");
            }
        }

        public static bool Diagnostic
        {
            get { return diagnostic; }
            set
            {
                diagnostic = value;
                ExtensionErrorHandler.Execute(() =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    var settings = LevyFlightWindowCommand.Instance?.SettingsStore;
                    if (settings == null)
                        return;

                    settings.SetBoolean(LevyFlightWindowCommand.SettingsCollectionName, DiagnosticKey, value);
                }, "Save Diagnostic option");
            }
        }

        public static bool DumpScanners
        {
            get { return dumpScanners; }
            set
            {
                dumpScanners = value;
                ExtensionErrorHandler.Execute(() =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    var settings = LevyFlightWindowCommand.Instance?.SettingsStore;
                    if (settings == null)
                        return;

                    settings.SetBoolean(LevyFlightWindowCommand.SettingsCollectionName, DumpScannersKey, value);
                }, "Save DumpScanners option");
            }
        }

        public static void SetScannerOrder(IEnumerable<Scanner> scanners)
        {
            ApplyScannerOrder(ScannerCatalog.NormalizeOrder(scanners));
            ExtensionErrorHandler.Execute(() =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var settings = LevyFlightWindowCommand.Instance?.SettingsStore;
                if (settings == null)
                    return;

                settings.SetString(
                    LevyFlightWindowCommand.SettingsCollectionName,
                    ScannerOrderKey,
                    string.Join(";", scannerOrder.Select(scanner => scanner.Id)));
            }, "Save scanner order option");
        }

        public static void Load()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var settings = LevyFlightWindowCommand.Instance?.SettingsStore;
                if (settings == null)
                    return;

                diagnostic = settings.GetBoolean(LevyFlightWindowCommand.SettingsCollectionName, DiagnosticKey, false);
                dumpScanners = settings.GetBoolean(LevyFlightWindowCommand.SettingsCollectionName, DumpScannersKey, false);
                treeSitterEngine = (TreeSitter.TreeSitterEngine)settings.GetInt32(
                    LevyFlightWindowCommand.SettingsCollectionName, TreeSitterEngineKey, (int)TreeSitter.TreeSitterEngine.Native);

                string savedOrder = settings.GetString(LevyFlightWindowCommand.SettingsCollectionName, ScannerOrderKey, null);
                ApplyScannerOrder(ScannerCatalog.NormalizeOrder(
                    string.IsNullOrWhiteSpace(savedOrder)
                        ? null
                        : savedOrder.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)));
            }
            catch (Exception ex)
            {
                Logger.Error("Read Levy Flight options", ex);
            }
        }

        private static void ApplyScannerOrder(IReadOnlyList<Scanner> order)
        {
            scannerOrder = order;
            ScannerCatalog.ApplyPriorityOrder(scannerOrder);
        }
    }
}
