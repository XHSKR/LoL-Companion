using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LoL_Companion
{
    internal sealed class LeagueAccountSwitchService
    {
        private const string Product = "/product-launcher/v1/products/league_of_legends/patchlines/live";
        private readonly Func<RiotClientConnection, RiotClientApi> openApi;
        private readonly Func<Task<IReadOnlyList<LeagueClientConnection>>> discover;
        private readonly Func<int, bool> running;
        private readonly Func<LCU_Request, Task> dodge;

        public LeagueAccountSwitchService(Func<RiotClientConnection, RiotClientApi> openApi = null,
            Func<Task<IReadOnlyList<LeagueClientConnection>>> discover = null, Func<int, bool> running = null,
            Func<LCU_Request, Task> dodge = null)
        {
            this.openApi = openApi ?? (connection => new RiotClientApi(connection));
            this.discover = discover ?? (() => Task.Run(() => LeagueClientConnection.DiscoverAll()));
            this.running = running ?? IsLeagueRunning;
            this.dodge = dodge ?? DodgeWithoutWaitingAsync;
        }

        public async Task RunAsync(LeagueClientSession current, RiotClientInstance target, string username,
            Action<string> progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (current.Connection.RiotApi == null || current.Connection.LeagueProcessId <= 0)
                throw new InvalidOperationException("현재 League 클라이언트의 실행 정보를 확인하지 못했습니다.");
            if (current.Connection.RiotPort == target.Connection.Port)
                throw new InvalidOperationException("League가 실행 중이지 않은 다른 계정을 선택해 주세요.");
            var initial = await discover();
            if (initial.Count != 1 || initial[0].Key != current.Connection.Key)
                throw new InvalidOperationException("실행 중인 League 클라이언트가 변경되었습니다. 목록을 확인해 주세요.");
            using (var targetApi = openApi(target.Connection))
            using (var sourceApi = openApi(current.Connection.RiotApi))
            {
                string actual = await MultiClientLaunchService.ReadLoggedInUsernameAsync(targetApi, token);
                if (string.IsNullOrEmpty(username) || !string.Equals(actual, username, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("선택한 계정의 Riot 로그인을 확인하지 못했습니다.");
                var identity = await targetApi.SendAsync("GET", "/player-session-lifecycle/v1/session", null, token);
                string puuid = (string)identity?["puuid"];
                if (string.IsNullOrWhiteSpace(puuid)) throw new InvalidOperationException("선택한 계정의 식별 정보를 확인하지 못했습니다.");
                var sourceSummoner = await current.Request.GET("/lol-summoner/v1/current-summoner");
                if ((string)sourceSummoner?["puuid"] == puuid)
                    throw new InvalidOperationException("현재 League와 다른 계정을 선택해 주세요.");
                if ((string)await current.Request.GET_Value("/lol-gameflow/v1/gameflow-phase") != "ChampSelect")
                    throw new InvalidOperationException("현재 클라이언트가 챔피언 선택 중이 아닙니다.");

                progress("닷지 요청 중...");
                // QUIT_SUCCESS is sufficient here: the next operation closes League.
                await dodge(current.Request);
                token.ThrowIfCancellationRequested();
                progress("기존 League 종료 중...");
                if (running(current.Connection.LeagueProcessId))
                    await sourceApi.SendAsync("DELETE", Product, null, token);
                using (var closing = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    closing.CancelAfter(TimeSpan.FromSeconds(20));
                    try { while (running(current.Connection.LeagueProcessId)) await Task.Delay(75, closing.Token); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    { throw new TimeoutException("기존 League가 종료되지 않아 선택한 계정은 실행하지 않았습니다."); }
                }
                token.ThrowIfCancellationRequested();
                progress(username + " League 실행 중...");
                await targetApi.SendAsync("POST", Product, new JObject(), token);
                using (var startup = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    startup.CancelAfter(TimeSpan.FromMinutes(2));
                    try
                    {
                        while (true)
                        {
                            startup.Token.ThrowIfCancellationRequested();
                            var clients = await discover();
                            foreach (var client in clients.Where(client => client.RiotPort == target.Connection.Port))
                                try
                                {
                                    using (var lcu = openApi(client.Api))
                                    {
                                        var login = await lcu.SendAsync("GET", "/lol-login/v1/session", null, startup.Token);
                                        if ((string)login?["state"] != "SUCCEEDED") continue;
                                        var summoner = await lcu.SendAsync("GET", "/lol-summoner/v1/current-summoner", null, startup.Token);
                                        if ((string)summoner?["puuid"] != puuid)
                                            throw new InvalidOperationException("실행된 League 계정이 선택한 계정과 다릅니다.");
                                        progress(username + " League 실행 완료");
                                        return;
                                    }
                                }
                                catch (RiotClientApiException) { }
                                catch (System.Net.Http.HttpRequestException) { }
                                catch (OperationCanceledException) when (!startup.IsCancellationRequested) { }
                            await Task.Delay(150, startup.Token);
                        }
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    { throw new TimeoutException("선택한 계정의 League 로그인 완료를 확인하지 못했습니다."); }
                }
            }
        }

        private static async Task DodgeWithoutWaitingAsync(LCU_Request request)
        {
            using (var server = new LeagueEdgeClient(request))
                await new QuickDodgeService(request, server).DodgeAsync(false);
        }

        private static bool IsLeagueRunning(int processId)
        {
            var processes = Process.GetProcessesByName("LeagueClient");
            try { return processes.Any(process => process.Id == processId); }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }
}
