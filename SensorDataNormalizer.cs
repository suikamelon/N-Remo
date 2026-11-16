using System;
using System.Linq;

namespace N_Remo
{
    // API・Webhook・DB・オートメーションでは英語の共通キー／値を使用し、
    // 画面へ表示する直前だけ現在の言語へ翻訳する。
    public static class SensorDataNormalizer
    {
        public static string NormalizeKey(string? key)
        {
            string value = (key ?? "").Trim();
            return value.ToLowerInvariant() switch
            {
                "movedetected" or "motiondetected" or "detectionstate" or "detected" or "人感" => "motionDetected",
                "battery" or "電池" => "battery",
                "temperature" or "temp" or "温度" => "temperature",
                "humidity" or "湿度" => "humidity",
                "lightlevel" or "illuminance" or "照度" => "illuminance",
                "brightness" or "明るさ" => "brightness",
                "lockstate" or "ロック" => "lockState",
                "doorstate" or "ドア" => "doorState",
                "openstate" or "開閉" => "openState",
                "power" or "powerstate" or "電源" => "power",
                "onlinestatus" or "online" or "オンライン" => "onlineStatus",
                "calibrate" or "校正" => "calibrate",
                _ => value
            };
        }

        public static string NormalizeValue(string? source)
        {
            string value = (source ?? "").Trim();
            return value.ToLowerInvariant() switch
            {
                "オン" or "on" => "on",
                "オフ" or "off" => "off",
                "はい" or "yes" or "true" or "detected" or "motion detected" or
                    "motion_detected" => "true",
                "いいえ" or "no" or "false" or "not detected" or "not_detected" or
                    "notdetected" or "no motion detected" => "false",
                "施錠" or "lock" or "locked" or "latchboltlocked" => "locked",
                "解錠" or "unlock" or "unlocked" or "latchboltunlocked" => "unlocked",
                "開" or "open" or "opened" => "open",
                "閉" or "close" or "closed" => "closed",
                "オンライン" or "online" => "online",
                "オフライン" or "offline" => "offline",
                "在宅" or "home" => "home",
                "不在" or "away" => "away",
                _ => value
            };
        }

        public static string NormalizeStoredData(string? source)
        {
            if (string.IsNullOrWhiteSpace(source)) return source ?? "";
            return string.Join(" / ", source.Split('/',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part =>
                {
                    int separator = Math.Max(part.IndexOf(':'), part.IndexOf('：'));
                    if (separator <= 0) return NormalizeValue(part);
                    return $"{NormalizeKey(part[..separator])}: {NormalizeValue(part[(separator + 1)..])}";
                }));
        }

        public static string DisplayKey(string? key)
        {
            string canonical = NormalizeKey(key);
            string resourceKey = "SensorKey." + canonical;
            string translated = LanguageManager.Get(resourceKey);
            return translated == resourceKey ? canonical : translated;
        }

        public static string DisplayValue(string? value, string? key = null)
        {
            string canonical = NormalizeValue(value);
            if (!string.IsNullOrWhiteSpace(key))
            {
                string contextualKey = "SensorValue." + NormalizeKey(key) + "." + canonical;
                string contextual = LanguageManager.Get(contextualKey);
                if (contextual != contextualKey) return contextual;
            }
            string resourceKey = "SensorValue." + canonical;
            string translated = LanguageManager.Get(resourceKey);
            return translated == resourceKey ? canonical : translated;
        }

        public static string DisplayStoredData(string? source)
        {
            if (string.IsNullOrWhiteSpace(source)) return source ?? "";
            return string.Join(" / ", NormalizeStoredData(source).Split('/',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part =>
                {
                    int separator = part.IndexOf(':');
                    if (separator <= 0) return DisplayValue(part);
                    string key = part[..separator];
                    return $"{DisplayKey(key)}: {DisplayValue(part[(separator + 1)..], key)}";
                }));
        }
    }
}
