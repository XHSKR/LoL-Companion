using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using WebSocketSharp;

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

        public WebSocket LCU;

        List<string[]> OPGG = new List<string[]>();
        string savedChampName = "";
        List<String> calledoutSummonerId = new List<String>();

        private void minimiseClient_conditional()
        {
            if (!Form1.Object.isClientFocused)
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

                List<string> eventNames = new List<string>
                {
                    "OnJsonApiEvent_lol-champ-select_v1_session",
                    "OnJsonApiEvent_lol-champ-select_v1_summoners",
                };

                // Game phase Behaviour
                if (uri.Contains("/lol-gameflow/v1/gameflow-phase"))
                {
                    if (data == "None")
                    {
                        //로비로 돌아올 경우 List와 Variable 초기화
                        foreach (string eventName in eventNames)
                        {
                            string wsMessage = $"[6, \"{eventName}\"]";
                            LCU.Send(wsMessage);
                        }

                        Console.WriteLine("List Cleared");
                        OPGG.Clear();
                        calledoutSummonerId.Clear();
                        isChatAvailable = false;
                        chat_in_finalization = false;
                    }

                    // Get QueueId
                    LCU_Request.GET("/lol-gameflow/v1/gameflow-metadata/player-status");
                    JObject player_status = JObject.Parse(Form1.Object.response);
                    string queueId = player_status["currentLobbyStatus"]["queueId"].ToString();

                    if (data == "ReadyCheck") //큐가 잡히면 자동수락
                    {
                        if (Form1.Object.materialCheckBox8.Checked)
                        {
                            minimiseClient_conditional();
                            Form1.Object.json = "0";
                            LCU_Request.POST("/lol-matchmaking/v1/ready-check/accept");
                        }
                    }

                    if (data == "ChampSelect")
                    {
                        foreach (string eventName in eventNames)
                        {
                            string wsMessage = $"[5, \"{eventName}\"]";
                            LCU.Send(wsMessage);
                        }

                        if (queueId == "-1") // Normal Game Only (430)
                        {
                            //Champion Instant Lock (Normal)
                            if (Form1.Object.materialCheckBox14.Checked)
                                pickChampion("practice");
                        }

                        if (queueId == "430") // Normal Game Only (430)
                        {
                            //Champion Instant Lock (Normal)
                            if (Form1.Object.materialCheckBox14.Checked)
                                pickChampion("blind");

                            //Position Callout
                            if (Form1.Object.materialCheckBox12.Checked)
                            {
                                string position = "";

                                if (Form1.Object.materialRadioButton5.Checked)
                                    position = "상단(탑) | Top | 上单";
                                if (Form1.Object.materialRadioButton6.Checked)
                                    position = "정글 | Jungle | 打野";
                                if (Form1.Object.materialRadioButton7.Checked)
                                    position = "미드 | Mid | 中单";
                                if (Form1.Object.materialRadioButton8.Checked)
                                    position = "봇(원딜) | Bot | ADC";
                                if (Form1.Object.materialRadioButton9.Checked)
                                    position = "서포터 | Support | 辅助";

                                LCU_Request.GET("/lol-chat/v1/conversations");
                                JArray jsonArray = JArray.Parse(Form1.Object.response);

                                while (!isChatAvailable)
                                {
                                    for (int i = 0; i < 5; i++)
                                    {
                                        sendChatinChampSelect(position);
                                    }
                                }
                                //Upon completing position callout, reset variable
                                isChatAvailable = false;
                            }
                        }

                        // Champion Intent
                        if (Form1.Object.materialCheckBox14.Checked && (queueId == "400" || queueId == "420" || queueId == "440"))
                            pickChampion("draft");

                        if (queueId == "440") // Solo, Flex Ranked(420, 440) // solo is excluded due to anonymity patch
                        {
                            if (Form1.Object.materialCheckBox4.Checked)
                                multiSearch();

                            if (Form1.Object.materialCheckBox11.Checked)
                                scrapOPGG();
                        }
                    }

                    //Automatic Report // may not work.. look into it later
                    if (data == "EndOfGame" && Form1.Object.materialCheckBox17.Checked)
                    {
                        if (queueId != "-1") //Exclude Practice Tool
                            Report();
                    }
                }

                // Finalization Chat
                if (uri.Contains("/lol-champ-select/v1/session"))
                {
                    // Get phase
                    string phase = json["data"]["timer"]["phase"].ToString();

                    if (phase == "FINALIZATION" && !chat_in_finalization && Form1.Object.materialCheckBox21.Checked)
                    {
                        sendChatinChampSelect(Form1.Object.materialSingleLineTextField2.Text);
                        chat_in_finalization = true;
                    }
                }

                // Ban, Pick, OPGG for Ranked, and ARAM
                if (uri.Contains("/lol-champ-select/v1/summoners/"))
                {
                    // Get QueueId
                    LCU_Request.GET("/lol-gameflow/v1/gameflow-metadata/player-status");
                    JObject player_status = JObject.Parse(Form1.Object.response);
                    string queueId = player_status["currentLobbyStatus"]["queueId"].ToString();

                    //Find localPlayerCellId
                    LCU_Request.GET("/lol-champ-select/v1/session");
                    JObject session = JObject.Parse(Form1.Object.response);
                    string localPlayerCellId = session["localPlayerCellId"].ToString();

                    //Get variables to determine the correct champion pick time
                    string activeActionType = json["data"]["activeActionType"].ToString();
                    bool isSelf = (bool)json["data"]["isSelf"];
                    string championIconStyle = json["data"]["championIconStyle"].ToString();
                    string skinId = json["data"]["skinId"].ToString();
                    bool isActingNow = (bool)json["data"]["isActingNow"];
                    bool isDonePicking = (bool)json["data"]["isDonePicking"];
                    bool isOnPlayersTeam = (bool)json["data"]["isOnPlayersTeam"];
                    string summonerId = json["data"]["summonerId"].ToString();

                    if (isSelf)
                    {
                        //Champion Ban
                        if (Form1.Object.materialCheckBox20.Checked && activeActionType == "ban")
                        {
                            if (Form1.Object.materialCheckBox19.Checked)
                                sendChatinChampSelect(Form1.Object.materialSingleLineTextField1.Text);

                            //Ban Champion
                            Form1.Object.json = "{" + $"\"championId\":{Form1.Object.selectedBanChampionId}, \"completed\": true" + "}";
                            LCU_Request.PATCH($"/lol-champ-select/v1/session/actions/{localPlayerCellId}");

                            minimiseClient_conditional();
                        }

                        //Champion Pick (Draft)
                        if (Form1.Object.materialCheckBox14.Checked && (queueId == "400" || queueId == "420" || queueId == "440"))
                            if (activeActionType == "pick" && skinId == "0" && isActingNow && !isDonePicking)
                                pickChampion("draft");
                    }

                    if ((Form1.Object.materialCheckBox11.Checked || Form1.Object.materialCheckBox18.Checked) && isSelf)
                    {
                        // Get my championName
                        int pFrom = championIconStyle.IndexOf("champion-icons/") + "champion-icons/".Length;
                        int pTo = championIconStyle.LastIndexOf(".png");
                        if (pTo == -1)
                            return;
                        string championId = championIconStyle.Substring(pFrom, pTo - pFrom);
                        string championName = ""; //1회성 Thread이므로 List를 쓰지 않음
                        for (int i = 0; i < Form1.Object.Champion.Count(); i++)
                            if (Form1.Object.Champion[i][1] == championId)
                                championName = Form1.Object.Champion[i][0];


                        if (Form1.Object.materialCheckBox18.Checked && queueId == "450") // ARAM
                        {
                            if (championName == savedChampName) //중복실행 방지
                                return;
                            savedChampName = championName;
                            string str = Regex.Replace(championName, "[^A-Za-z]", "");
                            if (str == "NunuWillump")
                                str = "Nunu";
                            Process.Start($"https://poro.gg/champions/{str}/aram");
                        }

                        if (Form1.Object.materialCheckBox11.Checked && queueId == "440")
                        {
                            if (isDonePicking && !calledoutSummonerId.Contains(summonerId) && championName != "" && isOnPlayersTeam)
                            {
                                // Get summonerName
                                LCU_Request.GET("/lol-summoner/v1/summoners/" + summonerId);
                                JObject summoners = JObject.Parse(Form1.Object.response);
                                string summonerName = summoners["displayName"].ToString();

                                bool foundChampion = false;
                                for (int i = 0; i < OPGG.Count(); i++)
                                {
                                    if (OPGG[i][1].Contains(championName) && OPGG[i][0] == summonerId)
                                    {
                                        sendChatinChampSelect($"{summonerName} | {championName} | {OPGG[i][2]}판 | 승률 {OPGG[i][5]}%");
                                        calledoutSummonerId.Add(summonerId);
                                        foundChampion = true;
                                        break;
                                    }
                                }

                                if (!foundChampion)
                                {
                                    sendChatinChampSelect($"{summonerName} | {championName} | 플레이 기록 없음");
                                    calledoutSummonerId.Add(summonerId);
                                }
                            }
                        }
                    }
                }
            };
            LCU.Connect();
            LCU.Send($"[5, \"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase\"]");
            Form1.Object.isConnectedtoWebsocket = true;
        }

        public WebSocket LCU_Debug;

        private void pickChampion(string queueType)
        {
            //Find my CellId (pick order)
            LCU_Request.GET("/lol-champ-select/v1/session");
            JObject session = JObject.Parse(Form1.Object.response);
            string localPlayerCellId = session["localPlayerCellId"].ToString();

            string id_ = "";
            switch (localPlayerCellId)
            {
                case "0": id_ = "10"; break;
                case "5": id_ = "11"; break;
                case "6": id_ = "12"; break;
                case "1": id_ = "13"; break;
                case "2": id_ = "14"; break;
                case "7": id_ = "15"; break;
                case "8": id_ = "16"; break;
                case "3": id_ = "17"; break;
                case "4": id_ = "18"; break;
                case "9": id_ = "19"; break;
            }

            if (queueType == "draft")
            {
                Form1.Object.json = "{" + $"\"championId\":{Form1.Object.selectedChampionId}, \"completed\": true" + "}";
                LCU_Request.PATCH($"/lol-champ-select/v1/session/actions/{id_}");
            }
            else
            {
                if (Form1.Object.materialCheckBox16.Checked) // lock-in
                    Form1.Object.json = "{" + $"\"championId\":{Form1.Object.selectedChampionId}, \"completed\": true" + "}";
                else // no lock-in
                    Form1.Object.json = "{" + $"\"championId\":{Form1.Object.selectedChampionId}, \"completed\": false" + "}";

                if (queueType == "blind")
                    LCU_Request.PATCH($"/lol-champ-select/v1/session/actions/{localPlayerCellId}");

                if (queueType == "practice")
                    LCU_Request.PATCH($"/lol-champ-select/v1/session/actions/1");
            }
            minimiseClient_conditional();
        }

        bool isChatAvailable = false;
        private void sendChatinChampSelect(string body)
        {
            LCU_Request.GET("/lol-chat/v1/conversations");
            JArray jsonArray = JArray.Parse(Form1.Object.response);
            string id = "";

            foreach (JObject item in jsonArray)
            {
                string type = item["type"].ToString();

                if (type == "championSelect")
                {
                    id = item["id"].ToString();
                    break;
                }
            }

            if (!string.IsNullOrEmpty(id))
            {
                isChatAvailable = true;

                Form1.Object.json = $"{{\"type\":\"chat\", \"isHistorical\":false, \"body\":\"{body}\"}}";
                LCU_Request.POST($"/lol-chat/v1/conversations/{id}/messages");
            }
        }

        public void debug()
        {
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
            List<string> eventNames = new List<string>
            {
                "OnJsonApiEvent_lol-champ-select_v1_session",
                "OnJsonApiEvent_lol-champ-select_v1_summoners",
                "OnJsonApiEvent_lol-gameflow_v1_gameflow-phase",
            };
            foreach (string eventName in eventNames)
            {
                string wsMessage = $"[5, \"{eventName}\"]";
                LCU_Debug.Send(wsMessage);
            }
        }

        private void Report()
        {
            try
            {
                Form1.Object.sendClientMessage("Reporting is in progress.");

                // Get current summoner ID
                LCU_Request.GET("/lol-summoner/v1/current-summoner");
                JObject current_summoner = JObject.Parse(Form1.Object.response);
                string mySummonerId = current_summoner["summonerId"].ToString();

                // Get session and game ID
                LCU_Request.GET("/lol-gameflow/v1/session");
                JObject json_ = (JObject)JsonConvert.DeserializeObject(Form1.Object.response);
                var gameId = json_["gameData"]["gameId"].ToString();

                // Gather summoner IDs from both teams
                var teamOne = json_["gameData"]["teamOne"].ToString();
                var teamTwo = json_["gameData"]["teamTwo"].ToString();

                var summonerId1 = GetSummonerIds(teamOne, mySummonerId);
                var summonerId2 = GetSummonerIds(teamTwo, mySummonerId);

                // Report summoner IDs
                ReportSummonerIds(gameId, summonerId1);
                ReportSummonerIds(gameId, summonerId2);
            }
            catch { }

            Form1.Object.sendClientMessage("Reporting is complete.");
        }

        private List<string> GetSummonerIds(string teamData, string mySummonerId)
        {
            var result = JsonConvert.DeserializeObject<List<Form1.Receiver>>(teamData);
            var summonerIds = result.Select(r => r.summonerId.ToString()).ToList();
            summonerIds.RemoveAll(id => id == mySummonerId);
            return summonerIds;
        }

        private void ReportSummonerIds(string gameId, List<string> summonerIds)
        {
            foreach (string summonerId in summonerIds)
            {
                try
                {
                    Form1.Object.json = $"{{\"gameId\":{gameId}, \"offenses\":\"NEGATIVE_ATTITUDE,VERBAL_ABUSE,HATE_SPEECH\", \"reportedSummonerId\":{summonerId}}}";
                    LCU_Request.POST("/lol-end-of-game/v2/player-complaints");
                }
                catch { }
            }
        }

        private void scrapOPGG()
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
                JObject summoners = JObject.Parse(Form1.Object.response);
                string summonerName = summoners["displayName"].ToString();

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
        }

        private void multiSearch()
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
                JObject summoners = JObject.Parse(Form1.Object.response);
                string summonerName = summoners["displayName"].ToString();
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
            JObject region_locale = JObject.Parse(Form1.Object.response);
            string region = region_locale["webRegion"].ToString();

            //주소접속
            string porofessorURL = "https://porofessor.gg/pregame/" + region + "/" + summonerNamesCombined + "/ranked-only";
            Process.Start(porofessorURL);
        }

    }
}