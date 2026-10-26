// Port of riot_windows.rs clipboard_get / clipboard_set (MIT).
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LoL_Companion
{
    internal static class RiotLoginClipboard
    {
        [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("user32.dll")] private static extern bool EmptyClipboard();
        [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);

        public static string Get()
        {
            if (!OpenClipboard(IntPtr.Zero)) return null;
            try
            {
                IntPtr memory = GetClipboardData(13);
                if (memory == IntPtr.Zero) return null;
                IntPtr pointer = GlobalLock(memory);
                if (pointer == IntPtr.Zero) return null;
                try
                {
                    int length = 0;
                    while (length <= 4096 && Marshal.ReadInt16(pointer, length * 2) != 0) length++;
                    return Marshal.PtrToStringUni(pointer, length);
                }
                finally { GlobalUnlock(memory); }
            }
            finally { CloseClipboard(); }
        }

        public static bool Set(string text)
        {
            if (!OpenClipboard(IntPtr.Zero)) return false;
            IntPtr memory = IntPtr.Zero;
            try
            {
                EmptyClipboard();
                byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
                memory = GlobalAlloc(2, (UIntPtr)bytes.Length);
                if (memory == IntPtr.Zero) return false;
                IntPtr pointer = GlobalLock(memory);
                if (pointer == IntPtr.Zero) return false;
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
                GlobalUnlock(memory);
                if (SetClipboardData(13, memory) == IntPtr.Zero) return false;
                memory = IntPtr.Zero; // Ownership belongs to Windows after SetClipboardData.
                return true;
            }
            finally { if (memory != IntPtr.Zero) GlobalFree(memory); CloseClipboard(); }
        }
    }
}
