using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace LoL_Companion
{
    internal sealed class PracticeToolLauncher
    {
        private readonly LCU_Request request;

        public PracticeToolLauncher(LCU_Request request)
        {
            this.request = request;
        }

        public async Task StartAsync()
        {
            string phase = (string)await request.GET_Value("/lol-gameflow/v1/gameflow-phase");
            if (phase != "None" && phase != "Lobby")
                throw new InvalidOperationException("현재 상태(" + phase + ")에서는 연습 모드를 시작할 수 없습니다.");

            if (phase == "Lobby")
            {
                JObject currentLobby = await request.GET("/lol-lobby/v2/lobby");
                if (currentLobby["localMember"]?["isLeader"]?.Value<bool>() != true)
                    throw new InvalidOperationException("로비 방장만 연습 모드를 시작할 수 있습니다.");
            }

            JArray queues = await request.GET_Array("/lol-game-queues/v1/queues");
            JObject queue = queues.OfType<JObject>().FirstOrDefault(candidate =>
                (string)candidate["gameMode"] == "PRACTICETOOL" &&
                candidate["isCustom"]?.Value<bool>() == true &&
                candidate["isEnabled"]?.Value<bool>() == true &&
                (string)candidate["queueAvailability"] == "Available");
            if (queue == null)
                throw new InvalidOperationException("현재 클라이언트에서 사용 가능한 연습 모드를 찾지 못했습니다.");

            JObject customModes = await request.GET("/lol-game-queues/v1/custom-non-default");
            JObject mode = (customModes["subcategories"] as JArray)?.OfType<JObject>()
                .FirstOrDefault(candidate =>
                    (string)candidate["gameMode"] == "PRACTICETOOL" &&
                    (int?)candidate["mapId"] == (int?)queue["mapId"] &&
                    (string)candidate["queueAvailability"] == "Available");
            JToken gameType = queue["gameTypeConfig"];
            if (mode == null || gameType == null || gameType.Type != JTokenType.Object)
                throw new InvalidOperationException("연습 모드의 최신 게임 설정을 불러오지 못했습니다.");

            var payload = new JObject
            {
                ["queueId"] = queue["id"].DeepClone(),
                ["customGameLobby"] = new JObject
                {
                    ["lobbyName"] = "Practice Tool",
                    ["lobbyPassword"] = string.Empty,
                    ["configuration"] = new JObject
                    {
                        ["gameMode"] = "PRACTICETOOL",
                        ["mapId"] = queue["mapId"].DeepClone(),
                        ["mutators"] = gameType.DeepClone(),
                        ["gameTypeConfig"] = gameType.DeepClone(),
                        ["spectatorPolicy"] = "AllAllowed",
                        ["teamSize"] = mode["numPlayersPerTeam"].DeepClone(),
                        ["maxPlayerCount"] = mode["maxPlayerCount"].DeepClone(),
                        ["gameServerRegion"] = string.Empty
                    }
                }
            };

            await request.POST("/lol-lobby/v2/lobby", payload.ToString());
            bool started = false;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                JObject lobby = await request.GET("/lol-lobby/v2/lobby");
                if ((int?)lobby["gameConfig"]?["queueId"] != (int?)queue["id"] ||
                    (string)lobby["gameConfig"]?["gameMode"] != "PRACTICETOOL")
                    throw new InvalidOperationException("연습 모드 로비 생성 결과가 요청한 설정과 다릅니다.");

                if (lobby["canStartActivity"]?.Value<bool>() == true)
                {
                    // The LCU can return HTTP 200 with success=false while the new lobby is initializing.
                    JToken result = await request.POST_Value(
                        "/lol-lobby/v1/lobby/custom/start-champ-select", "{}");
                    if (result?["success"]?.Value<bool>() != false)
                    {
                        started = true;
                        break;
                    }
                }
                await Task.Delay(250);
            }
            if (!started)
                throw new InvalidOperationException("연습 모드 로비가 아직 시작 가능한 상태가 아닙니다.");

            for (int attempt = 0; attempt < 40; attempt++)
            {
                phase = (string)await request.GET_Value("/lol-gameflow/v1/gameflow-phase");
                if (phase == "ChampSelect")
                    return;
                if (phase != "Lobby")
                    throw new InvalidOperationException("챔피언 선택 진입 중 클라이언트 상태가 변경되었습니다: " + phase);
                await Task.Delay(250);
            }
            throw new InvalidOperationException("연습 모드 챔피언 선택 화면으로 전환되지 않았습니다.");
        }
    }
}
