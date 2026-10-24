using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LoL_Companion
{
    internal sealed class RiotClientConnection
    {
        public int Port { get; private set; }
        public string Token { get; private set; }

        public static RiotClientConnection ParseLockfile(string contents)
        {
            string[] parts = contents.Trim().Split(':');
            int port;
            int processId;
            if (parts.Length != 5 || parts[0] != "Riot Client" ||
                !int.TryParse(parts[1], out processId) || processId <= 0 ||
                !int.TryParse(parts[2], out port) || port < 1 || port > 65535 ||
                string.IsNullOrWhiteSpace(parts[3]) || parts[4] != "https")
                throw new FormatException("Riot Client 연결 파일의 형식이 올바르지 않습니다.");
            return new RiotClientConnection { Port = port, Token = parts[3] };
        }

        internal static string ReadArgument(string commandLine, string name)
        {
            // Flags can be reordered, quoted individually, or entirely unquoted.
            Match match = Regex.Match(commandLine ?? string.Empty,
                "(?:^|\\s)\"?--" + Regex.Escape(name) + "=(?:\"([^\"]+)\"|([^\\s\"]+))");
            return match.Success ? (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : null;
        }

        public static RiotClientConnection Discover()
        {
            string lockfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Riot Games\Riot Client\Config\lockfile");
            try
            {
                using (var stream = new FileStream(lockfile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                    return ParseLockfile(reader.ReadToEnd());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (FormatException) { }

            foreach (string processName in new[] { "RiotClientUx", "LeagueClientUx" })
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT CommandLine FROM Win32_Process WHERE Name='" + processName + ".exe'"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementBaseObject result in results)
                    {
                        string commandLine = result["CommandLine"] as string;
                        string prefix = processName == "LeagueClientUx" ? "riotclient-" : string.Empty;
                        string token = ReadArgument(commandLine, prefix + (prefix.Length == 0 ? "remoting-auth-token" : "auth-token"));
                        int port;
                        if (!string.IsNullOrEmpty(token) && int.TryParse(ReadArgument(commandLine, prefix + "app-port"), out port) &&
                            port > 0 && port <= 65535)
                            return new RiotClientConnection { Port = port, Token = token };
                    }
                }
            }
            throw new InvalidOperationException("Riot Client의 로컬 API 연결 정보를 찾지 못했습니다.");
        }
    }

    internal interface IRiotClientApi
    {
        Task<JToken> SendAsync(string method, string path, JObject body, CancellationToken cancellationToken);
    }

    internal sealed class RiotClientApiException : InvalidOperationException
    {
        public int StatusCode { get; private set; }
        public string Method { get; private set; }
        public string Path { get; private set; }

        public RiotClientApiException(int statusCode, string method, string path)
            : base(statusCode == 429 ? "로그인 요청이 제한되었습니다. 잠시 후 다시 시도해 주세요."
                : "Riot Client 요청 실패: HTTP " + statusCode + " (" + method + " " + path + ")")
        {
            StatusCode = statusCode;
            Method = method;
            Path = path;
        }
    }

    internal sealed class RiotClientApi : IRiotClientApi, IDisposable
    {
        private readonly HttpClient client;

        public RiotClientApi(RiotClientConnection connection)
            : this(connection, new HttpClientHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
                    request.RequestUri.Host == "127.0.0.1"
            }) { }

        internal RiotClientApi(RiotClientConnection connection, HttpMessageHandler handler)
        {
            client = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:" + connection.Port),
                Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:" + connection.Token)));
        }

        public async Task<JToken> SendAsync(string method, string path, JObject body, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
                throw new ArgumentException("로컬 API 경로가 올바르지 않습니다.", nameof(path));
            using (var request = new HttpRequestMessage(new HttpMethod(method), path))
            {
                if (body != null)
                    request.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    // Never expose response bodies: auth responses can contain passwords/tokens.
                    if (!response.IsSuccessStatusCode)
                        throw new RiotClientApiException((int)response.StatusCode, method, path);
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return string.IsNullOrWhiteSpace(json) ? null : JToken.Parse(json);
                }
            }
        }

        public void Dispose() { client.Dispose(); }
    }
}
