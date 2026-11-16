using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.IO;
using System.Linq;
using LiteDB;
using MediaColor = System.Windows.Media.Color;
using WinForms = System.Windows.Forms;
using Microsoft.Win32;
using System.Collections.Generic;



namespace N_Remo
{
    /// <summary>
    /// Setting.xaml の相互作用ロジック
    /// </summary>
    public partial class Setting : Window
    {

        private readonly MainWindow mainWindow;
        private bool isLoadingSettings;
        private bool suppressSettingsSaveOnClose;
        private bool isHostedInEdit;
        internal bool IsClearingAllData { get; private set; }
        internal bool IsImportingBackup => suppressSettingsSaveOnClose;
        private bool IsSettingsUiReady => IsLoaded || isHostedInEdit;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWin = 0x0008;



        public Setting(MainWindow mainWindow)
        {
            InitializeComponent();

            this.mainWindow = mainWindow;
            Closing += Setting_Closing;

            try
            {
                isLoadingSettings = true;

                InitializeHotkeySettings();

                TextBoxURL1.Text =
                    Properties.Settings.Default.URL1;

                PasswordBoxNatureRemoCloudToken.Password =
                    Properties.Settings.Default.NatureRemoCloudToken;

                PasswordBoxSwitchBotToken.Password =
                    Properties.Settings.Default.SwitchBotToken;

                PasswordBoxSwitchBotSecret.Password =
                    Properties.Settings.Default.SwitchBotSecret;

                TextBoxTuyaAccessId.Text =
                    Properties.Settings.Default.TuyaAccessId;

                PasswordBoxTuyaAccessSecret.Password =
                    Properties.Settings.Default.TuyaAccessSecret;

                ComboBoxTuyaEndpoint.SelectedValue =
                    Properties.Settings.Default.TuyaEndpoint;

                TextBoxWidth.Text =
                    Properties.Settings.Default
                        .MainWindowWidth.ToString();

                TextBoxHeight.Text =
                    Properties.Settings.Default
                        .MainWindowHeight.ToString();

                CheckBoxCloseToTray.IsChecked =
                    Properties.Settings.Default
                        .CloseToTray;

                CheckBoxStartWithWindows.IsChecked =
                    Properties.Settings.Default.StartWithWindows;

                CheckBoxStartMinimizedToTray.IsChecked =
                    Properties.Settings.Default.StartMinimizedToTray;
                UpdateStartMinimizedAvailability();

                CheckBoxCloseWhenDeactivated.IsChecked =
                    Properties.Settings.Default.CloseWhenDeactivated;

                CheckBoxCloseAfterButtonClick.IsChecked =
                    Properties.Settings.Default
                        .CloseAfterButtonClick;

                CheckBoxMouseGestureEnabled.IsChecked =
                    Properties.Settings.Default
                        .MouseGestureEnabled;

                ComboBoxMouseCorner.SelectedValue =
                    Properties.Settings.Default
                        .MouseGestureCorner;

                ComboBoxMouseGesture.SelectedValue =
                    Properties.Settings.Default
                        .MouseGestureAction;

                ComboBoxLanguage.SelectedValue =
                    Properties.Settings.Default.Language;
            }
            finally
            {
                isLoadingSettings = false;
            }
        }

        internal List<TabItem> TakeTabsForEdit()
        {
            isHostedInEdit = true;
            if (Content is not System.Windows.Controls.TabControl tabControl)
                return new List<TabItem>();

            List<TabItem> tabs = tabControl.Items
                .OfType<TabItem>()
                .ToList();
            foreach (TabItem tab in tabs)
                tabControl.Items.Remove(tab);
            Content = null;
            return tabs;
        }

        internal void SaveHostedSettings()
        {
            if (!suppressSettingsSaveOnClose)
                SaveEditableSettingsFromControls();
        }

        private void ComboBoxLanguage_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (isLoadingSettings || !IsSettingsUiReady ||
                ComboBoxLanguage.SelectedValue is not string language)
            {
                return;
            }

            Properties.Settings.Default.Language = language;
            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();
            LanguageManager.SetLanguage(language);
        }

        private void Setting_Closing(
            object? sender,
            System.ComponentModel.CancelEventArgs e)
        {
            // インポート成功後は、画面に残るインポート前の値で
            // 復元した設定を上書きしない。
            if (suppressSettingsSaveOnClose)
            {
                return;
            }

            SaveEditableSettingsFromControls();
        }

        private bool SaveEditableSettingsFromControls(
            bool showValidationMessage = false)
        {
            if (!double.TryParse(TextBoxWidth.Text, out double width) ||
                width < 300 || width > 10000)
            {
                if (showValidationMessage)
                {
                    System.Windows.MessageBox.Show(
                        "Widthは300～10000の範囲で入力してください。",
                        "入力エラー",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    TextBoxWidth.Focus();
                }

                return false;
            }

            if (!double.TryParse(TextBoxHeight.Text, out double height) ||
                height < 200 || height > 10000)
            {
                if (showValidationMessage)
                {
                    System.Windows.MessageBox.Show(
                        "Heightは200～10000の範囲で入力してください。",
                        "入力エラー",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    TextBoxHeight.Focus();
                }

                return false;
            }

            Properties.Settings.Default.URL1 =
                TextBoxURL1.Text.Trim();
            Properties.Settings.Default.NatureRemoCloudToken =
                PasswordBoxNatureRemoCloudToken.Password.Trim();
            Properties.Settings.Default.SwitchBotToken =
                PasswordBoxSwitchBotToken.Password.Trim();
            Properties.Settings.Default.SwitchBotSecret =
                PasswordBoxSwitchBotSecret.Password.Trim();
            Properties.Settings.Default.TuyaAccessId =
                TextBoxTuyaAccessId.Text.Trim();
            Properties.Settings.Default.TuyaAccessSecret =
                PasswordBoxTuyaAccessSecret.Password.Trim();
            Properties.Settings.Default.TuyaEndpoint =
                ComboBoxTuyaEndpoint.SelectedValue?.ToString()
                ?? "https://openapi.tuyaus.com";
            Properties.Settings.Default.MainWindowWidth = width;
            Properties.Settings.Default.MainWindowHeight = height;
            Properties.Settings.Default.CloseToTray =
                CheckBoxCloseToTray.IsChecked == true;
            Properties.Settings.Default.StartWithWindows =
                CheckBoxStartWithWindows.IsChecked == true;
            Properties.Settings.Default.StartMinimizedToTray =
                CheckBoxStartMinimizedToTray.IsChecked == true;
            Properties.Settings.Default.CloseWhenDeactivated =
                CheckBoxCloseWhenDeactivated.IsChecked == true;
            Properties.Settings.Default.CloseAfterButtonClick =
                CheckBoxCloseAfterButtonClick.IsChecked == true;
            Properties.Settings.Default.MouseGestureEnabled =
                CheckBoxMouseGestureEnabled.IsChecked == true;
            Properties.Settings.Default.MouseGestureCorner =
                ComboBoxMouseCorner.SelectedValue?.ToString() ?? "BottomLeft";
            Properties.Settings.Default.MouseGestureAction =
                ComboBoxMouseGesture.SelectedValue?.ToString() ?? "MoveToCorner";

            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();
            return true;
        }

        private void SaveSwitchBotSettings_Click(
            object sender,
            RoutedEventArgs e)
        {
            Properties.Settings.Default.SwitchBotToken =
                PasswordBoxSwitchBotToken.Password.Trim();
            Properties.Settings.Default.SwitchBotSecret =
                PasswordBoxSwitchBotSecret.Password.Trim();
            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();

            System.Windows.MessageBox.Show(
                "SwitchBot設定を保存しました。",
                "SwitchBot",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async void TestSwitchBotConnection_Click(
            object sender,
            RoutedEventArgs e)
        {
            Properties.Settings.Default.SwitchBotToken =
                PasswordBoxSwitchBotToken.Password.Trim();
            Properties.Settings.Default.SwitchBotSecret =
                PasswordBoxSwitchBotSecret.Password.Trim();

            try
            {
                List<SwitchBotDevice> devices =
                    await SwitchBotApiClient.GetDevicesAsync();

                System.Windows.MessageBox.Show(
                    $"接続に成功しました。\nデバイス数: {devices.Count}",
                    "SwitchBot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.Message,
                    "SwitchBot接続エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SaveTuyaSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveTuyaControls();
            System.Windows.MessageBox.Show(
                "Tuya設定を保存しました。", "Tuya",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void TestTuyaConnection_Click(object sender, RoutedEventArgs e)
        {
            SaveTuyaControls();
            try
            {
                List<TuyaDevice> devices = await TuyaApiClient.GetDevicesAsync();
                System.Windows.MessageBox.Show(
                    $"接続に成功しました。\nデバイス数: {devices.Count}", "Tuya",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Tuya接続エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveTuyaControls()
        {
            Properties.Settings.Default.TuyaAccessId = TextBoxTuyaAccessId.Text.Trim();
            Properties.Settings.Default.TuyaAccessSecret = PasswordBoxTuyaAccessSecret.Password.Trim();
            Properties.Settings.Default.TuyaEndpoint = ComboBoxTuyaEndpoint.SelectedValue?.ToString()
                ?? "https://openapi.tuyaus.com";
            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();
            TuyaApiClient.ResetAuthentication();
        }

        // ========================================
        // チェック時の処理
        // ========================================
        private void CheckBoxCloseAfterButtonClick_Changed(
    object sender,
    RoutedEventArgs e)
        {
            if (isLoadingSettings)
                return;

            Properties.Settings.Default
                .CloseAfterButtonClick =
                CheckBoxCloseAfterButtonClick.IsChecked == true;

            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();
        }

        private void CheckBoxCloseWhenDeactivated_Changed(
            object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings || !IsSettingsUiReady)
                return;

            Properties.Settings.Default.CloseWhenDeactivated =
                CheckBoxCloseWhenDeactivated.IsChecked == true;
            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();
        }

        private void StartupSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings || !IsSettingsUiReady) return;

            Properties.Settings.Default.StartWithWindows =
                CheckBoxStartWithWindows.IsChecked == true;
            Properties.Settings.Default.StartMinimizedToTray =
                CheckBoxStartMinimizedToTray.IsChecked == true;
            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();
            WindowsStartupManager.Apply(
                Properties.Settings.Default.StartWithWindows);
        }

        private void UpdateStartMinimizedAvailability()
        {
            CheckBoxStartMinimizedToTray.IsEnabled =
                CheckBoxCloseToTray.IsChecked == true;
        }

        // ========================================
        // 起動時の設定読み込み処理
        // ========================================
        private void Setting_Loaded(
    object sender,
    RoutedEventArgs e)
        {
            try
            {
                isLoadingSettings = true;

                CheckBoxCloseAfterButtonClick.IsChecked =
                    Properties.Settings.Default
                        .CloseAfterButtonClick;
            }
            finally
            {
                isLoadingSettings = false;
            }

            CheckBoxMouseGestureEnabled.IsChecked =
    Properties.Settings.Default
        .MouseGestureEnabled;

            ComboBoxMouseCorner.SelectedValue =
                Properties.Settings.Default
                    .MouseGestureCorner;

            ComboBoxMouseGesture.SelectedValue =
                Properties.Settings.Default
                    .MouseGestureAction;
        }

        // ========================================
        // ホットキーのテキストを取得する処理
        // ========================================
        private string GetHotkeyText(uint modifiers,Key key)
        {
            List<string> parts =
                new List<string>();

            if ((modifiers & ModControl) != 0)
                parts.Add("Ctrl");

            if ((modifiers & ModAlt) != 0)
                parts.Add("Alt");

            if ((modifiers & ModShift) != 0)
                parts.Add("Shift");

            if ((modifiers & ModWin) != 0)
                parts.Add("Win");

            parts.Add(key.ToString());

            return string.Join(" + ", parts);
        }

        // ========================================
        // グローバルホットキー 設定を適用する処理
        // ========================================
        private void ApplyHotkeySettingsImmediately()
        {
            bool enabled =
                CheckBoxHotkeyEnabled.IsChecked == true;

            // 無効化は即時反映
            if (!enabled)
            {
                mainWindow.DisableGlobalHotkey();

                Properties.Settings.Default
                    .HotkeyEnabled = false;

                Properties.Settings.Default.Save();
                PersistentAppSettings.SaveFromProperties();
                return;
            }

            if (ComboBoxHotkeyKey.SelectedItem
                is not Key selectedKey)
            {
                return;
            }

            uint modifiers = 0;

            if (CheckBoxHotkeyCtrl.IsChecked == true)
                modifiers |= ModControl;

            if (CheckBoxHotkeyAlt.IsChecked == true)
                modifiers |= ModAlt;

            if (CheckBoxHotkeyShift.IsChecked == true)
                modifiers |= ModShift;

            if (CheckBoxHotkeyWin.IsChecked == true)
                modifiers |= ModWin;

            // 修飾キーを変更している途中の可能性があるため、
            // ここではメッセージを出さず以前の登録を維持する
            if (modifiers == 0)
                return;

            bool success =
                mainWindow.TryApplyGlobalHotkey(
                    modifiers,
                    selectedKey,
                    out string errorMessage);

            if (!success)
            {
                System.Windows.MessageBox.Show(
                    errorMessage,
                    "ホットキー登録エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            Properties.Settings.Default
                .HotkeyEnabled = true;

            Properties.Settings.Default
                .HotkeyModifiers =
                (int)modifiers;

            Properties.Settings.Default
                .HotkeyKey =
                selectedKey.ToString();

            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();
        }


        // ========================================
        // マウスジェスチャー 設定が変更されたときに即座に反映する処理
        // ========================================
        private void MouseGestureSetting_Changed(
    object sender,
    RoutedEventArgs e)
        {
            if (isLoadingSettings ||
                !IsSettingsUiReady)
            {
                return;
            }

            SaveAndApplyMouseGestureSettings();
        }

        // ========================================
        // マウスジェスチャー 設定を保存して即座に反映する処理
        // ========================================
        private void SaveAndApplyMouseGestureSettings()
        {
            string corner =
                ComboBoxMouseCorner.SelectedValue
                    ?.ToString()
                ?? "BottomRight";

            string action =
                ComboBoxMouseGesture.SelectedValue
                    ?.ToString()
                ?? "MoveToCorner";

            Properties.Settings.Default
                .MouseGestureEnabled =
                CheckBoxMouseGestureEnabled.IsChecked == true;

            Properties.Settings.Default
                .MouseGestureCorner =
                corner;

            Properties.Settings.Default
                .MouseGestureAction =
                action;

            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();

            // 監視を停止して新しい設定で再開
            mainWindow.ReloadMouseGestureSettings();
        }

        // ========================================
        // グローバルホットキー 設定画面を初期化
        // ========================================
        private void InitializeHotkeySettings()
        {
            List<Key> availableKeys = new List<Key>
            {
                Key.A, Key.B, Key.C, Key.D, Key.E,
                Key.F, Key.G, Key.H, Key.I, Key.J,
                Key.K, Key.L, Key.M, Key.N, Key.O,
                Key.P, Key.Q, Key.R, Key.S, Key.T,
                Key.U, Key.V, Key.W, Key.X, Key.Y,
                Key.Z,

                Key.D0, Key.D1, Key.D2, Key.D3,
                Key.D4, Key.D5, Key.D6, Key.D7,
                Key.D8, Key.D9,

                Key.F1, Key.F2, Key.F3, Key.F4,
                Key.F5, Key.F6, Key.F7, Key.F8,
                Key.F9, Key.F10, Key.F11, Key.F12
            };

            ComboBoxHotkeyKey.ItemsSource =
                availableKeys;

            CheckBoxHotkeyEnabled.IsChecked =
                Properties.Settings.Default
                    .HotkeyEnabled;

            uint modifiers =
                (uint)Properties.Settings.Default
                    .HotkeyModifiers;

            CheckBoxHotkeyCtrl.IsChecked =
                (modifiers & ModControl) != 0;

            CheckBoxHotkeyAlt.IsChecked =
                (modifiers & ModAlt) != 0;

            CheckBoxHotkeyShift.IsChecked =
                (modifiers & ModShift) != 0;

            CheckBoxHotkeyWin.IsChecked =
                (modifiers & ModWin) != 0;

            string savedKey =
                Properties.Settings.Default.HotkeyKey;

            if (Enum.TryParse(savedKey, out Key key))
            {
                ComboBoxHotkeyKey.SelectedItem = key;
            }
            else
            {
                ComboBoxHotkeyKey.SelectedItem = Key.N;
            }
        }

        // ========================================
        // タスクトレイ機能 チェックを入れた場合
        // ========================================
        private void CheckBoxCloseToTray_Checked(object sender,RoutedEventArgs e)
        {
            if (isLoadingSettings)
            {
                return;
            }

            Properties.Settings.Default.CloseToTray = true;
            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();
            UpdateStartMinimizedAvailability();
        }

        // ========================================
        // URL1の検索ボタンを押した場合
        // ========================================
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ButtonUrlAuto.Content = "検索中...";
                ButtonUrlAuto.IsEnabled = false;
            });

            string resultPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dns_sd_result.txt");

            try
            {
                // 前回の検索結果を今回の結果と取り違えないようにする
                if (File.Exists(resultPath))
                {
                    File.Delete(resultPath);
                }

                BatCommand();
                InstanceNameEdit();

                Properties.Settings.Default.URL1 = TextBoxURL1.Text;
                Properties.Settings.Default.Save();
                PersistentAppSettings.SaveFromProperties();
            }
            finally
            {
                // 検索成功・失敗にかかわらず、結果ファイルを片付ける
                if (File.Exists(resultPath))
                {
                    File.Delete(resultPath);
                }

                ButtonUrlAuto.Content = "自動取得";
                ButtonUrlAuto.IsEnabled = true;
            }
        }

        // ========================================
        // タスクトレイ機能 チェックが外れた場合
        // ========================================
        private void CheckBoxCloseToTray_Unchecked(object sender,RoutedEventArgs e)
        {
            if (isLoadingSettings)
            {
                return;
            }

            Properties.Settings.Default.CloseToTray = false;
            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();
            UpdateStartMinimizedAvailability();
        }


        public void BatCommand()
        {
            // 1. 一時フォルダのパスとファイル名を決める
            string tempPath = System.IO.Path.GetTempPath();
            string batchFilePath = System.IO.Path.Combine(tempPath, $"script_{Guid.NewGuid()}.bat");

            // 2. バッチファイルに書く内容
            string batchContent = "@echo off\n" +
                                  "set \"outputFile=%TMP%\\dns_sd_result.txt\"\n" +
                                  "start /b cmd /c \"dns-sd -B _remo._tcp > \"%outputFile%\"\"\n" +
                                  "timeout /t 2 /nobreak > nul\n" +
                                  "taskkill /f /im dns-sd.exe > nul\n" +
                                  "Yn";

            try
            {
                // 3. バッチファイルを作成して書き込む
                File.WriteAllText(batchFilePath, batchContent);

                // 4. バッチファイルを実行する設定
                var psi = new ProcessStartInfo
                {
                    FileName = batchFilePath,
                    UseShellExecute = false, // コマンドプロンプト画面を開く場合はtrue
                    CreateNoWindow = true  // ウィンドウを隠す場合はtrue
                };

                // 5. 実行して終了を待つ
                using (var process = Process.Start(psi))
                {
                    process?.WaitForExit();
                }
            }
            finally
            {
                // 6. 使い終わったバッチファイルを削除する
                if (File.Exists(batchFilePath))
                {
                    File.Delete(batchFilePath);
                }
            }


        }

        public void InstanceNameEdit()
        {
            string tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dns_sd_result.txt");
            
            if (File.Exists(tempPath))
            {
                // ファイルがあるとき
                string text = File.ReadAllText(tempPath);

                if (text.Contains("Remo-"))
                {
                    int index = text.IndexOf("Remo-");
                    int indexEnd = text.IndexOf(Environment.NewLine, index);
                    string res = text.Substring(index, indexEnd - index);
                    //MessageBox.Show(res);
                    TextBoxURL1.Text = res;
                }
                else
                {
                    System.Windows.MessageBox.Show("ネイチャーリモを見つけることができませんでした");
                }
            }
            else
            {
                // ファイルがないとき
                System.Windows.MessageBox.Show("検索できませんでした");
            }
        }

        // ========================================
        // settingsを保存する処理
        // ========================================
        private void SaveButton_Click(object sender,RoutedEventArgs e)
        {
            Properties.Settings.Default.URL1 = TextBoxURL1.Text.Trim();
            Properties.Settings.Default.NatureRemoCloudToken =
                PasswordBoxNatureRemoCloudToken.Password.Trim();

            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();

            System.Windows.MessageBox.Show(
                "保存しました。",
                "設定",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ========================================
        // MainWindowの背景色を変更する処理
        // ========================================
        private void BackgroundColorButton_Click(object sender,RoutedEventArgs e)
        {
            using (WinForms.ColorDialog dialog =
                   new WinForms.ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.AnyColor = true;

                if (dialog.ShowDialog() !=
                    WinForms.DialogResult.OK)
                {
                    return;
                }

                MediaColor selectedColor =
                    MediaColor.FromArgb(
                        dialog.Color.A,
                        dialog.Color.R,
                        dialog.Color.G,
                        dialog.Color.B);

                // MainWindowへすぐ反映
                if (Owner is MainWindow mainWindow)
                {
                    mainWindow.ChangeBackgroundColor(selectedColor);
                }

                // ユーザー設定へ保存
                string colorCode =
                    $"#{selectedColor.A:X2}" +
                    $"{selectedColor.R:X2}" +
                    $"{selectedColor.G:X2}" +
                    $"{selectedColor.B:X2}";

                Properties.Settings.Default.BackgroundColor =
                    colorCode;

                Properties.Settings.Default.Save();
                PersistentAppSettings.SaveFromProperties();
            }
        }

        // ========================================
        // MyData.dbのバックアップをエクスポートする処理
        // ========================================
        private void ExportBackupButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            WinForms.SaveFileDialog dialog =
                new WinForms.SaveFileDialog
                {
                    Filter =
                        "N-Remoバックアップ (*.nremo)|*.nremo|" +
                        "ZIPファイル (*.zip)|*.zip",

                    FileName =
                        $"N-Remo_Backup_" +
                        $"{DateTime.Now:yyyyMMdd_HHmmss}.nremo"
                };

            if (dialog.ShowDialog() != WinForms.DialogResult.OK)
                return;

            try
            {
                // 保存ボタンを押していない入力中の値も
                // バックアップへ含める。
                if (!SaveEditableSettingsFromControls(
                        showValidationMessage: true))
                {
                    return;
                }

                mainWindow.ExportApplicationBackup(
                    dialog.FileName);

                System.Windows.MessageBox.Show(
                    "データベースと設定をエクスポートしました。",
                    "バックアップ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"エクスポートに失敗しました。\n{ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ClearAllButtonsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBoxResult result = System.Windows.MessageBox.Show(
                LanguageManager.Get("すべてのボタンを削除します。この操作は元に戻せません。続行しますか？"),
                LanguageManager.Get("データのクリア"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                int deleted = new ButtonDatabase().ClearAllButtons();
                mainWindow.LoadAllButtons();
                if (Window.GetWindow(this) is Edit edit)
                    edit.ReloadAfterImport();

                System.Windows.MessageBox.Show(
                    string.Format(
                        LanguageManager.Get("すべてのボタンを削除しました。（{0}件）"),
                        deleted),
                    LanguageManager.Get("データのクリア"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowClearError(ex);
            }
        }

        private async void ClearSensorLogsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBoxResult result = System.Windows.MessageBox.Show(
                LanguageManager.Get("すべてのセンサーログを削除します。この操作は元に戻せません。続行しますか？"),
                LanguageManager.Get("データのクリア"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                ServerSensorSettings settings =
                    new ButtonDatabase().LoadServerSensorSettings();
                SensorLogService.ClearAll(settings);
                if (Window.GetWindow(this) is Edit edit)
                    await edit.ReloadSensorLogsAfterClearAsync();

                System.Windows.MessageBox.Show(
                    LanguageManager.Get("センサーログを削除しました。"),
                    LanguageManager.Get("データのクリア"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowClearError(ex);
            }
        }

        private void ClearAllDataButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBoxResult result = System.Windows.MessageBox.Show(
                LanguageManager.Get("ボタン、設定、認証情報、コマンド、センサー、オートメーション、センサーログをすべて削除して初期状態へ戻します。この操作は元に戻せません。続行しますか？"),
                LanguageManager.Get("すべてのデータをクリア"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                IsClearingAllData = true;
                mainWindow.PrepareForFullDataReset();

                ButtonDatabase database = new();
                ServerSensorSettings logSettings =
                    database.LoadServerSensorSettings();

                SensorLogService.ClearAll(logSettings);
                database.ClearAllData();

                PersistentAppSettings.ResetToDefaults();
                WindowsStartupManager.Apply(false);

                suppressSettingsSaveOnClose = true;
                System.Windows.MessageBox.Show(
                    LanguageManager.Get("すべてのデータを初期化しました。初期状態を確実に反映するため、N-Remoを終了します。"),
                    LanguageManager.Get("データのクリア"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                ShowClearError(ex);
                // 一部だけ削除された状態で終了時の自動保存が走ると、
                // 編集前のデータが復活するため、全消去フラグを保ったまま終了する。
                System.Windows.Application.Current.Shutdown();
            }
        }

        private static void ShowClearError(Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"{LanguageManager.Get("データの削除に失敗しました。")}\n{ex.Message}",
                LanguageManager.Get("エラー"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        // ========================================
        // MyData.dbのインポート
        // ========================================
        private async void ImportBackupButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            WinForms.OpenFileDialog dialog =
                new WinForms.OpenFileDialog
                {
                    Filter =
                        "N-Remoバックアップ (*.nremo;*.zip)|" +
                        "*.nremo;*.zip"
                };

            if (dialog.ShowDialog() != WinForms.DialogResult.OK)
                return;

            System.Windows.MessageBoxResult result =
                System.Windows.MessageBox.Show(
                    "現在のボタンデータと設定を置き換えますか？",
                    "インポート",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            // インポート前の画面内容で、復元したDBを終了時に上書きしない。
            suppressSettingsSaveOnClose = true;

            try
            {
                await mainWindow.ImportApplicationBackupAsync(
                    dialog.FileName);

                System.Windows.MessageBox.Show(
                    "データベースと設定をインポートしました。",
                    "インポート",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Close時にインポート前の入力欄で
                // 復元済み設定を上書きしない。
                mainWindow.CloseEditWindow();

                // 単独ウインドウとして使用している旧経路だけ閉じる。
                // Editへ統合済みの場合は、Edit側の終了処理に任せる。
                if (!isHostedInEdit)
                    Close();

                // Settingが閉じた後の描画タイミングで再反映
                _ = mainWindow.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        Properties.Settings.Default.Reload();

                        mainWindow.LoadBackgroundColor();
                        mainWindow.UpdateLayout();
                    }),
                    System.Windows.Threading
                        .DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                suppressSettingsSaveOnClose = false;
                System.Windows.MessageBox.Show(
                    $"インポートに失敗しました。\n{ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ========================================
        // ホットキー設定が変更されたときに即座に反映する処理
        // ========================================
        private void HotkeySetting_Changed(
    object sender,
    RoutedEventArgs e)
        {
            if (isLoadingSettings ||
                !IsSettingsUiReady)
            {
                return;
            }

            ApplyHotkeySettingsImmediately();
        }
        
        // ========================================
        // Setting画面のコントロールを最新の設定値に更新する処理
        // ========================================
        private void ReloadSettingControls()
        {
            try
            {
                isLoadingSettings = true;

                TextBoxURL1.Text =
                    Properties.Settings.Default.URL1;

                PasswordBoxNatureRemoCloudToken.Password =
                    Properties.Settings.Default.NatureRemoCloudToken;

                PasswordBoxSwitchBotToken.Password =
                    Properties.Settings.Default.SwitchBotToken;

                PasswordBoxSwitchBotSecret.Password =
                    Properties.Settings.Default.SwitchBotSecret;

                TextBoxWidth.Text =
                    Properties.Settings.Default
                        .MainWindowWidth.ToString();

                TextBoxHeight.Text =
                    Properties.Settings.Default
                        .MainWindowHeight.ToString();

                CheckBoxCloseToTray.IsChecked =
                    Properties.Settings.Default.CloseToTray;

                CheckBoxStartWithWindows.IsChecked =
                    Properties.Settings.Default.StartWithWindows;

                CheckBoxStartMinimizedToTray.IsChecked =
                    Properties.Settings.Default.StartMinimizedToTray;
                UpdateStartMinimizedAvailability();

                CheckBoxCloseWhenDeactivated.IsChecked =
                    Properties.Settings.Default.CloseWhenDeactivated;

                CheckBoxCloseAfterButtonClick.IsChecked =
                    Properties.Settings.Default
                        .CloseAfterButtonClick;

                CheckBoxMouseGestureEnabled.IsChecked =
                    Properties.Settings.Default
                        .MouseGestureEnabled;

                ComboBoxMouseCorner.SelectedValue =
                    Properties.Settings.Default
                        .MouseGestureCorner;

                ComboBoxMouseGesture.SelectedValue =
                    Properties.Settings.Default
                        .MouseGestureAction;
            }
            finally
            {
                isLoadingSettings = false;
            }
        }

        // ========================================
        // MainWindowのウィンドウサイズを変更する処理
        // ========================================
        private void ApplyWindowSizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(
            TextBoxWidth.Text,
            out double width))
            {
                System.Windows.MessageBox.Show(
                    "Widthには数値を入力してください。",
                    "入力エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                TextBoxWidth.Focus();
                return;
            }

            if (!double.TryParse(
                    TextBoxHeight.Text,
                    out double height))
            {
                System.Windows.MessageBox.Show(
                    "Heightには数値を入力してください。",
                    "入力エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                TextBoxHeight.Focus();
                return;
            }

            if (width < 300 || width > 10000)
            {
                System.Windows.MessageBox.Show(
                    "Widthは300～10000の範囲で入力してください。");

                TextBoxWidth.Focus();
                return;
            }

            if (height < 200 || height > 10000)
            {
                System.Windows.MessageBox.Show(
                    "Heightは200～10000の範囲で入力してください。");

                TextBoxHeight.Focus();
                return;
            }

            // MainWindowへすぐ反映
            mainWindow.ChangeWindowSize(
                width,
                height);

            // ユーザーデータへ保存
            Properties.Settings.Default.MainWindowWidth =
                width;

            Properties.Settings.Default.MainWindowHeight =
                height;

            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();

            System.Windows.MessageBox.Show(
                "ウィンドウサイズを反映して保存しました。",
                "設定",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ========================================
        // タスクトレイに閉じる設定を保存する処理
        // ========================================
        private void CheckBoxCloseToTray_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.CloseToTray = CheckBoxCloseToTray.IsChecked == true;

            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();
        }
    }
}
