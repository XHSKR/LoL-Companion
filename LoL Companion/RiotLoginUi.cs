using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WindowsInput;
using WindowsInput.Native;

namespace LoL_Companion
{
    // CEF/D3D login fields are not always exposed through Windows accessibility.
    // Confirm a copyable text input with a non-secret marker before entering credentials.
    internal static class RiotLoginUi
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rectangle);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        public static Task SubmitAsync(string username, string password, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { Submit(username, password, cancellationToken); completion.TrySetResult(true); }
                catch (OperationCanceledException) { completion.TrySetCanceled(); }
                catch (Exception ex) { completion.TrySetException(ex); }
            }) { IsBackground = true, Name = "Riot login input" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completion.Task;
        }

        private static void Submit(string username, string password, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                throw new InvalidOperationException("로그인 ID와 비밀번호가 필요합니다.");
            IntPtr previousDpi = IntPtr.Zero;
            try
            {
                try { previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4)); }
                catch (EntryPointNotFoundException) { }
                IntPtr window = FindWindow();
                if (window == IntPtr.Zero)
                    throw new InvalidOperationException("Riot 로그인 창을 찾지 못했습니다. Riot Client를 열고 다시 시도해 주세요.");
                Focus(window);
                Pause(250, cancellationToken);
                Rect client;
                var origin = new NativePoint();
                if (!GetClientRect(window, out client) || !ClientToScreen(window, ref origin))
                    throw new InvalidOperationException("Riot 로그인 창의 위치를 읽지 못했습니다.");
                int width = client.Right - client.Left;
                int height = client.Bottom - client.Top;
                var input = new InputSimulator();
                IDataObject originalClipboard = Clipboard.GetDataObject();
                bool clipboardChanged = false;
                try
                {
                    bool found = false;
                    foreach (Point point in GetProbePoints(width, height))
                    {
                        CheckTarget(window, cancellationToken);
                        SetCursorPos(origin.X + point.X, origin.Y + point.Y);
                        input.Mouse.LeftButtonClick();
                        Pause(100, cancellationToken);
                        string marker = "LC" + Guid.NewGuid().ToString("N").Substring(0, 10);
                        clipboardChanged = true;
                        Clipboard.SetText("LC-empty");
                        CheckTarget(window, cancellationToken);
                        input.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_A);
                        input.Keyboard.TextEntry(marker);
                        Pause(70, cancellationToken);
                        input.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_A);
                        input.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);
                        Pause(70, cancellationToken);
                        CheckTarget(window, cancellationToken);
                        found = Clipboard.ContainsText() && Clipboard.GetText() == marker;
                        if (found) break;
                        input.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_A);
                        input.Keyboard.KeyPress(VirtualKeyCode.DELETE);
                    }
                    if (!found)
                        throw new InvalidOperationException("Riot 로그인 ID 입력칸을 확인하지 못했습니다. Sign-in 화면에서 다시 시도해 주세요.");

                    CheckTarget(window, cancellationToken);
                    input.Keyboard.TextEntry(username); // The verified marker is still selected.
                    input.Keyboard.KeyPress(VirtualKeyCode.TAB);
                    Pause(120, cancellationToken);
                    CheckTarget(window, cancellationToken);
                    input.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_A);
                    input.Keyboard.TextEntry(password);
                    Pause(80, cancellationToken);
                    CheckTarget(window, cancellationToken);
                    input.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                }
                finally
                {
                    if (clipboardChanged)
                    {
                        if (originalClipboard != null) Clipboard.SetDataObject(originalClipboard, true);
                        else Clipboard.Clear();
                    }
                }
            }
            finally
            {
                if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi);
            }
        }

        internal static Point[] GetProbePoints(int width, int height)
        {
            if (width < 640 || height < 400)
                throw new InvalidOperationException("Riot 로그인 창이 너무 작거나 아직 준비되지 않았습니다.");
            // Scale with the actual client area; the first point matches the current Sign-in layout.
            double[] rows = { 0.30, 0.28, 0.32, 0.26, 0.34 };
            var points = new Point[rows.Length];
            for (int i = 0; i < rows.Length; i++)
                points[i] = new Point((int)(width * 0.13), (int)(height * rows[i]));
            return points;
        }

        private static void CheckTarget(IntPtr window, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RiotClientLauncher.EnsureGameIsNotRunning();
            if (GetForegroundWindow() != window)
                throw new InvalidOperationException("Riot 로그인 중 다른 창으로 포커스가 이동해 입력을 중단했습니다.");
        }

        private static void Pause(int milliseconds, CancellationToken cancellationToken)
        {
            if (cancellationToken.WaitHandle.WaitOne(milliseconds)) cancellationToken.ThrowIfCancellationRequested();
        }

        private static IntPtr FindWindow()
        {
            foreach (string name in new[] { "Riot Client", "RiotClientUx" })
                foreach (Process process in Process.GetProcessesByName(name))
                    using (process)
                        if (process.MainWindowHandle != IntPtr.Zero &&
                            process.MainWindowTitle.IndexOf("Riot", StringComparison.OrdinalIgnoreCase) >= 0)
                            return process.MainWindowHandle;
            return IntPtr.Zero;
        }

        private static void Focus(IntPtr window)
        {
            RiotClientLauncher.EnsureGameIsNotRunning();
            uint ignored;
            uint current = GetCurrentThreadId();
            uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out ignored);
            uint target = GetWindowThreadProcessId(window, out ignored);
            try
            {
                if (foreground != current) AttachThreadInput(current, foreground, true);
                if (target != current && target != foreground) AttachThreadInput(current, target, true);
                ShowWindow(window, 9);
                SetForegroundWindow(window);
            }
            finally
            {
                if (target != current && target != foreground) AttachThreadInput(current, target, false);
                if (foreground != current) AttachThreadInput(current, foreground, false);
            }
            if (GetForegroundWindow() != window)
                throw new InvalidOperationException("Riot 로그인 창을 활성화하지 못했습니다. Riot 창을 선택하고 다시 시도해 주세요.");
        }
    }
}
