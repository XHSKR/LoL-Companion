using MaterialSkin.Controls;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using WindowsInput;

namespace LoL_Companion
{
    public partial class Form1 : MaterialForm
    {
        // Instantiation
        SettingSaver SettingSaver = new SettingSaver();
        LCU_Request LCU_Request = new LCU_Request();
        Websocket Websocket = new Websocket();

        static Form1 _Object;
        public static Form1 Object
        {
            get { return _Object; }
            set { _Object = value; }
        }

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Object = this;

            SettingSaver.LoadCredentials();
            comboBox2.Items.AddRange(leagueID.ToArray());
            comboBox2.DropDownStyle = ComboBoxStyle.DropDownList;

            //Ignore HTTPS certificate error
            ServicePointManager.ServerCertificateValidationCallback += (objSender, certificate, chain, sslPolicyErrors) => true;

            //gHook
            gHook = new GlobalKeyboardHook(); // Create a new GlobalKeyboardHook
            gHook.KeyDown += new KeyEventHandler(gHook_KeyDown); // Declare a KeyDown Event
            // Add the keys you want to hook to the HookedKeys list
            foreach (Keys key in Enum.GetValues(typeof(Keys)))
                gHook.HookedKeys.Add(key);
            gHook.hook();
        }

        private void Form1_Shown(object sender, EventArgs e)
        {
            string versionUrl = "https://ddragon.leagueoflegends.com/api/versions.json";
            string championUrlFormat = "http://ddragon.leagueoflegends.com/cdn/{0}/data/en_US/champion.json";

            using (HttpClient client = new HttpClient())
            {
                string versionResponse = client.GetStringAsync(versionUrl).GetAwaiter().GetResult();
                string currentVersion = JArray.Parse(versionResponse)?.FirstOrDefault()?.ToString();

                string championUrl = string.Format(championUrlFormat, currentVersion);

                string championResponse = client.GetStringAsync(championUrl).GetAwaiter().GetResult();
                JObject root = JObject.Parse(championResponse);
                var champions = root["data"].Values();

                foreach (var champion in champions)
                {
                    string name = champion["name"].ToString();
                    string key = champion["key"].ToString();
                    string[] values = { name, key };
                    Champion.Add(values);
                }
            }

            //Add Champion Name to the combobox
            for (int i = 0; i < Champion.Count; i++)
            {
                comboBox3.Items.Add(Champion[i][0]);
                comboBox4.Items.Add(Champion[i][0]);
                comboBox6.Items.Add(Champion[i][0]);
            }

            //load checkbox & radiobox states from ini file
            SettingSaver.LoadSettings();

            //Set the location of league folder
            oldLeagueLocation = leagueLocation;
            Process[] process = Process.GetProcessesByName("RiotClientServices");
            try
            {
                if (process.Length != 0) //if riot client is running
                {
                    leagueLocation = process[0].MainModule.FileName;
                    leagueLocation = leagueLocation.Substring(0, leagueLocation.Length - 34);
                    leagueLocation += @"League of Legends\";
                }
            }
            catch { }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            gHook.unhook();
            SettingSaver.SaveSettings(); //Save checkbox & radiobox states
        }

        //Declare Variables
        string oldLeagueLocation;

        string summonerName, summonerId, accountId, region, email;

        public List<string[]> Champion = new List<string[]>();

        public static string leagueLocation = "C:/Riot Games/League of Legends/";

        GlobalKeyboardHook gHook;

        int[] lanerTimer = new int[5];
        string[] laners;

        int ingameTime;
        DateTime dt;

        public static bool isClientFocused = false;
        bool isClientRunning = false;
        bool isinGameRunning = false;
        bool isinGameFocused = false;

        bool isChatMuted = false;
        bool isReplay = true;

        public static List<string> leagueID;
        public static string leaguePass;

        public static bool isConnectedtoWebsocket = false;

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        private void SendMessage(string chatMessage)
        {
            if (!string.IsNullOrEmpty(chatMessage))
            {
                float PartsSize = 30;

                IEnumerable<String> iText = StringSpliter.SplitString(chatMessage, (int)Math.Round(PartsSize));
                var aText = iText.ToArray();
                int i = 0;
                InputSimulator Simulator = new InputSimulator();
                //Thread.Sleep(75);
                while (i < aText.Length)
                {
                    Simulator.Keyboard.TextEntry(aText[i]);
                    i++;
                    Thread.Sleep(20);
                }
            }
        }

        private void SimulateEnter()
        {
            InputSimulator Simulator = new InputSimulator();
            Simulator.Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.RETURN);
            Thread.Sleep(20);
        }

        private string GetActiveWindowName()
        {
            const int nChars = 256;
            StringBuilder Buff = new StringBuilder(nChars);
            IntPtr handle = GetForegroundWindow();

            if (GetWindowText(handle, Buff, nChars) > 0)
            {
                return Buff.ToString();
            }
            return null;
        }

        private readonly Dictionary<int, int> flashKeyMap = new Dictionary<int, int>
        {
            { Keys.F1.GetHashCode(), 0 },
            { Keys.F2.GetHashCode(), 1 },
            { Keys.F3.GetHashCode(), 2 },
            { Keys.F4.GetHashCode(), 3 },
            { Keys.F5.GetHashCode(), 4 }
        };

        private void gHook_KeyDown(object sender, KeyEventArgs e)
        {
            // 게임 중이며 포커스가 맞을 때만 처리
            if (!isinGameRunning || !isinGameFocused || ingameTime <= 1)
                return;

            // 1) F1~F5 → flashKeyMap 으로 처리
            if (flashKeyMap.TryGetValue(e.KeyValue, out int laneIndex))
            {
                if (lanerTimer[laneIndex] < ingameTime)
                    lanerTimer[laneIndex] = ingameTime + 300;
            }

            // 2) Caps Lock(20) 또는 `(192) 키 무시
            if ((e.KeyValue == 192 && materialCheckBox2.Checked) ||
                (e.KeyValue == 20 && !materialCheckBox2.Checked))
            {
                return;
            }

            // 3) 스펠 타이머 출력 조건
            if (materialCheckBox10.Checked &&
                flashKeyMap.ContainsKey(e.KeyValue))  // F1~F5 혹은 `(192)/20 과 중복 체크
            {
                SendSpellTimers();
            }
        }

        private void SendSpellTimers()
        {
            var sb = new StringBuilder();
            string[] lanes = { "top ", "jg ", "mid ", "ad ", "sup " };

            for (int i = 0; i < lanerTimer.Length; i++)
            {
                if (lanerTimer[i] <= ingameTime)
                    lanerTimer[i] = 0;

                if (lanerTimer[i] == 0)
                    continue;

                // 시간 표시(mm:ss)
                string timeStr = dt.AddSeconds(lanerTimer[i]).ToString("mm:ss");
                sb.Append(timeStr).Append(lanes[i]);
            }

            if (sb.Length == 0)
                return;

            // 맨 끝의 공백 제거
            string result = sb.ToString(0, sb.Length - 1);

            SimulateEnter();
            SendMessage(result);
            SimulateEnter();
        }

        private void timer1_Tick(object sender, EventArgs e) //Variable
        {
            Process[] leagueClient = Process.GetProcessesByName("LeagueClientUXRender");
            Process[] pname = Process.GetProcessesByName("League of Legends");

            isClientRunning = leagueClient.Length != 0;
            isinGameRunning = pname.Length != 0;

            string focusedApp = GetActiveWindowName();
            isClientFocused = focusedApp == "League of Legends";
            isinGameFocused = focusedApp == "League of Legends (TM) Client";

            if (isClientRunning)
            {
                if (!isConnectedtoWebsocket && isReplay)
                    try
                    {
                        RetrieveAccountInfo();
                        Websocket.ConnectToLCU();
                        isConnectedtoWebsocket = true;
                    }
                    catch { }
            }
            else
                isConnectedtoWebsocket = false;

            if (isinGameRunning)
                HandleInGame();
            else
                ResetInGame();
        }

        private async void RetrieveAccountInfo()
        {
            // 1) RiotClientServices 프로세스 탐색 및 경로 설정
            Process[] processes = Process.GetProcessesByName("RiotClientServices");
            if (processes.Length > 0)
            {
                var exePath = processes[0].MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    leagueLocation = Path.Combine(
                        Path.GetDirectoryName(exePath) ?? string.Empty,
                        "League of Legends");
                }
                else
                {
                    // 프로세스 정보가 비어 있으면 기본값으로 처리
                    leagueLocation = oldLeagueLocation;
                }
            }
            else
            {
                // RiotClientServices가 실행 중이 아니면 리턴하거나 기본값 사용
                leagueLocation = oldLeagueLocation;
            }

            // 2) Summoner 정보 가져오기 (실패 시 빈 JObject 반환하도록 GET 수정 권장)
            JObject currentSummoner = null;
            try
            {
                currentSummoner = await LCU_Request.GET("/lol-summoner/v1/current-summoner");
            }
            catch
            {
                // 무시하거나 로깅만 하고 넘어갑니다.
            }
            summonerName = currentSummoner?["displayName"]?.ToString() ?? "Unknown";
            summonerId = currentSummoner?["summonerId"]?.ToString() ?? string.Empty;

            // 3) 로그인 크리덴셜 가져오기
            JObject loginCreds = null;
            try
            {
                loginCreds = await LCU_Request.GET("/lol-login/v1/login-platform-credentials");
            }
            catch { }
            accountId = loginCreds?["username"]?.ToString() ?? string.Empty;

            // 4) Region/Locale 가져오기
            JObject regionInfo = null;
            try
            {
                regionInfo = await LCU_Request.GET("/riotclient/region-locale");
            }
            catch { }
            region = regionInfo?["webRegion"]?.ToString().ToUpper() ?? "UNKNOWN";
            email = "Not Available";

            bool isRiotServer = region != "STAGING.NA";

            if (isRiotServer)
            {
                // 5) Verified Email 가져오기
                JObject emailInfo = null;
                try
                {
                    emailInfo = await LCU_Request.GET("/lol-email-verification/v1/email");
                }
                catch { }
                email = emailInfo?["email"]?.ToString() ?? "Not Available";
            }
            else
            {
                // PBE, 중국, 가레나 등 비공식 서버
                region = "Non-Riot Server (PBE, China, Garena)";
                // leagueLocation은 기존 값 유지
            }

            // 7) UI 업데이트
            materialLabel14.Text =
                $"Summoner's Name: {summonerName}\n" +
                $"Account ID: {accountId}\n" +
                $"E-mail Address: {email}\n" +
                $"Region: {region}";

            // 8) Chat 정보 갱신
            try
            {
                string payload = "{ \"lol\": { \"rankedLeagueDivision\": \"I\", \"rankedLeagueQueue\": \"RANKED_SOLO_5x5\", \"rankedLeagueTier\": \"CHALLENGER\" } }";
                await LCU_Request.PUT("/lol-chat/v1/me", payload);
            }
            catch
            {
                // 무시하거나 필요 시 로깅
            }
        }

        private void HandleInGame()
        {
            if (ingameTime > 1 && isReplay)
            {
                UpdateInGameTime();
                //관전상태인지 확인
                try
                {
                    var client = new WebClient { };
                    client.DownloadData("https://127.0.0.1:2999/liveclientdata/activeplayer");
                    isReplay = false;
                }
                catch
                {
                    isReplay = true;
                }
            }
            //Disconnect websocket while in-game
            if (isConnectedtoWebsocket && !isReplay)
            {
                Websocket.LCU.Close();
                isConnectedtoWebsocket = false;
            }

            if (!isReplay)
            {
                if (materialCheckBox3.Checked && isinGameFocused && ingameTime > 1 && !isChatMuted)
                {
                    Thread.Sleep(50);
                    SimulateEnter();
                    SendMessage("/deafen");
                    SimulateEnter();
                    isChatMuted = true;
                }
            }
        }

        private void ResetInGame()
        {
            ingameTime = 0;
            lanerTimer = new int[5];
            textBox2.Text = "";
            textBox3.Text = "";
            textBox4.Text = "";
            textBox5.Text = "";
            textBox6.Text = "";
            textBox7.Text = "";

            isChatMuted = false;
            isReplay = true;

            if (this.TopMost)
                this.TopMost = false;
            textBox8.Text = string.Empty;
        }

        private void UpdateInGameTime()
        {
            textBox2.Text = dt.AddSeconds(ingameTime).ToString("mm:ss"); //Current Ingame time
            textBox3.Text = dt.AddSeconds(lanerTimer[0]).ToString("mm:ss"); //Top
            textBox4.Text = dt.AddSeconds(lanerTimer[1]).ToString("mm:ss"); //Jungle
            textBox5.Text = dt.AddSeconds(lanerTimer[2]).ToString("mm:ss"); //Mid
            textBox6.Text = dt.AddSeconds(lanerTimer[3]).ToString("mm:ss"); //Adc
            textBox7.Text = dt.AddSeconds(lanerTimer[4]).ToString("mm:ss"); //Support
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            if (isinGameRunning)
                if (ingameTime > 1)
                    try
                    {
                        string input = new WebClient().DownloadString(@"https://127.0.0.1:2999/liveclientdata/gamestats");
                        JObject gamestats = JObject.Parse(input);
                        double gameTime = Convert.ToDouble(gamestats["gameTime"]);
                        if (gameTime > 0.5)
                            ingameTime = Convert.ToInt32(gameTime);
                    }
                    catch { }
                else
                    ingameTime++;
        }

        int draggedTime = 0;
        bool isTimerOnforDragging = false;

        private async void timer3_Tick(object sender, EventArgs e)
        {
            if (materialCheckBox9.Checked && isinGameRunning) //롤 켜지면 강제종료 후 타이머 카운팅 시작
            {
                foreach (var process in Process.GetProcessesByName("League of Legends"))
                {
                    process.Kill();
                }
                isTimerOnforDragging = true;
            }

            if (isTimerOnforDragging)
            {
                draggedTime++;
                textBox1.Visible = true;
                textBox1.Text = dt.AddSeconds(draggedTime).ToString("mm:ss");
            }

            if (draggedTime == 230)
            {
                await LCU_Request.POST("/lol-gameflow/v1/reconnect", "0");

                materialCheckBox9.Checked = false;
                draggedTime = 0;
                isTimerOnforDragging = false;
            }
        }

        private void materialRaisedButton1_Click(object sender, EventArgs e) { lanerTimer[0] = 0; }
        private void materialRaisedButton2_Click(object sender, EventArgs e) { lanerTimer[1] = 0; }
        private void materialRaisedButton3_Click(object sender, EventArgs e) { lanerTimer[2] = 0; }
        private void materialRaisedButton4_Click(object sender, EventArgs e) { lanerTimer[3] = 0; }
        private void materialRaisedButton5_Click(object sender, EventArgs e) { lanerTimer[4] = 0; }

        private void materialRaisedButton9_Click(object sender, EventArgs e)
        {
            string selectedChampionId = string.Empty;
            //Find Champion id from List with selected Champion Name
            for (int i = 0; i < Champion.Count; i++)
            {
                if (comboBox4.Text == Champion[i][0])
                {
                    selectedChampionId = Champion[i][1];
                }
            }

            if (selectedChampionId == string.Empty)
            {
                MessageBox.Show("Please select the champion from the list.");
            }
            else
            {
                //datadragon 링크와 챔피언 id를 합침
                string champSplashURL = "http://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/champion-splashes/" + selectedChampionId;
                Process.Start(champSplashURL);
            }
        }

        private void materialSingleLineTextField3_Enter(object sender, EventArgs e)
        {
            materialSingleLineTextField3.Text = string.Empty;
        }

        private async void materialRaisedButton8_Click(object sender, EventArgs e)
        {
            try
            {
                string payload = $"{{\"key\":\"backgroundSkinId\", \"value\":\"{materialSingleLineTextField3.Text}\"}}";
                await LCU_Request.POST("/lol-summoner/v1/current-summoner/summoner-profile", payload);

            }
            catch
            {
                MessageBox.Show("Error while requesting POST method.");
            }
        }

        private void materialRaisedButton11_Click(object sender, EventArgs e)
        {
            materialSingleLineTextField3.Text = "53001";
            materialRaisedButton8_Click(null, null);
        }

        private void materialRaisedButton12_Click(object sender, EventArgs e)
        {
            materialSingleLineTextField3.Text = "4001";
            materialRaisedButton8_Click(null, null);
        }

        private void materialRaisedButton10_Click(object sender, EventArgs e)
        {
            materialSingleLineTextField3.Text = "12001";
            materialRaisedButton8_Click(null, null);
        }

        private async void materialRaisedButton7_Click(object sender, EventArgs e)
        {
            if (System.IO.File.Exists(@"items.js"))
            {
                using (var fileReader = new StreamReader(@"items.js"))
                {
                    string payload = fileReader.ReadLine();
                    await LCU_Request.PUT($"/lol-item-sets/v1/item-sets/{summonerId}/sets", payload);
                }
            }
        }

        private void materialRaisedButton18_Click(object sender, EventArgs e)
        {
            Process.Start(leagueLocation + @"LeagueClient.exe", "--allow-multiple-clients --locale=\"en_AU\"");
        }

        private async void materialRaisedButton24_Click(object sender, EventArgs e)
        {
            try
            {
                await LCU_Request.POST("/riotclient/kill-and-restart-ux", "0");
            }
            catch
            {
                MessageBox.Show("Error while requesting POST method.");
            }
        }

        private async void materialRaisedButton22_Click(object sender, EventArgs e)
        {
            try
            {
                string payload = @"
                {
                  ""customGameLobby"": {
                    ""configuration"": {
                      ""gameMode"": ""PRACTICETOOL"",
                      ""gameMutator"": """",
                      ""gameServerRegion"": """",
                      ""mapId"": 11,
                      ""mutators"": { ""id"": 1 },
                      ""spectatorPolicy"": ""AllAllowed"",
                      ""teamSize"": 5
                    },
                    ""lobbyName"": ""PRACTICETOOL NO LIMIT"",
                    ""lobbyPassword"": null
                  },
                  ""isCustom"": true
                }";
                await LCU_Request.POST("/lol-lobby/v2/lobby", payload);

                await LCU_Request.POST("/lol-lobby/v1/lobby/custom/start-champ-select", "0");

            }
            catch
            {
                MessageBox.Show("Error while requesting POST method.");
            }
        }

        private void TerminateLoL()
        {
            string[] processes = {
                //in-game Client
                "League of Legends", 
                //Riot Client
                "RiotClientCrashHandler",
                "RiotClientServices",
                "RiotClientUx",
                //League Client
                "LeagueClient",
                "LeagueClientUx"
                };

            for (int i = 0; i < processes.Length; i++)
            {
                Console.WriteLine("Terminating " + processes[i] + "...");
                foreach (var process in Process.GetProcessesByName(processes[i]))
                {
                    process.Kill();
                }
            }
        }

        private void AutoLogin(string region)
        {
            TerminateLoL();

            try
            {
                Process.Start(leagueLocation + @"LeagueClient.exe", $"--locale=\"{region}\"");
            }
            catch { }

            bool isComplete = false;
            while (!isComplete)
            {
                try
                {
                    string CommandLine = GetCommandLine();
                    if (string.IsNullOrEmpty(CommandLine)) throw new Exception("CommandLine is empty");

                    var matches = Regex.Match(CommandLine, "(--remoting-auth-token=)([^ ]*)( )(--app-port=)([^ ]*)( )");
                    string Pass = matches.Groups[2].Value;
                    string Port = matches.Groups[5].Value;

                    string payload = $"{{\"username\":\"{comboBox2.Text}\", \"password\":\"{leaguePass}\", \"persistLogin\":false}}";

                    byte[] bytes = Encoding.UTF8.GetBytes(payload); // convert from payload string to byte[]

                    using (var client = new WebClient { Credentials = new NetworkCredential("riot", Pass) })
                    {
                        client.UploadData("https://127.0.0.1:" + Port + "/rso-auth/v1/session/credentials", "PUT", bytes);
                    }
                    isComplete = true;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to auto login: {e.Message}");
                    Thread.Sleep(2000);
                }
            }
        }

        private string GetCommandLine()
        {
            string CommandLine = string.Empty;

            var process = Process.GetProcessesByName("RiotClientUx").FirstOrDefault();
            if (process == null) return CommandLine;

            using (var searcher = new ManagementObjectSearcher(
                   $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"))
            using (var objects = searcher.Get())
            {
                CommandLine = objects.Cast<ManagementBaseObject>().SingleOrDefault()?["CommandLine"]?.ToString();
            }

            return CommandLine;
        }

        private void materialRaisedButton30_Click(object sender, EventArgs e)
        {
            AutoLogin("ko_KR");
        }

        private void materialRaisedButton38_Click(object sender, EventArgs e)
        {
            AutoLogin("en_AU");
        }

        public static string savePath;
        private void materialCheckBox13_CheckedChanged(object sender, EventArgs e)
        {
            if (materialCheckBox13.Checked)
            {
                Directory.CreateDirectory("Logs");

                string datetime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                savePath = $"Logs/websocket_{datetime}.json";

                if (materialCheckBox13.Checked)
                    Websocket.Debug();
            }
            else
                Websocket.LCU_Debug.Close();
        }

        private void materialRaisedButton42_Click(object sender, EventArgs e)
        {
            Process.Start(@"Logs");
        }

        //Position Call Out
        private void materialCheckBox12_CheckedChanged(object sender, EventArgs e)
        {
            if (materialCheckBox12.Checked)
            {
                materialRadioButton5.Enabled = false;
                materialRadioButton6.Enabled = false;
                materialRadioButton7.Enabled = false;
                materialRadioButton8.Enabled = false;
                materialRadioButton9.Enabled = false;
            }
            else
            {
                materialRadioButton5.Enabled = true;
                materialRadioButton6.Enabled = true;
                materialRadioButton7.Enabled = true;
                materialRadioButton8.Enabled = true;
                materialRadioButton9.Enabled = true;
            }
        }

        //Champion Instant Lock
        public static string selectedChampionId = "";
        private void materialCheckBox14_CheckedChanged(object sender, EventArgs e)
        {
            if (materialCheckBox14.Checked)
            {
                selectedChampionId = "";
                //Find Champion id from List with selected Champion Name
                for (int i = 0; i < Champion.Count; i++)
                {
                    if (comboBox3.Text == Champion[i][0])
                    {
                        selectedChampionId = Champion[i][1];
                    }
                }

                if (selectedChampionId == string.Empty)
                {
                    MessageBox.Show("Please select the champion from the list.");
                    materialCheckBox14.Checked = false;
                }
                else
                {
                    //When disabled, sometimes the text gets ugly so here's the fix
                    string placeholder = comboBox3.Text;
                    comboBox3.Text = string.Empty;
                    comboBox3.Text = placeholder;
                    comboBox3.Enabled = false;
                    materialCheckBox16.Enabled = false;
                }
            }
            else
            {
                comboBox3.Enabled = true;
                materialCheckBox16.Enabled = true;
                selectedChampionId = "";
            }
        }

        private void comboBox3_Enter(object sender, EventArgs e)
        {
            comboBox3.Text = string.Empty;
        }

        //Champion Auto Ban
        public static string selectedBanChampionId = "";
        private void materialCheckBox20_CheckedChanged(object sender, EventArgs e)
        {
            if (materialCheckBox20.Checked)
            {
                selectedBanChampionId = "";
                //Find Champion id from List with selected Champion Name
                for (int i = 0; i < Champion.Count; i++)
                {
                    if (comboBox6.Text == Champion[i][0])
                    {
                        selectedBanChampionId = Champion[i][1];
                    }
                }

                if (selectedBanChampionId == string.Empty)
                {
                    MessageBox.Show("Please select the champion from the list.");
                    materialCheckBox20.Checked = false;
                }
                else
                {
                    //When disabled, sometimes the text gets ugly so here's the fix
                    string placeholder = comboBox6.Text;
                    comboBox6.Text = string.Empty;
                    comboBox6.Text = placeholder;
                    comboBox6.Enabled = false;
                }
            }
            else
            {
                comboBox6.Enabled = true;
                selectedBanChampionId = "";
            }
        }

        private void comboBox6_Enter(object sender, EventArgs e)
        {
            comboBox6.Text = String.Empty;
        }

        private async void materialRaisedButton6_Click(object sender, EventArgs e)
        {
            // 큐 생성
            string payload1 = "{ \"queueId\": 400 }";
            await LCU_Request.POST("/lol-lobby/v2/lobby", payload1);

            // 포지션 설정
            string payload2 = "{ \"firstPreference\":\"JUNGLE\", \"secondPreference\":\"MIDDLE\" }";
            await LCU_Request.PUT("/lol-lobby/v2/lobby/members/localMember/position-preferences", payload2);

            // 큐 돌리기 (matchmaking 시작)
            string payload3 = "0";
            await LCU_Request.POST("/lol-lobby/v2/lobby/matchmaking/search", payload3);
        }

        private async void materialRaisedButton20_Click(object sender, EventArgs e)
        {
            // 큐 생성
            string payload1 = "{ \"queueId\": 420 }";
            await LCU_Request.POST("/lol-lobby/v2/lobby", payload1);

            // 포지션 설정
            string payload2 = "{ \"firstPreference\":\"JUNGLE\", \"secondPreference\":\"MIDDLE\" }";
            await LCU_Request.PUT("/lol-lobby/v2/lobby/members/localMember/position-preferences", payload2);

            // 큐 돌리기 (matchmaking 시작)
            string payload3 = "0";
            await LCU_Request.POST("/lol-lobby/v2/lobby/matchmaking/search", payload3);
        }

        private async void materialRaisedButton28_Click(object sender, EventArgs e)
        {
            try
            {
                // 1) 현재 소환사 정보 조회
                JObject currentSummoner = await LCU_Request.GET("/lol-summoner/v1/current-summoner");
                string summonerId = currentSummoner["summonerId"].ToString();

                // 2) 해당 소환사의 아이템 세트 조회
                JObject itemSets = await LCU_Request.GET($"/lol-item-sets/v1/item-sets/{summonerId}/sets");
                string itemSetsJson = itemSets.ToString();

                // 3) 클립보드에 복사
                Clipboard.SetText(itemSetsJson);

                // 4) 파일에 저장
                using (var writer = new StreamWriter(@"items.js"))
                {
                    writer.WriteLine(itemSetsJson);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("GET 요청 중 오류가 발생했습니다: " + ex.Message);
            }
        }

        private void materialCheckBox9_CheckedChanged(object sender, EventArgs e)
        {
            if (materialCheckBox9.Checked)
            {
                timer3.Enabled = true;
            }
            else
            {
                timer3.Enabled = false;
                textBox1.Visible = false;
                draggedTime = 0;
                isTimerOnforDragging = false;
            }
        }

        private void materialSingleLineTextField1_Enter(object sender, EventArgs e)
        {
            materialSingleLineTextField1.Text = string.Empty;
        }

        private void materialSingleLineTextField2_Enter(object sender, EventArgs e)
        {
            materialSingleLineTextField2.Text = string.Empty;
        }

        private void materialCheckBox19_CheckedChanged(object sender, EventArgs e)
        {
            if (materialCheckBox19.Checked)
            {
                if (materialSingleLineTextField1.Text == "Enter your phrase" || materialSingleLineTextField1.Text == string.Empty)
                {
                    MessageBox.Show("Please enter your phrase.");
                    materialCheckBox19.Checked = false;
                }
                else
                    materialSingleLineTextField1.Enabled = false;
            }
            else
                materialSingleLineTextField1.Enabled = true;
        }

        private void materialCheckBox21_CheckedChanged(object sender, EventArgs e)
        {
            if (materialCheckBox21.Checked)
            {
                if (materialSingleLineTextField2.Text == "Enter your phrase" || materialSingleLineTextField2.Text == string.Empty)
                {
                    MessageBox.Show("Please enter your phrase.");
                    materialCheckBox21.Checked = false;
                }
                else
                    materialSingleLineTextField2.Enabled = false;
            }
            else
                materialSingleLineTextField2.Enabled = true;
        }

        private void SetLobbyPreset(string position, string champion, string ban)
        {
            // Position
            if (position == "Top")
                materialRadioButton5.Checked = true;
            else if (position == "Jungle")
                materialRadioButton6.Checked = true;
            else if (position == "Mid")
                materialRadioButton7.Checked = true;
            else if (position == "ADC")
                materialRadioButton8.Checked = true;
            else if (position == "Support")
                materialRadioButton9.Checked = true;

            materialCheckBox12.Checked = true;

            // Champion
            comboBox3.Text = champion;
            materialCheckBox16.Checked = true;
            materialCheckBox14.Checked = false;
            materialCheckBox14.Checked = true;

            // Ban
            comboBox6.Text = ban;
            materialCheckBox20.Checked = false;
            materialCheckBox20.Checked = true;
        }

        private void materialRaisedButton34_Click(object sender, EventArgs e)
        {
            SetLobbyPreset("Jungle", "Master Yi", "Naafiri");
        }

        private void materialRaisedButton36_Click(object sender, EventArgs e)
        {
            SetLobbyPreset("Support", "Zilean", "Thresh");
        }

        private void materialRaisedButton40_Click(object sender, EventArgs e)
        {
            SetLobbyPreset("Support", "Lulu", "Blitzcrank");
        }

        private async void materialRadioButton5_CheckedChanged(object sender, EventArgs e)
        {
            System.Windows.Forms.RadioButton radioButton = (System.Windows.Forms.RadioButton)sender;

            string firstPreference = "";

            switch (radioButton.Name)
            {
                case "materialRadioButton5":
                    firstPreference = "TOP";
                    break;
                case "materialRadioButton6":
                    firstPreference = "JUNGLE";
                    break;
                case "materialRadioButton7":
                    firstPreference = "MIDDLE";
                    break;
                case "materialRadioButton8":
                    firstPreference = "BOTTOM";
                    break;
                case "materialRadioButton9":
                    firstPreference = "UTILITY";
                    break;
            }

            if (radioButton.Checked)
            {
                try
                {
                    if (isClientRunning)
                    {
                        string payload = $"{{ \"firstPreference\":\"{firstPreference}\", \"secondPreference\":\"FILL\" }}";
                        await LCU_Request.PUT("/lol-lobby/v2/lobby/members/localMember/position-preferences", payload);
                    }
                }
                catch { }
            }
        }
    }
}
