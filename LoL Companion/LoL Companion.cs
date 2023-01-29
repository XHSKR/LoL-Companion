using System;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WindowsInput;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Net;
using Newtonsoft.Json;
using MaterialSkin;
using MaterialSkin.Controls;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Management;
using System.Security.Policy;

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

        class RootObject
        {
            public Dictionary<string, Data> data { get; set; }
        }

        class Data
        {
            public string key { get; set; }
            public string name { get; set; }
        }

        public Form1()
        {
            InitializeComponent();
            Object = this;

            comboBox1.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox2.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox5.DropDownStyle = ComboBoxStyle.DropDownList;

            //Ignore HTTPS certificate error
            ServicePointManager.ServerCertificateValidationCallback += (objSender, certificate, chain, sslPolicyErrors) => true;

            var materialSkinManager = MaterialSkinManager.Instance;
            materialSkinManager.AddFormToManage(this);
            materialSkinManager.Theme = MaterialSkinManager.Themes.LIGHT;
            materialSkinManager.ColorScheme = new ColorScheme(Primary.BlueGrey800, Primary.BlueGrey900, Primary.BlueGrey500, Accent.LightBlue200, TextShade.WHITE);

            //gHook
            gHook = new GlobalKeyboardHook(); // Create a new GlobalKeyboardHook
            gHook.KeyDown += new KeyEventHandler(gHook_KeyDown); // Declare a KeyDown Event
            // Add the keys you want to hook to the HookedKeys list
            foreach (Keys key in Enum.GetValues(typeof(Keys)))
                gHook.HookedKeys.Add(key);
            gHook.hook();

            //Get the latest version of league and combine with the given URL to retrieve champion data
            string version = new WebClient().DownloadString(@"https://ddragon.leagueoflegends.com/api/versions.json");
            var reg = new Regex("\".*?\"");
            var matches = reg.Matches(version);
            string currentVersion = matches[0].ToString().Replace("\"", "");
            string champURL = "http://ddragon.leagueoflegends.com/cdn/" + currentVersion + "/data/en_US/champion.json";

            var json = new WebClient().DownloadString(champURL);
            RootObject root = JsonConvert.DeserializeObject<RootObject>(json);
            foreach (string key in root.data.Keys)
            {
                Data champion = root.data[key];
                string[] values = { champion.name, champion.key };
                Champion.Add(values);
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

            //Set the default preset of chatMacro
            if (chatMacro1 == null && chatMacro2 == null && chatMacro3 == null && chatMacro4 == null && chatMacro5 == null)
            {
                chatMacro1 = "Message 1";
                chatMacro2 = "Message 2";
                chatMacro3 = "Message 3";
            }
            materialSingleLineTextField1.Text = chatMacro1;

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

        public bool isNewUser = false;

        string summonerName;
        string summonerId;
        string accountId;
        string region;
        string email;

        public List<string[]> Champion = new List<string[]>();

        public string leagueLocation = "C:/Riot Games/League of Legends/";
        public string riotURL = "https://127.0.0.1:";
        public string riotPort;
        public string riotPass;
        public string completeURL;
        public string response;
        public string json = string.Empty;

        GlobalKeyboardHook gHook;

        int[] lanerTimer = new int[5];
        string[] laners;

        int ingameTime;
        DateTime dt;

        bool isClientRunning = false;
        public bool isinGameRunning = false;
        public bool isClientFocused = false;
        bool isinGameFocused = false;
        bool isInGameTimeReceived = false;

        bool isChatMuted = false;
        bool isReplayChecked = false;
        bool isReplay = false;
        bool isReplayDataSent = false;
        int afkTime = 0;
        bool isAutomaticfAfkTriggered = false;

        int afkTimeForAutomation = 0;

        public bool isRiotServer = false;

        public class Receiver
        {
            public string summonerId { get; set; }

        }

        public bool isAccountInfoRetrieved = false;
        public bool isConnectedtoWebsocket = false;
        public bool isSummonerSearched = false;

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")] static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

        private const int MOUSEEVENTF_LEFTDOWN = 0x02;
        private const int MOUSEEVENTF_LEFTUP = 0x04;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x08;
        private const int MOUSEEVENTF_RIGHTUP = 0x10;

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void sendMessage(string chatMessage)
        {
            if (string.IsNullOrEmpty(chatMessage) == false)
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

        public void simulateEnter()
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

        public void sendAfkWarning()
        {
            this.TopMost = true;
            this.WindowState = FormWindowState.Normal;
            System.Media.SystemSounds.Beep.Play();
            label1.Visible = true;
        }

        public void gHook_KeyDown(object sender, KeyEventArgs e)
        {
            if (isinGameRunning = true && isinGameFocused == true && isInGameTimeReceived == true)
            {
                // Spamming
                if (e.KeyValue.ToString() == "222" && isSpamOn == true) // ' key 
                {
                    string str = materialSingleLineTextField6.Text;
                    str = str.ToUpper(); //대문자로 치환

                    if (materialCheckBox7.Checked == true) // Accumulative
                    {
                        for (int i = 0; i < str.Length; i++)
                        {
                            simulateEnter();
                            if (materialCheckBox6.Checked == true) //전체채팅이 켜져있다면
                                sendMessage("/all " + str.Substring(0, i + 1));
                            else
                                sendMessage(str.Substring(0, i + 1));
                            simulateEnter();
                        }
                    }
                    else //기본
                    {
                        simulateEnter();
                        if (materialCheckBox6.Checked == true) //전체채팅이 켜져있다면
                            sendMessage("/all " + str.ToString());
                        else
                            sendMessage(str);
                        simulateEnter();
                    }
                }

                if (e.KeyValue.ToString() == "123") //F12 Key
                {
                    foreach (var process in Process.GetProcessesByName("League of Legends"))
                    {
                        process.Kill();
                    }
                }

                //Spell Tracker
                if (materialCheckBox10.Checked == true && materialCheckBox19.Checked == false)
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

                    if (e.KeyValue.ToString() == "192" && materialCheckBox2.Checked == true) //Caps Lock을 사용한다고 했는데 `키를 누름
                    {
                        return;
                    }
                    if (e.KeyValue.ToString() == "20" && materialCheckBox2.Checked == false) // `키를 사용한다고 했는데 Caps Lock을 누름
                    {
                        return;
                    }

                    if (e.KeyValue.ToString() == "112" || e.KeyValue.ToString() == "113" || e.KeyValue.ToString() == "114" ||
                        e.KeyValue.ToString() == "115" || e.KeyValue.ToString() == "116" || e.KeyValue.ToString() == "192" || e.KeyValue.ToString() == "20") //Keypress 감지
                    {
                        if (isinGameRunning == true)
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
                                    if (materialCheckBox1.Checked == true)
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

                //Chat Macro
                if (materialCheckBox19.Checked == true && materialCheckBox10.Checked == false)
                {
                    if (e.KeyValue.ToString() == "112") //F1
                    {
                        simulateEnter();
                        sendMessage(chatMacro1);
                        simulateEnter();
                    }
                    if (e.KeyValue.ToString() == "113") //F2
                    {
                        simulateEnter();
                        sendMessage(chatMacro2);
                        simulateEnter();
                    }
                    if (e.KeyValue.ToString() == "114") //F3
                    {
                        simulateEnter();
                        sendMessage(chatMacro3);
                        simulateEnter();
                    }
                    if (e.KeyValue.ToString() == "115") //F4
                    {
                        simulateEnter();
                        sendMessage(chatMacro4);
                        simulateEnter();
                    }
                    if (e.KeyValue.ToString() == "116") //F5
                    {
                        simulateEnter();
                        sendMessage(chatMacro5);
                        simulateEnter();
                    }
                }

                if (materialCheckBox15.Checked == true && e.KeyValue.ToString() == "190") // .key
                {
                    if (isAutomaticfAfkTriggered == true)
                        isAutomaticfAfkTriggered = false;
                    else
                    {
                        simulateEnter();
                        sendMessage("Press the button again to abort");

                        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);

                        isAutomaticfAfkTriggered = true;
                    }
                }
            }
        }

        private void timer1_Tick(object sender, EventArgs e) //Variable
        {
            //League 클라이언트가 켜져있는지
            Process[] leagueClient = Process.GetProcessesByName("LeagueClientUXRender");
            if (leagueClient.Length != 0)
                isClientRunning = true;
            else
                isClientRunning = false;

            //인게임이 켜져있는지
            Process[] pname = Process.GetProcessesByName("League of Legends");
            if (pname.Length != 0)
                isinGameRunning = true;
            else
                isinGameRunning = false;

            //포커싱
            string focusedApp = getActiveWindowName();
            if (focusedApp == "League of Legends")
                isClientFocused = true;
            else
                isClientFocused = false;

            if (focusedApp == "League of Legends (TM) Client")
                isinGameFocused = true;
            else
                isinGameFocused = false;


            ////////////////////////////////////////////////////////////////////////////////////////////


            if (isClientRunning == true && isinGameRunning == false) //클라이언트만 켜져있으면 (인게임X)
            {
                if (isAccountInfoRetrieved == false) //데이터를 한번도 받아오지 않았다면
                {
                    Cursor.Current = Cursors.WaitCursor;

                    //Set the location of league folder
                    Process[] process = Process.GetProcessesByName("RiotClientServices");
                    try
                    {
                        leagueLocation = process[0].MainModule.FileName;
                        leagueLocation = leagueLocation.Substring(0, leagueLocation.Length - 34);
                        leagueLocation += @"League of Legends\";
                    }
                    catch { }

                    //롤 계정정보 가져오기
                    try
                    {
                        LCU_Request.GET("/lol-summoner/v1/current-summoner");
                        dynamic strings = JsonConvert.DeserializeObject(response);
                        summonerName = strings.displayName; //"displayName":"Tracer"
                        summonerId = strings.summonerId; //"summonerId": 2193977851

                        LCU_Request.GET("/lol-login/v1/login-platform-credentials");
                        strings = JsonConvert.DeserializeObject(response);
                        accountId = strings.username; //"username": "xhsoce"

                        LCU_Request.GET("/riotclient/region-locale");
                        strings = JsonConvert.DeserializeObject(response);
                        region = strings.webRegion; //"webRegion": "oce"
                        region = region.ToUpper();
                        email = "Not Available";

                        if (region == "STAGING.NA")
                            isRiotServer = false;
                        else
                            isRiotServer = true;

                        if (isRiotServer == false)
                        {
                            leagueLocation = oldLeagueLocation;

                            materialCheckBox4.Enabled = false;
                            materialCheckBox11.Enabled = false;
                            materialCheckBox5.Enabled = false;
                            materialCheckBox4.Checked = false;
                            materialCheckBox11.Checked = false;
                            materialCheckBox5.Checked = false;

                            region = "Non-Riot Server (PBE, China, Garena)";
                        }
                        else
                        {
                            materialCheckBox4.Enabled = true;
                            materialCheckBox11.Enabled = true;
                            materialCheckBox5.Enabled = true;

                            //If it's a Riot Server, get an email address of the account.
                            LCU_Request.GET("/lol-email-verification/v1/email");
                            strings = JsonConvert.DeserializeObject(response);
                            email = strings.email; //"email": "mail@xhs.kr"

                            //Modifying game.cfg file to use Replay API
                            string path = leagueLocation + @"Config\game.cfg";
                            var oldLines = System.IO.File.ReadAllLines(path);
                            var newLines = oldLines.Where(line => !line.Contains("EnableReplayApi"));
                            var newLines2 = newLines.Where(line => !line.Contains("[General]"));
                            System.IO.File.WriteAllLines(path, newLines2);
                            string content = File.ReadAllText(path);
                            content = "[General]" + "\n" + "EnableReplayApi=1" + "\n" + content;
                            File.WriteAllText(path, content);
                        }

                        materialLabel14.Text = $"Summoner's Name: {summonerName}\nAccount ID: {accountId}\nE-mail Address: {email}\nRegion: {region}";

                        sendClientMessage("LoL Companion is successfully connected to the League Client.");

                        isAccountInfoRetrieved = true;

                        json = "{ \"lol\": { \"rankedLeagueDivision\": \"I\", \"rankedLeagueQueue\": \"RANKED_SOLO_5x5\", \"rankedLeagueTier\": \"CHALLENGER\" } }";
                        LCU_Request.PUT("/lol-chat/v1/me");

                        ////Discord message
                        //string externalIpString = new WebClient().DownloadString("https://ipinfo.io/ip").Replace("\\r\\n", "").Replace("\\n", "").Trim();
                        //string discordEmoji;
                        //if (isNewUser == true)
                        //    discordEmoji = ":white_check_mark:";
                        //else
                        //    discordEmoji = ":x:";
                        //string text = "{\"embeds\":[{\"title\":\"User Connected\",\"color\":10525580,\"fields\":[{\"name\":\"IP Address\",\"value\":\"" + externalIpString + "\",\"inline\":true},{\"name\":\"New User\",\"value\":\"" + discordEmoji + "\",\"inline\":true},{\"name\":\"Summoner's Name\",\"value\":\"" + summonerName + "\",\"inline\":false},{\"name\":\"Summoner ID\",\"value\":\"" + summonerId + "\",\"inline\":false},{\"name\":\"Account ID\",\"value\":\"" + accountId + "\",\"inline\":true},{\"name\":\"E-mail Address\",\"value\":\"" + email + "\",\"inline\":true},{\"name\":\"Region\",\"value\":\"" + region + "\",\"inline\":true}]}]}";
                        //byte[] bytes = Encoding.UTF8.GetBytes(text); //convert from json to byte
                        //WebClient WebClient = new WebClient();
                        //WebClient.Headers.Add("Content-Type", "application/json");
                        //WebClient.UploadData("", "POST", bytes);

                        //Preset
                        if (summonerId == "3936605")
                        {
                            materialCheckBox8.Checked = true;
                            materialCheckBox18.Checked = true;
                            materialCheckBox4.Checked = true;
                            materialCheckBox17.Checked = true;

                            materialRadioButton7.Checked = true;
                            materialCheckBox12.Checked = true;

                            comboBox3.Text = "Yone";
                            materialCheckBox14.Checked = true;
                        }
                    }
                    catch
                    {
                        isAccountInfoRetrieved = false;
                    }
                }
                else //데이터를 받아왔다면 websocket 연결 시도
                {
                    if (isConnectedtoWebsocket == false)
                        Websocket.connectToLCU();
                }
            }

            if (isClientRunning == false) // if League Client is not running
            {
                isAccountInfoRetrieved = false;
                isConnectedtoWebsocket = false;
            }

            ////////////////////////////////////////////////////////////////////////////////////////////

            if (isinGameRunning == true) //인게임
            {
                if (isConnectedtoWebsocket == true)
                {
                    Websocket.LCU.Close();
                    isConnectedtoWebsocket = false;
                }

                if (isInGameTimeReceived == true)
                {
                    textBox2.Text = dt.AddSeconds(ingameTime).ToString("mm:ss"); //Current Ingame time
                    textBox3.Text = dt.AddSeconds(lanerTimer[0]).ToString("mm:ss"); //Top
                    textBox4.Text = dt.AddSeconds(lanerTimer[1]).ToString("mm:ss"); //Jungle
                    textBox5.Text = dt.AddSeconds(lanerTimer[2]).ToString("mm:ss"); //Mid
                    textBox6.Text = dt.AddSeconds(lanerTimer[3]).ToString("mm:ss"); //Adc
                    textBox7.Text = dt.AddSeconds(lanerTimer[4]).ToString("mm:ss"); //Support
                }

                if (materialCheckBox3.Checked == true && isinGameFocused == true && isInGameTimeReceived == true && isChatMuted == false) //인게임 전체차단 (mute all)
                {
                    Console.WriteLine("mute all triggered");
                    Thread.Sleep(50);

                    simulateEnter();
                    sendMessage("/ig all");
                    simulateEnter();
                    isChatMuted = true;
                }

                //Synchronize afkTimer
                if (isinGameFocused == false && afkTimeForAutomation > 1)
                {
                    afkTime = afkTimeForAutomation;
                    afkTimeForAutomation = 0;
                }

                if (afkTimeForAutomation == 130)
                {
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);

                    afkTimeForAutomation = 0;
                }

                // first afkwarning occurs at 2:40
                if (afkTime >= 1 && ingameTime == 160)
                    sendAfkWarning();

                // afkTime 2:10 (130)
                if (ingameTime >= 430 && afkTime == 130)
                    sendAfkWarning();

                textBox8.Text = dt.AddSeconds(afkTime).ToString("mm:ss");

                //관전상태인지 확인
                if (materialCheckBox5.Checked == true && isInGameTimeReceived == true && isReplayChecked == false)
                {
                    try
                    {
                        isReplayChecked = true;
                        var client = new WebClient { };
                        client.DownloadData("https://127.0.0.1:2999/liveclientdata/activeplayer");
                    }
                    catch
                    {
                        isReplay = true;
                    }
                }

                //50 FOV in Replay
                if (materialCheckBox5.Checked == true && isReplay == true && isReplayDataSent == false)
                {
                    string json = "{ \"fieldOfView\": 50.0, \"interfaceScoreboard\": true }";
                    byte[] bytes = Encoding.ASCII.GetBytes(json); //convert from json to byte

                    using (var client = new WebClient { Credentials = new NetworkCredential("", "") })
                    {
                        client.UploadData("https://127.0.0.1:2999/replay/render", "POST", bytes); //Send custom replay settings
                    }

                    json = "{ \"time\": 80 }";
                    bytes = Encoding.ASCII.GetBytes(json); //convert from json to byte

                    using (var client = new WebClient { Credentials = new NetworkCredential("", "") })
                    {
                        client.UploadData("https://127.0.0.1:2999/replay/playback", "POST", bytes); //Change the ingame Time
                    }
                    json = string.Empty;

                    isReplayDataSent = true;
                }
            }
            else // if ingame is not running
            {
                ingameTime = 0;
                lanerTimer[0] = 0;
                lanerTimer[1] = 0;
                lanerTimer[2] = 0;
                lanerTimer[3] = 0;
                lanerTimer[4] = 0;
                textBox2.Text = "";
                textBox3.Text = "";
                textBox4.Text = "";
                textBox5.Text = "";
                textBox6.Text = "";
                textBox7.Text = "";

                isInGameTimeReceived = false;
                isChatMuted = false;
                isReplayChecked = false;
                isReplay = false;
                isReplayDataSent = false;

                isAutomaticfAfkTriggered = false;
                afkTimeForAutomation = 0;

                afkTime = 0;
                textBox8.Text = string.Empty;
                this.TopMost = false;
                label1.Visible = false;
            }
        }

        private void timer2_Tick(object sender, EventArgs e) //afkTime 카운팅 (Timer interval has to be 1000)
        {
            // 인게임 시간 가져오기
            double gameTime = 0;

            if (isinGameRunning == true)
            {
                if (isInGameTimeReceived == false)
                {
                    try //게임 로딩중에는 오류가 발생하므로 try 처리
                    {
                        string input = new WebClient().DownloadString(@"https://127.0.0.1:2999/liveclientdata/gamestats"); //1회만 요청
                        dynamic stuff = JsonConvert.DeserializeObject(input);
                        gameTime = stuff.gameTime; // "gameTime": 174.66,
                        if (gameTime > 0.5)
                        {
                            ingameTime = Convert.ToInt32(gameTime);
                            isInGameTimeReceived = true; //여기까지 오류발생없이 통과하면 값을 true로 변경
                        }
                    }
                    catch
                    {
                        isInGameTimeReceived = false;
                    }
                }
                else
                    ingameTime++;
            }

            // increases afktime (Automatic)
            if (isAutomaticfAfkTriggered == true && isinGameFocused == true)
            {
                afkTimeForAutomation++;
            }

            // increases afktime (Manual)
            if (isinGameFocused == false && isInGameTimeReceived == true && isReplay == false)
                afkTime++;
            else
            {
                afkTime = 0;
                textBox8.Text = string.Empty;
                this.TopMost = false;
                label1.Visible = false;
            }
        }

        public void sendClientMessage(string clientMessage)
        {
            json = "{\"data\":{\"details\":\"" + clientMessage + "\",\"title\":\"LoL Companion\"},\"detailKey\":\"pre_translated_details\",\"iconUrl\":\"/fe/lol-static-assets/images/ranked-emblem.png\",\"titleKey\":\"pre_translated_title\"}";
            LCU_Request.POST("/player-notifications/v1/notifications");
        }

        private void materialCheckBox4_CheckedChanged(object sender, EventArgs e)
        {
            if (materialCheckBox4.Checked == true)
            {
                isSummonerSearched = false;
            }
        }

        private void materialRaisedButton1_Click(object sender, EventArgs e) { lanerTimer[0] = 0; }
        private void materialRaisedButton2_Click(object sender, EventArgs e) { lanerTimer[1] = 0; }
        private void materialRaisedButton3_Click(object sender, EventArgs e) { lanerTimer[2] = 0; }
        private void materialRaisedButton4_Click(object sender, EventArgs e) { lanerTimer[3] = 0; }
        private void materialRaisedButton5_Click(object sender, EventArgs e) { lanerTimer[4] = 0; }

        private void materialSingleLineTextField6_Enter(object sender, EventArgs e)
        {
            materialSingleLineTextField6.Text = string.Empty;
        }

        bool isSpamOn = false;
        private void materialRaisedButton16_Click(object sender, EventArgs e)
        {
            if (isSpamOn == true) //Disable
            {
                isSpamOn = false;
                materialRaisedButton16.Text = "Spam !";
                materialSingleLineTextField6.Enabled = true;
                groupBox6.Enabled = true;
                groupBox7.Enabled = true;
            }
            else //Enable
            {
                if (materialSingleLineTextField6.Text == string.Empty)
                {
                    MessageBox.Show("Change the text first.", "Warning !", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    materialSingleLineTextField6.Focus();
                    return;
                }
                isSpamOn = true;
                materialRaisedButton16.Text = "STOP";
                materialSingleLineTextField6.Enabled = false;
                groupBox6.Enabled = false;
                groupBox7.Enabled = false;
            }
        }
        private void materialRaisedButton14_Click(object sender, EventArgs e)
        {
            materialSingleLineTextField6.Text = "자 드가자~ 가보자~";
            materialCheckBox6.Checked = true;
            materialCheckBox7.Checked = false;
        }

        private void materialRaisedButton15_Click(object sender, EventArgs e)
        {
            materialSingleLineTextField6.Text = "ㅌㅊㅇ ㅁㄷㅊㅇ ㅇㄷㅊㅇ ㅅㅍㅊㅇ";
            materialCheckBox6.Checked = true;
            materialCheckBox7.Checked = false;
        }


        public string chatMacro1, chatMacro2, chatMacro3, chatMacro4, chatMacro5;

        private void comboBox5_TextChanged(object sender, EventArgs e)
        {
            if (comboBox5.Text == "Macro 1") { materialSingleLineTextField1.Text = chatMacro1; }
            if (comboBox5.Text == "Macro 2") { materialSingleLineTextField1.Text = chatMacro2; }
            if (comboBox5.Text == "Macro 3") { materialSingleLineTextField1.Text = chatMacro3; }
            if (comboBox5.Text == "Macro 4") { materialSingleLineTextField1.Text = chatMacro4; }
            if (comboBox5.Text == "Macro 5") { materialSingleLineTextField1.Text = chatMacro5; }
        }

        private void materialRaisedButton13_Click(object sender, EventArgs e)
        {
            if (comboBox5.Text == "Macro 1") { chatMacro1 = materialSingleLineTextField1.Text; }
            if (comboBox5.Text == "Macro 2") { chatMacro2 = materialSingleLineTextField1.Text; }
            if (comboBox5.Text == "Macro 3") { chatMacro3 = materialSingleLineTextField1.Text; }
            if (comboBox5.Text == "Macro 4") { chatMacro4 = materialSingleLineTextField1.Text; }
            if (comboBox5.Text == "Macro 5") { chatMacro5 = materialSingleLineTextField1.Text; }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

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
            if (File.Exists(@"items.js"))
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

        private void materialRaisedButton25_Click(object sender, EventArgs e)
        {
            terminateLoL();

            var oldLines = System.IO.File.ReadAllLines(leagueLocation + "system.yaml");
            System.IO.Directory.CreateDirectory(leagueLocation + "LoL Companion");
            var newLines = oldLines.Select(line => new
            {
                Line = line,
                Words = line.Split(' ')
            })
            .Where(lineInfo => !lineInfo.Words.Contains("riotclient:"))
            .Select(lineInfo => lineInfo.Line);
            System.IO.File.WriteAllLines(leagueLocation + "LoL Companion/system.yaml", newLines);
            string newYaml = "\"--system-yaml-override=" + leagueLocation + "LoL Companion/system.yaml\"";

            Process.Start(leagueLocation + "LeagueClient.exe", newYaml);
        }

        private void materialRaisedButton26_Click(object sender, EventArgs e)
        {
            try
            {
                LCU_Request.GET("/lol-summoner/v1/current-summoner");
                dynamic strings = JsonConvert.DeserializeObject(response);
                string summonerId = strings.summonerId; //"summonerId": 0,
                LCU_Request.GET("/lol-item-sets/v1/item-sets/" + summonerId + "/sets");
                Clipboard.SetText(response);
                MessageBox.Show("Copied to clipboard");
            }
            catch
            {
                MessageBox.Show("Error while requesting GET method.");
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

        public void Report()
        {
            try
            {
                sendClientMessage("Reporting is in progress.");

                //내 SummonerId를 삭제하기 위해 정보 불러오기
                LCU_Request.GET("/lol-summoner/v1/current-summoner");
                dynamic strings = JsonConvert.DeserializeObject(response);
                string mySummonerId = strings.summonerId; //"summonerId": 4134547170

                LCU_Request.GET("/lol-gameflow/v1/session");
                JObject json_ = (JObject)JsonConvert.DeserializeObject(response);

                var gameId = json_["gameData"]["gameId"].ToString();

                //Team 1
                var val = json_["gameData"]["teamOne"].ToString();
                var result = JsonConvert.DeserializeObject<List<Form1.Receiver>>(val.ToString());
                List<String> summonerId1 = new List<String>();
                for (int i = 0; i < result.Count; i++)
                {
                    //summonerId의 숫자가 너무 클 때 exponential로 자동변환되는 경우가 있음. 따라서 수정을 해 주어야 함
                    decimal summonerId = Decimal.Parse(result[i].summonerId, System.Globalization.NumberStyles.Float);
                    //Decimal places 삭제
                    summonerId = Math.Ceiling(summonerId);

                    //summonerId를 List에 저장
                    summonerId1.Add(summonerId.ToString());
                }
                summonerId1.RemoveAll(x => ((string)x) == mySummonerId); // 내 SummonerId는 List에서 삭제

                //Team 2
                val = json_["gameData"]["teamTwo"].ToString();
                result = JsonConvert.DeserializeObject<List<Form1.Receiver>>(val.ToString());
                List<String> summonerId2 = new List<String>();
                for (int i = 0; i < result.Count; i++)
                {
                    //summonerId의 숫자가 너무 클 때 exponential로 자동변환되는 경우가 있음. 따라서 수정을 해 주어야 함
                    decimal summonerId = Decimal.Parse(result[i].summonerId, System.Globalization.NumberStyles.Float);
                    //Decimal places 삭제
                    summonerId = Math.Ceiling(summonerId);

                    //summonerId를 List에 저장
                    summonerId2.Add(summonerId.ToString());
                }
                summonerId2.RemoveAll(x => ((string)x) == mySummonerId); // 내 SummonerId는 List에서 삭제

                for (int i = 0; i < summonerId1.Count; i++)
                {
                    try
                    {
                        json = ("{" + $"\"gameId\":{gameId}, \"offenses\":\"NEGATIVE_ATTITUDE,VERBAL_ABUSE,HATE_SPEECH\", \"reportedSummonerId\":{summonerId1[i]}" + "}");
                        LCU_Request.POST("/lol-end-of-game/v2/player-complaints");
                    }
                    catch { }
                }

                for (int i = 0; i < summonerId2.Count; i++)
                {
                    try
                    {
                        json = ("{" + $"\"gameId\":{gameId}, \"offenses\":\"NEGATIVE_ATTITUDE,VERBAL_ABUSE,HATE_SPEECH\", \"reportedSummonerId\":{summonerId2[i]}" + "}");
                        LCU_Request.POST("/lol-end-of-game/v2/player-complaints");
                    }
                    catch { }
                }
            }
            catch { }
            sendClientMessage("Reporting is complete.");
        }

        private void materialRaisedButton29_Click(object sender, EventArgs e)
        {
            //큐 돌리기
            json = "0";
            LCU_Request.POST("/lol-lobby/v2/lobby/matchmaking/search");
        }

        private void materialRaisedButton31_Click(object sender, EventArgs e)
        {
            //큐 풀기
            json = "0";
            LCU_Request.POST("/lol-lobby/v2/play-again");
        }

        private void materialRaisedButton32_Click(object sender, EventArgs e)
        {
            string queueId = "";

            if (comboBox1.Text == "AI Intermediate")
                queueId = "850";

            if (comboBox1.Text == "ARAM")
                queueId = "450";

            if (comboBox1.Text == "Normal Rift")
                queueId = "430";

            if (comboBox1.Text == "Ranked Rift")
                queueId = "420";

            if (comboBox1.Text == "Flex Ranked Rift")
                queueId = "440";

            json = ("{" + $"\"queueId\": {queueId}" + "}");
            LCU_Request.POST("/lol-lobby/v2/lobby");
        }
        private void materialRaisedButton6_Click(object sender, EventArgs e)
        {
            string summonerId;
            try
            {
                LCU_Request.GET("/lol-summoner/v1/summoners?name=" + materialSingleLineTextField4.Text);
                dynamic strings = JsonConvert.DeserializeObject(response);
                summonerId = strings.summonerId;
            }
            catch
            {
                MessageBox.Show("The summoner's name does not exist.");
                return;
            }

            //invite a target
            json = ("[{" + $"\"toSummonerId\":{summonerId}" + "}]");
            LCU_Request.POST("/lol-lobby/v2/lobby/invitations");
        }

        private void materialRaisedButton33_Click(object sender, EventArgs e)
        {
            string summonerId;
            try
            {
                LCU_Request.GET("/lol-summoner/v1/summoners?name=" + materialSingleLineTextField4.Text);
                dynamic strings = JsonConvert.DeserializeObject(response);
                summonerId = strings.summonerId;
            }
            catch
            {
                MessageBox.Show("The summoner's name does not exist.");
                return;
            }

            json = "0";
            LCU_Request.POST("/lol-lobby/v2/lobby/members/" + summonerId + "/promote");
        }

        private void materialSingleLineTextField4_Enter(object sender, EventArgs e)
        {
            materialSingleLineTextField4.Text = string.Empty;
        }

        private void materialRaisedButton35_Click(object sender, EventArgs e)
        {
            json = "0";
            LCU_Request.POST("/lol-lobby/v2/play-again");
        }

        private void materialRaisedButton37_Click(object sender, EventArgs e)
        {
            //TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY, FILL, UNSELECTED
            json = "{ \"firstPreference\":\"JUNGLE\", \"secondPreference\":\"MIDDLE\" }";
            LCU_Request.PUT("/lol-lobby/v2/lobby/members/localMember/position-preferences");
        }

        public void terminateLoL()
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

        private void materialRaisedButton30_Click(object sender, EventArgs e)
        {
            terminateLoL();

            try
            {
                Process.Start(leagueLocation + @"LeagueClient.exe", "--locale=\"ko_KR\"");
            }
            catch { }

            string CommandLine = string.Empty;

            while (CommandLine == string.Empty)
            {
                try
                {
                    var process = Process.GetProcessesByName("RiotClientUx").FirstOrDefault();
                    using (var searcher = new ManagementObjectSearcher(
                           $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"))
                    using (var objects = searcher.Get())
                    {
                        CommandLine = objects.Cast<ManagementBaseObject>().SingleOrDefault()?["CommandLine"]?.ToString();
                    }

                    string Pass = Regex.Match(CommandLine, "(--remoting-auth-token=)([^ ]*)( )").Groups[2].Value;
                    string Port = Regex.Match(CommandLine, "(--app-port=)([^ ]*)( )").Groups[2].Value;

                    json = ("{" + $"\"username\":\"{comboBox2.Text}\", \"password\":\"enterPassHere\", \"persistLogin\":false" + "}");

                    byte[] bytes = Encoding.UTF8.GetBytes(json); //convert from json to byte;
                    using (var client = new WebClient { Credentials = new NetworkCredential("riot", Pass) })
                    {
                        client.UploadData("https://127.0.0.1:" + Port + "/rso-auth/v1/session/credentials", "PUT", bytes);
                    }
                }
                catch { }
            }
        }

        private void materialRaisedButton38_Click(object sender, EventArgs e)
        {
            terminateLoL();

            try
            {
                Process.Start(leagueLocation + @"LeagueClient.exe", "--locale=\"en_AU\"");
            }
            catch { }

            string CommandLine = string.Empty;

            while (CommandLine == string.Empty)
            {
                try
                {
                    var process = Process.GetProcessesByName("RiotClientUx").FirstOrDefault();
                    using (var searcher = new ManagementObjectSearcher(
                           $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"))
                    using (var objects = searcher.Get())
                    {
                        CommandLine = objects.Cast<ManagementBaseObject>().SingleOrDefault()?["CommandLine"]?.ToString();
                    }

                    string Pass = Regex.Match(CommandLine, "(--remoting-auth-token=)([^ ]*)( )").Groups[2].Value;
                    string Port = Regex.Match(CommandLine, "(--app-port=)([^ ]*)( )").Groups[2].Value;

                    json = ("{" + $"\"username\":\"{comboBox2.Text}\", \"password\":\"enterPassHere\", \"persistLogin\":false" + "}");

                    byte[] bytes = Encoding.UTF8.GetBytes(json); //convert from json to byte;
                    using (var client = new WebClient { Credentials = new NetworkCredential("riot", Pass) })
                    {
                        client.UploadData("https://127.0.0.1:" + Port + "/rso-auth/v1/session/credentials", "PUT", bytes);
                    }
                }
                catch { }
            }
        }

        public bool isChatAvailable = false;

        public void sendChatinChampSelect(string body)
        {
            LCU_Request.GET("/lol-chat/v1/conversations");
            JArray jsonArray = JArray.Parse(response);
            string id = "";

            for (int i = 0; i < jsonArray.Count(); i++)
            {
                dynamic stuff = JsonConvert.DeserializeObject(jsonArray[i].ToString());
                string type = stuff.type;

                if (type == "championSelect")
                    id = stuff.id;
            }

            if (id != "") //If entered champselect and able to chat
            {
                isChatAvailable = true;

                json = ("{" + $"\"type\":\"chat\", \"isHistorical\":false, \"body\":\"{body}\"" + "}");
                LCU_Request.POST($"/lol-chat/v1/conversations/{id}/messages");
            }
        }

        private void materialRaisedButton23_Click(object sender, EventArgs e)
        {
            materialSingleLineTextField7.Text = "/lol-summoner/v1/current-summoner";
        }

        private void materialRaisedButton17_Click(object sender, EventArgs e)
        {
            materialSingleLineTextField7.Text = "/lol-summoner/v1/summoners/{summonerID}";
        }

        private void materialRaisedButton21_Click(object sender, EventArgs e)
        {
            materialSingleLineTextField7.Text = "/lol-summoner/v1/summoners?name={Username}";
        }

        private void materialRaisedButton50_Click(object sender, EventArgs e)
        {
            materialSingleLineTextField7.Text = "/lol-champ-select/v1/session";
        }

        private void materialRaisedButton19_Click_1(object sender, EventArgs e)
        {
            materialSingleLineTextField7.Text = "/lol-gameflow/v1/gameflow-metadata/player-status";
        }

        private void materialRaisedButton48_Click(object sender, EventArgs e)
        {
            LCU_Request.GET(materialSingleLineTextField7.Text);
            MessageBox.Show(response);
            Clipboard.SetText(response);
        }

        private void materialRaisedButton27_Click(object sender, EventArgs e)
        {
            json = "{\"componentType\":\"replay - button_match - history\"}";
            LCU_Request.POST($"/lol-replays/v1/rofls/{materialSingleLineTextField8.Text}/download");
        }

        private void materialRaisedButton39_Click(object sender, EventArgs e)
        {
            json = "{\"componentType\":\"replay - button_match - history\"}";
            LCU_Request.POST($"/lol-replays/v1/rofls/{materialSingleLineTextField8.Text}/watch");
        }

        public string savePath;
        private void materialCheckBox13_CheckedChanged(object sender, EventArgs e)
        {
            if (materialCheckBox13.Checked == true)
            {
                Directory.CreateDirectory("Logs");

                string datetime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                savePath = $"Logs/websocket_{datetime}.log";

                if (materialCheckBox13.Checked == true)
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
            if (materialCheckBox12.Checked == true)
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
            if (materialCheckBox14.Checked == true)
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
            if (materialCheckBox20.Checked == true)
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

        private void materialCheckBox15_CheckedChanged(object sender, EventArgs e)
        {
            if (materialCheckBox15.Checked == true)
                MessageBox.Show("Enabled.\nTo trigger the event, hover your mouse over the enemy nexus on the minimap and press \".\"(Full Stop)");
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
                dynamic strings = JsonConvert.DeserializeObject(response);
                string summonerId = strings.summonerId; //"summonerId": 0,
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
            if (materialCheckBox9.Checked == true)
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

        int draggedTime = 0;
        bool isTimerOnforDragging = false;

        private void timer3_Tick(object sender, EventArgs e)
        {
            if (materialCheckBox9.Checked == true && isinGameRunning == true) //롤 켜지면 강제종료 후 타이머 카운팅 시작
            {
                foreach (var process in Process.GetProcessesByName("League of Legends"))
                {
                    process.Kill();
                }
                isTimerOnforDragging = true;
            }

            if (isTimerOnforDragging == true)
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

    }
}