using LiteDB;
using System.Globalization;
using System.IO;
using System.Text;

namespace N_Remo
{
    public sealed class SensorLogEntry
    {
        [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
        public DateTime Timestamp { get; set; }
        public string SensorId { get; set; } = "";
        public string Provider { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string Metric { get; set; } = "";
        public string SeriesKey { get; set; } = "";
        public double? NumericValue { get; set; }
        public string RawValue { get; set; } = "";
        public string Unit { get; set; } = "";
        [BsonIgnore] public string TimestampDisplay => Timestamp.ToString("yyyy/MM/dd HH:mm:ss");
        [BsonIgnore] public string ValueDisplay =>
            $"{SensorDataNormalizer.DisplayValue(RawValue,
                Metric.StartsWith("Data:", StringComparison.OrdinalIgnoreCase) ? Metric[5..] : Metric)} {Unit}".TrimEnd();
        [BsonIgnore]
        public string MetricDisplay => Metric.StartsWith("Data:", StringComparison.OrdinalIgnoreCase)
            ? SensorDataNormalizer.DisplayKey(Metric[5..])
            : LanguageManager.Get(Metric switch
            {
                "Temperature" => "温度", "Humidity" => "湿度",
                "Illuminance" => "照度", "Battery" => "電池", _ => Metric
            });
    }

    public sealed class SensorLogSeriesInfo
    {
        [BsonId] public string SeriesKey { get; set; } = "";
        public string SensorId { get; set; } = "";
        public string Provider { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string Metric { get; set; } = "";
        public DateTime FirstTimestamp { get; set; }
        public DateTime LastTimestamp { get; set; }
    }

    internal sealed class SensorLogHourlyBucket
    {
        [BsonId] public string Id { get; set; } = "";
        public string SeriesKey { get; set; } = "";
        public DateTime BucketStart { get; set; }
        public double Sum { get; set; }
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double LastValue { get; set; }
        public int Count { get; set; }
    }

    internal sealed class SensorLogMetadata
    {
        [BsonId] public string Id { get; set; } = "";
        public bool Completed { get; set; }
    }

    public static class SensorLogService
    {
        private const string CsvMigrationId = "csv-migration-v1";
        private const string SeriesCatalogRepairId = "series-catalog-repair-v1";
        private static readonly object SyncObject = new();
        private static readonly Dictionary<string, DateTime> LastCleanupDates = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> MigratedPaths = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> InitializedPaths = new(StringComparer.OrdinalIgnoreCase);
        private static LiteDatabase? sharedDatabase;
        private static string sharedDatabasePath = "";

        public static string DefaultLogDirectory => Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? AppContext.BaseDirectory,
            "SensorLogs");

        public static string GetLogDirectory(ServerSensorSettings settings) =>
            string.IsNullOrWhiteSpace(settings.LogDirectory)
                ? DefaultLogDirectory
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.LogDirectory.Trim()));

        public static string GetDatabasePath(ServerSensorSettings settings) =>
            Path.Combine(GetLogDirectory(settings), "SensorLogs.db");

        public static void Record(IEnumerable<ServerSensorItem> sensors, ServerSensorSettings settings)
        {
            List<SensorLogEntry> entries = sensors.Where(sensor => sensor.LogEnabled)
                .SelectMany(CreateEntries).ToList();
            lock (SyncObject)
            {
                LiteDatabase db = OpenDatabase(settings);
                MigrateCsvIfNeeded(db, settings);
                if (entries.Count > 0)
                {
                    ILiteCollection<SensorLogEntry> logs = GetLogCollection(db);
                    ILiteCollection<SensorLogSeriesInfo> catalog = db.GetCollection<SensorLogSeriesInfo>("sensorLogSeries");
                    foreach (SensorLogEntry entry in entries)
                    {
                        PrepareEntry(entry);
                        logs.Insert(entry);
                        UpdateSeriesCatalog(catalog, entry);
                        UpdateHourlyBucket(db, entry);
                    }
                }
                Cleanup(db, settings);
            }
        }

        public static List<SensorLogSeriesInfo> LoadSeriesCatalog(ServerSensorSettings settings)
        {
            lock (SyncObject)
            {
                LiteDatabase db = OpenDatabase(settings);
                MigrateCsvIfNeeded(db, settings);
                RepairSeriesCatalogIfNeeded(db);
                return db.GetCollection<SensorLogSeriesInfo>("sensorLogSeries").FindAll()
                    .OrderBy(item => item.DeviceName, StringComparer.CurrentCulture)
                    .ThenBy(item => item.Metric, StringComparer.CurrentCulture).ToList();
            }
        }

        public static List<SensorLogEntry> LoadSeries(ServerSensorSettings settings,
            string sensorId, string metric, DateTime? from = null, DateTime? to = null)
        {
            string seriesKey = CreateSeriesKey(sensorId, metric);
            DateTime rangeFrom = from ?? DateTime.MinValue;
            DateTime rangeTo = to ?? DateTime.MaxValue;
            lock (SyncObject)
            {
                LiteDatabase db = OpenDatabase(settings);
                MigrateCsvIfNeeded(db, settings);
                return GetLogCollection(db).Query()
                    .Where(item => item.SeriesKey == seriesKey && item.Timestamp >= rangeFrom && item.Timestamp <= rangeTo)
                    .OrderBy(item => item.Timestamp).ToList();
            }
        }

        public static List<SensorLogEntry> LoadLatestSeries(
            ServerSensorSettings settings, string sensorId, string metric,
            DateTime from, DateTime to, int maximum = 1000)
        {
            string seriesKey = CreateSeriesKey(sensorId, metric);
            lock (SyncObject)
            {
                LiteDatabase db = OpenDatabase(settings);
                MigrateCsvIfNeeded(db, settings);
                return GetLogCollection(db).Query()
                    .Where(item => item.SeriesKey == seriesKey &&
                        item.Timestamp >= from && item.Timestamp <= to)
                    .OrderByDescending(item => item.Timestamp)
                    .Limit(Math.Max(1, maximum)).ToList();
            }
        }

        public static List<SensorLogEntry> LoadChartSeries(
            ServerSensorSettings settings, string sensorId, string metric,
            DateTime from, DateTime to)
        {
            // 直近48時間は常に生ログを使用する。全期間が48時間を超える場合も、
            // 古い部分だけを1時間集計にし、直近値を平均値で置き換えない。
            if (to - from <= TimeSpan.FromHours(48))
                return LoadSeries(settings, sensorId, metric, from, to);
            string seriesKey = CreateSeriesKey(sensorId, metric);
            DateTime rawFrom = to.AddHours(-48);
            if (rawFrom < from) rawFrom = from;
            lock (SyncObject)
            {
                LiteDatabase db = OpenDatabase(settings);
                MigrateCsvIfNeeded(db, settings);
                bool useLastValue = IsBinarySeries(db, seriesKey);
                List<SensorLogEntry> hourly = db
                    .GetCollection<SensorLogHourlyBucket>("sensorLogHourlyBuckets")
                    .Query()
                    .Where(item => item.SeriesKey == seriesKey &&
                        item.BucketStart >= from && item.BucketStart < rawFrom)
                    .OrderBy(item => item.BucketStart)
                    .ToEnumerable()
                    .Select(bucket => new SensorLogEntry
                    {
                        Timestamp = bucket.BucketStart,
                        SensorId = sensorId,
                        Metric = metric,
                        SeriesKey = seriesKey,
                        NumericValue = bucket.Count > 0
                            ? (useLastValue ? bucket.LastValue : bucket.Sum / bucket.Count)
                            : null,
                        RawValue = bucket.Count > 0
                            ? (useLastValue ? bucket.LastValue : bucket.Sum / bucket.Count)
                                .ToString("0.###", CultureInfo.InvariantCulture)
                            : ""
                    }).ToList();
                List<SensorLogEntry> recent = GetLogCollection(db).Query()
                    .Where(item => item.SeriesKey == seriesKey &&
                        item.Timestamp >= rawFrom && item.Timestamp <= to)
                    .OrderBy(item => item.Timestamp).ToList();
                if (hourly.Count == 0 && recent.Count == 0)
                    return LoadSeries(settings, sensorId, metric, from, to);
                hourly.AddRange(recent);
                return hourly;
            }
        }

        public static List<SensorLogEntry> LoadAll(ServerSensorSettings settings)
        {
            lock (SyncObject)
            {
                LiteDatabase db = OpenDatabase(settings);
                MigrateCsvIfNeeded(db, settings);
                return GetLogCollection(db).Query().OrderByDescending(item => item.Timestamp).ToList();
            }
        }

        public static List<SensorLogEntry> LoadToday(ServerSensorSettings settings)
        {
            DateTime from = DateTime.Today;
            DateTime to = from.AddDays(1);
            lock (SyncObject)
            {
                LiteDatabase db = OpenDatabase(settings);
                MigrateCsvIfNeeded(db, settings);
                return GetLogCollection(db).Query()
                    .Where(item => item.Timestamp >= from && item.Timestamp < to).ToList();
            }
        }

        public static void ExportDatabase(
            ServerSensorSettings settings, string destinationPath)
        {
            lock (SyncObject)
            {
                LiteDatabase db = OpenDatabase(settings);
                MigrateCsvIfNeeded(db, settings);
                db.Checkpoint();
                CloseSharedDatabase(GetDatabasePath(settings));
                File.Copy(GetDatabasePath(settings), destinationPath, overwrite: true);
            }
        }

        public static void ImportDatabase(
            ServerSensorSettings settings, string sourcePath)
        {
            lock (SyncObject)
            {
                // コピー前にLiteDBとして開けることを検証する。
                using (LiteDatabase validation = new(new ConnectionString
                {
                    Filename = sourcePath,
                    ReadOnly = true
                }))
                {
                    _ = validation.GetCollectionNames().ToList();
                }
                Directory.CreateDirectory(GetLogDirectory(settings));
                CloseSharedDatabase(GetDatabasePath(settings));
                File.Copy(sourcePath, GetDatabasePath(settings), overwrite: true);
                LastCleanupDates.Remove(GetDatabasePath(settings));
                MigratedPaths.Remove(GetDatabasePath(settings));
                InitializedPaths.Remove(GetDatabasePath(settings));
            }
        }

        public static void ClearAll(ServerSensorSettings settings)
        {
            lock (SyncObject)
            {
                LiteDatabase db = OpenDatabase(settings);
                db.GetCollection<SensorLogEntry>("sensorLogs").DeleteAll();
                db.GetCollection<SensorLogSeriesInfo>("sensorLogSeries").DeleteAll();
                db.GetCollection<SensorLogHourlyBucket>("sensorLogHourlyBuckets").DeleteAll();
                db.Checkpoint();
            }
        }

        private static LiteDatabase OpenDatabase(ServerSensorSettings settings)
        {
            string path = GetDatabasePath(settings);
            Directory.CreateDirectory(GetLogDirectory(settings));
            if (sharedDatabase == null ||
                !string.Equals(sharedDatabasePath, path, StringComparison.OrdinalIgnoreCase))
            {
                sharedDatabase?.Dispose();
                sharedDatabase = new LiteDatabase(path);
                sharedDatabasePath = path;
            }
            return sharedDatabase;
        }

        private static ILiteCollection<SensorLogEntry> GetLogCollection(LiteDatabase db)
        {
            ILiteCollection<SensorLogEntry> collection = db.GetCollection<SensorLogEntry>("sensorLogs");
            if (InitializedPaths.Add(sharedDatabasePath))
            {
                collection.EnsureIndex(item => item.Timestamp);
                collection.EnsureIndex(item => item.SeriesKey);
                ILiteCollection<SensorLogHourlyBucket> buckets =
                    db.GetCollection<SensorLogHourlyBucket>("sensorLogHourlyBuckets");
                buckets.EnsureIndex(item => item.SeriesKey);
                buckets.EnsureIndex(item => item.BucketStart);
            }
            return collection;
        }

        private static void MigrateCsvIfNeeded(LiteDatabase db, ServerSensorSettings settings)
        {
            string databasePath = GetDatabasePath(settings);
            if (MigratedPaths.Contains(databasePath)) return;
            ILiteCollection<SensorLogMetadata> metadata = db.GetCollection<SensorLogMetadata>("sensorLogMetadata");
            if (metadata.FindById(CsvMigrationId)?.Completed == true)
            {
                MigratedPaths.Add(databasePath);
                return;
            }
            ILiteCollection<SensorLogEntry> logs = GetLogCollection(db);
            ILiteCollection<SensorLogSeriesInfo> catalog = db.GetCollection<SensorLogSeriesInfo>("sensorLogSeries");
            foreach (string path in Directory.EnumerateFiles(GetLogDirectory(settings), "sensor-*.csv").OrderBy(file => file))
            {
                List<SensorLogEntry> batch = new(1000);
                foreach (string line in File.ReadLines(path).Skip(1))
                {
                    SensorLogEntry? entry = ParseCsvEntry(line);
                    if (entry == null) continue;
                    batch.Add(entry);
                    UpdateSeriesCatalog(catalog, entry);
                    UpdateHourlyBucket(db, entry);
                    if (batch.Count < 1000) continue;
                    logs.InsertBulk(batch);
                    batch.Clear();
                }
                if (batch.Count > 0) logs.InsertBulk(batch);
            }
            metadata.Upsert(new SensorLogMetadata { Id = CsvMigrationId, Completed = true });
            MigratedPaths.Add(databasePath);
        }

        // 初期のDB版では、CSV移行済みでも系列カタログが後から作成された場合に
        // 開始・終了日時が直近1件だけになることがあった。生ログを全走査せず、
        // SeriesKeyインデックスを使って各系列の先頭・末尾だけを読み直す。
        private static void RepairSeriesCatalogIfNeeded(LiteDatabase db)
        {
            ILiteCollection<SensorLogMetadata> metadata =
                db.GetCollection<SensorLogMetadata>("sensorLogMetadata");
            if (metadata.FindById(SeriesCatalogRepairId)?.Completed == true) return;

            ILiteCollection<SensorLogEntry> logs = GetLogCollection(db);
            ILiteCollection<SensorLogSeriesInfo> catalog =
                db.GetCollection<SensorLogSeriesInfo>("sensorLogSeries");
            foreach (SensorLogSeriesInfo info in catalog.FindAll().ToList())
            {
                SensorLogEntry? first = logs.Query()
                    .Where(item => item.SeriesKey == info.SeriesKey)
                    .OrderBy(item => item.Timestamp).Limit(1)
                    .ToEnumerable().FirstOrDefault();
                SensorLogEntry? last = logs.Query()
                    .Where(item => item.SeriesKey == info.SeriesKey)
                    .OrderByDescending(item => item.Timestamp).Limit(1)
                    .ToEnumerable().FirstOrDefault();
                if (first == null || last == null) continue;

                info.FirstTimestamp = first.Timestamp;
                info.LastTimestamp = last.Timestamp;
                info.SensorId = last.SensorId;
                info.Provider = last.Provider;
                info.DeviceName = last.DeviceName;
                info.Metric = last.Metric;
                catalog.Update(info);
            }

            metadata.Upsert(new SensorLogMetadata
            {
                Id = SeriesCatalogRepairId,
                Completed = true
            });
        }

        private static SensorLogEntry? ParseCsvEntry(string line)
        {
            string[] values = ParseCsv(line).ToArray();
            if (values.Length < 7 || !DateTime.TryParse(values[0], CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTime timestamp)) return null;
            SensorLogEntry entry = new()
            {
                Timestamp = timestamp.ToLocalTime(), SensorId = values[1], Provider = values[2],
                DeviceName = values[3], Metric = values[4], RawValue = values[5],
                NumericValue = TryParseNumber(values[5]), Unit = values[6]
            };
            PrepareEntry(entry);
            return entry;
        }

        private static void PrepareEntry(SensorLogEntry entry)
        {
            if (entry.Id == ObjectId.Empty) entry.Id = ObjectId.NewObjectId();
            entry.SeriesKey = CreateSeriesKey(entry.SensorId, entry.Metric);
        }

        private static string CreateSeriesKey(string sensorId, string metric) => $"{sensorId}\u001f{metric}";
        private static SensorLogSeriesInfo ToSeriesInfo(SensorLogEntry entry) => new()
        {
            SeriesKey = entry.SeriesKey, SensorId = entry.SensorId, Provider = entry.Provider,
            DeviceName = entry.DeviceName, Metric = entry.Metric,
            FirstTimestamp = entry.Timestamp, LastTimestamp = entry.Timestamp
        };

        private static void UpdateSeriesCatalog(
            ILiteCollection<SensorLogSeriesInfo> catalog, SensorLogEntry entry)
        {
            SensorLogSeriesInfo? info = catalog.FindById(entry.SeriesKey);
            if (info == null)
            {
                catalog.Insert(ToSeriesInfo(entry));
                return;
            }
            info.Provider = entry.Provider;
            info.DeviceName = entry.DeviceName;
            if (info.FirstTimestamp == default || entry.Timestamp < info.FirstTimestamp)
                info.FirstTimestamp = entry.Timestamp;
            if (entry.Timestamp > info.LastTimestamp)
                info.LastTimestamp = entry.Timestamp;
            catalog.Update(info);
        }

        private static void UpdateHourlyBucket(LiteDatabase db, SensorLogEntry entry)
        {
            if (!entry.NumericValue.HasValue ||
                !double.IsFinite(entry.NumericValue.Value)) return;
            DateTime bucketStart = new(entry.Timestamp.Year, entry.Timestamp.Month,
                entry.Timestamp.Day, entry.Timestamp.Hour, 0, 0, entry.Timestamp.Kind);
            string id = $"{entry.SeriesKey}\u001f{bucketStart:O}";
            ILiteCollection<SensorLogHourlyBucket> buckets =
                db.GetCollection<SensorLogHourlyBucket>("sensorLogHourlyBuckets");
            SensorLogHourlyBucket? bucket = buckets.FindById(id);
            double value = entry.NumericValue.Value;
            if (bucket == null)
            {
                buckets.Insert(new SensorLogHourlyBucket
                {
                    Id = id, SeriesKey = entry.SeriesKey,
                    BucketStart = bucketStart, Sum = value,
                    Minimum = value, Maximum = value,
                    LastValue = value, Count = 1
                });
                return;
            }
            bucket.Sum += value;
            bucket.Minimum = Math.Min(bucket.Minimum, value);
            bucket.Maximum = Math.Max(bucket.Maximum, value);
            bucket.LastValue = value;
            bucket.Count++;
            buckets.Update(bucket);
        }

        private static bool IsBinarySeries(LiteDatabase db, string seriesKey)
        {
            // 表示文字が明確な二値である系列だけを対象にする。
            // 数値センサーが偶然 0 / 100 になった場合は二値扱いしない。
            List<string> values = GetLogCollection(db).Query()
                .Where(item => item.SeriesKey == seriesKey)
                .OrderByDescending(item => item.Timestamp)
                .Limit(1000)
                .ToEnumerable()
                .Select(item => item.RawValue.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (values.Count == 0) return false;
            return values.All(value => value.ToLowerInvariant() switch
            {
                "オン" or "オフ" or "はい" or "いいえ" or
                "施錠" or "解錠" or "開" or "閉" or
                "on" or "off" or "yes" or "no" or
                "true" or "false" or "locked" or "unlocked" or
                "open" or "closed" => true,
                _ => false
            });
        }

        private static void CloseSharedDatabase(string path)
        {
            if (!string.Equals(sharedDatabasePath, path,
                    StringComparison.OrdinalIgnoreCase)) return;
            sharedDatabase?.Dispose();
            sharedDatabase = null;
            sharedDatabasePath = "";
        }

        internal static IEnumerable<SensorLogEntry> CreateEntries(ServerSensorItem sensor)
        {
            DateTime timestamp = sensor.UpdatedAt?.ToLocalTime() ?? DateTime.Now;
            IEnumerable<SensorLogEntry> CreateServerData(string value)
            {
                if (string.IsNullOrWhiteSpace(value)) yield break;
                foreach (string part in value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    int separator = Math.Max(part.IndexOf(':'), part.IndexOf('：'));
                    string label = SensorDataNormalizer.NormalizeKey(
                        separator > 0 ? part[..separator].Trim() : "data");
                    string rawValue = SensorDataNormalizer.NormalizeValue(
                        separator > 0 ? part[(separator + 1)..].Trim() : part.Trim());
                    if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(rawValue)) continue;
                    SensorLogEntry entry = new()
                    {
                        Timestamp = timestamp, SensorId = sensor.Id, Provider = sensor.Provider,
                        DeviceName = sensor.DeviceName, Metric = $"Data:{label}", RawValue = rawValue,
                        NumericValue = TryParseNumber(rawValue)
                    };
                    PrepareEntry(entry);
                    yield return entry;
                }
            }
            foreach (SensorLogEntry entry in CreateServerData(sensor.OtherValue)) yield return entry;
            foreach (SensorLogEntry entry in CreateServerData(sensor.OtherValue2)) yield return entry;
            foreach (SensorLogEntry entry in CreateServerData(sensor.OtherValue3)) yield return entry;
            foreach (SensorLogEntry entry in CreateServerData(sensor.OtherValue4)) yield return entry;
            foreach (SensorLogEntry entry in CreateServerData(sensor.OtherValue5)) yield return entry;
            foreach (SensorLogEntry entry in CreateServerData(sensor.OtherValue6)) yield return entry;
            foreach (SensorLogEntry entry in CreateServerData(sensor.OtherValue7)) yield return entry;
            foreach (SensorLogEntry entry in CreateServerData(sensor.OtherValue8)) yield return entry;
        }

        private static double? TryParseNumber(string value)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariantValue)) return invariantValue;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double currentValue)) return currentValue;
            string numericPrefix = new(value.Trim().TakeWhile(character => char.IsDigit(character) || character is '+' or '-' or '.' or ',').ToArray());
            if (!string.IsNullOrWhiteSpace(numericPrefix) && double.TryParse(numericPrefix, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double prefixedValue)) return prefixedValue;
            string[] parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string part in parts)
            {
                string lowerPart = part.ToLowerInvariant();
                if ((part.Contains("ロック", StringComparison.OrdinalIgnoreCase) || lowerPart.Contains("lock")) &&
                    TryConvertBinaryStatus(GetStatusValue(part), out double lockValue)) return lockValue;
            }
            foreach (string part in parts)
                if (TryConvertBinaryStatus(GetStatusValue(part), out double stateValue)) return stateValue;
            return null;
        }

        private static string GetStatusValue(string value)
        {
            int separator = Math.Max(value.LastIndexOf(':'), value.LastIndexOf('：'));
            return (separator >= 0 ? value[(separator + 1)..] : value).Trim();
        }

        private static bool TryConvertBinaryStatus(string value, out double result)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "オン": case "on": case "施錠": case "lock": case "locked": case "latchboltlocked":
                case "はい": case "yes": case "true": case "開": case "open": case "opened": case "検出":
                case "detected": case "漏水": case "wet": case "オンライン": case "online": case "有効":
                case "enabled": case "動作中": case "moving": case "明るい": case "bright":
                    result = 100; return true;
                case "オフ": case "off": case "解錠": case "unlock": case "unlocked": case "latchboltunlocked":
                case "いいえ": case "no": case "false": case "閉": case "close": case "closed": case "未検出":
                case "not detected": case "乾燥": case "dry": case "オフライン": case "offline": case "無効":
                case "disabled": case "停止": case "stopped": case "暗い": case "dark":
                    result = 0; return true;
                default: result = 0; return false;
            }
        }

        private static void Cleanup(LiteDatabase db, ServerSensorSettings settings)
        {
            if (!settings.LogRetentionEnabled) return;
            string path = GetDatabasePath(settings);
            if (LastCleanupDates.TryGetValue(path, out DateTime last) && last == DateTime.Today) return;
            DateTime cutoff = DateTime.Today.AddDays(-Math.Clamp(settings.LogRetentionDays, 1, 3650));
            GetLogCollection(db).DeleteMany(item => item.Timestamp < cutoff);
            LastCleanupDates[path] = DateTime.Today;
        }

        private static IEnumerable<string> ParseCsv(string line)
        {
            StringBuilder value = new(); bool quoted = false;
            for (int index = 0; index < line.Length; index++)
            {
                char c = line[index];
                if (c == '"' && quoted && index + 1 < line.Length && line[index + 1] == '"') { value.Append('"'); index++; }
                else if (c == '"') quoted = !quoted;
                else if (c == ',' && !quoted) { yield return value.ToString(); value.Clear(); }
                else value.Append(c);
            }
            yield return value.ToString();
        }
    }
}
