using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;

namespace BordtennisDemoCS;

public class Program
{
    public enum Seasons
    {
        SEASON_20_21 = 42020,
        SEASON_21_22 = 42021,
        SEASON_22_23 = 42022,
        SEASON_23_24 = 42023,
        SEASON_24_25 = 42024
    }

    private const string playerProfileBaseAddress =
        "https://bordtennisportalen.dk/SportsResults/Components/WebService1.asmx/GetPlayerProfile";

    private const string playerRankingListBaseAddress =
        "https://bordtennisportalen.dk/SportsResults/Components/WebService1.asmx/GetPlayerRankingListPoints";

    private static readonly HttpClient client = new();

    public static async Task<PlayerProfileRankingData> GetPlayerProfileData(PlayerProfileData playerProfilePayload)
    {
        var playerProfileHeaders = new Dictionary<string, string>
        {
            { "User-Agent", "Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0" },
            { "Content-Type", "application/json; charset=utf-8" }
        };

        var json = playerProfilePayload.ToJObject().ToString();
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(playerProfileBaseAddress, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        var playerProfileHtml = JObject.Parse(responseBody)["d"];
        var html = (string)playerProfileHtml["Html"];

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var aElement = doc.DocumentNode.SelectSingleNode("//a[@title='Vis opnåede point']");
        if (aElement == null)
            return null;

        var onclickValue = aElement.GetAttributeValue("onclick", null);
        if (onclickValue != null)
        {
            var start = onclickValue.IndexOf('(') + 1;
            var end = onclickValue.IndexOf(')');
            var paramsList = onclickValue.Substring(start, end - start).Split(',');

            var playerRankingData = new PlayerProfileRankingData(
                int.Parse(paramsList[0].Trim()),
                paramsList[1].Trim(),
                paramsList[2].Trim(),
                paramsList[3].Trim()
            );

            return playerRankingData;
        }

        return null;
    }

    public static async Task<List<Dictionary<string, string>>> GetPlayerRankingListData(
        PlayerProfileRankingData playerProfileRankingData)
    {
        var headers = new Dictionary<string, string>
        {
            { "User-Agent", "Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0" },
            { "Content-Type", "application/json; charset=utf-8" }
        };

        var json = playerProfileRankingData.ToJObject().ToString();
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(playerRankingListBaseAddress, content);
        var playerRankingHtml = await response.Content.ReadAsStringAsync();

        var rankingHtml = JObject.Parse(playerRankingHtml)["d"];
        var html = (string)rankingHtml["Html"];

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var table = doc.DocumentNode.SelectSingleNode("//table[contains(@class, 'playerprofilerankingpointstable')]");
        var headersList = new List<string>();

        foreach (var header in table.SelectNodes(".//th")) headersList.Add(header.InnerText.Trim());

        var data = new List<Dictionary<string, string>>();
        foreach (var row in table.SelectNodes(".//tr").Skip(1)) // Skip the header row
        {
            var cols = row.SelectNodes(".//td");
            if (cols != null && cols.Count > 0)
            {
                var rowData = new Dictionary<string, string>();
                for (var i = 0; i < cols.Count; i++) rowData[headersList[i]] = cols[i].InnerText.Trim();
                data.Add(rowData);
            }
        }

        return data.Count > 0 ? data : null;
    }

    public static async Task<string> GetContextKey()
    {
        var playerProfileHeaders = new Dictionary<string, string>
        {
            { "User-Agent", "Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0" }
        };

        var response = await client.GetAsync("https://bordtennisportalen.dk/");
        var html = await response.Content.ReadAsStringAsync();

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var scriptContent = doc.DocumentNode.SelectSingleNode("//script").InnerText;
        var match = Regex.Match(scriptContent, @"SR_CallbackContext\s*=\s*'([^']+)'");

        return match.Success ? match.Groups[1].Value : null;
    }

    public static async Task Main(string[] args)
    {
        var allStats = new List<Dictionary<string, string>>();

        foreach (Seasons season in Enum.GetValues(typeof(Seasons)))
        {
            var playerProfilePayload = new PlayerProfileData(
                "93A0FE1C7ECEE3176F67CBF3F964B3DF61029AD09065090C1E2C513EDE5A6DC2022B06A3A7B49B829A6A27791969EF65",
                (int)season,
                "328804"
            );

            Console.WriteLine($"Querying endpoint for data on Season: {season}");
            var playerData = await GetPlayerProfileData(playerProfilePayload);

            if (playerData == null)
                continue;

            var rankingData = await GetPlayerRankingListData(playerData);
            Console.WriteLine("Got response. Adding data to the collection");

            allStats.AddRange(rankingData);
        }

        using (var file = new StreamWriter("output.csv", false, Encoding.UTF8))
        {
            if (allStats.Count > 0)
            {
                var headers = string.Join(",", allStats[0].Keys);
                await file.WriteLineAsync(headers);

                foreach (var stat in allStats)
                {
                    var line = string.Join(",",
                        stat.Values.Select(value => $"\"{value}\"")); // Enclose each value in quotes
                    await file.WriteLineAsync(line);
                }
            }
        }
    }

    public class PlayerProfileData
    {
        public PlayerProfileData(string callbackContextKey, int seasonId, string playerId, bool getPlayerData = true,
            bool showUserProfile = true, bool showHeader = false)
        {
            CallbackContextKey = callbackContextKey;
            SeasonId = seasonId;
            PlayerId = playerId;
            GetPlayerData = getPlayerData;
            ShowUserProfile = showUserProfile;
            ShowHeader = showHeader;
        }

        public string CallbackContextKey { get; set; }
        public int SeasonId { get; set; }
        public string PlayerId { get; set; }
        public bool GetPlayerData { get; set; }
        public bool ShowUserProfile { get; set; }
        public bool ShowHeader { get; set; }

        public JObject ToJObject()
        {
            return new JObject
            {
                { "callbackcontextkey", CallbackContextKey },
                { "seasonid", SeasonId },
                { "playerid", PlayerId },
                { "getplayerdata", GetPlayerData },
                { "showUserProfile", ShowUserProfile },
                { "showheader", ShowHeader }
            };
        }
    }

    public class PlayerProfileRankingData
    {
        public PlayerProfileRankingData(int seasonId, string playerId, string rankingListId, string rankingListPlayerId)
        {
            SeasonId = seasonId;
            PlayerId = playerId;
            RankingListId = rankingListId;
            RankingListPlayerId = rankingListPlayerId;
        }

        public string CallbackContextKey { get; set; } =
            "93A0FE1C7ECEE3176F67CBF3F964B3DF61029AD09065090C1E2C513EDE5A6DC2152291EFAB40E90FD597017D5343147E";

        public bool GetPlayerData { get; set; } = true; // Assuming this is a constant value
        public string PlayerId { get; set; }
        public string RankingListId { get; set; }
        public string RankingListPlayerId { get; set; }
        public int SeasonId { get; set; }

        public JObject ToJObject()
        {
            return new JObject
            {
                { "callbackcontextkey", CallbackContextKey },
                { "seasonid", SeasonId },
                { "playerid", PlayerId },
                { "rankinglistid", RankingListId },
                { "rankinglistplayerid", RankingListPlayerId },
                { "getplayerdata", GetPlayerData }
            };
        }
    }
}