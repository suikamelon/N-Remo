using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace N_Remo
{
    public static class NatureRemoCloudApiClient
    {
        private const string BaseUrl = "https://api.nature.global/1";
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        public static async Task<List<SwitchBotCommandItem>>
            GetSignalCommandsAsync()
        {
            string token = GetToken();
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                $"{BaseUrl}/appliances");
            request.Headers.TryAddWithoutValidation(
                "Authorization",
                $"Bearer {token}");

            using HttpResponseMessage response =
                await HttpClient.SendAsync(request);
            string json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Nature Remo Cloud APIへの接続に失敗しました。\n" +
                    $"HTTP: {(int)response.StatusCode}\n{json}");
            }

            using JsonDocument document = JsonDocument.Parse(json);
            List<SwitchBotCommandItem> commands = new();

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return commands;
            }

            foreach (JsonElement appliance in
                document.RootElement.EnumerateArray())
            {
                string applianceId = GetString(appliance, "id");
                string applianceName = GetString(appliance, "nickname");
                string applianceType = GetString(appliance, "type");

                if (!appliance.TryGetProperty("signals", out JsonElement signals) ||
                    signals.ValueKind != JsonValueKind.Array)
                {
                    signals = default;
                }

                if (signals.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement signal in signals.EnumerateArray())
                    {
                        string signalId = GetString(signal, "id");
                        string signalName = GetString(signal, "name");
                        commands.Add(CreateCommand(
                            $"NatureRemoCloud|signal|{signalId}",
                            signalId,
                            applianceName,
                            applianceType,
                            "send",
                            "signal",
                            signalName));
                    }
                }

                AddApplianceButtons(
                    appliance,
                    "tv",
                    applianceId,
                    applianceName,
                    applianceType,
                    commands);
                AddApplianceButtons(
                    appliance,
                    "light",
                    applianceId,
                    applianceName,
                    applianceType,
                    commands);

                if (string.Equals(
                    applianceType,
                    "AC",
                    StringComparison.OrdinalIgnoreCase))
                {
                    commands.Add(CreateCommand(
                        $"NatureRemoCloud|aircon|{applianceId}|power-on",
                        applianceId,
                        applianceName,
                        applianceType,
                        "power-on",
                        "aircon",
                        "電源オン"));
                    commands.Add(CreateCommand(
                        $"NatureRemoCloud|aircon|{applianceId}|power-off",
                        applianceId,
                        applianceName,
                        applianceType,
                        "power-off",
                        "aircon",
                        "電源オフ"));
                }
            }

            return commands;
        }

        public static async Task<List<ServerSensorItem>> GetSensorsAsync()
        {
            string token = GetToken();
            using HttpRequestMessage request = new(HttpMethod.Get, $"{BaseUrl}/devices");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            using HttpResponseMessage response = await HttpClient.SendAsync(request);
            string json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Nature Remoセンサーの取得に失敗しました。\nHTTP: {(int)response.StatusCode}\n{json}");
            }

            using JsonDocument document = JsonDocument.Parse(json);
            List<ServerSensorItem> sensors = new();
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return sensors;

            foreach (JsonElement device in document.RootElement.EnumerateArray())
            {
                string id = GetString(device, "id");
                string name = GetString(device, "name");
                if (!device.TryGetProperty("newest_events", out JsonElement events) ||
                    events.ValueKind != JsonValueKind.Object)
                    continue;

                ServerSensorItem sensor = new()
                {
                    Id = $"NatureRemo|{id}", Provider = "Nature Remo",
                    DeviceId = id, DeviceName = name, DeviceType = "Nature Remo",
                    Temperature = GetEventValue(events, "te"),
                    Humidity = GetEventValue(events, "hu"),
                    Illuminance = GetEventValue(events, "il"),
                    AllValues = BuildNatureSensorDetails(events),
                    UpdatedAt = GetNewestEventTime(events), Status = "正常"
                };
                List<string> values = new();
                if (sensor.Temperature.HasValue)
                    values.Add($"temperature: {sensor.Temperature.Value:0.0}");
                if (sensor.Humidity.HasValue)
                    values.Add($"humidity: {sensor.Humidity.Value:0.#}");
                if (sensor.Illuminance.HasValue)
                    values.Add($"illuminance: {sensor.Illuminance.Value:0.#}");
                if (GetEventValue(events, "mo") is double motion)
                    values.Add($"motionDetected: {motion:0.#}");
                sensor.OtherValue = values.ElementAtOrDefault(0) ?? "";
                sensor.OtherValue2 = values.ElementAtOrDefault(1) ?? "";
                sensor.OtherValue3 = values.ElementAtOrDefault(2) ?? "";
                sensor.OtherValue4 = values.ElementAtOrDefault(3) ?? "";
                if (sensor.Temperature.HasValue || sensor.Humidity.HasValue ||
                    sensor.Illuminance.HasValue || !string.IsNullOrWhiteSpace(sensor.OtherValue))
                    sensors.Add(sensor);
            }
            return sensors;
        }

        private static double? GetEventValue(JsonElement events, string name)
        {
            if (!events.TryGetProperty(name, out JsonElement item) ||
                !item.TryGetProperty("val", out JsonElement value))
                return null;
            return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number)
                ? number : null;
        }

        private static DateTime? GetNewestEventTime(JsonElement events)
        {
            DateTime? newest = null;
            foreach (JsonProperty property in events.EnumerateObject())
            {
                if (property.Value.TryGetProperty("created_at", out JsonElement value) &&
                    DateTime.TryParse(value.GetString(), out DateTime parsed) &&
                    (!newest.HasValue || parsed > newest.Value))
                    newest = parsed;
            }
            return newest;
        }

        private static string BuildNatureSensorDetails(JsonElement events)
        {
            List<string> values = new();
            foreach (JsonProperty property in events.EnumerateObject())
            {
                string label = property.Name switch
                {
                    "te" => "temperature", "hu" => "humidity", "il" => "illuminance",
                    "mo" => "motionDetected", _ => property.Name
                };
                string value = property.Value.TryGetProperty("val", out JsonElement val)
                    ? val.ToString() : property.Value.ToString();
                string updated = property.Value.TryGetProperty("created_at", out JsonElement time)
                    ? time.GetString() ?? "" : "";
                values.Add(string.IsNullOrWhiteSpace(updated)
                    ? $"{label}: {value}"
                    : $"{label}: {value} ({updated})");
            }
            return string.Join(" / ", values);
        }

        public static async Task SendCommandAsync(
            string targetId,
            string command,
            string commandType)
        {
            string token = GetToken();
            string path;
            HttpContent content;

            switch (commandType)
            {
                case "tv":
                case "light":
                    path = $"appliances/{Uri.EscapeDataString(targetId)}/{commandType}";
                    content = CreateFormContent("button", command);
                    break;
                case "aircon":
                    path = $"appliances/{Uri.EscapeDataString(targetId)}/aircon_settings";
                    content = CreateFormContent("button", command);
                    break;
                default:
                    path = $"signals/{Uri.EscapeDataString(targetId)}/send";
                    content = new StringContent("");
                    break;
            }

            using HttpRequestMessage request = new(
                HttpMethod.Post,
                $"{BaseUrl}/{path}");
            request.Headers.TryAddWithoutValidation(
                "Authorization",
                $"Bearer {token}");
            request.Content = content;

            using HttpResponseMessage response =
                await HttpClient.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Nature Remoコマンドの送信に失敗しました。\n" +
                    $"HTTP: {(int)response.StatusCode}\n{body}");
            }
        }

        private static FormUrlEncodedContent CreateFormContent(
            string name,
            string value)
        {
            return new FormUrlEncodedContent(
                new[]
                {
                    new KeyValuePair<string, string>(name, value)
                });
        }

        private static void AddApplianceButtons(
            JsonElement appliance,
            string sectionName,
            string applianceId,
            string applianceName,
            string applianceType,
            ICollection<SwitchBotCommandItem> commands)
        {
            if (!appliance.TryGetProperty(sectionName, out JsonElement section) ||
                section.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            JsonElement buttons;
            if ((!section.TryGetProperty("buttons", out buttons) &&
                 !section.TryGetProperty("button", out buttons)) ||
                buttons.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement button in buttons.EnumerateArray())
            {
                string name = GetString(button, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string description = GetString(button, "label");
                if (string.IsNullOrWhiteSpace(description))
                {
                    description = name;
                }

                commands.Add(CreateCommand(
                    $"NatureRemoCloud|{sectionName}|{applianceId}|{name}",
                    applianceId,
                    applianceName,
                    applianceType,
                    name,
                    sectionName,
                    description));
            }
        }

        private static SwitchBotCommandItem CreateCommand(
            string id,
            string targetId,
            string applianceName,
            string applianceType,
            string command,
            string commandType,
            string description)
        {
            return new SwitchBotCommandItem
            {
                Id = id,
                Provider = "NatureRemoCloud",
                SourceDisplay = "Nature Remoコマンド",
                DeviceId = targetId,
                DeviceName = applianceName,
                DeviceType = string.IsNullOrWhiteSpace(applianceType)
                    ? "Nature Remo"
                    : applianceType,
                Command = command,
                Parameter = "default",
                CommandType = commandType,
                Description = description
            };
        }

        private static string GetToken()
        {
            string token = Properties.Settings.Default
                .NatureRemoCloudToken?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException(
                    "Nature Remoクラウドアクセストークンを設定してください。");
            }

            return token;
        }

        private static string GetString(
            JsonElement element,
            string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement value)
                ? value.GetString() ?? ""
                : "";
        }
    }
}
