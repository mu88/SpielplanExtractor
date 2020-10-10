using System;
using System.Linq;
using HtmlAgilityPack;

namespace SpielplanExtractor
{
    internal class KickerSeasonParser : ISeasonFactory
    {
        private const string Url = "https://www.kicker.de/dynamo-dresden-65/team-termine/3-liga/2020-21";

        /// <inheritdoc />
        public Season ConstructSeason()
        {
            // parse website
            var web = new HtmlWeb();
            var doc = web.Load(Url);

            var season = new Season(BuildSeasonString());

            var table = GetMainTable(doc);

            var rows = GetGameRowsFromTable(table);

            foreach (var row in rows)
            {
                var competition = GetCompetition(row);
                var team1 = GetTeam1(row);
                var team2 = GetTeam2(row);

                var identifier = BuildIdentifier(season, competition);

                var gameTime = BuildGameTime(row);
                var game = IsHomeGame(team1)
                               ? new Game(gameTime, "Dresden", team2, identifier)
                               : new Game(gameTime, team1, team1, identifier);

                season.Games.Add(game);
            }

            return season;
        }

        private static HtmlNodeCollection GetGameRowsFromTable(HtmlNode table)
        {
            return table.SelectNodes("//tbody/tr");
        }

        private static HtmlNode GetMainTable(HtmlDocument doc)
        {
            return doc.DocumentNode.Descendants()
                      .Where(x => x.NodeType == HtmlNodeType.Element)
                      .Where(x => x.Name == "table")
                      .First(x => x.GetAttributeValue("class", string.Empty) ==
                                  "kick__table kick__table--gamelist kick__table--gamelist-timeline");
        }

        private static string BuildIdentifier(Season season, string competition)
        {
            return $"{season.StartYear}/{season.EndYear} - {competition}";
        }

        private static string GetTeam2(HtmlNode row)
        {
            return row.ChildNodes[7].ChildNodes[3].ChildNodes[5].ChildNodes[3].InnerText.Trim();
        }

        private static string GetTeam1(HtmlNode row)
        {
            return row.ChildNodes[7].ChildNodes[3].ChildNodes[1].ChildNodes[1].InnerText.Trim();
        }

        private static string GetCompetition(HtmlNode row)
        {
            return row.ChildNodes[5].ChildNodes[0].InnerText;
        }

        private static DateTime BuildGameTime(HtmlNode row)
        {
            var rawGameDate = row.ChildNodes[1].InnerText;
            var rawGameTime = row.ChildNodes.ElementAtOrDefault(7)
                                 ?.ChildNodes.ElementAtOrDefault(3)
                                 ?.ChildNodes.ElementAtOrDefault(3)
                                 ?.ChildNodes.ElementAtOrDefault(1)
                                 ?.ChildNodes.ElementAtOrDefault(3)
                                 ?.InnerText.Trim() ?? "14:00";

            DateTime.TryParse(rawGameDate, out var gameDate);
            DateTime.TryParse(rawGameTime, out var gameTime);

            return gameDate.Add(new TimeSpan(gameTime.Hour, gameTime.Minute, 0));
        }

        private static string BuildSeasonString()
        {
            var lastUrlPart = Url.Split('/').Last();
            var years = lastUrlPart.Split('-');

            return $"{years[0]}/20{years[1]}";
        }

        private bool IsHomeGame(string team1)
        {
            return team1 == "Dynamo Dresden";
        }
    }
}