using LiteDB;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Shell;
using static System.Net.Mime.MediaTypeNames;
using WinForms = System.Windows.Forms;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Linq;
using System.Threading;


namespace N_Remo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>

    // ========================================
    // 赤外線送信処理 共通クラス
    // ========================================
    public static class NatureRemoSender
    {
        private static readonly HttpClient httpClient =
            new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

        public static bool HasAssignedCommand(
            ButtonSetting? setting)
        {
            if (setting == null)
            {
                return false;
            }

            string provider = setting.Provider?.Trim() ?? "";

            return provider switch
            {
                "SwitchBot" or "NatureRemoCloud" or "Tuya" =>
                    !string.IsNullOrWhiteSpace(setting.SwitchBotDeviceId) &&
                    !string.IsNullOrWhiteSpace(setting.SwitchBotCommand),
                "SwitchBotScene" or "TuyaScene" =>
                    !string.IsNullOrWhiteSpace(setting.SwitchBotDeviceId),
                "None" => false,
                _ => !string.IsNullOrWhiteSpace(setting.Data1)
            };
        }

        public static async Task SendAsync(
            string buttonName)
        {
            ButtonDatabase database =
                new ButtonDatabase();

            ButtonSetting? setting =
                database.Load(buttonName);

            if (setting == null)
            {
                throw new InvalidOperationException(
                    $"{buttonName}の設定がありません。");
            }

            if (string.Equals(
                setting.SwitchBotCommandType,
                "aircon-control",
                StringComparison.OrdinalIgnoreCase))
            {
                SwitchBotCommandItem panelCommand = new()
                {
                    Id = $"{setting.Provider}|aircon-control|" +
                        setting.SwitchBotDeviceId,
                    Provider = setting.Provider,
                    SourceDisplay = string.Equals(
                        setting.Provider,
                        "NatureRemoCloud",
                        StringComparison.OrdinalIgnoreCase)
                            ? "Nature Remoコマンド"
                            : "SwitchBotコマンド",
                    DeviceId = setting.SwitchBotDeviceId,
                    DeviceName = setting.SwitchBotDeviceName,
                    DeviceType = "Air Conditioner",
                    Command = setting.SwitchBotCommand,
                    Parameter = setting.SwitchBotParameter,
                    CommandType = setting.SwitchBotCommandType,
                    Description = "エアコン操作パネル"
                };
                AirConditionerControlWindow.ShowForCommand(
                    panelCommand,
                    System.Windows.Application.Current?.MainWindow);
                return;
            }

            if (string.Equals(
                    setting.Provider,
                    "SwitchBot",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(setting.SwitchBotDeviceId) ||
                    string.IsNullOrWhiteSpace(setting.SwitchBotCommand))
                {
                    throw new InvalidOperationException(
                        $"{buttonName}のSwitchBot設定が不完全です。");
                }

                await SwitchBotApiClient.SendCommandAsync(
                    setting.SwitchBotDeviceId,
                    setting.SwitchBotCommand,
                    setting.SwitchBotParameter,
                    setting.SwitchBotCommandType);
                return;
            }

            if (string.Equals(
                    setting.Provider,
                    "SwitchBotScene",
                    StringComparison.OrdinalIgnoreCase))
            {
                await SwitchBotApiClient.SendSceneAsync(
                    setting.SwitchBotDeviceId);
                return;
            }

            if (string.Equals(
                    setting.Provider,
                    "NatureRemoCloud",
                    StringComparison.OrdinalIgnoreCase))
            {
                await NatureRemoCloudApiClient.SendCommandAsync(
                    setting.SwitchBotDeviceId,
                    setting.SwitchBotCommand,
                    setting.SwitchBotCommandType,
                    setting.SwitchBotParameter);
                return;
            }

            if (string.Equals(
                    setting.Provider,
                    "Tuya",
                    StringComparison.OrdinalIgnoreCase))
            {
                await TuyaApiClient.SendCommandAsync(
                    setting.SwitchBotDeviceId,
                    setting.SwitchBotCommand,
                    setting.SwitchBotParameter,
                    setting.SwitchBotCommandType);
                return;
            }

            if (string.Equals(
                    setting.Provider,
                    "TuyaScene",
                    StringComparison.OrdinalIgnoreCase))
            {
                await TuyaApiClient.SendSceneAsync(
                    setting.SwitchBotDeviceId);
                return;
            }

            if (string.Equals(
                    setting.Provider,
                    "None",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{buttonName}には操作が割り当てられていません。");
            }

            string irData =
                setting.Data1?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(irData))
            {
                throw new InvalidOperationException(
                    $"{buttonName}に赤外線情報がありません。");
            }

            string url =
                Properties.Settings.Default.URL1
                    ?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException(
                    "Nature RemoのURLが設定されていません。");
            }

            // IPアドレスだけ保存されている場合
            if (!url.StartsWith(
                    "http://",
                    StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith(
                    "https://",
                    StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }

            url = url.TrimEnd('/');

            if (!url.EndsWith(
                    "/messages",
                    StringComparison.OrdinalIgnoreCase))
            {
                url += "/messages";
            }

            if (!Uri.TryCreate(
                    url,
                    UriKind.Absolute,
                    out Uri? uri))
            {
                throw new InvalidOperationException(
                    $"URLが正しくありません。\n{url}");
            }

            using HttpRequestMessage request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    uri);

            request.Headers.TryAddWithoutValidation(
                "X-Requested-With",
                "local");

            request.Content =
                new StringContent(
                    irData,
                    Encoding.UTF8,
                    "application/json");

            using HttpResponseMessage response =
                await httpClient.SendAsync(request);

            string responseText =
                await response.Content
                    .ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"赤外線送信に失敗しました。\n" +
                    $"HTTP: {(int)response.StatusCode}\n" +
                    responseText);
            }
        }
    }

    // ========================================
    // メインウィンドウ
    // ========================================
    public partial class MainWindow : Window
    {
        private Edit? editWindow;
        private readonly ButtonDatabase database;
        private System.Windows.Controls.Button? draggingButton;
        private System.Windows.Point mouseOffset;
        private WinForms.NotifyIcon? notifyIcon;
        private bool isReallyClosing;
        private bool isResettingAllData;
        private bool isImportingApplicationBackup;
        private bool isRuntimeModalDialogOpen;
        private bool balloonTipShown;
        private DateTime closeWhenDeactivatedReadyAt = DateTime.MaxValue;
        private readonly DispatcherTimer serverSensorTimer = new();
        private CancellationTokenSource? webhookRealtimeCancellation;
        private readonly DispatcherTimer automationScheduleTimer = new()
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        // 同じルールでも条件が一度解除されると期限が変わる。
        // ルールIDだけで予約を管理すると古い予約が新しい予約を妨げるため、
        // 現在有効な期限も一緒に保持する。
        private readonly Dictionary<int, DateTime> scheduledAutomationContinuationChecks = new();
        private bool isRefreshingServerSensors;
        private string webhookRealtimeStatus = "停止中";
        private readonly HashSet<string> serverSensorAssignedButtonNames =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<(string Title, string Message,
            WinForms.ToolTipIcon Icon)> automationNotificationQueue = new();
        private bool isShowingAutomationNotifications;
        private readonly SemaphoreSlim automationEvaluationLock = new(1, 1);

        private const int HotkeyId = 1001;
        private const int WmHotkey = 0x0312;

        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWin = 0x0008;
        private const uint ModNoRepeat = 0x4000;

        private HwndSource? hwndSource;
        private IntPtr windowHandle;

        private uint currentModifiers;
        private Key currentKey = Key.None;
        private bool hotkeyRegistered;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(
            IntPtr hWnd,
            int id,
            uint fsModifiers,
            uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(
            IntPtr hWnd,
            int id);

        private int draggingOriginalZIndex;
        private bool hasDraggingButtonMoved;

        private MouseGestureMonitor?
        mouseGestureMonitor;



        public MainWindow()
        {
            InitializeComponent();

            Title = "N-Remo";

            SourceInitialized += MainWindow_SourceInitialized;

            InitializeNotifyIcon();

            database = new ButtonDatabase();

            database.MigrateButtonIds();

            Loaded += MainWindow_Loaded;
            Deactivated += MainWindow_Deactivated;
            Activated += (_, _) =>
                closeWhenDeactivatedReadyAt = DateTime.UtcNow.AddSeconds(1);

            serverSensorTimer.Tick += async (_, _) =>
                await RefreshServerSensorsAsync();
            automationScheduleTimer.Tick += async (_, _) =>
                await CheckScheduledAutomationsAsync();

            LoadBackgroundColor();
            LoadWindowSize();
            LoadWindowPosition();

            mouseGestureMonitor =
                new MouseGestureMonitor(
                    ShowMainWindowFromMouseGesture);

            if (Properties.Settings.Default
                .MouseGestureEnabled)
            {
                mouseGestureMonitor.Start();
            }

        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            if (!Properties.Settings.Default.CloseWhenDeactivated ||
                !IsVisible || isReallyClosing ||
                editWindow?.IsVisible == true ||
                DateTime.UtcNow < closeWhenDeactivatedReadyAt)
                return;

            // メニューからEdit画面を開いたときは、アプリ内での画面切替なので閉じない。
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsActive || !IsVisible || isReallyClosing ||
                    editWindow?.IsVisible == true)
                    return;

                Close();
            }), DispatcherPriority.ApplicationIdle);
        }


        // ========================================
        // インポートした後に閉じる
        // ========================================
        public void CloseEditWindow()
        {
            if (editWindow == null)
                return;

            editWindow.Close();

            // Closedイベントでもnullにしている場合は
            // ここでの代入はなくても構いません
            editWindow = null;
        }

        // ========================================
        // マウスジェスチャーでMainWindowを表示する
        // ========================================
        private void ShowMainWindowFromMouseGesture()
        {
            Dispatcher.BeginInvoke(() =>
            {
                // トレイ格納中だけ表示
                if (IsVisible)
                    return;

                Show();

                WindowState =
                    WindowState.Normal;

                Activate();

                Topmost = true;
                Topmost = false;

                Focus();
            });
        }

        // ========================================
        // マウスジェスチャーの設定を再読み込みする
        // ========================================
        public void ReloadMouseGestureSettings()
        {
            if (mouseGestureMonitor == null)
                return;

            mouseGestureMonitor.Stop();

            if (Properties.Settings.Default
                .MouseGestureEnabled)
            {
                mouseGestureMonitor.Start();
            }
        }


        // ========================================
        // 閉じるボタンを押したときの処理
        // ========================================
        private void CloseAfterButtonClick()
        {
            bool closeToTray =
                Properties.Settings.Default.CloseToTray;

            if (closeToTray)
            {
                // タスクトレイへ格納
                Hide();

                if (notifyIcon != null)
                {
                    notifyIcon.Visible = true;
                }

                return;
            }

            // 完全終了
            isReallyClosing = true;

            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                notifyIcon = null;
            }

            System.Windows.Application.Current.Shutdown();
        }

        internal void DevicePanelCommandCompleted()
        {
            _ = RefreshServerSensorsAfterCommandAsync();

            if (Properties.Settings.Default.CloseAfterButtonClick)
                CloseAfterButtonClick();
        }

        // ========================================
        // ボタンを削除する処理
        // ========================================
        public bool DeleteButton(string buttonName)
        {
            System.Windows.Controls.Button? button = GetButton(buttonName);

            if (button == null)
                return false;

            MainCanvas.Children.Remove(button);
            database.Delete(buttonName);

            return true;
        }

        // ========================================
        // ボタンを追加する処理
        // ========================================
        public ButtonSetting AddNewButton()
        {
            string buttonName = CreateNextButtonName();

            int maximumZIndex = MainCanvas.Children
                .OfType<UIElement>()
                .Select(System.Windows.Controls.Panel.GetZIndex)
                .DefaultIfEmpty(0)
                .Max();

            ButtonSetting setting = new ButtonSetting
            {
                Name = buttonName,
                NameContent = "新しいボタン",
                IsVisible = true,

                Left = 20,
                Top = 20,

                Width = 90,
                Height = 35,

                FontSize = 12,
                FontColor = "#FF000000",
                FontWeight = "Normal",
                BackgroundColor = "#FFE0E0E0",

                HorizontalAlignment = "Center",
                VerticalAlignment = "Center",

                IsMouseOverEffectEnabled = true,
                ZIndex = maximumZIndex + 1,

                Data1 = "",
                Data2 = "",
                Data3 = ""
            };

            database.Save(setting);
            CreateButton(setting);

            NotifyButtonStateChanged(setting.Name);

            return setting;
        }

        public ButtonSetting AddDevicePanel(SwitchBotCommandItem command)
        {
            string deviceType = command.DeviceType?.Trim() ?? "";
            string panelType = deviceType.Contains("air conditioner",
                    StringComparison.OrdinalIgnoreCase) ||
                deviceType.Equals("AC", StringComparison.OrdinalIgnoreCase)
                    ? "AirConditioner"
                    : deviceType.Contains("tv", StringComparison.OrdinalIgnoreCase) ||
                      deviceType.Contains("television", StringComparison.OrdinalIgnoreCase)
                        ? "TV"
                    : deviceType.Contains("plug", StringComparison.OrdinalIgnoreCase) ||
                      command.CommandType.Equals("Boolean",
                          StringComparison.OrdinalIgnoreCase)
                        ? "Plug"
                        : "";
            if (string.IsNullOrWhiteSpace(panelType))
            {
                throw new InvalidOperationException(
                    "現在、専用パネルを追加できるのはエアコン、テレビ、プラグです。");
            }

            ButtonSetting? existing = database.LoadAll().FirstOrDefault(setting =>
                string.Equals(setting.ItemType, "DevicePanel",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(setting.Provider, command.Provider,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(setting.SwitchBotDeviceId, command.DeviceId,
                    StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                throw new InvalidOperationException(
                    $"{command.DeviceName}のパネルは既に追加されています。");
            }

            int maximumZIndex = MainCanvas.Children.OfType<UIElement>()
                .Select(System.Windows.Controls.Panel.GetZIndex)
                .DefaultIfEmpty(0).Max();
            string buttonName = CreateNextButtonName();
            ButtonSetting setting = new()
            {
                Name = buttonName,
                NameContent = string.IsNullOrWhiteSpace(command.DeviceName)
                    ? "デバイス"
                    : command.DeviceName,
                ItemType = "DevicePanel",
                PanelType = panelType,
                PanelTheme = "Light",
                PanelAirPower = "on",
                PanelAirMode = "cool",
                PanelAirTemperature = 26,
                PanelAirFan = "",
                IsVisible = true,
                Left = 20,
                Top = 20,
                Width = panelType == "AirConditioner" ? 310 :
                    panelType == "TV" ? 310 : 220,
                Height = panelType == "AirConditioner" ? 255 :
                    panelType == "TV" ? 490 : 150,
                FontSize = 12,
                FontColor = "#FF000000",
                FontWeight = "Normal",
                BackgroundColor = "#FFE0E0E0",
                HorizontalAlignment = "Center",
                VerticalAlignment = "Center",
                IsMouseOverEffectEnabled = false,
                ZIndex = maximumZIndex + 1,
                Provider = command.Provider,
                SwitchBotDeviceId = command.DeviceId,
                SwitchBotDeviceName = command.DeviceName,
                SwitchBotCommand = command.Command,
                SwitchBotParameter = command.Parameter,
                SwitchBotCommandType = command.CommandType
            };
            database.Save(setting);
            CreateButton(setting);
            NotifyButtonStateChanged(setting.Name);
            return setting;
        }

        public void RefreshDevicePanelDefinitions(
            IEnumerable<SwitchBotCommandItem> commands)
        {
            Dictionary<string, SwitchBotCommandItem> definitions = commands
                .Where(command => !string.IsNullOrWhiteSpace(command.DeviceId))
                .GroupBy(command => $"{command.Provider}|{command.DeviceId}",
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group =>
                    group.FirstOrDefault(command => string.Equals(
                        command.CommandType,
                        "aircon-control",
                        StringComparison.OrdinalIgnoreCase)) ?? group.First(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (ButtonSetting panel in database.LoadAll().Where(setting =>
                string.Equals(setting.ItemType, "DevicePanel",
                    StringComparison.OrdinalIgnoreCase)))
            {
                string key = $"{panel.Provider}|{panel.SwitchBotDeviceId}";
                if (!definitions.TryGetValue(key, out SwitchBotCommandItem? command))
                    continue;
                panel.SwitchBotCommand = command.Command;
                panel.SwitchBotParameter = command.Parameter;
                panel.SwitchBotCommandType = command.CommandType;
                database.Save(panel);
                ApplyButtonSetting(panel);
            }
        }

        // ========================================
        // ボタン名を自動生成する処理
        // ========================================
        private string CreateNextButtonName()
        {
            int maximumNumber =
                database.LoadAll()
                    .Select(setting =>
                    {
                        string id =
                            setting.Name.Trim();

                        if (id.StartsWith(
                            "Button",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            id = id["Button".Length..];
                        }
                        else if (id.StartsWith(
                            "No",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            id = id["No".Length..];
                        }

                        return int.TryParse(
                            id,
                            out int number)
                                ? number
                                : 0;
                    })
                    .DefaultIfEmpty(0)
                    .Max();

            return $"No{maximumNumber + 1:000}";
        }

        // ========================================
        // ボタンを作成してCanvasに追加
        // ========================================
        private System.Windows.Controls.Button CreateButton(ButtonSetting setting)
        {
            System.Windows.Controls.Button button =
                new System.Windows.Controls.Button
                {
                    Name = setting.Name,
                    Tag = setting.Name,
                    Content = setting.NameContent,

                    Width =
                        setting.Width > 0
                            ? setting.Width
                            : 90,

                    Height =
                        setting.Height > 0
                            ? setting.Height
                            : 35,

                    FontSize =
                        setting.FontSize > 0
                            ? setting.FontSize
                            : 12,

                    Style =
                        TryFindResource(
                            "RoundedButtonStyle")
                        as Style
                };

            button.PreviewMouseLeftButtonDown +=
                Button_MouseLeftButtonDown;

            button.PreviewMouseMove +=
                Button_MouseMove;

            button.PreviewMouseLeftButtonUp +=
                Button_MouseLeftButtonUp;

            MainCanvas.Children.Add(button);

            ApplySettingToButton(
                button,
                setting);

            return button;
        }

        // ========================================
        // DataGridの編集内容をButtonへ反映して保存
        // ========================================
        public void ApplyAndSaveButtonSetting(
            ButtonSetting setting)
        {
            if (GetButton(setting.Name) == null)
            {
                throw new InvalidOperationException(
                    $"Buttonが見つかりません: {setting.Name}");
            }

            if (double.IsNaN(setting.Width) ||
                double.IsInfinity(setting.Width) ||
                setting.Width <= 0)
            {
                throw new InvalidOperationException(
                    "幅は0より大きい数値にしてください。");
            }

            if (double.IsNaN(setting.Height) ||
                double.IsInfinity(setting.Height) ||
                setting.Height <= 0)
            {
                throw new InvalidOperationException(
                    "高さは0より大きい数値にしてください。");
            }

            if (double.IsNaN(setting.FontSize) ||
                double.IsInfinity(setting.FontSize) ||
                setting.FontSize <= 0)
            {
                throw new InvalidOperationException(
                    "フォントサイズは0より大きい数値にしてください。");
            }

            // MainWindowのButtonへ反映
            ApplyButtonSetting(setting);

            // Data1などを含めてそのまま保存
            database.Save(setting);

            NotifyButtonStateChanged(
                setting.Name);
        }

        // ========================================
        // Button状態が変化したことをEditへ通知
        // ========================================
        public event Action<string>?
            ButtonStateChanged;

        public void NotifyButtonStateChanged(
            string buttonName)
        {
            ButtonStateChanged?.Invoke(
                buttonName);
            ApplyServerSensorValues();
        }

        // ========================================
        // マウスオーバーエフェクトのON/OFF
        // ========================================
        public bool? ToggleMouseOverEffect(string buttonName)
        {
            System.Windows.Controls.Button? button = GetButton(buttonName);

            if (button == null)
                return null;

            // RoundedButtonStyleが外されていた場合は戻す
            if (TryFindResource("RoundedButtonStyle") is Style roundedStyle &&
                button.Style != roundedStyle)
            {
                button.Style = roundedStyle;
            }

            bool newValue =
                !ButtonEffects.GetIsMouseOverEffectEnabled(button);

            ButtonEffects.SetIsMouseOverEffectEnabled(
                button,
                newValue);

            // LiteDBへ保存
            SaveButtonSetting(buttonName);

            // DataGridButtonsを更新
            NotifyButtonStateChanged(buttonName);

            button.InvalidateVisual();

            return newValue;
        }

        // ========================================
        // ボタンのStyleをON/OFFする
        // ========================================
        public bool? ToggleButtonStyle(
            string buttonName)
        {
            System.Windows.Controls.Button? button =
                GetButton(buttonName);

            if (button == null)
            {
                return null;
            }

            Style? roundedStyle =
                TryFindResource(
                    "RoundedButtonStyle") as Style;

            if (roundedStyle == null)
            {
                throw new InvalidOperationException(
                    "RoundedButtonStyleが見つかりません。");
            }

            bool styleIsEnabled =
                ReferenceEquals(
                    button.Style,
                    roundedStyle);

            if (styleIsEnabled)
            {
                // StyleをOFF
                button.ClearValue(
                    FrameworkElement.StyleProperty);

                return false;
            }
            else
            {
                // StyleをON
                button.Style = roundedStyle;

                return true;
            }
        }

        // ========================================
        // 指定したボタンを最前面にする
        // ========================================
        public bool BringButtonToFront(
            string buttonName)
        {
            System.Windows.Controls.Button? targetButton =
                GetButton(buttonName);

            if (targetButton == null)
            {
                return false;
            }

            int maximumZIndex = 0;

            foreach (UIElement element
                in MainCanvas.Children)
            {
                int zIndex =
                    System.Windows.Controls.Panel.GetZIndex(element);

                if (zIndex > maximumZIndex)
                {
                    maximumZIndex = zIndex;
                }
            }

            System.Windows.Controls.Panel.SetZIndex(
                targetButton,
                maximumZIndex + 1);

            SaveButtonSetting(buttonName);

            return true;
        }

        // ========================================
        // グローバルホットキーの解除とタスクトレイの破棄
        // ========================================
        protected override void OnClosed(EventArgs e)
        {
            serverSensorTimer.Stop();
            webhookRealtimeCancellation?.Cancel();
            webhookRealtimeCancellation?.Dispose();
            webhookRealtimeCancellation = null;
            automationScheduleTimer.Stop();
            DisableGlobalHotkey();

            if (hwndSource != null)
            {
                hwndSource.RemoveHook(
                    WindowMessageHook);

                hwndSource = null;
            }

            editWindow?.Close();

            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                notifyIcon = null;
            }

            base.OnClosed(e);
        }

        // ========================================
        // グローバルホットキー表示・非表示の切り替え
        // ========================================
        private void ToggleMainWindow()
        {
            if (IsVisible &&
                WindowState != WindowState.Minimized)
            {
                Hide();
            }
            else
            {
                Show();

                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }

                Activate();

                Topmost = true;
                Topmost = false;

                Focus();
            }
        }

        // ========================================
        // ホットキーが押されたときの処理
        // ========================================
        private IntPtr WindowMessageHook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
        {
            if (message == WmHotkey &&
                wParam.ToInt32() == HotkeyId)
            {
                ToggleMainWindow();

                handled = true;
            }

            return IntPtr.Zero;
        }

        // ========================================
        // グローバルホットキーの解除
        // ========================================
        public void DisableGlobalHotkey()
        {
            if (!hotkeyRegistered ||
                windowHandle == IntPtr.Zero)
            {
                return;
            }

            UnregisterHotKey(
                windowHandle,
                HotkeyId);

            hotkeyRegistered = false;
            currentModifiers = 0;
            currentKey = Key.None;
        }

        // ========================================
        // グローバルホットキーの登録
        // ========================================
        public bool TryApplyGlobalHotkey(uint modifiers,Key key,out string errorMessage)
        {
            errorMessage = "";

            if (windowHandle == IntPtr.Zero)
            {
                errorMessage =
                    "MainWindowの準備が完了していません。";

                return false;
            }

            if (key == Key.None)
            {
                errorMessage =
                    "キーを選択してください。";

                return false;
            }

            uint oldModifiers = currentModifiers;
            Key oldKey = currentKey;
            bool wasRegistered = hotkeyRegistered;

            if (hotkeyRegistered)
            {
                UnregisterHotKey(
                    windowHandle,
                    HotkeyId);

                hotkeyRegistered = false;
            }

            uint virtualKey =
                (uint)KeyInterop.VirtualKeyFromKey(key);

            bool result = RegisterHotKey(
                windowHandle,
                HotkeyId,
                modifiers | ModNoRepeat,
                virtualKey);

            if (!result)
            {
                // 新しい登録に失敗したら以前の登録へ戻す
                if (wasRegistered &&
                    oldKey != Key.None)
                {
                    uint oldVirtualKey =
                        (uint)KeyInterop
                            .VirtualKeyFromKey(oldKey);

                    hotkeyRegistered =
                        RegisterHotKey(
                            windowHandle,
                            HotkeyId,
                            oldModifiers | ModNoRepeat,
                            oldVirtualKey);
                }

                errorMessage =
                    "ホットキーを登録できませんでした。\n" +
                    "ほかのアプリですでに使用されている可能性があります。";

                return false;
            }

            currentModifiers = modifiers;
            currentKey = key;
            hotkeyRegistered = true;

            return true;
        }

        // ========================================
        // グローバルホットキーの読み込み
        // ========================================
        private void LoadGlobalHotkey()
        {
            if (!Properties.Settings.Default.HotkeyEnabled)
            {
                return;
            }

            string savedKey =
                Properties.Settings.Default.HotkeyKey;

            if (!Enum.TryParse(
                    savedKey,
                    out Key key) ||
                key == Key.None)
            {
                return;
            }

            uint modifiers =
                (uint)Properties.Settings.Default
                    .HotkeyModifiers;

            TryApplyGlobalHotkey(
                modifiers,
                key,
                out _);
        }

        // ========================================
        // グローバルホットキー 初期化処理
        // ========================================
        private void MainWindow_SourceInitialized(object? sender,EventArgs e)
        {
            WindowInteropHelper helper =
                new WindowInteropHelper(this);

            windowHandle = helper.Handle;

            hwndSource =
                HwndSource.FromHwnd(windowHandle);

            hwndSource?.AddHook(WindowMessageHook);

            LoadGlobalHotkey();
        }

        // ========================================
        // タスクトレイを初期化
        // ========================================
        private void InitializeNotifyIcon()
        {
            notifyIcon = new WinForms.NotifyIcon
            {
                Text = "N-Remo",
                Visible = true
            };

            string executablePath =
                Process.GetCurrentProcess()
                    .MainModule?.FileName ?? "";

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                System.Drawing.Icon? appIcon =
                    System.Drawing.Icon.ExtractAssociatedIcon(
                        executablePath);

                if (appIcon != null)
                {
                    notifyIcon.Icon = appIcon;
                }
            }

            WinForms.ContextMenuStrip trayMenu =
                new WinForms.ContextMenuStrip();

            WinForms.ToolStripMenuItem showMenuItem =
                new WinForms.ToolStripMenuItem("表示");

            showMenuItem.Click += (_, _) =>
            {
                Dispatcher.Invoke(ShowMainWindow);
            };

            WinForms.ToolStripMenuItem exitMenuItem =
                new WinForms.ToolStripMenuItem("終了");

            exitMenuItem.Click += (_, _) =>
            {
                Dispatcher.Invoke(ExitApplication);
            };

            trayMenu.Items.Add(showMenuItem);
            trayMenu.Items.Add(new WinForms.ToolStripSeparator());
            trayMenu.Items.Add(exitMenuItem);

            notifyIcon.ContextMenuStrip = trayMenu;

            // ダブルクリックでMainWindowを表示
            notifyIcon.DoubleClick += (_, _) =>
            {
                Dispatcher.Invoke(ShowMainWindow);
            };
        }

        // ========================================
        // タスクトレイを初期化 ×ボタンでタスクトレイへ格納
        // ========================================
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!isResettingAllData)
                SaveWindowPosition();

            bool closeToTray =
                Properties.Settings.Default.CloseToTray;

            if (closeToTray && !isReallyClosing)
            {
                // 終了をキャンセル
                e.Cancel = true;

                // MainWindowをトレイへ格納する際は、
                // 開いている設定・ボタン編集ウィンドウも閉じる。
                CloseEditWindow();

                // MainWindowを非表示
                Hide();

                if (!balloonTipShown &&
                    notifyIcon != null)
                {
                    notifyIcon.ShowBalloonTip(
                        3000,
                        "N-Remo",
                        "N-Remoはタスクトレイで動作しています。",
                        WinForms.ToolTipIcon.Info);

                    balloonTipShown = true;
                }

                return;
            }

            base.OnClosing(e);
        }

        public void PrepareForFullDataReset()
        {
            isResettingAllData = true;
            isReallyClosing = true;
        }

        // ========================================
        // タスクトレイを初期化 MainWindowを再表示
        // ========================================
        private void ShowMainWindow()
        {
            Show();

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        // ========================================
        // トレイメニューから完全終了
        // ========================================
        private void ExitApplication()
        {
            isReallyClosing = true;

            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
            }

            mouseGestureMonitor?.Dispose();
            mouseGestureMonitor = null;

            Close();
        }

        // ========================================
        // 保存されたウィンドウサイズを読み込む
        // ========================================
        private void LoadWindowSize()
        {
            double savedWidth =
                Properties.Settings.Default.MainWindowWidth;

            double savedHeight =
                Properties.Settings.Default.MainWindowHeight;

            if (IsValidWindowSize(savedWidth, savedHeight))
            {
                Width = savedWidth;
                Height = savedHeight;
            }
        }

        // ========================================
        // 保存されたウィンドウ位置を読み込む
        // ========================================
        private void LoadWindowPosition()
        {
            double savedLeft = Properties.Settings.Default.MainWindowLeft;
            double savedTop = Properties.Settings.Default.MainWindowTop;

            if (!IsValidWindowPosition(savedLeft, savedTop))
                return;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = savedLeft;
            Top = savedTop;
        }

        // ========================================
        // 現在のウィンドウ位置を保存する
        // ========================================
        private void SaveWindowPosition()
        {
            if (WindowState != WindowState.Normal ||
                !IsValidWindowPosition(Left, Top))
                return;

            Properties.Settings.Default.MainWindowLeft = Left;
            Properties.Settings.Default.MainWindowTop = Top;
            Properties.Settings.Default.Save();
        }

        private bool IsValidWindowPosition(double left, double top)
        {
            if (double.IsNaN(left) || double.IsInfinity(left) ||
                double.IsNaN(top) || double.IsInfinity(top))
                return false;

            var windowBounds = new System.Drawing.Rectangle(
                (int)Math.Round(left),
                (int)Math.Round(top),
                Math.Max(1, (int)Math.Round(Width)),
                Math.Max(1, (int)Math.Round(Height)));

            return WinForms.Screen.AllScreens.Any(screen =>
            {
                System.Drawing.Rectangle intersection =
                    System.Drawing.Rectangle.Intersect(
                        windowBounds, screen.WorkingArea);
                return intersection.Width >= 50 && intersection.Height >= 50;
            });
        }

        // ========================================
        // Settingからウィンドウサイズを変更
        // ========================================
        public void ChangeWindowSize(
            double width,
            double height)
        {
            // 最大化中なら通常表示へ戻す
            if (WindowState != WindowState.Normal)
            {
                WindowState = WindowState.Normal;
            }

            Width = width;
            Height = height;
        }
        private static bool IsValidWindowSize(double width,double height)
        {
            return
                !double.IsNaN(width) &&
                !double.IsInfinity(width) &&
                !double.IsNaN(height) &&
                !double.IsInfinity(height) &&
                width >= 300 &&
                height >= 200 &&
                width <= 10000 &&
                height <= 10000;
        }

        // ========================================
        // 起動時のバックグラウンドカラーの読み込み
        // ========================================
        public void LoadBackgroundColor()
        {
            string colorCode =
                Properties.Settings.Default
                    .BackgroundColor;

            try
            {
                if (string.IsNullOrWhiteSpace(
                        colorCode))
                {
                    MainCanvas.Background =
                        System.Windows.Media.Brushes.White;

                    return;
                }

                System.Windows.Media.Color color =
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                        .ConvertFromString(colorCode);

                SolidColorBrush brush =
                    new SolidColorBrush(color);

                brush.Freeze();

                MainCanvas.Background =
                    brush;
            }
            catch
            {
                MainCanvas.Background =
                    System.Windows.Media.Brushes.White;
            }

            MainCanvas.InvalidateVisual();
        }

        // ========================================
        // バックグランドカラーの共有
        // ========================================
        public void ChangeBackgroundColor(System.Windows.Media.Color color)
        {
            MainCanvas.Background = new SolidColorBrush(color);
        }

        // ========================================
        // コマンドプロンプト実行での結果を取得するためのクラス
        // ========================================
        private class CurlResult
        {
            public int ExitCode { get; set; }
            public string Output { get; set; } = "";
            public string Error { get; set; } = "";
        }

        // ========================================
        // クリック処理 & ドラックアンドロップでボタンを移動する処理
        // ========================================
        private async void Button_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Button panelHost &&
                editWindow == null &&
                panelHost.Content is DeviceCommandPanel)
            {
                // 通常表示中はパネル内部の操作部品へクリックを渡す。
                return;
            }

            // Editが開いていない場合
            if (editWindow == null)
            {
                if (sender is not System.Windows.Controls.Button button)
                    return;

                string buttonName =
                    button.Name;

                try
                {
                    await NatureRemoSender.SendAsync(
                        buttonName);

                    _ = RefreshServerSensorsAfterCommandAsync();

                    // 赤外線送信成功後に終了
                    if (Properties.Settings.Default
                        .CloseAfterButtonClick)
                    {
                        CloseAfterButtonClick();
                    }
                }
                catch (Exception)
                {
                    //System.Windows.MessageBox.Show(
                    //    $"赤外線送信に失敗しました。\n" +
                    //    ex.Message,
                    //    "送信エラー",
                    //    MessageBoxButton.OK,
                    //    MessageBoxImage.Error);
                }

                return;
            }

            // Editが開いていた時の処理
            if (sender is System.Windows.Controls.Button clickedButton)
            {
                //System.Windows.MessageBox.Show(clickedButton.Name);
                // 編集対象を設定
                editWindow?.SetText(clickedButton.Name);

                if (sender is System.Windows.Controls.Button button)
                {
                    string buttonName =
                        button.Name;
                }

                // -----------------------------
                // DataGridの該当行を選択
                // -----------------------------

                if (editWindow != null)
                {
                    // Editが開いている
                    editWindow.SelectDataGridItem(clickedButton.Name);
                }
            }

            // ドラッグ対象
            draggingButton =
                (System.Windows.Controls.Button)sender;

            hasDraggingButtonMoved = false;

            // 必ず最前面へ移動する前に保存する
            draggingOriginalZIndex =
                System.Windows.Controls.Panel.GetZIndex(
                    draggingButton);

            // ボタン内のクリック位置
            mouseOffset =
                e.GetPosition(draggingButton);

            draggingButton.CaptureMouse();

            draggingButton.Cursor =
                System.Windows.Input.Cursors.SizeAll;

            // ドラッグ中だけ一時的に最前面
            System.Windows.Controls.Panel.SetZIndex(
                draggingButton,
                int.MaxValue);

            e.Handled = true;
        }

        // ========================================
        // ドラックアンドロップでボタンを移動する処理 2
        // ========================================
        private void Button_MouseMove(object sender,System.Windows.Input.MouseEventArgs e)
        {
            if (draggingButton == null ||
                e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
            {
                return;
            }

            System.Windows.Point mousePosition = e.GetPosition(MainCanvas);

            double newLeft = mousePosition.X - mouseOffset.X;
            double newTop = mousePosition.Y - mouseOffset.Y;

            // Canvasの外へ出ないように制限
            double maxLeft = Math.Max(
                0,
                MainCanvas.ActualWidth - draggingButton.ActualWidth);

            double maxTop = Math.Max(
                0,
                MainCanvas.ActualHeight - draggingButton.ActualHeight);

            newLeft = Math.Clamp(newLeft, 0, maxLeft);
            newTop = Math.Clamp(newTop, 0, maxTop);

            double currentLeft = Canvas.GetLeft(draggingButton);
            double currentTop = Canvas.GetTop(draggingButton);

            if (double.IsNaN(currentLeft))
                currentLeft = 0;

            if (double.IsNaN(currentTop))
                currentTop = 0;

            if (Math.Abs(newLeft - currentLeft) > 0.01 ||
                Math.Abs(newTop - currentTop) > 0.01)
            {
                hasDraggingButtonMoved = true;
            }

            Canvas.SetLeft(draggingButton, newLeft);
            Canvas.SetTop(draggingButton, newTop);
        }

        // ========================================
        // ドラックアンドロップでボタンを移動する処理 3
        // ========================================
        private void Button_MouseLeftButtonUp(object sender,MouseButtonEventArgs e)
        {
            if (draggingButton == null)
            {
                return;
            }

            draggingButton.ReleaseMouseCapture();
            draggingButton.Cursor = System.Windows.Input.Cursors.Arrow;
            

            // ドラッグ前の重なり順へ戻す
            System.Windows.Controls.Panel.SetZIndex(
                draggingButton,
                draggingOriginalZIndex);

            // 実際にドラッグ移動した時だけ位置を保存する。
            // 通常クリックではセンサー値を含むContentを保存対象にしない。
            if (hasDraggingButtonMoved)
                SaveButtonSetting(draggingButton.Name);

            draggingButton = null;
            hasDraggingButtonMoved = false;
            e.Handled = true;
        }

        // ========================================
        // コンテキストメニューの開くイベント
        // ========================================
        private void MainCanvas_ContextMenuOpening(object sender,ContextMenuEventArgs e)
        {
            // マウス直下の要素を取得
            IInputElement element = MainCanvas.InputHitTest(
                Mouse.GetPosition(MainCanvas));

            // ボタンまたはボタン内部を右クリックした場合はキャンセル
            DependencyObject? current = element as DependencyObject;

            while (current != null && current != MainCanvas)
            {
                if (current is System.Windows.Controls.Button)
                {
                    e.Handled = true;
                    return;
                }

                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
        }

        // ========================================
        // Editを開く
        // ========================================
        private void EditMenuItem_Click(
    object sender,
    RoutedEventArgs e)
        {
            if (editWindow == null)
            {
                editWindow = new Edit(this)
                {
                    Owner = this,
                    ShowInTaskbar = false,
                    WindowStartupLocation =
                        WindowStartupLocation.Manual
                };

                editWindow.Loaded += (_, _) =>
                {
                    SetEditWindowPosition();
                };

                editWindow.Closed += (_, _) =>
                {
                    editWindow = null;

                    // Editの破棄完了後にMainWindowへ戻す
                    Dispatcher.BeginInvoke(
                        new Action(() =>
                        {
                            RestoreMainWindowFocus();
                        }),
                        DispatcherPriority.ApplicationIdle);
                };

                editWindow.Show();

                // Edit表示直後の入力フォーカスもMainWindowへ戻す。
                Activate();
                Focus();
            }
            else
            {
                SetEditWindowPosition();

                if (editWindow.WindowState ==
                    WindowState.Minimized)
                {
                    editWindow.WindowState =
                        WindowState.Normal;
                }

                editWindow.Activate();
                editWindow.Focus();
            }

            editWindow?.ShowButtonEditTab();
        }

        private void SensorLogMenuItem_Click(
            object sender, RoutedEventArgs e)
        {
            EditMenuItem_Click(sender, e);
            editWindow?.ShowSensorLogTab();
        }

        private void ExitMenuItem_Click(
            object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }

        // ========================================
        // フォーカスをMainWindowへ戻す
        // ========================================
        private void RestoreMainWindowFocus()
        {
            // タスクトレイへ格納中なら表示しない
            if (!IsVisible)
            {
                return;
            }

            if (WindowState ==
                WindowState.Minimized)
            {
                WindowState =
                    WindowState.Normal;
            }

            Activate();
            Focus();

            Keyboard.Focus(this);
        }

        // ========================================
        // フォーカスをMainWindowへ戻す
        // ========================================
        private System.Windows.Point GetMainWindowPosition()
        {
            if (WindowState ==
                WindowState.Normal)
            {
                return new System.Windows.Point(
                    Left,
                    Top);
            }

            return new System.Windows.Point(
                RestoreBounds.Left,
                RestoreBounds.Top);
        }

        // ========================================
        // Settingを開く
        // ========================================
        private void SettingMenuItem_Click(
    object sender,
    RoutedEventArgs e)
        {
            EditMenuItem_Click(sender, e);
            editWindow?.ShowSettingsTab();
        }

        // ========================================
        // 起動時にLiteDBから読み込み
        // ========================================
        private async void MainWindow_Loaded(object sender,RoutedEventArgs e)
        {
            AutomationDiagnosticLog.Write(
                "N-Remo起動。保存済みの継続条件を引き継ぐ。");
            LoadAllButtons();
            ReloadServerSensorSettingsAndRefresh(useCachedValues: true);
            ReloadWebhookRelaySettings();
            // センサー通信が長引いても、時刻条件などの定期判定を止めない。
            automationScheduleTimer.Start();
            await EvaluateSimpleAutomationsAsync();
            if (database.LoadServerSensorSettings().AutoUpdateEnabled)
            {
                // アプリが終了していた間の状態を推測で継続させない。
                // Webhook有効時も起動直後だけSwitchBot APIで現状態を確認し、
                // 保存済みの継続条件が今も成立している場合だけ実行する。
                // 全サービスの更新を直列で待つと、Nature RemoやTuyaの応答待ちで
                // SwitchBot条件の確認まで到達しないことがある。起動時の継続判定は
                // 対象のSwitchBotだけを先に取得する。
                await RefreshServerSensorsAsync(
                    forceSwitchBotRefresh: true,
                    switchBotOnly: true);
            }
        }

        private async Task CheckScheduledAutomationsAsync()
        {
            if (isRefreshingServerSensors) return;
            ServerSensorSettings sensorSettings = database.LoadServerSensorSettings();
            if (!sensorSettings.AutoUpdateEnabled &&
                !Properties.Settings.Default.WebhookRelayEnabled) return;
            string today = DateTime.Today.ToString("yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture);
            List<SimpleAutomationRule> rules = database.LoadAutomations();
            bool refreshRequired = false;
            foreach (SimpleAutomationRule rule in rules.Where(rule => rule.IsEnabled))
            {
                if (!IsAutomationFrequencyAllowed(rule,
                        GetAutomationScheduleDate(rule)))
                    continue;
                bool ruleChanged = false;
                foreach (AutomationCondition condition in rule.Conditions.Where(condition =>
                    condition.IsEnabled && string.Equals(condition.TriggerSensorId, "Time",
                        StringComparison.OrdinalIgnoreCase)))
                {
                    TimeSpan target;
                    if (string.Equals(condition.TriggerDataLabel, "時間帯",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryParseAutomationTimeRange(condition.ConditionValue,
                                out target, out _))
                            continue;
                    }
                    else if (!TimeSpan.TryParse(condition.ConditionValue, out target))
                        continue;
                    if (
                        DateTime.Now.TimeOfDay < target ||
                        string.Equals(condition.LastScheduledRefreshDate, today, StringComparison.Ordinal))
                        continue;
                    condition.LastScheduledRefreshDate = today;
                    refreshRequired = true;
                    ruleChanged = true;
                }
                if (ruleChanged) database.SaveAutomation(rule);
            }
            if (refreshRequired)
                await RefreshServerSensorsAsync();
            else
                await EvaluateSimpleAutomationsAsync();
        }

        public void ReloadServerSensorSettingsAndRefresh(bool useCachedValues)
        {
            ServerSensorSettings settings = database.LoadServerSensorSettings();
            serverSensorTimer.Stop();
            serverSensorTimer.Interval = TimeSpan.FromSeconds(
                Math.Clamp(settings.UpdateIntervalSeconds, 30, 3600));
            if (settings.AutoUpdateEnabled)
                serverSensorTimer.Start();
            if (useCachedValues)
                ApplyServerSensorValues();
        }

        /// <summary>
        /// モーダル通知の表示中にDispatcherTimerがUIスレッドへ割り込み、
        /// ダイアログのボタン操作を長時間止めないよう一時停止する。
        /// </summary>
        public void SuspendRuntimeTimersForModalDialog()
        {
            isRuntimeModalDialogOpen = true;
            serverSensorTimer.Stop();
            automationScheduleTimer.Stop();
            AutomationDiagnosticLog.Write("インポート完了通知を表示。自動処理を一時停止。");
        }

        /// <summary>
        /// モーダル通知を閉じた後、現在の設定に従って定期処理を再開する。
        /// </summary>
        public void ResumeRuntimeTimersAfterModalDialog()
        {
            isRuntimeModalDialogOpen = false;
            // インポート処理内ですでに最新値を取得・反映済みなので、
            // ダイアログを閉じた直後に全ボタンを再走査しない。
            ReloadServerSensorSettingsAndRefresh(useCachedValues: false);
            automationScheduleTimer.Start();
            AutomationDiagnosticLog.Write("インポート完了通知を終了。自動処理を再開。");
        }

        public void ReloadWebhookRelaySettings()
        {
            webhookRealtimeCancellation?.Cancel();
            webhookRealtimeCancellation?.Dispose();
            webhookRealtimeCancellation = null;
            if (Properties.Settings.Default.WebhookRelayEnabled &&
                !string.IsNullOrWhiteSpace(Properties.Settings.Default.WebhookRelayBaseUrl) &&
                !string.IsNullOrWhiteSpace(Properties.Settings.Default.WebhookRelayApiKey))
            {
                webhookRealtimeCancellation = new CancellationTokenSource();
                _ = RunWebhookRealtimeLoopAsync(webhookRealtimeCancellation.Token);
            }
            else
            {
                webhookRealtimeStatus = "停止中";
            }
        }

        public string GetWebhookRealtimeStatus() =>
            LanguageManager.Get(webhookRealtimeStatus);

        private async Task RunWebhookRealtimeLoopAsync(CancellationToken cancellationToken)
        {
            int retrySeconds = 2;
            while (!cancellationToken.IsCancellationRequested &&
                   Properties.Settings.Default.WebhookRelayEnabled)
            {
                try
                {
                    webhookRealtimeStatus = LanguageManager.Get("接続中...");
                    await WebhookRelayClient.RunRealtimeAsync(
                        Properties.Settings.Default.WebhookLastEventId,
                        ProcessWebhookRealtimeEventAsync,
                        status => webhookRealtimeStatus = status,
                        cancellationToken);
                    retrySeconds = 2;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    webhookRealtimeStatus = string.Format(
                        LanguageManager.Get("切断: {0}（{1}秒後に再接続）"),
                        ex.Message, retrySeconds);
                }
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(retrySeconds), cancellationToken);
                }
                catch (OperationCanceledException) { break; }
                retrySeconds = Math.Min(retrySeconds * 2, 60);
            }
        }

        private async Task ProcessWebhookRealtimeEventAsync(WebhookRelayEvent relayEvent)
        {
            if (isRuntimeModalDialogOpen)
                return;

            if (relayEvent.Id > 0 &&
                relayEvent.Id <= Properties.Settings.Default.WebhookLastEventId)
                return;

            await Dispatcher.InvokeAsync(async () =>
            {
                string id = $"SwitchBot|{relayEvent.DeviceMac}";
                ServerSensorItem? old = database.LoadServerSensors()
                    .FirstOrDefault(item => string.Equals(item.Id, id,
                        StringComparison.OrdinalIgnoreCase));
                ServerSensorItem? sensor =
                    WebhookRelayClient.ConvertToSensor(relayEvent, old);
                if (sensor == null) return;

                List<ServerSensorItem> update = new() { sensor };
                database.SaveServerSensors(update);
                SensorLogService.Record(update, database.LoadServerSensorSettings());
                AutomationDiagnosticLog.Write(
                    $"Webhook受信\tSensor={sensor.Id}\tValues={sensor.AllValues}");
                await EvaluateSimpleAutomationsAsync(update);
                ApplyServerSensorValues();

                if (relayEvent.Id > 0)
                {
                    Properties.Settings.Default.WebhookLastEventId = relayEvent.Id;
                    Properties.Settings.Default.Save();
                    PersistentAppSettings.SaveFromProperties();
                }
            }).Task.Unwrap();
        }

        private async Task RefreshServerSensorsAsync(
            bool forceSwitchBotRefresh = false,
            bool switchBotOnly = false,
            bool discardStaleValues = false)
        {
            if (isRuntimeModalDialogOpen || isRefreshingServerSensors)
                return;
            isRefreshingServerSensors = true;
            try
            {
                Dictionary<string, ServerSensorItem> saved = database
                    .LoadServerSensors().ToDictionary(item => item.Id);
                List<ServerSensorItem> received = new();

                if (!switchBotOnly && !string.IsNullOrWhiteSpace(
                    Properties.Settings.Default.NatureRemoCloudToken))
                {
                    try { received.AddRange(await NatureRemoCloudApiClient.GetSensorsAsync()); }
                    catch (Exception ex)
                    {
                        AutomationDiagnosticLog.Write(
                            $"Nature Remoセンサー更新失敗: {ex.Message}");
                    }
                }

                ServerSensorSettings currentSensorSettings =
                    database.LoadServerSensorSettings();
                if ((forceSwitchBotRefresh ||
                     !Properties.Settings.Default.WebhookRelayEnabled ||
                     currentSensorSettings.AutoUpdateSwitchBotWhenWebhookEnabled) &&
                    !string.IsNullOrWhiteSpace(Properties.Settings.Default.SwitchBotToken) &&
                    !string.IsNullOrWhiteSpace(Properties.Settings.Default.SwitchBotSecret))
                {
                    try
                    {
                        AutomationDiagnosticLog.Write(
                            switchBotOnly
                                ? "起動時SwitchBot状態確認を開始。"
                                : "SwitchBot状態確認を開始。");
                        List<ServerSensorItem> switchBotSensors =
                            await SwitchBotApiClient.GetSensorsAsync()
                                .WaitAsync(TimeSpan.FromSeconds(30));
                        received.AddRange(switchBotSensors);
                        AutomationDiagnosticLog.Write(
                            $"SwitchBot状態確認完了。取得={switchBotSensors.Count}台");
                    }
                    catch (Exception ex)
                    {
                        AutomationDiagnosticLog.Write(
                            $"SwitchBotセンサー更新失敗: {ex.Message}");
                    }
                }

                if (!switchBotOnly &&
                    !string.IsNullOrWhiteSpace(Properties.Settings.Default.TuyaAccessId) &&
                    !string.IsNullOrWhiteSpace(Properties.Settings.Default.TuyaAccessSecret))
                {
                    try { received.AddRange(await TuyaApiClient.GetSensorsAsync()); }
                    catch (Exception ex)
                    {
                        AutomationDiagnosticLog.Write(
                            $"Tuyaセンサー更新失敗: {ex.Message}");
                    }
                }

                foreach (ServerSensorItem sensor in received)
                {
                    if (saved.TryGetValue(sensor.Id, out ServerSensorItem? old))
                    {
                        sensor.CopyDataButtonAssignmentsFrom(old);
                        sensor.LogEnabled = old.LogEnabled;

                        // 旧形式の行単位割り当てを最初の有効な項目へ移行する。
                        if (!string.IsNullOrWhiteSpace(old.AssignedButtonNumber) &&
                            string.IsNullOrWhiteSpace(sensor.OtherButtonNumber) &&
                            string.IsNullOrWhiteSpace(sensor.OtherButtonNumber2) &&
                            string.IsNullOrWhiteSpace(sensor.OtherButtonNumber3) &&
                            string.IsNullOrWhiteSpace(sensor.OtherButtonNumber4) &&
                            string.IsNullOrWhiteSpace(sensor.OtherButtonNumber5) &&
                            string.IsNullOrWhiteSpace(sensor.OtherButtonNumber6) &&
                            string.IsNullOrWhiteSpace(sensor.OtherButtonNumber7) &&
                            string.IsNullOrWhiteSpace(sensor.OtherButtonNumber8))
                        {
                            sensor.OtherButtonNumber = old.AssignedButtonNumber;
                        }
                    }
                }

                // オートメーションはログを参照しない。大量ログの保存や集計が
                // 長引いても実行が遅れないよう、取得直後の値を最優先で判定する。
                AutomationDiagnosticLog.Write(
                    $"最新センサー値による自動化判定を開始。取得={received.Count}台");
                await EvaluateSimpleAutomationsAsync(received);
                AutomationDiagnosticLog.Write(
                    "最新センサー値による自動化判定が完了。");

                // 判定後に、通信で今回取得できた値だけをログへ記録する。
                SensorLogService.Record(received,
                    database.LoadServerSensorSettings());

                if (!discardStaleValues)
                {
                    foreach (ServerSensorItem old in saved.Values.Where(old =>
                        !received.Any(sensor => sensor.Id == old.Id) &&
                        !received.Any(sensor => sensor.Provider == old.Provider)))
                        received.Add(old);

                    if (received.Count > 0)
                        database.SaveServerSensors(received);
                }
                else
                {
                    // インポートされた古い状態は残さず、今回実際に取得できた
                    // センサーだけを現在値として保存する。0件なら空にする。
                    database.ReplaceServerSensors(received);
                }
                ApplyServerSensorValues();
            }
            finally { isRefreshingServerSensors = false; }
        }

        internal async Task EvaluateSimpleAutomationsAsync(
            IEnumerable<ServerSensorItem>? currentSensors = null,
            bool allowWithoutSensorUpdates = false)
        {
            if (isImportingApplicationBackup || isRuntimeModalDialogOpen)
                return;

            await automationEvaluationLock.WaitAsync();
            try
            {
                ServerSensorSettings sensorSettings = database.LoadServerSensorSettings();
                if (!sensorSettings.AutoUpdateEnabled &&
                    !Properties.Settings.Default.WebhookRelayEnabled &&
                    !allowWithoutSensorUpdates) return;
                List<SimpleAutomationRule> rules = database.LoadAutomations();
                if (rules.Count == 0) return;
                Dictionary<string, ServerSensorItem> sensorMap = database.LoadServerSensors()
                    .ToDictionary(sensor => sensor.Id, StringComparer.OrdinalIgnoreCase);
                if (currentSensors != null)
                    foreach (ServerSensorItem sensor in currentSensors)
                        sensorMap[sensor.Id] = sensor;
                List<ServerSensorItem> sensors = sensorMap.Values.ToList();
                List<SensorLogEntry> currentEntries = sensors
                    .SelectMany(SensorLogService.CreateEntries).ToList();
                Dictionary<string, SwitchBotCommandItem> commands = database.LoadSwitchBotCommands()
                    .Where(command => !string.IsNullOrWhiteSpace(command.Id))
                    .GroupBy(command => command.Id).ToDictionary(group => group.Key, group => group.First());
                bool temporaryProtectionActive =
                    database.IsTemporaryProtectionActive();

                foreach (SimpleAutomationRule rule in rules)
                {
                    rule.EnsureConditions();
                    rule.EnsureActions();
                    List<AutomationCondition> enabledConditions = rule.Conditions
                        .Where(condition => condition.IsEnabled).ToList();
                    bool scheduleAllowed = IsAutomationFrequencyAllowed(rule,
                        GetAutomationScheduleDate(rule));
                    bool matched = rule.IsEnabled && scheduleAllowed &&
                        (!rule.SkipWhileTemporaryProtection ||
                         !temporaryProtectionActive) &&
                        enabledConditions.Count > 0 &&
                        (string.Equals(rule.LogicalOperator, "いずれかを満たす", StringComparison.Ordinal)
                            ? enabledConditions.Any(condition => IsAutomationConditionMatched(condition, currentEntries))
                            : enabledConditions.All(condition => IsAutomationConditionMatched(condition, currentEntries)));
                    if (!matched)
                    {
                        if (rule.IsLatched || rule.CurrentConsecutiveCount != 0 ||
                            rule.ConditionMatchedSinceUtc.HasValue)
                        {
                            AutomationDiagnosticLog.Write(rule,
                                $"条件不成立。継続計測を解除。開始={rule.ConditionMatchedSinceUtc:O}");
                            rule.IsLatched = false;
                            rule.CurrentConsecutiveCount = 0;
                            rule.ConditionMatchedSinceUtc = null;
                            database.SaveAutomation(rule);
                        }
                        continue;
                    }
                    if (rule.IsLatched)
                        continue;

                int continuationMinutes = Math.Clamp(
                    rule.RequiredConsecutiveCount, 0, 525600);
                rule.RequiredConsecutiveCount = continuationMinutes;
                DateTime nowUtc = DateTime.UtcNow;
                if (!rule.ConditionMatchedSinceUtc.HasValue)
                {
                    rule.ConditionMatchedSinceUtc = nowUtc;
                    rule.CurrentConsecutiveCount = 1;
                    database.SaveAutomation(rule);
                    AutomationDiagnosticLog.Write(rule,
                        $"条件成立。継続計測開始。必要={continuationMinutes}分、期限={nowUtc.AddMinutes(continuationMinutes):O}");
                    if (continuationMinutes > 0)
                    {
                        ScheduleAutomationContinuationCheck(rule.Id,
                            nowUtc.AddMinutes(continuationMinutes));
                        continue;
                    }
                }
                // LiteDBはUTCで保存したDateTimeをローカル時刻として読み戻す。
                // プロパティ名がUtcでもKindがLocalになるため、そのままUTCの現在時刻と
                // 比較すると日本では9時間遅れて期限到達する。必ずUTCへ戻して比較する。
                DateTime matchedSinceUtc = rule.ConditionMatchedSinceUtc.Value.Kind switch
                {
                    DateTimeKind.Utc => rule.ConditionMatchedSinceUtc.Value,
                    DateTimeKind.Local => rule.ConditionMatchedSinceUtc.Value.ToUniversalTime(),
                    _ => DateTime.SpecifyKind(
                        rule.ConditionMatchedSinceUtc.Value,
                        DateTimeKind.Local).ToUniversalTime()
                };
                rule.ConditionMatchedSinceUtc = matchedSinceUtc;
                DateTime dueUtc = matchedSinceUtc.AddMinutes(continuationMinutes);
                if (continuationMinutes > 0)
                    ScheduleAutomationContinuationCheck(rule.Id, dueUtc);
                if (nowUtc < dueUtc)
                    continue;

                // Webhookで追跡できるSwitchBot以外は、継続時間経過後に行われた
                // API更新でも条件が成立していることを確認してから実行する。
                // これにより、更新間隔中に状態が変わっていた場合の誤実行を防ぐ。
                if (!HasReliableSensorObservation(rule, enabledConditions,
                        currentEntries, dueUtc.ToLocalTime()))
                {
                    AutomationDiagnosticLog.Write(rule,
                        $"期限到達後の最新状態を確認できないため保留。期限={dueUtc:O}");
                    continue;
                }
                bool hasExecutableAction = rule.Actions.Any(action =>
                    action.CommandId.StartsWith("Mode:", StringComparison.OrdinalIgnoreCase) ||
                    action.CommandId.StartsWith("Protection:", StringComparison.OrdinalIgnoreCase) ||
                    action.CommandId.StartsWith("Button:", StringComparison.OrdinalIgnoreCase) ||
                    commands.ContainsKey(action.CommandId));
                if (!hasExecutableAction)
                {
                    AutomationDiagnosticLog.Write(rule,
                        "実行可能なコマンドが見つからないため中止。");
                    continue;
                }
                rule.IsLatched = true;
                foreach (AutomationCondition condition in enabledConditions.Where(condition =>
                    string.Equals(condition.TriggerSensorId, "Time", StringComparison.OrdinalIgnoreCase) &&
                    IsAutomationConditionMatched(condition, currentEntries)))
                    condition.LastTriggeredDate = GetAutomationTimePeriodKey(condition);
                database.SaveAutomation(rule);
                AutomationDiagnosticLog.Write(rule,
                    $"継続条件成立。実行処理を開始。開始={rule.ConditionMatchedSinceUtc:O}、期限={dueUtc:O}");
                    _ = ExecuteAutomationAfterDelayAsync(
                        rule.Id, GetAutomationExecutionSignature(rule));
                }
            }
            finally
            {
                automationEvaluationLock.Release();
            }
        }

        private void ScheduleAutomationContinuationCheck(int ruleId, DateTime dueUtc)
        {
            if (scheduledAutomationContinuationChecks.TryGetValue(ruleId,
                    out DateTime scheduledDueUtc) && scheduledDueUtc == dueUtc)
                return;

            scheduledAutomationContinuationChecks[ruleId] = dueUtc;
            _ = RecheckAutomationAtDueTimeAsync(ruleId, dueUtc);
        }

        private async Task RecheckAutomationAtDueTimeAsync(int ruleId, DateTime dueUtc)
        {
            try
            {
                TimeSpan delay = dueUtc - DateTime.UtcNow;
                if (delay > TimeSpan.Zero) await Task.Delay(delay);
                if (!IsLoaded) return;

                // 条件が途中で不成立になった後に再成立した場合、古い期限の
                // Taskは何もせず終了する。現在の期限だけが再取得を行う。
                if (!scheduledAutomationContinuationChecks.TryGetValue(ruleId,
                        out DateTime currentDueUtc) || currentDueUtc != dueUtc)
                    return;

                SimpleAutomationRule? dueRule = database.LoadAutomation(ruleId);
                if (dueRule != null)
                    AutomationDiagnosticLog.Write(dueRule,
                        $"継続期限に到達。最新状態を再取得。期限={dueUtc:O}");

                // 継続時間の終了時点で最新状態を再取得する。Webhook利用時も、
                // コマンド直後の状態を確実に確認するためSwitchBot APIを使用する。
                await RefreshServerSensorsAsync(forceSwitchBotRefresh: true);
                await EvaluateSimpleAutomationsAsync();
            }
            finally
            {
                if (scheduledAutomationContinuationChecks.TryGetValue(ruleId,
                        out DateTime currentDueUtc) && currentDueUtc == dueUtc)
                    scheduledAutomationContinuationChecks.Remove(ruleId);
            }
        }

        private static bool HasReliableSensorObservation(
            SimpleAutomationRule rule,
            IReadOnlyCollection<AutomationCondition> conditions,
            IReadOnlyCollection<SensorLogEntry> entries,
            DateTime dueLocal)
        {
            bool useAny = string.Equals(rule.LogicalOperator, "いずれかを満たす",
                StringComparison.Ordinal);
            if (useAny && conditions.Any(condition =>
                string.Equals(condition.TriggerSensorId, "Time",
                    StringComparison.OrdinalIgnoreCase) &&
                IsAutomationConditionMatched(condition, entries)))
                return true;

            List<AutomationCondition> sensorConditions = conditions.Where(condition =>
                !string.Equals(condition.TriggerSensorId, "Time",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(condition.TriggerSensorId, "PresenceMode",
                    StringComparison.OrdinalIgnoreCase) &&
                (!useAny || IsAutomationConditionMatched(condition, entries))).ToList();
            if (sensorConditions.Count == 0)
                return true;

            bool IsFreshOrRealtime(AutomationCondition condition)
            {
                if (Properties.Settings.Default.WebhookRelayEnabled &&
                    condition.TriggerSensorId.StartsWith("SwitchBot|",
                        StringComparison.OrdinalIgnoreCase))
                    return true;
                string conditionKey = SensorDataNormalizer.NormalizeKey(
                    condition.TriggerDataLabel);
                return entries.Any(entry =>
                    string.Equals(entry.SensorId, condition.TriggerSensorId,
                        StringComparison.OrdinalIgnoreCase) &&
                    TryGetNormalizedMetricKey(entry.Metric, out string entryKey) &&
                    string.Equals(entryKey, conditionKey,
                        StringComparison.OrdinalIgnoreCase) &&
                    entry.Timestamp >= dueLocal);
            }

            return useAny
                ? sensorConditions.Any(IsFreshOrRealtime)
                : sensorConditions.All(IsFreshOrRealtime);
        }

        private static string GetAutomationExecutionSignature(SimpleAutomationRule rule)
        {
            // UIでルールを編集したあと、編集前に予約された処理を識別する。
            // 実行状態（IsLatchedや最終実行日）は意図的に含めない。
            StringBuilder signature = new();
            signature.Append(rule.IsEnabled).Append('\u001f')
                .Append(rule.DelayMinutes).Append('\u001f')
                .Append(rule.RequiredConsecutiveCount).Append('\u001f')
                .Append(rule.LogicalOperator).Append('\u001f')
                .Append(rule.ExecutionFrequency).Append('\u001f')
                .Append(rule.SelectedWeekdays).Append('\u001f')
                .Append(rule.WeeklyDay).Append('\u001f')
                .Append(rule.SkipWhileTemporaryProtection).Append('\u001f')
                .Append(rule.IsNotificationEnabled);
            rule.EnsureActions();
            foreach (AutomationAction action in rule.Actions.OrderBy(action => action.SortOrder))
                signature.Append('\u001d').Append(action.CommandId)
                    .Append('\u001f').Append(action.ProtectionMinutes);
            foreach (AutomationCondition condition in rule.Conditions
                .OrderBy(condition => condition.SortOrder))
            {
                signature.Append('\u001e')
                    .Append(condition.IsEnabled).Append('\u001f')
                    .Append(condition.TriggerSensorId).Append('\u001f')
                    .Append(condition.TriggerDataLabel).Append('\u001f')
                    .Append(condition.ConditionValue).Append('\u001f')
                    .Append(condition.Comparison).Append('\u001f')
                    .Append(condition.SortOrder);
            }
            return signature.ToString();
        }

        private static bool IsAutomationConditionMatched(
            AutomationCondition condition, IReadOnlyCollection<SensorLogEntry> entries)
        {
            if (string.Equals(condition.TriggerSensorId, "Time", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(condition.TriggerDataLabel, "時間帯",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseAutomationTimeRange(condition.ConditionValue,
                            out TimeSpan start, out TimeSpan end)) return false;
                    TimeSpan now = DateTime.Now.TimeOfDay;
                    bool isInRange = start <= end
                        ? now >= start && now <= end
                        : now >= start || now <= end;
                    if (!isInRange) return false;

                    // 日付をまたぐ時間帯では、深夜側も開始日の実行として扱う。
                    DateTime periodDate = start > end && now <= end
                        ? DateTime.Today.AddDays(-1)
                        : DateTime.Today;
                    string periodKey = periodDate.ToString("yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture);
                    return !string.Equals(condition.LastTriggeredDate, periodKey,
                        StringComparison.Ordinal);
                }
                if (!TimeSpan.TryParse(condition.ConditionValue, out TimeSpan target)) return false;
                string today = DateTime.Today.ToString("yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture);
                return DateTime.Now.TimeOfDay >= target &&
                    !string.Equals(condition.LastTriggeredDate, today, StringComparison.Ordinal);
            }
            if (string.Equals(condition.TriggerSensorId, "PresenceMode",
                    StringComparison.OrdinalIgnoreCase))
                return string.Equals(
                    Properties.Settings.Default.PresenceMode,
                    SensorDataNormalizer.NormalizeValue(condition.ConditionValue),
                    StringComparison.OrdinalIgnoreCase);
            string conditionKey = SensorDataNormalizer.NormalizeKey(
                condition.TriggerDataLabel);
            SensorLogEntry? latest = entries
                .Where(entry =>
                    string.Equals(entry.SensorId, condition.TriggerSensorId,
                        StringComparison.OrdinalIgnoreCase) &&
                    TryGetNormalizedMetricKey(entry.Metric, out string entryKey) &&
                    string.Equals(entryKey, conditionKey,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.Timestamp).FirstOrDefault();
            if (latest == null) return false;
            if (condition.Comparison == "以上" || condition.Comparison == "以下")
            {
                if (!latest.NumericValue.HasValue ||
                    !double.TryParse(condition.ConditionValue, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.CurrentCulture, out double threshold) &&
                    !double.TryParse(condition.ConditionValue, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out threshold)) return false;
                return condition.Comparison == "以上"
                    ? latest.NumericValue.Value >= threshold
                    : latest.NumericValue.Value <= threshold;
            }
            if (latest.NumericValue.HasValue &&
                double.TryParse(condition.ConditionValue, out double equalNumber))
                return Math.Abs(latest.NumericValue.Value - equalNumber) < 0.000001;
            return string.Equals(
                SensorDataNormalizer.NormalizeValue(latest.RawValue),
                SensorDataNormalizer.NormalizeValue(condition.ConditionValue),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseAutomationTimeRange(
            string? source, out TimeSpan start, out TimeSpan end)
        {
            start = default;
            end = default;
            if (string.IsNullOrWhiteSpace(source)) return false;
            string[] parts = source.Split(
                new[] { "～", "〜", "~", "-" },
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2 &&
                TimeSpan.TryParse(parts[0], out start) &&
                TimeSpan.TryParse(parts[1], out end) &&
                start >= TimeSpan.Zero && start < TimeSpan.FromDays(1) &&
                end >= TimeSpan.Zero && end < TimeSpan.FromDays(1);
        }

        private static string GetAutomationTimePeriodKey(
            AutomationCondition condition)
        {
            DateTime periodDate = GetAutomationTimePeriodDate(condition);
            return periodDate.ToString("yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private static DateTime GetAutomationTimePeriodDate(
            AutomationCondition condition)
        {
            DateTime periodDate = DateTime.Today;
            if (string.Equals(condition.TriggerDataLabel, "時間帯",
                    StringComparison.OrdinalIgnoreCase) &&
                TryParseAutomationTimeRange(condition.ConditionValue,
                    out TimeSpan start, out TimeSpan end) &&
                start > end && DateTime.Now.TimeOfDay <= end)
                periodDate = periodDate.AddDays(-1);
            return periodDate;
        }

        private static DateTime GetAutomationScheduleDate(SimpleAutomationRule rule)
        {
            AutomationCondition? timeCondition = rule.Conditions.FirstOrDefault(condition =>
                condition.IsEnabled && string.Equals(condition.TriggerSensorId, "Time",
                    StringComparison.OrdinalIgnoreCase));
            return timeCondition == null
                ? DateTime.Today
                : GetAutomationTimePeriodDate(timeCondition);
        }

        private static bool IsAutomationFrequencyAllowed(
            SimpleAutomationRule rule, DateTime scheduleDate)
        {
            // 実行頻度は時刻・時間帯条件を含むルールだけに適用する。
            if (!rule.Conditions.Any(condition => condition.IsEnabled &&
                    string.Equals(condition.TriggerSensorId, "Time",
                        StringComparison.OrdinalIgnoreCase)))
                return true;

            int day = (int)scheduleDate.DayOfWeek;
            return rule.ExecutionFrequency switch
            {
                "Once" => !rule.HasExecutedOnce,
                "SelectedDays" => (rule.SelectedWeekdays & (1 << day)) != 0,
                "Weekdays" => scheduleDate.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday,
                "WeekendsHolidays" => scheduleDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
                                      JapaneseHolidayCalendar.IsHoliday(scheduleDate),
                "Weekly" => day == Math.Clamp(rule.WeeklyDay, 0, 6),
                _ => true
            };
        }

        private static bool TryGetNormalizedMetricKey(
            string? metric, out string normalizedKey)
        {
            const string dataPrefix = "Data:";
            normalizedKey = "";
            if (string.IsNullOrWhiteSpace(metric) ||
                !metric.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
                return false;
            normalizedKey = SensorDataNormalizer.NormalizeKey(
                metric[dataPrefix.Length..]);
            return !string.IsNullOrWhiteSpace(normalizedKey);
        }

        private async Task ExecuteAutomationAfterDelayAsync(
            int ruleId, string expectedSignature)
        {
            try
            {
                SimpleAutomationRule? scheduledRule = database.LoadAutomation(ruleId);
                if (scheduledRule == null) return;
                int delayMinutes = scheduledRule.DelayMinutes;
                if (delayMinutes > 0)
                    await Task.Delay(TimeSpan.FromMinutes(Math.Clamp(delayMinutes, 0, 525600)));

                // 待機中に削除・無効化・条件変更・実行先変更された予約は破棄する。
                // これにより、画面にもう存在しない古いルールが後から動作しない。
                SimpleAutomationRule? currentRule = database.LoadAutomation(ruleId);
                if (currentRule == null || !currentRule.IsEnabled ||
                    !string.Equals(GetAutomationExecutionSignature(currentRule),
                        expectedSignature, StringComparison.Ordinal))
                    return;

                currentRule.EnsureActions();
                List<string> failures = new();
                foreach (AutomationAction action in currentRule.Actions
                    .OrderBy(action => action.SortOrder))
                {
                    AutomationDiagnosticLog.Write(currentRule,
                        $"コマンド実行開始: {action.CommandId}");
                    try
                    {
                        await ExecuteAutomationCommandAsync(action);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{action.CommandId}: {ex.Message}");
                        AutomationDiagnosticLog.Write(currentRule,
                            $"コマンド実行失敗: {action.CommandId}\n{ex}");
                    }
                }
                if (failures.Count == 0)
                {
                    if (string.Equals(currentRule.ExecutionFrequency, "Once",
                            StringComparison.Ordinal) &&
                        currentRule.Conditions.Any(condition => condition.IsEnabled &&
                            string.Equals(condition.TriggerSensorId, "Time",
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        currentRule.HasExecutedOnce = true;
                        database.SaveAutomation(currentRule);
                        editWindow?.UpdateAutomationOnceExecutionState(
                            currentRule.Id, true);
                    }
                    if (currentRule.IsNotificationEnabled)
                        ShowAutomationNotification(currentRule.Name);
                    AutomationDiagnosticLog.Write(currentRule,
                        "すべてのコマンド実行が完了。");
                }
                else
                {
                    string failureMessage = string.Join("\n", failures);
                    if (currentRule.IsNotificationEnabled)
                        ShowAutomationFailureNotification(
                            currentRule.Name, failureMessage);
                    AutomationDiagnosticLog.Write(currentRule,
                        $"一部のコマンド実行に失敗。失敗={failures.Count}件");
                }
                _ = RefreshServerSensorsAfterCommandAsync();
            }
            catch (Exception ex)
            {
                // 条件が成立したまま失敗を繰り返すと家電へ連続送信されるため、
                // 予期しない失敗でもラッチは維持する。条件が一度不成立になれば
                // 通常の判定処理で解除され、次回は再び実行できる。
                SimpleAutomationRule? failedRule = database.LoadAutomation(ruleId);
                if (failedRule != null)
                {
                    AutomationDiagnosticLog.Write(failedRule,
                        $"実行失敗: {ex}");
                    failedRule.IsLatched = true;
                    database.SaveAutomation(failedRule);
                    if (failedRule.IsNotificationEnabled)
                        ShowAutomationFailureNotification(
                            failedRule.Name, ex.Message);
                }
            }
        }

        private async Task ExecuteAutomationCommandAsync(AutomationAction action)
        {
            string commandId = action.CommandId;
            if (string.Equals(commandId, "Protection:Start",
                    StringComparison.OrdinalIgnoreCase))
            {
                database.StartTemporaryProtection(action.ProtectionMinutes);
                return;
            }
            if (string.Equals(commandId, "Protection:Cancel",
                    StringComparison.OrdinalIgnoreCase))
            {
                database.ClearTemporaryProtection();
                return;
            }
            if (commandId.StartsWith("Mode:", StringComparison.OrdinalIgnoreCase))
            {
                await SetPresenceModeAsync(commandId["Mode:".Length..]);
                return;
            }
            const string buttonPrefix = "Button:";
            if (commandId.StartsWith(buttonPrefix, StringComparison.OrdinalIgnoreCase))
            {
                await NatureRemoSender.SendAsync(commandId[buttonPrefix.Length..]);
                return;
            }
            SwitchBotCommandItem command = database.LoadSwitchBotCommands()
                .FirstOrDefault(item => string.Equals(item.Id, commandId,
                    StringComparison.Ordinal)) ??
                throw new InvalidOperationException(
                    $"実行するコマンドが見つかりません: {commandId}");
            if (string.Equals(command.CommandType, "aircon-control",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "エアコン操作パネルは簡易オートメーションから実行できません。" +
                    "固定したエアコンコマンドを使用してください。");
            }
            switch (command.Provider)
            {
                case "NatureRemoCloud":
                    await NatureRemoCloudApiClient.SendCommandAsync(
                        command.DeviceId, command.Command, command.CommandType,
                        command.Parameter); break;
                case "SwitchBotScene":
                    await SwitchBotApiClient.SendSceneAsync(command.DeviceId); break;
                case "Tuya":
                    await TuyaApiClient.SendCommandAsync(command.DeviceId, command.Command,
                        command.Parameter, command.CommandType); break;
                case "TuyaScene":
                    await TuyaApiClient.SendSceneAsync(command.DeviceId); break;
                default:
                    await SwitchBotApiClient.SendCommandAsync(command.DeviceId, command.Command,
                        command.Parameter, command.CommandType); break;
            }
        }

        internal async Task SetPresenceModeAsync(string mode)
        {
            string normalizedMode = string.Equals(mode, "Away",
                StringComparison.OrdinalIgnoreCase) ? "Away" : "Home";
            if (string.Equals(Properties.Settings.Default.PresenceMode,
                    normalizedMode, StringComparison.OrdinalIgnoreCase))
                return;

            Properties.Settings.Default.PresenceMode = normalizedMode;
            Properties.Settings.Default.Save();
            PersistentAppSettings.SaveFromProperties();

            await EvaluateSimpleAutomationsAsync(
                allowWithoutSensorUpdates: true);
        }

        private void ShowAutomationFailureNotification(
            string automationName, string message)
        {
            Dispatcher.BeginInvoke(() =>
            {
                EnqueueAutomationNotification(
                    "N-Remo 簡易オートメーション エラー",
                    $"{automationName}\n{message}",
                    WinForms.ToolTipIcon.Error);
            });
        }

        private void ShowAutomationNotification(string actionName)
        {
            Dispatcher.BeginInvoke(() =>
            {
                EnqueueAutomationNotification(
                    "N-Remo 簡易オートメーション",
                    $"{actionName}を実行しました。",
                    WinForms.ToolTipIcon.Info);
            });
        }

        private void EnqueueAutomationNotification(
            string title, string message, WinForms.ToolTipIcon icon)
        {
            AutomationDiagnosticLog.Write(
                $"デスクトップ通知を予約: {title} / {message.Replace(Environment.NewLine, " | ")}");
            automationNotificationQueue.Enqueue((title, message, icon));
            if (isShowingAutomationNotifications) return;
            isShowingAutomationNotifications = true;
            _ = ShowQueuedAutomationNotificationsAsync();
        }

        private async Task ShowQueuedAutomationNotificationsAsync()
        {
            try
            {
                while (automationNotificationQueue.Count > 0)
                {
                    var notification = automationNotificationQueue.Dequeue();
                    if (NativeNotificationService.TryShow(
                            notification.Title,
                            notification.Message,
                            out string notificationError))
                    {
                        AutomationDiagnosticLog.Write(
                            $"Windowsネイティブ通知を表示: {notification.Title}");
                    }
                    else
                    {
                        AutomationDiagnosticLog.Write(
                            $"Windowsネイティブ通知失敗: {notificationError}");
                    }
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
            finally
            {
                isShowingAutomationNotifications = false;
            }
        }

        private async Task RefreshServerSensorsAfterCommandAsync()
        {
            try
            {
                ServerSensorSettings settings =
                    database.LoadServerSensorSettings();

                if (!settings.RefreshAfterCommandEnabled)
                    return;

                int delaySeconds =
                    settings.RefreshAfterCommandSeconds <= 0
                        ? 10
                        : Math.Clamp(
                            settings.RefreshAfterCommandSeconds,
                            1,
                            3600);

                await Task.Delay(
                    TimeSpan.FromSeconds(delaySeconds));

                if (!IsLoaded)
                    return;

                await RefreshServerSensorsAsync(forceSwitchBotRefresh: true);
            }
            catch
            {
                // コマンド自体は成功しているため、遅延更新の失敗では
                // ボタン操作をエラー扱いにしない。
            }
        }

        private void ApplyServerSensorValues()
        {
            List<ButtonSetting> buttons = database.LoadAll();
            serverSensorAssignedButtonNames.Clear();
            foreach (ButtonSetting setting in buttons)
            {
                System.Windows.Controls.Button? button = GetButton(setting.Name);
                if (button?.Content is DeviceCommandPanel devicePanel)
                    devicePanel.RefreshStatus();
                else if (button != null)
                    button.Content = setting.NameContent;
            }

            List<ServerSensorItem> sensors = database.LoadServerSensors();
            bool migratedLegacyAssignment = false;
            foreach (ServerSensorItem sensor in sensors)
            {
                if (!string.IsNullOrWhiteSpace(sensor.AssignedButtonNumber) &&
                    string.IsNullOrWhiteSpace(sensor.OtherButtonNumber) &&
                    string.IsNullOrWhiteSpace(sensor.OtherButtonNumber2) &&
                    string.IsNullOrWhiteSpace(sensor.OtherButtonNumber3) &&
                    string.IsNullOrWhiteSpace(sensor.OtherButtonNumber4) &&
                    string.IsNullOrWhiteSpace(sensor.OtherButtonNumber5) &&
                    string.IsNullOrWhiteSpace(sensor.OtherButtonNumber6) &&
                    string.IsNullOrWhiteSpace(sensor.OtherButtonNumber7) &&
                    string.IsNullOrWhiteSpace(sensor.OtherButtonNumber8))
                {
                    sensor.OtherButtonNumber = sensor.AssignedButtonNumber;
                    sensor.AssignedButtonNumber = "";
                    migratedLegacyAssignment = true;
                }

                // 旧形式の割り当てを移行した後で登録する。
                // 先に登録すると初回だけ割り当て済みと判定されず、
                // 動的なセンサー値が表示名として保存されてしまう。
                foreach (string assignedButton in new[]
                {
                    sensor.OtherButtonNumber,
                    sensor.OtherButtonNumber2,
                    sensor.OtherButtonNumber3,
                    sensor.OtherButtonNumber4,
                    sensor.OtherButtonNumber5,
                    sensor.OtherButtonNumber6,
                    sensor.OtherButtonNumber7,
                    sensor.OtherButtonNumber8
                }.Where(name => !string.IsNullOrWhiteSpace(name)))
                {
                    serverSensorAssignedButtonNames.Add(assignedButton);
                }

                ApplySensorValueToButton(buttons, sensor.OtherButtonNumber,
                    sensor.OtherValue);
                ApplySensorValueToButton(buttons, sensor.OtherButtonNumber2,
                    sensor.OtherValue2);
                ApplySensorValueToButton(buttons, sensor.OtherButtonNumber3,
                    sensor.OtherValue3);
                ApplySensorValueToButton(buttons, sensor.OtherButtonNumber4,
                    sensor.OtherValue4);
                ApplySensorValueToButton(buttons, sensor.OtherButtonNumber5,
                    sensor.OtherValue5);
                ApplySensorValueToButton(buttons, sensor.OtherButtonNumber6,
                    sensor.OtherValue6);
                ApplySensorValueToButton(buttons, sensor.OtherButtonNumber7,
                    sensor.OtherValue7);
                ApplySensorValueToButton(buttons, sensor.OtherButtonNumber8,
                    sensor.OtherValue8);
            }
            if (migratedLegacyAssignment)
                database.SaveServerSensors(sensors);
        }

        private void ApplySensorValueToButton(
            IReadOnlyCollection<ButtonSetting> settings,
            string buttonName,
            string value)
        {
            if (string.IsNullOrWhiteSpace(buttonName) ||
                string.IsNullOrWhiteSpace(value))
                return;

            System.Windows.Controls.Button? button = GetButton(buttonName);
            ButtonSetting? setting = settings.FirstOrDefault(item =>
                item.Name == buttonName);
            if (button == null || setting == null)
                return;

            // 以前の状態保存でセンサー値そのものが表示名へ混入していた
            // データは、同じ値を検出した時点で空の表示名へ戻す。
            if (string.Equals(
                    setting.NameContent,
                    value,
                    StringComparison.Ordinal))
            {
                setting.NameContent = "";
                database.Save(setting);
            }

            string template = setting.NameContent ?? "";
            if (!template.Contains('@'))
            {
                button.Content = template;
                return;
            }

            button.Content = template.Replace(
                "@",
                GetSensorPlaceholderValue(value),
                StringComparison.Ordinal);
        }

        private static string GetSensorPlaceholderValue(string value)
        {
            string result = value.Trim();
            int separatorIndex = result.IndexOfAny(new[] { ':', '：' });
            if (separatorIndex >= 0 && separatorIndex < result.Length - 1)
                result = result[(separatorIndex + 1)..].Trim();

            // 「26.0」のような数値は不要な末尾の0を除いて表示する。
            if (double.TryParse(
                result,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double numericValue) && double.IsFinite(numericValue))
            {
                return numericValue.ToString(
                    "0.################",
                    System.Globalization.CultureInfo.CurrentCulture);
            }

            return SensorDataNormalizer.DisplayValue(result);
        }


        // ========================================
        // Button名からButtonを取得
        // ========================================
        public System.Windows.Controls.Button? GetButton(string buttonId)
        {
            string normalizedId =
                NormalizeButtonId(buttonId);

            return MainCanvas.Children
                .OfType<System.Windows.Controls.Button>()
                .FirstOrDefault(button =>
                    string.Equals(
                        NormalizeButtonId(button.Name),
                        normalizedId,
                        StringComparison.OrdinalIgnoreCase));
        }

        // ========================================
        // Button名を正規化
        // ========================================
        private static string NormalizeButtonId(
    string value)
        {
            string id =
                value.Trim();

            if (id.StartsWith(
                "Button",
                StringComparison.OrdinalIgnoreCase))
            {
                id = id["Button".Length..];
            }
            else if (id.StartsWith(
                "No",
                StringComparison.OrdinalIgnoreCase))
            {
                id = id["No".Length..];
            }

            if (int.TryParse(
                id,
                out int number))
            {
                return $"No{number:000}";
            }

            return value.Trim();
        }

        // ========================================
        // Button → ButtonSetting
        // ========================================
        public ButtonSetting GetButtonSetting(
    string buttonName)
        {
            System.Windows.Controls.Button? button =
                GetButton(buttonName);

            if (button == null)
            {
                throw new Exception(
                    $"Buttonが見つかりません: {buttonName}");
            }

            // 引数ではなく、実際のボタンが持つIDを使用
            string actualButtonName =
                button.Name;

            double top =
                Canvas.GetTop(button);

            double left =
                Canvas.GetLeft(button);

            if (double.IsNaN(top))
                top = 0;

            if (double.IsNaN(left))
                left = 0;

            // No001などの正しいIDでDBを読み込む
            ButtonSetting? savedSetting =
                database.Load(actualButtonName);

            return new ButtonSetting
            {
                Name =
                    actualButtonName,

                IsVisible =
                    button.Visibility ==
                    Visibility.Visible,

                // 位置・サイズ・色などの状態保存では、画面上のContentを
                // 表示名として取り込まない。Contentには温度などの動的な
                // センサー値が追加されている場合があるため、正式な表示名は
                // LiteDBの値を維持する。表示名の変更は
                // SaveButtonDisplayNameで明示的に保存する。
                NameContent =
                    savedSetting?.NameContent ??
                    button.Content?.ToString() ?? "",

                ItemType = savedSetting?.ItemType ?? "Button",
                PanelType = savedSetting?.PanelType ?? "",
                PanelTheme = savedSetting?.PanelTheme ?? "Light",
                PanelAirPower = savedSetting?.PanelAirPower ?? "on",
                PanelAirMode = savedSetting?.PanelAirMode ?? "cool",
                PanelAirTemperature = savedSetting?.PanelAirTemperature is >= 16 and <= 30
                    ? savedSetting.PanelAirTemperature
                    : 26,
                PanelAirFan = savedSetting?.PanelAirFan ?? "",

                Top = top,
                Left = left,
                Width = button.Width,
                Height = button.Height,
                FontSize = button.FontSize,

                FontColor =
                    button.Foreground?.ToString() ?? "",

                FontWeight =
                    button.FontWeight.ToString(),

                BackgroundColor =
                    button.Background?.ToString() ?? "",

                HorizontalAlignment =
                    button.HorizontalAlignment.ToString(),

                VerticalAlignment =
                    button.VerticalAlignment.ToString(),

                ZIndex =
                    System.Windows.Controls.Panel.GetZIndex(button),

                IsMouseOverEffectEnabled =
                    ButtonEffects
                        .GetIsMouseOverEffectEnabled(button),

                // 既存データを必ず維持
                Data1 =
                    savedSetting?.Data1 ?? "",

                Data2 =
                    savedSetting?.Data2 ?? "",

                Data3 =
                    savedSetting?.Data3 ?? "",

                Provider =
                    string.IsNullOrWhiteSpace(savedSetting?.Provider)
                        ? "NatureRemo"
                        : savedSetting.Provider,

                SwitchBotDeviceId =
                    savedSetting?.SwitchBotDeviceId ?? "",

                SwitchBotDeviceName =
                    savedSetting?.SwitchBotDeviceName ?? "",

                SwitchBotCommand =
                    savedSetting?.SwitchBotCommand ?? "",

                SwitchBotParameter =
                    savedSetting?.SwitchBotParameter ?? "default",

                SwitchBotCommandType =
                    savedSetting?.SwitchBotCommandType ?? "command"
            };
        }

        private bool IsButtonAssignedToServerSensor(string buttonName)
        {
            return serverSensorAssignedButtonNames.Contains(buttonName);
        }


        // ========================================
        // ButtonSetting → Button
        // ========================================
        public void ApplyButtonSetting(ButtonSetting setting)
        {
            System.Windows.Controls.Button? button = GetButton(setting.Name);

            if (button == null)
            {
                CreateButton(setting);
                return;
            }

            ApplySettingToButton(button, setting);
        }

        private void ApplySettingToButton(
            System.Windows.Controls.Button button,
            ButtonSetting setting)
        {
            button.Visibility =
                setting.IsVisible
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            bool isDevicePanel = string.Equals(
                setting.ItemType,
                "DevicePanel",
                StringComparison.OrdinalIgnoreCase);
            if (isDevicePanel)
            {
                button.Style = TryFindResource("DevicePanelHostStyle") as Style;
                button.Content = new DeviceCommandPanel(setting);
            }
            else
            {
                button.Style = TryFindResource("RoundedButtonStyle") as Style;
                button.Content = setting.NameContent;
            }
            button.Width = setting.Width;
            button.Height = setting.Height;
            button.FontSize = setting.FontSize;

            Canvas.SetLeft(button, setting.Left);
            Canvas.SetTop(button, setting.Top);
            System.Windows.Controls.Panel.SetZIndex(button, setting.ZIndex);

            ButtonEffects.SetIsMouseOverEffectEnabled(
                button,
                setting.IsMouseOverEffectEnabled);

            // FontColor、FontWeight、BackgroundColorの
            // 現在の反映処理をここへ移動
            // フォントカラー
            try
            {
                button.Foreground =
                    new SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                            setting.FontColor));
            }
            catch
            {
                button.Foreground = System.Windows.Media.Brushes.Black;
            }


            // FontWeight
            try
            {
                object? convertedWeight = new FontWeightConverter()
                    .ConvertFromString(setting.FontWeight);
                button.FontWeight = convertedWeight is FontWeight fontWeight
                    ? fontWeight
                    : FontWeights.Normal;
            }
            catch
            {
                button.FontWeight = FontWeights.Normal;
            }


            // Background
            try
            {
                button.Background =
                    new SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                            setting.BackgroundColor));
            }
            catch
            {
                button.Background = System.Windows.Media.Brushes.LightGray;
            }

            ButtonEffects.SetIsMouseOverEffectEnabled(button, setting.IsMouseOverEffectEnabled);

            // 位置（Canvas の添付プロパティを使う）
            Canvas.SetLeft(button, setting.Left);
            Canvas.SetTop(button, setting.Top);

            // 重なり順
            System.Windows.Controls.Panel.SetZIndex(button, setting.ZIndex);
        }

        // ========================================
        // buttonの設定をLiteDBに保存する処理
        // ========================================
        public void SaveButtonSetting(string buttonName)
        {
            ButtonSetting setting = GetButtonSetting(buttonName);

            database.Save(setting);

            // パネルの文字色・背景色は内部の操作画面へ反映する必要があるため、
            // 保存直後に内容を作り直す。通常ボタンは現在の表示を維持する。
            if (string.Equals(setting.ItemType, "DevicePanel",
                StringComparison.OrdinalIgnoreCase))
                ApplyButtonSetting(setting);

            // EditのDataGridへ通知
            NotifyButtonStateChanged(
                buttonName);
        }

        public void SaveButtonDisplayName(
            string buttonName,
            string displayName)
        {
            ButtonSetting? setting = database.Load(buttonName);
            if (setting == null)
                return;

            setting.NameContent = displayName;
            database.Save(setting);

            System.Windows.Controls.Button? button = GetButton(buttonName);
            if (button != null)
            {
                if (string.Equals(setting.ItemType, "DevicePanel",
                    StringComparison.OrdinalIgnoreCase))
                    button.Content = new DeviceCommandPanel(setting);
                else
                    button.Content = displayName;
            }

            NotifyButtonStateChanged(buttonName);
        }

        // ========================================
        // LiteDBから全Buttonを読み込み
        // ========================================
        public void LoadAllButtons()
        {
            // 動的ボタンだけを削除
            List<System.Windows.Controls.Button> currentButtons =
                MainCanvas.Children
                    .OfType<System.Windows.Controls.Button>()
                    .ToList();

            foreach (System.Windows.Controls.Button button in currentButtons)
            {
                MainCanvas.Children.Remove(button);
            }

            List<ButtonSetting> settings =
                database.LoadAll()
                    .OrderBy(setting => setting.ZIndex)
                    .ToList();

            foreach (ButtonSetting setting in settings)
            {
                CreateButton(setting);
            }
        }


        // ========================================
        // 特定ButtonのDB読み込み
        // ========================================
        public void LoadButton(string buttonName)
        {
            ButtonSetting? setting = database.Load(buttonName);

            if (setting == null)
                return;

            ApplyButtonSetting(setting);
        }

        // ========================================
        // EditWindowの位置を設定
        // ========================================
        private void SetEditWindowPosition()
        {
            if (editWindow == null)
                return;

            // 現在MainWindowがある画面の作業領域
            Rect workArea = SystemParameters.WorkArea;

            double margin = 10;

            // MainWindowの右側
            double rightX = Left + ActualWidth + margin;

            double y = Top;


            // ---------------------------------
            // 右側に置けるか確認
            // ---------------------------------

            if (rightX + editWindow.ActualWidth <= workArea.Right)
            {
                editWindow.Left = rightX;
            }
            else
            {
                // ---------------------------------
                // 右に置けない場合は左側
                // ---------------------------------

                double leftX =
                    Left
                    - editWindow.ActualWidth
                    - margin;

                if (leftX >= workArea.Left)
                {
                    editWindow.Left = leftX;
                }
                else
                {
                    // 左右どちらも厳しい場合
                    editWindow.Left =
                        Math.Max(
                            workArea.Left,
                            workArea.Right
                            - editWindow.ActualWidth);
                }
            }


            // ---------------------------------
            // 上下にはみ出さないようにする
            // ---------------------------------

            if (y < workArea.Top)
            {
                y = workArea.Top;
            }

            if (y + editWindow.ActualHeight > workArea.Bottom)
            {
                y = workArea.Bottom - editWindow.ActualHeight;
            }

            editWindow.Top = y;
        }

        // ========================================
        // MyData.dbのバックアップをエクスポートする処理
        // ========================================
        public void ExportDatabaseBackup(string destinationPath)
        {
            database.ExportBackup(destinationPath);
        }

        public void ExportApplicationBackup(string destinationPath)
        {
            AppBackupService.Export(
                destinationPath,
                database);
        }

        // ========================================
        // MyData.dbのインポート用
        // ========================================
        public async Task ImportApplicationBackupAsync(string sourcePath)
        {
            // 実行中の更新がDBへ書き込んでいる間は置換しない。
            DateTime waitLimit = DateTime.UtcNow.AddMinutes(1);
            while (isRefreshingServerSensors && DateTime.UtcNow < waitLimit)
                await Task.Delay(100);
            if (isRefreshingServerSensors)
                throw new TimeoutException(
                    "センサー更新の完了を待機中にタイムアウトしました。");

            isImportingApplicationBackup = true;
            serverSensorTimer.Stop();
            automationScheduleTimer.Stop();
            webhookRealtimeCancellation?.Cancel();

            bool importCompleted = false;
            try
            {
                // ZIP展開、LiteDB検証・置換、ログDBコピーはデータ量に
                // 比例して時間がかかるため、UIスレッドを占有しない。
                await Task.Run(() =>
                    AppBackupService.Import(sourcePath, database));

                Properties.Settings.Default.Reload();
                ApplyImportedSettings();
                LoadAllButtons();

                // バックアップ中の継続時間・実行済み状態を引き継がない。
                database.ResetAutomationRuntimeStates();

                // キャッシュを画面や自動化へ適用せず、各APIから現在値を取得。
                ReloadServerSensorSettingsAndRefresh(useCachedValues: false);
                await RefreshServerSensorsAsync(
                    forceSwitchBotRefresh: true,
                    switchBotOnly: false,
                    discardStaleValues: true);

                importCompleted = true;
            }
            finally
            {
                isImportingApplicationBackup = false;
                ReloadServerSensorSettingsAndRefresh(useCachedValues: true);
                ReloadWebhookRelaySettings();
                automationScheduleTimer.Start();
            }

            if (importCompleted)
            {
                // 最新値の保存が完了してから一度だけ判定する。
                await EvaluateSimpleAutomationsAsync(
                    allowWithoutSensorUpdates: true);
            }

            MainCanvas.InvalidateMeasure();
            MainCanvas.InvalidateArrange();
            MainCanvas.InvalidateVisual();
            UpdateLayout();
        }

        // ========================================
        // インポートされた設定を適用
        // ========================================
        private void ApplyImportedSettings()
        {
            // 最大化・最小化中なら通常状態へ戻す
            if (WindowState != WindowState.Normal)
            {
                WindowState =
                    WindowState.Normal;
            }

            LoadWindowSize();
            LoadBackgroundColor();

            string importedLanguage = Properties.Settings.Default.Language;
            if (!string.IsNullOrWhiteSpace(importedLanguage))
                LanguageManager.SetLanguage(importedLanguage);

            ReloadMouseGestureSettings();
            WindowsStartupManager.Apply(
                Properties.Settings.Default.StartWithWindows);

            DisableGlobalHotkey();

            if (Properties.Settings.Default
                .HotkeyEnabled)
            {
                LoadGlobalHotkey();
            }

            MainCanvas.InvalidateVisual();
        }


    }
}
