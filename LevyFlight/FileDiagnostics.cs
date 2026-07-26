using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LevyFlight
{
    internal sealed class FileDiagnostics
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, string> _operations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _directory;

        public FileDiagnostics(string directory)
        {
            _directory = directory;
        }

        public bool IsEnabled { get; set; }

        public string BeginOperation(string name)
        {
            if (!IsEnabled)
                return null;

            try
            {
                lock (_lock)
                {
                    EnsureDirectoryExists();
                    string operationId = BuildOperationId(name);
                    string baseId = operationId;
                    for (int i = 1; _operations.ContainsKey(operationId) || OperationFilesExist(operationId); i++)
                    {
                        operationId = baseId + "-" + i;
                    }

                    _operations[operationId] = name;
                    return operationId;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Begin diagnostics operation", ex);
                return null;
            }
        }

        public void Write(string operationId, string fileSuffix, string content)
        {
            WriteFile(operationId, fileSuffix, content, append: false);
        }

        public void Append(string operationId, string fileSuffix, string content)
        {
            WriteFile(operationId, fileSuffix, content, append: true);
        }

        public void CompleteOperation(string operationId)
        {
            if (operationId == null)
                return;

            lock (_lock)
            {
                _operations.Remove(operationId);
            }
        }

        private void WriteFile(string operationId, string fileSuffix, string content, bool append)
        {
            if (!IsEnabled || operationId == null)
                return;

            try
            {
                lock (_lock)
                {
                    if (!_operations.ContainsKey(operationId))
                        return;

                    EnsureDirectoryExists();
                    string path = Path.Combine(_directory, operationId + "-" + Sanitize(fileSuffix) + ".txt");
                    if (append)
                    {
                        File.AppendAllText(path, content, Encoding.UTF8);
                    }
                    else
                    {
                        File.WriteAllText(path, content, Encoding.UTF8);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Write diagnostics file", ex);
            }
        }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(_directory))
            {
                Directory.CreateDirectory(_directory);
            }
        }

        private bool OperationFilesExist(string operationId)
        {
            try
            {
                return Directory.Exists(_directory) && Directory.GetFiles(_directory, operationId + "-*.txt").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildOperationId(string name)
        {
            return DateTime.Now.ToString("yyMMdd-HHmmss") + "-" + Sanitize(name);
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = "unknown";

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }
            return name;
        }
    }
}
