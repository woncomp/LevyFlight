using Microsoft.VisualStudio.Shell;
using System;
using System.Diagnostics;

namespace LevyFlight
{
    internal static class LevyFlightOptions
    {
        private const string DiagnosticKey = "Diagnostic";
        private static bool diagnostic;

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
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[LevyFlight] Read Diagnostic option failed: " + ex.Message);
            }
        }
    }
}
