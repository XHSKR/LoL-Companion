using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;

namespace LoL_Companion
{
    internal sealed class QuickDodgeService
    {
        private readonly LCU_Request lcu;
        private readonly LeagueEdgeClient server;

        public QuickDodgeService(LCU_Request lcu, LeagueEdgeClient server)
        {
            this.lcu = lcu;
            this.server = server;
        }

        public async Task DodgeAsync()
        {
            string phase = (string)await lcu.GET_Value("/lol-gameflow/v1/gameflow-phase");
            if (phase != "ChampSelect")
                throw new InvalidOperationException("닷지는 챔피언 선택 중에만 사용할 수 있습니다. 현재 상태: " + phase);

            JObject session = await lcu.GET("/lol-gameflow/v1/session");
            string gameId = session["gameData"]?["gameId"]?.ToString();
            if ((string)session["phase"] != "ChampSelect" || string.IsNullOrWhiteSpace(gameId))
                throw new InvalidOperationException("챔피언 선택 세션을 확인하지 못했습니다.");

            // Send one server request. Never close the client or fall back to a different exit API.
            await server.QuitChampionSelectAsync(gameId);
            for (int attempt = 0; attempt < 40; attempt++)
            {
                phase = (string)await lcu.GET_Value("/lol-gameflow/v1/gameflow-phase");
                if (phase == "None" || phase == "Lobby" || phase == "Matchmaking")
                    return;
                if (phase != "ChampSelect")
                    throw new InvalidOperationException("닷지 응답 후 클라이언트 상태가 변경되었습니다: " + phase);
                await Task.Delay(250);
            }
            throw new InvalidOperationException("서버는 퇴장 성공을 응답했지만 챔피언 선택 종료가 확인되지 않았습니다.");
        }
    }
}
