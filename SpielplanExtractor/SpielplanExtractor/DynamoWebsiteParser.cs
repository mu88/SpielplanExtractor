using System;
using System.Linq;
using HtmlAgilityPack;

namespace SpielplanExtractor
{
    internal class DynamoWebsiteParser : ISeasonFactory
    {
        private const string Url = "https://www.dynamo-dresden.de/saison/spielplan-2020.html";

        /// <inheritdoc />
        public Season ConstructSeason()
        {
            // parse website
            var web = new HtmlWeb();
            var doc = web.Load(Url);

            // determine season
            var tableHeader = doc.DocumentNode.SelectSingleNode("//header/h2").InnerText;
            var seasonString = tableHeader.Substring(tableHeader.Length - 9, 9); // matches to '2018/2019'
            var season = new Season(seasonString);

            // get and iterate over table of games
            var tbodyNode = doc.DocumentNode.SelectSingleNode("//tbody");
            foreach (var tbodyChildNode in tbodyNode.ChildNodes)
            {
                // ensure a non-empty table
                var trowChildNodes = tbodyChildNode.ChildNodes.Where(x => x.OriginalName == "td").ToList();
                if (trowChildNodes.Count == 0)
                {
                    continue;
                }

                // extract basic game facts
                var date = trowChildNodes[0].InnerText.Split('-')[0].Split('.');
                var day = Convert.ToInt32(date[0]);
                var month = Convert.ToInt32(date[1]);
                var year = Convert.ToInt32(date[2]);
                var hour = Convert.ToInt32(trowChildNodes[1].InnerText.Substring(0, 2));
                var minute = Convert.ToInt32(trowChildNodes[1].InnerText.Substring(3, 2));

                // determine participating teams
                var teamsString = "";
                var gameString = "";
                var teamFinder = trowChildNodes[2].ChildNodes.Where(x => x.OriginalName == "span" || x.OriginalName == "a").ToList();
                if (teamFinder.Count == 2) // sometimes the root element directly contains two span-objects...
                {
                    gameString = teamFinder[0].InnerText;
                    teamsString = teamFinder[1].InnerText;
                }
                else if (teamFinder.Count == 1) // and other times one a-object with two nested span-objects
                {
                    gameString = teamFinder[0].ChildNodes[1].InnerText;
                    teamsString = teamFinder[0].ChildNodes[3].InnerText;
                }

                var teams = teamsString.Split('-'); // matches to 'Team A - Team B' => afterwards remove leading and trailing whitespaces
                var homeTeam = teams[0].Remove(teams[0].Length - 1, 1);
                var awayTeam = teams[1].Remove(0, 1);

                // find out location of the game (home or away)
                string location;
                string opponent;
                if (homeTeam.Contains("Dynamo Dresden"))
                {
                    location = "Dresden";
                    opponent = awayTeam;
                }
                else
                {
                    location = homeTeam;
                    opponent = homeTeam;
                }

                // ignore 'Testspiele'
                if (gameString.Split('-')[0].StartsWith("Test"))
                {
                    continue;
                }

                // each game gets it's unique identifier
                var identifier = $"{season.StartYear}/{season.EndYear} - {gameString}";

                season.Games.Add(new Game(new DateTime(year, month, day, hour, minute, 0), location, opponent, identifier));
            }

            return season;
        }
    }
}