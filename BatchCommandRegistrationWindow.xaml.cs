using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace N_Remo
{
    public partial class BatchCommandRegistrationWindow : Window
    {
        private static string AllProviders =>
            LanguageManager.Get("すべての提供元");
        private static string AllDevices =>
            LanguageManager.Get("すべてのデバイス");

        private readonly List<BatchCommandSelection> allItems;
        private readonly ObservableCollection<BatchCommandSelection> visibleItems = new();
        private bool isUpdatingFilters;

        public IReadOnlyList<SwitchBotCommandItem> SelectedCommands { get; private set; } =
            Array.Empty<SwitchBotCommandItem>();

        public BatchCommandRegistrationWindow(IEnumerable<SwitchBotCommandItem> commands)
        {
            InitializeComponent();

            allItems = commands
                .GroupBy(command => command.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => new BatchCommandSelection(group.First(), UpdateSelectionCount))
                .OrderBy(item => ProviderDisplay(item.Command))
                .ThenBy(item => item.Command.DeviceName)
                .ThenBy(item => item.Command.Description)
                .ToList();

            CommandDataGrid.ItemsSource = visibleItems;
            LoadProviders();
            ApplyFilter();
            UpdateSelectionCount();
        }

        private void LoadProviders()
        {
            isUpdatingFilters = true;
            try
            {
                List<string> providers = allItems
                    .Select(item => ProviderDisplay(item.Command))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(value => value)
                    .ToList();
                providers.Insert(0, AllProviders);
                ProviderComboBox.ItemsSource = providers;
                ProviderComboBox.SelectedIndex = 0;
                LoadDevices();
            }
            finally
            {
                isUpdatingFilters = false;
            }
        }

        private void LoadDevices()
        {
            string provider = ProviderComboBox.SelectedItem as string ?? AllProviders;
            List<DeviceOption> devices = allItems
                .Where(item => provider == AllProviders ||
                    ProviderDisplay(item.Command) == provider)
                .Select(item => new DeviceOption
                {
                    Key = DeviceKey(item.Command),
                    Display = $"{item.Command.DeviceName} ({item.Command.DeviceType})"
                })
                .GroupBy(option => option.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(option => option.Display)
                .ToList();
            devices.Insert(0, new DeviceOption { Key = AllDevices, Display = AllDevices });
            DeviceComboBox.ItemsSource = devices;
            DeviceComboBox.SelectedIndex = 0;
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingFilters)
                return;

            if (ReferenceEquals(sender, ProviderComboBox))
            {
                isUpdatingFilters = true;
                try { LoadDevices(); }
                finally { isUpdatingFilters = false; }
            }
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string provider = ProviderComboBox.SelectedItem as string ?? AllProviders;
            string deviceKey = DeviceComboBox.SelectedValue as string ?? AllDevices;

            visibleItems.Clear();
            foreach (BatchCommandSelection item in allItems.Where(item =>
                (provider == AllProviders || ProviderDisplay(item.Command) == provider) &&
                (deviceKey == AllDevices || DeviceKey(item.Command) == deviceKey)))
            {
                visibleItems.Add(item);
            }
            UpdateSelectionCount();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (BatchCommandSelection item in visibleItems)
                item.IsSelected = true;
            UpdateSelectionCount();
        }

        private void ClearVisibleSelection_Click(object sender, RoutedEventArgs e)
        {
            foreach (BatchCommandSelection item in visibleItems)
                item.IsSelected = false;
            UpdateSelectionCount();
        }

        private void Register_Click(object sender, RoutedEventArgs e)
        {
            SelectedCommands = allItems
                .Where(item => item.IsSelected)
                .Select(item => item.Command)
                .ToList();
            if (SelectedCommands.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    LanguageManager.Get("登録するコマンドを選択してください。"),
                    LanguageManager.Get("デバイス別コマンド一括登録"));
                return;
            }
            DialogResult = true;
        }

        private void UpdateSelectionCount()
        {
            if (SelectionCountTextBlock == null)
                return;
            int selected = allItems.Count(item => item.IsSelected);
            SelectionCountTextBlock.Text = string.Format(
                LanguageManager.Get("表示中: {0}件 / 選択中: {1}件"),
                visibleItems.Count,
                selected);
        }

        private static string ProviderDisplay(SwitchBotCommandItem command) =>
            command.Provider switch
            {
                "NatureRemoCloud" => "Nature Remo",
                "SwitchBot" or "SwitchBotScene" => "SwitchBot",
                "Tuya" or "TuyaScene" => "Tuya",
                _ => command.Provider
            };

        private static string DeviceKey(SwitchBotCommandItem command) =>
            $"{ProviderDisplay(command)}|{command.DeviceId}";

        private sealed class DeviceOption
        {
            public string Key { get; init; } = "";
            public string Display { get; init; } = "";
        }
    }

    public sealed class BatchCommandSelection : INotifyPropertyChanged
    {
        private bool isSelected;
        private readonly Action selectionChanged;

        public SwitchBotCommandItem Command { get; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                    return;
                isSelected = value;
                OnPropertyChanged();
                selectionChanged();
            }
        }

        public BatchCommandSelection(
            SwitchBotCommandItem command,
            Action selectionChanged)
        {
            Command = command;
            this.selectionChanged = selectionChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
