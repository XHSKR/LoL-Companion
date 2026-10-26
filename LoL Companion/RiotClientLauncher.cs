using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Management;

namespace LoL_Companion
{
    internal static class RiotClientLauncher
    {
        public static string ResolveExecutable(string leagueDirectory)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT ExecutablePath FROM Win32_Process WHERE Name='RiotClientServices.exe'"))
                using (var results = searcher.Get())
                    foreach (ManagementBaseObject result in results)
                    {
                        string executable = result["ExecutablePath"] as string;
                        if (File.Exists(executable)) return executable;
                    }
            }
            catch (ManagementException) { }
            catch (UnauthorizedAccessException) { }

            string installsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"Riot Games\RiotClientInstalls.json");
            if (File.Exists(installsFile))
            {
                var installs = JObject.Parse(File.ReadAllText(installsFile));
                foreach (string key in new[] { "rc_default", "rc_live" })
                {
                    string executable = (string)installs[key];
                    if (File.Exists(executable)) return executable;
                }
            }

            if (!string.IsNullOrWhiteSpace(leagueDirectory))
            {
                string executable = Path.GetFullPath(Path.Combine(leagueDirectory, @"..\Riot Client\RiotClientServices.exe"));
                if (File.Exists(executable)) return executable;
            }
            const string defaultExecutable = @"C:\Riot Games\Riot Client\RiotClientServices.exe";
            if (File.Exists(defaultExecutable)) return defaultExecutable;
            throw new FileNotFoundException("RiotClientServices.exe를 찾지 못했습니다. Riot Client 설치를 확인해 주세요.");
        }

    }
}
