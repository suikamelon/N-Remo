using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace N_Remo
{
    public sealed class WebhookRelayEvent
    {
        public long Id { get; set; }
        public DateTime ReceivedAt { get; set; }
        public string Provider { get; set; } = "SwitchBot";
        public string DeviceMac { get; set; } = "";
        public string EventType { get; set; } = "";
        public JsonElement Context { get; set; }
    }

    public static class WebhookRelayClient
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static async Task<string> CheckHealthAsync()
        {
            string provider = NormalizeProvider(Properties.Settings.Default.WebhookRelayProvider);
            return provider == "Firebase"
                ? await CheckFirebaseAsync()
                : await CheckCloudflareAsync();
        }

        public static async Task RunRealtimeAsync(
            long after,
            Func<WebhookRelayEvent, Task> onEvent,
            Action<string>? onStatus,
            CancellationToken cancellationToken)
        {
            string provider = NormalizeProvider(Properties.Settings.Default.WebhookRelayProvider);
            if (provider == "Firebase")
                await RunFirebaseSseAsync(onEvent, onStatus, cancellationToken);
            else
                await RunCloudflareWebSocketAsync(after, onEvent, onStatus, cancellationToken);
        }

        private static string NormalizeProvider(string? provider) =>
            string.Equals(provider, "Firebase", StringComparison.OrdinalIgnoreCase)
                ? "Firebase" : "CloudflareRealtime";

        private static async Task<string> CheckCloudflareAsync()
        {
            string baseUrl = GetBaseUrl();
            string apiKey = GetApiKey();
            Uri target = new(new Uri(baseUrl.TrimEnd('/') + "/"), "health");
            using HttpRequestMessage request = new(HttpMethod.Get, target);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using HttpResponseMessage response = await HttpClient.SendAsync(request);
            string content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Cloudflare接続エラー HTTP {(int)response.StatusCode}\n{content}");
            return LanguageManager.Get("Cloudflareリアルタイム中継へ接続できます。");
        }

        private static async Task<string> CheckFirebaseAsync()
        {
            string token = await SignInFirebaseAsync();
            using HttpResponseMessage response = await HttpClient.GetAsync(
                BuildFirebaseEventsUri(token, shallow: true));
            string content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Firebase接続エラー HTTP {(int)response.StatusCode}\n{content}");
            return LanguageManager.Get("Firebase Realtime Databaseへ接続できます。");
        }

        private static async Task RunCloudflareWebSocketAsync(
            long after,
            Func<WebhookRelayEvent, Task> onEvent,
            Action<string>? onStatus,
            CancellationToken cancellationToken)
        {
            string baseUrl = GetBaseUrl();
            string apiKey = GetApiKey();
            Uri httpUri = new(baseUrl.TrimEnd('/') + "/");
            UriBuilder builder = new(httpUri)
            {
                Scheme = httpUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                    ? "wss" : "ws",
                Path = httpUri.AbsolutePath.TrimEnd('/') + "/ws",
                Query = $"after={after}"
            };

            using ClientWebSocket socket = new();
            socket.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            await socket.ConnectAsync(builder.Uri, cancellationToken);
            onStatus?.Invoke(LanguageManager.Get("Cloudflare WebSocketに接続しました。"));
            byte[] buffer = new byte[16384];
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using MemoryStream message = new();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,
                            "closing", CancellationToken.None);
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text) continue;
                WebhookRelayEvent? relayEvent = JsonSerializer.Deserialize<WebhookRelayEvent>(
                    Encoding.UTF8.GetString(message.ToArray()), JsonOptions);
                if (relayEvent != null) await onEvent(relayEvent);
            }
        }

        private static async Task RunFirebaseSseAsync(
            Func<WebhookRelayEvent, Task> onEvent,
            Action<string>? onStatus,
            CancellationToken cancellationToken)
        {
            string token = await SignInFirebaseAsync();
            using HttpRequestMessage request = new(HttpMethod.Get,
                BuildFirebaseEventsUri(token, shallow: false));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            using HttpResponseMessage response = await HttpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Firebase SSE接続エラー HTTP {(int)response.StatusCode}");
            onStatus?.Invoke(LanguageManager.Get("Firebase SSEに接続しました。"));

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using StreamReader reader = new(stream);
            string eventName = "";
            StringBuilder data = new();
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(cancellationToken);
                if (line == null) return;
                if (line.StartsWith("event:", StringComparison.Ordinal))
                    eventName = line[6..].Trim();
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                    data.Append(line[5..].Trim());
                else if (line.Length == 0)
                {
                    if ((eventName == "put" || eventName == "patch") && data.Length > 0)
                        foreach (WebhookRelayEvent item in ParseFirebaseEvents(data.ToString()))
                            await onEvent(item);
                    if (eventName is "cancel" or "auth_revoked")
                        throw new InvalidOperationException("Firebaseの購読認証が失効しました。");
                    eventName = "";
                    data.Clear();
                }
            }
        }

        private static IEnumerable<WebhookRelayEvent> ParseFirebaseEvents(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
                data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                yield break;
            if (LooksLikeEvent(data))
            {
                WebhookRelayEvent? single = data.Deserialize<WebhookRelayEvent>(JsonOptions);
                if (single != null) yield return single;
                yield break;
            }
            if (data.ValueKind != JsonValueKind.Object) yield break;
            foreach (JsonProperty property in data.EnumerateObject())
            {
                if (!LooksLikeEvent(property.Value)) continue;
                WebhookRelayEvent? item = property.Value.Deserialize<WebhookRelayEvent>(JsonOptions);
                if (item != null) yield return item;
            }
        }

        private static bool LooksLikeEvent(JsonElement value) =>
            value.ValueKind == JsonValueKind.Object && value.TryGetProperty("deviceMac", out _);

        private static async Task<string> SignInFirebaseAsync()
        {
            string webApiKey = GetApiKey();
            string email = Properties.Settings.Default.WebhookFirebaseEmail?.Trim() ?? "";
            string password = Properties.Settings.Default.WebhookFirebasePassword ?? "";
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException(
                    "Firebase専用ユーザーのメールアドレスとパスワードを入力してください。");
            using StringContent body = new(JsonSerializer.Serialize(new
            {
                email,
                password,
                returnSecureToken = true
            }), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await HttpClient.PostAsync(
                "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=" +
                Uri.EscapeDataString(webApiKey), body);
            string json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Firebase匿名認証エラー HTTP {(int)response.StatusCode}\n{json}");
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("idToken", out JsonElement token))
                throw new InvalidOperationException("Firebase認証トークンを取得できませんでした。");
            return token.GetString() ?? throw new InvalidOperationException(
                "Firebase認証トークンが空です。");
        }

        private static Uri BuildFirebaseEventsUri(string token, bool shallow)
        {
            string suffix = shallow ? "&shallow=true" : "";
            return new Uri(GetBaseUrl().TrimEnd('/') +
                "/events.json?auth=" + Uri.EscapeDataString(token) + suffix);
        }

        private static string GetBaseUrl()
        {
            string value = Properties.Settings.Default.WebhookRelayBaseUrl?.Trim() ?? "";
            if (!Uri.TryCreate(value, UriKind.Absolute, out _))
                throw new InvalidOperationException("リアルタイム接続URLが正しくありません。");
            return value;
        }

        private static string GetApiKey()
        {
            string value = Properties.Settings.Default.WebhookRelayApiKey?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("接続キーを入力してください。");
            return value;
        }

        public static ServerSensorItem? ConvertToSensor(
            WebhookRelayEvent relayEvent, ServerSensorItem? saved)
        {
            if (string.IsNullOrWhiteSpace(relayEvent.DeviceMac) ||
                relayEvent.Context.ValueKind != JsonValueKind.Object) return null;
            JsonElement context = relayEvent.Context;
            double? temperature = GetNumber(context, "temperature", "temp");
            double? humidity = GetNumber(context, "humidity");
            double? illuminance = GetNumber(context, "lightLevel", "illuminance");
            int? battery = GetNumber(context, "battery") is double b ? (int)Math.Round(b) : null;
            string deviceType = GetText(context, "deviceType");
            HashSet<string> ignored = new(StringComparer.OrdinalIgnoreCase)
                { "deviceMac", "deviceId", "deviceType", "timeOfSample" };
            List<string> receivedValues = context.EnumerateObject()
                .Where(p => !ignored.Contains(p.Name))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p => $"{NormalizeWebhookKey(p.Name, deviceType)}: {FormatValue(p.Value)}").ToList();
            List<string> values = MergeDataValues(saved, receivedValues);
            string[] data = new string[8];
            for (int i = 0; i < Math.Min(7, values.Count); i++) data[i] = values[i];
            if (values.Count >= 8) data[7] = string.Join(" / ", values.Skip(7));
            ServerSensorItem sensor = new()
            {
                Id = $"SwitchBot|{relayEvent.DeviceMac}", Provider = "SwitchBot",
                DeviceId = relayEvent.DeviceMac, DeviceName = saved?.DeviceName ?? relayEvent.DeviceMac,
                DeviceType = string.IsNullOrWhiteSpace(deviceType)
                    ? saved?.DeviceType ?? "Webhook" : deviceType,
                Temperature = temperature ?? saved?.Temperature,
                Humidity = humidity ?? saved?.Humidity,
                Illuminance = illuminance ?? saved?.Illuminance,
                Battery = battery ?? saved?.Battery,
                OtherValue = data[0], OtherValue2 = data[1], OtherValue3 = data[2],
                OtherValue4 = data[3], OtherValue5 = data[4], OtherValue6 = data[5],
                OtherValue7 = data[6], OtherValue8 = data[7],
                AllValues = string.Join(" / ", values),
                UpdatedAt = relayEvent.ReceivedAt == default ? DateTime.UtcNow : relayEvent.ReceivedAt,
                Status = "Webhook"
            };
            if (saved != null)
            {
                sensor.CopyDataButtonAssignmentsFrom(saved);
                sensor.LogEnabled = saved.LogEnabled;
            }
            return sensor;
        }

        private static List<string> MergeDataValues(
            ServerSensorItem? saved, IEnumerable<string> receivedValues)
        {
            List<KeyValuePair<string, string>> merged = new();

            void Merge(string? storedValue)
            {
                if (string.IsNullOrWhiteSpace(storedValue)) return;
                foreach (string part in storedValue.Split('/',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    int separator = Math.Max(part.IndexOf(':'), part.IndexOf('：'));
                    if (separator <= 0) continue;
                    string label = SensorDataNormalizer.NormalizeKey(part[..separator].Trim());
                    string value = SensorDataNormalizer.NormalizeValue(part[(separator + 1)..].Trim());
                    if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
                        continue;
                    int index = merged.FindIndex(item => string.Equals(
                        item.Key, label, StringComparison.OrdinalIgnoreCase));
                    KeyValuePair<string, string> item = new(label, value);
                    if (index >= 0)
                        merged[index] = item;
                    else
                        merged.Add(item);
                }
            }

            if (saved != null)
            {
                Merge(saved.OtherValue); Merge(saved.OtherValue2);
                Merge(saved.OtherValue3); Merge(saved.OtherValue4);
                Merge(saved.OtherValue5); Merge(saved.OtherValue6);
                Merge(saved.OtherValue7); Merge(saved.OtherValue8);
            }
            foreach (string value in receivedValues) Merge(value);
            return merged.Select(item => $"{item.Key}: {item.Value}").ToList();
        }

        private static double? GetNumber(JsonElement value, params string[] names)
        {
            foreach (string name in names)
                if (value.TryGetProperty(name, out JsonElement property) &&
                    property.ValueKind == JsonValueKind.Number &&
                    property.TryGetDouble(out double number)) return number;
            return null;
        }
        private static string GetText(JsonElement value, string name) =>
            value.TryGetProperty(name, out JsonElement property) ? property.ToString() : "";
        private static string FormatValue(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.True => "true", JsonValueKind.False => "false",
            JsonValueKind.String => SensorDataNormalizer.NormalizeValue(value.GetString()),
            _ => SensorDataNormalizer.NormalizeValue(value.ToString())
        };

        private static string NormalizeWebhookKey(string propertyName, string deviceType)
        {
            // detectionStateは通常は人感だが、Water Detectorでは漏水状態として使われる。
            if (propertyName.Equals("detectionState", StringComparison.OrdinalIgnoreCase) &&
                deviceType.Contains("Water", StringComparison.OrdinalIgnoreCase))
                return "waterLeakStatus";
            return SensorDataNormalizer.NormalizeKey(propertyName);
        }

        // Webhookと通常のデバイス状態APIでは、同じ状態でも項目名が異なる。
        // 簡易オートメーションの保存済み条件と一致する共通ラベルへ揃える。
        internal static string NormalizeDataLabel(string propertyName) =>
            SensorDataNormalizer.NormalizeKey(propertyName);

        internal static string NormalizeDataValue(string value) =>
            SensorDataNormalizer.NormalizeValue(value);

        internal static string NormalizeStoredData(string value)
        {
            return SensorDataNormalizer.NormalizeStoredData(value);
        }

        private static string Translate(string value) => value.ToLowerInvariant() switch
        {
            "on" => "オン", "off" => "オフ", "lock" or "locked" => "施錠",
            "unlock" or "unlocked" => "解錠", "open" => "開",
            "close" or "closed" => "閉",
            "detected" or "motion detected" or "motion_detected" => "はい",
            "not detected" or "not_detected" or "notdetected" or
                "no motion detected" => "いいえ",
            "yes" or "true" => "はい", "no" or "false" => "いいえ",
            _ => value
        };
    }
}
