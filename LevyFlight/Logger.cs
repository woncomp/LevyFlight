using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace LevyFlight
{
    internal static class Logger
    {
        private static IVsOutputWindowPane _pane;
        private static Guid PaneGuid = new Guid("B7C3D2E1-4F5A-4B6C-8D9E-0F1A2B3C4D5E");

        public static void Initialize(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var outputWindow = serviceProvider.GetService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (outputWindow == null)
                return;

            outputWindow.CreatePane(ref PaneGuid, "LevyFlight", 1, 1);
            outputWindow.GetPane(ref PaneGuid, out _pane);
        }

        public static void Log(string message)
        {
            if (_pane == null)
                return;

            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    _pane?.OutputString($"[LevyFlight] {DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
                });
            }
            catch
            {
            }
        }

        public static void Error(string message, Exception ex = null)
        {
            Log($"ERROR: {message} {ex?.Message}");
        }
    }
}
