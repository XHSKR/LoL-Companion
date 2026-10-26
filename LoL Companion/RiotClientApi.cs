using Newtonsoft.Json.Linq;
using System;
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

        internal static RiotClientConnection Create(int port, string token)
        {
            if (port < 1 || port > 65535 || string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("로컬 API 연결 정보가 올바르지 않습니다.");
            return new RiotClientConnection { Port = port, Token = token };
        }

        internal static string ReadArgument(string commandLine, string name)
        {
            // Flags can be reordered, quoted individually, or entirely unquoted.
            Match match = Regex.Match(commandLine ?? string.Empty,
                "(?:^|\\s)\"?--" + Regex.Escape(name) + "=(?:\"([^\"]+)\"|([^\\s\"]+))");
            return match.Success ? (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : null;
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
            : this(connection, LocalApiHttpHandler.Create()) { }

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
                    string json = response.Content == null ? null : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return string.IsNullOrWhiteSpace(json) ? null : JToken.Parse(json);
                }
            }
        }

        public void Dispose() { client.Dispose(); }
    }
}
