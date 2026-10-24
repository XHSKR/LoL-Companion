using LoL_Companion;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class AccountLoginTests
{
    private static int assertions;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            TestConnectionParsing();
            TestHttpAsync().GetAwaiter().GetResult();
            TestLoginAsync().GetAwaiter().GetResult();
            if (args.Contains("--live-read-only")) TestLiveReadOnlyAsync().GetAwaiter().GetResult();
            TestSettingsAndLayout();
            Console.WriteLine("PASS: " + assertions + " assertions");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }

    private static async Task ExpectFailureAsync(Func<Task> action, string expectedMessage)
    {
        try { await action(); }
        catch (InvalidOperationException ex)
        {
            Check(ex.Message.Contains(expectedMessage), "Expected failure: " + expectedMessage);
            return;
        }
        throw new Exception("Expected failure was not raised: " + expectedMessage);
    }

    private static void TestConnectionParsing()
    {
        var connection = RiotClientConnection.ParseLockfile("Riot Client:123:4567:test-token:https\n");
        Check(connection.Port == 4567 && connection.Token == "test-token", "Riot lockfile parsing");
        foreach (string contents in new[] { "", "LeagueClient:1:2:x:https", "Riot Client:1:65536:x:https", "Riot Client:1:0:x:https",
            "Riot Client:1:80::https", "Riot Client:1:80:x:http", "Riot Client:0:80:x:https" })
        {
            bool rejected = false;
            try { RiotClientConnection.ParseLockfile(contents); } catch (FormatException) { rejected = true; }
            Check(rejected, "Malformed lockfile rejected");
        }
        Check(RiotClientConnection.ReadArgument("exe --app-port=123 --remoting-auth-token=abc --other=value", "remoting-auth-token") == "abc", "Unquoted token stops at whitespace");
        Check(RiotClientConnection.ReadArgument("exe \"--riotclient-auth-token=abc\" --riotclient-app-port=456", "riotclient-auth-token") == "abc", "Quoted flag");
        Check(RiotClientConnection.ReadArgument("exe --app-port=\"123\"", "app-port") == "123", "Quoted value");
        Check(RiotClientConnection.ReadArgument("exe --riotclient-app-port=456 --app-port=123", "app-port") == "123", "No prefix confusion");
    }

    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Reply(request);
        }
    }

    private static async Task TestHttpAsync()
    {
        const string password = "special\"\\\n한글";
        var handler = new Handler { Reply = async request =>
        {
            Check(request.RequestUri.Host == "127.0.0.1" && request.RequestUri.Port == 4567, "Requests stay on local Riot API");
            Check(request.Headers.Authorization.Scheme == "Basic", "Local API authentication");
            Check(request.Content.Headers.ContentType.MediaType == "application/json", "JSON content type");
            Check((string)JObject.Parse(await request.Content.ReadAsStringAsync())["password"] == password, "Credentials are JSON escaped");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        } };
        using (var api = new RiotClientApi(RiotClientConnection.ParseLockfile("Riot Client:123:4567:token:https"), handler))
            await api.SendAsync("POST", "/test", new JObject { ["password"] = password }, CancellationToken.None);

        int calls = 0;
        handler = new Handler { Reply = request =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("SECRET_RESPONSE_BODY") });
        } };
        using (var api = new RiotClientApi(RiotClientConnection.ParseLockfile("Riot Client:123:4567:token:https"), handler))
            await ExpectFailureAsync(() => api.SendAsync("POST", "/test", new JObject(), CancellationToken.None), "제한");
        Check(calls == 1, "Rate limited credentials are never retried");

        handler = new Handler { Reply = request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("\"DisallowedOtherProducts\"") }) };
        using (var api = new RiotClientApi(RiotClientConnection.ParseLockfile("Riot Client:123:4567:token:https"), handler))
            Check((string)await api.SendAsync("GET", "/product-integration/v1/signout/permission", null, CancellationToken.None) == "DisallowedOtherProducts",
                "Real transport accepts Riot's scalar signout permission response");

        handler = new Handler { Reply = request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            { Content = new StringContent("SECRET_RESPONSE_BODY") }) };
        using (var api = new RiotClientApi(RiotClientConnection.ParseLockfile("Riot Client:123:4567:token:https"), handler))
        {
            bool caught = false;
            try { await api.SendAsync("POST", "/player-session-lifecycle/v1/session", new JObject(), CancellationToken.None); }
            catch (RiotClientApiException ex)
            {
                caught = true;
                Check(ex.StatusCode == 409 && ex.Method == "POST" && ex.Path == "/player-session-lifecycle/v1/session", "HTTP conflicts retain structured method and route");
                Check(!ex.Message.Contains("SECRET_RESPONSE_BODY"), "HTTP error bodies containing credentials are not exposed");
            }
            Check(caught, "Transport raises typed session conflicts");
        }
    }

    private sealed class FakeApi : IRiotClientApi
    {
        public readonly List<string> Calls = new List<string>();
        public JObject Authentication = JObject.Parse("{type:'success',success:{login_token:'token',puuid:'selected-puuid'}}");
        public JObject Session = JObject.Parse("{loginState:'Authorized',puuid:'selected-puuid',userInfo:{username:'selected'}}");
        public JObject InitialSession = JObject.Parse("{loginState:'PendingProductContext',config:{persistLogin:true,requireVNG:true,additionalPermissions:['existing'],loginStrategy:'qrcode'}}");
        public bool AuthenticatedInitially;
        public string SignoutPermission = "Allowed";
        public readonly Queue<string> SignoutPermissions = new Queue<string>();
        public bool MissingInitially;
        public bool CreateConflict;
        public bool MissingAfterConflict;
        public int CredentialFailureStatus;
        public int InitialReadFailureStatus;
        public bool UseUi;
        public bool AuthorizeBeforeUi;
        public int UiSubmissions;
        public readonly List<JObject> ConfigurationWrites = new List<JObject>();
        private int sessionReads;
        private bool submitted;
        private bool signedOut;
        public readonly List<string> Progress = new List<string>();
        public Task<JToken> SendAsync(string method, string path, JObject body, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(method + " " + path);
            if (method == "GET" && path == "/player-session-lifecycle/v1/session")
            {
                sessionReads++;
                if (InitialReadFailureStatus != 0) throw new RiotClientApiException(InitialReadFailureStatus, method, path);
                if (MissingInitially && (sessionReads == 1 || MissingAfterConflict)) throw new RiotClientApiException(404, method, path);
                if (submitted && UseUi && !AuthorizeBeforeUi && UiSubmissions == 0 && (string)Authentication["type"] == "auth")
                    return Task.FromResult<JToken>(JObject.Parse("{loginState:'PendingAuthentication',actionRequired:true}"));
                return Task.FromResult<JToken>(submitted ? Session : InitialSession);
            }
            if (method == "GET" && path == "/rso-auth/v1/session")
                return Task.FromResult<JToken>(new JObject { ["type"] = AuthenticatedInitially && !signedOut ? "authenticated" : "needs_credentials" });
            if (method == "GET" && path.EndsWith("/signout/permission"))
                return Task.FromResult<JToken>(new JValue(SignoutPermissions.Count > 0 ? SignoutPermissions.Dequeue() : SignoutPermission));
            if (method == "POST" && path.EndsWith("/signout")) signedOut = true;
            if ((method == "POST" || method == "PUT") && path == "/player-session-lifecycle/v1/session")
            {
                if (method == "POST" && CreateConflict) throw new RiotClientApiException(409, method, path);
                ConfigurationWrites.Add((JObject)body.DeepClone());
                bool expectedPersistence = method == "PUT" && InitialSession["config"]?["persistLogin"]?.Value<bool>() == true;
                Check((bool)body["persistLogin"] == expectedPersistence && (string)body["productContext"]?["productId"] == "league_of_legends", "Existing persistence preserved; new sessions disable persistence");
            }
            if (path.EndsWith("/complete"))
            {
                submitted = true;
                Check((string)body["username"] == "selected" && (string)body["password"] == "test-password", "Selected credentials submitted");
                Check((string)body["language"] == "ko-KR", "Locale converted for authentication");
                if (CredentialFailureStatus != 0) throw new RiotClientApiException(CredentialFailureStatus, method, path);
                return Task.FromResult<JToken>(Authentication);
            }
            if (path.EndsWith("/start")) return Task.FromResult<JToken>(JObject.Parse("{type:'auth'}"));
            return Task.FromResult<JToken>(null);
        }
        public Task Login(CancellationToken cancellationToken = default(CancellationToken), Action ensureCanSwitch = null)
        {
            return new AccountLoginService(this, ensureCanSwitch ?? (() => { }), (milliseconds, token) =>
            { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            UseUi ? (Func<string, string, CancellationToken, Task>)((username, password, token) =>
            {
                token.ThrowIfCancellationRequested();
                UiSubmissions++;
                Check(username == "selected" && password == "test-password", "UI fallback receives only the selected credentials");
                return Task.CompletedTask;
            }) : null)
                .LoginAsync("selected", "test-password", "ko_KR", Progress.Add, cancellationToken);
        }
    }

    private static async Task TestLoginAsync()
    {
        var api = new FakeApi();
        await api.Login();
        Check(api.Calls.SequenceEqual(new[] { "GET /player-session-lifecycle/v1/session", "GET /rso-auth/v1/session",
            "PUT /player-session-lifecycle/v1/session", "PUT /player-session-lifecycle/v1/login-strategy",
            "POST /rso-authenticator/v1/authentication/riot-identity/start", "POST /rso-authenticator/v1/authentication/riot-identity/complete",
            "PUT /player-session-lifecycle/v1/login-token", "GET /player-session-lifecycle/v1/session" }), "Existing session updated without DELETE/POST recreation");
        Check((bool)api.ConfigurationWrites.Single()["requireVNG"] &&
            (string)api.ConfigurationWrites.Single()["additionalPermissions"]?[0] == "existing", "Official client configuration is preserved by PUT");
        Check((bool)api.ConfigurationWrites.Single()["persistLogin"], "HTTP 409 prevented by retaining immutable persistence on existing sessions");

        api = new FakeApi { MissingInitially = true };
        await api.Login();
        Check(api.Calls.Count(call => call == "POST /player-session-lifecycle/v1/session") == 1, "Create only when no lifecycle session exists");

        api = new FakeApi { MissingInitially = true, CreateConflict = true };
        await api.Login();
        Check(api.Calls.Count(call => call == "POST /player-session-lifecycle/v1/session") == 1 &&
            api.Calls.Count(call => call == "PUT /player-session-lifecycle/v1/session") == 1, "HTTP 409 creation race recovered once by GET/PUT");
        Check((bool)api.ConfigurationWrites.Single()["requireVNG"], "Race recovery preserves recreated client config");

        api = new FakeApi { MissingInitially = true, CreateConflict = true, MissingAfterConflict = true };
        await ExpectFailureAsync(() => api.Login(), "HTTP 409");
        Check(!api.Calls.Any(call => call.EndsWith("/complete")), "Unresolved session conflict never submits credentials");

        api = new FakeApi { AuthenticatedInitially = true };
        await api.Login();
        Check(api.Calls.Contains("GET /product-integration/v1/signout/permission") &&
            api.Calls.Contains("POST /product-integration/v1/signout"), "Existing authentication uses Riot's permitted signout flow");
        Check(api.Calls.IndexOf("POST /product-integration/v1/signout") < api.Calls.IndexOf("PUT /player-session-lifecycle/v1/session"), "Logout completes before session configuration");

        api = new FakeApi { AuthenticatedInitially = true };
        api.SignoutPermissions.Enqueue("DisallowedOtherProducts");
        api.SignoutPermissions.Enqueue("DisallowedOtherProducts");
        await api.Login();
        Check(api.Calls.Count(call => call == "GET /product-integration/v1/signout/permission") == 3,
            "Waits for closed game product session cleanup before permitted signout");
        Check(api.Progress.Any(message => message.Contains("세션 정리")), "Reports delayed product session cleanup");

        foreach (string permission in new[] { "Disallowed", "DisallowedOtherProducts" })
        {
            api = new FakeApi { AuthenticatedInitially = true, SignoutPermission = permission };
            await ExpectFailureAsync(() => api.Login(), "게임을 마친 후");
            Check(api.Calls.All(call => call.StartsWith("GET ")), "Denied Riot signout permission prevents all state changes");
        }

        api = new FakeApi();
        api.InitialSession = (JObject)api.Session.DeepClone();
        await api.Login();
        Check(api.Calls.Count == 1 && api.Calls[0].StartsWith("GET "), "Already selected account never logs out");

        api = new FakeApi();
        await ExpectFailureAsync(() => api.Login(ensureCanSwitch: () => { throw new InvalidOperationException("게임 실행 중"); }), "게임 실행 중");
        Check(api.Calls.Count == 0, "In-game guard blocks all requests");
        int guardChecks = 0;
        api = new FakeApi { AuthenticatedInitially = true };
        await ExpectFailureAsync(() => api.Login(ensureCanSwitch: () => { if (++guardChecks > 1) throw new InvalidOperationException("게임 실행 중"); }), "게임 실행 중");
        Check(api.Calls.All(call => call.StartsWith("GET ")), "Game starting after preflight prevents logout");

        api = new FakeApi { Authentication = JObject.Parse("{type:'error',error:'auth_failure'}") };
        await ExpectFailureAsync(() => api.Login(), "비밀번호");
        Check(api.Calls.Count(call => call.EndsWith("/complete")) == 1 && !api.Calls.Any(call => call.EndsWith("/login-token")), "Wrong password stops immediately");

        foreach (int status in new[] { 409, 429 })
        {
            api = new FakeApi { CredentialFailureStatus = status };
            await ExpectFailureAsync(() => api.Login(), status == 429 ? "제한" : "HTTP 409");
            Check(api.Calls.Count(call => call.EndsWith("/complete")) == 1, "Credential HTTP failures are never retried or treated as session races");
        }
        api = new FakeApi { InitialReadFailureStatus = 401 };
        await ExpectFailureAsync(() => api.Login(), "HTTP 401");
        Check(api.Calls.Count == 1, "Authentication errors reading a session are not mistaken for missing sessions");

        foreach (string response in new[] { "{type:'multifactor'}", "{type:'auth',captcha:{type:'hcaptcha'}}", "{type:'error',error:'captcha_required'}" })
        {
            api = new FakeApi { Authentication = JObject.Parse(response) };
            await api.Login();
            Check(api.Progress.Any(message => message.Contains("추가 인증")), "Interactive authentication handoff");
            Check(!api.Calls.Any(call => call.EndsWith("/login-token")), "No invented token during interactive login");
        }
        api = new FakeApi { UseUi = true, Authentication = JObject.Parse("{type:'auth',error:'captcha_not_allowed',captcha:{type:'hcaptcha'}}") };
        await api.Login();
        Check(api.UiSubmissions == 1, "Native Riot form is filled once when direct credentials require CAPTCHA");
        Check(api.Calls.Count(call => call.EndsWith("/complete")) == 1, "UI fallback does not resend the direct credential API request");
        api = new FakeApi { UseUi = true, AuthorizeBeforeUi = true, Authentication = JObject.Parse("{type:'auth'}") };
        await api.Login();
        Check(api.UiSubmissions == 0, "Never type into the Riot home screen after concurrent sign-in");
        api = new FakeApi { UseUi = true, Authentication = JObject.Parse("{type:'multifactor'}") };
        await api.Login();
        Check(api.UiSubmissions == 0, "MFA prompts do not receive username/password keystrokes");
        api = new FakeApi { Session = JObject.Parse("{loginState:'Authorized',puuid:'wrong-puuid',userInfo:{username:'wrong'}}") };
        await ExpectFailureAsync(() => api.Login(), "다릅니다");
        api = new FakeApi { Session = JObject.Parse("{loginState:'Error',errorCode:'denied'}") };
        await ExpectFailureAsync(() => api.Login(), "실패");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            api = new FakeApi();
            bool cancelled = false;
            try { await api.Login(cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && api.Calls.Count == 0, "Cancellation stops authentication");
        }
        api = new FakeApi();
        await ExpectFailureAsync(() => new AccountLoginService(api, () => { }).LoginAsync("", "", "ko_KR", api.Progress.Add, CancellationToken.None), "credentials.conf");
        Check(api.Calls.Count == 0, "Empty credentials do not sign out the current account");
    }

    private static void TestSettingsAndLayout()
    {
        string originalDirectory = Environment.CurrentDirectory;
        string directory = Path.Combine(Path.GetTempPath(), "lol-login-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Environment.CurrentDirectory = directory;
            Application.EnableVisualStyles();
            using (var form = new Form1())
            {
                Form1.Object = form;
                var saver = new SettingSaver(directory);
                File.WriteAllText("credentials.conf", "one, two,,one\npassword-with-spaces  ");
                saver.LoadCredentials();
                Check(Form1.leagueID.SequenceEqual(new[] { "one", "two" }), "Account list trims and deduplicates");
                Check(Form1.leaguePass == "password-with-spaces  ", "Password whitespace is preserved");
                form.comboBox2.Items.AddRange(new object[] { "one", "two" });
                form.comboBox2.SelectedItem = "two";
                saver.SaveSettings();
                string saved = File.ReadAllText("settings.ini");
                form.comboBox2.SelectedItem = "one";
                saver.LoadSettings();
                Check(form.comboBox2.Text == "two", "Restored selected-account setting");
                File.WriteAllLines("settings.ini", File.ReadAllLines("settings.ini").Where(line => !line.StartsWith("\"Log-in ID\"")));
                saver.LoadSettings();
                Check(form.comboBox2.Text == "two", "Settings from the removed-login version still load");
                File.WriteAllText("settings.ini", saved);
                File.WriteAllText("credentials.conf", "");
                saver.LoadCredentials();
                Check(Form1.leagueID.Count == 0 && Form1.leaguePass == "", "Empty credential file is handled");
                Check(form.comboBox2.DropDownStyle == ComboBoxStyle.DropDownList, "Account selection is a list");
                Check(form.materialRaisedButton30.Parent == form.tabPage1 && form.materialRaisedButton38.Parent == form.tabPage1, "Both login buttons exist on the League tab");
                Check(!form.comboBox2.Bounds.IntersectsWith(form.materialRaisedButton38.Bounds), "Login controls do not overlap");
                // Render without invoking app startup, global hooks, network loads, or saving real settings.
                form.Load -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), form,
                    typeof(Form1).GetMethod("Form1_Load", BindingFlags.Instance | BindingFlags.NonPublic));
                form.Shown -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), form,
                    typeof(Form1).GetMethod("Form1_Shown", BindingFlags.Instance | BindingFlags.NonPublic));
                form.FormClosing -= (FormClosingEventHandler)Delegate.CreateDelegate(typeof(FormClosingEventHandler), form,
                    typeof(Form1).GetMethod("Form1_FormClosing", BindingFlags.Instance | BindingFlags.NonPublic));
                foreach (FieldInfo field in typeof(Form1).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    if (field.FieldType == typeof(System.Windows.Forms.Timer)) ((System.Windows.Forms.Timer)field.GetValue(form)).Stop();
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.Show();
                var visibleControls = form.tabPage1.Controls.Cast<Control>().Where(control => control.Visible).ToArray();
                foreach (Control control in visibleControls)
                {
                    Check(form.tabPage1.ClientRectangle.Contains(control.Bounds), "Control stays within tab: " + control.Name);
                    foreach (Control other in visibleControls.Where(other => string.CompareOrdinal(control.Name, other.Name) < 0))
                        Check(!control.Bounds.IntersectsWith(other.Bounds), "Controls do not overlap: " + control.Name + " and " + other.Name);
                }
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "account-login-layout.png"));
                }
                form.Close();
            }
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            // Remove only the unique temporary directory created by this test.
            Directory.Delete(directory, true);
        }
    }

    private static async Task TestLiveReadOnlyAsync()
    {
        var connection = RiotClientConnection.Discover();
        using (var api = new RiotClientApi(connection))
        {
            JObject session = (JObject)await api.SendAsync("GET", "/player-session-lifecycle/v1/session", null, CancellationToken.None);
            Check(session["loginState"] != null, "Live Riot Client session can be read");
            JObject spec = (JObject)await api.SendAsync("GET", "/swagger/v3/openapi.json", null, CancellationToken.None);
            foreach (string path in new[] { "/player-session-lifecycle/v1/session", "/player-session-lifecycle/v1/login-token",
                "/player-session-lifecycle/v1/login-strategy", "/product-integration/v1/signout", "/product-integration/v1/signout/permission",
                "/rso-authenticator/v1/authentication/riot-identity/start", "/rso-authenticator/v1/authentication/riot-identity/complete" })
                Check(spec["paths"]?[path] != null, "Live API supports " + path);
            Console.WriteLine("Live read-only API check passed; no sign-out or credential submission performed.");
        }
    }
}
