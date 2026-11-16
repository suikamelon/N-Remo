using LiteDB;

namespace N_Remo
{
    public class ButtonSetting
    {
        [BsonId]
        public string Name { get; set; } = "";

        public bool IsVisible { get; set; }

        public string NameContent { get; set; } = "";

        public double Top { get; set; }

        public double Left { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public double FontSize { get; set; }

        public string FontColor { get; set; } = "";

        public string FontWeight { get; set; } = "";

        public string BackgroundColor { get; set; } = "";

        public string HorizontalAlignment { get; set; } = "";

        public string VerticalAlignment { get; set; } = "";

        public string Data1 { get; set; } = "";

        public string Data2 { get; set; } = "";

        public string Data3 { get; set; } = "";

        public string Provider { get; set; } = "NatureRemo";

        [BsonIgnore]
        public string ServerSensorProviderDisplay { get; set; } = "";

        [BsonIgnore]
        public string ProviderDisplay =>
            !string.IsNullOrWhiteSpace(ServerSensorProviderDisplay)
                ? ServerSensorProviderDisplay
                : Provider switch
                {
                    "SwitchBot" => "SwitchBot",
                    "SwitchBotScene" => "SwitchBotシーン",
                    "NatureRemoCloud" => "Nature Remo Cloud",
                    "Tuya" => "Tuya",
                    "TuyaScene" => "Tuyaシーン",
                    _ => string.IsNullOrWhiteSpace(Data1)
                        ? LanguageManager.Get("未設定")
                        : "Nature Remo"
                };

        public string SwitchBotDeviceId { get; set; } = "";

        public string SwitchBotDeviceName { get; set; } = "";

        public string SwitchBotCommand { get; set; } = "";

        public string SwitchBotParameter { get; set; } = "default";

        public string SwitchBotCommandType { get; set; } = "command";

        public bool IsMouseOverEffectEnabled { get; set; } = true;

        public int ZIndex { get; set; } = 0;
    }
}
