using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace N_Remo
{
    public sealed class SwitchBotDevice
    {
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string DeviceType { get; set; } = "";
    }

    public sealed class SwitchBotCommandItem
    {
        [LiteDB.BsonId]
        public string Id { get; set; } = "";

        [LiteDB.BsonIgnore]
        public string AssignedButtonNumbers { get; set; } = "";

        [LiteDB.BsonIgnore]
        public List<string> AvailableButtonNumbers { get; set; } = new();

        public string Provider { get; set; } = "SwitchBot";
        public string SourceDisplay { get; set; } = "SwitchBotコマンド";

        [LiteDB.BsonIgnore]
        public string SourceDisplayLocalized => SourceDisplay switch
        {
            "SwitchBotコマンド" =>
                $"SwitchBot {LanguageManager.Get("コマンド")}",
            "SwitchBotシーン" =>
                $"SwitchBot {LanguageManager.Get("シーン")}",
            "Nature Remoコマンド" =>
                $"Nature Remo {LanguageManager.Get("コマンド")}",
            "Tuyaコマンド" =>
                $"Tuya {LanguageManager.Get("コマンド")}",
            "Tuyaシーン" =>
                $"Tuya {LanguageManager.Get("シーン")}",
            _ => SourceDisplay
        };

        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string DeviceType { get; set; } = "";
        public string Command { get; set; } = "";
        public string Parameter { get; set; } = "default";
        public string CommandType { get; set; } = "command";
        public string Description { get; set; } = "";
    }

    public static class SwitchBotApiClient
    {
        private const string BaseUrl = "https://api.switch-bot.com/v1.1";
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        public static async Task<List<SwitchBotDevice>> GetDevicesAsync()
        {
            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Get,
                $"{BaseUrl}/devices");

            using HttpResponseMessage response =
                await HttpClient.SendAsync(request);

            string json = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, json);

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement body = document.RootElement.GetProperty("body");
            List<SwitchBotDevice> devices = new();

            AddDevices(body, "deviceList", devices);
            AddDevices(body, "infraredRemoteList", devices);
            return devices;
        }

        public static async Task<List<ServerSensorItem>> GetSensorsAsync()
        {
            List<SwitchBotDevice> devices = await GetDevicesAsync();
            List<Task<ServerSensorItem?>> tasks = devices
                .Where(device => !string.IsNullOrWhiteSpace(device.DeviceId))
                .Select(GetSensorAsync).ToList();
            ServerSensorItem?[] results = await Task.WhenAll(tasks);
            return results.Where(item => item != null).Cast<ServerSensorItem>().ToList();
        }

        private static async Task<ServerSensorItem?> GetSensorAsync(SwitchBotDevice device)
        {
            try
            {
                using HttpRequestMessage request = CreateRequest(
                    HttpMethod.Get,
                    $"{BaseUrl}/devices/{Uri.EscapeDataString(device.DeviceId)}/status");
                using HttpResponseMessage response = await HttpClient.SendAsync(request);
                string json = await response.Content.ReadAsStringAsync();
                EnsureSuccess(response, json);
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("statusCode", out JsonElement statusCode) &&
                    statusCode.ValueKind == JsonValueKind.Number &&
                    statusCode.GetInt32() != 100)
                    return null;
                if (!document.RootElement.TryGetProperty("body", out JsonElement body) ||
                    body.ValueKind != JsonValueKind.Object)
                    return null;

                double? temperature = GetNumber(body, "temperature", "temp");
                double? humidity = GetNumber(body, "humidity");
                double? illuminance = GetNumber(body, "lightLevel", "illuminance");
                int? battery = GetNumber(body, "battery") is double batteryValue
                    ? (int)Math.Round(batteryValue) : null;
                string[] otherValues = GetOtherSensorValues(body);
                string allValues = BuildStatusDetails(body);
                if (string.IsNullOrWhiteSpace(allValues))
                    return null;

                return new ServerSensorItem
                {
                    Id = $"SwitchBot|{device.DeviceId}", Provider = "SwitchBot",
                    DeviceId = device.DeviceId, DeviceName = device.DeviceName,
                    DeviceType = device.DeviceType, Temperature = temperature,
                    Humidity = humidity, Illuminance = illuminance, Battery = battery,
                    OtherValue = otherValues[0], OtherValue2 = otherValues[1],
                    OtherValue3 = otherValues[2], OtherValue4 = otherValues[3],
                    OtherValue5 = otherValues[4], OtherValue6 = otherValues[5],
                    OtherValue7 = otherValues[6], OtherValue8 = otherValues[7],
                    AllValues = allValues,
                    UpdatedAt = DateTime.UtcNow, Status = "正常"
                };
            }
            catch
            {
                return null;
            }
        }

        private static double? GetNumber(JsonElement element, params string[] names)
        {
            foreach (string name in names)
            {
                if (element.TryGetProperty(name, out JsonElement value) &&
                    value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
                    return number;
            }
            return null;
        }

        private static string[] GetOtherSensorValues(JsonElement body)
        {
            // 機種ごとの固定リストにはせず、状態JSONに含まれる全項目から
            // デバイス識別用の共通情報だけを除外し、測定値を含む全状態を取り込む。
            HashSet<string> excludedNames = new(StringComparer.OrdinalIgnoreCase)
            {
                "deviceId", "deviceType", "hubDeviceId", "version"
            };
            List<string> values = body.EnumerateObject()
                .Where(property =>
                    !excludedNames.Contains(property.Name) &&
                    property.Value.ValueKind is not JsonValueKind.Null and
                        not JsonValueKind.Undefined)
                // JSONのプロパティ順が変化してもデータ列が入れ替わらないようにする。
                .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .Select(property =>
                    $"{SensorDataNormalizer.NormalizeKey(property.Name)}: {FormatStatusValue(property.Value)}")
                .ToList();

            string[] result = new string[8];
            for (int index = 0; index < Math.Min(7, values.Count); index++)
                result[index] = values[index];
            if (values.Count >= 8)
                result[7] = string.Join(" / ", values.Skip(7));
            return result;
        }

        private static string FormatStatusValue(JsonElement value)
        {
            return value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? value.GetRawText()
                : SensorDataNormalizer.NormalizeValue(value.ToString());
        }

        private static string GetStatusLabel(string propertyName)
        {
            return propertyName switch
            {
                "deviceId" => "デバイスID", "deviceType" => "種類",
                "hubDeviceId" => "Hub ID", "battery" => "電池",
                "temperature" or "temp" => "温度", "humidity" => "湿度",
                "lockState" => "ロック", "doorState" => "ドア",
                "calibrate" => "校正", "version" => "バージョン",
                "moveDetected" or "motionDetected" => "人感",
                "openState" => "開閉", "waterLeakStatus" => "漏水",
                "brightness" => "明るさ", "lightLevel" => "照度",
                "power" => "電源", "slidePosition" => "位置",
                "moving" => "動作中", "onlineStatus" => "オンライン",
                "status" => "状態", _ => propertyName
            };
        }

        private static string TranslateStatusValue(JsonElement value)
        {
            string text = value.ToString();
            return text.ToLowerInvariant() switch
            {
                "lock" or "locked" => "施錠",
                "latchboltlocked" => "施錠",
                "unlock" or "unlocked" => "解錠",
                "latchboltunlocked" => "解錠",
                "jammed" => "引っ掛かり",
                "open" => "開",
                "close" or "closed" => "閉",
                "timeoutnotclose" => "開放状態",
                "true" => "はい",
                "false" => "いいえ",
                "on" => "オン",
                "off" => "オフ",
                _ => text
            };
        }

        private static string BuildStatusDetails(JsonElement body)
        {
            List<string> values = new();
            foreach (JsonProperty property in body.EnumerateObject())
            {
                values.Add($"{SensorDataNormalizer.NormalizeKey(property.Name)}: " +
                    FormatStatusValue(property.Value));
            }
            return string.Join(" / ", values);
        }

        public static List<SwitchBotCommandItem> GetCommandCatalog(
            IEnumerable<SwitchBotDevice> devices)
        {
            return devices
                .SelectMany(device => GetCommands(device).Select(command =>
                    new SwitchBotCommandItem
                    {
                        Id = $"{device.DeviceId}|{command.Command}|{command.CommandType}",
                        Provider = "SwitchBot",
                        SourceDisplay = "SwitchBotコマンド",
                        DeviceId = device.DeviceId,
                        DeviceName = device.DeviceName,
                        DeviceType = device.DeviceType,
                        Command = command.Command,
                        Parameter = command.Parameter,
                        CommandType = command.CommandType,
                        Description = command.Description
                    }))
                .ToList();
        }

        public static async Task<List<SwitchBotCommandItem>>
            GetScenesAsync()
        {
            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Get,
                $"{BaseUrl}/scenes");

            using HttpResponseMessage response =
                await HttpClient.SendAsync(request);

            string json = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, json);

            using JsonDocument document = JsonDocument.Parse(json);
            List<SwitchBotCommandItem> scenes = new();

            if (document.RootElement.TryGetProperty("body", out JsonElement body) &&
                body.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement scene in body.EnumerateArray())
                {
                    string sceneId = GetString(scene, "sceneId");
                    string sceneName = GetString(scene, "sceneName");
                    scenes.Add(new SwitchBotCommandItem
                    {
                        Id = $"SwitchBotScene|{sceneId}",
                        Provider = "SwitchBotScene",
                        SourceDisplay = "SwitchBotシーン",
                        DeviceId = sceneId,
                        DeviceName = sceneName,
                        DeviceType = "シーン",
                        Command = "execute",
                        Parameter = "default",
                        CommandType = "scene",
                        Description = sceneName
                    });
                }
            }

            return scenes;
        }

        public static async Task SendSceneAsync(string sceneId)
        {
            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Post,
                $"{BaseUrl}/scenes/{Uri.EscapeDataString(sceneId)}/execute");

            request.Content = new StringContent(
                "{}",
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response =
                await HttpClient.SendAsync(request);
            string json = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, json);
        }

        public static async Task SetupWebhookAsync(string webhookUrl)
        {
            if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out _))
                throw new InvalidOperationException("Webhook受信URLが正しくありません。");
            await SendWebhookRequestAsync("setupWebhook", new
            {
                action = "setupWebhook", url = webhookUrl, deviceList = "ALL"
            });
        }

        public static Task<string> QueryWebhookAsync() =>
            SendWebhookRequestAsync("queryWebhook", new { action = "queryUrl" });

        public static async Task DeleteWebhookAsync(string webhookUrl)
        {
            await SendWebhookRequestAsync("deleteWebhook", new
            {
                action = "deleteWebhook", url = webhookUrl
            });
        }

        private static async Task<string> SendWebhookRequestAsync(
            string endpoint, object body)
        {
            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Post, $"{BaseUrl}/webhook/{endpoint}");
            request.Content = new StringContent(JsonSerializer.Serialize(body),
                Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await HttpClient.SendAsync(request);
            string json = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, json);
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("statusCode", out JsonElement code) &&
                code.ValueKind == JsonValueKind.Number && code.GetInt32() != 100)
            {
                string message = document.RootElement.TryGetProperty(
                    "message", out JsonElement value)
                        ? value.GetString() ?? "SwitchBot Webhook APIエラー"
                        : "SwitchBot Webhook APIエラー";
                throw new InvalidOperationException(
                    $"{message} (code: {code.GetInt32()})\n{json}");
            }
            return json;
        }

        public static async Task SendCommandAsync(
            string deviceId,
            string command,
            string parameter,
            string commandType)
        {
            var body = new
            {
                command,
                parameter = string.IsNullOrWhiteSpace(parameter)
                    ? "default"
                    : parameter,
                commandType = string.IsNullOrWhiteSpace(commandType)
                    ? "command"
                    : commandType
            };

            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Post,
                $"{BaseUrl}/devices/{Uri.EscapeDataString(deviceId)}/commands");

            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response =
                await HttpClient.SendAsync(request);

            string json = await response.Content.ReadAsStringAsync();
            EnsureSuccess(response, json);

            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("statusCode", out JsonElement code) &&
                code.GetInt32() != 100)
            {
                string message = document.RootElement.TryGetProperty("message", out JsonElement value)
                    ? value.GetString() ?? "SwitchBot APIエラー"
                    : "SwitchBot APIエラー";
                throw new InvalidOperationException($"{message} (code: {code})");
            }
        }

        private static HttpRequestMessage CreateRequest(
            HttpMethod method,
            string url)
        {
            string token = Properties.Settings.Default.SwitchBotToken?.Trim() ?? "";
            string secret = Properties.Settings.Default.SwitchBotSecret?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException(
                    "SwitchBotのTokenとSecretを設定してください。");
            }

            string timestamp = DateTimeOffset.UtcNow
                .ToUnixTimeMilliseconds()
                .ToString();
            string nonce = Guid.NewGuid().ToString();
            string data = token + timestamp + nonce;

            using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(secret));
            string sign = Convert.ToBase64String(
                hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));

            HttpRequestMessage request = new(method, url);
            request.Headers.TryAddWithoutValidation("Authorization", token);
            request.Headers.TryAddWithoutValidation("sign", sign);
            request.Headers.TryAddWithoutValidation("nonce", nonce);
            request.Headers.TryAddWithoutValidation("t", timestamp);
            return request;
        }

        private static void AddDevices(
            JsonElement body,
            string propertyName,
            ICollection<SwitchBotDevice> devices)
        {
            if (!body.TryGetProperty(propertyName, out JsonElement list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement item in list.EnumerateArray())
            {
                devices.Add(new SwitchBotDevice
                {
                    DeviceId = GetString(item, "deviceId"),
                    DeviceName = GetString(item, "deviceName"),
                    DeviceType = GetString(item, "deviceType", "remoteType")
                });
            }
        }

        private static string GetString(
            JsonElement item,
            params string[] names)
        {
            foreach (string name in names)
            {
                if (item.TryGetProperty(name, out JsonElement value))
                {
                    return value.GetString() ?? "";
                }
            }
            return "";
        }

        private static IEnumerable<(string Command, string Parameter, string CommandType, string Description)>
            GetCommands(SwitchBotDevice device)
        {
            string type = device.DeviceType.ToLowerInvariant();

            if (type.Contains("lock"))
            {
                yield return ("lock", "default", "command", "施錠");
                yield return ("unlock", "default", "command", "解錠");
                yield break;
            }

            if (type.Contains("curtain") || type.Contains("blind"))
            {
                yield return ("turnOn", "default", "command", "開く");
                yield return ("turnOff", "default", "command", "閉じる");
                yield return ("pause", "default", "command", "停止");
                yield break;
            }

            if (type == "bot")
            {
                yield return ("press", "default", "command", "押す");
                yield return ("turnOn", "default", "command", "オン");
                yield return ("turnOff", "default", "command", "オフ");
                yield break;
            }

            if (type.Contains("tv") || type.Contains("air conditioner") ||
                type.Contains("light") || type.Contains("fan") ||
                type.Contains("plug") || type.Contains("humidifier") ||
                type.Contains("purifier") || type.Contains("heater"))
            {
                yield return ("turnOn", "default", "command", "オン");
                yield return ("turnOff", "default", "command", "オフ");
                yield break;
            }

            yield return ("turnOn", "default", "command", "オン（対応機器のみ）");
            yield return ("turnOff", "default", "command", "オフ（対応機器のみ）");
        }

        private static void EnsureSuccess(
            HttpResponseMessage response,
            string body)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"SwitchBot APIへの接続に失敗しました。\n" +
                    $"HTTP: {(int)response.StatusCode}\n{body}");
            }
        }
    }
}
