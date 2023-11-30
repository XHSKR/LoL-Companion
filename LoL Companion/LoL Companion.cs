using IWshRuntimeLibrary;
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
using System.Linq.Expressions;

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

            SettingSaver.loadCredentials();
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
            SettingSaver.loadStates();

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
            SettingSaver.saveStates(); //Save checkbox & radiobox states
        }

        //Declare Variables
        string oldLeagueLocation;

        string summonerName, summonerId, accountId, region, email;

        public List<string[]> Champion = new List<string[]>();

        public string leagueLocation = "C:/Riot Games/League of Legends/";
        public string riotPort, riotPass, completeURL;
        public string response;
        public string json = string.Empty;

        GlobalKeyboardHook gHook;

        int[] lanerTimer = new int[5];
        string[] laners;

        int ingameTime;
        DateTime dt;

        public bool isClientRunning = false;
        public bool isinGameRunning = false;
        public bool isClientFocused = false;
        bool isinGameFocused = false;

        bool isChatMuted = false;
        bool isReplay = true;
        int afkTime = 0;

        public List<string> leagueID;
        public string leaguePass;

        public bool isConnectedtoWebsocket = false;

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        private void sendMessage(string chatMessage)
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

        private void simulateEnter()
        {
            InputSimulator Simulator = new InputSimulator();
            Simulator.Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.RETURN);
            Thread.Sleep(20);
        }

        private string getActiveWindowName()
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

        private void sendAfkWarning()
        {
            this.TopMost = true;

            this.WindowState = FormWindowState.Normal;
            System.Media.SystemSounds.Beep.Play();
            label1.Visible = true;
        }

        private void gHook_KeyDown(object sender, KeyEventArgs e)
        {
            if (isinGameRunning && isinGameFocused && ingameTime > 1)
            {
                if (e.KeyValue.ToString() == "122") //F11 Key
                {
                    Process[] runningProcesses = Process.GetProcesses();
                    foreach (Process process in runningProcesses)
                    {
                        if (process.ProcessName == "League of Legends")
                            process.CloseMainWindow();
                    }
                }

                //Spell Tracker
                if (materialCheckBox10.Checked)
                {
                    laners = new string[5] { "top ", "jg ", "mid ", "ad ", "sup " };
                    if (e.KeyValue.ToString() == "112") //F1
                    {
                        if (lanerTimer[0] < ingameTime)
                            lanerTimer[0] = ingameTime + 300;
                    }
                    if (e.KeyValue.ToString() == "113") //F2
                    {
                        if (lanerTimer[1] < ingameTime)
                            lanerTimer[1] = ingameTime + 300;
                    }
                    if (e.KeyValue.ToString() == "114") //F3
                    {
                        if (lanerTimer[2] < ingameTime)
                            lanerTimer[2] = ingameTime + 300;
                    }
                    if (e.KeyValue.ToString() == "115") //F4
                    {
                        if (lanerTimer[3] < ingameTime)
                            lanerTimer[3] = ingameTime + 300;
                    }
                    if (e.KeyValue.ToString() == "116") //F5
                    {
                        if (lanerTimer[4] < ingameTime)
                            lanerTimer[4] = ingameTime + 300;
                    }

                    /////////////////////////////////////////////

                    if (e.KeyValue.ToString() == "192" && materialCheckBox2.Checked) //Caps Lock을 사용한다고 했는데 `키를 누름
                    {
                        return;
                    }
                    if (e.KeyValue.ToString() == "20" && !materialCheckBox2.Checked) // `키를 사용한다고 했는데 Caps Lock을 누름
                    {
                        return;
                    }

                    if (e.KeyValue.ToString() == "112" || e.KeyValue.ToString() == "113" || e.KeyValue.ToString() == "114" ||
                        e.KeyValue.ToString() == "115" || e.KeyValue.ToString() == "116" || e.KeyValue.ToString() == "192" || e.KeyValue.ToString() == "20") //Keypress 감지
                    {
                        if (isinGameRunning)
                        {
                            string spelltimes = "";
                            string editedSpelltimes = "";

                            for (int i = 0; i < 5; i++)
                            {
                                if (lanerTimer[i] <= ingameTime) //시간이 넘어가면 라인별 점멸시간 삭제
                                {
                                    lanerTimer[i] = 0;
                                }

                                if (lanerTimer[i] != 0)
                                {
                                    string fourDigit;
                                    if (materialCheckBox1.Checked)
                                    {
                                        string lanerTimerString = lanerTimer[i].ToString();
                                        string roundDown = lanerTimerString.Substring(0, lanerTimerString.Length - 1) + "0";
                                        lanerTimer[i] = Convert.ToInt32(roundDown);
                                        fourDigit = dt.AddSeconds(lanerTimer[i]).ToString("mm:ss");
                                    }
                                    else
                                    {
                                        fourDigit = dt.AddSeconds(lanerTimer[i]).ToString("mm:ss");

                                    }
                                    spelltimes += fourDigit + laners[i];
                                    editedSpelltimes = spelltimes.Substring(0, spelltimes.Length - 1);
                                }
                            }
                            if (editedSpelltimes != "")
                            {
                                simulateEnter();
                                sendMessage(editedSpelltimes);
                                simulateEnter();
                            }
                        }
                    }
                }
            }
        }

        private void timer1_Tick(object sender, EventArgs e) //Variable
        {
            Process[] leagueClient = Process.GetProcessesByName("LeagueClientUXRender");
            Process[] pname = Process.GetProcessesByName("League of Legends");

            isClientRunning = leagueClient.Length != 0;
            isinGameRunning = pname.Length != 0;

            string focusedApp = getActiveWindowName();
            isClientFocused = focusedApp == "League of Legends";
            isinGameFocused = focusedApp == "League of Legends (TM) Client";

            if (isClientRunning)
            {
                if (!isConnectedtoWebsocket && isReplay)
                    try
                    {
                        RetrieveAccountInfo();
                        Websocket.connectToLCU();
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

        private void RetrieveAccountInfo()
        {
            Process[] processes = Process.GetProcessesByName("RiotClientServices");
            if (processes.Length > 0)
            {
                leagueLocation = processes[0].MainModule.FileName;
                leagueLocation = leagueLocation.Substring(0, leagueLocation.Length - 34);
                leagueLocation += @"League of Legends\";
            }

            LCU_Request.GET("/lol-summoner/v1/current-summoner");
            JObject current_summoner = JObject.Parse(response);
            summonerName = current_summoner["displayName"].ToString();
            summonerId = current_summoner["summonerId"].ToString();

            LCU_Request.GET("/lol-login/v1/login-platform-credentials");
            JObject login_platform_credentials = JObject.Parse(response);
            accountId = login_platform_credentials["username"].ToString();

            LCU_Request.GET("/riotclient/region-locale");
            JObject region_locale = JObject.Parse(response);
            region = region_locale["webRegion"].ToString().ToUpper();
            email = "Not Available";

            bool isRiotServer = region != "STAGING.NA";

            if (isRiotServer)
            {
                //If it's a Riot Server, get the email address of the account.
                LCU_Request.GET("/lol-email-verification/v1/email");
                JObject email_ = JObject.Parse(response);
                email = email_["email"].ToString();

                // Modify game.cfg file to use Replay API
                string path = leagueLocation + @"Config\game.cfg";
                var oldLines = System.IO.File.ReadAllLines(path);
                var newLines = oldLines.Where(line => !line.Contains("EnableReplayApi"));
                var newLines2 = newLines.Where(line => !line.Contains("[General]"));
                System.IO.File.WriteAllLines(path, newLines2);
                string content = System.IO.File.ReadAllText(path);
                content = "[General]" + "\n" + "EnableReplayApi=1" + "\n" + content;
                System.IO.File.WriteAllText(path, content);

                // Modify game.cfg file to use Replay API
                string gameCfgPath = Path.Combine(leagueLocation, @"Config\game.cfg");
                string[] gameCfgLines = System.IO.File.ReadAllLines(gameCfgPath);
                var modifiedLines = new List<string> { "[General]", "EnableReplayApi=1" };
                modifiedLines.AddRange(gameCfgLines.Where(line => !line.Contains("EnableReplayApi")));
                System.IO.File.WriteAllLines(gameCfgPath, modifiedLines);
            }
            else
            {
                leagueLocation = oldLeagueLocation;

                region = "Non-Riot Server (PBE, China, Garena)";
            }

            materialLabel14.Text = $"Summoner's Name: {summonerName}\nAccount ID: {accountId}\nE-mail Address: {email}\nRegion: {region}";

            json = "{ \"lol\": { \"rankedLeagueDivision\": \"I\", \"rankedLeagueQueue\": \"RANKED_SOLO_5x5\", \"rankedLeagueTier\": \"CHALLENGER\" } }";
            LCU_Request.PUT("/lol-chat/v1/me");
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
                    simulateEnter();
                    sendMessage("/deafen");
                    simulateEnter();
                    isChatMuted = true;
                }

                // first afkwarning occurs at 2:40
                if (afkTime >= 1 && ingameTime == 160)
                    sendAfkWarning();

                // afkTime 2:10 (130)
                if (ingameTime >= 430 && afkTime == 130)
                    sendAfkWarning();

                textBox8.Text = dt.AddSeconds(afkTime).ToString("mm:ss");
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
            afkTime = 0;
            textBox8.Text = string.Empty;
            label1.Visible = false;
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

            // increases afktime (Manual)
            if (!isinGameFocused && ingameTime > 1 && !isReplay)
                afkTime++;
            else
            {
                if (this.TopMost)
                    this.TopMost = false;
                afkTime = 0;
                textBox8.Text = string.Empty;
                label1.Visible = false;
            }
        }

        int draggedTime = 0;
        bool isTimerOnforDragging = false;

        private void timer3_Tick(object sender, EventArgs e)
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
                json = "0";
                LCU_Request.POST("/lol-gameflow/v1/reconnect");

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

        private void materialRaisedButton8_Click(object sender, EventArgs e)
        {
            try
            {
                json = ("{" + $"\"key\":\"backgroundSkinId\", \"value\":\"{materialSingleLineTextField3.Text}\"" + "}");
                LCU_Request.POST("/lol-summoner/v1/current-summoner/summoner-profile");
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

        private void materialRaisedButton7_Click(object sender, EventArgs e)
        {
            if (System.IO.File.Exists(@"items.js"))
            {
                var file_read = new StreamReader(@"items.js");
                json = file_read.ReadLine();
                LCU_Request.PUT("/lol-item-sets/v1/item-sets/" + summonerId + "/sets");
                file_read.Close();
            }
        }

        private void materialRaisedButton18_Click(object sender, EventArgs e)
        {
            Process.Start(leagueLocation + @"LeagueClient.exe", "--allow-multiple-clients --locale=\"en_AU\"");
        }

        private void materialRaisedButton24_Click(object sender, EventArgs e)
        {
            try
            {
                json = "0";
                LCU_Request.POST("/riotclient/kill-and-restart-ux");
            }
            catch
            {
                MessageBox.Show("Error while requesting POST method.");
            }
        }

        private void materialRaisedButton22_Click(object sender, EventArgs e)
        {
            try
            {
                json = "{ \"customGameLobby\": { \"configuration\": { \"gameMode\": \"PRACTICETOOL\", \"gameMutator\": \"\", \"gameServerRegion\": \"\", \"mapId\": 11, \"mutators\": {\"id\": 1}, \"spectatorPolicy\": \"AllAllowed\", \"teamSize\": 5    },    \"lobbyName\": \"PRACTICETOOL NO LIMIT\",    \"lobbyPassword\": null  },  \"isCustom\": true }";
                LCU_Request.POST("/lol-lobby/v2/lobby");

                json = "0";
                LCU_Request.POST("/lol-lobby/v1/lobby/custom/start-champ-select");
            }
            catch
            {
                MessageBox.Show("Error while requesting POST method.");
            }
        }

        private void terminateLoL()
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
            terminateLoL();

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

                    json = ("{" + $"\"username\":\"{comboBox2.Text}\", \"password\":\"{leaguePass}\", \"persistLogin\":false" + "}");

                    byte[] bytes = Encoding.UTF8.GetBytes(json); //convert from json to byte;
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

        public string savePath;
        private void materialCheckBox13_CheckedChanged(object sender, EventArgs e)
        {
            if (materialCheckBox13.Checked)
            {
                Directory.CreateDirectory("Logs");

                string datetime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                savePath = $"Logs/websocket_{datetime}.json";

                if (materialCheckBox13.Checked)
                    Websocket.debug();
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
        public string selectedChampionId = "";
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
        public string selectedBanChampionId = "";
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

        private void materialRaisedButton20_Click(object sender, EventArgs e)
        {
            json = ("{" + $"\"queueId\": 420" + "}");
            LCU_Request.POST("/lol-lobby/v2/lobby");

            json = "{ \"firstPreference\":\"JUNGLE\", \"secondPreference\":\"MIDDLE\" }";
            LCU_Request.PUT("/lol-lobby/v2/lobby/members/localMember/position-preferences");

            //큐 돌리기
            json = "0";
            LCU_Request.POST("/lol-lobby/v2/lobby/matchmaking/search");
        }

        private void materialRaisedButton28_Click(object sender, EventArgs e)
        {
            try
            {
                LCU_Request.GET("/lol-summoner/v1/current-summoner");
                JObject current_summoner = JObject.Parse(response);
                string summonerId = current_summoner["summonerId"].ToString();
                LCU_Request.GET("/lol-item-sets/v1/item-sets/" + summonerId + "/sets");
                Clipboard.SetText(response);
            }
            catch
            {
                MessageBox.Show("Error while requesting GET method.");
            }

            using (StreamWriter writer = new StreamWriter(@"items.js"))
            {
                writer.WriteLine(response);
                writer.Close();
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
            SetLobbyPreset("Jungle", "Master Yi", "Zac");
        }

        private void materialRaisedButton36_Click(object sender, EventArgs e)
        {
            SetLobbyPreset("Support", "Zilean", "Thresh");
        }

        private void materialRaisedButton40_Click(object sender, EventArgs e)
        {
            SetLobbyPreset("Support", "Lulu", "Blitzcrank");
        }

        private void materialRadioButton5_CheckedChanged(object sender, EventArgs e)
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
                        json = "{ \"firstPreference\":\"" + firstPreference + "\", \"secondPreference\":\"FILL\" }";
                        LCU_Request.PUT("/lol-lobby/v2/lobby/members/localMember/position-preferences");
                    }
                }
                catch { }
            }
        }
    }
}
