using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LoL_Companion
{
    class LCU_Request : IDisposable
    {
        private readonly HttpClient _httpClient;

        public LCU_Request()
        {
            // 자체 서명된 인증서 허용 (로컬 호출 전용)
            var handler = new HttpClientHandler
            {
                // .NET Framework 4.7.2에서는 self-signed cert 허용 설정
                ServerCertificateCustomValidationCallback = (sender, cert, chain, errors) => true
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30) // 필요에 따라 조정
            };
        }

        public static string riotPass;
        public static string riotPort;
        public static string completeURL;

        private void GetRiotCredentials(string plugIn)
        {
            var process = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
            if (process == null)
                throw new InvalidOperationException("LeagueClientUx 프로세스를 찾을 수 없습니다.");

            string commandLine;
            using (var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"))
            using (var results = searcher.Get())
            {
                commandLine = results.Cast<ManagementBaseObject>()
                                     .SingleOrDefault()?["CommandLine"]?.ToString()
                              ?? throw new InvalidOperationException("CommandLine 정보를 읽어올 수 없습니다.");
            }

            riotPass = Regex.Match(commandLine,
                                   @"--remoting-auth-token=([^""]+)")
                            .Groups[1].Value;

            riotPort = Regex.Match(commandLine,
                                   @"--app-port=(\d+)")
                            .Groups[1].Value;

            completeURL = $"https://127.0.0.1:{riotPort}{plugIn}";
        }

        public async Task<JObject> GET(string plugIn)
        {
            GetRiotCredentials(plugIn);

            using (var request = new HttpRequestMessage(HttpMethod.Get, completeURL))
            {
                AddBasicAuthHeader(request);
                using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    string jsonString = await response.Content
                                            .ReadAsStringAsync()
                                            .ConfigureAwait(false);
                    return JObject.Parse(jsonString);
                }
            }
        }

        public async Task<JArray> GET_Array(string plugIn)
        {
            GetRiotCredentials(plugIn);

            using (var request = new HttpRequestMessage(HttpMethod.Get, completeURL))
            {
                AddBasicAuthHeader(request);
                using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    string jsonString = await response.Content
                                            .ReadAsStringAsync()
                                            .ConfigureAwait(false);
                    return JArray.Parse(jsonString);
                }
            }
        }

        public async Task POST(string plugIn, string jsonBody)
        {
            await SendWithBodyAsync("POST", plugIn, jsonBody).ConfigureAwait(false);
        }

        public async Task PUT(string plugIn, string jsonBody)
        {
            await SendWithBodyAsync("PUT", plugIn, jsonBody).ConfigureAwait(false);
        }

        public async Task PATCH(string plugIn, string jsonBody)
        {
            await SendWithBodyAsync("PATCH", plugIn, jsonBody).ConfigureAwait(false);
        }

        private async Task SendWithBodyAsync(string method, string plugIn, string bodyJson)
        {
            GetRiotCredentials(plugIn);

            using (var request = new HttpRequestMessage(new HttpMethod(method), completeURL))
            {
                // 파라미터로 받은 bodyJson 사용
                request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                AddBasicAuthHeader(request);

                using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                }
            }
        }

        private void AddBasicAuthHeader(HttpRequestMessage request)
        {
            var token = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"riot:{riotPass}"));
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Basic", token);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
