using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LoL_Companion
{
    internal sealed class AccountLoginService
    {
        private readonly IRiotClientApi api;
        private readonly Action ensureCanSwitch;
        private readonly Func<int, CancellationToken, Task> delay;
        private readonly Func<string, string, CancellationToken, Task> submitThroughUi;
        private const string SessionPath = "/player-session-lifecycle/v1/session";

        public AccountLoginService(IRiotClientApi api, Action ensureCanSwitch, Func<int, CancellationToken, Task> delay = null,
            Func<string, string, CancellationToken, Task> submitThroughUi = null)
        {
            this.api = api;
            this.ensureCanSwitch = ensureCanSwitch ?? throw new ArgumentNullException(nameof(ensureCanSwitch));
            this.delay = delay ?? Task.Delay;
            this.submitThroughUi = submitThroughUi;
        }

        public async Task LoginAsync(string username, string password, string locale,
            Action<string> reportProgress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                throw new InvalidOperationException("credentials.conf에 로그인 ID와 비밀번호를 입력해 주세요.");

            ensureCanSwitch();
            cancellationToken.ThrowIfCancellationRequested();
            reportProgress("로그인 세션을 확인하는 중...");
            JObject currentSession = await ReadLifecycleSessionAsync(cancellationToken);
            if (IsSelectedAccountAuthorized(currentSession, username)) return;

            JToken authentication = await api.SendAsync("GET", "/rso-auth/v1/session", null, cancellationToken);
            if ((string)authentication?["type"] == "authenticated")
            {
                // Respect Riot's sign-out permission, including games in other Riot products.
                JToken permission = null;
                for (int attempt = 0; attempt < 120; attempt++)
                {
                    ensureCanSwitch();
                    permission = await api.SendAsync("GET", "/product-integration/v1/signout/permission", null, cancellationToken);
                    if ((string)permission != "DisallowedOtherProducts") break;
                    reportProgress("종료된 게임 클라이언트의 세션 정리를 기다리는 중...");
                    await delay(500, cancellationToken);
                }
                if (permission?.Type != JTokenType.String || (string)permission != "Allowed")
                    throw new InvalidOperationException("Riot Client가 계정 전환을 허용하지 않습니다. 실행 중인 게임을 마친 후 다시 시도해 주세요.");
                reportProgress("기존 계정에서 로그아웃하는 중...");
                await SendChangeAsync("POST", "/product-integration/v1/signout", null, cancellationToken);
                bool signedOut = false;
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    authentication = await api.SendAsync("GET", "/rso-auth/v1/session", null, cancellationToken);
                    if ((string)authentication?["type"] != "authenticated") { signedOut = true; break; }
                    await delay(250, cancellationToken);
                }
                if (!signedOut) throw new TimeoutException("기존 계정의 로그아웃이 완료되지 않았습니다. 잠시 후 다시 시도해 주세요.");
                currentSession = await ReadLifecycleSessionAsync(cancellationToken);
            }

            await PrepareLifecycleSessionAsync(currentSession, cancellationToken);

            string language = locale.Replace('_', '-');
            JObject prompt = (JObject)await SendChangeAsync("POST", "/rso-authenticator/v1/authentication/riot-identity/start",
                new JObject { ["language"] = language, ["productId"] = "league_of_legends" }, cancellationToken);
            ThrowIfAuthenticationFailed(prompt);
            reportProgress("선택한 계정으로 로그인하는 중...");
            JObject result = (JObject)await SendChangeAsync("POST", "/rso-authenticator/v1/authentication/riot-identity/complete",
                new JObject { ["username"] = username, ["password"] = password, ["remember"] = false,
                    ["language"] = language, ["captcha"] = string.Empty }, cancellationToken);

            ThrowIfAuthenticationFailed(result);
            string loginToken = (string)result["success"]?["login_token"];
            string expectedPuuid = (string)result["success"]?["puuid"];
            if ((string)result["type"] == "success" && !string.IsNullOrEmpty(loginToken))
                await SendChangeAsync("PUT", "/player-session-lifecycle/v1/login-token",
                    new JObject { ["loginToken"] = loginToken, ["remember"] = false }, cancellationToken);
            else if (submitThroughUi != null && ((string)result["type"] == "auth" ||
                ((string)result["error"] ?? string.Empty).IndexOf("captcha", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                ensureCanSwitch();
                currentSession = await ReadLifecycleSessionAsync(cancellationToken);
                if (!IsSelectedAccountAuthorized(currentSession, username))
                {
                    reportProgress("Riot 로그인 화면에 계정 정보를 입력하는 중...");
                    try { await submitThroughUi(username, password, cancellationToken); }
                    catch (InvalidOperationException)
                    {
                        // The user/client may finish signing in while the UI is being located.
                        currentSession = await ReadLifecycleSessionAsync(cancellationToken);
                        if (!IsSelectedAccountAuthorized(currentSession, username)) throw;
                    }
                }
                reportProgress("Riot Client의 로그인 완료를 확인하는 중...");
            }
            else
                reportProgress("Riot Client에서 추가 인증/CAPTCHA를 완료해 주세요. 완료하면 게임을 실행합니다.");

            // HTTP success alone does not mean that the selected account has logged in.
            for (int attempt = 0; attempt < 240; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JObject session = (JObject)await api.SendAsync("GET", SessionPath, null, cancellationToken);
                string state = (string)session["loginState"];
                if (state == "Authorized")
                {
                    string actualUsername = (string)session["userInfo"]?["username"];
                    if ((!string.IsNullOrEmpty(expectedPuuid) && expectedPuuid != (string)session["puuid"]) ||
                        (!string.IsNullOrEmpty(actualUsername) && !string.Equals(username, actualUsername, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("선택한 계정과 실제 로그인한 계정이 다릅니다. Riot Client에서 선택한 계정으로 로그인해 주세요.");
                    if (string.IsNullOrEmpty(expectedPuuid) && string.IsNullOrEmpty(actualUsername))
                        throw new InvalidOperationException("로그인한 계정을 확인할 수 없습니다. Riot Client에서 로그인 상태를 확인해 주세요.");
                    return;
                }
                if (state == "Error" || !string.IsNullOrEmpty((string)session["errorCode"]))
                    throw new InvalidOperationException("Riot Client 로그인이 실패했습니다. 클라이언트에서 오류와 계정 정보를 확인해 주세요.");
                if (session["actionRequired"]?.Value<bool>() == true)
                    reportProgress("Riot Client에서 추가 인증/CAPTCHA 또는 약관 확인을 완료해 주세요.");
                await delay(1000, cancellationToken);
            }
            throw new TimeoutException("로그인 대기 시간이 지났습니다. Riot Client에서 인증 상태를 확인한 후 다시 시도해 주세요.");
        }

        private static bool IsSelectedAccountAuthorized(JObject session, string username)
        {
            return (string)session?["loginState"] == "Authorized" &&
                string.Equals((string)session["userInfo"]?["username"], username, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<JObject> ReadLifecycleSessionAsync(CancellationToken cancellationToken)
        {
            try { return (JObject)await api.SendAsync("GET", SessionPath, null, cancellationToken); }
            catch (RiotClientApiException ex) when (ex.StatusCode == 404 && ex.Path == SessionPath && ex.Method == "GET")
            { return null; }
        }

        private static JObject ConfigureSession(JObject session)
        {
            // PUT replaces the session config. Preserve fields owned by the official client.
            var config = session?["config"] is JObject existing ? (JObject)existing.DeepClone() : new JObject();
            // Riot rejects changing persistence on an existing session (HTTP 409).
            if (config["persistLogin"] == null) config["persistLogin"] = false;
            config["authIntent"] = "SignIn";
            config["productContext"] = new JObject { ["productId"] = "league_of_legends", ["patchlineId"] = "live" };
            if (config["scopes"] == null)
                config["scopes"] = new JArray("openid", "link", "ban", "lol_region", "lol", "account");
            return config;
        }

        private async Task PrepareLifecycleSessionAsync(JObject session, CancellationToken cancellationToken)
        {
            if (session != null)
                await SendChangeAsync("PUT", SessionPath, ConfigureSession(session), cancellationToken);
            else
            {
                try { await SendChangeAsync("POST", SessionPath, ConfigureSession(null), cancellationToken); }
                catch (RiotClientApiException ex) when (ex.StatusCode == 409 && ex.Path == SessionPath && ex.Method == "POST")
                {
                    // The official client can recreate its session between our GET and POST.
                    // Recover only this creation conflict; never retry credential submissions.
                    session = await ReadLifecycleSessionAsync(cancellationToken);
                    if (session == null) throw;
                    await SendChangeAsync("PUT", SessionPath, ConfigureSession(session), cancellationToken);
                }
            }
            // The API explicitly requires a separate call when changing login strategy.
            await SendChangeAsync("PUT", "/player-session-lifecycle/v1/login-strategy",
                new JObject { ["loginStrategy"] = "riot_identity" }, cancellationToken);
        }

        private Task<JToken> SendChangeAsync(string method, string path, JObject body, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ensureCanSwitch();
            return api.SendAsync(method, path, body, cancellationToken);
        }

        private static void ThrowIfAuthenticationFailed(JObject response)
        {
            if (response == null)
                throw new InvalidOperationException("Riot Client에서 인증 응답을 받지 못했습니다. 잠시 후 다시 시도해 주세요.");
            string error = (string)response["error"];
            if ((string)response["type"] == "error" || !string.IsNullOrEmpty(error))
            {
                if (error == "auth_failure" || error == "invalid_credentials")
                    throw new InvalidOperationException("로그인 ID 또는 비밀번호가 올바르지 않습니다. credentials.conf를 확인해 주세요.");
                if (error != null && error.IndexOf("captcha", StringComparison.OrdinalIgnoreCase) >= 0)
                    return; // The official client must complete the interactive challenge.
                throw new InvalidOperationException("Riot Client가 로그인을 거부했습니다. 클라이언트에서 오류를 확인해 주세요.");
            }
        }
    }
}
