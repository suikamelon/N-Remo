using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;

namespace N_Remo
{
    /// <summary>
    /// 発行方法やEXEの場所が変わっても共通で使用するユーザー設定。
    /// Properties.Settingsの全UserScoped設定を名前と文字列値で保存する。
    /// </summary>
    public static class PersistentAppSettings
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "N-Remo");
        private static readonly string SettingsPath = Path.Combine(
            SettingsDirectory, "AppSettings.json");

        public static void LoadIntoProperties()
        {
            if (!File.Exists(SettingsPath))
            {
                TryUpgradeLegacyDotNetSettings();
                SaveFromProperties();
                return;
            }

            try
            {
                PersistentSettingsData? data = JsonSerializer.Deserialize<PersistentSettingsData>(
                    File.ReadAllText(SettingsPath));
                if (data == null) return;

                if (data.Values is { Count: > 0 })
                {
                    ApplyValues(data.Values);
                    if (data.SchemaVersion < 3)
                        TryImportLatestLegacyUserConfig();
                }
                else
                {
                    TryUpgradeLegacyDotNetSettings();
                    ApplyLegacyValues(data);
                }

                Properties.Settings.Default.Save();
                if (data.SchemaVersion < 3 ||
                    data.Values is not { Count: > 0 })
                    SaveFromProperties();
            }
            catch
            {
                // 壊れたJSONの場合は.NET側の設定を維持する。
            }
        }

        public static void SaveFromProperties()
        {
            Directory.CreateDirectory(SettingsDirectory);
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            foreach (SettingsProperty property in Properties.Settings.Default.Properties)
            {
                if (property.Attributes[typeof(UserScopedSettingAttribute)] == null)
                    continue;
                object? value = Properties.Settings.Default[property.Name];
                TypeConverter converter = TypeDescriptor.GetConverter(property.PropertyType);
                values[property.Name] = converter.ConvertToInvariantString(value) ?? "";
            }

            PersistentSettingsData data = new()
            {
                SchemaVersion = 3,
                Values = values
            };
            string temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(data,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }

        public static void ResetToDefaults()
        {
            // Settings.Reset()だけに依存すると、画面上で編集中だった値や
            // 旧user.config由来の値が次回起動時に再度取り込まれる余地がある。
            // 全UserScoped設定を定義済みの既定値へ明示的に戻し、N-Remo独自の
            // 永続設定ファイルも同じ内容で上書きする。
            foreach (SettingsProperty property in
                Properties.Settings.Default.Properties)
            {
                if (property.Attributes[typeof(UserScopedSettingAttribute)] == null)
                    continue;

                try
                {
                    string serializedDefault =
                        property.DefaultValue?.ToString() ?? "";
                    TypeConverter converter =
                        TypeDescriptor.GetConverter(property.PropertyType);
                    Properties.Settings.Default[property.Name] =
                        converter.ConvertFromInvariantString(serializedDefault);
                }
                catch
                {
                    // 個別の既定値変換に失敗しても、残りの設定は初期化する。
                }
            }

            Properties.Settings.Default.Save();
            SaveFromProperties();
        }

        private static void ApplyValues(IReadOnlyDictionary<string, string> values)
        {
            foreach ((string name, string serializedValue) in values)
            {
                SettingsProperty? property = Properties.Settings.Default.Properties[name];
                if (property == null ||
                    property.Attributes[typeof(UserScopedSettingAttribute)] == null)
                    continue;
                try
                {
                    TypeConverter converter = TypeDescriptor.GetConverter(property.PropertyType);
                    Properties.Settings.Default[name] =
                        converter.ConvertFromInvariantString(serializedValue);
                }
                catch
                {
                    // 廃止・型変更された個別項目だけを無視する。
                }
            }
        }

        private static void ApplyLegacyValues(PersistentSettingsData data)
        {
            if (data.MainWindowWidth is >= 300)
                Properties.Settings.Default.MainWindowWidth = data.MainWindowWidth.Value;
            if (data.MainWindowHeight is >= 200)
                Properties.Settings.Default.MainWindowHeight = data.MainWindowHeight.Value;
            if (data.NatureRemoUrl != null)
                Properties.Settings.Default.URL1 = data.NatureRemoUrl;
            if (data.NatureRemoCloudToken != null)
                Properties.Settings.Default.NatureRemoCloudToken = data.NatureRemoCloudToken;
            if (data.SwitchBotToken != null)
                Properties.Settings.Default.SwitchBotToken = data.SwitchBotToken;
            if (data.SwitchBotSecret != null)
                Properties.Settings.Default.SwitchBotSecret = data.SwitchBotSecret;
            if (data.Language != null)
                Properties.Settings.Default.Language = data.Language;
        }

        private static void TryUpgradeLegacyDotNetSettings()
        {
            try
            {
                Properties.Settings.Default.Upgrade();
                TryImportLatestLegacyUserConfig();
                Properties.Settings.Default.Save();
            }
            catch
            {
                // 以前のuser.configが存在しない環境では既定値を使用する。
            }
        }

        private static void TryImportLatestLegacyUserConfig()
        {
            try
            {
                string currentPath = ConfigurationManager.OpenExeConfiguration(
                    ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;
                var candidate = Directory
                    .EnumerateFiles(SettingsDirectory, "user.config",
                        SearchOption.AllDirectories)
                    .Where(path => !string.Equals(
                        Path.GetFullPath(path), Path.GetFullPath(currentPath),
                        StringComparison.OrdinalIgnoreCase))
                    .Select(path => new
                    {
                        Path = path,
                        Values = ReadLegacyValues(path)
                    })
                    .Where(item => item.Values.Count > 0)
                    // 新しいEXEが既定値で作った設定より、実際に多くの項目を
                    // カスタマイズしていた設定を優先する。同点なら最新を使う。
                    .OrderByDescending(item => CountNonDefaultValues(item.Values))
                    .ThenByDescending(item => File.GetLastWriteTimeUtc(item.Path))
                    .FirstOrDefault();
                if (candidate == null) return;
                ApplyValues(candidate.Values);
            }
            catch
            {
                // 旧設定の探索に失敗しても現在の設定で起動する。
            }
        }

        private static Dictionary<string, string> ReadLegacyValues(string path)
        {
            XDocument document = XDocument.Load(path);
            return document
                    .Descendants()
                    .Where(element => element.Name.LocalName == "setting")
                    .Select(element => new
                    {
                        Name = element.Attribute("name")?.Value,
                        Value = element.Elements()
                            .FirstOrDefault(child => child.Name.LocalName == "value")?.Value
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item.Name) &&
                                   item.Value != null)
                    .ToDictionary(item => item.Name!, item => item.Value!,
                        StringComparer.Ordinal);
        }

        private static int CountNonDefaultValues(
            IReadOnlyDictionary<string, string> values)
        {
            int count = 0;
            foreach ((string name, string value) in values)
            {
                SettingsProperty? property = Properties.Settings.Default.Properties[name];
                if (property == null) continue;
                string defaultValue = property.DefaultValue?.ToString() ?? "";
                if (!string.Equals(value, defaultValue, StringComparison.OrdinalIgnoreCase))
                    count++;
            }
            return count;
        }

        private sealed class PersistentSettingsData
        {
            public int SchemaVersion { get; set; }
            public Dictionary<string, string>? Values { get; set; }
            public double? MainWindowWidth { get; set; }
            public double? MainWindowHeight { get; set; }
            public string? NatureRemoUrl { get; set; }
            public string? NatureRemoCloudToken { get; set; }
            public string? SwitchBotToken { get; set; }
            public string? SwitchBotSecret { get; set; }
            public string? Language { get; set; }
        }
    }
}
