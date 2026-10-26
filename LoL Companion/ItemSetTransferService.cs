using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace LoL_Companion
{
    internal static class ItemSetTransferService
    {
        internal const string Format = "lol-companion-item-sets-v1";

        public static async Task<JObject> ExportAsync(LCU_Request request)
        {
            var summoner = await request.GET("/lol-summoner/v1/current-summoner");
            return new JObject { ["puuid"] = summoner["puuid"],
                ["items"] = await request.GET("/lol-item-sets/v1/item-sets/" + summoner["summonerId"] + "/sets") };
        }

        public static JToken CreateExport(JArray entries)
        {
            return entries.Count == 1 ? entries[0]["items"].DeepClone()
                : new JObject { ["format"] = Format, ["accounts"] = entries.DeepClone() };
        }

        public static async Task ImportAsync(LCU_Request request, JToken saved)
        {
            var summoner = await request.GET("/lol-summoner/v1/current-summoner");
            JToken items = saved;
            if ((string)saved["format"] == Format)
            {
                items = ((JArray)saved["accounts"]).OfType<JObject>()
                    .SingleOrDefault(entry => (string)entry["puuid"] == (string)summoner["puuid"])?["items"];
                if (items == null) throw new InvalidOperationException("이 계정의 아이템 세트가 저장 파일에 없습니다.");
            }
            await request.PUT("/lol-item-sets/v1/item-sets/" + summoner["summonerId"] + "/sets", items.ToString());
        }
    }
}
