using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

        private void minimiseClient_conditional()
        {
            if (!Form1.Object.isClientFocused)
            {
                Task.Delay(50);
                //Minimize League window
                IntPtr wdwIntPtr = FindWindow(null, "League of Legends");
                ShowWindow(wdwIntPtr, ShowWindowEnum.Minimize);
            }
        }
        bool chat_in_finalization = false;

        public void connectToLCU()
        {
            LCU = new WebSocket($"wss://127.0.0.1:{LCU_Request.riotPort}/", "wamp");
            LCU.SetCredentials("riot", LCU_Request.riotPass, true);
            LCU.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
            LCU.SslConfiguration.ServerCertificateValidationCallback = (send, certificate, chain, sslPolicyErrors) => true;
            LCU.OnMessage += async (s, e) =>
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
                    JObject playerStatus = await LCU_Request.GET("/lol-gameflow/v1/gameflow-metadata/player-status");
                    string queueId = playerStatus["currentLobbyStatus"]["queueId"].ToString();

                    if (data == "ReadyCheck") //큐가 잡히면 자동수락
                    {
                        if (Form1.Object.materialCheckBox8.Checked)
                        {
                            minimiseClient_conditional();
                            await LCU_Request.POST("/lol-matchmaking/v1/ready-check/accept", "0");
                        }

                        //로비로 돌아올 경우 List와 Variable 초기화
                        Console.WriteLine("List Cleared");
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
                                await pickChampion("practice");
                        }

                        if (queueId == "430") // Normal Game Only (430)
                        {
                            //Champion Instant Lock (Normal)
                            if (Form1.Object.materialCheckBox14.Checked)
                                await pickChampion("blind");

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

                                JArray jsonArray = await LCU_Request.GET_Array("/lol-chat/v1/conversations");

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
                            await pickChampion("draft");
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

                // Ban, Pick for Ranked, and ARAM
                if (uri.Contains("/lol-champ-select/v1/summoners/"))
                {
                    // Get QueueId
                    JObject playerStatus = await LCU_Request.GET("/lol-gameflow/v1/gameflow-metadata/player-status");
                    string queueId = playerStatus["currentLobbyStatus"]["queueId"].ToString();

                    // Find localPlayerCellId
                    JObject session = await LCU_Request.GET("/lol-champ-select/v1/session");
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

                            string payload = $"{{\"championId\":{Form1.Object.selectedBanChampionId}, \"completed\": true}}";
                            await LCU_Request.PATCH($"/lol-champ-select/v1/session/actions/{localPlayerCellId}", payload);

                            minimiseClient_conditional();
                        }

                        //Champion Pick (Draft)
                        if (Form1.Object.materialCheckBox14.Checked && (queueId == "400" || queueId == "420" || queueId == "440"))
                            if (activeActionType == "pick" && skinId == "0" && isActingNow && !isDonePicking)
                                await pickChampion("draft");
                    }
                }
            };
            LCU.Connect();
            LCU.Send($"[5, \"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase\"]");
            Form1.Object.isConnectedtoWebsocket = true;
        }

        public WebSocket LCU_Debug;

        private async Task pickChampion(string queueType)
        {
            // Find my CellId (pick order)
            JObject session = await LCU_Request.GET("/lol-champ-select/v1/session");
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
                string payload = $"{{\"championId\":{Form1.Object.selectedChampionId}, \"completed\": true}}";
                await LCU_Request.PATCH($"/lol-champ-select/v1/session/actions/{id_}", payload);
            }
            else
            {
                string payload;

                if (Form1.Object.materialCheckBox16.Checked) // lock-in
                    payload = $"{{\"championId\":{Form1.Object.selectedChampionId}, \"completed\": true}}";
                else // no lock-in
                    payload = $"{{\"championId\":{Form1.Object.selectedChampionId}, \"completed\": false}}";

                if (queueType == "blind")
                    await LCU_Request.PATCH($"/lol-champ-select/v1/session/actions/{localPlayerCellId}", payload);

                if (queueType == "practice")
                    await LCU_Request.PATCH($"/lol-champ-select/v1/session/actions/1", payload);
            }
        }

        bool isChatAvailable = false;
        private async void sendChatinChampSelect(string body)
        {
            JArray jsonArray = await LCU_Request.GET_Array("/lol-chat/v1/conversations");
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

                string payload = $"{{\"type\":\"chat\", \"isHistorical\":false, \"body\":\"{body}\"}}";
                await LCU_Request.POST($"/lol-chat/v1/conversations/{id}/messages", payload);
            }
        }

        public void debug()
        {
            LCU_Debug = new WebSocket($"wss://127.0.0.1:{LCU_Request.riotPort}/", "wamp");
            LCU_Debug.SetCredentials("riot", LCU_Request.riotPass, true);
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