using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LoL_Companion
{
    internal static class LeagueInstallation
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(
            uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(
            SafeProcessHandle process, uint flags, StringBuilder fileName, ref int size);

        public static string Resolve(string fallback)
        {
            // League executables live in the game installation, unlike RiotClientServices.
            foreach (string processName in new[] { "LeagueClient", "LeagueClientUx" })
            {
                Process[] processes = Process.GetProcessesByName(processName);
                try
                {
                    foreach (Process process in processes)
                    {
                        string executable = TryGetExecutablePath(process.Id);
                        if (string.IsNullOrEmpty(executable))
                            continue;

                        string directory = Path.GetDirectoryName(executable);
                        if (!string.IsNullOrEmpty(directory) &&
                            File.Exists(Path.Combine(directory, "LeagueClient.exe")))
                        {
                            return directory + Path.DirectorySeparatorChar;
                        }
                    }
                }
                catch (Win32Exception)
                {
                    // The client may exit or deny access while it is being inspected.
                }
                catch (InvalidOperationException)
                {
                    // Keep the saved location if the process has already exited.
                }
                finally
                {
                    foreach (Process process in processes)
                        process.Dispose();
                }
            }

            return fallback;
        }

        private static string TryGetExecutablePath(int processId)
        {
            // Avoid MainModule, which also requests access to the process's modules.
            using (SafeProcessHandle handle = OpenProcess(
                ProcessQueryLimitedInformation, false, processId))
            {
                if (handle.IsInvalid)
                    return null;

                var buffer = new StringBuilder(32768);
                int size = buffer.Capacity;
                return QueryFullProcessImageName(handle, 0, buffer, ref size)
                    ? buffer.ToString()
                    : null;
            }
        }
    }
}
