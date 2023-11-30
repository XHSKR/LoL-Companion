using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text;
using System.Threading;
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
                        // 닷지될경우 champ-select 소켓 연결종료
                        foreach (string eventName in eventNames)
                        {
                            string wsMessage = $"[6, \"{eventName}\"]";
                            LCU.Send(wsMessage);
                        }
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

                        //로비로 돌아올 경우 List와 Variable 초기화
                        Console.WriteLine("List Cleared");
                        OPGG.Clear();
                        calledoutSummonerId.Clear();
                        isChatAvailable = false;
                        chat_in_finalization = false;
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
    }
}