using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using WebSocketSharp;
using System.Security.Authentication;
using System.Web;
using HtmlAgilityPack;

namespace LoL_Companion
{
    class Websocket
    {
        // Instantiation
        LCU_Request LCU_Request = new LCU_Request();

        [DllImport("user32.dll")] private static extern IntPtr FindWindow(string className, string windowTitle);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, ShowWindowEnum flags);
        private enum ShowWindowEnum
        {
            Hide = 0,
            ShowNormal = 1, ShowMinimized = 2, ShowMaximized = 3,
            Maximize = 3, ShowNormalNoActivate = 4, Show = 5,
            Minimize = 6, ShowMinNoActivate = 7, ShowNoActivate = 8,
            Restore = 9, ShowDefault = 10, ForceMinimized = 11
        };

        public void scrapOPGG()
        {
            //summonerID를 가져옴
            LCU_Request.GET("/lol-champ-select/v1/session");

            var obj = JObject.Parse(Form1.Object.response);
            var myTeam = obj["myTeam"];
            var result = JsonConvert.DeserializeObject<List<Form1.Receiver>>(myTeam.ToString());

            List<String> summonerId = new List<String>();
            for (int i = 0; i < result.Count; i++)
            {
                //summonerId를 List에 저장
                summonerId.Add(result[i].summonerId);
            }

            for (int i = 0; i < result.Count; i++)
            {
                //summonerName을 가져옴
                LCU_Request.GET("/lol-summoner/v1/summoners/" + result[i].summonerId);
                dynamic strings = JsonConvert.DeserializeObject(Form1.Object.response);
                string summonerName = strings.displayName;

                var web = new HtmlWeb();
                var doc = web.Load($"https://www.op.gg/summoner/champions/userName={summonerName}");

                List<string> list = new List<string>();
                var summonerStats_1 = doc.DocumentNode.SelectNodes("//*[@class = 'Row TopRanker']"); //맨 위에서부터 7개 까지만 보여줌
                var summonerStats_2 = doc.DocumentNode.SelectNodes("//*[@class = 'Row ']"); //7개 그 이후부터 보여줌

                decimal Win, Loss;

                if (summonerStats_1 != null)
                {
                    foreach (var summonerStats in summonerStats_1) //맨 위에서부터 7개
                    {
                        //챔피언
                        var Champion = (HttpUtility.HtmlDecode(summonerStats.SelectSingleNode(".//*[@class = 'ChampionName Cell']").InnerText)).Trim();

                        //승리
                        try
                        {
                            var Win_str = HttpUtility.HtmlDecode(summonerStats.SelectSingleNode(".//*[@class = 'Text Left']").InnerText).Trim();
                            Win = decimal.Parse(Win_str.Substring(0, Win_str.Length - 1));
                        }
                        catch { Win = 0; }

                        //패배
                        try
                        {
                            var Loss_str = (HttpUtility.HtmlDecode(summonerStats.SelectSingleNode(".//*[@class = 'Text Right']").InnerText)).Trim();
                            Loss = decimal.Parse(Loss_str.Substring(0, Loss_str.Length - 1));
                        }
                        catch { Loss = 0; }

                        //총 판수
                        var totalGame = Win + Loss;

                        //승률
                        var winRate = Math.Round((Win / totalGame) * 100, 0);

                        string[] values = { summonerId[i], Champion, totalGame.ToString(), Win.ToString(), Loss.ToString(), winRate.ToString() };
                        OPGG.Add(values);
                    }
                }

                if (summonerStats_2 != null)
                {
                    foreach (var summonerStats in summonerStats_2) //7개 이후부터
                    {
                        //챔피언
                        var Champion = (HttpUtility.HtmlDecode(summonerStats.SelectSingleNode(".//*[@class = 'ChampionName Cell']").InnerText)).Trim();

                        //승리
                        try
                        {
                            var Win_str = HttpUtility.HtmlDecode(summonerStats.SelectSingleNode(".//*[@class = 'Text Left']").InnerText).Trim();
                            Win = decimal.Parse(Win_str.Substring(0, Win_str.Length - 1));
                        }
                        catch { Win = 0; }

                        //패배
                        try
                        {
                            var Loss_str = (HttpUtility.HtmlDecode(summonerStats.SelectSingleNode(".//*[@class = 'Text Right']").InnerText)).Trim();
                            Loss = decimal.Parse(Loss_str.Substring(0, Loss_str.Length - 1));
                        }
                        catch { Loss = 0; }

                        //총 판수
                        var totalGame = Win + Loss;

                        //승률
                        var winRate = Math.Round((Win / totalGame) * 100, 0);

                        string[] values = { summonerId[i], Champion, totalGame.ToString(), Win.ToString(), Loss.ToString(), winRate.ToString() };
                        OPGG.Add(values);
                    }
                }
            }
            //Form1.Object.sendChatinChampSelect("Initialization Successful - Powered by LoL Companion");
        }

        public void multiSearch()
        {
            //summonerID를 가져옴
            LCU_Request.GET("/lol-champ-select/v1/session");
            var obj = JObject.Parse(Form1.Object.response);
            var input = obj["myTeam"];
            var result = JsonConvert.DeserializeObject<List<Form1.Receiver>>(input.ToString());
            List<String> summonerNames = new List<String>();
            //summonerID를 실제 이름으로 변환 (summonerId를 가져오는 과정은 사실 for 문 첫번째에 있음)
            for (int i = 0; i < result.Count; i++)
            {
                LCU_Request.GET("/lol-summoner/v1/summoners/" + result[i].summonerId);
                dynamic strings = JsonConvert.DeserializeObject(Form1.Object.response);
                string summonerName = strings.displayName;
                summonerNames.Add(summonerName);
            }
            string summonerNamesCombined = "";
            //아이디가 모였으므로 합침
            for (int i = 0; i < summonerNames.Count; i++)
            {
                summonerNamesCombined = summonerNamesCombined + summonerNames[i] + ", ";
            }
            summonerNamesCombined = summonerNamesCombined.Substring(0, summonerNamesCombined.Length - 2);

            //서버확인
            LCU_Request.GET("/riotclient/region-locale");
            dynamic strings2 = JsonConvert.DeserializeObject(Form1.Object.response);
            string region = strings2.webRegion; //"webRegion": "oce"

            //주소접속
            string porofessorURL = "https://porofessor.gg/pregame/" + region + "/" + summonerNamesCombined + "/ranked-only";
            Process.Start(porofessorURL);
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public WebSocket LCU;

        public List<string[]> OPGG = new List<string[]>();
        string savedChampName = "";
        public List<String> calledoutSummonerId = new List<String>();

        public void minimiseClient_conditional()
        {
            if (Form1.Object.isClientFocused == false)
            {
                Thread.Sleep(50);
                //Minimize League window
                IntPtr wdwIntPtr = FindWindow(null, "League of Legends");
                ShowWindow(wdwIntPtr, ShowWindowEnum.Minimize);
            }
        }

        bool chat_in_finalization = false;

        public void connectToLCU()
        {
            LCU_Request.getriotCredentials(null);

            LCU = new WebSocket($"wss://127.0.0.1:{Form1.Object.riotPort}/", "wamp");
            LCU.SetCredentials("riot", Form1.Object.riotPass, true);
            LCU.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
            LCU.SslConfiguration.ServerCertificateValidationCallback = (send, certificate, chain, sslPolicyErrors) => true;
            LCU.OnMessage += (s, e) =>
            {
                if (!e.IsText)
                    return;

                var eventArray = JArray.Parse(e.Data);
                var eventNumber = eventArray[0].ToObject<int>();
                if (eventNumber != 8)
                    return;

                var leagueEvent = eventArray[2];
                JObject json = JObject.Parse(leagueEvent.ToString());
                var uri = json["uri"].ToString();
                var data = json["data"].ToString();

                if (uri.Contains("/lol-champ-select/v1/session"))
                {
                    // Get phase
                    LCU_Request.GET("/lol-champ-select/v1/session");
                    JObject json_ = JObject.Parse(Form1.Object.response);
                    string phase = json_["timer"]["phase"].ToString();

                    if (phase == "FINALIZATION" && chat_in_finalization == false && Form1.Object.materialCheckBox21.Checked == true)
                    {
                        Form1.Object.sendChatinChampSelect(Form1.Object.materialSingleLineTextField2.Text);
                        chat_in_finalization = true;
                    }
                }

                //Lobby Behaviour (Ban, Pick for Ranked)
                if (uri.Contains("/lol-champ-select/v1/summoners/"))
                {
                    //Find localPlayerCellId
                    LCU_Request.GET("/lol-champ-select/v1/session");
                    dynamic strings = JObject.Parse(Form1.Object.response);
                    string localPlayerCellId = strings.localPlayerCellId;

                    var activeActionType = json["data"]["activeActionType"].ToString();
                    var isSelf = json["data"]["isSelf"].ToString();
                    var championIconStyle = json["data"]["championIconStyle"].ToString();
                    var isActingNow = json["data"]["isActingNow"].ToString();
                    var isDonePicking = json["data"]["isDonePicking"].ToString();

                    if (isSelf == "True")
                    {
                        //Champion Ban
                        if (Form1.Object.materialCheckBox20.Checked == true && activeActionType == "ban")
                        {
                            if (Form1.Object.materialCheckBox19.Checked == true)
                                Form1.Object.sendChatinChampSelect(Form1.Object.materialSingleLineTextField1.Text);

                            //Ban Champion
                            Form1.Object.json = "{" + $"\"championId\":{Form1.Object.selectedBanChampionId}, \"completed\": true" + "}";
                            LCU_Request.PATCH($"/lol-champ-select/v1/session/actions/{localPlayerCellId}");

                            minimiseClient_conditional();
                        }


                        // Get QueueId
                        LCU_Request.GET("/lol-gameflow/v1/gameflow-metadata/player-status");
                        JObject json_ = JObject.Parse(Form1.Object.response);
                        string queueId = json_["currentLobbyStatus"]["queueId"].ToString();
                        //Champion Pick (Ranked)
                        if (Form1.Object.materialCheckBox14.Checked == true && queueId == "420" && queueId == "440")
                        {
                            if (activeActionType == "pick" && championIconStyle == "display:none" && isActingNow == "True" && isDonePicking == "False")
                            {
                                string id_ = "";
                                if (localPlayerCellId == "0") { id_ = "10"; }
                                if (localPlayerCellId == "5") { id_ = "11"; }
                                if (localPlayerCellId == "6") { id_ = "12"; }
                                if (localPlayerCellId == "1") { id_ = "13"; }
                                if (localPlayerCellId == "2") { id_ = "14"; }
                                if (localPlayerCellId == "7") { id_ = "15"; }
                                if (localPlayerCellId == "8") { id_ = "16"; }
                                if (localPlayerCellId == "3") { id_ = "17"; }
                                if (localPlayerCellId == "4") { id_ = "18"; }
                                if (localPlayerCellId == "9") { id_ = "19"; }

                                //Select Champion(lock in)
                                Form1.Object.json = "{" + $"\"championId\":{Form1.Object.selectedChampionId}, \"completed\": true" + "}";
                                LCU_Request.PATCH($"/lol-champ-select/v1/session/actions/{id_}");

                                minimiseClient_conditional();
                            }
                        }
                    }
                }

                if (uri.Contains("/lol-gameflow/v1/gameflow-phase"))
                {
                    if (data == "ReadyCheck") //큐가 잡히면 자동수락
                    {
                        //로비로 돌아와서 다시 큐가 잡힌 경우 List와 Variable 초기화
                        Console.WriteLine("List Cleared");
                        OPGG.Clear();
                        calledoutSummonerId.Clear();
                        Form1.Object.isChatAvailable = false;
                        chat_in_finalization = false;

                        if (Form1.Object.materialCheckBox8.Checked == true)
                        {
                            minimiseClient_conditional();
                            Form1.Object.json = "0";
                            LCU_Request.POST("/lol-matchmaking/v1/ready-check/accept");
                        }
                    }

                    if (data == "ChampSelect")
                    {
                        // Get QueueId
                        LCU_Request.GET("/lol-gameflow/v1/gameflow-metadata/player-status");
                        JObject json_ = JObject.Parse(Form1.Object.response);
                        string queueId = json_["currentLobbyStatus"]["queueId"].ToString();

                        if (queueId == "430") // Normal Game Only (430)
                        {
                            //Position Callout
                            if (Form1.Object.materialCheckBox12.Checked == true)
                            {
                                string position = "";

                                if (Form1.Object.materialRadioButton5.Checked == true)
                                    position = "상단(탑) | Top | 上单";
                                if (Form1.Object.materialRadioButton6.Checked == true)
                                    position = "정글 | Jungle | 打野";
                                if (Form1.Object.materialRadioButton7.Checked == true)
                                    position = "미드 | Mid | 中单";
                                if (Form1.Object.materialRadioButton8.Checked == true)
                                    position = "봇(원딜) | Bot | ADC";
                                if (Form1.Object.materialRadioButton9.Checked == true)
                                    position = "서포터 | Support | 辅助";

                                LCU_Request.GET("/lol-chat/v1/conversations");
                                JArray jsonArray = JArray.Parse(Form1.Object.response);

                                while (Form1.Object.isChatAvailable == false)
                                //while (i == 5) 하면 안되는 이유는 i가 5가 될 때 까지 반복하는 것이 아니라 i가 5일 때에만 반복하게 되는 것이다.
                                //따라서 while (i < 5)로 수정하여 i가 5보다 적을 동안만 반복하게 해야 한다.
                                //해당 코드에서는 isChatAvailable이 false일동안 계속 반복시켜 true로 변하면 멈춘다.
                                {
                                    for (int i = 0; i < 7; i++)
                                    {
                                        Form1.Object.sendChatinChampSelect(position);
                                    }
                                }
                                //Upon completing position callout, reset variable
                                Form1.Object.isChatAvailable = false;
                            }

                            //Champion Instant Lock (Normal)
                            if (Form1.Object.materialCheckBox14.Checked == true)
                            {
                                //Find my CellId (pick order)
                                LCU_Request.GET("/lol-champ-select/v1/session");
                                dynamic strings = JsonConvert.DeserializeObject(Form1.Object.response);
                                string localPlayerCellId = strings.localPlayerCellId;

                                //Select Champion (lock in)
                                if (Form1.Object.materialCheckBox16.Checked == true)
                                    Form1.Object.json = "{" + $"\"championId\":{Form1.Object.selectedChampionId}, \"completed\": true" + "}";
                                else //no lock in
                                    Form1.Object.json = "{" + $"\"championId\":{Form1.Object.selectedChampionId}, \"completed\": false" + "}";

                                LCU_Request.PATCH($"/lol-champ-select/v1/session/actions/{localPlayerCellId}");
                            }
                        }

                        if (queueId == "440") // Solo, Flex Ranked(420, 440) // solo is excluded due to anonymity
                        {
                            if (Form1.Object.materialCheckBox4.Checked == true)
                                multiSearch();

                            if (Form1.Object.materialCheckBox11.Checked == true)
                                scrapOPGG();
                        }
                    }

                    //Automatic Report
                    if (data == "EndOfGame" && Form1.Object.materialCheckBox17.Checked == true)
                    {
                        // Get QueueId
                        LCU_Request.GET("/lol-gameflow/v1/gameflow-metadata/player-status");
                        JObject json_ = JObject.Parse(Form1.Object.response);
                        string queueId = json_["currentLobbyStatus"]["queueId"].ToString();

                        if (queueId != "-1") //Exclude Practice Tool
                            Form1.Object.Report();
                    }
                }

                if (uri.Contains("/lol-champ-select/v1/summoners/"))
                {
                    // Get QueueId
                    LCU_Request.GET("/lol-gameflow/v1/gameflow-metadata/player-status");
                    JObject json_ = JObject.Parse(Form1.Object.response);
                    string queueId = json_["currentLobbyStatus"]["queueId"].ToString();

                    dynamic stuff = JsonConvert.DeserializeObject(data.ToString());
                    bool isSelf = false;
                    if (queueId == "450" && Form1.Object.materialCheckBox18.Checked == true) // ARAM
                    {
                        isSelf = stuff.isSelf;
                    }

                    if (Form1.Object.materialCheckBox11.Checked == true || isSelf == true)
                    {
                        //Champion name works differently upon Language Settings

                        bool isDonePicking = stuff.isDonePicking;

                        if (isDonePicking == true || isSelf == true) // 랭크용 || ARAM용
                        {
                            string championIconStyle = stuff.championIconStyle;
                            bool isOnPlayersTeam = stuff.isOnPlayersTeam;
                            string summonerId = stuff.summonerId;

                            int pFrom = championIconStyle.IndexOf("champion-icons/") + "champion-icons/".Length;
                            int pTo = championIconStyle.LastIndexOf(".png");
                            string championId = championIconStyle.Substring(pFrom, pTo - pFrom);
                            string championName = ""; //1회성 Thread이므로 List를 쓰지 않음
                            for (int i = 0; i < Form1.Object.Champion.Count(); i++)
                            {
                                if (Form1.Object.Champion[i][1] == championId)
                                    championName = Form1.Object.Champion[i][0];
                            }

                            //Console.WriteLine($"summonerId: {summonerId} | championName: {championName} | isDonePicking: {isDonePicking} | isOnPlayersTeam: {isOnPlayersTeam}");

                            if (queueId == "450") // ARAM
                            {
                                if (championName == savedChampName) //중복실행 방지
                                    return;
                                savedChampName = championName;
                                string str = Regex.Replace(championName, "[^A-Za-z]", "");
                                if (str == "NunuWillump")
                                    str = "Nunu";
                                Process.Start($"https://poro.gg/champions/{str}/aram");
                            }

                            if (queueId == "420" || queueId == "440") // Solo, Flex Ranked(420, 440)
                            {
                                bool skipSearch = false;
                                for (int i = 0; i < calledoutSummonerId.Count(); i++)
                                {
                                    if (calledoutSummonerId[i] == summonerId)
                                    {
                                        skipSearch = true;
                                    }
                                }

                                if (skipSearch == false && championName != "" && isDonePicking == true && isOnPlayersTeam == true)
                                {
                                    //summonerName을 가져옴
                                    LCU_Request.GET("/lol-summoner/v1/summoners/" + summonerId);
                                    dynamic strings = JsonConvert.DeserializeObject(Form1.Object.response);
                                    string summonerName = strings.displayName;

                                    for (int i = 0; i < OPGG.Count(); i++) //OPGG 열(column) 길이동안 반복
                                    {
                                        if (OPGG[i][1].Contains(championName))
                                        {
                                            if (OPGG[i][0] == summonerId)
                                            {
                                                Form1.Object.sendChatinChampSelect($"{summonerName} | {championName} | {OPGG[i][2]}판 | 승률 {OPGG[i][5]}%");
                                                calledoutSummonerId.Add(summonerId);
                                            }
                                        }
                                    }

                                    //플레이기록없음을 띄우기 위해서 불렸는지 체크를 한번 더 해야함.
                                    for (int i = 0; i < calledoutSummonerId.Count(); i++)
                                    {
                                        if (calledoutSummonerId[i] == summonerId)
                                        {
                                            skipSearch = true;
                                        }
                                    }

                                    if (skipSearch == false)
                                    {
                                        Form1.Object.sendChatinChampSelect($"{summonerName} | {championName} | 플레이 기록 없음");
                                        calledoutSummonerId.Add(summonerId);
                                    }
                                }
                            }
                        }
                    }
                }
            };
            LCU.Connect();
            LCU.Send("[5, \"OnJsonApiEvent\"]");

            Form1.Object.isConnectedtoWebsocket = true;
        }

        public WebSocket LCU_Debug;
        public void debug()
        {
            LCU_Request.getriotCredentials(null);

            LCU_Debug = new WebSocket($"wss://127.0.0.1:{Form1.Object.riotPort}/", "wamp");
            LCU_Debug.SetCredentials("riot", Form1.Object.riotPass, true);
            LCU_Debug.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
            LCU_Debug.SslConfiguration.ServerCertificateValidationCallback = (send, certificate, chain, sslPolicyErrors) => true;
            LCU_Debug.OnMessage += (s, e) =>
            {
                if (e.IsText)
                {
                    var eventArray = JArray.Parse(e.Data);
                    System.IO.File.AppendAllText(Form1.Object.savePath, eventArray + "\n\n\n", Encoding.GetEncoding("UTF-8"));
                }
            };
            LCU_Debug.Connect();
            LCU_Debug.Send("[5, \"OnJsonApiEvent\"]");
        }
    }
}