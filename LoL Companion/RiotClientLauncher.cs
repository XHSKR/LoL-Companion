using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

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

        public static void Start(string executable, string locale, bool launchLeague)
        {
            using (Process process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = (launchLeague ? "--launch-product=league_of_legends --launch-patchline=live " : string.Empty) +
                    "--locale=" + locale,
                UseShellExecute = false,
                CreateNoWindow = true
            })) { }
        }

        public static async Task<RiotClientConnection> WaitForConnectionAsync(CancellationToken cancellationToken)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    while (true)
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        try
                        {
                            RiotClientConnection connection = await Task.Run(() => RiotClientConnection.Discover(), timeout.Token);
                            using (var api = new RiotClientApi(connection))
                                await api.SendAsync("GET", "/rso-auth/v1/session", null, timeout.Token);
                            return connection;
                        }
                        catch (Exception ex) when (ex is InvalidOperationException || ex is System.Net.Http.HttpRequestException ||
                            ex is ManagementException || (ex is OperationCanceledException && !timeout.IsCancellationRequested)) { }
                        await Task.Delay(500, timeout.Token);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("Riot Client가 30초 안에 준비되지 않았습니다. 클라이언트 업데이트나 실행 상태를 확인해 주세요.");
                }
            }
        }

        public static void EnsureGameIsNotRunning()
        {
            Process[] games = Process.GetProcessesByName("League of Legends");
            try
            {
                if (games.Length != 0)
                    throw new InvalidOperationException("게임 실행 중에는 계정을 전환할 수 없습니다. 게임을 종료한 후 다시 시도해 주세요.");
            }
            finally { foreach (Process game in games) game.Dispose(); }
        }

        public static async Task CloseLeagueClientAsync(CancellationToken cancellationToken)
        {
            EnsureGameIsNotRunning();
            foreach (string name in new[] { "LeagueClient", "LeagueClientUx", "LeagueClientUxRender" })
                foreach (Process process in Process.GetProcessesByName(name))
                    using (process)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        EnsureGameIsNotRunning();
                        try
                        {
                            process.Kill();
                            for (int attempt = 0; !process.HasExited && attempt < 40; attempt++)
                                await Task.Delay(100, cancellationToken);
                            if (!process.HasExited)
                                throw new TimeoutException("기존 League Client가 종료되지 않았습니다.");
                        }
                        catch (InvalidOperationException) { /* The process already exited. */ }
                    }
        }
    }
}
