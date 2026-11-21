using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace N_Remo
{
    public partial class DeviceCommandPanel : System.Windows.Controls.UserControl
    {
        private readonly ButtonSetting setting;
        private readonly ButtonDatabase database = new();
        private readonly Dictionary<string, SwitchBotCommandItem> tvCommands =
            new(StringComparer.OrdinalIgnoreCase);

        public DeviceCommandPanel(ButtonSetting setting)
        {
            InitializeComponent();
            this.setting = setting;
            ApplyTheme();

            AirConditionerPanel.Visibility = Visibility.Collapsed;
            PlugPanel.Visibility = Visibility.Collapsed;
            TvPanel.Visibility = Visibility.Collapsed;

            if (string.Equals(setting.PanelType, "Plug",
                StringComparison.OrdinalIgnoreCase))
            {
                PlugPanel.Visibility = Visibility.Visible;
                PlugTitle.Text = setting.NameContent;
                RefreshStatus();
            }
            else if (string.Equals(setting.PanelType, "TV",
                StringComparison.OrdinalIgnoreCase))
            {
                TvPanel.Visibility = Visibility.Visible;
                TvTitle.Text = setting.NameContent;
                InitializeTvControls();
            }
            else
            {
                AirConditionerPanel.Visibility = Visibility.Visible;
                AirConditionerTitle.Text = setting.NameContent;
                InitializeAirConditionerControls();
            }
        }

        public void RefreshStatus()
        {
            if (!string.Equals(setting.PanelType, "Plug",
                StringComparison.OrdinalIgnoreCase))
                return;

            ServerSensorItem? sensor = database.LoadServerSensors()
                .FirstOrDefault(item => string.Equals(
                    item.DeviceId,
                    setting.SwitchBotDeviceId,
                    StringComparison.OrdinalIgnoreCase));
            string state = sensor == null ? "不明" : FindPowerState(sensor);
            PlugStatusText.Text = $"{LanguageManager.Get("状態")}：" +
                LanguageManager.Get(state);
        }

        private void ApplyTheme()
        {
            bool dark = string.Equals(setting.PanelTheme, "Dark",
                StringComparison.OrdinalIgnoreCase);
            string fallbackBackground = dark ? "#FF202020" : "#FFE5E5E5";
            string fallbackForeground = dark ? "#FFFFFFFF" : "#FF000000";

            // 旧版のパネルは通常ボタン用の既定色を保持しているため、
            // ダークテーマの場合だけテーマ色へ読み替える。
            string background = setting.BackgroundColor;
            string foreground = setting.FontColor;
            if (string.IsNullOrWhiteSpace(background) ||
                (dark && string.Equals(background, "#FFE0E0E0",
                    StringComparison.OrdinalIgnoreCase)))
                background = fallbackBackground;
            if (string.IsNullOrWhiteSpace(foreground) ||
                (dark && string.Equals(foreground, "#FF000000",
                    StringComparison.OrdinalIgnoreCase)))
                foreground = fallbackForeground;

            PanelBorder.Background = CreateBrush(background, fallbackBackground);
            System.Windows.Media.Brush foregroundBrush =
                CreateBrush(foreground, fallbackForeground);
            Foreground = foregroundBrush;

            // ButtonとComboBoxはアプリ共通Style側で文字色を持つため、
            // パネルに指定された文字色を各操作部品へ明示的に反映する。
            foreach (System.Windows.Controls.Button button in
                FindVisualChildren<System.Windows.Controls.Button>(DesignSurface))
                button.Foreground = foregroundBrush;

            foreach (System.Windows.Controls.ComboBox comboBox in
                FindVisualChildren<System.Windows.Controls.ComboBox>(DesignSurface))
            {
                comboBox.Foreground = foregroundBrush;
                Style itemStyle = new(typeof(ComboBoxItem));
                itemStyle.Setters.Add(new Setter(
                    ForegroundProperty,
                    foregroundBrush));
                comboBox.ItemContainerStyle = itemStyle;
            }
            // 状態表示も補助色ではなく、ユーザーが指定した文字色へ統一する。
            PlugStatusText.Foreground = foregroundBrush;
        }

        private static System.Windows.Media.Brush CreateBrush(
            string colorCode,
            string fallback)
        {
            try
            {
                return new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString(colorCode));
            }
            catch
            {
                return new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString(fallback));
            }
        }

        private void InitializeTvControls()
        {
            TvBlueButton.Content = LanguageManager.Get("青");
            TvRedButton.Content = LanguageManager.Get("赤");
            TvGreenButton.Content = LanguageManager.Get("緑");
            TvYellowButton.Content = LanguageManager.Get("黄");

            tvCommands.Clear();
            foreach (SwitchBotCommandItem command in database.LoadSwitchBotCommands()
                .Where(command =>
                    string.Equals(command.Provider, setting.Provider,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(command.DeviceId, setting.SwitchBotDeviceId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                foreach (string action in TvActions)
                {
                    if (!tvCommands.ContainsKey(action) &&
                        IsTvCommandMatch(action, command))
                        tvCommands[action] = command;
                }
            }

            foreach (System.Windows.Controls.Button button in
                FindVisualChildren<System.Windows.Controls.Button>(TvPanel))
            {
                string action = button.Tag as string ?? "";
                button.IsEnabled = tvCommands.ContainsKey(action);
                if (action.StartsWith("Channel", StringComparison.OrdinalIgnoreCase) &&
                    action.Length > "Channel".Length)
                    button.ToolTip = button.IsEnabled ? null :
                        LanguageManager.Get("この操作には対応していません。");
            }
        }

        private static readonly string[] TvActions =
        {
            "Power", "Input", "Mute",
            "ChannelDown", "ChannelUp", "VolumeDown", "VolumeUp",
            "Up", "Down", "Left", "Right", "Ok",
            "Channel1", "Channel2", "Channel3", "Channel4",
            "Channel5", "Channel6", "Channel7", "Channel8",
            "Channel9", "Channel10", "Channel11", "Channel12",
            "Blue", "Red", "Green", "Yellow",
            "Back", "Home", "Menu"
        };

        private static bool IsTvCommandMatch(
            string action,
            SwitchBotCommandItem command)
        {
            string[] values =
            {
                NormalizeTvCommand(command.Command),
                NormalizeTvCommand(command.Description)
            };
            string[] aliases = action switch
            {
                "Power" => new[] { "power", "power-button", "turnOn", "電源", "電源ボタン" },
                "Input" => new[] { "input", "input-select", "inputSelect", "入力切替", "入力" },
                "Mute" => new[] { "mute", "消音", "ミュート" },
                "ChannelUp" => new[] { "channelAdd", "channelUp", "ch-up", "chUp", "チャンネル+", "チャンネル＋", "チャンネル上" },
                "ChannelDown" => new[] { "channelSub", "channelDown", "ch-down", "chDown", "チャンネル-", "チャンネル－", "チャンネル下" },
                "VolumeUp" => new[] { "volumeAdd", "volumeUp", "vol-up", "volUp", "音量+", "音量＋", "音量上" },
                "VolumeDown" => new[] { "volumeSub", "volumeDown", "vol-down", "volDown", "音量-", "音量－", "音量下" },
                "Up" => new[] { "up", "cursor-up", "cursorUp", "arrow-up", "arrowUp", "上" },
                "Down" => new[] { "down", "cursor-down", "cursorDown", "arrow-down", "arrowDown", "下" },
                "Left" => new[] { "left", "cursor-left", "cursorLeft", "arrow-left", "arrowLeft", "左" },
                "Right" => new[] { "right", "cursor-right", "cursorRight", "arrow-right", "arrowRight", "右" },
                "Ok" => new[] { "enter", "select", "決定", "ok" },
                "Back" => new[] { "back", "return", "戻る" },
                "Home" => new[] { "home", "ホーム" },
                "Menu" => new[] { "menu", "メニュー" },
                "Blue" => new[] { "blue", "青", "青ボタン" },
                "Red" => new[] { "red", "赤", "赤ボタン" },
                "Green" => new[] { "green", "緑", "緑ボタン" },
                "Yellow" => new[] { "yellow", "黄", "黄色", "黄ボタン" },
                _ when action.StartsWith("Channel", StringComparison.OrdinalIgnoreCase) &&
                    action.Length > "Channel".Length => ChannelAliases(
                        action["Channel".Length..]),
                _ => Array.Empty<string>()
            };
            return aliases.Select(NormalizeTvCommand).Any(alias =>
                values.Any(value => value == alias ||
                    (alias.Length >= 4 && value.Contains(alias,
                        StringComparison.OrdinalIgnoreCase))));
        }

        private static string[] ChannelAliases(string number) =>
            new[]
            {
                number,
                $"ch-{number}",
                $"ch{number}",
                $"channel-{number}",
                $"channel{number}",
                $"チャンネル{number}"
            };

        private static string NormalizeTvCommand(string value) =>
            new(value.Replace('＋', '+').Replace('－', '-')
                .Where(character => char.IsLetterOrDigit(character) ||
                character is '+' or '-').Select(char.ToLowerInvariant).ToArray());

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
            where T : DependencyObject
        {
            for (int index = 0;
                index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
                index++)
            {
                DependencyObject child =
                    System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
                if (child is T match)
                    yield return match;
                foreach (T descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        private static string FindPowerState(ServerSensorItem sensor)
        {
            foreach (string value in new[]
            {
                sensor.OtherValue, sensor.OtherValue2, sensor.OtherValue3,
                sensor.OtherValue4, sensor.OtherValue5, sensor.OtherValue6,
                sensor.OtherValue7, sensor.OtherValue8
            })
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                int separator = value.IndexOfAny(new[] { ':', '：' });
                string key = separator > 0 ? value[..separator].Trim() : "";
                if (key.Contains("power", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("電源", StringComparison.OrdinalIgnoreCase))
                {
                    string raw = separator >= 0 ? value[(separator + 1)..].Trim() : value;
                    return SensorDataNormalizer.DisplayValue(raw);
                }
            }
            return "不明";
        }

        private void InitializeAirConditionerControls()
        {
            AirPowerComboBox.ItemsSource = new[]
            {
                new SelectionOption("on", LanguageManager.Get("オン")),
                new SelectionOption("off", LanguageManager.Get("オフ"))
            };
            AirPowerComboBox.SelectedValue = string.Equals(
                setting.PanelAirPower,
                "off",
                StringComparison.OrdinalIgnoreCase)
                    ? "off"
                    : "on";

            HashSet<string> supported = ReadSupportedModes(setting.SwitchBotParameter);
            List<SelectionOption> modes = new();
            AddMode(modes, supported, "cool", "冷房");
            AddMode(modes, supported, "heat", "暖房");
            AddMode(modes, supported, "dry", "除湿");
            AddMode(modes, supported, "fan", "送風");
            AirModeComboBox.ItemsSource = modes;
            string savedMode = setting.PanelAirMode?.Trim().ToLowerInvariant() ?? "cool";
            AirModeComboBox.SelectedValue = modes.Any(option =>
                string.Equals(option.Value, savedMode,
                    StringComparison.OrdinalIgnoreCase))
                        ? savedMode
                        : modes.FirstOrDefault()?.Value;
            UpdateFanOptions();
            if (!string.IsNullOrWhiteSpace(setting.PanelAirFan) &&
                AirFanComboBox.ItemsSource is IEnumerable<SelectionOption> fanOptions &&
                fanOptions.Any(option => string.Equals(
                    option.Value,
                    setting.PanelAirFan,
                    StringComparison.OrdinalIgnoreCase)))
            {
                AirFanComboBox.SelectedValue = setting.PanelAirFan;
            }
            AirTemperatureSlider.Value = Math.Clamp(
                setting.PanelAirTemperature is >= 16 and <= 30
                    ? setting.PanelAirTemperature
                    : 26,
                16,
                30);
        }

        private static void AddMode(
            ICollection<SelectionOption> modes,
            ISet<string> supported,
            string value,
            string display)
        {
            if (supported.Count == 0 || supported.Contains(value))
                modes.Add(new SelectionOption(value, LanguageManager.Get(display)));
        }

        private static HashSet<string> ReadSupportedModes(string parameter)
        {
            HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(parameter) || parameter == "default")
                return result;
            try
            {
                using JsonDocument document = JsonDocument.Parse(parameter);
                if (!document.RootElement.TryGetProperty(
                        "supportedModes", out JsonElement modes) ||
                    modes.ValueKind != JsonValueKind.Array)
                    return result;
                foreach (JsonElement item in modes.EnumerateArray())
                {
                    string value = (item.GetString() ?? "").ToLowerInvariant() switch
                    {
                        "warm" => "heat",
                        "blow" => "fan",
                        string other => other
                    };
                    if (!string.IsNullOrWhiteSpace(value))
                        result.Add(value);
                }
            }
            catch (JsonException) { }
            return result;
        }

        private void AirPowerComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            bool isOn = string.Equals(AirPowerComboBox.SelectedValue as string,
                "on", StringComparison.OrdinalIgnoreCase);
            if (AirModeComboBox != null)
                AirModeComboBox.IsEnabled = isOn;
            if (AirTemperatureSlider != null)
                AirTemperatureSlider.IsEnabled = isOn;
            if (AirFanComboBox != null)
                AirFanComboBox.IsEnabled = isOn;
        }

        private void AirModeComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e) => UpdateFanOptions();

        private void UpdateFanOptions()
        {
            if (AirFanComboBox == null || AirModeComboBox == null)
                return;

            List<SelectionOption> options;
            if (string.Equals(setting.Provider, "NatureRemoCloud",
                StringComparison.OrdinalIgnoreCase))
            {
                string mode = AirModeComboBox.SelectedValue as string ?? "cool";
                List<string> values = ReadNatureFanValues(
                    setting.SwitchBotParameter,
                    mode);
                options = values.Select(value => new SelectionOption(
                    value,
                    NatureFanDisplay(value))).ToList();
                if (options.Count == 0)
                {
                    options.Add(new SelectionOption(
                        "auto",
                        LanguageManager.Get("自動")));
                }
            }
            else
            {
                options = new List<SelectionOption>
                {
                    new("1", LanguageManager.Get("自動")),
                    new("2", LanguageManager.Get("弱")),
                    new("3", LanguageManager.Get("中")),
                    new("4", LanguageManager.Get("強"))
                };
            }
            AirFanComboBox.ItemsSource = options;
            AirFanComboBox.SelectedIndex = 0;
        }

        private static List<string> ReadNatureFanValues(
            string parameter,
            string canonicalMode)
        {
            if (string.IsNullOrWhiteSpace(parameter) || parameter == "default")
                return new List<string>();
            try
            {
                using JsonDocument document = JsonDocument.Parse(parameter);
                if (!document.RootElement.TryGetProperty(
                        "volumesByMode", out JsonElement byMode) ||
                    byMode.ValueKind != JsonValueKind.Object)
                    return new List<string>();
                string natureMode = canonicalMode switch
                {
                    "heat" => "warm",
                    "fan" => "blow",
                    _ => canonicalMode
                };
                JsonProperty property = byMode.EnumerateObject()
                    .FirstOrDefault(item => item.Name.Equals(
                        natureMode,
                        StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(property.Name) ||
                    property.Value.ValueKind != JsonValueKind.Array)
                    return new List<string>();
                return property.Value.EnumerateArray()
                    .Select(item => item.GetString() ?? item.ToString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
            }
            catch (JsonException)
            {
                return new List<string>();
            }
        }

        private static string NatureFanDisplay(string value) =>
            value.ToLowerInvariant() switch
            {
                "auto" => LanguageManager.Get("自動"),
                "1" => LanguageManager.Get("弱") + " 1",
                "2" => LanguageManager.Get("弱") + " 2",
                "3" => LanguageManager.Get("中") + " 3",
                "4" => LanguageManager.Get("強") + " 4",
                "5" => LanguageManager.Get("強") + " 5",
                _ => value
            };

        private void AirTemperatureSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (AirTemperatureText != null)
                AirTemperatureText.Text = $"{Math.Round(e.NewValue):0}℃";
        }

        private async void AirSendButton_Click(object sender, RoutedEventArgs e)
        {
            await RunCommandAsync(AirSendButton, async () =>
            {
                string power = AirPowerComboBox.SelectedValue as string ?? "on";
                string mode = AirModeComboBox.SelectedValue as string ?? "cool";
                string fan = AirFanComboBox.SelectedValue as string ??
                    (string.Equals(setting.Provider, "NatureRemoCloud",
                        StringComparison.OrdinalIgnoreCase) ? "auto" : "1");
                int temperature = (int)Math.Round(AirTemperatureSlider.Value);
                await SendAirConditionerAsync(power, mode, temperature, fan);
                setting.PanelAirPower = power;
                setting.PanelAirMode = mode;
                setting.PanelAirTemperature = temperature;
                setting.PanelAirFan = fan;
                database.Save(setting);
            });
        }

        private async void PlugOnButton_Click(object sender, RoutedEventArgs e) =>
            await RunCommandAsync(PlugOnButton, () => SendPlugAsync(true));

        private async void PlugOffButton_Click(object sender, RoutedEventArgs e) =>
            await RunCommandAsync(PlugOffButton, () => SendPlugAsync(false));

        private async void TvCommandButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button)
                return;
            string action = button.Tag as string ?? "";
            if (!tvCommands.TryGetValue(action, out SwitchBotCommandItem? command))
                return;
            await RunCommandAsync(button, () => SendTvCommandAsync(command));
        }

        private static Task SendTvCommandAsync(SwitchBotCommandItem command) =>
            command.Provider switch
            {
                "NatureRemoCloud" => NatureRemoCloudApiClient.SendCommandAsync(
                    command.DeviceId, command.Command, command.CommandType,
                    command.Parameter),
                "Tuya" => TuyaApiClient.SendCommandAsync(
                    command.DeviceId, command.Command, command.Parameter,
                    command.CommandType),
                _ => SwitchBotApiClient.SendCommandAsync(
                    command.DeviceId, command.Command, command.Parameter,
                    command.CommandType)
            };

        private async Task RunCommandAsync(
            System.Windows.Controls.Button source,
            Func<Task> action)
        {
            source.IsEnabled = false;
            try
            {
                await action();
                await Task.Delay(500);
                RefreshStatus();
                if (Window.GetWindow(this) is MainWindow mainWindow)
                    mainWindow.DevicePanelCommandCompleted();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message,
                    LanguageManager.Get("コマンド送信エラー"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { source.IsEnabled = true; }
        }

        private Task SendAirConditionerAsync(
            string power,
            string mode,
            int temperature,
            string fan)
        {
            if (string.Equals(setting.Provider, "NatureRemoCloud",
                StringComparison.OrdinalIgnoreCase))
            {
                if (power == "off")
                {
                    return NatureRemoCloudApiClient.SendCommandAsync(
                        setting.SwitchBotDeviceId, "power-off", "aircon");
                }
                string natureMode = mode switch
                {
                    "heat" => "warm", "fan" => "blow", _ => mode
                };
                string parameter = JsonSerializer.Serialize(
                    new Dictionary<string, string>
                    {
                        ["operation_mode"] = natureMode,
                        ["temperature"] = temperature.ToString(),
                        ["air_volume"] = fan
                    });
                return NatureRemoCloudApiClient.SendCommandAsync(
                    setting.SwitchBotDeviceId,
                    "setAircon",
                    "aircon-settings",
                    parameter);
            }

            string modeCode = mode switch
            {
                "cool" => "2", "dry" => "3", "fan" => "4",
                "heat" => "5", _ => "2"
            };
            return SwitchBotApiClient.SendCommandAsync(
                setting.SwitchBotDeviceId,
                "setAll",
                $"{temperature},{modeCode},{fan},{power}",
                "command");
        }

        private Task SendPlugAsync(bool turnOn)
        {
            if (string.Equals(setting.Provider, "Tuya",
                StringComparison.OrdinalIgnoreCase))
            {
                List<SwitchBotCommandItem> commands = database.LoadSwitchBotCommands();
                SwitchBotCommandItem? command = commands.FirstOrDefault(item =>
                    string.Equals(item.Provider, "Tuya", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.DeviceId, setting.SwitchBotDeviceId,
                        StringComparison.OrdinalIgnoreCase) &&
                    item.CommandType.Equals("Boolean", StringComparison.OrdinalIgnoreCase) &&
                    bool.TryParse(item.Parameter, out bool value) && value == turnOn &&
                    (string.Equals(item.Command, setting.SwitchBotCommand,
                        StringComparison.OrdinalIgnoreCase) ||
                     item.Command.Contains("switch", StringComparison.OrdinalIgnoreCase) ||
                     item.Command.Contains("power", StringComparison.OrdinalIgnoreCase)));
                if (command == null)
                    throw new InvalidOperationException("Tuyaの電源コマンドが見つかりません。");
                return TuyaApiClient.SendCommandAsync(command.DeviceId,
                    command.Command, command.Parameter, command.CommandType);
            }

            return SwitchBotApiClient.SendCommandAsync(
                setting.SwitchBotDeviceId,
                turnOn ? "turnOn" : "turnOff",
                "default",
                "command");
        }

        private sealed class SelectionOption
        {
            public string Value { get; }
            public string Display { get; }
            public SelectionOption(string value, string display)
            {
                Value = value;
                Display = display;
            }
        }
    }
}
