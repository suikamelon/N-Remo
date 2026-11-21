using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using MediaColor = System.Windows.Media.Color;
using WinForms = System.Windows.Forms;



namespace N_Remo
{
    /// <summary>
    /// Edit.xaml の相互作用ロジック
    /// </summary>
    public partial class Edit : System.Windows.Window
    {
        
        private readonly MainWindow mainWindow;
        private readonly Setting hostedSettings;
        private TabItem? generalSettingsTab;

        private string currentButtonName = "";

        [DllImport(
        "shell32.dll",
        CharSet = CharSet.Unicode)]
        private static extern int PickIconDlg(
        IntPtr hwndOwner,
        StringBuilder iconPath,
        uint iconPathLength,
        ref int iconIndex);

        private readonly ObservableCollection<ButtonSetting>
        buttonSettings = new();

        private readonly ObservableCollection<SwitchBotCommandItem>
            switchBotCommands = new();

        private readonly ObservableCollection<ServerSensorItem>
            serverSensors = new();

        private readonly ObservableCollection<SimpleAutomationRule>
            simpleAutomations = new();
        private bool isLoadingAutomations;
        private List<SensorLogSeriesInfo> sensorLogSeriesCatalog = new();
        private List<SensorLogEntry> allFilteredSensorLogEntries = new();
        private bool isLoadingSensorLogs;
        private int sensorLogFilterRequestVersion;
        private int sensorLogGridRequestVersion;
        private double sensorLogRangeStart;
        private double sensorLogRangeEnd = 1;
        private DateTime sensorLogOverallFrom;
        private DateTime sensorLogOverallTo;
        private double sensorLogDragAnchor;
        private double sensorLogDragOriginalStart;
        private double sensorLogDragOriginalEnd = 1;
        private bool sensorLogNewRangeWasDragged;
        private SensorLogRangeDragMode sensorLogRangeDragMode;

        private enum SensorLogRangeDragMode
        {
            None, NewRange, LeftHandle, RightHandle, Move
        }

        private enum SensorLogTimeStep
        {
            Minute1, Minute5, Minute10, Hour1, Day1, Day10, Month1
        }

        private sealed class SensorLogDeviceOption
        {
            public string SensorId { get; init; } = "";
            public string DeviceName { get; init; } = "";
        }

        private sealed class SensorLogMetricOption
        {
            public string Metric { get; init; } = "";
            public string MetricDisplay => Metric.StartsWith("Data:",
                    StringComparison.OrdinalIgnoreCase)
                ? SensorDataNormalizer.DisplayKey(Metric[5..])
                : LanguageManager.Get(Metric switch
                {
                    "Temperature" => "温度", "Humidity" => "湿度",
                    "Illuminance" => "照度", "Battery" => "電池",
                    _ => Metric
                });
        }

        private bool isUpdatingEditor;
        private bool isUpdatingSwitchBotAssignments;
        private bool isUpdatingSensorSettings;
        private bool isUpdatingSensorAssignments;
        private bool isUpdatingSensorLogFilters;
        // インポート直後に全タブを同期再構築すると、コマンド数や
        // オートメーション数に比例してUIが長時間停止する。
        // 未表示の重いタブは、最初に開いた時だけ遅延再読み込みする。
        private bool pendingImportedCommandsReload;
        private bool pendingImportedSensorsReload;
        private bool pendingImportedAutomationsReload;
        private const string GitHubRepositoryUrl =
            "https://github.com/suikamelon/N-Remo";
        private string latestReleaseUrl = GitHubRepositoryUrl + "/releases";


        public Edit(MainWindow mainWindow)
        {
            InitializeComponent();

            ApplyCenteredTextColumnStyles(DataGridButtons);
            ApplyCenteredTextColumnStyles(DataGridSwitchBotCommands);
            ApplyCenteredTextColumnStyles(DataGridSensorLogs);

            this.mainWindow = mainWindow;

            hostedSettings = new Setting(mainWindow)
            {
                Owner = mainWindow,
                ShowInTaskbar = false
            };
            List<TabItem> settingsTabs = hostedSettings.TakeTabsForEdit();
            for (int index = 0; index < settingsTabs.Count; index++)
                EditTabControl.Items.Insert(index, settingsTabs[index]);
            generalSettingsTab = settingsTabs.FirstOrDefault();

            // 全体設定、Nature Remo設定、SwitchBot設定の直後に
            // Webhook連携を配置し、その後にTuya設定を表示する。
            if (EditTabControl.Items.Contains(WebhookRelayTab))
            {
                EditTabControl.Items.Remove(WebhookRelayTab);
                int webhookTabIndex = Math.Min(3, EditTabControl.Items.Count);
                EditTabControl.Items.Insert(webhookTabIndex, WebhookRelayTab);
            }

            // Edit表示時
            Loaded += Edit_Loaded;

            DataGridButtons.ItemsSource =
                buttonSettings;

            DataGridSwitchBotCommands.ItemsSource =
                switchBotCommands;

            DataGridServerSensors.ItemsSource =
                serverSensors;

            DataGridSimpleAutomations.ItemsSource = simpleAutomations;
            AutomationRuleList.ItemsSource = simpleAutomations;

            mainWindow.ButtonStateChanged +=
                MainWindow_ButtonStateChanged;
            Closing += Edit_Closing;
        }

        private static void ApplyCenteredTextColumnStyles(
            System.Windows.Controls.DataGrid dataGrid)
        {
            Style displayStyle = new(typeof(TextBlock));
            displayStyle.Setters.Add(new Setter(
                FrameworkElement.VerticalAlignmentProperty,
                VerticalAlignment.Center));

            Style editingStyle = new(typeof(System.Windows.Controls.TextBox));
            editingStyle.Setters.Add(new Setter(
                System.Windows.Controls.Control.VerticalContentAlignmentProperty,
                VerticalAlignment.Center));
            editingStyle.Setters.Add(new Setter(
                FrameworkElement.VerticalAlignmentProperty,
                VerticalAlignment.Stretch));

            foreach (DataGridTextColumn column in
                dataGrid.Columns.OfType<DataGridTextColumn>())
            {
                column.ElementStyle = displayStyle;
                column.EditingElementStyle = editingStyle;
            }
        }

        private async void RefreshSwitchBotDevices_Click(
            object sender,
            RoutedEventArgs e)
        {
            List<string> errors = new();

            try
            {
                switchBotCommands.Clear();

                if (!string.IsNullOrWhiteSpace(
                    Properties.Settings.Default.NatureRemoCloudToken))
                {
                    try
                    {
                        foreach (SwitchBotCommandItem command in
                            await NatureRemoCloudApiClient
                                .GetSignalCommandsAsync())
                        {
                            switchBotCommands.Add(command);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Nature Remo: {ex.Message}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(
                        Properties.Settings.Default.SwitchBotToken) &&
                    !string.IsNullOrWhiteSpace(
                        Properties.Settings.Default.SwitchBotSecret))
                {
                    try
                    {
                        List<SwitchBotDevice> devices =
                            await SwitchBotApiClient.GetDevicesAsync();

                        foreach (SwitchBotCommandItem command in
                            SwitchBotApiClient.GetCommandCatalog(devices))
                        {
                            switchBotCommands.Add(command);
                        }

                        foreach (SwitchBotCommandItem scene in
                            await SwitchBotApiClient.GetScenesAsync())
                        {
                            switchBotCommands.Add(scene);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"SwitchBot: {ex.Message}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.TuyaAccessId) &&
                    !string.IsNullOrWhiteSpace(Properties.Settings.Default.TuyaAccessSecret))
                {
                    try
                    {
                        foreach (SwitchBotCommandItem command in await TuyaApiClient.GetCommandsAsync())
                            switchBotCommands.Add(command);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Tuya: {ex.Message}");
                    }
                    try
                    {
                        foreach (SwitchBotCommandItem scene in await TuyaApiClient.GetScenesAsync())
                            switchBotCommands.Add(scene);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Tuyaシーン: {ex.Message}");
                    }
                }

                RefreshSwitchBotAssignments();
                new ButtonDatabase()
                    .SaveSwitchBotCommands(switchBotCommands);
                mainWindow.RefreshDevicePanelDefinitions(switchBotCommands);
                RefreshAutomationOptions();

                if (switchBotCommands.Count == 0)
                {
                    System.Windows.MessageBox.Show(
                        errors.Count > 0
                            ? string.Join("\n\n", errors)
                            : "取得対象がありません。Nature Remo、SwitchBot、またはTuyaの認証情報を設定してください。",
                        "サーバーコマンド");
                }
                else if (errors.Count > 0)
                {
                    System.Windows.MessageBox.Show(
                        "一部のコマンドを取得できませんでした。\n\n" +
                        string.Join("\n\n", errors),
                        "サーバーコマンド",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.Message,
                    "サーバーコマンド取得エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void TestSwitchBotCommand_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DataGridSwitchBotCommands.SelectedItem
                is not SwitchBotCommandItem command)
            {
                System.Windows.MessageBox.Show(
                    "テストするコマンドを選択してください。");
                return;
            }

            if (string.Equals(
                command.CommandType,
                "aircon-control",
                StringComparison.OrdinalIgnoreCase))
            {
                AirConditionerControlWindow.ShowForCommand(command, this);
                return;
            }

            try
            {
                await SendServerCommandAsync(command);

                System.Windows.MessageBox.Show(
                    "コマンドを送信しました。",
                    "サーバーコマンド");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.Message,
                    "コマンド送信エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void BatchRegisterServerCommands_Click(
            object sender,
            RoutedEventArgs e)
        {
            List<SwitchBotCommandItem> availableCommands = new();
            List<string> errors = new();

            try
            {
                if (!string.IsNullOrWhiteSpace(
                    Properties.Settings.Default.NatureRemoCloudToken))
                {
                    try
                    {
                        availableCommands.AddRange(await NatureRemoCloudApiClient
                            .GetSignalCommandsAsync());
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Nature Remo: {ex.Message}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(
                        Properties.Settings.Default.SwitchBotToken) &&
                    !string.IsNullOrWhiteSpace(
                        Properties.Settings.Default.SwitchBotSecret))
                {
                    try
                    {
                        List<SwitchBotDevice> devices =
                            await SwitchBotApiClient.GetDevicesAsync();
                        availableCommands.AddRange(
                            SwitchBotApiClient.GetCommandCatalog(devices));
                        availableCommands.AddRange(
                            await SwitchBotApiClient.GetScenesAsync());
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"SwitchBot: {ex.Message}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(
                        Properties.Settings.Default.TuyaAccessId) &&
                    !string.IsNullOrWhiteSpace(
                        Properties.Settings.Default.TuyaAccessSecret))
                {
                    try
                    {
                        availableCommands.AddRange(
                            await TuyaApiClient.GetCommandsAsync());
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Tuya: {ex.Message}");
                    }
                    try
                    {
                        availableCommands.AddRange(
                            await TuyaApiClient.GetScenesAsync());
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Tuyaシーン: {ex.Message}");
                    }
                }

                availableCommands = availableCommands
                    .Where(command => !string.IsNullOrWhiteSpace(command.Id))
                    .GroupBy(command => command.Id,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();

                if (availableCommands.Count == 0)
                {
                    System.Windows.MessageBox.Show(
                        errors.Count > 0
                            ? string.Join("\n\n", errors)
                            : LanguageManager.Get(
                                "取得対象がありません。Nature Remo、SwitchBot、またはTuyaの認証情報を設定してください。"),
                        LanguageManager.Get("デバイス別コマンド一括登録"),
                        MessageBoxButton.OK,
                        errors.Count > 0
                            ? MessageBoxImage.Warning
                            : MessageBoxImage.Information);
                    return;
                }

                BatchCommandRegistrationWindow dialog = new(availableCommands)
                {
                    Owner = this
                };
                if (dialog.ShowDialog() != true)
                    return;

                HashSet<string> existingIds = switchBotCommands
                    .Select(command => command.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                int added = 0;
                int skipped = 0;
                foreach (SwitchBotCommandItem command in dialog.SelectedCommands)
                {
                    if (!existingIds.Add(command.Id))
                    {
                        skipped++;
                        continue;
                    }
                    switchBotCommands.Add(command);
                    added++;
                }

                RefreshSwitchBotAssignments();
                new ButtonDatabase().SaveSwitchBotCommands(switchBotCommands);
                mainWindow.RefreshDevicePanelDefinitions(switchBotCommands);
                RefreshAutomationOptions();
                DataGridSwitchBotCommands.Items.Refresh();

                string result = string.Format(
                    LanguageManager.Get("{0}件を登録しました。"), added);
                if (skipped > 0)
                {
                    result += "\n" + string.Format(
                        LanguageManager.Get("登録済みの{0}件は追加しませんでした。"),
                        skipped);
                }
                if (errors.Count > 0)
                {
                    result += "\n\n" +
                        LanguageManager.Get("一部の提供元から取得できませんでした。") +
                        "\n" + string.Join("\n\n", errors);
                }
                System.Windows.MessageBox.Show(
                    result,
                    LanguageManager.Get("デバイス別コマンド一括登録"),
                    MessageBoxButton.OK,
                    errors.Count > 0
                        ? MessageBoxImage.Warning
                        : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.Message,
                    LanguageManager.Get("デバイス別コマンド一括登録"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void AddSelectedDevicePanel_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DataGridSwitchBotCommands.SelectedItem
                is not SwitchBotCommandItem command)
            {
                System.Windows.MessageBox.Show(
                    LanguageManager.Get("パネルを追加するデバイスを選択してください。"),
                    LanguageManager.Get("デバイスパネル"));
                return;
            }

            try
            {
                ButtonSetting created = mainWindow.AddDevicePanel(command);
                ButtonSetting? existing = buttonSettings.FirstOrDefault(item =>
                    string.Equals(item.Name, created.Name,
                        StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    buttonSettings.Add(created);
                    existing = created;
                }
                DataGridButtons.SelectedItem = existing;
                DataGridButtons.ScrollIntoView(existing);
                System.Windows.MessageBox.Show(
                    string.Format(
                        LanguageManager.Get("{0}のデバイスパネルを追加しました。"),
                        command.DeviceName),
                    LanguageManager.Get("デバイスパネル"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.Message,
                    LanguageManager.Get("デバイスパネル"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static Task SendServerCommandAsync(
            SwitchBotCommandItem command)
        {
            return command.Provider switch
            {
                "NatureRemoCloud" =>
                    NatureRemoCloudApiClient.SendCommandAsync(
                        command.DeviceId,
                        command.Command,
                        command.CommandType,
                        command.Parameter),
                "SwitchBotScene" =>
                    SwitchBotApiClient.SendSceneAsync(
                        command.DeviceId),
                "Tuya" =>
                    TuyaApiClient.SendCommandAsync(
                        command.DeviceId,
                        command.Command,
                        command.Parameter,
                        command.CommandType),
                "TuyaScene" =>
                    TuyaApiClient.SendSceneAsync(command.DeviceId),
                _ => SwitchBotApiClient.SendCommandAsync(
                    command.DeviceId,
                    command.Command,
                    command.Parameter,
                    command.CommandType)
            };
        }

        private void ClearSwitchBotAssignment_Click(
            object sender,
            RoutedEventArgs e)
        {
            string buttonName = TextBoxNo.Text.Trim();
            if (string.IsNullOrWhiteSpace(buttonName))
            {
                System.Windows.MessageBox.Show(
                    "ボタン編集タブで解除するボタンを選択してください。");
                return;
            }

            ButtonDatabase database = new();
            ButtonSetting? setting = database.Load(buttonName);
            if (setting == null)
            {
                System.Windows.MessageBox.Show(
                    $"{buttonName}の設定が見つかりません。");
                return;
            }

            if (!string.Equals(
                    setting.Provider,
                    "SwitchBot",
                    StringComparison.OrdinalIgnoreCase))
            {
                System.Windows.MessageBox.Show(
                    $"{buttonName}にはSwitchBotコマンドが割り当てられていません。");
                return;
            }

            MessageBoxResult result =
                System.Windows.MessageBox.Show(
                    $"{buttonName}のSwitchBot割り当てを解除しますか？\n" +
                    "保存済みの赤外線データは削除されません。",
                    "SwitchBot割り当て解除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            setting.Provider = "None";
            setting.SwitchBotDeviceId = "";
            setting.SwitchBotDeviceName = "";
            setting.SwitchBotCommand = "";
            setting.SwitchBotParameter = "default";
            setting.SwitchBotCommandType = "command";
            database.Save(setting);
            mainWindow.NotifyButtonStateChanged(buttonName);
            RefreshSwitchBotAssignments();

            System.Windows.MessageBox.Show(
                $"{buttonName}のSwitchBot割り当てを解除しました。",
                "SwitchBot");
        }

        private void AssignSwitchBotCommand_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DataGridSwitchBotCommands.SelectedItem
                is not SwitchBotCommandItem command)
            {
                System.Windows.MessageBox.Show(
                    "割り当てるコマンドを選択してください。");
                return;
            }

            string buttonName = TextBoxNo.Text.Trim();
            if (string.IsNullOrWhiteSpace(buttonName))
            {
                System.Windows.MessageBox.Show(
                    "Nature Remoタブで割り当て先ボタンを選択してください。");
                return;
            }

            ButtonDatabase database = new();
            ButtonSetting? setting = database.Load(buttonName);
            if (setting == null)
            {
                System.Windows.MessageBox.Show(
                    $"{buttonName}の設定が見つかりません。");
                return;
            }

            setting.Provider = command.Provider;
            setting.SwitchBotDeviceId = command.DeviceId;
            setting.SwitchBotDeviceName = command.DeviceName;
            setting.SwitchBotCommand = command.Command;
            setting.SwitchBotParameter = command.Parameter;
            setting.SwitchBotCommandType = command.CommandType;
            database.Save(setting);
            mainWindow.NotifyButtonStateChanged(buttonName);
            RefreshSwitchBotAssignments();

            System.Windows.MessageBox.Show(
                $"{buttonName}に {command.DeviceName} / {command.Description} を割り当てました。",
                "SwitchBot");
        }


        // ========================================
        // ボタン追加
        // ========================================
        private void AddButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            ButtonSetting createdSetting =
                mainWindow.AddNewButton();

            // NotifyButtonStateChangedによって追加された
            // DataGrid内のデータを取得する
            ButtonSetting? gridSetting =
                buttonSettings.FirstOrDefault(x =>
                    string.Equals(
                        x.Name,
                        createdSetting.Name,
                        StringComparison.OrdinalIgnoreCase));

            // 通知で追加されていなかった場合だけ追加
            if (gridSetting == null)
            {
                gridSetting = createdSetting;
                buttonSettings.Add(gridSetting);
            }

            try
            {
                // TextChangedなどの編集処理を一時停止
                isUpdatingEditor = true;

                // TextBoxを書き換える前に新しい行を選択する
                DataGridButtons.SelectedItem =
                    gridSetting;

                TextBoxNo.Text =
                    gridSetting.Name;

                TextBoxButtonName.Text =
                    gridSetting.NameContent;

                TextBoxIR.Text =
                    gridSetting.Data1;

                CheckBoxVisible.IsChecked =
                    gridSetting.IsVisible;

                currentButtonName =
                    gridSetting.Name;
            }
            finally
            {
                isUpdatingEditor = false;
            }

            DataGridButtons.ScrollIntoView(
                gridSetting);

            DataGridButtons.Focus();
        }

        // ========================================
        // ボタン削除
        // ========================================
        private void DeleteButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DataGridButtons.SelectedItem
                is not ButtonSetting setting)
            {
                System.Windows.MessageBox.Show(
                    "削除するボタンを選択してください。");
                return;
            }

            MessageBoxResult result =
                System.Windows.MessageBox.Show(
                    $"{setting.Name}を削除しますか？\n" +
                    "赤外線情報も削除されます。",
                    "ボタンの削除",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            if (!mainWindow.DeleteButton(setting.Name))
            {
                System.Windows.MessageBox.Show(
                    "ボタンを削除できませんでした。");
                return;
            }

            buttonSettings.Remove(setting);

            TextBoxNo.Clear();
            TextBoxButtonName.Clear();
            TextBoxIR.Clear();
        }

        // ========================================
        // 高さを狭める
        // ========================================
        private void DecreaseHeightButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            System.Windows.Controls.Button? button =
                GetTargetButton();

            if (button == null)
                return;

            double amount =
                GetChangeAmount();

            double currentHeight =
                double.IsNaN(button.Height)
                    ? button.ActualHeight
                    : button.Height;

            button.Height =
                Math.Max(10, currentHeight - amount);

            mainWindow.SaveButtonSetting(button.Name);
        }

        // ========================================
        // 高さを大きくする
        // ========================================
        private void IncreaseHeightButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            System.Windows.Controls.Button? button =
                GetTargetButton();

            if (button == null)
                return;

            double amount =
                GetChangeAmount();

            double currentHeight =
                double.IsNaN(button.Height)
                    ? button.ActualHeight
                    : button.Height;

            button.Height =
                currentHeight + amount;

            mainWindow.SaveButtonSetting(button.Name);
        }

        // ========================================
        // 幅を狭める
        // ========================================
        private void DecreaseWidthButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            System.Windows.Controls.Button? button =
                GetTargetButton();

            if (button == null)
                return;

            double amount =
                GetChangeAmount();

            double currentWidth =
                double.IsNaN(button.Width)
                    ? button.ActualWidth
                    : button.Width;

            button.Width =
                Math.Max(10, currentWidth - amount);

            mainWindow.SaveButtonSetting(button.Name);
        }

        // ========================================
        // 幅を広げる
        // ========================================
        private void IncreaseWidthButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            System.Windows.Controls.Button? button =
                GetTargetButton();

            if (button == null)
                return;

            double amount =
                GetChangeAmount();

            double currentWidth =
                double.IsNaN(button.Width)
                    ? button.ActualWidth
                    : button.Width;

            button.Width =
                currentWidth + amount;

            mainWindow.SaveButtonSetting(button.Name);
        }

        // ========================================
        // 下へ移動
        // ========================================
        private void MoveDownButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            System.Windows.Controls.Button? button =
                GetTargetButton();

            if (button == null)
                return;

            double amount =
                GetChangeAmount();

            double top =
                Canvas.GetTop(button);

            if (double.IsNaN(top))
                top = 0;

            Canvas.SetTop(
                button,
                top + amount);

            mainWindow.SaveButtonSetting(button.Name);
        }

        // ========================================
        // 上へ移動
        // ========================================
        private void MoveUpButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            System.Windows.Controls.Button? button =
                GetTargetButton();

            if (button == null)
                return;

            double amount =
                GetChangeAmount();

            double top =
                Canvas.GetTop(button);

            if (double.IsNaN(top))
                top = 0;

            Canvas.SetTop(
                button,
                Math.Max(0, top - amount));

            mainWindow.SaveButtonSetting(button.Name);
        }

        // ========================================
        // 右へ移動
        // ========================================
        private void MoveRightButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            System.Windows.Controls.Button? button =
                GetTargetButton();

            if (button == null)
                return;

            double amount =
                GetChangeAmount();

            double left =
                Canvas.GetLeft(button);

            if (double.IsNaN(left))
                left = 0;

            Canvas.SetLeft(
                button,
                left + amount);

            mainWindow.SaveButtonSetting(button.Name);
        }

        // ========================================
        // 左へ移動
        // ========================================
        private void MoveLeftButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            System.Windows.Controls.Button? button =
                GetTargetButton();

            if (button == null)
                return;

            double amount =
                GetChangeAmount();

            double left =
                Canvas.GetLeft(button);

            if (double.IsNaN(left))
                left = 0;

            Canvas.SetLeft(
                button,
                Math.Max(0, left - amount));

            mainWindow.SaveButtonSetting(button.Name);
        }

        // ========================================
        // ボタンの位置やサイズを変更する量を取得
        // ========================================
        private double GetChangeAmount()
        {
            bool shiftPressed =
                (Keyboard.Modifiers &
                 ModifierKeys.Shift)
                == ModifierKeys.Shift;

            return shiftPressed ? 1 : 10;
        }

        // ========================================
        // Edit終了時にイベントを解除
        // ========================================
        protected override void OnClosed(EventArgs e)
        {
            mainWindow.ButtonStateChanged -=
                MainWindow_ButtonStateChanged;

            base.OnClosed(e);
        }

        // ========================================
        // DataGrid編集後にButtonへ反映
        // ========================================
        private void DataGridButtons_CellEditEnding(
            object sender,
            DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction !=
                DataGridEditAction.Commit)
            {
                return;
            }

            if (e.Row.Item is not
                ButtonSetting setting)
            {
                return;
            }

            // CellEditEnding時点ではBindingが
            // まだ確定していないので、次のUI処理で反映
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    try
                    {
                        mainWindow
                            .ApplyAndSaveButtonSetting(
                                setting);

                        // 選択中の編集欄も更新
                        if (DataGridButtons.SelectedItem
                            is ButtonSetting selected)
                        {
                            TextBoxNo.Text =
                                selected.Name;

                            TextBoxButtonName.Text =
                                selected.NameContent;

                            CheckBoxVisible.IsChecked =
                                selected.IsVisible;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show(
                            $"編集内容を反映できませんでした。\n" +
                            ex.Message,
                            "入力エラー",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        // 不正な値をMainWindowの状態へ戻す
                        RefreshDataGridRow(
                            setting.Name);
                    }
                }),
                DispatcherPriority.Background);
        }

        private void ButtonVisibilityGridCheckBox_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isUpdatingEditor ||
                sender is not System.Windows.Controls.CheckBox checkBox ||
                checkBox.DataContext is not ButtonSetting setting)
            {
                return;
            }

            // DataGridCheckBoxColumnは表示モードのまま直接クリックすると、
            // CellEditEndingが発生しない場合がある。チェック状態をここで
            // 明示的にモデルへ反映し、MainWindowとLiteDBを即時更新する。
            setting.IsVisible = checkBox.IsChecked == true;

            try
            {
                mainWindow.ApplyAndSaveButtonSetting(setting);

                if (ReferenceEquals(DataGridButtons.SelectedItem, setting))
                {
                    try
                    {
                        isUpdatingEditor = true;
                        CheckBoxVisible.IsChecked = setting.IsVisible;
                    }
                    finally
                    {
                        isUpdatingEditor = false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"編集内容を反映できませんでした。\n{ex.Message}",
                    "入力エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                RefreshDataGridRow(setting.Name);
            }
        }

        // ========================================
        // MainWindowの変更をDataGridへ反映
        // ========================================
        private void MainWindow_ButtonStateChanged(
    string buttonName)
        {
            ButtonSetting newSetting;

            try
            {
                newSetting =
                    mainWindow.GetButtonSetting(
                        buttonName);
            }
            catch
            {
                return;
            }

            ApplyServerSensorProviderDisplay(newSetting);

            ButtonSetting? existing =
                buttonSettings.FirstOrDefault(x =>
                    string.Equals(
                        x.Name,
                        buttonName,
                        StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                buttonSettings.Add(newSetting);
                RefreshSwitchBotAssignments();
                return;
            }

            int index =
                buttonSettings.IndexOf(existing);

            bool wasSelected =
                ReferenceEquals(
                    DataGridButtons.SelectedItem,
                    existing);

            try
            {
                isUpdatingEditor = true;

                buttonSettings[index] =
                    newSetting;

                if (wasSelected)
                {
                    DataGridButtons.SelectedItem =
                        newSetting;

                    TextBoxNo.Text =
                        newSetting.Name;

                    TextBoxButtonName.Text =
                        newSetting.NameContent;

                    CheckBoxVisible.IsChecked =
                        newSetting.IsVisible;

                    TextBoxIR.Text =
                        newSetting.Data1;
                }
            }
            finally
            {
                isUpdatingEditor = false;
            }

            RefreshSwitchBotAssignments();
        }

        // ========================================
        // MainWindowの変更をDataGridへ反映 2
        // ========================================
        private void RefreshDataGridRow(
            string buttonName)
        {
            ButtonSetting? oldSetting =
                buttonSettings.FirstOrDefault(
                    item => item.Name == buttonName);

            ButtonSetting newSetting =
                mainWindow.GetButtonSetting(
                    buttonName);

            if (oldSetting == null)
            {
                buttonSettings.Add(
                    newSetting);

                return;
            }

            int index =
                buttonSettings.IndexOf(
                    oldSetting);

            bool wasSelected =
                ReferenceEquals(
                    DataGridButtons.SelectedItem,
                    oldSetting);

            buttonSettings[index] =
                newSetting;

            if (wasSelected)
            {
                DataGridButtons.SelectedItem =
                    newSetting;
            }
        }

        // ========================================
        // 太字のON/OFFを切り替える
        // ========================================
        private void ButtonFontWeightToggle_Click(object sender,RoutedEventArgs e)
        {
            string buttonName =
                TextBoxNo.Text.Trim();

            if (string.IsNullOrWhiteSpace(buttonName))
            {
                System.Windows.MessageBox.Show(
                    "編集するボタンが選択されていません。",
                    "確認",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            System.Windows.Controls.Button? targetButton =
                mainWindow.GetButton(buttonName);

            if (targetButton == null)
            {
                System.Windows.MessageBox.Show(
                    $"「{buttonName}」というボタンはありません。",
                    "確認",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                bool currentlyBold =
                    targetButton.FontWeight
                        .ToOpenTypeWeight()
                    >= FontWeights.Bold
                        .ToOpenTypeWeight();

                if (currentlyBold)
                {
                    // 標準へ変更
                    targetButton.FontWeight =
                        FontWeights.Normal;

                    ButtonFontWeightToggle.Content =
                        LanguageManager.Get("太字：OFF");
                }
                else
                {
                    // 太字へ変更
                    targetButton.FontWeight =
                        FontWeights.Bold;

                    ButtonFontWeightToggle.Content =
                        LanguageManager.Get("太字：ON");
                }

                // 変更後の状態をLiteDBへ保存
                mainWindow.SaveButtonSetting(
                    buttonName);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"変更の保存に失敗しました。\n{ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ========================================
        // 太字のON/OFF状態を更新する
        // ========================================
        private void UpdateFontWeightToggle(string buttonName)
        {
            System.Windows.Controls.Button? button =
                mainWindow.GetButton(buttonName);

            if (button == null)
            {
                ButtonFontWeightToggle.Content =
                    LanguageManager.Get("太字：OFF");

                return;
            }

            bool isBold =
                button.FontWeight.ToOpenTypeWeight()
                >= FontWeights.Bold.ToOpenTypeWeight();

            ButtonFontWeightToggle.Content =
                isBold
                    ? LanguageManager.Get("太字：ON")
                    : LanguageManager.Get("太字：OFF");
        }

        public void RefreshLocalizedState()
        {
            DataGridButtons.Items.Refresh();
            DataGridSwitchBotCommands.Items.Refresh();
            DataGridServerSensors.Items.Refresh();
            DataGridSensorLogs.Items.Refresh();
            RefreshAutomationOptions();
            AutomationRuleList.Items.Refresh();
            TextBoxWebhookStatus.Text = mainWindow.GetWebhookRealtimeStatus();

            string buttonName = TextBoxNo.Text.Trim();
            if (!string.IsNullOrWhiteSpace(buttonName))
                UpdateFontWeightToggle(buttonName);
            else
                ButtonFontWeightToggle.Content =
                    LanguageManager.Get("太字：OFF");
        }

        // ========================================
        // ショートカット名として使用できない文字を除去
        // ========================================
        private static string MakeValidFileName(string value)
        {
            foreach (char invalidCharacter
                in System.IO.Path.GetInvalidFileNameChars())
            {
                value = value.Replace(
                    invalidCharacter,
                    '_');
            }

            value = value.Trim();

            return string.IsNullOrWhiteSpace(value)
                ? "N-Remo"
                : value;
        }

        // ========================================
        // Windowsショートカットを作成するメソッド
        // ========================================
        private static void CreateWindowsShortcut(
            string shortcutPath,
            string executablePath,
            string buttonName,
            string iconPath,
            int iconIndex)
        {
            Type? shellType =
                Type.GetTypeFromProgID(
                    "WScript.Shell");

            if (shellType == null)
            {
                throw new InvalidOperationException(
                    "Windows Script Hostを利用できません。");
            }

            object? shellObject =
                Activator.CreateInstance(shellType);

            if (shellObject == null)
            {
                throw new InvalidOperationException(
                    "WScript.Shellを作成できません。");
            }

            dynamic shell = shellObject;
            dynamic shortcut =
                shell.CreateShortcut(shortcutPath);

            try
            {
                shortcut.TargetPath =
                    executablePath;

                shortcut.Arguments =
                    $"--send \"{buttonName}\"";

                shortcut.WorkingDirectory =
                    System.IO.Path.GetDirectoryName(
                        executablePath);

                shortcut.Description =
                    $"{buttonName}のコマンドを送信";

                shortcut.IconLocation =
                    $"{iconPath},{iconIndex}";

                shortcut.Save();
            }
            finally
            {
                if (Marshal.IsComObject(shortcut))
                {
                    Marshal.FinalReleaseComObject(
                        shortcut);
                }

                if (Marshal.IsComObject(shellObject))
                {
                    Marshal.FinalReleaseComObject(
                        shellObject);
                }
            }
        }

        // ========================================
        // ショートカット作成
        // ========================================
        private void CreateShortcutButton_Click(object sender,RoutedEventArgs e)
        {
            string buttonName =
                TextBoxNo.Text.Trim();

            if (string.IsNullOrWhiteSpace(buttonName))
            {
                System.Windows.MessageBox.Show(
                    "TextBoxNOにボタン名がありません。",
                    "入力エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            // MainWindowに実在するボタンか確認
            System.Windows.Controls.Button? button =
                mainWindow.GetButton(buttonName);

            if (button == null)
            {
                System.Windows.MessageBox.Show(
                    $"「{buttonName}」というボタンはありません。",
                    "入力エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            // ローカル赤外線またはクラウドコマンドが保存されているか確認
            ButtonDatabase database =
                new ButtonDatabase();

            ButtonSetting? setting =
                database.Load(buttonName);

            if (!NatureRemoSender.HasAssignedCommand(setting))
            {
                System.Windows.MessageBox.Show(
                    $"{buttonName}に実行するコマンドが設定されていません。",
                    "設定エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            string executablePath =
                Process.GetCurrentProcess()
                    .MainModule?.FileName ?? "";

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                System.Windows.MessageBox.Show(
                    "実行ファイルのパスを取得できません。");

                return;
            }

            // 最初に表示するアイコンファイル
            StringBuilder iconPath =
                new StringBuilder(
                    executablePath,
                    1024);

            int iconIndex = 0;

            IntPtr ownerHandle =
                new WindowInteropHelper(this).Handle;

            int iconSelected =
                PickIconDlg(
                    ownerHandle,
                    iconPath,
                    (uint)iconPath.Capacity,
                    ref iconIndex);

            // アイコン選択をキャンセル
            if (iconSelected == 0)
            {
                return;
            }

            try
            {
                string displayName =
                    button.Content?.ToString()
                    ?? buttonName;

                string shortcutName =
                    MakeValidFileName(displayName);

                string desktopPath =
                    Environment.GetFolderPath(
                        Environment.SpecialFolder
                            .DesktopDirectory);

                string shortcutPath =
                    System.IO.Path.Combine(
                        desktopPath,
                        shortcutName + ".lnk");

                CreateWindowsShortcut(
                    shortcutPath,
                    executablePath,
                    buttonName,
                    iconPath.ToString(),
                    iconIndex);

                System.Windows.MessageBox.Show(
                    $"ショートカットを作成しました。\n" +
                    shortcutPath,
                    "作成完了",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"ショートカットの作成に失敗しました。\n" +
                    ex.Message,
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ========================================
        // マウスオーバー効果を切り替える
        // ========================================
        private void MouseOverEffectButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            string buttonName = TextBoxNo.Text.Trim();

            if (string.IsNullOrWhiteSpace(buttonName))
            {
                System.Windows.MessageBox.Show("ボタン名を入力してください。");
                return;
            }

            bool? result =
                mainWindow.ToggleMouseOverEffect(buttonName);

            if (result == null)
            {
                System.Windows.MessageBox.Show(
                    $"{buttonName}が見つかりません。");
                return;
            }

            MouseOverEffectButton.Content =
                result.Value
                    ? "マウスオーバー装飾：ON"
                    : "マウスオーバー装飾：OFF";
        }

        // ========================================
        // ボタンのStyleを切り替える
        // ========================================
        private void ToggleStyleButton_Click(object sender,RoutedEventArgs e)
        {
            string buttonName =
                TextBoxNo.Text.Trim();

            if (string.IsNullOrWhiteSpace(buttonName))
            {
                System.Windows.MessageBox.Show(
                    "ボタン名が入力されていません。");

                return;
            }

            bool? result =
                mainWindow.ToggleButtonStyle(
                    buttonName);

            if (result == null)
            {
                System.Windows.MessageBox.Show(
                    $"「{buttonName}」というボタンはありません。",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }
        }

        // ========================================
        // ボタンを前面に表示する
        // ========================================
        private void BringToFrontButton_Click(
        object sender,
        RoutedEventArgs e)
        {
            string buttonName = TextBoxNo.Text.Trim();

            if (string.IsNullOrWhiteSpace(buttonName))
            {
                System.Windows.MessageBox.Show(
                    "ボタン名を入力してください。");

                return;
            }

            bool result =
                mainWindow.BringButtonToFront(
                    buttonName);

            if (!result)
            {
                System.Windows.MessageBox.Show(
                    $"「{buttonName}」というボタンはありません。",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            mainWindow.Activate();
        }

        // ========================================
        // IRText プロパティを追加
        // ========================================
        public string IRText
        {
            get { return TextBoxIR.Text; }
        }

        public void SelectDataGridItem(string buttonName)
        {
            if (DataGridButtons.ItemsSource == null)
                return;

            foreach (var item in DataGridButtons.Items)
            {
                if (item is ButtonSetting setting)
                {
                    if (setting.Name == buttonName)
                    {
                        // 行を選択
                        DataGridButtons.SelectedItem = setting;

                        // 選択した行までスクロール
                        DataGridButtons.ScrollIntoView(setting);

                        break;
                    }
                }
            }
        }

        // ========================================
        // データグリッドの選択行が変わったときに、編集欄を更新
        // ========================================
        private void DataGridButtons_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (isUpdatingEditor)
                return;

            if (DataGridButtons.SelectedItem
                is not ButtonSetting setting)
            {
                return;
            }

            try
            {
                isUpdatingEditor = true;

                TextBoxNo.Text =
                    setting.Name;

                TextBoxButtonName.Text =
                    setting.NameContent;

                CheckBoxVisible.IsChecked =
                    setting.IsVisible;

                TextBoxIR.Text =
                    setting.Data1;

                currentButtonName =
                    setting.Name;

                UpdateFontWeightToggle(
                    setting.Name);
                UpdateDevicePanelThemeControls(setting);
            }
            finally
            {
                isUpdatingEditor = false;
            }
        }

        // ========================================
        // Edit表示時
        // ========================================
        private void Edit_Loaded(object sender,RoutedEventArgs e)
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            TextBlockAboutVersion.Text = string.Format(
                LanguageManager.Get("バージョン {0}"),
                version?.ToString(3) ?? "1.1.0");

            // MyData.db → DataGrid
            LoadDataGrid();

            LoadSwitchBotCommands();

            LoadServerSensors();
            LoadServerSensorSettings();
            LoadSimpleAutomations();
            LoadWebhookSettings();

            // DataGridにデータがある場合
            if (DataGridButtons.Items.Count > 0)
            {
                // 一番上の行を選択
                DataGridButtons.SelectedIndex = 0;

                // 選択行までスクロール
                DataGridButtons.ScrollIntoView(
                    DataGridButtons.SelectedItem);

                // DataGridにフォーカス
                DataGridButtons.Focus();
            }
        }

        private static void OpenExternalUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }

        private void OpenGitHub_Click(object sender, RoutedEventArgs e) =>
            OpenExternalUrl(GitHubRepositoryUrl);

        private void OpenGitHubReleases_Click(object sender, RoutedEventArgs e) =>
            OpenExternalUrl(GitHubRepositoryUrl + "/releases");

        private void OpenGitHubIssues_Click(object sender, RoutedEventArgs e) =>
            OpenExternalUrl(GitHubRepositoryUrl + "/issues");

        private void OpenLatestRelease_Click(object sender, RoutedEventArgs e) =>
            OpenExternalUrl(latestReleaseUrl);

        private async void CheckForUpdates_Click(
            object sender,
            RoutedEventArgs e)
        {
            ButtonCheckForUpdates.IsEnabled = false;
            ButtonOpenLatestRelease.IsEnabled = false;
            TextBlockUpdateStatus.Text = LanguageManager.Get(
                "最新バージョンを確認しています...");

            try
            {
                using CancellationTokenSource timeout =
                    new(TimeSpan.FromSeconds(8));
                using HttpRequestMessage request = new(
                    HttpMethod.Get,
                    "https://api.github.com/repos/suikamelon/N-Remo/releases/latest");
                request.Headers.UserAgent.ParseAdd("N-Remo/1.1");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");

                using HttpClient client = new();
                using HttpResponseMessage response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
                response.EnsureSuccessStatusCode();

                await using Stream stream =
                    await response.Content.ReadAsStreamAsync(timeout.Token);
                using JsonDocument json = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: timeout.Token);

                string tag = json.RootElement
                    .GetProperty("tag_name").GetString() ?? string.Empty;
                latestReleaseUrl = json.RootElement
                    .GetProperty("html_url").GetString() ??
                    GitHubRepositoryUrl + "/releases";

                string normalized = tag.Trim().TrimStart('v', 'V');
                int dashIndex = normalized.IndexOf('-');
                if (dashIndex >= 0)
                    normalized = normalized[..dashIndex];

                if (!Version.TryParse(normalized, out Version? latest))
                    throw new InvalidOperationException(
                        LanguageManager.Get("公開バージョンを解析できませんでした。"));

                Version current =
                    Assembly.GetExecutingAssembly().GetName().Version ??
                    new Version(1, 1, 0);

                ButtonOpenLatestRelease.IsEnabled = true;
                TextBlockUpdateStatus.Text = latest > current
                    ? string.Format(
                        LanguageManager.Get(
                            "新しいバージョン {0} を利用できます。"), tag)
                    : string.Format(
                        LanguageManager.Get(
                            "現在のバージョンは最新です。（最新：{0}）"), tag);
            }
            catch (OperationCanceledException)
            {
                TextBlockUpdateStatus.Text = LanguageManager.Get(
                    "更新確認がタイムアウトしました。しばらくしてから再度お試しください。");
            }
            catch (Exception ex)
            {
                TextBlockUpdateStatus.Text = string.Format(
                    LanguageManager.Get(
                        "更新情報を取得できませんでした。{0}"), ex.Message);
            }
            finally
            {
                ButtonCheckForUpdates.IsEnabled = true;
            }
        }

        private void LoadWebhookSettings()
        {
            CheckBoxWebhookRelayEnabled.IsChecked =
                Properties.Settings.Default.WebhookRelayEnabled;
            string provider = Properties.Settings.Default.WebhookRelayProvider;
            if (string.Equals(provider, "Cloudflare", StringComparison.OrdinalIgnoreCase))
                provider = "CloudflareRealtime";
            ComboBoxWebhookRelayProvider.SelectedItem =
                ComboBoxWebhookRelayProvider.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => string.Equals(item.Tag?.ToString(),
                        provider, StringComparison.OrdinalIgnoreCase));
            ComboBoxWebhookRelayProvider.SelectedIndex =
                ComboBoxWebhookRelayProvider.SelectedIndex < 0 ? 0 :
                    ComboBoxWebhookRelayProvider.SelectedIndex;
            TextBoxWebhookRelayBaseUrl.Text =
                Properties.Settings.Default.WebhookRelayBaseUrl;
            TextBoxWebhookReceiveUrl.Text =
                Properties.Settings.Default.WebhookReceiveUrl;
            PasswordBoxWebhookApiKey.Password =
                Properties.Settings.Default.WebhookRelayApiKey;
            TextBoxWebhookFirebaseEmail.Text =
                Properties.Settings.Default.WebhookFirebaseEmail;
            PasswordBoxWebhookFirebasePassword.Password =
                Properties.Settings.Default.WebhookFirebasePassword;
            TextBoxWebhookStatus.Text = mainWindow.GetWebhookRealtimeStatus();
        }

        private bool SaveWebhookSettings(bool showMessage)
        {
            string newBaseUrl = TextBoxWebhookRelayBaseUrl.Text.Trim().TrimEnd('/');
            string newProvider =
                (ComboBoxWebhookRelayProvider.SelectedItem as ComboBoxItem)
                    ?.Tag?.ToString() ?? "CloudflareRealtime";
            bool relayChanged = !string.Equals(
                Properties.Settings.Default.WebhookRelayBaseUrl,
                newBaseUrl, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Properties.Settings.Default.WebhookRelayProvider,
                    newProvider, StringComparison.OrdinalIgnoreCase);

            Properties.Settings.Default.WebhookRelayEnabled =
                CheckBoxWebhookRelayEnabled.IsChecked == true;
            Properties.Settings.Default.WebhookRelayProvider = newProvider;
            Properties.Settings.Default.WebhookRelayBaseUrl =
                newBaseUrl;
            Properties.Settings.Default.WebhookReceiveUrl =
                TextBoxWebhookReceiveUrl.Text.Trim();
            Properties.Settings.Default.WebhookRelayApiKey =
                PasswordBoxWebhookApiKey.Password.Trim();
            Properties.Settings.Default.WebhookFirebaseEmail =
                TextBoxWebhookFirebaseEmail.Text.Trim();
            Properties.Settings.Default.WebhookFirebasePassword =
                PasswordBoxWebhookFirebasePassword.Password;
            if (relayChanged)
                Properties.Settings.Default.WebhookLastEventId = 0;
            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();
            mainWindow.ReloadWebhookRelaySettings();
            if (showMessage)
                System.Windows.MessageBox.Show(
                    LanguageManager.Get("Webhook連携設定を保存しました。"),
                    LanguageManager.Get("Webhook連携"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        private void SaveWebhookSettings_Click(object sender, RoutedEventArgs e) =>
            SaveWebhookSettings(showMessage: true);

        private async void CheckWebhookRelay_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveWebhookSettings(showMessage: false)) return;
            try
            {
                TextBoxWebhookStatus.Text = LanguageManager.Get(
                    "リアルタイムサービスへ接続しています...");
                TextBoxWebhookStatus.Text = LanguageManager.Get("接続成功") + "\n" +
                    await WebhookRelayClient.CheckHealthAsync();
            }
            catch (Exception ex)
            {
                TextBoxWebhookStatus.Text = LanguageManager.Get("接続失敗") + "\n" + ex.Message;
            }
        }

        private async void RegisterSwitchBotWebhook_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveWebhookSettings(showMessage: false)) return;
            string url = TextBoxWebhookReceiveUrl.Text.Trim();
            if (System.Windows.MessageBox.Show(
                    LanguageManager.Get("入力したWebhook受信URLをSwitchBotへ登録します。\n" +
                        "既存の登録がある場合は、先に登録状態を確認してください。") +
                    "\n\n" + url,
                    LanguageManager.Get("Webhook登録"), MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                await SwitchBotApiClient.SetupWebhookAsync(url);
                TextBoxWebhookStatus.Text = LanguageManager.Get(
                    "SwitchBotへWebhookを登録しました。");
            }
            catch (Exception ex) { TextBoxWebhookStatus.Text =
                LanguageManager.Get("登録失敗") + "\n" + ex.Message; }
        }

        private async void QuerySwitchBotWebhook_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TextBoxWebhookStatus.Text = LanguageManager.Get("SwitchBotの登録状態") + "\n" +
                    await SwitchBotApiClient.QueryWebhookAsync();
            }
            catch (Exception ex) { TextBoxWebhookStatus.Text =
                LanguageManager.Get("確認失敗") + "\n" + ex.Message; }
        }

        private async void DeleteSwitchBotWebhook_Click(object sender, RoutedEventArgs e)
        {
            string url = TextBoxWebhookReceiveUrl.Text.Trim();
            if (System.Windows.MessageBox.Show(
                    LanguageManager.Get("次のWebhook登録を解除しますか？") + "\n" + url,
                    LanguageManager.Get("Webhook解除"), MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                await SwitchBotApiClient.DeleteWebhookAsync(url);
                TextBoxWebhookStatus.Text = LanguageManager.Get(
                    "SwitchBotのWebhook登録を解除しました。");
            }
            catch (Exception ex) { TextBoxWebhookStatus.Text =
                LanguageManager.Get("解除失敗") + "\n" + ex.Message; }
        }

        private void OpenWebhookTemplate_Click(object sender, RoutedEventArgs e)
        {
            string folderName = (sender as FrameworkElement)?.Tag?.ToString() ?? "Cloudflare";
            string path = System.IO.Path.Combine(AppContext.BaseDirectory,
                "WebhookTemplates", folderName);
            if (!Directory.Exists(path))
            {
                System.Windows.MessageBox.Show(
                    LanguageManager.Get("テンプレートフォルダーが見つかりません。") +
                    "\n" + path,
                    LanguageManager.Get("Webhook連携"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        // ========================================
        // MyData.db → DataGrid
        // ========================================
        private void LoadDataGrid()
        {
            buttonSettings.Clear();

            foreach (ButtonSetting setting
                in new ButtonDatabase()
                    .LoadAll()
                    .OrderBy(setting => setting.ZIndex))
            {
                buttonSettings.Add(setting);
            }
        }

        private void LoadSwitchBotCommands()
        {
            switchBotCommands.Clear();

            foreach (SwitchBotCommandItem command in
                new ButtonDatabase().LoadSwitchBotCommands())
            {
                switchBotCommands.Add(command);
            }

            RefreshSwitchBotAssignments();
        }

        private void LoadSimpleAutomations()
        {
            try
            {
                isLoadingAutomations = true;
                simpleAutomations.Clear();
                foreach (SimpleAutomationRule rule in new ButtonDatabase().LoadAutomations())
                {
                    PopulateAutomationOptions(rule);
                    simpleAutomations.Add(rule);
                }
                if (simpleAutomations.Count > 0)
                {
                    AutomationRuleList.SelectedIndex = 0;
                    foreach (SimpleAutomationRule rule in simpleAutomations)
                        rule.IsExpanded = false;
                }
            }
            finally { isLoadingAutomations = false; }
        }

        private void RefreshAutomationOptions()
        {
            try
            {
                isLoadingAutomations = true;
                foreach (SimpleAutomationRule rule in simpleAutomations)
                    PopulateAutomationOptions(rule);
            }
            finally { isLoadingAutomations = false; }
        }

        private void PopulateAutomationOptions(SimpleAutomationRule rule)
        {
            rule.EnsureConditions();
            rule.EnsureActions();
            foreach (AutomationCondition condition in rule.Conditions)
                PopulateAutomationConditionOptions(condition);

            List<AutomationOption> commandOptions = switchBotCommands
                .Where(command => !string.Equals(
                    command.CommandType,
                    "aircon-control",
                    StringComparison.OrdinalIgnoreCase))
                .Select(command => new AutomationOption
                {
                    Value = command.Id,
                    Display = $"{command.SourceDisplayLocalized} / {command.DeviceName} / {command.Description}"
                }).ToList();

            commandOptions.Insert(0, new AutomationOption
            {
                Value = "Protection:Cancel",
                Display = LanguageManager.Get("一時運転保護を解除")
            });
            commandOptions.Insert(0, new AutomationOption
            {
                Value = "Protection:Start",
                Display = LanguageManager.Get("一時運転保護を開始")
            });

            commandOptions.Insert(0, new AutomationOption
            {
                Value = "Mode:Away",
                Display = LanguageManager.Get("不在モードへ切り替え")
            });
            commandOptions.Insert(0, new AutomationOption
            {
                Value = "Mode:Home",
                Display = LanguageManager.Get("在宅モードへ切り替え")
            });

            // MainWindowのボタンを実行先として選べるようにする。
            // Button: プレフィックスでサーバーコマンドのIDと区別する。
            commandOptions.AddRange(new ButtonDatabase().LoadAll()
                .Where(button => !string.IsNullOrWhiteSpace(button.Name) &&
                    !string.Equals(button.SwitchBotCommandType,
                        "aircon-control",
                        StringComparison.OrdinalIgnoreCase) &&
                    (!string.IsNullOrWhiteSpace(button.Data1) ||
                     string.Equals(button.Provider, "SwitchBot",
                         StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(button.Provider, "SwitchBotScene",
                         StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(button.Provider, "NatureRemoCloud",
                         StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(button.Provider, "Tuya",
                         StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(button.Provider, "TuyaScene",
                         StringComparison.OrdinalIgnoreCase)))
                .OrderBy(button => button.Name, StringComparer.OrdinalIgnoreCase)
                .Select(button => new AutomationOption
                {
                    Value = $"Button:{button.Name}",
                    Display = $"{LanguageManager.Get("ボタン")} / {button.Name} / " +
                        (string.IsNullOrWhiteSpace(button.NameContent)
                            ? button.Name
                            : button.NameContent)
                }));
            rule.CommandOptions = commandOptions;
            foreach (AutomationAction action in rule.Actions)
                action.CommandOptions = commandOptions;
            rule.ActionCommandId = rule.Actions.FirstOrDefault()?.CommandId ?? "";
        }

        private void PopulateAutomationConditionOptions(AutomationCondition condition)
        {
            string selectedTriggerSensorId = condition.TriggerSensorId;
            string selectedDataLabel = condition.TriggerDataLabel;
            string selectedComparison = condition.Comparison;

            condition.TriggerOptions = new List<AutomationOption>
            {
                new() { Value = "Time", Display = LanguageManager.Get("時間") },
                new() { Value = "PresenceMode", Display = LanguageManager.Get("N-Remo / 在宅状態") }
            };
            condition.TriggerOptions.AddRange(serverSensors.Select(sensor => new AutomationOption
            {
                Value = sensor.Id,
                Display = $"{sensor.Provider} / {sensor.DeviceName} / {sensor.DeviceType}"
            }));

            condition.TriggerSensorId = selectedTriggerSensorId;
            UpdateAutomationDataOptions(condition);
            if (condition.DataOptions.Any(option =>
                    string.Equals(option.Value, selectedDataLabel,
                        StringComparison.OrdinalIgnoreCase)))
                condition.TriggerDataLabel = selectedDataLabel;
            condition.Comparison = selectedComparison;
        }

        private void UpdateAutomationDataOptions(AutomationCondition condition)
        {
            if (condition.TriggerSensorId == "Time")
            {
                condition.DataOptions = new List<AutomationOption>
                {
                    new() { Value = "時刻", Display = LanguageManager.Get("時刻") },
                    new() { Value = "時間帯", Display = LanguageManager.Get("時間帯") }
                };
                if (condition.TriggerDataLabel != "時刻" &&
                    condition.TriggerDataLabel != "時間帯")
                    condition.TriggerDataLabel = "時刻";
            }
            else if (condition.TriggerSensorId == "PresenceMode")
            {
                condition.DataOptions = new List<AutomationOption>
                {
                    new() { Value = "mode", Display = LanguageManager.Get("モード") }
                };
                condition.TriggerDataLabel = "mode";
                condition.ConditionValueOptions = new List<AutomationOption>
                {
                    new() { Value = "Home", Display = LanguageManager.Get("在宅") },
                    new() { Value = "Away", Display = LanguageManager.Get("不在") }
                };
                string normalizedValue = SensorDataNormalizer.NormalizeValue(
                    condition.ConditionValue);
                condition.ConditionValue = string.Equals(normalizedValue, "away",
                    StringComparison.OrdinalIgnoreCase) ? "Away" : "Home";
                condition.Comparison = "ならば";
            }
            else
            {
                condition.ConditionValueOptions = new();
                ServerSensorItem? sensor = serverSensors.FirstOrDefault(item => item.Id == condition.TriggerSensorId)
                    ?? new ButtonDatabase().LoadServerSensors().FirstOrDefault(item => item.Id == condition.TriggerSensorId);
                condition.DataOptions = sensor == null ? new() : GetAutomationDataOptions(sensor);
                if (condition.DataOptions.Count > 0 && !condition.DataOptions.Any(option => option.Value == condition.TriggerDataLabel))
                    condition.TriggerDataLabel = condition.DataOptions[0].Value;
            }
        }

        private static List<AutomationOption> GetAutomationDataOptions(ServerSensorItem sensor)
        {
            string[] values = { sensor.OtherValue, sensor.OtherValue2, sensor.OtherValue3,
                sensor.OtherValue4, sensor.OtherValue5, sensor.OtherValue6,
                sensor.OtherValue7, sensor.OtherValue8 };
            return values.SelectMany(value => (value ?? "").Split('/',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(part =>
                {
                    int separator = Math.Max(part.IndexOf(':'), part.IndexOf('：'));
                    return (separator > 0 ? part[..separator] : part).Trim();
                })
                .Where(label => !string.IsNullOrWhiteSpace(label)).Distinct()
                .Select(label => SensorDataNormalizer.NormalizeKey(label))
                .Select(label => new AutomationOption
                {
                    Value = label,
                    Display = SensorDataNormalizer.DisplayKey(label)
                }).ToList();
        }

        private void AddSimpleAutomation_Click(object sender, RoutedEventArgs e)
        {
            // 設定途中の初期値がセンサー値と一致しても実行されないよう、
            // 新規オートメーションは無効状態で作成する。
            SimpleAutomationRule rule = new()
            {
                IsEnabled = false,
                SortOrder = simpleAutomations.Count
            };
            rule.EnsureConditions();
            rule.EnsureActions();
            try
            {
                isLoadingAutomations = true;
                foreach (SimpleAutomationRule existingRule in simpleAutomations)
                    existingRule.IsExpanded = false;
                PopulateAutomationOptions(rule);
                if (rule.CommandOptions.Count > 0)
                {
                    rule.Actions[0].CommandId = rule.CommandOptions[0].Value;
                    rule.ActionCommandId = rule.Actions[0].CommandId;
                }
                new ButtonDatabase().SaveAutomation(rule);
                simpleAutomations.Add(rule);
                rule.IsExpanded = true;
                AutomationRuleList.SelectedItem = rule;
                AutomationRuleList.ScrollIntoView(rule);
            }
            finally { isLoadingAutomations = false; }
        }

        private void DeleteSimpleAutomation_Click(object sender, RoutedEventArgs e)
        {
            if (AutomationRuleList.SelectedItem is not SimpleAutomationRule rule) return;
            string deletePrompt = string.Format(
                LanguageManager.Get("オートメーション「{0}」を削除しますか？"),
                rule.Name);
            if (System.Windows.MessageBox.Show(
                    deletePrompt,
                    LanguageManager.Get("簡易オートメーション"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            new ButtonDatabase().DeleteAutomation(rule.Id);
            simpleAutomations.Remove(rule);
            SaveAutomationOrder();
        }

        private void MoveAutomationUp_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedAutomation(-1);
        }

        private void MoveAutomationDown_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedAutomation(1);
        }

        private void MoveSelectedAutomation(int offset)
        {
            if (AutomationRuleList.SelectedItem is not SimpleAutomationRule selected)
                return;
            int currentIndex = simpleAutomations.IndexOf(selected);
            int targetIndex = currentIndex + offset;
            if (currentIndex < 0 || targetIndex < 0 || targetIndex >= simpleAutomations.Count)
                return;

            simpleAutomations.Move(currentIndex, targetIndex);
            SaveAutomationOrder();
            AutomationRuleList.SelectedItem = selected;
            AutomationRuleList.ScrollIntoView(selected);
        }

        private void SaveAutomationOrder()
        {
            for (int index = 0; index < simpleAutomations.Count; index++)
                simpleAutomations[index].SortOrder = index;
            // 並べ替えでは、実行中の連続回数やラッチ状態を変更しない。
            new ButtonDatabase().SaveAutomationOrder(
                simpleAutomations.Select(rule => rule.Id));
        }

        private void AutomationTrigger_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingAutomations || sender is not System.Windows.Controls.ComboBox comboBox ||
                comboBox.DataContext is not AutomationCondition condition) return;
            if (comboBox.SelectedValue is not string selectedTrigger ||
                string.IsNullOrWhiteSpace(selectedTrigger)) return;
            try
            {
                isLoadingAutomations = true;
                condition.TriggerSensorId = selectedTrigger;
                UpdateAutomationDataOptions(condition);
                if (selectedTrigger == "Time")
                    condition.Comparison = "ならば";
            }
            finally { isLoadingAutomations = false; }
            SimpleAutomationRule? owner = simpleAutomations.FirstOrDefault(item =>
                item.Conditions.Contains(condition));
            owner?.NotifyTimeConditionChanged();
            SaveAutomationForCondition(condition);
        }

        private void SimpleAutomation_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataGridSimpleAutomations.SelectedItem is SimpleAutomationRule rule)
            {
                rule.EnsureConditions();
                DataGridAutomationConditions.ItemsSource = rule.Conditions;
            }
            else DataGridAutomationConditions.ItemsSource = null;
        }

        private void AddAutomationCondition_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not SimpleAutomationRule rule) return;
            AutomationCondition condition = new() { SortOrder = rule.Conditions.Count };
            PopulateAutomationConditionOptions(condition);
            rule.Conditions.Add(condition);
            rule.SelectedCondition = condition;
            rule.NotifyTimeConditionChanged();
            AutomationRuleList.Items.Refresh();
            SaveAutomation(rule);
        }

        private void DeleteAutomationCondition_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not SimpleAutomationRule rule ||
                rule.SelectedCondition is not AutomationCondition condition ||
                rule.Conditions.Count <= 1) return;
            rule.Conditions.Remove(condition);
            for (int index = 0; index < rule.Conditions.Count; index++)
                rule.Conditions[index].SortOrder = index;
            rule.SelectedCondition = rule.Conditions.FirstOrDefault();
            rule.NotifyTimeConditionChanged();
            AutomationRuleList.Items.Refresh();
            SaveAutomation(rule);
        }

        private void AddAutomationAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not SimpleAutomationRule rule) return;
            AutomationAction action = new() { SortOrder = rule.Actions.Count };
            action.CommandOptions = rule.CommandOptions;
            if (rule.CommandOptions.Count > 0)
                action.CommandId = rule.CommandOptions[0].Value;
            rule.Actions.Add(action);
            rule.SelectedAction = action;
            AutomationRuleList.Items.Refresh();
            SaveAutomation(rule);
        }

        private void DeleteAutomationAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not SimpleAutomationRule rule ||
                rule.SelectedAction is not AutomationAction action ||
                rule.Actions.Count <= 1) return;
            rule.Actions.Remove(action);
            for (int index = 0; index < rule.Actions.Count; index++)
                rule.Actions[index].SortOrder = index;
            rule.SelectedAction = rule.Actions.FirstOrDefault();
            rule.ActionCommandId = rule.Actions.FirstOrDefault()?.CommandId ?? "";
            AutomationRuleList.Items.Refresh();
            SaveAutomation(rule);
        }

        private void AutomationActionValue_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingAutomations || sender is not System.Windows.Controls.ComboBox comboBox ||
                comboBox.DataContext is not AutomationAction action ||
                comboBox.SelectedValue is not string commandId) return;
            action.CommandId = commandId;
            Dispatcher.BeginInvoke(() =>
            {
                SimpleAutomationRule? rule = simpleAutomations.FirstOrDefault(item =>
                    item.Actions.Contains(action));
                if (rule != null) SaveAutomation(rule);
            }, DispatcherPriority.Background);
        }

        private void AutomationActionTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (isLoadingAutomations || sender is not FrameworkElement element ||
                element.DataContext is not AutomationAction action) return;
            action.ProtectionMinutes = Math.Clamp(action.ProtectionMinutes, 1, 525600);
            SimpleAutomationRule? rule = simpleAutomations.FirstOrDefault(item =>
                item.Actions.Contains(action));
            if (rule != null) SaveAutomation(rule);
        }

        private void AutomationConditionValue_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingAutomations ||
                sender is not System.Windows.Controls.ComboBox comboBox ||
                comboBox.DataContext is not AutomationCondition condition)
                return;

            // SelectionChangedの時点ではTwoWay Bindingの書き戻しが
            // 完了していない場合があるため、比較条件は明示的に反映する。
            if (string.Equals(comboBox.Tag as string, "Comparison",
                    StringComparison.Ordinal) &&
                comboBox.SelectedValue is string comparison &&
                !string.IsNullOrWhiteSpace(comparison))
            {
                condition.Comparison = comparison;
            }
            else if (string.Equals(comboBox.Tag as string, "Data",
                         StringComparison.Ordinal) &&
                     condition.TriggerSensorId == "Time" &&
                     comboBox.SelectedValue is string timeDataLabel)
            {
                condition.TriggerDataLabel = timeDataLabel;
                condition.Comparison = "ならば";
                if (timeDataLabel == "時間帯" &&
                    !condition.ConditionValue.Contains('～') &&
                    !condition.ConditionValue.Contains('-') &&
                    !condition.ConditionValue.Contains('~') &&
                    !condition.ConditionValue.Contains('〜'))
                    condition.ConditionValue = "17:00～07:00";
                else if (timeDataLabel == "時刻" &&
                         !TimeSpan.TryParse(condition.ConditionValue, out _))
                    condition.ConditionValue = "12:00";
            }
            else if (string.Equals(comboBox.Tag as string, "ConditionValue",
                         StringComparison.Ordinal) &&
                     condition.TriggerSensorId == "PresenceMode" &&
                     comboBox.SelectedValue is string presenceValue)
            {
                condition.ConditionValue = presenceValue;
                condition.Comparison = "ならば";
            }

            Dispatcher.BeginInvoke(
                () => SaveAutomationForCondition(condition),
                DispatcherPriority.Background);
        }

        private void AutomationConditionValueTextBox_LostFocus(
            object sender, RoutedEventArgs e)
        {
            if (isLoadingAutomations || sender is not System.Windows.Controls.TextBox textBox ||
                textBox.DataContext is not AutomationCondition condition)
                return;

            string input = textBox.Text.Trim();
            if (!TryValidateAutomationConditionValue(condition, input,
                    out string validationMessage))
            {
                System.Windows.MessageBox.Show(
                    validationMessage,
                    LanguageManager.Get("入力値の警告"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
                textBox.Focus();
                textBox.SelectAll();
                return;
            }

            textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            SaveAutomationForCondition(condition);
        }

        private bool TryValidateAutomationConditionValue(
            AutomationCondition condition, string input, out string message)
        {
            message = "";
            if (string.IsNullOrWhiteSpace(input))
            {
                message = LanguageManager.Get("データ値を入力してください。");
                return false;
            }

            if (condition.TriggerSensorId == "Time")
            {
                bool valid = condition.TriggerDataLabel == "時間帯"
                    ? TryParseTimeRangeInput(input)
                    : TryParseTimeInput(input);
                if (!valid)
                {
                    message = condition.TriggerDataLabel == "時間帯"
                        ? LanguageManager.Get("時間帯は「17:00～07:00」の形式で入力してください。時は0～23、分は0～59です。")
                        : LanguageManager.Get("時刻は「21:26」の形式で入力してください。時は0～23、分は0～59です。");
                }
                return valid;
            }

            string? currentValue = GetCurrentSensorValue(condition);
            if (string.IsNullOrWhiteSpace(currentValue)) return true;
            string canonicalCurrent = SensorDataNormalizer.NormalizeValue(currentValue);
            string[][] enumGroups =
            {
                new[] { "true", "false" }, new[] { "on", "off" },
                new[] { "locked", "unlocked" }, new[] { "open", "closed" },
                new[] { "online", "offline" }
            };
            string[]? allowedCanonical = enumGroups.FirstOrDefault(group =>
                group.Contains(canonicalCurrent, StringComparer.OrdinalIgnoreCase));
            if (allowedCanonical != null)
            {
                string[] allowedDisplay = allowedCanonical
                    .Select(value => SensorDataNormalizer.DisplayValue(
                        value, condition.TriggerDataLabel)).ToArray();
                bool valid = allowedDisplay.Any(value =>
                    string.Equals(value, input, StringComparison.CurrentCulture));
                if (!valid)
                    message = string.Format(
                        LanguageManager.Get("データ値は「{0}」から選んで入力してください。"),
                        string.Join(" / ", allowedDisplay));
                return valid;
            }

            if (double.TryParse(canonicalCurrent, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _))
            {
                bool valid = double.TryParse(input, NumberStyles.Float,
                    CultureInfo.CurrentCulture, out _) ||
                    double.TryParse(input, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out _);
                if (!valid)
                    message = LanguageManager.Get("このデータ値には数値を入力してください。");
                return valid;
            }
            return true;
        }

        private string? GetCurrentSensorValue(AutomationCondition condition)
        {
            ServerSensorItem? sensor = serverSensors.FirstOrDefault(item =>
                item.Id == condition.TriggerSensorId) ??
                new ButtonDatabase().LoadServerSensors().FirstOrDefault(item =>
                    item.Id == condition.TriggerSensorId);
            if (sensor == null) return null;
            string key = SensorDataNormalizer.NormalizeKey(condition.TriggerDataLabel);
            if (key == "temperature") return sensor.Temperature?.ToString(CultureInfo.InvariantCulture);
            if (key == "humidity") return sensor.Humidity?.ToString(CultureInfo.InvariantCulture);
            if (key == "illuminance") return sensor.Illuminance?.ToString(CultureInfo.InvariantCulture);
            if (key == "battery") return sensor.Battery?.ToString(CultureInfo.InvariantCulture);

            string[] sources = { sensor.OtherValue, sensor.OtherValue2,
                sensor.OtherValue3, sensor.OtherValue4, sensor.OtherValue5,
                sensor.OtherValue6, sensor.OtherValue7, sensor.OtherValue8,
                sensor.AllValues };
            foreach (string part in sources.SelectMany(source => (source ?? "").Split('/',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            {
                int separator = Math.Max(part.IndexOf(':'), part.IndexOf('：'));
                if (separator > 0 && string.Equals(
                        SensorDataNormalizer.NormalizeKey(part[..separator]), key,
                        StringComparison.OrdinalIgnoreCase))
                    return part[(separator + 1)..].Trim();
            }
            return null;
        }

        private static bool TryParseTimeInput(string value) =>
            TimeSpan.TryParseExact(value.Trim(), new[] { @"h\:mm", @"hh\:mm" },
                CultureInfo.InvariantCulture, out TimeSpan time) &&
            time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);

        private static bool TryParseTimeRangeInput(string value)
        {
            string[] parts = value.Split(new[] { "～", "〜", "~", "-" },
                StringSplitOptions.TrimEntries);
            return parts.Length == 2 && TryParseTimeInput(parts[0]) &&
                   TryParseTimeInput(parts[1]);
        }

        private void AutomationCondition_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (sender is System.Windows.Controls.DataGrid grid && grid.DataContext is SimpleAutomationRule rule)
                Dispatcher.BeginInvoke(() => SaveAutomation(rule), DispatcherPriority.Background);
        }

        private void AutomationCondition_CurrentCellChanged(object? sender, EventArgs e)
        {
            if (!isLoadingAutomations && sender is System.Windows.Controls.DataGrid grid &&
                grid.DataContext is SimpleAutomationRule rule)
                SaveAutomation(rule);
        }

        private void SaveSelectedAutomation()
        {
            if (!isLoadingAutomations && AutomationRuleList.SelectedItem is SimpleAutomationRule rule)
                SaveAutomation(rule);
        }

        private void SaveAutomationForCondition(AutomationCondition condition)
        {
            if (isLoadingAutomations) return;
            SimpleAutomationRule? rule = simpleAutomations.FirstOrDefault(item =>
                item.Conditions.Contains(condition));
            if (rule != null) SaveAutomation(rule);
        }

        private void AutomationRuleExpander_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is not Expander expander ||
                expander.DataContext is not SimpleAutomationRule rule) return;
            AutomationRuleList.SelectedItem = rule;
            if (rule.SelectedCondition == null)
                rule.SelectedCondition = rule.Conditions.FirstOrDefault();
            if (rule.SelectedAction == null)
                rule.SelectedAction = rule.Actions.FirstOrDefault();
        }

        private void AutomationRuleCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element &&
                element.DataContext is SimpleAutomationRule rule)
                SaveAutomation(rule);
        }

        private void AutomationRuleTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element &&
                element.DataContext is SimpleAutomationRule rule)
                SaveAutomation(rule);
        }

        private void AutomationValue_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingAutomations || sender is not System.Windows.Controls.ComboBox comboBox ||
                comboBox.DataContext is not SimpleAutomationRule rule) return;
            Dispatcher.BeginInvoke(() => SaveAutomation(rule), DispatcherPriority.Background);
        }

        private void AutomationFrequency_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingAutomations || sender is not System.Windows.Controls.ComboBox comboBox ||
                comboBox.DataContext is not SimpleAutomationRule rule) return;
            Dispatcher.BeginInvoke(() => SaveAutomation(rule),
                DispatcherPriority.Background);
        }

        private void ResetOnceAutomation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not SimpleAutomationRule rule) return;
            rule.ResetOnceExecution();
            InitializeTimeConditionsForNextOccurrence(rule);
            SaveAutomation(rule, preserveOnceExecutionState: false);
        }

        private void SimpleAutomation_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (!isLoadingAutomations && e.Row.Item is SimpleAutomationRule rule)
                Dispatcher.BeginInvoke(() => SaveAutomation(rule), DispatcherPriority.Background);
        }

        private void SimpleAutomation_CurrentCellChanged(object? sender, EventArgs e)
        {
            if (!isLoadingAutomations && DataGridSimpleAutomations.CurrentItem is SimpleAutomationRule rule)
                SaveAutomation(rule);
        }

        private static void SaveAutomation(
            SimpleAutomationRule rule, bool preserveOnceExecutionState = true)
        {
            rule.EnsureActions();
            ButtonDatabase database = new();
            SimpleAutomationRule? saved = rule.Id == 0 ? null : database.LoadAutomation(rule.Id);

            // ComboBoxの再構築中には、一時的にSelectedValueが未選択になることがある。
            // その瞬間の自動保存で、既存の実行コマンドを空にしない。
            if (saved != null &&
                rule.Actions.All(action => string.IsNullOrWhiteSpace(action.CommandId)) &&
                saved.Actions.Any(action => !string.IsNullOrWhiteSpace(action.CommandId)))
            {
                rule.Actions = saved.Actions;
            }

            for (int index = 0; index < rule.Actions.Count; index++)
                rule.Actions[index].SortOrder = index;
            rule.ActionCommandId = rule.Actions.FirstOrDefault()?.CommandId ?? "";
            rule.DelayMinutes = Math.Clamp(rule.DelayMinutes, 0, 525600);
            rule.RequiredConsecutiveCount = Math.Clamp(rule.RequiredConsecutiveCount, 0, 525600);
            foreach (AutomationAction action in rule.Actions)
                action.ProtectionMinutes = Math.Clamp(action.ProtectionMinutes, 1, 525600);
            bool sameDefinition = saved != null &&
                HasSameAutomationDefinition(saved, rule);
            if (saved == null || !sameDefinition)
            {
                // 実際に条件や実行内容が変わった場合だけ、継続判定を数え直す。
                rule.CurrentConsecutiveCount = 0;
                rule.ConditionMatchedSinceUtc = null;
                rule.IsLatched = false;
                // 実行条件やコマンドを変更した1回実行は、新しい内容として
                // 再び実行できるようにする。
                if (preserveOnceExecutionState)
                    rule.HasExecutedOnce = false;
                // 「現在時刻 >= 指定時刻」という判定方式のため、設定時点で
                // すでに過ぎている今日の時刻は消化済みにして次回を待つ。
                InitializeTimeConditionsForNextOccurrence(rule);
            }
            else
            {
                // DataGridの再表示や選択変更による自動保存では、実行状態を失わない。
                rule.CurrentConsecutiveCount = saved.CurrentConsecutiveCount;
                rule.ConditionMatchedSinceUtc = saved.ConditionMatchedSinceUtc;
                rule.IsLatched = saved.IsLatched;
                rule.LastTriggeredDate = saved.LastTriggeredDate;
                rule.LastTimeRefreshDate = saved.LastTimeRefreshDate;
                // Editを開いたままバックグラウンドで1回実行が完了しても、
                // 画面側の古いfalseで実行済み状態を上書きしない。
                if (preserveOnceExecutionState)
                    rule.HasExecutedOnce = saved.HasExecutedOnce;
            }
            database.SaveAutomation(rule);
        }

        private static void InitializeTimeConditionsForNextOccurrence(
            SimpleAutomationRule rule)
        {
            DateTime now = DateTime.Now;
            string today = now.Date.ToString("yyyy-MM-dd",
                CultureInfo.InvariantCulture);
            foreach (AutomationCondition condition in rule.Conditions.Where(condition =>
                         condition.IsEnabled && string.Equals(condition.TriggerSensorId,
                             "Time", StringComparison.OrdinalIgnoreCase)))
            {
                condition.LastTriggeredDate = "";
                condition.LastScheduledRefreshDate = "";
                if (string.Equals(condition.TriggerDataLabel, "時間帯",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryGetTimeRange(condition.ConditionValue,
                            out TimeSpan start, out TimeSpan end))
                        continue;
                    bool inRange = start <= end
                        ? now.TimeOfDay >= start && now.TimeOfDay <= end
                        : now.TimeOfDay >= start || now.TimeOfDay <= end;
                    if (!inRange) continue;
                    DateTime periodDate = start > end && now.TimeOfDay <= end
                        ? now.Date.AddDays(-1)
                        : now.Date;
                    condition.LastTriggeredDate = periodDate.ToString(
                        "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    condition.LastScheduledRefreshDate = today;
                }
                else if (TimeSpan.TryParse(condition.ConditionValue,
                             CultureInfo.InvariantCulture, out TimeSpan target) &&
                         target >= TimeSpan.Zero && target < TimeSpan.FromDays(1) &&
                         now.TimeOfDay >= target)
                {
                    condition.LastTriggeredDate = today;
                    condition.LastScheduledRefreshDate = today;
                }
            }
        }

        private static bool TryGetTimeRange(
            string value, out TimeSpan start, out TimeSpan end)
        {
            start = default;
            end = default;
            string[] parts = (value ?? "").Split(
                new[] { "～", "〜", "~", "-" },
                StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2 &&
                   TimeSpan.TryParse(parts[0], CultureInfo.InvariantCulture, out start) &&
                   TimeSpan.TryParse(parts[1], CultureInfo.InvariantCulture, out end) &&
                   start >= TimeSpan.Zero && start < TimeSpan.FromDays(1) &&
                   end >= TimeSpan.Zero && end < TimeSpan.FromDays(1);
        }

        private static bool HasSameAutomationDefinition(
            SimpleAutomationRule left, SimpleAutomationRule right)
        {
            left.EnsureConditions();
            right.EnsureConditions();
            left.EnsureActions();
            right.EnsureActions();
            if (left.IsEnabled != right.IsEnabled ||
                left.Actions.Count != right.Actions.Count ||
                !string.Equals(left.LogicalOperator, right.LogicalOperator, StringComparison.Ordinal) ||
                !string.Equals(left.ExecutionFrequency, right.ExecutionFrequency,
                    StringComparison.Ordinal) ||
                left.SelectedWeekdays != right.SelectedWeekdays ||
                left.WeeklyDay != right.WeeklyDay ||
                left.SkipWhileTemporaryProtection != right.SkipWhileTemporaryProtection ||
                left.DelayMinutes != right.DelayMinutes ||
                left.RequiredConsecutiveCount != right.RequiredConsecutiveCount ||
                left.IsNotificationEnabled != right.IsNotificationEnabled ||
                left.Conditions.Count != right.Conditions.Count)
                return false;

            AutomationAction[] leftActions = left.Actions.OrderBy(item => item.SortOrder).ToArray();
            AutomationAction[] rightActions = right.Actions.OrderBy(item => item.SortOrder).ToArray();
            for (int index = 0; index < leftActions.Length; index++)
                if (!string.Equals(leftActions[index].CommandId,
                        rightActions[index].CommandId, StringComparison.Ordinal) ||
                    leftActions[index].ProtectionMinutes !=
                        rightActions[index].ProtectionMinutes)
                    return false;

            AutomationCondition[] leftConditions = left.Conditions.OrderBy(item => item.SortOrder).ToArray();
            AutomationCondition[] rightConditions = right.Conditions.OrderBy(item => item.SortOrder).ToArray();
            for (int index = 0; index < leftConditions.Length; index++)
            {
                AutomationCondition a = leftConditions[index];
                AutomationCondition b = rightConditions[index];
                if (a.IsEnabled != b.IsEnabled ||
                    !string.Equals(a.TriggerSensorId, b.TriggerSensorId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(SensorDataNormalizer.NormalizeKey(a.TriggerDataLabel),
                        SensorDataNormalizer.NormalizeKey(b.TriggerDataLabel), StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(SensorDataNormalizer.NormalizeValue(a.ConditionValue),
                        SensorDataNormalizer.NormalizeValue(b.ConditionValue), StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(a.Comparison, b.Comparison, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private void LoadServerSensors()
        {
            serverSensors.Clear();
            foreach (ServerSensorItem sensor in
                new ButtonDatabase().LoadServerSensors()
                    .OrderBy(sensor => sensor.Provider)
                    .ThenBy(sensor => sensor.DeviceName))
            {
                MigrateLegacySensorAssignment(sensor);
                serverSensors.Add(sensor);
            }
            new ButtonDatabase().SaveServerSensors(serverSensors);
            RefreshSensorAssignments();
        }

        private static void MigrateLegacySensorAssignment(ServerSensorItem sensor)
        {
            if (string.IsNullOrWhiteSpace(sensor.AssignedButtonNumber) ||
                !string.IsNullOrWhiteSpace(sensor.OtherButtonNumber) ||
                !string.IsNullOrWhiteSpace(sensor.OtherButtonNumber2) ||
                !string.IsNullOrWhiteSpace(sensor.OtherButtonNumber3) ||
                !string.IsNullOrWhiteSpace(sensor.OtherButtonNumber4) ||
                !string.IsNullOrWhiteSpace(sensor.OtherButtonNumber5) ||
                !string.IsNullOrWhiteSpace(sensor.OtherButtonNumber6) ||
                !string.IsNullOrWhiteSpace(sensor.OtherButtonNumber7) ||
                !string.IsNullOrWhiteSpace(sensor.OtherButtonNumber8))
                return;
            sensor.OtherButtonNumber = sensor.AssignedButtonNumber;
            sensor.AssignedButtonNumber = "";
        }

        private void LoadServerSensorSettings()
        {
            ServerSensorSettings settings =
                new ButtonDatabase().LoadServerSensorSettings();
            try
            {
                isUpdatingSensorSettings = true;
                CheckBoxSensorAutoUpdate.IsChecked = settings.AutoUpdateEnabled;
                CheckBoxSensorAutoUpdateSwitchBotWithWebhook.IsChecked =
                    settings.AutoUpdateSwitchBotWhenWebhookEnabled;
                CheckBoxSensorRefreshAfterCommand.IsChecked =
                    settings.RefreshAfterCommandEnabled;
                CheckBoxSensorLogRetention.IsChecked =
                    settings.LogRetentionEnabled;
                TextBoxSensorLogRetentionDays.Text =
                    Math.Clamp(settings.LogRetentionDays <= 0 ? 30 :
                        settings.LogRetentionDays, 1, 3650).ToString();
                TextBoxSensorLogDirectory.Text =
                    string.IsNullOrWhiteSpace(settings.LogDirectory)
                        ? SensorLogService.DefaultLogDirectory
                        : settings.LogDirectory;
                TextBoxSensorRefreshAfterCommandSeconds.Text =
                    (settings.RefreshAfterCommandSeconds <= 0
                        ? 10
                        : Math.Clamp(
                            settings.RefreshAfterCommandSeconds,
                            1,
                            3600)).ToString();
                TextBoxSensorUpdateInterval.Text =
                    settings.UpdateIntervalSeconds.ToString();
            }
            finally
            {
                isUpdatingSensorSettings = false;
            }
        }

        private async void RefreshServerSensors_Click(
            object sender, RoutedEventArgs e)
        {
            await RefreshServerSensorsAsync(showMessages: true);
        }

        private async Task RefreshServerSensorsAsync(bool showMessages)
        {
            ButtonDatabase database = new();
            Dictionary<string, ServerSensorItem> saved = database
                .LoadServerSensors().ToDictionary(item => item.Id);
            List<ServerSensorItem> received = new();
            List<string> errors = new();

            if (!string.IsNullOrWhiteSpace(
                Properties.Settings.Default.NatureRemoCloudToken))
            {
                try { received.AddRange(await NatureRemoCloudApiClient.GetSensorsAsync()); }
                catch (Exception ex) { errors.Add($"Nature Remo: {ex.Message}"); }
            }

            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.SwitchBotToken) &&
                !string.IsNullOrWhiteSpace(Properties.Settings.Default.SwitchBotSecret))
            {
                try { received.AddRange(await SwitchBotApiClient.GetSensorsAsync()); }
                catch (Exception ex) { errors.Add($"SwitchBot: {ex.Message}"); }
            }

            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.TuyaAccessId) &&
                !string.IsNullOrWhiteSpace(Properties.Settings.Default.TuyaAccessSecret))
            {
                try { received.AddRange(await TuyaApiClient.GetSensorsAsync()); }
                catch (Exception ex) { errors.Add($"Tuya: {ex.Message}"); }
            }

            foreach (ServerSensorItem sensor in received)
            {
                if (saved.TryGetValue(sensor.Id, out ServerSensorItem? old))
                {
                    MigrateLegacySensorAssignment(old);
                    sensor.CopyDataButtonAssignmentsFrom(old);
                    sensor.LogEnabled = old.LogEnabled;
                }
            }

            // 通信に成功した今回の取得値だけをログへ記録する。
            SensorLogService.Record(received,
                database.LoadServerSensorSettings());
            await mainWindow.EvaluateSimpleAutomationsAsync(received);

            foreach (ServerSensorItem old in saved.Values.Where(old =>
                !received.Any(sensor => sensor.Id == old.Id) &&
                ((old.Provider == "Nature Remo" && errors.Any(error => error.StartsWith("Nature Remo:"))) ||
                 (old.Provider == "SwitchBot" && errors.Any(error => error.StartsWith("SwitchBot:"))) ||
                 (old.Provider == "Tuya" && errors.Any(error => error.StartsWith("Tuya:"))))))
                received.Add(old);

            if (received.Count > 0)
            {
                serverSensors.Clear();
                foreach (ServerSensorItem sensor in received
                    .OrderBy(item => item.Provider).ThenBy(item => item.DeviceName))
                    serverSensors.Add(sensor);
                database.SaveServerSensors(serverSensors);
                RefreshSensorAssignments();
                RefreshAutomationOptions();
                mainWindow.ReloadServerSensorSettingsAndRefresh(useCachedValues: true);
            }

            if (showMessages && received.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    errors.Count > 0 ? string.Join("\n\n", errors) :
                    "取得できるセンサーがありません。認証情報と対応機器を確認してください。",
                    "サーバーセンサー");
            }
            else if (showMessages && errors.Count > 0)
            {
                System.Windows.MessageBox.Show(
                    "一部のセンサーを取得できませんでした。\n\n" +
                    string.Join("\n\n", errors), "サーバーセンサー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshSensorAssignments()
        {
            List<string> buttons = new ButtonDatabase().LoadAll()
                .Select(item => item.Name).OrderBy(name => name).ToList();
            buttons.Insert(0, "");
            try
            {
                isUpdatingSensorAssignments = true;
                foreach (ServerSensorItem sensor in serverSensors)
                    sensor.AvailableButtonNumbers = new List<string>(buttons);
                UpdateButtonSensorProviderDisplays();
                DataGridServerSensors.Items.Refresh();
            }
            finally { isUpdatingSensorAssignments = false; }
        }

        private void UpdateButtonSensorProviderDisplays()
        {
            foreach (ButtonSetting setting in buttonSettings)
                ApplyServerSensorProviderDisplay(setting);

            DataGridButtons.Items.Refresh();
        }

        private void ApplyServerSensorProviderDisplay(
            ButtonSetting setting)
        {
            ServerSensorItem? assignedSensor =
                serverSensors.FirstOrDefault(sensor =>
                    IsSensorMetricAssignedToButton(
                        sensor,
                        setting.Name));

            setting.ServerSensorProviderDisplay =
                assignedSensor?.Provider switch
                {
                    "Nature Remo" => "Nature Remo",
                    "SwitchBot" => "SwitchBot",
                    null => "",
                    _ => assignedSensor.Provider
                };
        }

        private static bool IsSensorMetricAssignedToButton(
            ServerSensorItem sensor,
            string buttonName)
        {
            return string.Equals(
                       sensor.OtherButtonNumber,
                       buttonName,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       sensor.OtherButtonNumber2,
                       buttonName,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       sensor.OtherButtonNumber3,
                       buttonName,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       sensor.OtherButtonNumber4,
                       buttonName,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       sensor.OtherButtonNumber5,
                       buttonName,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sensor.OtherButtonNumber6, buttonName,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sensor.OtherButtonNumber7, buttonName,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sensor.OtherButtonNumber8, buttonName,
                       StringComparison.OrdinalIgnoreCase);
        }

        private void SensorValueAssignmentComboBox_DropDownClosed(
            object? sender, EventArgs e)
        {
            if (isUpdatingSensorAssignments ||
                sender is not System.Windows.Controls.ComboBox comboBox ||
                comboBox.DataContext is not ServerSensorItem sensor)
                return;

            string metric = comboBox.Tag?.ToString() ?? "";
            string buttonName = comboBox.SelectedItem as string ?? "";
            SetSensorButtonNumber(sensor, metric, buttonName);
            if (!string.IsNullOrWhiteSpace(buttonName))
            {
                foreach (ServerSensorItem item in serverSensors)
                {
                    foreach (string otherMetric in new[]
                        { "Other", "Other2", "Other3", "Other4", "Other5",
                          "Other6", "Other7", "Other8" })
                    {
                        if ((item.Id != sensor.Id || otherMetric != metric) &&
                            GetSensorButtonNumber(item, otherMetric) == buttonName)
                            SetSensorButtonNumber(item, otherMetric, "");
                    }
                }
            }

            new ButtonDatabase().SaveServerSensors(serverSensors);
            RefreshSensorAssignments();
            mainWindow.ReloadServerSensorSettingsAndRefresh(useCachedValues: true);
        }

        private void SensorLogCheckBox_Click(object sender, RoutedEventArgs e)
        {
            // DataGridのTwoWayバインドを確実に反映してから保存する。
            DataGridServerSensors.CommitEdit(DataGridEditingUnit.Cell, true);
            DataGridServerSensors.CommitEdit(DataGridEditingUnit.Row, true);
            new ButtonDatabase().SaveServerSensors(serverSensors);
        }

        private static string GetSensorButtonNumber(
            ServerSensorItem sensor, string metric) => metric switch
        {
            "Other2" => sensor.OtherButtonNumber2,
            "Other3" => sensor.OtherButtonNumber3,
            "Other4" => sensor.OtherButtonNumber4,
            "Other5" => sensor.OtherButtonNumber5,
            "Other6" => sensor.OtherButtonNumber6,
            "Other7" => sensor.OtherButtonNumber7,
            "Other8" => sensor.OtherButtonNumber8,
            _ => sensor.OtherButtonNumber
        };

        private static void SetSensorButtonNumber(
            ServerSensorItem sensor, string metric, string value)
        {
            switch (metric)
            {
                case "Other2": sensor.OtherButtonNumber2 = value; break;
                case "Other3": sensor.OtherButtonNumber3 = value; break;
                case "Other4": sensor.OtherButtonNumber4 = value; break;
                case "Other5": sensor.OtherButtonNumber5 = value; break;
                case "Other6": sensor.OtherButtonNumber6 = value; break;
                case "Other7": sensor.OtherButtonNumber7 = value; break;
                case "Other8": sensor.OtherButtonNumber8 = value; break;
                default: sensor.OtherButtonNumber = value; break;
            }
        }

        private void ServerSensorSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (isUpdatingSensorSettings || !IsLoaded)
                return;

            if (!int.TryParse(TextBoxSensorUpdateInterval.Text, out int seconds))
                seconds = 60;
            seconds = Math.Clamp(seconds, 30, 3600);
            TextBoxSensorUpdateInterval.Text = seconds.ToString();

            if (!int.TryParse(
                    TextBoxSensorRefreshAfterCommandSeconds.Text,
                    out int commandDelaySeconds))
            {
                commandDelaySeconds = 10;
            }

            commandDelaySeconds =
                Math.Clamp(commandDelaySeconds, 1, 3600);

            TextBoxSensorRefreshAfterCommandSeconds.Text =
                commandDelaySeconds.ToString();

            if (!int.TryParse(TextBoxSensorLogRetentionDays.Text,
                    out int logRetentionDays))
                logRetentionDays = 30;
            logRetentionDays = Math.Clamp(logRetentionDays, 1, 3650);
            TextBoxSensorLogRetentionDays.Text = logRetentionDays.ToString();

            string logDirectory = TextBoxSensorLogDirectory.Text.Trim();
            if (!string.IsNullOrWhiteSpace(logDirectory))
            {
                try { logDirectory = System.IO.Path.GetFullPath(
                    Environment.ExpandEnvironmentVariables(logDirectory)); }
                catch
                {
                    System.Windows.MessageBox.Show(
                        LanguageManager.Get("ログの保存先が正しくありません。"),
                        LanguageManager.Get("センサーログ"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                TextBoxSensorLogDirectory.Text = logDirectory;
            }

            new ButtonDatabase().SaveServerSensorSettings(new ServerSensorSettings
            {
                AutoUpdateEnabled = CheckBoxSensorAutoUpdate.IsChecked == true,
                AutoUpdateSwitchBotWhenWebhookEnabled =
                    CheckBoxSensorAutoUpdateSwitchBotWithWebhook.IsChecked == true,
                UpdateIntervalSeconds = seconds,
                RefreshAfterCommandEnabled =
                    CheckBoxSensorRefreshAfterCommand.IsChecked == true,
                RefreshAfterCommandSeconds = commandDelaySeconds,
                LogRetentionEnabled =
                    CheckBoxSensorLogRetention.IsChecked == true,
                LogRetentionDays = logRetentionDays,
                LogDirectory = logDirectory,
                DisplayMode = "Individual"
            });
            mainWindow.ReloadServerSensorSettingsAndRefresh(useCachedValues: true);
        }

        private void SelectSensorLogDirectory_Click(
            object sender, RoutedEventArgs e)
        {
            using WinForms.FolderBrowserDialog dialog = new()
            {
                Description = LanguageManager.Get("センサーログの保存先を選択してください。"),
                ShowNewFolderButton = true
            };
            string current = TextBoxSensorLogDirectory.Text.Trim();
            if (Directory.Exists(current)) dialog.SelectedPath = current;
            if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;
            TextBoxSensorLogDirectory.Text = dialog.SelectedPath;
            ServerSensorSetting_Changed(sender, e);
        }

        public void ShowSensorLogTab()
        {
            if (ReferenceEquals(EditTabControl.SelectedItem, SensorLogTab))
            {
                if (IsLoaded) _ = LoadSensorLogsAsync();
                return;
            }
            EditTabControl.SelectedItem = SensorLogTab;
        }

        internal void UpdateAutomationOnceExecutionState(int ruleId, bool executed)
        {
            SimpleAutomationRule? rule = simpleAutomations.FirstOrDefault(item =>
                item.Id == ruleId);
            if (rule != null)
                rule.HasExecutedOnce = executed;
        }

        private async void EditTabControl_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
        {
            // 子コントロールのSelectionChangedのバブリングは処理しない。
            if (!ReferenceEquals(e.OriginalSource, EditTabControl) || !IsLoaded)
                return;

            ReloadPendingImportedTabData();

            if (ReferenceEquals(EditTabControl.SelectedItem, SimpleAutomationTab) &&
                simpleAutomations.Count > 0)
            {
                SimpleAutomationRule first = simpleAutomations[0];
                AutomationRuleList.SelectedItem = first;
                AutomationRuleList.ScrollIntoView(first);
            }
            else if (ReferenceEquals(EditTabControl.SelectedItem, SensorLogTab))
            {
                await LoadSensorLogsAsync();
            }
        }

        private void ReloadPendingImportedTabData()
        {
            if (ReferenceEquals(EditTabControl.SelectedItem, ServerCommandTab) &&
                pendingImportedCommandsReload)
            {
                LoadSwitchBotCommands();
                pendingImportedCommandsReload = false;
            }
            else if (ReferenceEquals(EditTabControl.SelectedItem, ServerSensorTab) &&
                pendingImportedSensorsReload)
            {
                LoadServerSensors();
                pendingImportedSensorsReload = false;
            }
            else if (ReferenceEquals(EditTabControl.SelectedItem, SimpleAutomationTab) &&
                (pendingImportedCommandsReload || pendingImportedSensorsReload ||
                 pendingImportedAutomationsReload))
            {
                // オートメーションの選択肢はコマンドとセンサーを参照するため、
                // このタブを開いた場合は依存データを先に一度だけ読み込む。
                if (pendingImportedCommandsReload)
                    LoadSwitchBotCommands();
                if (pendingImportedSensorsReload)
                    LoadServerSensors();
                LoadSimpleAutomations();
                pendingImportedCommandsReload = false;
                pendingImportedSensorsReload = false;
                pendingImportedAutomationsReload = false;
            }
        }

        private async void RefreshSensorLogs_Click(object sender, RoutedEventArgs e) =>
            await LoadSensorLogsAsync();

        private async Task LoadSensorLogsAsync()
        {
            if (isLoadingSensorLogs) return;
            isLoadingSensorLogs = true;
            string selectedSensor =
                (ComboBoxSensorLogDevice.SelectedItem as SensorLogDeviceOption)?.SensorId ?? "";
            string selectedMetric =
                (ComboBoxSensorLogMetric.SelectedItem as SensorLogMetricOption)?.Metric ?? "";
            ServerSensorSettings settings =
                new ButtonDatabase().LoadServerSensorSettings();

            try
            {
                // DBから軽量な系列一覧だけを取得する。既存CSVの初回移行も
                // バックグラウンドで行い、UIスレッドを停止させない。
                sensorLogSeriesCatalog = await Task.Run(() =>
                    SensorLogService.LoadSeriesCatalog(settings));

                isUpdatingSensorLogFilters = true;
                ComboBoxSensorLogDevice.ItemsSource = sensorLogSeriesCatalog
                    .GroupBy(item => item.SensorId)
                    .Select(group => new SensorLogDeviceOption
                    {
                        SensorId = group.Key,
                        DeviceName = $"{group.First().DeviceName} ({group.First().Provider})"
                    }).OrderBy(item => item.DeviceName).ToList();
                ComboBoxSensorLogDevice.SelectedItem =
                    ComboBoxSensorLogDevice.Items.Cast<SensorLogDeviceOption>()
                        .FirstOrDefault(item => item.SensorId == selectedSensor) ??
                    ComboBoxSensorLogDevice.Items.Cast<SensorLogDeviceOption>().FirstOrDefault();
                string sensorId =
                    (ComboBoxSensorLogDevice.SelectedItem as SensorLogDeviceOption)?.SensorId ?? "";
                UpdateSensorLogMetricOptions(sensorLogSeriesCatalog, sensorId, selectedMetric);
            }
            finally
            {
                isUpdatingSensorLogFilters = false;
                isLoadingSensorLogs = false;
            }
            await ApplySensorLogFilterAsync();
        }

        private void UpdateSensorLogMetricOptions(
            IReadOnlyCollection<SensorLogSeriesInfo> all,
            string sensorId,
            string selectedMetric)
        {
            List<SensorLogMetricOption> options = all
                .Where(item => item.SensorId == sensorId)
                .Select(item => item.Metric)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(metric => new SensorLogMetricOption { Metric = metric })
                .OrderBy(item => item.MetricDisplay, StringComparer.CurrentCulture)
                .ToList();
            ComboBoxSensorLogMetric.ItemsSource = options;
            ComboBoxSensorLogMetric.SelectedItem = options
                .FirstOrDefault(item => string.Equals(item.Metric, selectedMetric,
                    StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();
        }

        private async void SensorLogFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || isUpdatingSensorLogFilters) return;
            if (ReferenceEquals(sender, ComboBoxSensorLogDevice))
            {
                string selectedMetric =
                    (ComboBoxSensorLogMetric.SelectedItem as SensorLogMetricOption)?.Metric ?? "";
                try
                {
                    isUpdatingSensorLogFilters = true;
                    string sensorId =
                        (ComboBoxSensorLogDevice.SelectedItem as SensorLogDeviceOption)?.SensorId ?? "";
                    UpdateSensorLogMetricOptions(
                        sensorLogSeriesCatalog, sensorId, selectedMetric);
                }
                finally { isUpdatingSensorLogFilters = false; }
            }
            await ApplySensorLogFilterAsync();
        }

        private async Task ApplySensorLogFilterAsync()
        {
            int requestVersion = ++sensorLogFilterRequestVersion;
            if (ComboBoxSensorLogDevice.SelectedItem is not SensorLogDeviceOption device ||
                ComboBoxSensorLogMetric.SelectedItem is not SensorLogMetricOption metricOption)
            {
                allFilteredSensorLogEntries.Clear();
                sensorLogOverallFrom = default;
                sensorLogOverallTo = default;
                DataGridSensorLogs.ItemsSource = null;
                DrawSensorLogChart(); return;
            }
            string metric = metricOption.Metric;
            ServerSensorSettings settings =
                new ButtonDatabase().LoadServerSensorSettings();
            SensorLogSeriesInfo? series = sensorLogSeriesCatalog.FirstOrDefault(
                item => item.SensorId == device.SensorId && item.Metric == metric);
            DateTime rangeTo = series?.LastTimestamp ?? DateTime.Now;
            DateTime rangeFrom = series?.FirstTimestamp ?? rangeTo.AddHours(-24);
            sensorLogOverallFrom = rangeFrom;
            sensorLogOverallTo = rangeTo;
            List<SensorLogEntry> loadedEntries = await Task.Run(() =>
                SensorLogService.LoadChartSeries(
                    settings, device.SensorId, metric, rangeFrom, rangeTo));
            if (requestVersion != sensorLogFilterRequestVersion ||
                ComboBoxSensorLogDevice.SelectedItem is not SensorLogDeviceOption currentDevice ||
                ComboBoxSensorLogMetric.SelectedItem is not SensorLogMetricOption currentMetric ||
                currentDevice.SensorId != device.SensorId ||
                currentMetric.Metric != metric)
                return;
            allFilteredSensorLogEntries = loadedEntries;
            // カタログの開始時刻は、保存期間による削除や集計機能追加前の
            // データを指したままの場合がある。左端は実際に取得できた最初の
            // グラフ点に合わせ、右端だけは生ログの正確な最新時刻を維持する。
            if (allFilteredSensorLogEntries.Count > 0)
            {
                sensorLogOverallFrom = allFilteredSensorLogEntries.First().Timestamp;
                sensorLogOverallTo = rangeTo > allFilteredSensorLogEntries.Last().Timestamp
                    ? rangeTo
                    : allFilteredSensorLogEntries.Last().Timestamp;
            }
            // 下部のナビゲーターには全期間を読み込み、初期選択だけを
            // 右端の直近24時間にする。
            if (sensorLogOverallTo > sensorLogOverallFrom)
            {
                DateTime initialFrom = sensorLogOverallTo.AddHours(-24);
                sensorLogRangeStart = GetTimestampPosition(
                    initialFrom, sensorLogOverallFrom, sensorLogOverallTo);
            }
            else
            {
                sensorLogRangeStart = 0;
            }
            sensorLogRangeEnd = 1;
            ApplySelectedSensorLogRange();
            await RefreshSensorLogGridAsync();
        }

        private void ApplySelectedSensorLogRange()
        {
            if (sensorLogOverallFrom != default && sensorLogOverallTo != default)
            {
                GetSelectedSensorLogTimeRange(out DateTime from, out DateTime to);
                TextBlockSensorLogRange.Text =
                    $"{from:yyyy/MM/dd HH:mm} ～ {to:yyyy/MM/dd HH:mm}";
            }
            else
            {
                TextBlockSensorLogRange.Text = "";
            }
            DrawSensorLogChart();
        }

        private async Task RefreshSensorLogGridAsync()
        {
            int requestVersion = ++sensorLogGridRequestVersion;
            if (ComboBoxSensorLogDevice.SelectedItem is not SensorLogDeviceOption device ||
                ComboBoxSensorLogMetric.SelectedItem is not SensorLogMetricOption metric ||
                sensorLogOverallFrom == default || sensorLogOverallTo == default)
            {
                DataGridSensorLogs.ItemsSource = null;
                return;
            }
            GetSelectedSensorLogTimeRange(out DateTime from, out DateTime to);
            ServerSensorSettings settings =
                new ButtonDatabase().LoadServerSensorSettings();
            List<SensorLogEntry> latest = await Task.Run(() =>
                SensorLogService.LoadLatestSeries(settings, device.SensorId,
                    metric.Metric, from, to, 1000));
            if (requestVersion != sensorLogGridRequestVersion ||
                ComboBoxSensorLogDevice.SelectedItem is not SensorLogDeviceOption currentDevice ||
                ComboBoxSensorLogMetric.SelectedItem is not SensorLogMetricOption currentMetric ||
                currentDevice.SensorId != device.SensorId ||
                currentMetric.Metric != metric.Metric)
                return;
            // ObservableCollectionへ1件ずつ追加せず、1回の通知で一覧を差し替える。
            DataGridSensorLogs.ItemsSource = latest;
        }

        private async void ResetSensorLogRange_Click(object sender, RoutedEventArgs e)
        {
            if (ComboBoxSensorLogDevice.SelectedItem is not SensorLogDeviceOption device ||
                ComboBoxSensorLogMetric.SelectedItem is not SensorLogMetricOption metric)
                return;
            SensorLogSeriesInfo? series = sensorLogSeriesCatalog.FirstOrDefault(
                item => item.SensorId == device.SensorId && item.Metric == metric.Metric);
            if (series == null || series.FirstTimestamp == default) return;
            ServerSensorSettings settings =
                new ButtonDatabase().LoadServerSensorSettings();
            allFilteredSensorLogEntries = await Task.Run(() =>
                SensorLogService.LoadChartSeries(settings, device.SensorId,
                    metric.Metric, series.FirstTimestamp, series.LastTimestamp));
            sensorLogOverallFrom = allFilteredSensorLogEntries.Count > 0
                ? allFilteredSensorLogEntries.First().Timestamp
                : series.FirstTimestamp;
            sensorLogOverallTo = allFilteredSensorLogEntries.Count > 0 &&
                allFilteredSensorLogEntries.Last().Timestamp > series.LastTimestamp
                    ? allFilteredSensorLogEntries.Last().Timestamp
                    : series.LastTimestamp;
            sensorLogRangeStart = 0;
            sensorLogRangeEnd = 1;
            ApplySelectedSensorLogRange();
            await RefreshSensorLogGridAsync();
        }

        private void SensorLogChart_SizeChanged(object sender, SizeChangedEventArgs e) =>
            DrawSensorLogChart();

        private void DrawSensorLogChart()
        {
            SensorLogChart.Children.Clear();
            List<SensorLogEntry> points = new();
            if (allFilteredSensorLogEntries.Count > 0)
            {
                GetSelectedSensorLogTimeRange(out DateTime chartFrom,
                    out DateTime chartTo);
                points = DownsampleSensorLogEntries(
                    allFilteredSensorLogEntries
                        .Where(item => item.Timestamp >= chartFrom &&
                            item.Timestamp <= chartTo &&
                            item.NumericValue.HasValue &&
                            double.IsFinite(item.NumericValue.Value))
                        .OrderBy(item => item.Timestamp).ToList(), 500);
            }
            TextBlockSensorLogEmpty.Visibility = points.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
            double width = SensorLogChart.ActualWidth;
            double height = SensorLogChart.ActualHeight;
            if (points.Count == 0 || width < 100 || height < 80) return;

            const double left = 55, right = 15, top = 18;
            // X軸の日付と操作案内が重ならないよう、概要グラフまでの
            // 縦方向の余白を十分に確保する。
            const double navigatorHeight = 42, navigatorGap = 52, bottom = 8;
            double chartWidth = Math.Max(1, width - left - right);
            double chartHeight = Math.Max(1,
                height - top - navigatorGap - navigatorHeight - bottom);
            CalculateSensorLogYAxis(points,
                out double min, out double max, out double yStep);
            GetSelectedSensorLogTimeRange(
                out DateTime visibleFrom, out DateTime visibleTo);

            int yIntervalCount = Math.Max(1,
                (int)Math.Round((max - min) / yStep));
            for (int index = 0; index <= yIntervalCount; index++)
            {
                double value = max - yStep * index;
                double y = top + chartHeight * index / yIntervalCount;
                SensorLogChart.Children.Add(new Line
                {
                    X1 = left, X2 = left + chartWidth, Y1 = y, Y2 = y,
                    Stroke = new SolidColorBrush(MediaColor.FromArgb(45, 100, 100, 100)),
                    StrokeThickness = 1
                });
                TextBlock label = new()
                {
                    Text = value.ToString(yStep < 1 ? "0.0" : "0.##"),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                    FontSize = 11
                };
                Canvas.SetLeft(label, 2); Canvas.SetTop(label, y - 8);
                SensorLogChart.Children.Add(label);
            }

            // X軸は表示期間に応じて、時刻・日付・月の境界に揃えた
            // 固定間隔の目盛りを使用する。
            TimeSpan visibleDuration = visibleTo - visibleFrom;
            SensorLogTimeStep timeStep = SelectSensorLogTimeStep(visibleDuration);
            double lastTimeLabelRight = double.NegativeInfinity;
            foreach (DateTime timestamp in EnumerateSensorLogTimeTicks(
                visibleFrom, visibleTo, timeStep))
            {
                double x = left + chartWidth * GetTimestampPosition(
                    timestamp, visibleFrom, visibleTo);
                SensorLogChart.Children.Add(new Line
                {
                    X1 = x, X2 = x, Y1 = top, Y2 = top + chartHeight,
                    Stroke = new SolidColorBrush(MediaColor.FromArgb(30, 100, 100, 100)),
                    StrokeThickness = 1
                });

                string format = timeStep switch
                {
                    SensorLogTimeStep.Minute1 or SensorLogTimeStep.Minute5 or
                    SensorLogTimeStep.Minute10 =>
                        visibleFrom.Date == visibleTo.Date ? "HH:mm" : "MM/dd HH:mm",
                    SensorLogTimeStep.Hour1 => "MM/dd HH:mm",
                    SensorLogTimeStep.Day1 or SensorLogTimeStep.Day10 => "MM/dd",
                    _ => "yyyy/MM"
                };
                double labelWidth = format switch
                {
                    "HH:mm" or "MM/dd" => 52,
                    "yyyy/MM" => 62,
                    _ => 88
                };
                double labelLeft = Math.Clamp(
                    x - labelWidth / 2,
                    left - 2,
                    left + chartWidth - labelWidth + 2);

                // 目盛り線は残し、文字だけが隣と重なる場合は間引く。
                if (labelLeft < lastTimeLabelRight + 4)
                {
                    continue;
                }

                TextBlock timeLabel = new()
                {
                    Text = timestamp.ToString(format),
                    Width = labelWidth,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 10,
                    Foreground = (System.Windows.Media.Brush)
                        FindResource("TextSecondaryBrush")
                };
                Canvas.SetLeft(timeLabel, labelLeft);
                Canvas.SetTop(timeLabel, top + chartHeight + 7);
                SensorLogChart.Children.Add(timeLabel);
                lastTimeLabelRight = labelLeft + labelWidth;
            }

            TimeSpan maximumConnectedGap = TimeSpan.FromHours(3);
            bool useStepLine = IsBinarySensorLogSeries(points);
            Polyline? line = null;
            System.Windows.Point? previousPoint = null;
            for (int index = 0; index < points.Count; index++)
            {
                if (line == null || (index > 0 &&
                    points[index].Timestamp - points[index - 1].Timestamp >
                        maximumConnectedGap))
                {
                    line = new Polyline
                    {
                        Stroke = new SolidColorBrush(
                            MediaColor.FromRgb(0, 120, 212)),
                        StrokeThickness = 2,
                        Clip = new RectangleGeometry(new System.Windows.Rect(
                            left, top, chartWidth, chartHeight))
                    };
                    SensorLogChart.Children.Add(line);
                    previousPoint = null;
                }

                double x = left + chartWidth * GetTimestampPosition(
                    points[index].Timestamp, visibleFrom, visibleTo);
                double y = Math.Clamp(
                    top + (max - points[index].NumericValue!.Value) /
                        (max - min) * chartHeight,
                    top,
                    top + chartHeight);
                System.Windows.Point currentPoint = new(x, y);
                if (useStepLine && previousPoint.HasValue)
                    line.Points.Add(new System.Windows.Point(
                        currentPoint.X, previousPoint.Value.Y));
                line.Points.Add(currentPoint);
                previousPoint = currentPoint;
            }

            DrawSensorLogNavigator(left, top + chartHeight + navigatorGap,
                chartWidth, navigatorHeight);
        }

        private static void CalculateSensorLogYAxis(
            IReadOnlyCollection<SensorLogEntry> points,
            out double min,
            out double max,
            out double step)
        {
            double rawMin = points.Min(item => item.NumericValue!.Value);
            double rawMax = points.Max(item => item.NumericValue!.Value);
            double range = rawMax - rawMin;
            double targetStep = range <= 0.0001 ? 0 : range / 6;

            double[] allowedSteps =
            {
                1,
                5,
                10,
                50,
                100,
                500,
                1000,
                5000,
                10000
            };
            step = allowedSteps.FirstOrDefault(candidate =>
                candidate >= targetStep);
            if (step <= 0) step = allowedSteps[^1];

            min = Math.Floor(rawMin / step) * step;
            max = Math.Ceiling(rawMax / step) * step;
            if (Math.Abs(max - min) < 0.0001)
            {
                min -= step;
                max += step;
            }
        }

        private static bool IsBinarySensorLogSeries(
            IReadOnlyCollection<SensorLogEntry> points)
        {
            if (points.Count == 0 || !points.All(item =>
                    item.NumericValue.HasValue &&
                    (Math.Abs(item.NumericValue.Value) < 0.0001 ||
                     Math.Abs(item.NumericValue.Value - 100) < 0.0001)))
                return false;

            return points.Any(item =>
            {
                string value = item.RawValue.Trim().ToLowerInvariant();
                return value is "オン" or "オフ" or "on" or "off" or
                    "施錠" or "解錠" or "lock" or "unlock" or "locked" or
                    "unlocked" or "はい" or "いいえ" or "yes" or "no" or
                    "true" or "false";
            });
        }

        private static SensorLogTimeStep SelectSensorLogTimeStep(TimeSpan duration)
        {
            double targetMinutes = Math.Max(1, duration.TotalMinutes / 8);
            (SensorLogTimeStep Step, double Minutes)[] candidates =
            {
                (SensorLogTimeStep.Minute1, 1),
                (SensorLogTimeStep.Minute5, 5),
                (SensorLogTimeStep.Minute10, 10),
                (SensorLogTimeStep.Hour1, 60),
                (SensorLogTimeStep.Day1, 24 * 60),
                (SensorLogTimeStep.Day10, 10 * 24 * 60),
                (SensorLogTimeStep.Month1, 30.4375 * 24 * 60)
            };
            return candidates.MinBy(candidate =>
                Math.Abs(Math.Log(candidate.Minutes / targetMinutes))).Step;
        }

        private static IEnumerable<DateTime> EnumerateSensorLogTimeTicks(
            DateTime from, DateTime to, SensorLogTimeStep step)
        {
            DateTime current;
            Func<DateTime, DateTime> next;
            switch (step)
            {
                case SensorLogTimeStep.Minute1:
                    current = AlignTimeTick(from, TimeSpan.FromMinutes(1));
                    next = value => value.AddMinutes(1);
                    break;
                case SensorLogTimeStep.Minute5:
                    current = AlignTimeTick(from, TimeSpan.FromMinutes(5));
                    next = value => value.AddMinutes(5);
                    break;
                case SensorLogTimeStep.Minute10:
                    current = AlignTimeTick(from, TimeSpan.FromMinutes(10));
                    next = value => value.AddMinutes(10);
                    break;
                case SensorLogTimeStep.Hour1:
                    current = AlignTimeTick(from, TimeSpan.FromHours(1));
                    next = value => value.AddHours(1);
                    break;
                case SensorLogTimeStep.Day1:
                    current = from.Date;
                    if (current < from) current = current.AddDays(1);
                    next = value => value.AddDays(1);
                    break;
                case SensorLogTimeStep.Day10:
                    long dayNumber = from.Date.Ticks / TimeSpan.TicksPerDay;
                    current = new DateTime(
                        ((dayNumber + 9) / 10 * 10) * TimeSpan.TicksPerDay,
                        from.Kind);
                    next = value => value.AddDays(10);
                    break;
                default:
                    current = new DateTime(from.Year, from.Month, 1,
                        0, 0, 0, from.Kind);
                    if (current < from) current = current.AddMonths(1);
                    next = value => value.AddMonths(1);
                    break;
            }

            for (int count = 0; current <= to && count < 500; count++)
            {
                yield return current;
                current = next(current);
            }
        }

        private static DateTime AlignTimeTick(DateTime value, TimeSpan interval)
        {
            long ticks = interval.Ticks;
            long alignedTicks = ((value.Ticks + ticks - 1) / ticks) * ticks;
            return new DateTime(alignedTicks, value.Kind);
        }

        private void DrawSensorLogNavigator(
            double left, double top, double width, double height)
        {
            if (allFilteredSensorLogEntries.Count == 0) return;
            List<SensorLogEntry> overview = DownsampleSensorLogEntries(
                allFilteredSensorLogEntries
                    .Where(item => item.NumericValue.HasValue &&
                        double.IsFinite(item.NumericValue.Value)).ToList(), 600);
            if (overview.Count == 0) return;
            double min = overview.Min(item => item.NumericValue!.Value);
            double max = overview.Max(item => item.NumericValue!.Value);
            if (Math.Abs(max - min) < 0.0001) { min -= 1; max += 1; }
            DateTime overallFrom = sensorLogOverallFrom;
            DateTime overallTo = sensorLogOverallTo;

            System.Windows.Shapes.Rectangle navigatorBackground = new()
            {
                Width = width, Height = height,
                Fill = new SolidColorBrush(MediaColor.FromArgb(18, 0, 120, 212)),
                Stroke = new SolidColorBrush(MediaColor.FromArgb(80, 100, 100, 100))
            };
            SensorLogChart.Children.Add(navigatorBackground);
            Canvas.SetLeft(navigatorBackground, left);
            Canvas.SetTop(navigatorBackground, top);

            TimeSpan maximumConnectedGap = TimeSpan.FromHours(3);
            bool useStepLine = IsBinarySensorLogSeries(overview);
            Polyline? overviewLine = null;
            System.Windows.Point? previousPoint = null;
            for (int index = 0; index < overview.Count; index++)
            {
                if (overviewLine == null || (index > 0 &&
                    overview[index].Timestamp - overview[index - 1].Timestamp >
                        maximumConnectedGap))
                {
                    overviewLine = new Polyline
                    {
                        Stroke = new SolidColorBrush(
                            MediaColor.FromRgb(90, 120, 145)),
                        StrokeThickness = 1.25,
                        Clip = new RectangleGeometry(new System.Windows.Rect(
                            left, top, width, height))
                    };
                    SensorLogChart.Children.Add(overviewLine);
                    previousPoint = null;
                }

                double x = left + width * GetTimestampPosition(
                    overview[index].Timestamp, overallFrom, overallTo);
                double y = Math.Clamp(
                    top + (max - overview[index].NumericValue!.Value) /
                        (max - min) * height,
                    top,
                    top + height);
                System.Windows.Point currentPoint = new(x, y);
                if (useStepLine && previousPoint.HasValue)
                    overviewLine.Points.Add(new System.Windows.Point(
                        currentPoint.X, previousPoint.Value.Y));
                overviewLine.Points.Add(currentPoint);
                previousPoint = currentPoint;
            }

            double selectionLeft = left + width * sensorLogRangeStart;
            double selectionWidth = Math.Max(3,
                width * (sensorLogRangeEnd - sensorLogRangeStart));
            System.Windows.Shapes.Rectangle selection = new()
            {
                Width = selectionWidth, Height = height,
                Fill = new SolidColorBrush(MediaColor.FromArgb(50, 0, 120, 212)),
                Stroke = new SolidColorBrush(MediaColor.FromRgb(0, 120, 212)),
                StrokeThickness = 2
            };
            Canvas.SetLeft(selection, selectionLeft); Canvas.SetTop(selection, top);
            SensorLogChart.Children.Add(selection);

            foreach (double handleX in new[]
                { selectionLeft, selectionLeft + selectionWidth })
            {
                System.Windows.Shapes.Rectangle handle = new()
                {
                    Width = 7, Height = height,
                    Fill = new SolidColorBrush(MediaColor.FromRgb(0, 120, 212))
                };
                Canvas.SetLeft(handle, handleX - 3.5); Canvas.SetTop(handle, top);
                SensorLogChart.Children.Add(handle);
            }

            TextBlock hint = new()
            {
                Text = LanguageManager.Get("下のグラフをドラッグして表示期間を選択"),
                FontSize = 11,
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush")
            };
            Canvas.SetLeft(hint, left); Canvas.SetTop(hint, top - 19);
            SensorLogChart.Children.Add(hint);
        }

        private void GetSelectedSensorLogTimeRange(
            out DateTime from, out DateTime to)
        {
            DateTime overallFrom = sensorLogOverallFrom;
            DateTime overallTo = sensorLogOverallTo;
            if (overallFrom == default || overallTo == default)
            {
                overallTo = DateTime.Now;
                overallFrom = overallTo.AddHours(-24);
            }
            if (overallTo <= overallFrom)
                overallTo = overallFrom.AddMinutes(1);

            from = InterpolateTimestamp(
                overallFrom, overallTo, sensorLogRangeStart);
            to = InterpolateTimestamp(
                overallFrom, overallTo, sensorLogRangeEnd);
        }

        private static DateTime InterpolateTimestamp(
            DateTime from, DateTime to, double fraction)
        {
            fraction = Math.Clamp(fraction, 0, 1);
            return from.AddTicks((long)((to - from).Ticks * fraction));
        }

        private static double GetTimestampPosition(
            DateTime timestamp, DateTime from, DateTime to)
        {
            double durationTicks = (to - from).Ticks;
            if (durationTicks <= 0) return 0;
            return Math.Clamp((timestamp - from).Ticks / durationTicks, 0, 1);
        }

        private static List<SensorLogEntry> DownsampleSensorLogEntries(
            IReadOnlyList<SensorLogEntry> source, int maximum)
        {
            if (source.Count <= maximum) return source.ToList();
            List<SensorLogEntry> result = new(maximum);
            for (int index = 0; index < maximum; index++)
                result.Add(source[index * (source.Count - 1) / (maximum - 1)]);
            return result;
        }

        private void SensorLogChart_MouseLeftButtonDown(
            object sender, MouseButtonEventArgs e)
        {
            if (allFilteredSensorLogEntries.Count == 0) return;
            if (!TryGetSensorLogNavigatorPosition(e.GetPosition(SensorLogChart),
                    out double position)) return;

            const double handleTolerance = 0.015;
            sensorLogDragAnchor = position;
            sensorLogDragOriginalStart = sensorLogRangeStart;
            sensorLogDragOriginalEnd = sensorLogRangeEnd;
            sensorLogNewRangeWasDragged = false;
            if (Math.Abs(position - sensorLogRangeStart) <= handleTolerance)
                sensorLogRangeDragMode = SensorLogRangeDragMode.LeftHandle;
            else if (Math.Abs(position - sensorLogRangeEnd) <= handleTolerance)
                sensorLogRangeDragMode = SensorLogRangeDragMode.RightHandle;
            else if (position > sensorLogRangeStart && position < sensorLogRangeEnd &&
                     sensorLogRangeEnd - sensorLogRangeStart < 0.995)
                sensorLogRangeDragMode = SensorLogRangeDragMode.Move;
            else
            {
                sensorLogRangeDragMode = SensorLogRangeDragMode.NewRange;
            }
            SensorLogChart.CaptureMouse();
            e.Handled = true;
        }

        private void SensorLogChart_MouseMove(
            object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sensorLogRangeDragMode == SensorLogRangeDragMode.None) return;
            if (!TryGetSensorLogNavigatorPosition(e.GetPosition(SensorLogChart),
                    out double position, requireVerticalHit: false)) return;

            const double minimumRange = 0.002;
            switch (sensorLogRangeDragMode)
            {
                case SensorLogRangeDragMode.LeftHandle:
                    sensorLogRangeStart = Math.Min(position,
                        sensorLogRangeEnd - minimumRange); break;
                case SensorLogRangeDragMode.RightHandle:
                    sensorLogRangeEnd = Math.Max(position,
                        sensorLogRangeStart + minimumRange); break;
                case SensorLogRangeDragMode.NewRange:
                    if (Math.Abs(position - sensorLogDragAnchor) >= minimumRange)
                    {
                        sensorLogNewRangeWasDragged = true;
                        sensorLogRangeStart = Math.Min(sensorLogDragAnchor, position);
                        sensorLogRangeEnd = Math.Max(sensorLogDragAnchor, position);
                    }
                    break;
                case SensorLogRangeDragMode.Move:
                    double width = sensorLogDragOriginalEnd - sensorLogDragOriginalStart;
                    double start = sensorLogDragOriginalStart + position - sensorLogDragAnchor;
                    start = Math.Clamp(start, 0, 1 - width);
                    sensorLogRangeStart = start; sensorLogRangeEnd = start + width; break;
            }
            sensorLogRangeStart = Math.Clamp(sensorLogRangeStart, 0, 1);
            sensorLogRangeEnd = Math.Clamp(sensorLogRangeEnd, 0, 1);
            ApplySelectedSensorLogRange();
        }

        private void SensorLogChart_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
            EndSensorLogRangeDrag();

        private void SensorLogChart_MouseLeave(
            object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                EndSensorLogRangeDrag();
        }

        private async void EndSensorLogRangeDrag()
        {
            if (sensorLogRangeDragMode == SensorLogRangeDragMode.None) return;
            if (sensorLogRangeDragMode == SensorLogRangeDragMode.NewRange &&
                !sensorLogNewRangeWasDragged)
            {
                sensorLogRangeStart = sensorLogDragOriginalStart;
                sensorLogRangeEnd = sensorLogDragOriginalEnd;
            }
            sensorLogRangeDragMode = SensorLogRangeDragMode.None;
            sensorLogNewRangeWasDragged = false;
            SensorLogChart.ReleaseMouseCapture();
            ApplySelectedSensorLogRange();
            await RefreshSensorLogGridAsync();
        }

        private bool TryGetSensorLogNavigatorPosition(
            System.Windows.Point point, out double position,
            bool requireVerticalHit = true)
        {
            const double left = 55, right = 15, top = 18;
            const double navigatorHeight = 42, navigatorGap = 52, bottom = 8;
            double width = Math.Max(1, SensorLogChart.ActualWidth - left - right);
            double mainHeight = Math.Max(1, SensorLogChart.ActualHeight - top -
                navigatorGap - navigatorHeight - bottom);
            double navigatorTop = top + mainHeight + navigatorGap;
            position = Math.Clamp((point.X - left) / width, 0, 1);
            return !requireVerticalHit ||
                (point.Y >= navigatorTop - 5 &&
                 point.Y <= navigatorTop + navigatorHeight + 5);
        }

        private void RefreshSwitchBotAssignments()
        {
            ButtonDatabase database = new();
            List<ButtonSetting> allButtons =
                database.LoadAll();
            List<ButtonSetting> assignedButtons =
                allButtons
                    .Where(setting =>
                        string.Equals(setting.Provider, "SwitchBot",
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(setting.Provider, "SwitchBotScene",
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(setting.Provider, "NatureRemoCloud",
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(setting.Provider, "Tuya",
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(setting.Provider, "TuyaScene",
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            try
            {
                isUpdatingSwitchBotAssignments = true;

                foreach (SwitchBotCommandItem command in switchBotCommands)
                {
                    command.AssignedButtonNumbers = string.Join(
                        ", ",
                        assignedButtons
                            .Where(setting =>
                                string.Equals(
                                    setting.Provider,
                                    command.Provider,
                                    StringComparison.OrdinalIgnoreCase) &&
                                setting.SwitchBotDeviceId == command.DeviceId &&
                                setting.SwitchBotCommand == command.Command &&
                                setting.SwitchBotCommandType == command.CommandType &&
                                setting.SwitchBotParameter == command.Parameter)
                            .Select(setting => setting.Name)
                            .OrderBy(name => name));

                    command.AvailableButtonNumbers = allButtons
                        .Select(setting => setting.Name)
                        .OrderBy(name => name)
                        .ToList();

                    command.AvailableButtonNumbers.Insert(0, "");

                    if (!string.IsNullOrWhiteSpace(command.AssignedButtonNumbers) &&
                        !command.AvailableButtonNumbers.Contains(
                            command.AssignedButtonNumbers))
                    {
                        command.AvailableButtonNumbers.Insert(
                            0,
                            command.AssignedButtonNumbers);
                    }
                }

                DataGridSwitchBotCommands.Items.Refresh();
            }
            finally
            {
                isUpdatingSwitchBotAssignments = false;
            }
        }

        private void SwitchBotAssignmentComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (isUpdatingSwitchBotAssignments ||
                sender is not System.Windows.Controls.ComboBox comboBox ||
                comboBox.DataContext is not SwitchBotCommandItem command ||
                comboBox.SelectedItem is not string buttonName ||
                buttonName.Contains(", "))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(buttonName))
            {
                ClearSwitchBotCommandAssignments(command);
                return;
            }

            ButtonDatabase database = new();
            ButtonSetting? setting = database.Load(buttonName);
            if (setting == null)
            {
                return;
            }

            if (string.Equals(
                    setting.Provider,
                    command.Provider,
                    StringComparison.OrdinalIgnoreCase) &&
                setting.SwitchBotDeviceId == command.DeviceId &&
                setting.SwitchBotCommand == command.Command &&
                setting.SwitchBotCommandType == command.CommandType &&
                setting.SwitchBotParameter == command.Parameter)
            {
                return;
            }

            setting.Provider = command.Provider;
            setting.SwitchBotDeviceId = command.DeviceId;
            setting.SwitchBotDeviceName = command.DeviceName;
            setting.SwitchBotCommand = command.Command;
            setting.SwitchBotParameter = command.Parameter;
            setting.SwitchBotCommandType = command.CommandType;
            database.Save(setting);

            mainWindow.NotifyButtonStateChanged(buttonName);
            RefreshSwitchBotAssignments();
        }

        private void ClearSwitchBotCommandAssignments(
            SwitchBotCommandItem command)
        {
            ButtonDatabase database = new();
            List<ButtonSetting> targets = database
                .LoadAll()
                .Where(setting =>
                    string.Equals(
                        setting.Provider,
                        command.Provider,
                        StringComparison.OrdinalIgnoreCase) &&
                    setting.SwitchBotDeviceId == command.DeviceId &&
                    setting.SwitchBotCommand == command.Command &&
                    setting.SwitchBotCommandType == command.CommandType &&
                    setting.SwitchBotParameter == command.Parameter)
                .ToList();

            if (targets.Count == 0)
            {
                return;
            }

            foreach (ButtonSetting setting in targets)
            {
                setting.Provider = "None";
                setting.SwitchBotDeviceId = "";
                setting.SwitchBotDeviceName = "";
                setting.SwitchBotCommand = "";
                setting.SwitchBotParameter = "default";
                setting.SwitchBotCommandType = "command";
                database.Save(setting);
                mainWindow.NotifyButtonStateChanged(setting.Name);
            }

            RefreshSwitchBotAssignments();
        }

        private void Edit_Closing(
            object? sender,
            System.ComponentModel.CancelEventArgs e)
        {
            if (hostedSettings.IsClearingAllData ||
                hostedSettings.IsImportingBackup)
                return;

            SaveCurrentIrData();

            hostedSettings.SaveHostedSettings();

            // インポート後にまだ該当タブを開いていない場合、画面上の
            // 古いコレクションでインポート済みDBを上書きしない。
            if (!pendingImportedCommandsReload)
            {
                new ButtonDatabase()
                    .SaveSwitchBotCommands(switchBotCommands);
            }

            if (!pendingImportedAutomationsReload)
            {
                foreach (SimpleAutomationRule rule in simpleAutomations)
                    SaveAutomation(rule);
            }
        }

        public void ShowSettingsTab()
        {
            if (generalSettingsTab != null)
                EditTabControl.SelectedItem = generalSettingsTab;
        }

        public void ShowButtonEditTab() =>
            EditTabControl.SelectedItem = ButtonEditTab;

        // ========================================
        // Button名を設定
        // ========================================
        public void SetButtonName(string buttonName)
        {
            currentButtonName = buttonName;
        }


        // ========================================
        // インポート後にエディタを再読み込み
        // ========================================
        public void ReloadAfterImport()
        {
            try
            {
                isUpdatingEditor = true;

                LoadDataGrid();
                LoadServerSensorSettings();
                LoadWebhookSettings();

                pendingImportedCommandsReload = true;
                pendingImportedSensorsReload = true;
                pendingImportedAutomationsReload = true;

                if (DataGridButtons.Items.Count > 0)
                {
                    DataGridButtons.SelectedIndex = 0;

                    DataGridButtons.ScrollIntoView(
                        DataGridButtons.SelectedItem);
                }
                else
                {
                    TextBoxNo.Clear();
                    TextBoxButtonName.Clear();
                    TextBoxIR.Clear();
                }
            }
            finally
            {
                isUpdatingEditor = false;
            }

            DataGridButtons.Items.Refresh();
        }

        public async Task ReloadSensorLogsAfterClearAsync()
        {
            await LoadSensorLogsAsync();
        }

        // クラス内に追加するヘルパー
        private System.Windows.Controls.Button? GetTargetButton()
        {
            if (mainWindow == null ||
                TextBoxNo == null)
            {
                return null;
            }

            string buttonName =
                TextBoxNo.Text.Trim();

            if (string.IsNullOrWhiteSpace(buttonName))
                return null;

            // FindNameは使用しない
            return mainWindow.GetButton(buttonName);
        }

        public void SetText(string text)
        {
            if (TextBoxNo == null)
            {
                return;
            }

            TextBoxNo.Text = text;

            UpdateFontWeightToggle(text);
            ButtonSetting? setting = buttonSettings.FirstOrDefault(item =>
                string.Equals(item.Name, text, StringComparison.OrdinalIgnoreCase));
            if (setting != null)
                UpdateDevicePanelThemeControls(setting);
        }

        private void UpdateDevicePanelThemeControls(ButtonSetting setting)
        {
            bool isPanel = string.Equals(setting.ItemType, "DevicePanel",
                StringComparison.OrdinalIgnoreCase);
            DevicePanelThemeButton.Visibility = isPanel
                ? Visibility.Visible
                : Visibility.Collapsed;
            MouseOverEffectButton.Visibility = isPanel
                ? Visibility.Collapsed
                : Visibility.Visible;
            DevicePanelThemeButton.Content = string.Equals(
                setting.PanelTheme,
                "Dark",
                StringComparison.OrdinalIgnoreCase)
                    ? LanguageManager.Get("パネル：ダーク")
                    : LanguageManager.Get("パネル：ライト");
        }

        private void DevicePanelThemeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (DataGridButtons.SelectedItem is not ButtonSetting setting ||
                !string.Equals(setting.ItemType, "DevicePanel",
                    StringComparison.OrdinalIgnoreCase))
                return;

            setting.PanelTheme = string.Equals(setting.PanelTheme, "Dark",
                StringComparison.OrdinalIgnoreCase)
                    ? "Light"
                    : "Dark";
            mainWindow.ApplyAndSaveButtonSetting(setting);
            DataGridButtons.Items.Refresh();
            UpdateDevicePanelThemeControls(setting);
        }

        // ========================================
        // ボタン名を変更する
        // ========================================
        private void TextBoxButtonName_TextChanged(object sender,TextChangedEventArgs e)
        {
            if (isUpdatingEditor)
                return;

            string buttonName =
                TextBoxNo.Text.Trim();

            if (string.IsNullOrWhiteSpace(buttonName))
                return;

            System.Windows.Controls.Button? button =
                mainWindow.GetButton(buttonName);

            if (button == null)
                return;

            string newContent =
                TextBoxButtonName.Text;

            ButtonSetting? selectedSetting = buttonSettings.FirstOrDefault(item =>
                string.Equals(item.Name, buttonName,
                    StringComparison.OrdinalIgnoreCase));
            // デバイスパネルは文字列をContentへ直接代入すると
            // パネル自体が消えるため、確定時に再描画する。
            if (!string.Equals(selectedSetting?.ItemType, "DevicePanel",
                StringComparison.OrdinalIgnoreCase))
                button.Content = TextBoxButtonName.Text;
        }

        // ========================================
        // ボタン名を変更した後、LiteDBへ保存する
        // ========================================
        private void TextBoxButtonName_LostKeyboardFocus(
    object sender,
    KeyboardFocusChangedEventArgs e)
        {
            if (isUpdatingEditor)
                return;

            string buttonName =
                TextBoxNo.Text.Trim();

            if (string.IsNullOrWhiteSpace(buttonName))
                return;

            System.Windows.Controls.Button? button =
                mainWindow.GetButton(buttonName);

            if (button == null)
                return;

            string newContent =
                TextBoxButtonName.Text;

            bool isDevicePanel = string.Equals(
                buttonSettings.FirstOrDefault(item => string.Equals(
                    item.Name, buttonName,
                    StringComparison.OrdinalIgnoreCase))?.ItemType,
                "DevicePanel",
                StringComparison.OrdinalIgnoreCase);
            if (!isDevicePanel)
                button.Content = newContent;

            ButtonSetting? setting =
                buttonSettings.FirstOrDefault(x =>
                    string.Equals(
                        x.Name,
                        buttonName,
                        StringComparison.OrdinalIgnoreCase));

            if (setting != null)
            {
                setting.NameContent =
                    newContent;
            }

            // 表示名はセンサーの一時表示と分離して保存する
            mainWindow.SaveButtonDisplayName(
                buttonName,
                newContent);
        }

        // ========================================
        // キーダウン時にEnterが押されたらDataGridへフォーカスを移す
        // ========================================
        private void TextBoxButtonName_KeyDown(
    object sender,
    System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            // IME変換確定後にフォーカスを移す
            DataGridButtons.Focus();

            e.Handled = true;
        }

        // ========================================
        // フォントサイズを変更する
        // ========================================
        private void IncreaseFontSizeButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            System.Windows.Controls.Button? button = GetTargetButton();

            if (button == null)
                return;

            button.FontSize += 1;

            mainWindow.SaveButtonSetting(
                button.Name);
        }

        // ========================================
        // フォントサイズを変更する
        // ========================================
        private void DecreaseFontSizeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            System.Windows.Controls.Button? button = GetTargetButton();

            if (button == null)
                return;

            button.FontSize =
                Math.Max(1, button.FontSize - 1);

            mainWindow.SaveButtonSetting(
                button.Name);
        }

        // ========================================
        // ボタン背景色を変更する
        // ========================================
        private void Button_Click_10(object sender,RoutedEventArgs e)
        {
            System.Windows.Controls.Button? button = GetTargetButton();

            if (button == null)
            {
                System.Windows.MessageBox.Show(
                    "対象のボタンが見つかりません。");
                return;
            }

            using WinForms.ColorDialog dialog =
                new WinForms.ColorDialog();

            if (button.Background
                is SolidColorBrush currentBrush)
            {
                MediaColor currentColor =
                    currentBrush.Color;

                dialog.Color =
                    System.Drawing.Color.FromArgb(
                        currentColor.A,
                        currentColor.R,
                        currentColor.G,
                        currentColor.B);
            }

            if (dialog.ShowDialog() !=
                WinForms.DialogResult.OK)
            {
                return;
            }

            System.Drawing.Color color =
                dialog.Color;

            button.Background =
                new SolidColorBrush(
                    MediaColor.FromArgb(
                        color.A,
                        color.R,
                        color.G,
                        color.B));

            mainWindow.SaveButtonSetting(
                button.Name);
        }

        // ========================================
        // 文字色変更する
        // ========================================
        private void Button_Click_11(
    object sender,
    RoutedEventArgs e)
        {
            System.Windows.Controls.Button? button = GetTargetButton();

            if (button == null)
            {
                System.Windows.MessageBox.Show(
                    "対象のボタンが見つかりません。");
                return;
            }

            using WinForms.ColorDialog dialog =
                new WinForms.ColorDialog();

            if (button.Foreground
                is SolidColorBrush currentBrush)
            {
                MediaColor currentColor =
                    currentBrush.Color;

                dialog.Color =
                    System.Drawing.Color.FromArgb(
                        currentColor.A,
                        currentColor.R,
                        currentColor.G,
                        currentColor.B);
            }

            if (dialog.ShowDialog() !=
                WinForms.DialogResult.OK)
            {
                return;
            }

            System.Drawing.Color color =
                dialog.Color;

            button.Foreground =
                new SolidColorBrush(
                    MediaColor.FromArgb(
                        color.A,
                        color.R,
                        color.G,
                        color.B));

            mainWindow.SaveButtonSetting(
                button.Name);
        }


        // ========================================
        // ボタンの表示/非表示を切り替える
        // ========================================
        private void CheckBoxVisible_Changed(
    object sender,
    RoutedEventArgs e)
        {
            if (!IsLoaded ||
                isUpdatingEditor)
            {
                return;
            }

            System.Windows.Controls.Button? button =
                GetTargetButton();

            if (button == null)
                return;

            button.Visibility =
                CheckBoxVisible.IsChecked == true
                    ? Visibility.Visible
                    : Visibility.Hidden;

            mainWindow.SaveButtonSetting(
                button.Name);
        }

        // ========================================
        // 保守作業用
        // ========================================
        private void Button_Click_12(object sender, RoutedEventArgs e)
        {
            if (mainWindow == null || TextBoxNo == null)
                return;

            // TextBoxNo.Text（ボタン名）から ButtonSetting を取得
            ButtonSetting? setting = mainWindow.GetButtonSetting(TextBoxNo.Text);

            if (setting == null)
                return;

            // LiteDBへ保存
            var database = new ButtonDatabase();
            database.Save(setting);
        }

        // ========================================
        // 赤外線を取得
        // ========================================
        private async void GetIrButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                TextBoxIR.Text =
                    "取得中...";

                string url =
                    Properties.Settings.Default
                        .URL1.Trim();

                if (!url.StartsWith("http://") &&
                    !url.StartsWith("https://"))
                {
                    url = "http://" + url;
                }

                url =
                    url.TrimEnd('/') +
                    "/messages";

                using HttpRequestMessage request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        url);

                request.Headers
                    .TryAddWithoutValidation(
                        "X-Requested-With",
                        "local");

                using HttpResponseMessage response =
                    await new HttpClient()
                        .SendAsync(request);

                string result =
                    await response.Content
                        .ReadAsStringAsync();

                response.EnsureSuccessStatusCode();

                TextBoxIR.Text =
                    result;

                SaveCurrentIrData();
            }
            catch (Exception ex)
            {
                TextBoxIR.Text =
                    $"取得エラー: {ex.Message}";
            }
        }

        private void TextBoxIR_LostKeyboardFocus(
            object sender,
            KeyboardFocusChangedEventArgs e)
        {
            SaveCurrentIrData();
        }

        private void SaveCurrentIrData()
        {
            if (isUpdatingEditor)
                return;

            string buttonName =
                TextBoxNo.Text.Trim();

            if (string.IsNullOrWhiteSpace(buttonName))
                return;

            if (TextBoxIR.Text == "取得中..." ||
                TextBoxIR.Text.StartsWith(
                    "取得エラー:",
                    StringComparison.Ordinal))
            {
                return;
            }

            ButtonDatabase database =
                new ButtonDatabase();

            ButtonSetting? setting =
                database.Load(buttonName);

            if (setting == null)
                return;

            setting.Data1 =
                TextBoxIR.Text;

            database.Save(setting);

            ButtonSetting? gridSetting =
                buttonSettings.FirstOrDefault(
                    item => string.Equals(
                        item.Name,
                        buttonName,
                        StringComparison.OrdinalIgnoreCase));

            if (gridSetting != null)
                gridSetting.Data1 = TextBoxIR.Text;

            DataGridButtons.Items.Refresh();
            mainWindow.NotifyButtonStateChanged(buttonName);
        }

        // ========================================
        // 終了時
        // ========================================
        private void Edit_Closed(object? sender,EventArgs e)
        {
            mainWindow.ButtonStateChanged -=
                MainWindow_ButtonStateChanged;
        }


    }
}
