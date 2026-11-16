using LiteDB;
using System;
using System.Collections.Generic;

namespace N_Remo
{
    public sealed class ServerSensorItem
    {
        [BsonId]
        public string Id { get; set; } = "";
        public string Provider { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string DeviceType { get; set; } = "";
        public double? Temperature { get; set; }
        public double? Humidity { get; set; }
        public double? Illuminance { get; set; }
        public int? Battery { get; set; }
        public string OtherValue { get; set; } = "";
        public string OtherValue2 { get; set; } = "";
        public string OtherValue3 { get; set; } = "";
        public string OtherValue4 { get; set; } = "";
        public string OtherValue5 { get; set; } = "";
        public string OtherValue6 { get; set; } = "";
        public string OtherValue7 { get; set; } = "";
        public string OtherValue8 { get; set; } = "";
        public string AllValues { get; set; } = "";
        public DateTime? UpdatedAt { get; set; }
        public string Status { get; set; } = "";
        public string AssignedButtonNumber { get; set; } = "";
        public string TemperatureButtonNumber { get; set; } = "";
        public string HumidityButtonNumber { get; set; } = "";
        public string IlluminanceButtonNumber { get; set; } = "";
        public string BatteryButtonNumber { get; set; } = "";
        public string OtherButtonNumber { get; set; } = "";
        public string OtherButtonNumber2 { get; set; } = "";
        public string OtherButtonNumber3 { get; set; } = "";
        public string OtherButtonNumber4 { get; set; } = "";
        public string OtherButtonNumber5 { get; set; } = "";
        public string OtherButtonNumber6 { get; set; } = "";
        public string OtherButtonNumber7 { get; set; } = "";
        public string OtherButtonNumber8 { get; set; } = "";
        public bool LogEnabled { get; set; }

        [BsonIgnore]
        public List<string> AvailableButtonNumbers { get; set; } = new();

        [BsonIgnore]
        public string TemperatureDisplay => Temperature.HasValue
            ? $"{Temperature.Value:0.0} ℃" : "－";
        [BsonIgnore]
        public string HumidityDisplay => Humidity.HasValue
            ? $"{Humidity.Value:0.#} %" : "－";
        [BsonIgnore]
        public string IlluminanceDisplay => Illuminance.HasValue
            ? $"{Illuminance.Value:0.#}" : "－";
        [BsonIgnore]
        public string BatteryDisplay => Battery.HasValue
            ? $"{Battery.Value} %" : "－";
        [BsonIgnore]
        public string UpdatedAtDisplay => UpdatedAt.HasValue
            ? UpdatedAt.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")
            : "－";
        [BsonIgnore] public string OtherValueDisplay => SensorDataNormalizer.DisplayStoredData(OtherValue);
        [BsonIgnore] public string OtherValue2Display => SensorDataNormalizer.DisplayStoredData(OtherValue2);
        [BsonIgnore] public string OtherValue3Display => SensorDataNormalizer.DisplayStoredData(OtherValue3);
        [BsonIgnore] public string OtherValue4Display => SensorDataNormalizer.DisplayStoredData(OtherValue4);
        [BsonIgnore] public string OtherValue5Display => SensorDataNormalizer.DisplayStoredData(OtherValue5);
        [BsonIgnore] public string OtherValue6Display => SensorDataNormalizer.DisplayStoredData(OtherValue6);
        [BsonIgnore] public string OtherValue7Display => SensorDataNormalizer.DisplayStoredData(OtherValue7);
        [BsonIgnore] public string OtherValue8Display => SensorDataNormalizer.DisplayStoredData(OtherValue8);
        [BsonIgnore] public string AllValuesDisplay => SensorDataNormalizer.DisplayStoredData(AllValues);

        public void CopyDataButtonAssignmentsFrom(ServerSensorItem old)
        {
            Dictionary<string, string> assignments =
                new(StringComparer.OrdinalIgnoreCase);
            AddAssignments(assignments,
                new[] { old.OtherValue, old.OtherValue2, old.OtherValue3, old.OtherValue4,
                        old.OtherValue5, old.OtherValue6, old.OtherValue7, old.OtherValue8 },
                new[] { old.OtherButtonNumber, old.OtherButtonNumber2,
                        old.OtherButtonNumber3, old.OtherButtonNumber4,
                        old.OtherButtonNumber5, old.OtherButtonNumber6,
                        old.OtherButtonNumber7, old.OtherButtonNumber8 });
            AddAssignment(assignments, "温度", old.TemperatureButtonNumber);
            AddAssignment(assignments, "湿度", old.HumidityButtonNumber);
            AddAssignment(assignments, "照度", old.IlluminanceButtonNumber);
            AddAssignment(assignments, "電池", old.BatteryButtonNumber);

            string[] values = { OtherValue, OtherValue2, OtherValue3, OtherValue4,
                OtherValue5, OtherValue6, OtherValue7, OtherValue8 };
            string[] buttons = new string[8];
            for (int index = 0; index < values.Length; index++)
                assignments.TryGetValue(GetDataLabel(values[index]), out buttons[index]!);
            OtherButtonNumber = buttons[0]; OtherButtonNumber2 = buttons[1];
            OtherButtonNumber3 = buttons[2]; OtherButtonNumber4 = buttons[3];
            OtherButtonNumber5 = buttons[4]; OtherButtonNumber6 = buttons[5];
            OtherButtonNumber7 = buttons[6]; OtherButtonNumber8 = buttons[7];
        }

        private static void AddAssignments(
            IDictionary<string, string> assignments,
            IReadOnlyList<string> values,
            IReadOnlyList<string> buttons)
        {
            for (int index = 0; index < values.Count; index++)
                AddAssignment(assignments, GetDataLabel(values[index]), buttons[index]);
        }

        private static void AddAssignment(
            IDictionary<string, string> assignments, string label, string button)
        {
            if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(button))
                assignments[label] = button;
        }

        private static string GetDataLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            int separator = Math.Max(value.IndexOf(':'), value.IndexOf('：'));
            return (separator > 0 ? value[..separator] : value).Trim();
        }
    }

    public sealed class ServerSensorSettings
    {
        [BsonId]
        public string Id { get; set; } = "Current";
        public bool AutoUpdateEnabled { get; set; } = false;
        public bool AutoUpdateSwitchBotWhenWebhookEnabled { get; set; } = false;
        public int UpdateIntervalSeconds { get; set; } = 300;
        public bool RefreshAfterCommandEnabled { get; set; }
        public int RefreshAfterCommandSeconds { get; set; } = 10;
        public bool LogRetentionEnabled { get; set; }
        public int LogRetentionDays { get; set; } = 30;
        public string LogDirectory { get; set; } = "";
        public string DisplayMode { get; set; } = "TemperatureHumidity";
    }
}
