using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LoL_Companion
{
    internal sealed class RiotClientCloseService
    {
        private readonly Func<Task<IReadOnlyList<RiotClientInstance>>> discover;
        private readonly Func<RiotClientConnection, RiotClientApi> openApi;
        private readonly Func<int, bool> running;
        private readonly Func<RiotClientInstance, Action> prepareTermination;

        public RiotClientCloseService(Func<Task<IReadOnlyList<RiotClientInstance>>> discover = null,
            Func<RiotClientConnection, RiotClientApi> openApi = null, Func<int, bool> running = null,
            Func<RiotClientInstance, Action> prepareTermination = null)
        {
            this.discover = discover ?? (() => Task.Run(() => RiotClientDiscovery.DiscoverAll()));
            this.openApi = openApi ?? (connection => new RiotClientApi(connection));
            this.running = running ?? IsRiotRunning;
            this.prepareTermination = prepareTermination ?? PrepareTermination;
        }

        public async Task CloseAsync(RiotClientInstance selected, CancellationToken token)
        {
            if (selected == null) throw new ArgumentNullException(nameof(selected));
            token.ThrowIfCancellationRequested();
            var instances = await discover();
            var current = instances.FirstOrDefault(instance => instance.Key == selected.Key &&
                instance.Connection.Token == selected.Connection.Token);
            if (current == null)
            {
                if (!running(selected.ProcessId)) return;
                throw new InvalidOperationException("선택한 Riot Client의 연결 정보가 변경되었습니다. 목록에서 다시 선택해 주세요.");
            }
            token.ThrowIfCancellationRequested();
            var terminate = prepareTermination(current);
            using (var api = openApi(current.Connection))
            {
                try { await api.SendAsync("POST", "/process-control/v1/process/quit", null, token); }
                // A successful quit can close the local API before its response arrives.
                catch (System.Net.Http.HttpRequestException) { }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            }
            var grace = Stopwatch.StartNew();
            while (running(selected.ProcessId) && grace.Elapsed < TimeSpan.FromSeconds(1))
                await Task.Delay(100, token);
            token.ThrowIfCancellationRequested();
            // Riot's quit API may close only its UI and leave RiotClientServices in
            // the background. Stop the exact process captured before the request.
            if (running(selected.ProcessId)) terminate();
            using (var closing = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                closing.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    while (running(selected.ProcessId) || (await discover()).Any(instance => instance.Key == selected.Key &&
                        instance.Connection.Token == selected.Connection.Token))
                    {
                        closing.Token.ThrowIfCancellationRequested();
                        await Task.Delay(100, closing.Token);
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { throw new TimeoutException("선택한 Riot Client의 종료가 확인되지 않았습니다."); }
            }
        }

        private static Action PrepareTermination(RiotClientInstance instance)
        {
            DateTime started;
            try
            {
                using (var process = Process.GetProcessById(instance.ProcessId))
                {
                    if (!IsRiotName(process.ProcessName)) throw new InvalidOperationException("선택한 프로세스가 Riot Client가 아닙니다.");
                    started = process.StartTime;
                }
            }
            catch (ArgumentException) { return () => { }; }
            return () =>
            {
                try
                {
                    using (var process = Process.GetProcessById(instance.ProcessId))
                    {
                        if (process.HasExited) return;
                        if (!IsRiotName(process.ProcessName) || process.StartTime != started)
                            throw new InvalidOperationException("선택한 Riot Client 프로세스가 변경되어 종료하지 않았습니다.");
                        process.Kill();
                    }
                }
                catch (ArgumentException) { }
            };
        }

        private static bool IsRiotName(string name) => new[] { "RiotClientServices", "Riot Client", "RiotClientUx", "RiotClient" }
            .Contains(name, StringComparer.OrdinalIgnoreCase);

        private static bool IsRiotRunning(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                    return !process.HasExited && IsRiotName(process.ProcessName);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
    }
}
