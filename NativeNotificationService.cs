using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;

namespace N_Remo
{
    internal static class NativeNotificationService
    {
        private static readonly object SyncRoot = new();
        private static bool isRegistered;

        internal static bool TryRegister(out string errorMessage)
        {
            lock (SyncRoot)
            {
                if (isRegistered)
                {
                    errorMessage = "";
                    return true;
                }

                try
                {
                    AppNotificationManager manager =
                        AppNotificationManager.Default;

                    manager.NotificationInvoked +=
                        NotificationManager_NotificationInvoked;
                    manager.Register();

                    isRegistered = true;
                    errorMessage = "";
                    return true;
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                    AutomationDiagnosticLog.Write(
                        $"Windowsネイティブ通知の登録失敗: {ex}");
                    return false;
                }
            }
        }

        internal static bool TryShow(
            string title,
            string message,
            out string errorMessage)
        {
            if (!TryRegister(out errorMessage))
                return false;

            try
            {
                AppNotification notification =
                    new AppNotificationBuilder()
                        .AddText(title)
                        .AddText(message)
                        .BuildNotification();

                AppNotificationManager.Default.Show(notification);
                errorMessage = "";
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                AutomationDiagnosticLog.Write(
                    $"Windowsネイティブ通知の表示失敗: {ex}");
                return false;
            }
        }

        internal static void Unregister()
        {
            lock (SyncRoot)
            {
                if (!isRegistered)
                    return;

                try
                {
                    AppNotificationManager manager =
                        AppNotificationManager.Default;
                    manager.NotificationInvoked -=
                        NotificationManager_NotificationInvoked;
                    manager.Unregister();
                }
                catch (Exception ex)
                {
                    AutomationDiagnosticLog.Write(
                        $"Windowsネイティブ通知の登録解除失敗: {ex}");
                }
                finally
                {
                    isRegistered = false;
                }
            }
        }

        private static void NotificationManager_NotificationInvoked(
            AppNotificationManager sender,
            AppNotificationActivatedEventArgs args)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (System.Windows.Application.Current.MainWindow is not
                        System.Windows.Window window)
                        return;

                    if (!window.IsVisible)
                        window.Show();

                    if (window.WindowState ==
                        System.Windows.WindowState.Minimized)
                        window.WindowState =
                            System.Windows.WindowState.Normal;

                    window.Activate();
                });
            }
            catch (Exception ex)
            {
                AutomationDiagnosticLog.Write(
                    $"Windowsネイティブ通知クリック処理失敗: {ex}");
            }
        }
    }
}
