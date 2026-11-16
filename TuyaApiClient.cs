using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace N_Remo
{
    public static class TuyaApiClient
    {
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(25) };
        private static readonly SemaphoreSlim TokenLock = new(1, 1);
        private static string accessToken = "";
        private static DateTimeOffset accessTokenExpiresAt = DateTimeOffset.MinValue;

        public static void ResetAuthentication()
        {
            accessToken = "";
            accessTokenExpiresAt = DateTimeOffset.MinValue;
        }

        public static async Task<List<TuyaDevice>> GetDevicesAsync()
        {
            List<TuyaDevice> devices = new();
            string lastId = "";
            do
            {
                string path = "/v2.0/cloud/thing/device?page_size=20" +
                    (string.IsNullOrWhiteSpace(lastId) ? "" : $"&last_id={Uri.EscapeDataString(lastId)}");
                using JsonDocument document = await SendAsync(HttpMethod.Get, path);
                JsonElement result = GetResult(document.RootElement);
                JsonElement list = result.ValueKind == JsonValueKind.Array
                    ? result
                    : result.TryGetProperty("list", out JsonElement value) ? value : default;
                if (list.ValueKind != JsonValueKind.Array) break;
                foreach (JsonElement item in list.EnumerateArray())
                {
                    devices.Add(new TuyaDevice
                    {
                        DeviceId = GetString(item, "id", "device_id"),
                        DeviceName = GetString(item, "name", "device_name"),
                        DeviceType = GetString(item, "category", "product_name"),
                        SpaceId = GetString(item, "space_id", "spaceId", "home_id", "homeId")
                    });
                }
                bool hasMore = result.ValueKind == JsonValueKind.Object &&
                    result.TryGetProperty("has_more", out JsonElement more) && more.ValueKind == JsonValueKind.True;
                lastId = result.ValueKind == JsonValueKind.Object ? GetString(result, "last_id") : "";
                if (!hasMore || string.IsNullOrWhiteSpace(lastId)) break;
            } while (true);
            return devices.Where(device => !string.IsNullOrWhiteSpace(device.DeviceId)).ToList();
        }

        public static async Task<List<SwitchBotCommandItem>> GetCommandsAsync()
        {
            List<TuyaDevice> devices = await GetDevicesAsync();
            List<SwitchBotCommandItem> commands = new();
            foreach (TuyaDevice device in devices)
            {
                using JsonDocument document = await SendAsync(HttpMethod.Get,
                    $"/v1.0/iot-03/devices/{Uri.EscapeDataString(device.DeviceId)}/functions");
                JsonElement result = GetResult(document.RootElement);
                JsonElement functions = result.ValueKind == JsonValueKind.Array
                    ? result
                    : result.TryGetProperty("functions", out JsonElement value) ? value : default;
                if (functions.ValueKind != JsonValueKind.Array) continue;
                foreach (JsonElement function in functions.EnumerateArray())
                {
                    string code = GetString(function, "code");
                    string type = GetString(function, "type");
                    string values = GetString(function, "values");
                    foreach ((string parameter, string label) in GetParameters(type, values))
                    {
                        commands.Add(new SwitchBotCommandItem
                        {
                            Id = $"Tuya|{device.DeviceId}|{code}|{parameter}", Provider = "Tuya",
                            SourceDisplay = "Tuyaコマンド", DeviceId = device.DeviceId,
                            DeviceName = device.DeviceName, DeviceType = device.DeviceType,
                            Command = code, Parameter = parameter, CommandType = type,
                            Description = string.IsNullOrWhiteSpace(label) ? code : $"{code}: {label}"
                        });
                    }
                }
            }
            return commands;
        }

        public static async Task<List<SwitchBotCommandItem>> GetScenesAsync()
        {
            HashSet<string> spaceIds = (await GetDevicesAsync())
                .Select(device => device.SpaceId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (spaceIds.Count == 0)
            {
                using JsonDocument spacesDocument = await SendAsync(HttpMethod.Get,
                    "/v2.0/cloud/space/child?only_sub=false&page_size=100");
                JsonElement spacesResult = GetResult(spacesDocument.RootElement);
                JsonElement spaces = spacesResult.ValueKind == JsonValueKind.Array
                    ? spacesResult
                    : spacesResult.TryGetProperty("data", out JsonElement data)
                        ? data : default;
                if (spaces.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in spaces.EnumerateArray())
                    {
                        string id = item.ValueKind == JsonValueKind.Object
                            ? GetString(item, "id", "space_id", "spaceId")
                            : item.ToString();
                        if (!string.IsNullOrWhiteSpace(id)) spaceIds.Add(id);
                    }
                }
            }

            List<SwitchBotCommandItem> scenes = new();
            foreach (string spaceId in spaceIds)
            {
                for (int page = 1; page <= 100; page++)
                {
                    string path = "/v2.0/cloud/scene/rule" +
                        // Tuyaの署名ではクエリ名を辞書順に並べる必要がある。
                        $"?page_no={page}&page_size=100&space_id={Uri.EscapeDataString(spaceId)}&type=scene";
                    using JsonDocument document = await SendAsync(HttpMethod.Get, path);
                    JsonElement result = GetResult(document.RootElement);
                    JsonElement list = result.ValueKind == JsonValueKind.Array
                        ? result
                        : result.TryGetProperty("list", out JsonElement value)
                            ? value : default;
                    if (list.ValueKind != JsonValueKind.Array) break;
                    foreach (JsonElement item in list.EnumerateArray())
                    {
                        string id = GetString(item, "id", "rule_id");
                        string name = GetString(item, "name");
                        string ruleType = GetString(item, "type");
                        if (string.IsNullOrWhiteSpace(id) ||
                            (!string.IsNullOrWhiteSpace(ruleType) &&
                             !ruleType.Equals("scene", StringComparison.OrdinalIgnoreCase)))
                            continue;
                        scenes.Add(new SwitchBotCommandItem
                        {
                            Id = $"TuyaScene|{id}",
                            Provider = "TuyaScene",
                            SourceDisplay = "Tuyaシーン",
                            DeviceId = id,
                            DeviceName = string.IsNullOrWhiteSpace(name) ? "Tuyaシーン" : name,
                            DeviceType = "シーン",
                            Command = "execute",
                            Parameter = "default",
                            CommandType = "scene",
                            Description = string.IsNullOrWhiteSpace(name) ? "Tuyaシーン" : name
                        });
                    }
                    bool hasMore = result.ValueKind == JsonValueKind.Object &&
                        result.TryGetProperty("has_more", out JsonElement more) &&
                        more.ValueKind == JsonValueKind.True;
                    if (!hasMore) break;
                }
            }
            return scenes.GroupBy(scene => scene.Id)
                .Select(group => group.First()).ToList();
        }

        public static async Task SendSceneAsync(string ruleId)
        {
            using JsonDocument response = await SendAsync(HttpMethod.Post,
                $"/v2.0/cloud/scene/rule/{Uri.EscapeDataString(ruleId)}/actions/trigger");
            JsonElement result = GetResult(response.RootElement);
            if (result.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException(
                    "Tuyaシーンを実行できませんでした。\n" +
                    response.RootElement.GetRawText());
        }

        public static async Task<List<ServerSensorItem>> GetSensorsAsync()
        {
            List<TuyaDevice> devices = await GetDevicesAsync();
            List<ServerSensorItem> sensors = new();
            foreach (TuyaDevice device in devices)
            {
                try
                {
                    using JsonDocument document = await SendAsync(HttpMethod.Get,
                        $"/v1.0/iot-03/devices/{Uri.EscapeDataString(device.DeviceId)}/status");
                    JsonElement result = GetResult(document.RootElement);
                    if (result.ValueKind != JsonValueKind.Array) continue;
                    List<string> values = result.EnumerateArray()
                        .Select(item => $"{GetString(item, "code")}: {FormatValue(item.GetProperty("value"))}")
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
                    if (values.Count == 0) continue;
                    string[] data = new string[8];
                    for (int index = 0; index < Math.Min(7, values.Count); index++) data[index] = values[index];
                    if (values.Count >= 8) data[7] = string.Join(" / ", values.Skip(7));
                    sensors.Add(new ServerSensorItem
                    {
                        Id = $"Tuya|{device.DeviceId}", Provider = "Tuya", DeviceId = device.DeviceId,
                        DeviceName = device.DeviceName, DeviceType = device.DeviceType,
                        OtherValue = data[0], OtherValue2 = data[1], OtherValue3 = data[2], OtherValue4 = data[3],
                        OtherValue5 = data[4], OtherValue6 = data[5], OtherValue7 = data[6], OtherValue8 = data[7],
                        AllValues = string.Join(" / ", values), UpdatedAt = DateTime.UtcNow, Status = "正常"
                    });
                }
                catch { }
            }
            return sensors;
        }

        public static async Task SendCommandAsync(string deviceId, string code, string parameter, string type)
        {
            object value = ParseParameter(parameter, type);
            List<Dictionary<string, object>> commands = new();

            // 照明のモード・明るさ・色温度は、消灯中や別モードのままでは
            // APIが成功しても見た目に反映されないことがある。
            if (code.Equals("work_mode", StringComparison.OrdinalIgnoreCase) ||
                IsWhiteModeValueCommand(code))
            {
                commands.Add(new() { ["code"] = "switch_led", ["value"] = true });
            }
            if (IsWhiteModeValueCommand(code))
            {
                commands.Add(new() { ["code"] = "work_mode", ["value"] = "white" });
            }
            commands.Add(new() { ["code"] = code, ["value"] = value });

            string body = JsonSerializer.Serialize(new { commands });
            using JsonDocument response = await SendAsync(HttpMethod.Post,
                $"/v1.0/iot-03/devices/{Uri.EscapeDataString(deviceId)}/commands", body);

            JsonElement root = response.RootElement;
            if (!root.TryGetProperty("result", out JsonElement result))
            {
                throw new InvalidOperationException(
                    "Tuyaコマンドの応答にresultがありません。\n" +
                    root.GetRawText());
            }
            if (result.ValueKind == JsonValueKind.False ||
                result.ValueKind == JsonValueKind.Null)
            {
                string codeText = GetString(root, "code");
                string message = GetString(root, "msg", "message");
                throw new InvalidOperationException(
                    "Tuyaがコマンドの実行を拒否しました。\n" +
                    $"コード: {codeText}\n" +
                    $"メッセージ: {message}\n" +
                    $"応答: {root.GetRawText()}");
            }

            await VerifyCommandStateAsync(deviceId, code, value);
        }

        private static bool IsWhiteModeValueCommand(string code) =>
            code.Equals("bright_value", StringComparison.OrdinalIgnoreCase) ||
            code.Equals("bright_value_v2", StringComparison.OrdinalIgnoreCase) ||
            code.Equals("temp_value", StringComparison.OrdinalIgnoreCase) ||
            code.Equals("temp_value_v2", StringComparison.OrdinalIgnoreCase);

        private static async Task VerifyCommandStateAsync(
            string deviceId, string code, object expectedValue)
        {
            string actualValue = "未取得";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                await Task.Delay(800);
                using JsonDocument statusResponse = await SendAsync(HttpMethod.Get,
                    $"/v1.0/iot-03/devices/{Uri.EscapeDataString(deviceId)}/status");
                JsonElement statuses = GetResult(statusResponse.RootElement);
                if (statuses.ValueKind != JsonValueKind.Array) continue;
                foreach (JsonElement status in statuses.EnumerateArray())
                {
                    if (!string.Equals(GetString(status, "code"), code,
                            StringComparison.OrdinalIgnoreCase) ||
                        !status.TryGetProperty("value", out JsonElement actual)) continue;
                    actualValue = FormatValue(actual);
                    if (TuyaValuesEqual(actual, expectedValue)) return;
                }
            }

            throw new InvalidOperationException(
                "Tuya APIはコマンドを受理しましたが、デバイス状態に反映されませんでした。\n" +
                $"コマンド: {code}\n" +
                $"送信値: {JsonSerializer.Serialize(expectedValue)}\n" +
                $"現在値: {actualValue}");
        }

        private static bool TuyaValuesEqual(JsonElement actual, object expected)
        {
            if (expected is bool boolean)
                return actual.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                    actual.GetBoolean() == boolean;
            if (expected is long integer)
                return actual.TryGetInt64(out long actualInteger) &&
                    actualInteger == integer;
            if (expected is double number)
                return actual.TryGetDouble(out double actualNumber) &&
                    Math.Abs(actualNumber - number) < 0.000001;
            return string.Equals(actual.ToString(), expected.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<JsonDocument> SendAsync(HttpMethod method, string path, string body = "")
        {
            path = NormalizeSignedPath(path);
            string token = path.StartsWith("/v1.0/token", StringComparison.Ordinal) ? "" : await GetAccessTokenAsync();
            string clientId = Properties.Settings.Default.TuyaAccessId?.Trim() ?? "";
            string secret = Properties.Settings.Default.TuyaAccessSecret?.Trim() ?? "";
            string endpoint = (Properties.Settings.Default.TuyaEndpoint ?? "https://openapi.tuyaus.com").TrimEnd('/');
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException("TuyaのAccess IDとAccess Secretを設定してください。");
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            string nonce = Guid.NewGuid().ToString("N");
            string contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            string stringToSign = $"{method.Method.ToUpperInvariant()}\n{contentHash}\n\n{path}";
            string signSource = clientId + token + timestamp + nonce + stringToSign;
            using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(secret));
            string sign = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signSource)));
            using HttpRequestMessage request = new(method, endpoint + path);
            request.Headers.TryAddWithoutValidation("client_id", clientId);
            request.Headers.TryAddWithoutValidation("sign", sign);
            request.Headers.TryAddWithoutValidation("sign_method", "HMAC-SHA256");
            request.Headers.TryAddWithoutValidation("t", timestamp);
            request.Headers.TryAddWithoutValidation("nonce", nonce);
            request.Headers.TryAddWithoutValidation("lang", "ja");
            if (!string.IsNullOrWhiteSpace(token)) request.Headers.TryAddWithoutValidation("access_token", token);
            if (!string.IsNullOrEmpty(body)) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await HttpClient.SendAsync(request);
            string json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Tuya API接続エラー HTTP {(int)response.StatusCode}\n{json}");
            JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("success", out JsonElement success) && success.ValueKind == JsonValueKind.False)
            {
                string code = GetString(document.RootElement, "code");
                string message = GetString(document.RootElement, "msg", "message");
                string responseJson = document.RootElement.GetRawText();
                document.Dispose();
                throw new InvalidOperationException(
                    "Tuya APIエラー\n" +
                    $"コード: {code}\n" +
                    $"メッセージ: {message}\n" +
                    $"応答: {responseJson}");
            }
            return document;
        }

        private static string NormalizeSignedPath(string path)
        {
            int queryIndex = path.IndexOf('?');
            if (queryIndex < 0 || queryIndex == path.Length - 1) return path;
            string query = string.Join("&", path[(queryIndex + 1)..]
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .OrderBy(part => part.Split('=', 2)[0], StringComparer.Ordinal)
                .ThenBy(part => part, StringComparer.Ordinal));
            return path[..queryIndex] + "?" + query;
        }

        private static async Task<string> GetAccessTokenAsync()
        {
            if (!string.IsNullOrWhiteSpace(accessToken) && DateTimeOffset.UtcNow < accessTokenExpiresAt) return accessToken;
            await TokenLock.WaitAsync();
            try
            {
                if (!string.IsNullOrWhiteSpace(accessToken) && DateTimeOffset.UtcNow < accessTokenExpiresAt) return accessToken;
                using JsonDocument document = await SendAsync(HttpMethod.Get, "/v1.0/token?grant_type=1");
                JsonElement result = GetResult(document.RootElement);
                accessToken = GetString(result, "access_token");
                int seconds = result.TryGetProperty("expire_time", out JsonElement expire) && expire.TryGetInt32(out int value) ? value : 7200;
                accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, seconds - 60));
                return accessToken;
            }
            finally { TokenLock.Release(); }
        }

        private static JsonElement GetResult(JsonElement root)
        {
            if (!root.TryGetProperty("result", out JsonElement result)) throw new InvalidOperationException("Tuya APIの応答にresultがありません。");
            return result;
        }

        private static string GetString(JsonElement item, params string[] names)
        {
            foreach (string name in names) if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out JsonElement value))
                return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
            return "";
        }

        private static IEnumerable<(string Parameter, string Label)> GetParameters(string type, string values)
        {
            if (type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
            { yield return ("true", "オン"); yield return ("false", "オフ"); yield break; }
            if (type.Equals("Enum", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(values))
            {
                using JsonDocument document = JsonDocument.Parse(values);
                if (document.RootElement.TryGetProperty("range", out JsonElement range) && range.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement item in range.EnumerateArray()) { string value = item.ToString(); yield return (value, value); }
                yield break;
            }
            if (type.Equals("Integer", StringComparison.OrdinalIgnoreCase) || type.Equals("Value", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(values)) yield break;
                JsonDocument? document = null;
                try { document = JsonDocument.Parse(values); }
                catch { yield break; }
                using (document)
                {
                    if (!document.RootElement.TryGetProperty("min", out JsonElement minElement) ||
                        !document.RootElement.TryGetProperty("max", out JsonElement maxElement) ||
                        !minElement.TryGetInt64(out long minimum) ||
                        !maxElement.TryGetInt64(out long maximum)) yield break;
                    long step = document.RootElement.TryGetProperty("step", out JsonElement stepElement) &&
                        stepElement.TryGetInt64(out long parsedStep) && parsedStep > 0
                            ? parsedStep : 1;
                    HashSet<long> emitted = new();
                    foreach (int percent in new[] { 20, 40, 60, 80, 100 })
                    {
                        double requested = maximum * percent / 100d;
                        long normalized = minimum + (long)Math.Round(
                            (requested - minimum) / step,
                            MidpointRounding.AwayFromZero) * step;
                        normalized = Math.Clamp(normalized, minimum, maximum);
                        if (emitted.Add(normalized))
                            yield return (normalized.ToString(CultureInfo.InvariantCulture),
                                $"{percent}%（{normalized}）");
                    }
                }
                yield break;
            }
            // Json・Rawなど、空文字では実行できない複雑な型は表示しない。
            yield break;
        }

        private static object ParseParameter(string parameter, string type)
        {
            if (type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) && bool.TryParse(parameter, out bool boolean)) return boolean;
            if ((type.Equals("Integer", StringComparison.OrdinalIgnoreCase) || type.Equals("Value", StringComparison.OrdinalIgnoreCase)) &&
                long.TryParse(parameter, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)) return number;
            if ((type.Equals("Json", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("Raw", StringComparison.OrdinalIgnoreCase)) &&
                string.IsNullOrWhiteSpace(parameter))
                throw new InvalidOperationException(
                    $"Tuyaの{type}型コマンドは空のまま送信できません。");
            if (!string.IsNullOrWhiteSpace(parameter) && (parameter.TrimStart().StartsWith("{") || parameter.TrimStart().StartsWith("[")))
                return JsonSerializer.Deserialize<JsonElement>(parameter);
            return parameter;
        }

        private static string FormatValue(JsonElement value) => value.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? value.GetRawText() : value.ToString();
    }

    public sealed class TuyaDevice
    {
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string DeviceType { get; set; } = "";
        public string SpaceId { get; set; } = "";
    }
}
