// C# port of riot_windows.rs at e822ff4f4ef049be13f07241e1f30477c230c88f.
// Copyright (c) 2024 Riot Account Manager. MIT: see THIRD_PARTY_NOTICES.md.
using Interop.UIAutomationClient;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WindowsInput;
using WindowsInput.Native;

namespace LoL_Companion
{
    internal static class RiotLoginUi
    {
        private const string Probe = "§ram§";
        public static Task SubmitForClientAsync(string username, string password, int? riotPort, CancellationToken token,
            Action<string> progress = null)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var context = SynchronizationContext.Current;
            Action<string> report = message =>
            {
                if (progress == null) return;
                if (context == null) progress(message);
                else context.Post(_ => progress(message), null);
            };
            var thread = new Thread(() =>
            {
                IntPtr dpi = IntPtr.Zero;
                try
                {
                    try { dpi = RiotLoginWindow.SetThreadDpiAwarenessContext(new IntPtr(-4)); }
                    catch (EntryPointNotFoundException) { }
                    Submit(username, password, riotPort, token, report);
                    completion.TrySetResult(true);
                }
                catch (OperationCanceledException) { completion.TrySetCanceled(); }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally { if (dpi != IntPtr.Zero) RiotLoginWindow.SetThreadDpiAwarenessContext(dpi); }
            }) { IsBackground = true, Name = "Riot login input" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completion.Task;
        }

        private static void Submit(string username, string password, int? riotPort, CancellationToken token, Action<string> progress)
        {
            var input = new InputSimulator();
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            IntPtr window = IntPtr.Zero;
            uint owner = 0;
            int width = 0, height = 0, probeOffset = 0;
            TimeSpan stableSince = TimeSpan.Zero;
            TimeSpan nextOwnerLookup = TimeSpan.Zero;
            HashSet<uint> allowedProcesses = null;
            bool credentialsStarted = false;
            Action inputReady = () => { progress("로그인 입력칸 확인 완료. 계정 입력 중..."); credentialsStarted = true; };
            while (elapsed.Elapsed < TimeSpan.FromSeconds(60))
            {
                token.ThrowIfCancellationRequested();
                // A known window can be checked natively. Do not repeat WMI queries
                // for every visible Riot window on every readiness poll.
                if (!RiotLoginWindow.IsOwnedWindow(window, owner))
                {
                    if (riotPort.HasValue && (allowedProcesses == null || elapsed.Elapsed >= nextOwnerLookup))
                    {
                        allowedProcesses = RiotLoginWindow.FindOwnerProcesses(riotPort.Value);
                        nextOwnerLookup = elapsed.Elapsed + TimeSpan.FromMilliseconds(500);
                    }
                    window = RiotLoginWindow.Find(riotPort, allowedProcesses);
                    owner = RiotLoginWindow.GetOwnerProcessId(window);
                    width = height = probeOffset = 0;
                    stableSince = elapsed.Elapsed;
                }
                RiotLoginWindow.Rect size;
                if (window == IntPtr.Zero || !RiotLoginWindow.GetClientRect(window, out size) ||
                    size.Right - size.Left < 640 || size.Bottom - size.Top < 400)
                {
                    // The splash and sign-in views can use different native windows.
                    window = IntPtr.Zero;
                    Pause(150, token);
                    continue;
                }
                if (width != size.Right - size.Left || height != size.Bottom - size.Top)
                {
                    width = size.Right - size.Left;
                    height = size.Bottom - size.Top;
                    probeOffset = 0;
                    stableSince = elapsed.Elapsed;
                    RiotLoginWindow.RequestAccessibility(window);
                }
                try
                {
                    // Visible, enabled login fields are the readiness signal. They
                    // do not require a fixed window-stability delay.
                    if (FillViaUia(window, username, password, input, token, inputReady))
                    { progress("로그인 화면에 계정 입력 및 제출 완료"); return; }
                }
                catch (COMException) when (!credentialsStarted) { }
                catch (InvalidOperationException) when (!credentialsStarted) { }
                if (!RiotLoginWindow.IsOwnedWindow(window, owner))
                {
                    window = IntPtr.Zero;
                    continue;
                }
                try
                {
                    // Some Riot builds expose no UIA edit fields. Inspect a small
                    // batch of marker points, then check accessibility again.
                    if (elapsed.Elapsed - stableSince >= TimeSpan.FromMilliseconds(750) &&
                        FillViaProbe(window, username, password, input, token, ref probeOffset, inputReady))
                    { progress("로그인 화면에 계정 입력 및 제출 완료"); return; }
                }
                catch (COMException) when (!credentialsStarted) { }
                catch (InvalidOperationException) when (!credentialsStarted) { }
                Pause(150, token);
            }
            throw new TimeoutException("Riot Client의 로그인 입력칸이 60초 안에 준비되지 않았습니다.");
        }

        private static bool FillViaUia(IntPtr window, string username, string password, InputSimulator input, CancellationToken token,
            Action inputReady)
        {
            IUIAutomation automation = new CUIAutomation();
            var root = automation.ElementFromHandle(window);
            var array = root.FindAll(TreeScope.TreeScope_Descendants, automation.CreatePropertyCondition(30003, 50004));
            var elements = new List<IUIAutomationElement>();
            var fields = new List<RiotLoginField>();
            for (int i = 0; i < array.Length; i++)
            {
                try
                {
                    var element = array.GetElement(i);
                    fields.Add(new RiotLoginField { Name = element.CurrentName,
                        IsPassword = element.CurrentIsPassword != 0, Offscreen = element.CurrentIsOffscreen != 0,
                        Enabled = element.CurrentIsEnabled != 0 });
                    elements.Add(element);
                }
                catch (COMException) { }
            }
            var selected = RiotLoginFields.Pick(fields);
            if (selected == null) return false;
            RiotLoginWindow.StealForeground(window, input, token);
            FocusAndType(window, input, elements[selected.Item1], username, token, inputReady);
            Pause(160, token);
            FocusAndType(window, input, elements[selected.Item2], password, token);
            Pause(150, token);
            RequireForeground(window);
            input.Keyboard.KeyPress(VirtualKeyCode.RETURN);
            return true;
        }

        private static void FocusAndType(IntPtr window, InputSimulator input, IUIAutomationElement element, string text, CancellationToken token,
            Action inputReady = null)
        {
            bool focused = false;
            RiotLoginWindow.StealForeground(window, input, token);
            try { element.SetFocus(); focused = element.CurrentHasKeyboardFocus != 0; } catch (COMException) { }
            try
            {
                var r = element.CurrentBoundingRectangle;
                int width = r.right - r.left, height = r.bottom - r.top;
                if (!focused && (width <= 8 || height <= 8)) throw new InvalidOperationException("empty field rectangle");
                if (!focused)
                {
                    RiotLoginWindow.SetCursorPos(r.left + width / 2, r.top + height / 2);
                    Pause(40, token);
                    input.Mouse.LeftButtonClick();
                }
            }
            catch (COMException) { }
            Pause(180, token);
            if (element.CurrentHasKeyboardFocus == 0)
                throw new InvalidOperationException("Riot 로그인 입력칸에 포커스를 맞추지 못했습니다. 해당 창을 확인해 주세요.");
            RequireForeground(window);
            inputReady?.Invoke();
            TypeIntoFocused(window, input, text, token);
        }

        private static bool FillViaProbe(IntPtr window, string username, string password, InputSimulator input, CancellationToken token,
            ref int offset, Action inputReady)
        {
            string saved = RiotLoginClipboard.Get();
            Point[] points = GetProbePoints(window);
            Point? found = null;
            try
            {
                int start = offset;
                int end = Math.Min(start + 4, points.Length);
                for (int i = start; i < end; i++)
                {
                    token.ThrowIfCancellationRequested();
                    RiotLoginWindow.StealForeground(window, input, token);
                    RiotLoginWindow.SetCursorPos(points[i].X, points[i].Y);
                    Pause(40, token);
                    input.Mouse.LeftButtonClick();
                    Pause(90, token);
                    RiotLoginClipboard.Set(string.Empty);
                    TypeIntoFocused(window, input, Probe, token);
                    input.Keyboard.KeyDown(VirtualKeyCode.CONTROL).KeyPress(VirtualKeyCode.VK_A);
                    Pause(30, token);
                    input.Keyboard.KeyPress(VirtualKeyCode.VK_C).KeyUp(VirtualKeyCode.CONTROL);
                    Pause(40, token);
                    Pause(50, token);
                    if ((RiotLoginClipboard.Get() ?? string.Empty).Contains(Probe)) { found = points[i]; break; }
                }
                offset = end >= points.Length ? 0 : end;
                if (!found.HasValue) return false;
                RiotLoginWindow.StealForeground(window, input, token);
                RiotLoginWindow.SetCursorPos(found.Value.X, found.Value.Y);
                Pause(40, token);
                input.Mouse.LeftButtonClick();
                Pause(80, token);
                RequireForeground(window);
                inputReady();
                TypeIntoFocused(window, input, username, token);
                input.Keyboard.KeyPress(VirtualKeyCode.TAB);
                Pause(160, token);
                TypeIntoFocused(window, input, password, token);
                Pause(150, token);
                RequireForeground(window);
                input.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                return true;
            }
            finally { if (saved != null) RiotLoginClipboard.Set(saved); }
        }

        private static Point[] GetProbePoints(IntPtr window)
        {
            RiotLoginWindow.Rect rectangle;
            var origin = new RiotLoginWindow.NativePoint();
            if (!RiotLoginWindow.GetClientRect(window, out rectangle) || !RiotLoginWindow.ClientToScreen(window, ref origin))
                throw new InvalidOperationException("Could not read Riot Client window size");
            return GetProbePoints(rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top,
                Math.Max(96u, RiotLoginWindow.GetDpiForWindow(window)), new Point(origin.X, origin.Y));
        }

        internal static Point[] GetProbePoints(int width, int height, uint dpi = 96, Point origin = default(Point))
        {
            if (width < 200 || height < 200) throw new InvalidOperationException("Could not read Riot Client window size");
            double scale = Math.Max(96u, dpi) / 96.0;
            int column = Math.Max(Math.Min((int)(480 * scale), width * 45 / 100), (int)(280 * scale));
            var points = new List<Point>();
            int[] xs = { column / 2, column / 3, column * 2 / 3, (int)(120 * scale) };
            foreach (double ratio in new[] { .32, .28, .36, .24, .40, .20 })
                foreach (int x in xs) points.Add(new Point(origin.X + x, origin.Y + (int)(height * ratio)));
            foreach (int cssY in new[] { 180, 220, 260, 300, 340 })
            {
                int y = (int)(cssY * scale);
                if (y > 40 && y < height - 40) points.Add(new Point(origin.X + column / 2, origin.Y + y));
            }
            for (int i = points.Count - 1; i > 0; i--) if (points[i] == points[i - 1]) points.RemoveAt(i);
            return points.ToArray();
        }

        private static void TypeIntoFocused(IntPtr window, InputSimulator input, string text, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            RequireForeground(window);
            input.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_A);
            Pause(50, token);
            input.Keyboard.KeyPress(VirtualKeyCode.DELETE);
            Pause(80, token);
            RequireForeground(window);
            input.Keyboard.TextEntry(text);
            Pause(120, token);
        }

        private static void Pause(int milliseconds, CancellationToken token)
        { if (token.WaitHandle.WaitOne(milliseconds)) token.ThrowIfCancellationRequested(); }

        private static void RequireForeground(IntPtr window)
        {
            if (window == IntPtr.Zero || RiotLoginWindow.GetForegroundWindow() != window)
                throw new InvalidOperationException("Riot 로그인 창이 활성 상태가 아니어서 입력을 중단했습니다. 해당 창을 확인해 주세요.");
        }
    }
}
