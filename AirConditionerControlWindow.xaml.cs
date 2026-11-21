using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace N_Remo
{
    public partial class AirConditionerControlWindow : Window
    {
        private readonly SwitchBotCommandItem command;

        public AirConditionerControlWindow(SwitchBotCommandItem command)
        {
            InitializeComponent();
            this.command = command;
            DeviceNameTextBlock.Text = $"{command.SourceDisplayLocalized} / " +
                $"{command.DeviceName}";

            PowerComboBox.ItemsSource = new[]
            {
                new SelectionOption("on", LanguageManager.Get("オン")),
                new SelectionOption("off", LanguageManager.Get("オフ"))
            };
            PowerComboBox.SelectedValue = "on";

            ModeComboBox.ItemsSource = CreateModeOptions(command);
            ModeComboBox.SelectedIndex = 0;
            TemperatureSlider.Value = 26;
        }

        public static bool? ShowForCommand(
            SwitchBotCommandItem command,
            Window? owner = null)
        {
            AirConditionerControlWindow window = new(command);
            if (owner != null)
                window.Owner = owner;
            return window.ShowDialog();
        }

        private static List<SelectionOption> CreateModeOptions(
            SwitchBotCommandItem command)
        {
            HashSet<string> supported = ReadSupportedModes(command.Parameter);
            List<SelectionOption> options = new();
            AddMode(options, supported, "cool", "冷房");
            AddMode(options, supported, "heat", "暖房");
            AddMode(options, supported, "dry", "除湿");
            AddMode(options, supported, "fan", "送風");
            if (options.Count == 0)
            {
                options.AddRange(new[]
                {
                    new SelectionOption("cool", LanguageManager.Get("冷房")),
                    new SelectionOption("heat", LanguageManager.Get("暖房")),
                    new SelectionOption("dry", LanguageManager.Get("除湿")),
                    new SelectionOption("fan", LanguageManager.Get("送風"))
                });
            }
            return options;
        }

        private static void AddMode(
            ICollection<SelectionOption> options,
            ISet<string> supported,
            string canonical,
            string display)
        {
            if (supported.Count == 0 || supported.Contains(canonical))
                options.Add(new SelectionOption(
                    canonical,
                    LanguageManager.Get(display)));
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
                foreach (JsonElement modeElement in modes.EnumerateArray())
                {
                    string mode = modeElement.GetString() ?? "";
                    string canonical = mode.ToLowerInvariant() switch
                    {
                        "warm" => "heat",
                        "blow" => "fan",
                        _ => mode.ToLowerInvariant()
                    };
                    if (!string.IsNullOrWhiteSpace(canonical))
                        result.Add(canonical);
                }
            }
            catch (JsonException)
            {
                // 古い設定値の場合は標準の4モードを表示する。
            }
            return result;
        }

        private void PowerComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            bool isOn = string.Equals(
                PowerComboBox.SelectedValue as string,
                "on",
                StringComparison.OrdinalIgnoreCase);
            if (ModeComboBox != null)
                ModeComboBox.IsEnabled = isOn;
            if (TemperatureSlider != null)
                TemperatureSlider.IsEnabled = isOn;
        }

        private void TemperatureSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (TemperatureTextBlock != null)
                TemperatureTextBlock.Text = $"{Math.Round(e.NewValue):0}℃";
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            string power = PowerComboBox.SelectedValue as string ?? "on";
            string mode = ModeComboBox.SelectedValue as string ?? "cool";
            int temperature = (int)Math.Round(TemperatureSlider.Value);
            SendButton.IsEnabled = false;
            try
            {
                await SendAsync(power, mode, temperature);
                System.Windows.MessageBox.Show(
                    LanguageManager.Get("コマンドを送信しました。"),
                    LanguageManager.Get("エアコン操作"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.Message,
                    LanguageManager.Get("コマンド送信エラー"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SendButton.IsEnabled = true;
            }
        }

        private Task SendAsync(string power, string mode, int temperature)
        {
            if (command.Provider.Equals(
                "NatureRemoCloud",
                StringComparison.OrdinalIgnoreCase))
            {
                if (power == "off")
                {
                    return NatureRemoCloudApiClient.SendCommandAsync(
                        command.DeviceId,
                        "power-off",
                        "aircon");
                }
                string natureMode = mode switch
                {
                    "heat" => "warm",
                    "fan" => "blow",
                    _ => mode
                };
                string parameter = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["operation_mode"] = natureMode,
                    ["temperature"] = temperature.ToString(),
                    ["air_volume"] = "auto"
                });
                return NatureRemoCloudApiClient.SendCommandAsync(
                    command.DeviceId,
                    "setAircon",
                    "aircon-settings",
                    parameter);
            }

            string modeCode = mode switch
            {
                "cool" => "2",
                "dry" => "3",
                "fan" => "4",
                "heat" => "5",
                _ => "2"
            };
            return SwitchBotApiClient.SendCommandAsync(
                command.DeviceId,
                "setAll",
                $"{temperature},{modeCode},1,{power}",
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
