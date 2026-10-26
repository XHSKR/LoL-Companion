using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace LoL_Companion
{
    class LCU_Request : IDisposable
    {
        private readonly HttpClient client;
        public LeagueClientConnection Connection { get; }
        public LCU_Request(LeagueClientConnection connection, HttpMessageHandler handler = null)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            client = new HttpClient(handler ?? LocalApiHttpHandler.Create())
                { BaseAddress = new Uri("https://127.0.0.1:" + connection.Api.Port), Timeout = TimeSpan.FromSeconds(3) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:" + connection.Api.Token)));
        }
        public async Task<JObject> GET(string path) => (JObject)await GET_Value(path).ConfigureAwait(false);
        public async Task<JArray> GET_Array(string path) => (JArray)await GET_Value(path).ConfigureAwait(false);
        public Task<JToken> GET_Value(string path) => SendAsync("GET", path, null);
        public async Task POST(string path, string body) { await SendAsync("POST", path, body).ConfigureAwait(false); }
        public Task<JToken> POST_Value(string path, string body) => SendAsync("POST", path, body);
        public async Task PUT(string path, string body) { await SendAsync("PUT", path, body).ConfigureAwait(false); }
        public async Task PATCH(string path, string body) { await SendAsync("PATCH", path, body).ConfigureAwait(false); }
        private async Task<JToken> SendAsync(string method, string path, string body)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
                throw new ArgumentException("로컬 API 경로가 올바르지 않습니다.", nameof(path));
            using (var request = new HttpRequestMessage(new HttpMethod(method), path))
            {
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (var response = await client.SendAsync(request).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException(method + " " + path + ": HTTP " + (int)response.StatusCode);
                    string json = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return string.IsNullOrWhiteSpace(json) ? null : JToken.Parse(json);
                }
            }
        }
        public void Dispose() { client.Dispose(); }
    }
}
