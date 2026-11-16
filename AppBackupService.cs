using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace N_Remo
{
    public static class AppBackupService
    {
        private const string DatabaseEntry =
            "MyData.db";

        private const string SettingsEntry =
            "settings.json";

        private const string SensorLogDatabaseEntry =
            "SensorLogs.db";

        public static void Export(
            string destinationPath,
            ButtonDatabase database)
        {
            string tempDirectory =
                Path.Combine(
                    Path.GetTempPath(),
                    $"N-Remo_{Guid.NewGuid():N}");

            Directory.CreateDirectory(
                tempDirectory);

            try
            {
                string tempDatabasePath =
                    Path.Combine(
                        tempDirectory,
                        DatabaseEntry);

                string tempSettingsPath =
                    Path.Combine(
                        tempDirectory,
                        SettingsEntry);

                string tempSensorLogDatabasePath =
                    Path.Combine(
                        tempDirectory,
                        SensorLogDatabaseEntry);

                // LiteDBをエクスポート
                database.ExportBackup(
                    tempDatabasePath);

                // ユーザー設定をJSONへ保存
                ExportSettings(
                    tempSettingsPath);

                // センサーログは設定DBと分離した専用LiteDBとして保存する。
                SensorLogService.ExportDatabase(
                    database.LoadServerSensorSettings(),
                    tempSensorLogDatabasePath);

                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }

                ZipFile.CreateFromDirectory(
                    tempDirectory,
                    destinationPath,
                    CompressionLevel.Optimal,
                    includeBaseDirectory: false);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(
                        tempDirectory,
                        recursive: true);
                }
            }
        }

        public static void Import(
            string sourcePath,
            ButtonDatabase database)
        {
            string tempDirectory =
                Path.Combine(
                    Path.GetTempPath(),
                    $"N-Remo_{Guid.NewGuid():N}");

            Directory.CreateDirectory(
                tempDirectory);

            try
            {
                using ZipArchive archive =
                    ZipFile.OpenRead(sourcePath);

                ZipArchiveEntry? databaseEntry =
                    archive.GetEntry(DatabaseEntry);

                ZipArchiveEntry? settingsEntry =
                    archive.GetEntry(SettingsEntry);

                ZipArchiveEntry? sensorLogDatabaseEntry =
                    archive.GetEntry(SensorLogDatabaseEntry);

                if (databaseEntry == null)
                {
                    throw new InvalidDataException(
                        "バックアップ内にMyData.dbがありません。");
                }

                string tempDatabasePath =
                    Path.Combine(
                        tempDirectory,
                        DatabaseEntry);

                databaseEntry.ExtractToFile(
                    tempDatabasePath,
                    overwrite: true);

                database.ImportBackup(
                    tempDatabasePath);

                // 古いバックアップとの互換性のため、
                // settings.jsonがない場合はDBだけ復元
                if (settingsEntry != null)
                {
                    string tempSettingsPath =
                        Path.Combine(
                            tempDirectory,
                            SettingsEntry);

                    settingsEntry.ExtractToFile(
                        tempSettingsPath,
                        overwrite: true);

                    ImportSettings(
                        tempSettingsPath);
                }

                // ログDBを含まない旧バックアップもそのまま読み込める。
                if (sensorLogDatabaseEntry != null)
                {
                    string tempSensorLogDatabasePath =
                        Path.Combine(tempDirectory, SensorLogDatabaseEntry);
                    sensorLogDatabaseEntry.ExtractToFile(
                        tempSensorLogDatabasePath, overwrite: true);
                    SensorLogService.ImportDatabase(
                        database.LoadServerSensorSettings(),
                        tempSensorLogDatabasePath);
                }
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(
                        tempDirectory,
                        recursive: true);
                }
            }
        }

        private static void ExportSettings(
            string destinationPath)
        {
            List<BackupSettingItem> items =
                new List<BackupSettingItem>();

            foreach (SettingsProperty property
                in Properties.Settings.Default.Properties)
            {
                // ユーザースコープだけを対象にする
                if (property.Attributes[
                        typeof(UserScopedSettingAttribute)]
                    == null)
                {
                    continue;
                }

                object? value =
                    Properties.Settings.Default[
                        property.Name];

                TypeConverter converter =
                    TypeDescriptor.GetConverter(
                        property.PropertyType);

                string serializedValue =
                    converter.ConvertToInvariantString(
                        value)
                    ?? "";

                items.Add(
                    new BackupSettingItem
                    {
                        Name = property.Name,
                        Value = serializedValue
                    });
            }

            string json =
                JsonSerializer.Serialize(
                    items,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            File.WriteAllText(
                destinationPath,
                json);
        }

        private static void ImportSettings(
            string sourcePath)
        {
            string json =
                File.ReadAllText(sourcePath);

            List<BackupSettingItem>? items =
                JsonSerializer.Deserialize
                    <List<BackupSettingItem>>(json);

            if (items == null)
                return;

            foreach (BackupSettingItem item in items)
            {
                SettingsProperty? property =
                    Properties.Settings.Default
                        .Properties[item.Name];

                // 現在のバージョンに存在しない設定は無視
                if (property == null)
                    continue;

                if (property.Attributes[
                        typeof(UserScopedSettingAttribute)]
                    == null)
                {
                    continue;
                }

                try
                {
                    TypeConverter converter =
                        TypeDescriptor.GetConverter(
                            property.PropertyType);

                    object? value =
                        converter.ConvertFromInvariantString(
                            item.Value);

                    Properties.Settings.Default[
                        item.Name] = value;
                }
                catch
                {
                    // 変換できない古い設定は無視
                }
            }

            Properties.Settings.Default.Save();

            // 実行中の設定オブジェクトへ読み直す
            Properties.Settings.Default.Reload();
            PersistentAppSettings.SaveFromProperties();
        }

        private sealed class BackupSettingItem
        {
            public string Name { get; set; } = "";
            public string Value { get; set; } = "";
        }
    }
}
