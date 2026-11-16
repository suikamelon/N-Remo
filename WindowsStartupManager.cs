using Microsoft.Win32;
using System.Diagnostics;

namespace N_Remo
{
    internal static class WindowsStartupManager
    {
        private const string RunKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "N-Remo";

        public static void Apply(bool enabled)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    RunKeyPath, writable: true);
                if (key == null) return;

                if (!enabled)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                    return;
                }

                string executablePath = Environment.ProcessPath ??
                    Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrWhiteSpace(executablePath)) return;
                key.SetValue(ValueName, $"\"{executablePath}\"",
                    RegistryValueKind.String);
            }
            catch
            {
                // レジストリがポリシーで制限されていてもアプリの起動は妨げない。
            }
        }
    }
}
