using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace LoL_Companion
{
    internal sealed class LeagueClientSession : IDisposable
    {
        public LeagueClientConnection Connection { get; }
        public LCU_Request Request { get; }
        public Websocket Events { get; }
        public string Name { get; private set; }
        public string Phase { get; private set; } = "Unavailable";
        public string Puuid { get; private set; }
        public string Error { get; private set; }
        private string acceptedCheck;
        private readonly SemaphoreSlim acceptGate = new SemaphoreSlim(1);

        public LeagueClientSession(LeagueClientConnection connection, LCU_Request request = null)
        {
            Connection = connection;
            Request = request ?? new LCU_Request(connection);
            Events = new Websocket(Request, phase =>
            {
                Phase = phase;
                if (phase != "ReadyCheck") acceptedCheck = null;
            });
            Name = "PID " + connection.ProcessId;
        }

        public async Task RefreshAsync(bool autoAccept)
        {
            try
            {
                Phase = (string)await Request.GET_Value("/lol-gameflow/v1/gameflow-phase");
                if (Phase != "ReadyCheck") acceptedCheck = null;
                if (autoAccept && Phase == "ReadyCheck") await AcceptAsync();
                Error = null;
            }
            catch (Exception ex)
            {
                Phase = "Unavailable";
                Error = DescribeError(ex);
                return;
            }
            try
            {
                JObject summoner = await Request.GET("/lol-summoner/v1/current-summoner");
                Puuid = (string)summoner["puuid"];
                Name = (string)summoner["gameName"] ?? (string)summoner["displayName"] ?? Name;
                string tag = (string)summoner["tagLine"];
                if (!string.IsNullOrEmpty(tag)) Name += "#" + tag;
            }
            catch (Exception ex)
            {
                Error = DescribeError(ex);
            }
        }

        internal static string DescribeError(Exception error)
        {
            for (Exception inner = error; inner != null; inner = inner.InnerException)
            {
                var web = inner as System.Net.WebException;
                if (web != null && (web.Status == System.Net.WebExceptionStatus.SecureChannelFailure ||
                    web.Status == System.Net.WebExceptionStatus.TrustFailure)) return "LCU SSL/TLS 연결 실패";
            }
            if (error is OperationCanceledException) return "LCU 응답 시간 초과";
            if (error is System.Net.Http.HttpRequestException)
                return error.InnerException == null ? error.Message : "LCU 연결 실패";
            return error.GetType().Name;
        }

        public async Task<bool> AcceptAsync()
        {
            await acceptGate.WaitAsync();
            try
            {
                if ((string)await Request.GET_Value("/lol-gameflow/v1/gameflow-phase") != "ReadyCheck") return false;
                JObject ready = await Request.GET("/lol-matchmaking/v1/ready-check");
                string id = ready["id"]?.ToString() ?? "current-ready-check";
                if ((string)ready["state"] != "InProgress" || (string)ready["playerResponse"] != "None" ||
                    acceptedCheck == id) return false;
                await Request.POST("/lol-matchmaking/v1/ready-check/accept", "0");
                acceptedCheck = id;
                return true;
            }
            finally { acceptGate.Release(); }
        }

        public override string ToString() => Name + " | " + Phase + " | PID " + Connection.ProcessId;
        public void Dispose() { Events.Disconnect(); Request.Dispose(); }
    }

    internal sealed class MultiClientManager : IDisposable
    {
        private readonly List<LeagueClientSession> clients = new List<LeagueClientSession>();
        private readonly Func<Task<IReadOnlyList<LeagueClientConnection>>> discover;
        private readonly Func<LeagueClientConnection, LCU_Request> createRequest;
        private bool disposed;
        private readonly SemaphoreSlim refreshGate = new SemaphoreSlim(1);
        public LeagueClientSession[] Clients => clients.ToArray();

        public MultiClientManager(Func<Task<IReadOnlyList<LeagueClientConnection>>> discover = null,
            Func<LeagueClientConnection, LCU_Request> createRequest = null)
        {
            this.discover = discover ?? (() => Task.Run(() => LeagueClientConnection.DiscoverAll()));
            this.createRequest = createRequest;
        }

        public async Task RefreshAsync(bool autoAccept)
        {
            await refreshGate.WaitAsync();
            try
            {
                var connections = await discover();
                if (disposed) return;
                foreach (var old in clients.Where(client => !connections.Any(connection =>
                    connection.Key == client.Connection.Key && connection.Api.Token == client.Connection.Api.Token)).ToArray())
                { clients.Remove(old); old.Dispose(); }
                foreach (var connection in connections.Where(connection => !clients.Any(client => client.Connection.Key == connection.Key)))
                    clients.Add(new LeagueClientSession(connection, createRequest?.Invoke(connection)));
                await Task.WhenAll(Clients.Select(client => client.RefreshAsync(autoAccept)));
            }
            finally { refreshGate.Release(); }
        }

        public async Task<int> ApplyToPhaseAsync(string phase, Func<LeagueClientSession, Task> action)
        {
            return await ApplyAsync(action, phase);
        }

        public async Task<int> ApplyAsync(Func<LeagueClientSession, Task> action, params string[] phases)
        {
            int completed = 0;
            var errors = new List<string>();
            foreach (var client in Clients)
            {
                try
                {
                    if (phases.Length > 0 && !phases.Contains((string)await client.Request.GET_Value("/lol-gameflow/v1/gameflow-phase"))) continue;
                    await action(client);
                    completed++;
                }
                catch (Exception ex) { errors.Add(client.Name + ": " + ex.Message); }
            }
            if (errors.Count > 0)
                throw new InvalidOperationException(completed + "개 완료\n" + string.Join("\n", errors));
            return completed;
        }

        public void Dispose() { disposed = true; foreach (var client in clients) client.Dispose(); clients.Clear(); }
    }
}
