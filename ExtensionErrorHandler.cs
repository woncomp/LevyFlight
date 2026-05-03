using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LevyFlight
{
    internal static class ExtensionErrorHandler
    {
        public static void Execute(Action action, string context)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log(context, ex);
            }
        }

        public static async Task ExecuteAsync(Func<Task> action, string context)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Log(context, ex);
            }
        }

        public static void Log(string context, Exception ex)
        {
            Debug.WriteLine("[LevyFlight] " + context + ": " + ex);
        }

        public static bool LooksLikeLevyFlight(Exception ex)
        {
            if (ex == null)
                return false;

            Type type = ex.TargetSite?.DeclaringType;
            if (type?.Namespace != null && type.Namespace.StartsWith("LevyFlight", StringComparison.Ordinal))
                return true;

            string stack = ex.ToString();
            return stack.IndexOf("LevyFlight", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
