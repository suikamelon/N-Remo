using LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace N_Remo
{
    public class ButtonDatabase
    {
        private readonly string databasePath;
        private readonly object syncObject = new object();

        public ButtonDatabase()
        {
            string dataDirectory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder
                            .LocalApplicationData),
                    "N-Remo");

            Directory.CreateDirectory(
                dataDirectory);

            databasePath =
                Path.Combine(
                    dataDirectory,
                    "MyData.db");

            string oldDatabasePath =
                Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "MyData.db");

            if (!File.Exists(databasePath) &&
                File.Exists(oldDatabasePath))
            {
                File.Copy(
                    oldDatabasePath,
                    databasePath);
            }
        }

        public void Save(ButtonSetting setting)
        {
            using LiteDatabase db =
                OpenDatabase();

            var collection =
                db.GetCollection<ButtonSetting>(
                    "buttons");

            collection.Upsert(setting);
        }

        public ButtonSetting? Load(string name)
        {
            using LiteDatabase db =
                OpenDatabase();

            var collection =
                db.GetCollection<ButtonSetting>(
                    "buttons");

            return collection.FindById(name);
        }

        public List<ButtonSetting> LoadAll()
        {
            using LiteDatabase db =
                OpenDatabase();

            var collection =
                db.GetCollection<ButtonSetting>(
                    "buttons");

            return collection
                .FindAll()
                .ToList();
        }

        public void SaveSwitchBotCommands(
            IEnumerable<SwitchBotCommandItem> commands)
        {
            using LiteDatabase db = OpenDatabase();
            var collection =
                db.GetCollection<SwitchBotCommandItem>(
                    "switchBotCommands");

            collection.DeleteAll();
            foreach (SwitchBotCommandItem command in commands)
            {
                if (string.IsNullOrWhiteSpace(command.Id))
                {
                    command.Id =
                        $"{command.DeviceId}|{command.Command}|{command.CommandType}";
                }

                collection.Upsert(command);
            }
        }

        public List<SwitchBotCommandItem> LoadSwitchBotCommands()
        {
            using LiteDatabase db = OpenDatabase();
            return db
                .GetCollection<SwitchBotCommandItem>(
                    "switchBotCommands")
                .FindAll()
                .ToList();
        }

        public void SaveAutomation(SimpleAutomationRule rule)
        {
            using LiteDatabase db = OpenDatabase();
            var collection = db.GetCollection<SimpleAutomationRule>("simpleAutomations");
            if (rule.Id == 0)
                rule.Id = collection.Insert(rule).AsInt32;
            else
                collection.Upsert(rule);
        }

        public void StartTemporaryProtection(int minutes)
        {
            using LiteDatabase db = OpenDatabase();
            db.GetCollection<AutomationProtectionState>("automationProtection")
                .Upsert(new AutomationProtectionState
                {
                    Id = "Current",
                    ProtectionUntilUtc = DateTime.UtcNow.AddMinutes(
                        Math.Clamp(minutes, 1, 525600))
                });
        }

        public void ClearTemporaryProtection()
        {
            using LiteDatabase db = OpenDatabase();
            db.GetCollection<AutomationProtectionState>("automationProtection")
                .Delete("Current");
        }

        public bool IsTemporaryProtectionActive()
        {
            using LiteDatabase db = OpenDatabase();
            var collection = db.GetCollection<AutomationProtectionState>(
                "automationProtection");
            AutomationProtectionState? state = collection.FindById("Current");
            if (state?.ProtectionUntilUtc == null) return false;
            DateTime untilUtc = state!.ProtectionUntilUtc!.Value.Kind switch
            {
                DateTimeKind.Utc => state.ProtectionUntilUtc.Value,
                DateTimeKind.Local => state.ProtectionUntilUtc.Value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(state.ProtectionUntilUtc.Value,
                    DateTimeKind.Local).ToUniversalTime()
            };
            if (untilUtc > DateTime.UtcNow) return true;
            collection.Delete("Current");
            return false;
        }

        public List<SimpleAutomationRule> LoadAutomations()
        {
            using LiteDatabase db = OpenDatabase();
            var collection = db.GetCollection<SimpleAutomationRule>("simpleAutomations");
            List<SimpleAutomationRule> rules = collection.FindAll()
                .OrderBy(rule => rule.SortOrder)
                .ThenBy(rule => rule.Id).ToList();
            for (int index = 0; index < rules.Count; index++)
            {
                SimpleAutomationRule rule = rules[index];
                int before = rule.Conditions.Count;
                int actionsBefore = rule.Actions.Count;
                int consecutiveCountBefore = rule.RequiredConsecutiveCount;
                rule.EnsureConditions();
                rule.EnsureActions();
                bool normalized = false;

                // 旧版の画面更新時に実行コマンドが空で保存される不具合から復旧する。
                // 対象はN-Remoが作成する既定の在宅・不在モード切替ルールだけに限定する。
                if (rule.Actions.All(action => string.IsNullOrWhiteSpace(action.CommandId)))
                {
                    string? recoveredCommandId = rule.Name switch
                    {
                        "在宅モード" => "Mode:Home",
                        "不在モード" => "Mode:Away",
                        _ => null
                    };
                    if (recoveredCommandId != null)
                    {
                        rule.Actions[0].CommandId = recoveredCommandId;
                        rule.ActionCommandId = recoveredCommandId;
                        normalized = true;
                    }
                }
                foreach (AutomationCondition condition in rule.Conditions)
                {
                    string normalizedLabel = SensorDataNormalizer.NormalizeKey(
                        condition.TriggerDataLabel);
                    string normalizedValue = SensorDataNormalizer.NormalizeValue(
                        condition.ConditionValue);
                    if (!string.Equals(condition.TriggerDataLabel, normalizedLabel,
                            StringComparison.Ordinal) ||
                        !string.Equals(condition.ConditionValue, normalizedValue,
                            StringComparison.Ordinal))
                    {
                        condition.TriggerDataLabel = normalizedLabel;
                        condition.ConditionValue = normalizedValue;
                        normalized = true;
                    }
                }
                bool sortOrderChanged = rule.SortOrder != index;
                if (sortOrderChanged)
                    rule.SortOrder = index;
                if (before == 0 || actionsBefore == 0 || consecutiveCountBefore < 0 || sortOrderChanged || normalized)
                    collection.Upsert(rule);
            }
            return rules;
        }

        public void SaveAutomationOrder(IEnumerable<int> orderedIds)
        {
            using LiteDatabase db = OpenDatabase();
            var collection = db.GetCollection<SimpleAutomationRule>("simpleAutomations");
            int index = 0;
            foreach (int id in orderedIds)
            {
                SimpleAutomationRule? rule = collection.FindById(id);
                if (rule == null) continue;
                rule.SortOrder = index++;
                collection.Update(rule);
            }
        }

        public SimpleAutomationRule? LoadAutomation(int id)
        {
            using LiteDatabase db = OpenDatabase();
            SimpleAutomationRule? rule = db
                .GetCollection<SimpleAutomationRule>("simpleAutomations")
                .FindById(id);
            rule?.EnsureConditions();
            rule?.EnsureActions();
            return rule;
        }

        public void ResetAutomationRuntimeStates()
        {
            using LiteDatabase db = OpenDatabase();
            var collection = db.GetCollection<SimpleAutomationRule>("simpleAutomations");
            // LiteDBのカーソルを列挙しながら同じコレクションを更新すると、
            // 起動時に例外となることがあるため、先にメモリへ読み込む。
            List<SimpleAutomationRule> rules = collection.FindAll().ToList();
            foreach (SimpleAutomationRule rule in rules)
            {
                // アプリ停止中は条件が継続していたことを保証できないため、
                // 起動時には継続時間を最初から計測する。また、旧版で送信に
                // 失敗したまま残った実行済みラッチもここで解除する。
                rule.IsLatched = false;
                rule.CurrentConsecutiveCount = 0;
                rule.ConditionMatchedSinceUtc = null;
                collection.Update(rule);
            }
        }

        public bool DeleteAutomation(int id)
        {
            using LiteDatabase db = OpenDatabase();
            return db.GetCollection<SimpleAutomationRule>("simpleAutomations").Delete(id);
        }

        public void SaveServerSensors(IEnumerable<ServerSensorItem> sensors)
        {
            using LiteDatabase db = OpenDatabase();
            var collection = db.GetCollection<ServerSensorItem>("serverSensors");
            foreach (ServerSensorItem sensor in sensors)
            {
                collection.Upsert(sensor);
            }
        }

        public void ReplaceServerSensors(
            IEnumerable<ServerSensorItem> sensors)
        {
            using LiteDatabase db = OpenDatabase();
            var collection =
                db.GetCollection<ServerSensorItem>("serverSensors");
            collection.DeleteAll();
            foreach (ServerSensorItem sensor in sensors)
                collection.Upsert(sensor);
        }

        public List<ServerSensorItem> LoadServerSensors()
        {
            using LiteDatabase db = OpenDatabase();
            var collection = db.GetCollection<ServerSensorItem>("serverSensors");
            List<ServerSensorItem> sensors = collection.FindAll().ToList();
            foreach (ServerSensorItem sensor in sensors)
            {
                string[] before = { sensor.OtherValue, sensor.OtherValue2, sensor.OtherValue3,
                    sensor.OtherValue4, sensor.OtherValue5, sensor.OtherValue6,
                    sensor.OtherValue7, sensor.OtherValue8, sensor.AllValues };
                sensor.OtherValue = SensorDataNormalizer.NormalizeStoredData(sensor.OtherValue);
                sensor.OtherValue2 = SensorDataNormalizer.NormalizeStoredData(sensor.OtherValue2);
                sensor.OtherValue3 = SensorDataNormalizer.NormalizeStoredData(sensor.OtherValue3);
                sensor.OtherValue4 = SensorDataNormalizer.NormalizeStoredData(sensor.OtherValue4);
                sensor.OtherValue5 = SensorDataNormalizer.NormalizeStoredData(sensor.OtherValue5);
                sensor.OtherValue6 = SensorDataNormalizer.NormalizeStoredData(sensor.OtherValue6);
                sensor.OtherValue7 = SensorDataNormalizer.NormalizeStoredData(sensor.OtherValue7);
                sensor.OtherValue8 = SensorDataNormalizer.NormalizeStoredData(sensor.OtherValue8);
                sensor.AllValues = SensorDataNormalizer.NormalizeStoredData(sensor.AllValues);
                string[] after = { sensor.OtherValue, sensor.OtherValue2, sensor.OtherValue3,
                    sensor.OtherValue4, sensor.OtherValue5, sensor.OtherValue6,
                    sensor.OtherValue7, sensor.OtherValue8, sensor.AllValues };
                if (!before.SequenceEqual(after, StringComparer.Ordinal))
                    collection.Upsert(sensor);
            }
            return sensors;
        }

        public ServerSensorSettings LoadServerSensorSettings()
        {
            using LiteDatabase db = OpenDatabase();
            return db.GetCollection<ServerSensorSettings>("serverSensorSettings")
                .FindById("Current") ?? new ServerSensorSettings();
        }

        public void SaveServerSensorSettings(ServerSensorSettings settings)
        {
            using LiteDatabase db = OpenDatabase();
            db.GetCollection<ServerSensorSettings>("serverSensorSettings")
                .Upsert(settings);
        }

        // ========================================
        // 既存LiteDBのIDをNo形式へ移行
        // ========================================
        public void MigrateButtonIds()
        {
            using LiteDatabase db =
                OpenDatabase();

            var collection =
                db.GetCollection<ButtonSetting>(
                    "buttons");

            List<ButtonSetting> settings =
                collection.FindAll().ToList();

            foreach (ButtonSetting setting
                in settings)
            {
                string oldId =
                    setting.Name.Trim();

                string numberText =
                    oldId;

                if (numberText.StartsWith(
                    "Button",
                    StringComparison.OrdinalIgnoreCase))
                {
                    numberText =
                        numberText["Button".Length..];
                }
                else if (numberText.StartsWith(
                    "No",
                    StringComparison.OrdinalIgnoreCase))
                {
                    numberText =
                        numberText["No".Length..];
                }

                if (!int.TryParse(
                    numberText,
                    out int number))
                {
                    continue;
                }

                string newId =
                    $"No{number:000}";

                if (string.Equals(
                    oldId,
                    newId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ButtonSetting? existing =
                    collection.FindById(newId);

                if (existing != null)
                    continue;

                setting.Name = newId;

                collection.Upsert(setting);
                collection.Delete(oldId);
            }
        }

        private LiteDatabase OpenDatabase(bool readOnly = false)
        {
            ConnectionString connectionString =
                new ConnectionString
                {
                    Filename = databasePath,
                    Connection = ConnectionType.Shared,
                    ReadOnly = readOnly
                };

            return new LiteDatabase(
                connectionString);
        }

        // ========================================
        // ボタンを削除する処理
        // ========================================
        public bool Delete(string name)
        {
            using LiteDatabase db =
                OpenDatabase();

            var collection =
                db.GetCollection<ButtonSetting>(
                    "buttons");

            return collection.Delete(name);
        }

        public int ClearAllButtons()
        {
            using LiteDatabase db = OpenDatabase();
            int deleted = db.GetCollection<ButtonSetting>("buttons").DeleteAll();
            db.Checkpoint();
            return deleted;
        }

        public void ClearAllData()
        {
            lock (syncObject)
            {
                using LiteDatabase db = OpenDatabase();
                foreach (string collectionName in
                    db.GetCollectionNames().ToList())
                {
                    db.DropCollection(collectionName);
                }
                db.Checkpoint();
            }
        }

        // ========================================
        // データベースをバックアップ
        // ========================================
        public void ExportBackup(string destinationPath)
        {
            lock (syncObject)
            {
                if (!File.Exists(databasePath))
                {
                    throw new FileNotFoundException(
                        "MyData.dbが見つかりません。",
                        databasePath);
                }

                if (string.Equals(
                    Path.GetFullPath(databasePath),
                    Path.GetFullPath(destinationPath),
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "元のMyData.dbと同じ場所には保存できません。");
                }

                // WALの内容をデータベースへ反映して閉じる
                using (LiteDatabase db = OpenDatabase())
                {
                    db.Checkpoint();
                }

                // DBを閉じた後でコピー
                File.Copy(
                    databasePath,
                    destinationPath,
                    overwrite: true);
            }
        }

        // ========================================
        // バックアップファイルからインポート
        // ========================================
        public void ImportBackup(string sourcePath)
        {
            lock (syncObject)
            {
                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException(
                        "バックアップファイルが見つかりません。",
                        sourcePath);
                }

                string sourceFullPath =
                    Path.GetFullPath(sourcePath);

                string databaseFullPath =
                    Path.GetFullPath(databasePath);

                if (string.Equals(
                    sourceFullPath,
                    databaseFullPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "現在使用中のMyData.dbは選択できません。");
                }

                // バックアップが正常なLiteDBか確認
                var connectionString = new ConnectionString
                {
                    Filename = sourceFullPath,
                    ReadOnly = true
                };

                using (var backupDb =
                    new LiteDatabase(connectionString))
                {
                    // コレクションへアクセスして読み込みを確認
                    backupDb
                        .GetCollection<ButtonSetting>("buttons")
                        .FindOne(Query.All());
                }

                string databaseDirectory =
                    Path.GetDirectoryName(databaseFullPath)
                    ?? AppDomain.CurrentDomain.BaseDirectory;

                string temporaryPath = Path.Combine(
                    databaseDirectory,
                    $"MyData_import_{Guid.NewGuid():N}.tmp");

                string recoveryPath = Path.Combine(
                    databaseDirectory,
                    $"MyData_before_import_" +
                    $"{DateTime.Now:yyyyMMdd_HHmmss}.db");

                try
                {
                    // まず一時ファイルへコピー
                    File.Copy(
                        sourceFullPath,
                        temporaryPath,
                        overwrite: true);

                    if (File.Exists(databaseFullPath))
                    {
                        // 現在のDBをrecoveryPathへ残して置き換える
                        File.Replace(
                            temporaryPath,
                            databaseFullPath,
                            recoveryPath,
                            ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(
                            temporaryPath,
                            databaseFullPath);
                    }
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }
        }
    }
}
