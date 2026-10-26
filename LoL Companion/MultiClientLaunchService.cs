using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LoL_Companion
{
    // Starts independent Riot sessions and stops as soon as the selected accounts are signed in.
    internal sealed class MultiClientLaunchService
    {
        internal static string[] SelectAccounts(IEnumerable<string> accounts, int count)
        {
            string[] selected = accounts.Where(account => !string.IsNullOrWhiteSpace(account))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (count < 1 || selected.Length != count)
                throw new InvalidOperationException("로그인할 계정을 하나 이상 체크해 주세요. 같은 계정은 중복 로그인할 수 없습니다.");
            return selected;
        }

        internal static ProcessStartInfo CreateStartInfo(string riotExecutable, int port, string secret,
            string data, string locale)
        {
            return new ProcessStartInfo
            {
                FileName = riotExecutable, UseShellExecute = false, CreateNoWindow = true,
                Arguments = "--allow-multiple-clients --app-port=" + port + " --remoting-auth-token=" + secret +
                    " --user-data-root=\"" + data + "\" --client-user-data-root=\"" + Path.Combine(data, "UX") + "\" --locale=" + locale
            };
        }

        internal static async Task<string> ReadLoggedInUsernameAsync(IRiotClientApi api, CancellationToken token)
        {
            var authentication = await api.SendAsync("GET", "/rso-auth/v1/session", null, token);
            if ((string)authentication?["type"] != "authenticated") return null;
            var session = await api.SendAsync("GET", "/player-session-lifecycle/v1/session", null, token);
            string state = (string)session?["loginState"];
            string username = (string)session?["userInfo"]?["username"];
            if ((state != "Authorized" && state != "PendingProductContext") ||
                (bool?)session?["actionRequired"] == true || !string.IsNullOrEmpty((string)session?["errorCode"]) ||
                string.IsNullOrWhiteSpace((string)session?["puuid"]) || string.IsNullOrWhiteSpace(username)) return null;
            return username;
        }

        internal static async Task WaitForLoginAsync(IRiotClientApi api, string username, CancellationToken token)
        {
            using (var login = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                login.CancelAfter(TimeSpan.FromMinutes(4));
                try
                {
                    while (true)
                    {
                        login.Token.ThrowIfCancellationRequested();
                        string actual = null;
                        try { actual = await ReadLoggedInUsernameAsync(api, login.Token); }
                        catch (System.Net.Http.HttpRequestException) { }
                        catch (RiotClientApiException ex) when (ex.StatusCode == 404 || ex.StatusCode == 503) { }
                        catch (OperationCanceledException) when (!login.IsCancellationRequested) { }
                        if (string.Equals(actual, username, StringComparison.OrdinalIgnoreCase)) return;
                        if (actual != null)
                            throw new InvalidOperationException("Riot Client에 로그인된 계정이 선택한 계정과 다릅니다.");
                        await Task.Delay(250, login.Token);
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { throw new TimeoutException("Riot Client에서 선택한 계정의 로그인을 확인하지 못했습니다."); }
            }
        }

        public async Task LaunchAsync(IEnumerable<string> accounts, int count, string password, string locale,
            string leagueDirectory, Action<string> progress, CancellationToken token)
        {
            string[] selected = SelectAccounts(accounts, count);
            if (string.IsNullOrEmpty(password) || (password == "password" && selected.Any(account => account.StartsWith("username"))))
                throw new InvalidOperationException("credentials.conf에 실제 로그인 계정과 공통 비밀번호를 입력해 주세요.");
            string riotExecutable = await Task.Run(() => RiotClientLauncher.ResolveExecutable(leagueDirectory), token);
            var running = await Task.Run(() => RiotClientDiscovery.DiscoverAll(), token);
            var alreadyLoggedIn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var instance in running)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using (var api = new RiotClientApi(instance.Connection))
                    {
                        string username = await ReadLoggedInUsernameAsync(api, token);
                        if (username != null) alreadyLoggedIn.Add(username);
                    }
                }
                catch (RiotClientApiException) { }
                catch (System.Net.Http.HttpRequestException) { }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            }
            for (int index = 0; index < selected.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                string prefix = (index + 1) + "/" + count + " " + selected[index] + ": ";
                try
                {
                    if (alreadyLoggedIn.Contains(selected[index]))
                    {
                        progress(prefix + "Riot Client에 이미 로그인되어 있습니다.");
                        continue;
                    }
                    await LoginOneAsync(riotExecutable, selected[index], password, locale,
                        message => progress(prefix + message), token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(prefix + ex.Message + "\n로그인한 Riot Client는 유지됩니다. 추가 로그인을 중단했습니다.", ex);
                }
            }
            progress(count + "개 계정 Riot Client 로그인 확인 완료");
        }

        private static async Task LoginOneAsync(string riotExecutable, string username, string password,
            string locale, Action<string> progress, CancellationToken token)
        {
            int port;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try { port = ((IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
            string secret = Guid.NewGuid().ToString("N");
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LoL Companion", "Clients", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(data);
            var connection = RiotClientConnection.Create(port, secret);
            progress("독립 Riot Client 실행 중...");
            using (var process = Process.Start(CreateStartInfo(riotExecutable, port, secret, data, locale))) { }
            using (var api = new RiotClientApi(connection))
            {
                progress("실행한 Riot Client의 로그인 창 준비 및 계정 입력 중...");
                await RiotLoginUi.SubmitForClientAsync(username, password, port, token, progress);
                progress("Riot 로그인 완료 확인 중... (추가 인증은 해당 Riot 창에서 완료해 주세요)");
                await WaitForLoginAsync(api, username, token);
                progress("Riot Client 로그인 완료");
            }
        }
    }
}
