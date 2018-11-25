using System;
using System.Linq;
using System.Net;
using System.Security;
using HtmlAgilityPack;
using Microsoft.Exchange.WebServices.Data;

namespace SpielplanExtractor
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var username = args[0]; // e. g. 'bla@bla.onmicrosoft.com'
            var password = MakeSecureString(args[1]);
            var url = new Uri(args[2]); // e. g. 'https://www.dynamo-dresden.de/saison/spielplan/2018-2019.html'

            var service = CreateExchangeService(username, password);

            var dynamoFolderId = FindDynamoFolderId(service) ?? throw new Exception("Calendar 'Dynamo' not found.");

            var season = ConstructSeason(url);

            SetUpAppointments(season, service, dynamoFolderId);
        }

        private static void SetUpAppointments(Season season, ExchangeService service, FolderId dynamoFolderId)
        {
            var existingAppointments = LoadExistingAppointments(season, service, dynamoFolderId);

            foreach (var game in season.Games)
            {
                // ignore games that have already been played
                var isInThePast = game.Date<DateTime.Now.Date;
                if (isInThePast)
                {
                    continue;
                }

                Appointment appointment;
                var isNewAppointment = false;
                var existingAppointment = existingAppointments.FirstOrDefault(x => x.Body.ToString().Contains(game.Identifier));
                if (existingAppointment == null)
                {
                    appointment = new Appointment(service)
                    {
                        Body = game.Identifier,
                        LegacyFreeBusyStatus = LegacyFreeBusyStatus.Free,
                        IsReminderSet = false,
                        Subject = game.Opponent
                    };

                    isNewAppointment = true;
                }
                else
                {
                    appointment = existingAppointment;
                }

                // ALWAYS set date and location of the game
                appointment.Start = game.Date;
                appointment.End = game.Date.AddMinutes(105);
                appointment.Location = game.Location;

                // either store the new appointment or update the existing appointment
                if (isNewAppointment)
                {
                    appointment.Save(dynamoFolderId, SendInvitationsMode.SendToNone);
                }
                else
                {
                    appointment.Update(ConflictResolutionMode.AlwaysOverwrite, SendInvitationsOrCancellationsMode.SendToNone);
                }
            }
        }

        private static FindItemsResults<Appointment> LoadExistingAppointments(Season season, ExchangeService service,
            FolderId dynamoFolderId)
        {
            var calendarView = new CalendarView(new DateTime(season.StartYear, 1, 1), new DateTime(season.EndYear, 12, 31));
            var existingAppointments = service.FindAppointments(dynamoFolderId, calendarView);
            foreach (var existingAppointment in existingAppointments)
            {
                existingAppointment.Load();
            }

            return existingAppointments;
        }

        private static Season ConstructSeason(Uri url)
        {
            // parse website
            var web = new HtmlWeb();
            var doc = web.Load(url);
            
            // determine season
            var tableHeader = doc.DocumentNode.SelectSingleNode("//header/h2").InnerText;
            var seasonString = tableHeader.Substring(tableHeader.Length-9, 9); // matches to '2018/2019'
            var season = new Season(seasonString);
            
            // get and iterate over table of games
            var tbodyNode = doc.DocumentNode.SelectSingleNode("//tbody");
            foreach (var tbodyChildNode in tbodyNode.ChildNodes)
            {
                // ensure a non-empty table
                var trowChildNodes = tbodyChildNode.ChildNodes.Where(x => x.OriginalName == "td").ToList();
                if (trowChildNodes.Count==0)
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
                var homeTeam = teams[0].Remove(teams[0].Length-1, 1);
                var awayTeam = teams[1].Remove(0,1);

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
        
        private static SecureString MakeSecureString(string password)
        {
            var secure = new SecureString();
            foreach (var c in password)
            {
                secure.AppendChar(c);
            }

            return secure;
        }

        private static bool RedirectionCallback(string url)
        {
            return url.ToLower().StartsWith("https://");
        }

        static ExchangeService CreateExchangeService(string userEmailAddress, SecureString userPassword)
        {
            var service = new ExchangeService {Credentials = new NetworkCredential(userEmailAddress, userPassword)};

            // Look up the user's EWS endpoint by using Autodiscover.
            service.AutodiscoverUrl(userEmailAddress, RedirectionCallback);

            return service;
        }

        private static FolderId FindDynamoFolderId(ExchangeService service)
        {
            var view = new FolderView(20)
            {
                PropertySet = new PropertySet(BasePropertySet.IdOnly) {FolderSchema.DisplayName},
                Traversal = FolderTraversal.Deep
            };
            var folders = service.FindFolders(WellKnownFolderName.Calendar, view);

            foreach (var folder in folders)
            {
                if (folder.DisplayName == "Dynamo")
                {
                    return folder.Id;
                }
            }

            return null;
        }
    }
}
