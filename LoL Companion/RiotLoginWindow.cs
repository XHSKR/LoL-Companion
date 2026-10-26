// Port of DangNghia17/ChangeLogin_Account_RiotClient, riot_windows.rs.
// MIT license: see THIRD_PARTY_NOTICES.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using WindowsInput;
using WindowsInput.Native;

namespace LoL_Companion
{
    internal static class RiotLoginWindow
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct NativePoint { public int X, Y; }
        private delegate bool EnumWindow(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr data);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window, out Rect rectangle);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [StructLayout(LayoutKind.Sequential)]
        private struct Message
        {
            public IntPtr Window;
            public uint Id;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public NativePoint Point;
            public uint Private;
        }
        [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr window,
            uint minimum, uint maximum, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(
            IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        public static bool IsRunning()
        {
            foreach (string name in new[] { "RiotClientServices", "RiotClientUx", "RiotClient", "Riot Client" })
            {
                var processes = Process.GetProcessesByName(name);
                try { if (processes.Length > 0) return true; }
                finally { foreach (var process in processes) process.Dispose(); }
            }
            return false;
        }

        public static IntPtr Find(int? riotPort = null, ISet<uint> allowedProcesses = null)
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;
            int ownPid = Process.GetCurrentProcess().Id;
            EnumWindows((window, data) =>
            {
                if (!IsWindowVisible(window)) return true;
                uint pid;
                GetWindowThreadProcessId(window, out pid);
                if (pid == ownPid) return true;
                var title = new StringBuilder(512);
                if (GetWindowText(window, title, title.Capacity) <= 0 || !IsClientTitle(title.ToString())) return true;
                // Existing multi-client integration binds input to its own Riot instance.
                if (allowedProcesses != null && !allowedProcesses.Contains(pid)) return true;
                if (allowedProcesses == null && riotPort.HasValue && !HasPort(pid, riotPort.Value)) return true;
                Rect rectangle;
                if (!GetWindowRect(window, out rectangle)) return true;
                long area = (long)(rectangle.Right - rectangle.Left) * (rectangle.Bottom - rectangle.Top);
                if (area > bestArea) { best = window; bestArea = area; }
                return true;
            }, IntPtr.Zero);
            return best;
        }

        internal static HashSet<uint> FindOwnerProcesses(int port)
        {
            var owners = new HashSet<uint>();
            using (var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='Riot Client.exe' OR Name='RiotClientUx.exe' OR Name='RiotClient.exe'"))
            using (var rows = searcher.Get())
                foreach (ManagementBaseObject row in rows)
                {
                    string command = row["CommandLine"] as string;
                    if (RiotClientConnection.ReadArgument(command, "app-port") == port.ToString() ||
                        RiotClientConnection.ReadArgument(command, "riotclient-app-port") == port.ToString())
                        owners.Add(Convert.ToUInt32(row["ProcessId"]));
                }
            return owners;
        }

        internal static bool IsClientTitle(string title)
        {
            string value = title.ToLowerInvariant();
            return value.Contains("riot client") && !value.Contains("account manager");
        }

        internal static uint GetOwnerProcessId(IntPtr window)
        {
            uint processId = 0;
            if (window != IntPtr.Zero) GetWindowThreadProcessId(window, out processId);
            return processId;
        }

        internal static bool IsOwnedWindow(IntPtr window, uint processId)
        {
            return window != IntPtr.Zero && processId != 0 && IsWindowVisible(window) && GetOwnerProcessId(window) == processId;
        }

        private static bool HasPort(uint pid, int port)
        {
            using (var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + pid))
            using (var rows = searcher.Get())
                foreach (ManagementBaseObject row in rows)
                {
                    string command = row["CommandLine"] as string;
                    if (RiotClientConnection.ReadArgument(command, "app-port") == port.ToString() ||
                        RiotClientConnection.ReadArgument(command, "riotclient-app-port") == port.ToString()) return true;
                }
            return false;
        }

        public static bool ForceForeground(IntPtr window)
        {
            if (window == IntPtr.Zero) return false;
            if (GetForegroundWindow() == window) return true;
            // This runs on a dedicated STA worker. It must have a message queue
            // before Windows permits its input queue to attach to another thread.
            Message message;
            PeekMessage(out message, IntPtr.Zero, 0, 0, 0);
            ShowWindowAsync(window, IsIconic(window) ? 9 : 5);
            uint ignored;
            uint target = GetWindowThreadProcessId(window, out ignored);
            uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out ignored);
            uint own = GetCurrentThreadId();
            bool attachedForeground = foreground != 0 && foreground != own && AttachThreadInput(own, foreground, true);
            bool attachedTarget = target != 0 && target != own && target != foreground && AttachThreadInput(own, target, true);
            try { BringWindowToTop(window); SetForegroundWindow(window); }
            finally
            {
                if (attachedTarget) AttachThreadInput(own, target, false);
                if (attachedForeground) AttachThreadInput(own, foreground, false);
            }
            return GetForegroundWindow() == window;
        }

        public static void StealForeground(IntPtr window, InputSimulator input, CancellationToken token = default(CancellationToken))
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (GetForegroundWindow() == window || ForceForeground(window)) return;
                if (attempt % 5 == 0)
                {
                    input.Keyboard.KeyDown(VirtualKeyCode.MENU);
                    try { ForceForeground(window); }
                    finally { input.Keyboard.KeyUp(VirtualKeyCode.MENU); }
                }
                if (GetForegroundWindow() == window) return;
                if (token.WaitHandle.WaitOne(100)) token.ThrowIfCancellationRequested();
            }
            throw new InvalidOperationException("Riot 로그인 창을 활성화하지 못했습니다. 해당 Riot 창을 한 번 클릭한 후 다시 로그인해 주세요.");
        }

        public static void RequestAccessibility(IntPtr window)
        {
            IntPtr result;
            SendMessageTimeout(window, 0x003D, IntPtr.Zero, new IntPtr(-4), 2, 100, out result);
        }
    }
}
