using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LoL_Companion
{
    internal sealed class LeagueEdgeClient : IDisposable
    {
        private readonly LCU_Request lcu;
        private readonly HttpClient http;
        private readonly string installationPath;
        private readonly bool ownsClient;

        public LeagueEdgeClient(LCU_Request lcu, HttpClient http = null, string installationPath = null)
        {
            this.lcu = lcu;
            this.installationPath = installationPath;
            ownsClient = http == null;
            this.http = http ?? new HttpClient(new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12,
                // External Riot servers must have a valid certificate, unlike the local LCU.
                ServerCertificateCustomValidationCallback = (sender, certificate, chain, errors) =>
                    errors == SslPolicyErrors.None
            }) { Timeout = TimeSpan.FromSeconds(10) };
        }

        public async Task QuitChampionSelectAsync(string expectedGameId)
        {
            JObject locale = await lcu.GET("/riotclient/region-locale");
            string region = (string)locale["region"];
            string root = installationPath ?? LeagueInstallation.Resolve(Form1.leagueLocation);
            Uri server = ResolveServer(File.ReadAllLines(Path.Combine(root, "system.yaml")), region);
            string token = (string)await lcu.GET_Value("/lol-league-session/v1/league-session-token");
            string version = (string)await lcu.GET_Value("/lol-patch/v1/game-version");
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(version))
                throw new InvalidOperationException("롤 서버 연결 정보를 불러오지 못했습니다.");

            // Recheck after resolving credentials so a delayed click cannot quit a different session.
            JObject currentSession = await lcu.GET("/lol-gameflow/v1/session");
            if ((string)currentSession["phase"] != "ChampSelect" ||
                currentSession["gameData"]?["gameId"]?.ToString() != expectedGameId)
                throw new InvalidOperationException("챔피언 선택 세션이 변경되어 닷지 요청을 보내지 않았습니다.");

            using (var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(server, "team-builder-ledge/v1/quit")))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.TryAddWithoutValidation("User-Agent",
                    "LeagueOfLegendsClient/" + version + " (rcp-be-lol-lobby-team-builder)");
                // The installed client sends this POST without a per-call JSON body.
                using (HttpResponseMessage response = await http.SendAsync(request).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("롤 서버 닷지 요청 실패: HTTP " + (int)response.StatusCode);

                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    string status = (string)JObject.Parse(json)["status"];
                    if (status != "QUIT_SUCCESS")
                        throw new InvalidOperationException("롤 서버에서 닷지가 확인되지 않았습니다: " + (status ?? "응답 없음"));
                }
            }
        }

        internal static Uri ResolveServer(IEnumerable<string> lines, string region)
        {
            var scopes = new List<KeyValuePair<int, string>>();
            var urls = new List<KeyValuePair<string, string>>();
            var pattern = new Regex(@"^(?<indent> *)(?<key>[A-Za-z0-9_-]+):\s*(?<value>.*)$");
            foreach (string line in lines)
            {
                Match match = pattern.Match(line);
                if (!match.Success)
                    continue;
                int indent = match.Groups["indent"].Length;
                scopes.RemoveAll(scope => scope.Key >= indent);
                string key = match.Groups["key"].Value;
                string value = match.Groups["value"].Value.Trim();
                if (value.Length == 0)
                {
                    scopes.Add(new KeyValuePair<int, string>(indent, key));
                    continue;
                }
                if (key != "league_edge_url" || !scopes.Any(scope => scope.Value == "league_edge"))
                    continue;

                int regionIndex = scopes.FindIndex(scope => scope.Value == "region_data");
                string alias = regionIndex >= 0 && scopes.Count > regionIndex + 1
                    ? scopes[regionIndex + 1].Value : string.Empty;
                value = value.Split('#')[0].Trim().Trim('\'', '"');
                urls.Add(new KeyValuePair<string, string>(alias, value));
            }

            string[] matching = urls.Where(item => string.Equals(item.Key, region,
                StringComparison.OrdinalIgnoreCase)).Select(item => item.Value).Distinct().ToArray();
            if (matching.Length == 0)
                matching = urls.Select(item => item.Value).Distinct().ToArray();
            Uri uri;
            if (matching.Length != 1 || !Uri.TryCreate(matching[0], UriKind.Absolute, out uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Query.Length != 0 ||
                uri.Fragment.Length != 0 || uri.AbsolutePath != "/" ||
                !(uri.Host.EndsWith(".pvp.net", StringComparison.OrdinalIgnoreCase) ||
                  uri.Host.EndsWith(".leagueoflegends.com", StringComparison.OrdinalIgnoreCase) ||
                  uri.Host.EndsWith(".riotgames.com", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("현재 지역의 롤 서버 주소를 안전하게 확인하지 못했습니다.");
            return uri;
        }

        public void Dispose()
        {
            if (ownsClient)
                http.Dispose();
        }
    }
}
