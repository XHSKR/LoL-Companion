using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text;
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
            if (!Form1.isClientFocused)
            {
                Task.Delay(50);
                //Minimize League window
                IntPtr wdwIntPtr = FindWindow(null, "League of Legends");
                ShowWindow(wdwIntPtr, ShowWindowEnum.Minimize);
            }
        }

        private static readonly HttpClient httpClient = new HttpClient();
        private static Dictionary<int, Dictionary<string, int>> ccTypeMapping;
        private static Dictionary<int, string> champNameMapping;
        private bool chat_in_finalization = false;

        // 1) 최신 Data Dragon 버전 조회
        private static async Task<string> GetLatestDataDragonVersionAsync()
        {
            string versionsJson = await httpClient.GetStringAsync(
                "https://ddragon.leagueoflegends.com/api/versions.json");
            JArray versions = JArray.Parse(versionsJson);
            return versions[0].ToString();
        }

        // 2) 챔피언별 CC 유형별 매핑 초기화
        // 챔피언별 CC 유형별 매핑 초기화 (한국어, tooltip 사용, 둔화 삭제, passive 포함)
        private static async Task InitCCTypeMappingAsync()
        {
            if (ccTypeMapping != null) return;

            ccTypeMapping = new Dictionary<int, Dictionary<string, int>>();
            champNameMapping = new Dictionary<int, string>();

            // 최신 버전 조회
            string version = await GetLatestDataDragonVersionAsync();
            // 한국어 데이터 URL
            string url = $"https://ddragon.leagueoflegends.com/cdn/{version}/data/ko_KR/championFull.json";
            string jsonText = await httpClient.GetStringAsync(url);
            JObject allData = JObject.Parse(jsonText)["data"] as JObject;

            // 한글 CC 키워드 목록 ('둔화' 제거)
            string[] ccKeywords = {
                "기절",    // stun
                "속박",    // root/snare
                "제압",    // suppress
                "도발",    // taunt
                "공포",    // fear
                "침묵",    // silence
                "수면",    // sleep
                "실명",    // blind
                "변이",    // polymorph
                "매혹"     // charm
            };

            foreach (var prop in allData.Properties())
            {
                JObject champ = (JObject)prop.Value;
                int champId = int.Parse(champ["key"].ToString());
                // 한글 챔피언 이름
                string champName = champ["name"].ToString();
                champNameMapping[champId] = champName;

                // 키워드별 초기 카운트
                var dict = ccKeywords.ToDictionary(kw => kw, kw => 0);

                // 1) spells의 tooltip 검사
                foreach (var spell in (JArray)champ["spells"])
                {
                    string tooltip = spell["tooltip"]?.ToString().ToLower() ?? "";
                    foreach (string kw in ccKeywords)
                    {
                        if (tooltip.Contains(kw))
                            dict[kw]++;
                    }
                }

                // 2) passive의 tooltip 또는 description 검사
                if (champ["passive"] is JObject passive)
                {
                    // tooltip 필드가 없으면 description 사용
                    string passiveText = passive["tooltip"]?.ToString().ToLower()
                                         ?? passive["description"]?.ToString().ToLower()
                                         ?? "";
                    foreach (string kw in ccKeywords)
                    {
                        if (passiveText.Contains(kw))
                            dict[kw]++;
                    }
                }

                ccTypeMapping[champId] = dict;
            }
        }

        // 3) FINALIZATION 단계에서 상대팀 CC 계산 및 채팅 전송
        private async Task CheckEnemyTeamCCAsync()
        {
            await InitCCTypeMappingAsync();

            JObject session = await LCU_Request.GET("/lol-champ-select/v1/session");
            var theirTeam = (JArray)session["theirTeam"];

            foreach (var member in theirTeam)
            {
                int id = (int)member["championId"];
                if (!ccTypeMapping.ContainsKey(id)) continue;

                string name = champNameMapping[id];
                var counts = ccTypeMapping[id]
                    .Where(kv => kv.Value > 0)
                    .Select(kv => $"{kv.Key}{kv.Value}");

                if (!counts.Any()) continue;

                string message = $"{name}: {string.Join(", ", counts)}";
                Console.WriteLine(message);
                sendChatinChampSelect(message);
            }
        }

        public void connectToLCU()
        {
            LCU = new WebSocket($"wss://127.0.0.1:{LCU_Request.riotPort}/", "wamp");
            LCU.SetCredentials("riot", LCU_Request.riotPass, true);
            LCU.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
            LCU.SslConfiguration.ServerCertificateValidationCallback =
                (send, certificate, chain, sslPolicyErrors) => true;

            LCU.OnMessage += async (s, e) =>
            {
                if (!e.IsText)
                    return;

                JArray eventArray;
                try
                {
                    eventArray = JArray.Parse(e.Data);
                }
                catch
                {
                    return;
                }

                if (eventArray.Count < 3)
                    return;

                int eventNumber = eventArray[0].ToObject<int>();
                if (eventNumber != 8)
                    return;

                JObject json;
                try
                {
                    json = JObject.Parse(eventArray[2].ToString());
                }
                catch
                {
                    return;
                }

                string uri = json["uri"]?.ToString() ?? string.Empty;
                string data = json["data"]?.ToString() ?? string.Empty;

                var eventNames = new List<string>
                {
                    "OnJsonApiEvent_lol-champ-select_v1_session",
                    "OnJsonApiEvent_lol-champ-select_v1_summoners",
                };

                // 게임플로우 단계 처리
                if (uri.Contains("/lol-gameflow/v1/gameflow-phase"))
                {
                    if (data == "None")
                    {
                        foreach (var name in eventNames)
                            LCU.Send($"[6, \"{name}\"]");
                    }

                    // QueueId 안전하게 가져오기
                    string queueId = string.Empty;
                    try
                    {
                        var playerStatus = await LCU_Request.GET("/lol-gameflow/v1/gameflow-metadata/player-status");
                        var lobbyStatus = playerStatus?["currentLobbyStatus"];
                        if (lobbyStatus is JObject obj && obj["queueId"] != null)
                            queueId = obj["queueId"].ToString();
                    }
                    catch
                    {
                        // 무시
                    }

                    if (data == "ReadyCheck")
                    {
                        if (Form1.Object.materialCheckBox8.Checked)
                        {
                            minimiseClient_conditional();
                            await LCU_Request.POST("/lol-matchmaking/v1/ready-check/accept", "0");
                        }
                        isChatAvailable = false;
                        chat_in_finalization = false;
                    }

                    if (data == "ChampSelect")
                    {
                        foreach (var name in eventNames)
                            LCU.Send($"[5, \"{name}\"]");

                        // 1) 연습 모드 (Practice)
                        if (queueId == "-1" && Form1.Object.materialCheckBox14.Checked)
                        {
                            await pickChampion("practice");
                        }

                        // 2) 노말 블라인드 (QueueId 430)
                        if (queueId == "430" && Form1.Object.materialCheckBox14.Checked)
                        {
                            await pickChampion("blind");
                            if (Form1.Object.materialCheckBox12.Checked)
                            {
                                // 포지션 호출 로직
                                string position = "";
                                if (Form1.Object.materialRadioButton5.Checked) position = "상단(탑) | Top | 上单";
                                if (Form1.Object.materialRadioButton6.Checked) position = "정글 | Jungle | 打野";
                                if (Form1.Object.materialRadioButton7.Checked) position = "미드 | Mid | 中单";
                                if (Form1.Object.materialRadioButton8.Checked) position = "봇(원딜) | Bot | ADC";
                                if (Form1.Object.materialRadioButton9.Checked) position = "서포터 | Support | 辅助";

                                JArray convoArray = null;
                                try
                                {
                                    convoArray = await LCU_Request.GET_Array("/lol-chat/v1/conversations");
                                }
                                catch { }

                                while (!isChatAvailable)
                                {
                                    for (int i = 0; i < 5; i++)
                                        sendChatinChampSelect(position);
                                }
                                isChatAvailable = false;
                            }
                        }

                        // 3) 드래프트·랭크·ARAM (QueueId 400,420,440)
                        if (Form1.Object.materialCheckBox14.Checked &&
                            (queueId == "400" || queueId == "420" || queueId == "440"))
                        {
                            await pickChampion("draft");
                        }
                    }
                }

                // Finalization 단계 채팅
                if (uri.Contains("/lol-champ-select/v1/session"))
                {
                    string phase = json["data"]?["timer"]?["phase"]?.ToString() ?? string.Empty;
                    if (phase == "FINALIZATION" && !chat_in_finalization)
                    {
                        await CheckEnemyTeamCCAsync();
                        if (Form1.Object.materialCheckBox21.Checked)
                            sendChatinChampSelect(Form1.Object.materialSingleLineTextField2.Text);

                        chat_in_finalization = true;
                    }
                }

                // 밴/픽 처리
                if (uri.Contains("/lol-champ-select/v1/summoners/"))
                {
                    // 안전하게 QueueId
                    string queueId2 = string.Empty;
                    try
                    {
                        var ps = await LCU_Request.GET("/lol-gameflow/v1/gameflow-metadata/player-status");
                        var ls = ps?["currentLobbyStatus"];
                        if (ls is JObject o && o["queueId"] != null)
                            queueId2 = o["queueId"].ToString();
                    }
                    catch { }

                    // 세션 정보
                    JObject session = null;
                    try
                    {
                        session = await LCU_Request.GET("/lol-champ-select/v1/session");
                    }
                    catch { }

                    string localPlayerCellId = session?["localPlayerCellId"]?.ToString() ?? string.Empty;

                    // JSON 데이터 안전 접근
                    var dataNode = json["data"];
                    string actionType = dataNode?["activeActionType"]?.ToString() ?? string.Empty;
                    bool isSelf = dataNode?["isSelf"]?.ToObject<bool>() ?? false;
                    string skinId = dataNode?["skinId"]?.ToString() ?? string.Empty;
                    bool isActingNow = dataNode?["isActingNow"]?.ToObject<bool>() ?? false;
                    bool isDonePicking = dataNode?["isDonePicking"]?.ToObject<bool>() ?? false;

                    if (isSelf)
                    {
                        // 밴
                        if (Form1.Object.materialCheckBox20.Checked && actionType == "ban")
                        {
                            if (Form1.Object.materialCheckBox19.Checked)
                                sendChatinChampSelect(Form1.Object.materialSingleLineTextField1.Text);

                            string payload = $"{{\"championId\":{Form1.Object.selectedBanChampionId},\"completed\":true}}";
                            await LCU_Request.PATCH(
                                $"/lol-champ-select/v1/session/actions/{localPlayerCellId}",
                                payload
                            );

                            minimiseClient_conditional();
                        }

                        // 드래프트 픽
                        if (Form1.Object.materialCheckBox14.Checked &&
                            (queueId2 == "400" || queueId2 == "420" || queueId2 == "440") &&
                            actionType == "pick" && skinId == "0" &&
                            isActingNow && !isDonePicking)
                        {
                            await pickChampion("draft");
                        }
                    }
                }
            };

            LCU.Connect();
            LCU.Send("[5, \"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase\"]");
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