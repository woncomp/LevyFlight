using System;
using System.IO;
using System.Text;

namespace LevyFlight
{
    /// <summary>
    /// One dump file per quick-open window session. Created only when the
    /// "Dump Scanners" option is enabled. Each scanner writes one section.
    /// </summary>
    internal sealed class ScannerDump
    {
        private readonly object _lock = new object();
        private readonly string _filePath;
        private bool _firstSection = true;

        private ScannerDump(string filePath)
        {
            _filePath = filePath;
        }

        public string FilePath => _filePath;

        public static string GetDumpDirectory()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "LevyFlight", "ScannerDumps");
        }

        public static ScannerDump StartNew(ScannerContext context)
        {
            try
            {
                string directory = GetDumpDirectory();
                Directory.CreateDirectory(directory);
                string filePath = Path.Combine(directory, DateTime.Now.ToString("yyMMdd-HHmmss-fff") + "-Scan.txt");
                var dump = new ScannerDump(filePath);
                dump.WriteHeader(context);
                Logger.Log("Scanner dump: " + filePath);
                return dump;
            }
            catch (Exception ex)
            {
                Logger.Error("Start scanner dump", ex);
                return null;
            }
        }

        public ScannerDumpSection BeginScanner(Scanner scanner)
        {
            return new ScannerDumpSection(this, scanner);
        }

        internal void WriteSection(string content)
        {
            try
            {
                lock (_lock)
                {
                    if (!_firstSection)
                    {
                        File.AppendAllText(_filePath, Environment.NewLine + Environment.NewLine, Encoding.UTF8);
                    }
                    _firstSection = false;
                    File.AppendAllText(_filePath, content, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Write scanner dump section", ex);
            }
        }

        private void WriteHeader(ScannerContext context)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Levy Flight Scanner Dump");
            builder.AppendLine("Captured: " + DateTime.Now.ToString("O"));
            builder.AppendLine("Current File: " + (context.CurrentFile ?? "<none>"));
            builder.Append("Scanner Order: ");
            for (int i = 0; i < LevyFlightOptions.ScannerOrder.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(LevyFlightOptions.ScannerOrder[i].Id);
            }
            builder.AppendLine();
            File.WriteAllText(_filePath, builder.ToString(), Encoding.UTF8);
        }
    }

    /// <summary>
    /// Per-scanner dump writer. Entries are buffered and flushed atomically when
    /// the scan completes so concurrent scanners never interleave in the file.
    /// <see cref="Null"/> is a no-op instance used when dumping is disabled.
    /// </summary>
    internal sealed class ScannerDumpSection
    {
        public static readonly ScannerDumpSection Null = new ScannerDumpSection(null, null);

        private readonly ScannerDump _owner;
        private readonly StringBuilder _builder;
        private int _inputCount;
        private int _producedCount;
        private int _discardedCount;

        internal ScannerDumpSection(ScannerDump owner, Scanner scanner)
        {
            _owner = owner;
            if (scanner == null)
            {
                return;
            }

            ScannerId = scanner.Id;
            _builder = new StringBuilder();
            _builder.AppendLine(new string('=', 72));
            _builder.AppendLine("  Scanner: " + scanner.DisplayName + " (" + scanner.Id + "), Priority " + scanner.Priority);
            _builder.AppendLine(new string('=', 72));
        }

        public bool IsActive => _builder != null;

        public string ScannerId { get; }

        public void Input(string entry)
        {
            if (!IsActive)
            {
                return;
            }

            _inputCount++;
            _builder.AppendLine("  IN    " + entry);
        }

        public void Produced(string entry, string detail = null)
        {
            if (!IsActive)
            {
                return;
            }

            _producedCount++;
            _builder.AppendLine("  KEEP  " + entry + (string.IsNullOrEmpty(detail) ? string.Empty : "  (" + detail + ")"));
        }

        public void Discarded(string entry, string reason)
        {
            if (!IsActive)
            {
                return;
            }

            _discardedCount++;
            _builder.AppendLine("  DROP  " + entry + "  -- " + reason);
        }

        public void Detail(string text)
        {
            if (!IsActive)
            {
                return;
            }

            _builder.AppendLine("  ..    " + text);
        }

        public void Complete()
        {
            if (!IsActive)
            {
                return;
            }

            _builder.AppendLine("  ------");
            _builder.AppendLine("  Summary: " + _inputCount + " input, " + _producedCount + " kept, " + _discardedCount + " dropped");
            _owner.WriteSection(_builder.ToString());
        }
    }
}
