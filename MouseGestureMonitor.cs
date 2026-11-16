using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace N_Remo
{
    public sealed class MouseGestureMonitor :
        IDisposable
    {
        private const int WhMouseLl = 14;

        private const int WmMouseMove = 0x0200;
        private const int WmLButtonDown = 0x0201;
        private const int WmRButtonDown = 0x0204;
        private const int WmRButtonUp = 0x0205;

        private readonly Action gestureDetected;
        private readonly LowLevelMouseProc mouseProc;

        private IntPtr hookHandle = IntPtr.Zero;

        private bool wasInCorner;
        private bool rightButtonDown;
        private int rightClickCount;

        private DateTime lastRightClickTime;
        private DateTime rightButtonDownTime;
        private DateTime lastGestureTime;

        public MouseGestureMonitor(
            Action gestureDetected)
        {
            this.gestureDetected =
                gestureDetected;

            mouseProc = HookCallback;
        }

        public void Start()
        {
            if (hookHandle != IntPtr.Zero)
                return;

            using Process process =
                Process.GetCurrentProcess();

            using ProcessModule? module =
                process.MainModule;

            IntPtr moduleHandle =
                GetModuleHandle(
                    module?.ModuleName);

            hookHandle =
                SetWindowsHookEx(
                    WhMouseLl,
                    mouseProc,
                    moduleHandle,
                    0);

            if (hookHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "マウス監視を開始できませんでした。");
            }
        }

        public void Stop()
        {
            if (hookHandle == IntPtr.Zero)
                return;

            UnhookWindowsHookEx(
                hookHandle);

            hookHandle = IntPtr.Zero;
        }

        private IntPtr HookCallback(
            int nCode,
            IntPtr wParam,
            IntPtr lParam)
        {
            if (nCode >= 0)
            {
                MouseHookStruct mouseInfo =
                    Marshal.PtrToStructure
                        <MouseHookStruct>(lParam);

                bool inCorner =
                    IsTargetCorner(
                        mouseInfo.Point.X,
                        mouseInfo.Point.Y);

                string action =
                    Properties.Settings.Default
                        .MouseGestureAction
                    ?? "MoveToCorner";

                int message =
                    wParam.ToInt32();

                if (message == WmMouseMove)
                {
                    string targetArea =
                        Properties.Settings.Default
                            .MouseGestureCorner
                        ?? "BottomRight";

                    if (action == "MoveToCorner")
                    {
                        if (targetArea == "Anywhere")
                        {
                            // 画面内でマウスが動いたら通知
                            // RaiseGesture内の連続実行防止が働く
                            RaiseGesture();
                        }
                        else if (inCorner &&
                                 !wasInCorner)
                        {
                            // 指定した画面隅へ入ったとき
                            RaiseGesture();
                        }
                    }

                    wasInCorner = inCorner;
                }
                else if (message == WmRButtonDown)
                {
                    rightButtonDown = true;
                    rightButtonDownTime =
                        DateTime.Now;
                }
                else if (message == WmLButtonDown)
                {
                    if (!inCorner)
                    {
                        // 何もしない
                    }
                    else if (action == "LeftClick")
                    {
                        RaiseGesture();
                    }
                    else if (
                        action == "RightLeftClick" &&
                        rightButtonDown)
                    {
                        RaiseGesture();
                    }
                }
                else if (message == WmRButtonUp)
                {
                    TimeSpan pressDuration =
                        DateTime.Now -
                        rightButtonDownTime;

                    rightButtonDown = false;

                    if (inCorner &&
                        action == "RightLongPress" &&
                        pressDuration.TotalMilliseconds >=
                        Properties.Settings.Default
                            .MouseLongPressMilliseconds)
                    {
                        RaiseGesture();
                    }

                    ProcessRightClick(
                        action,
                        inCorner);
                }
            }

            return CallNextHookEx(
                hookHandle,
                nCode,
                wParam,
                lParam);
        }

        private void ProcessRightClick(
            string action,
            bool inCorner)
        {
            if (!inCorner)
            {
                rightClickCount = 0;
                return;
            }

            DateTime now =
                DateTime.Now;

            double elapsed =
                (now - lastRightClickTime)
                    .TotalMilliseconds;

            if (elapsed <=
                WinForms.SystemInformation
                    .DoubleClickTime)
            {
                rightClickCount++;
            }
            else
            {
                rightClickCount = 1;
            }

            lastRightClickTime = now;

            if (action == "RightDoubleClick" &&
                rightClickCount == 2)
            {
                rightClickCount = 0;
                RaiseGesture();
            }
            else if (
                action == "RightTripleClick" &&
                rightClickCount == 3)
            {
                rightClickCount = 0;
                RaiseGesture();
            }
        }

        private bool IsTargetCorner(
    int x,
    int y)
        {
            string corner =
                Properties.Settings.Default
                    .MouseGestureCorner
                ?? "BottomRight";

            // 画面のどこでも有効
            if (corner == "Anywhere")
                return true;

            var point =
                new System.Drawing.Point(x, y);

            var screen =
                WinForms.Screen.FromPoint(point);

            var bounds =
                screen.Bounds;

            int margin =
                Math.Max(
                    1,
                    Properties.Settings.Default
                        .MouseCornerMargin);

            return corner switch
            {
                "TopLeft" =>
                    x <= bounds.Left + margin &&
                    y <= bounds.Top + margin,

                "TopRight" =>
                    x >= bounds.Right - margin &&
                    y <= bounds.Top + margin,

                "BottomLeft" =>
                    x <= bounds.Left + margin &&
                    y >= bounds.Bottom - margin,

                "BottomRight" =>
                    x >= bounds.Right - margin &&
                    y >= bounds.Bottom - margin,

                _ => false
            };
        }

        private void RaiseGesture()
        {
            // 同じ操作による連続表示を防止
            if ((DateTime.Now - lastGestureTime)
                    .TotalMilliseconds < 500)
            {
                return;
            }

            lastGestureTime =
                DateTime.Now;

            gestureDetected.Invoke();
        }

        public void Dispose()
        {
            Stop();
        }

        private delegate IntPtr LowLevelMouseProc(
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct MousePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseHookStruct
        {
            public MousePoint Point;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int hookId,
            LowLevelMouseProc hookFunction,
            IntPtr moduleHandle,
            uint threadId);

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(
            IntPtr hookHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hookHandle,
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(
            string? moduleName);
    }
}