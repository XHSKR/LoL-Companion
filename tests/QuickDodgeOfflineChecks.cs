using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using LoL_Companion;

namespace LoL_Companion
{
    // All local API access is replaced here: this executable cannot contact LCU.
    internal class LCU_Request
    {
        public Queue<string> Phases = new Queue<string>(new[] { "ChampSelect", "Lobby" });
        public Queue<string> Games = new Queue<string>(new[] { "42", "42" });
        public Task<JToken> GET_Value(string path)
        {
            if (path.EndsWith("gameflow-phase")) return Task.FromResult<JToken>(new JValue(Phases.Dequeue()));
            if (path.EndsWith("league-session-token")) return Task.FromResult<JToken>(new JValue("offline-test-token"));
            if (path.EndsWith("game-version")) return Task.FromResult<JToken>(new JValue("offline-test-version"));
            throw new Exception("Unexpected local read: " + path);
        }
        public Task<JObject> GET(string path)
        {
            if (path == "/riotclient/region-locale") return Task.FromResult(JObject.Parse("{region:'KR'}"));
            if (path == "/lol-gameflow/v1/session") return Task.FromResult(JObject.Parse("{phase:'ChampSelect',gameData:{gameId:'" + Games.Dequeue() + "'}}"));
            throw new Exception("Unexpected local read: " + path);
        }
    }
    internal static class Form1 { public static string leagueLocation = "UNUSED"; }
    internal static class LeagueInstallation { public static string Resolve(string path) { throw new Exception("Must use offline fixture"); } }
}

internal sealed class FakeServer : HttpMessageHandler
{
    public int Calls;
    public HttpStatusCode Code = HttpStatusCode.OK;
    public string Json = "{status:'QUIT_SUCCESS'}";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Calls++;
        Check(request.Method == HttpMethod.Post, "POST method");
        Check(request.RequestUri.AbsoluteUri == "https://kr-red.lol.sgp.pvp.net/team-builder-ledge/v1/quit", "server quit route");
        Check(request.Headers.Authorization.ToString() == "Bearer offline-test-token", "session bearer");
        Check(request.Content == null || (await request.Content.ReadAsByteArrayAsync()).Length == 0, "empty request body");
        return new HttpResponseMessage(Code) { Content = new StringContent(Json) };
    }
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); }
}

internal static class QuickDodgeOfflineChecks
{
    private static int passed;
    private const string Yaml = "region_data:\n  KR:\n    services:\n      league_edge:\n        league_edge_url: https://kr-red.lol.sgp.pvp.net\n  EUW:\n    services:\n      league_edge:\n        league_edge_url: https://euw1-red.lol.sgp.pvp.net\n";
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
    private static async Task MustFail(Func<Task> action, string label)
    {
        bool failed = false;
        try { await action(); } catch { failed = true; }
        Check(failed, label);
    }
    private static async Task Run()
    {
        string fixture = Path.Combine(Path.GetTempPath(), "lol-dodge-offline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "system.yaml"), Yaml);
        try
        {
            Check(LeagueEdgeClient.ResolveServer(Yaml.Split('\n'), "KR").Host == "kr-red.lol.sgp.pvp.net", "regional server selection");
            Check(LeagueEdgeClient.ResolveServer(Yaml.Split('\n'), "euw").Host == "euw1-red.lol.sgp.pvp.net", "case insensitive region");
            foreach (string invalid in new[] { Yaml.Replace("https:", "http:"), Yaml.Replace(".pvp.net", ".example.com"), Yaml })
                await MustFail(() => { LeagueEdgeClient.ResolveServer(invalid.Split('\n'), "UNKNOWN"); return Task.CompletedTask; }, "reject unsafe or ambiguous server");
            string actual = @"C:\Riot Games\League of Legends\system.yaml";
            if (File.Exists(actual)) Check(LeagueEdgeClient.ResolveServer(File.ReadAllLines(actual), "KR").Host == "kr-red.lol.sgp.pvp.net", "installed yaml parsed offline");

            var lcu = new LCU_Request(); var handler = new FakeServer();
            using (var http = new HttpClient(handler)) using (var server = new LeagueEdgeClient(lcu, http, fixture))
            { await new QuickDodgeService(lcu, server).DodgeAsync(); Check(handler.Calls == 1, "success sends exactly one mocked request"); }

            foreach (string phase in new[] { "None", "InProgress" })
            {
                lcu = new LCU_Request { Phases = new Queue<string>(new[] { phase }) }; handler = new FakeServer();
                using (var http = new HttpClient(handler)) using (var server = new LeagueEdgeClient(lcu, http, fixture))
                { await MustFail(() => new QuickDodgeService(lcu, server).DodgeAsync(), "reject phase " + phase); Check(handler.Calls == 0, "no request outside champion select"); }
            }
            lcu = new LCU_Request { Games = new Queue<string>(new[] { "42", "43" }) }; handler = new FakeServer();
            using (var http = new HttpClient(handler)) using (var server = new LeagueEdgeClient(lcu, http, fixture))
            { await MustFail(() => new QuickDodgeService(lcu, server).DodgeAsync(), "reject changed game id"); Check(handler.Calls == 0, "changed session sends nothing"); }

            foreach (bool httpFailure in new[] { false, true })
            {
                lcu = new LCU_Request(); handler = new FakeServer { Code = httpFailure ? HttpStatusCode.BadRequest : HttpStatusCode.OK, Json = "{status:'QUIT_FAILED'}" };
                using (var http = new HttpClient(handler)) using (var server = new LeagueEdgeClient(lcu, http, fixture))
                { await MustFail(() => new QuickDodgeService(lcu, server).DodgeAsync(), httpFailure ? "reject HTTP failure" : "reject service failure within HTTP 200"); Check(handler.Calls == 1, "failure has no retry or fallback"); }
            }
        }
        finally { File.Delete(Path.Combine(fixture, "system.yaml")); Directory.Delete(fixture); }
        Console.WriteLine("TOTAL " + passed + " passed; all HTTP requests mocked; no client/process interaction.");
    }
    private static int Main() { try { Run().GetAwaiter().GetResult(); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; } }
}
