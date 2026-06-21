using Microsoft.VisualStudio.Shell;
using System;
using System.Diagnostics;

namespace LevyFlight
{
    internal static class LevyFlightOptions
    {
        private const string DiagnosticKey = "Diagnostic";
        private const string TreeSitterEngineKey = "TreeSitterEngine";
        private static bool diagnostic;
        private static TreeSitter.TreeSitterEngine treeSitterEngine = TreeSitter.TreeSitterEngine.Native;

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

        public static void Load()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var settings = LevyFlightWindowCommand.Instance?.SettingsStore;
                if (settings == null)
                    return;

                diagnostic = settings.GetBoolean(LevyFlightWindowCommand.SettingsCollectionName, DiagnosticKey, false);
                treeSitterEngine = (TreeSitter.TreeSitterEngine)settings.GetInt32(
                    LevyFlightWindowCommand.SettingsCollectionName, TreeSitterEngineKey, (int)TreeSitter.TreeSitterEngine.Native);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[LevyFlight] Read Diagnostic option failed: " + ex.Message);
            }
        }
    }
}
