using LiteDB;
using System.Collections.Generic;
using System.ComponentModel;
using System;
using System.Runtime.CompilerServices;

namespace N_Remo
{
    public sealed class SimpleAutomationRule : INotifyPropertyChanged
    {
        private List<AutomationOption> commandOptions = new();
        private bool isExpanded;
        private AutomationCondition? selectedCondition;
        private AutomationAction? selectedAction;
        private string actionCommandId = "";
        private string executionFrequency = "Daily";
        private bool hasExecutedOnce;
        public event PropertyChangedEventHandler? PropertyChanged;
        [BsonId(true)] public int Id { get; set; }
        public int SortOrder { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string Name { get; set; } = "新しいオートメーション";
        public string LogicalOperator { get; set; } = "すべて満たす";
        public int DelayMinutes { get; set; }
        // 互換性のためDB上の名前は維持するが、現在は条件の継続分数を表す。
        // 0なら条件成立を最初に確認した時点で実行する。
        public int RequiredConsecutiveCount { get; set; }
        public int CurrentConsecutiveCount { get; set; }
        public System.DateTime? ConditionMatchedSinceUtc { get; set; }
        public string ActionCommandId
        {
            get => actionCommandId;
            set
            {
                value ??= "";
                if (actionCommandId == value) return;
                actionCommandId = value;
                OnPropertyChanged();
            }
        }
        public bool IsNotificationEnabled { get; set; }
        public bool SkipWhileTemporaryProtection { get; set; }
        public bool IsLatched { get; set; }
        public string LastTriggeredDate { get; set; } = "";
        public string LastTimeRefreshDate { get; set; } = "";
        // 時刻・時間帯条件の実行頻度。旧データはDailyとして読み込まれる。
        public string ExecutionFrequency
        {
            get => string.IsNullOrWhiteSpace(executionFrequency) ? "Daily" : executionFrequency;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? "Daily" : value;
                if (executionFrequency == normalized) return;
                executionFrequency = normalized;
                if (normalized == "SelectedDays" && SelectedWeekdays == 0)
                    SelectedWeekdays = 0b0111110;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSelectedDaysFrequency));
                OnPropertyChanged(nameof(IsWeeklyFrequency));
                OnPropertyChanged(nameof(IsOnceFrequency));
            }
        }
        // DayOfWeekの値をビット位置として使用（Sunday=bit0）。
        public int SelectedWeekdays { get; set; } = 0b1111111;
        public int WeeklyDay { get; set; } = (int)DayOfWeek.Monday;
        public bool HasExecutedOnce
        {
            get => hasExecutedOnce;
            set
            {
                if (hasExecutedOnce == value) return;
                hasExecutedOnce = value;
                OnPropertyChanged();
            }
        }
        public List<AutomationCondition> Conditions { get; set; } = new();
        public List<AutomationAction> Actions { get; set; } = new();

        // 旧バージョンの1条件データ（移行用）。
        public string TriggerSensorId { get; set; } = "Time";
        public string TriggerDataLabel { get; set; } = "時刻";
        public string ConditionValue { get; set; } = "12:00";
        public string Comparison { get; set; } = "ならば";

        [BsonIgnore]
        public List<AutomationOption> CommandOptions
        {
            get => commandOptions;
            set { commandOptions = value; OnPropertyChanged(); }
        }
        [BsonIgnore] public List<AutomationOption> LogicalOperatorOptions => new()
        {
            new() { Value = "すべて満たす", Display = LanguageManager.Get("すべて満たす") },
            new() { Value = "いずれかを満たす", Display = LanguageManager.Get("いずれかを満たす") }
        };
        [BsonIgnore] public List<AutomationOption> ExecutionFrequencyOptions => new()
        {
            new() { Value = "Daily", Display = LanguageManager.Get("毎日") },
            new() { Value = "Once", Display = LanguageManager.Get("1回だけ") },
            new() { Value = "SelectedDays", Display = LanguageManager.Get("曜日指定") },
            new() { Value = "Weekdays", Display = LanguageManager.Get("平日のみ") },
            new() { Value = "WeekendsHolidays", Display = LanguageManager.Get("土日・祝日のみ") },
            new() { Value = "Weekly", Display = LanguageManager.Get("毎週") }
        };
        [BsonIgnore] public List<AutomationOption> WeekdayOptions => new()
        {
            new() { Value = "0", Display = LanguageManager.Get("日曜日") },
            new() { Value = "1", Display = LanguageManager.Get("月曜日") },
            new() { Value = "2", Display = LanguageManager.Get("火曜日") },
            new() { Value = "3", Display = LanguageManager.Get("水曜日") },
            new() { Value = "4", Display = LanguageManager.Get("木曜日") },
            new() { Value = "5", Display = LanguageManager.Get("金曜日") },
            new() { Value = "6", Display = LanguageManager.Get("土曜日") }
        };
        [BsonIgnore] public string WeeklyDayValue
        {
            get => Math.Clamp(WeeklyDay, 0, 6).ToString();
            set
            {
                if (!int.TryParse(value, out int day)) return;
                WeeklyDay = Math.Clamp(day, 0, 6);
                OnPropertyChanged();
            }
        }
        [BsonIgnore] public bool HasTimeCondition => Conditions.Exists(condition =>
            condition.IsEnabled && string.Equals(condition.TriggerSensorId, "Time",
                StringComparison.OrdinalIgnoreCase));
        [BsonIgnore] public bool IsSelectedDaysFrequency => ExecutionFrequency == "SelectedDays";
        [BsonIgnore] public bool IsWeeklyFrequency => ExecutionFrequency == "Weekly";
        [BsonIgnore] public bool IsOnceFrequency => ExecutionFrequency == "Once";
        [BsonIgnore] public bool SundaySelected { get => IsDaySelected(0); set => SetDaySelected(0, value); }
        [BsonIgnore] public bool MondaySelected { get => IsDaySelected(1); set => SetDaySelected(1, value); }
        [BsonIgnore] public bool TuesdaySelected { get => IsDaySelected(2); set => SetDaySelected(2, value); }
        [BsonIgnore] public bool WednesdaySelected { get => IsDaySelected(3); set => SetDaySelected(3, value); }
        [BsonIgnore] public bool ThursdaySelected { get => IsDaySelected(4); set => SetDaySelected(4, value); }
        [BsonIgnore] public bool FridaySelected { get => IsDaySelected(5); set => SetDaySelected(5, value); }
        [BsonIgnore] public bool SaturdaySelected { get => IsDaySelected(6); set => SetDaySelected(6, value); }
        [BsonIgnore]
        public bool IsExpanded
        {
            get => isExpanded;
            set { if (isExpanded == value) return; isExpanded = value; OnPropertyChanged(); }
        }
        [BsonIgnore]
        public AutomationCondition? SelectedCondition
        {
            get => selectedCondition;
            set { if (ReferenceEquals(selectedCondition, value)) return; selectedCondition = value; OnPropertyChanged(); }
        }
        [BsonIgnore]
        public AutomationAction? SelectedAction
        {
            get => selectedAction;
            set { if (ReferenceEquals(selectedAction, value)) return; selectedAction = value; OnPropertyChanged(); }
        }

        public void EnsureConditions()
        {
            if (RequiredConsecutiveCount < 0)
                RequiredConsecutiveCount = 0;
            if (Conditions.Count > 0) return;
            Conditions.Add(new AutomationCondition
            {
                TriggerSensorId = TriggerSensorId,
                TriggerDataLabel = TriggerDataLabel,
                ConditionValue = ConditionValue,
                Comparison = Comparison
            });
        }

        public void NotifyTimeConditionChanged() =>
            OnPropertyChanged(nameof(HasTimeCondition));

        public void ResetOnceExecution()
        {
            HasExecutedOnce = false;
            IsLatched = false;
            ConditionMatchedSinceUtc = null;
            CurrentConsecutiveCount = 0;
            foreach (AutomationCondition condition in Conditions)
            {
                condition.LastTriggeredDate = "";
                condition.LastScheduledRefreshDate = "";
            }
            OnPropertyChanged(nameof(HasExecutedOnce));
        }

        private bool IsDaySelected(int day) =>
            (SelectedWeekdays & (1 << day)) != 0;

        private void SetDaySelected(int day, bool selected)
        {
            int mask = 1 << day;
            int updated = selected ? SelectedWeekdays | mask : SelectedWeekdays & ~mask;
            if (updated == SelectedWeekdays) return;
            SelectedWeekdays = updated;
            OnPropertyChanged();
        }

        public void EnsureActions()
        {
            if (Actions.Count > 0) return;
            Actions.Add(new AutomationAction
            {
                SortOrder = 0,
                CommandId = ActionCommandId
            });
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class AutomationAction : INotifyPropertyChanged
    {
        private string commandId = "";
        private int protectionMinutes = 120;
        private List<AutomationOption> commandOptions = new();
        public event PropertyChangedEventHandler? PropertyChanged;
        public int SortOrder { get; set; }
        public int ProtectionMinutes
        {
            get => protectionMinutes;
            set
            {
                if (protectionMinutes == value) return;
                protectionMinutes = value;
                OnPropertyChanged();
            }
        }
        [BsonIgnore] public int DisplayOrder => SortOrder + 1;
        public string CommandId
        {
            get => commandId;
            set
            {
                value ??= "";
                if (commandId == value) return;
                commandId = value;
                OnPropertyChanged();
            }
        }
        [BsonIgnore]
        public List<AutomationOption> CommandOptions
        {
            get => commandOptions;
            set { commandOptions = value; OnPropertyChanged(); }
        }
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class AutomationProtectionState
    {
        [BsonId] public string Id { get; set; } = "Current";
        public System.DateTime? ProtectionUntilUtc { get; set; }
    }

    public sealed class AutomationCondition : INotifyPropertyChanged
    {
        private string triggerDataLabel = "時刻";
        private string comparison = "ならば";
        private string triggerSensorId = "Time";
        private string conditionValue = "12:00";
        private List<AutomationOption> triggerOptions = new();
        private List<AutomationOption> dataOptions = new();
        private List<AutomationOption> conditionValueOptions = new();
        public event PropertyChangedEventHandler? PropertyChanged;
        public int SortOrder { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string TriggerSensorId
        {
            get => triggerSensorId;
            set
            {
                value ??= "";
                if (triggerSensorId == value) return;
                triggerSensorId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPresenceMode));
            }
        }
        public string TriggerDataLabel
        {
            get => triggerDataLabel;
            set { if (triggerDataLabel == value) return; triggerDataLabel = value; OnPropertyChanged(); }
        }
        public string ConditionValue
        {
            get => conditionValue;
            set { conditionValue = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(ConditionValueDisplay)); }
        }
        [BsonIgnore]
        public string ConditionValueDisplay
        {
            get => TriggerSensorId == "Time" ? ConditionValue :
                SensorDataNormalizer.DisplayValue(ConditionValue, TriggerDataLabel);
            set => ConditionValue = TriggerSensorId == "Time" ? value : SensorDataNormalizer.NormalizeValue(value);
        }
        public string Comparison
        {
            get => comparison;
            set { if (comparison == value) return; comparison = value; OnPropertyChanged(); }
        }
        public string LastScheduledRefreshDate { get; set; } = "";
        public string LastTriggeredDate { get; set; } = "";
        [BsonIgnore]
        public List<AutomationOption> TriggerOptions
        {
            get => triggerOptions;
            set { triggerOptions = value; OnPropertyChanged(); }
        }
        [BsonIgnore]
        public List<AutomationOption> DataOptions
        {
            get => dataOptions;
            set { dataOptions = value; OnPropertyChanged(); }
        }
        [BsonIgnore]
        public bool IsPresenceMode => string.Equals(
            TriggerSensorId, "PresenceMode", StringComparison.OrdinalIgnoreCase);
        [BsonIgnore]
        public List<AutomationOption> ConditionValueOptions
        {
            get => conditionValueOptions;
            set { conditionValueOptions = value; OnPropertyChanged(); }
        }
        [BsonIgnore] public List<AutomationOption> ComparisonOptions => new()
        {
            new() { Value = "ならば", Display = LanguageManager.Get("ならば") },
            new() { Value = "以上", Display = LanguageManager.Get("以上") },
            new() { Value = "以下", Display = LanguageManager.Get("以下") }
        };
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class AutomationOption
    {
        public string Value { get; set; } = "";
        public string Display { get; set; } = "";
    }
}
