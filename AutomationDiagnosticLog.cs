using System;
using System.IO;

namespace N_Remo
{
    internal static class AutomationDiagnosticLog
    {
        private static readonly object SyncObject = new();

        internal static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "N-Remo",
            "AutomationDiagnostics.log");

        internal static void Write(SimpleAutomationRule rule, string message)
        {
            Write($"Rule={rule.Id}:{rule.Name}\t{message}");
        }

        internal static void Write(string message)
        {
            try
            {
                string? directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\t" +
                    $"{message}{Environment.NewLine}";
                lock (SyncObject)
                    File.AppendAllText(FilePath, line);
            }
            catch
            {
                // 診断ログの失敗でオートメーション本体を止めない。
            }
        }
    }
}
