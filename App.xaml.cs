using System.Configuration;
using System.Data;
using System.Windows;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Globalization;


namespace N_Remo
{
    public partial class App : System.Windows.Application
    {

        static App()
        {
            // Windows App SDKを単一ファイルから利用するため、ネイティブ
            // ランタイムの基準フォルダーを通知APIの初期化前に指定する。
            Environment.SetEnvironmentVariable(
                "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY",
                AppContext.BaseDirectory);
        }

        private Mutex? singleInstanceMutex;
        private bool ownsMutex;




        protected override async void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (_, eventArgs) =>
                AutomationDiagnosticLog.Write(
                    $"UI未処理例外: {eventArgs.Exception}");
            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
                AutomationDiagnosticLog.Write(
                    $"未処理例外: {eventArgs.ExceptionObject}");
            TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
                AutomationDiagnosticLog.Write(
                    $"未監視Task例外: {eventArgs.Exception}");

            // 単一EXEの保存場所に依存しない設定を読み込む
            PersistentAppSettings.LoadIntoProperties();
            WindowsStartupManager.Apply(
                N_Remo.Properties.Settings.Default.StartWithWindows);

            string language = N_Remo.Properties.Settings.Default.Language;
            if (string.IsNullOrWhiteSpace(language))
            {
                language = CultureInfo.CurrentUICulture.Name
                    .StartsWith("ja", StringComparison.OrdinalIgnoreCase)
                        ? "ja-JP"
                        : "en-US";
                N_Remo.Properties.Settings.Default.Language = language;
                N_Remo.Properties.Settings.Default.Save();
                PersistentAppSettings.SaveFromProperties();
            }

            try
            {
                LanguageManager.Initialize(language);
            }
            catch (Exception ex)
            {
                // 言語辞書の問題だけでアプリ全体を起動不能にしない。
                AutomationDiagnosticLog.Write(
                    $"起動時の言語初期化に失敗。日本語へ復旧: {ex}");
                language = "ja-JP";
                N_Remo.Properties.Settings.Default.Language = language;
                N_Remo.Properties.Settings.Default.Save();
                PersistentAppSettings.SaveFromProperties();
                LanguageManager.Initialize(language);
            }

            // ========================================
            // 引数付き起動：赤外線を送信して終了
            // ========================================
            if (TryGetSendButtonName(
                    e.Args,
                    out string buttonName))
            {
                // 引数付き起動は二重起動制限の対象外
                ShutdownMode =
                    ShutdownMode.OnExplicitShutdown;

                base.OnStartup(e);

                await SendAndExitAsync(buttonName);
                return;
            }

            // ========================================
            // 通常起動：二重起動を抑止
            // ========================================
            const string mutexName =
                @"Local\N-Remo_SingleInstance";

            singleInstanceMutex =
                new Mutex(
                    initiallyOwned: true,
                    name: mutexName,
                    createdNew: out bool createdNew);

            if (!createdNew)
            {
                System.Windows.MessageBox.Show(
                    "N-Remoはすでに起動しています。",
                    "N-Remo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Shutdown();
                return;
            }

            ownsMutex = true;

            base.OnStartup(e);

            if (!NativeNotificationService.TryRegister(
                    out string notificationRegistrationError))
            {
                AutomationDiagnosticLog.Write(
                    $"Windowsネイティブ通知を利用できません: {notificationRegistrationError}");
            }

            // ========================================
            // 通常起動
            // ========================================
            MainWindow mainWindow =
                new MainWindow();

            MainWindow = mainWindow;
            mainWindow.Show();
            if (N_Remo.Properties.Settings.Default.CloseToTray &&
                N_Remo.Properties.Settings.Default.StartMinimizedToTray)
                mainWindow.Hide();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AutomationDiagnosticLog.Write(
                $"N-Remo終了。終了コード={e.ApplicationExitCode}");
            try
            {
                PersistentAppSettings.SaveFromProperties();
            }
            catch
            {
                // 終了処理は妨げない
            }

            if (ownsMutex)
            {
                singleInstanceMutex?.ReleaseMutex();
            }

            singleInstanceMutex?.Dispose();
            singleInstanceMutex = null;

            NativeNotificationService.Unregister();

            base.OnExit(e);
        }

        private static bool TryGetSendButtonName(
            string[] arguments,
            out string buttonName)
        {
            buttonName = "";

            if (arguments.Length < 2)
            {
                return false;
            }

            if (!string.Equals(
                    arguments[0],
                    "--send",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            buttonName =
                arguments[1].Trim();

            return !string.IsNullOrWhiteSpace(
                buttonName);
        }

        private async Task SendAndExitAsync(
            string buttonName)
        {
            try
            {
                await NatureRemoSender.SendAsync(
                    buttonName);

                // 成功時は何も表示せず終了
                Shutdown(0);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"コマンド送信に失敗しました。\n" +
                    ex.Message,
                    "N-Remo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown(1);
            }
        }
    }
}
