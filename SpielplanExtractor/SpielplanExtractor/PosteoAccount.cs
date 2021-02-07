using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace SpielplanExtractor
{
    internal class PosteoAccount : IAccount
    {
        public PosteoAccount(string username, string password)
        {
            Username = username;
            Password = password;
            HttpClient = new HttpClient();
            HttpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", ToBase64String($"{username}:{password}"));
        }

        private HttpClient HttpClient { get; }

        private string Password { get; }

        private string Username { get; }

        private string CalendarUri => "https://posteo.de:8443";

        /// <inheritdoc />
        public async Task SetUpAppointmentsAsync(Season season)
        {
            var dynamoFolder = FindDynamoFolder();
            var existingCalDavEvents = GetExistingCalDavEvents(dynamoFolder, season).ToList();

            foreach (var game in season.Games)
            {
                // ignore games that have already been played
                var isInThePast = game.Date < DateTime.Now.Date;
                if (isInThePast)
                {
                    continue;
                }

                var existingEvent =
                    existingCalDavEvents.SingleOrDefault(x => x.Description.Equals(game.Identifier, StringComparison.OrdinalIgnoreCase));
                if (existingEvent != null)
                {
                    await UpdateExistingGameAsync(existingEvent, game);
                }
                else
                {
                    await SaveNewGameAsync(game, dynamoFolder);
                }
            }
        }

        private static Stream ExecuteMethod(string username,
                                            string password,
                                            string calDavUri,
                                            string methodName,
                                            string content,
                                            string contentType,
                                            string depth)
        {
            // This code was borrowed from Stack Overflow. Therefore, it is very straightforward, ugly und not refactored.

            var httpGetRequest = (HttpWebRequest)WebRequest.Create(calDavUri);
            httpGetRequest.Credentials = new NetworkCredential(username, password);
            httpGetRequest.PreAuthenticate = true;
            httpGetRequest.Method = methodName;
            httpGetRequest.Headers.Add("Depth", depth);
            httpGetRequest.Headers.Add("Authorization", username);

            //httpGetRequest.UserAgent = "DAVKit/3.0.6 (661); CalendarStore/3.0.8 (860); iCal/3.0.8 (1287); Mac OS X/10.5.8 (9L31a)";
            httpGetRequest.UserAgent = "curl/7.37.0";
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                httpGetRequest.ContentType = contentType;
            }


            using (var streamWriter = new StreamWriter(httpGetRequest.GetRequestStream()))
            {
                var data = content;

                streamWriter.Write(data);
            }

            var httpGetResponse = (HttpWebResponse)httpGetRequest.GetResponse();
            var responseStream = httpGetResponse.GetResponseStream();

            return responseStream;
        }

        private static string NewCalDavIdentifier()
        {
            return ToBase64Url(Guid.NewGuid().ToString());
        }

        private static string ToBase64Url(string s)
        {
            return ToBase64String(s)
                   .Replace('+', '-') // replace URL unsafe characters with safe ones
                   .Replace('/', '_') // replace URL unsafe characters with safe ones
                   .Replace("=", "");
        }

        private static string ToBase64String(string s)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        }

        private IEnumerable<(string Name, string UrlSegment)> GetCalendars()
        {
            // This code was borrowed from Stack Overflow. Therefore, it is very straightforward, ugly und not refactored.

            var results = new Collection<(string, string)>();
            try
            {
                var responseStream = ExecuteMethod(Username,
                                                   Password,
                                                   CalendarUri,
                                                   "PROPFIND",
                                                   "<propfind xmlns='DAV:'><prop><current-user-principal/></prop></propfind>",
                                                   "application/x-www-form-urlencoded",
                                                   "0");

                var xmlDocument = new XmlDocument();
                xmlDocument.Load(responseStream);
                var xmlInner = xmlDocument.InnerXml;

                var innerXmlDocument = new XmlDocument();
                innerXmlDocument.LoadXml(xmlInner);

                var statusCheck = innerXmlDocument.GetElementsByTagName("d:status")[0];
                if (statusCheck == null)
                {
                    throw new NullReferenceException("Status is null");
                }

                var status = statusCheck.InnerText.Trim();
                if (status != "HTTP/1.1 200 OK")
                {
                    throw new WebException("Status was not HTTP 200 OK");
                }

                var userPrincipalElement = innerXmlDocument.GetElementsByTagName("d:current-user-principal")[0];
                var userPrincipalNode = userPrincipalElement?.ChildNodes[0];
                if (userPrincipalNode == null)
                {
                    throw new NullReferenceException("User principal is null");
                }

                var href = userPrincipalNode.InnerText.Trim();

                var baseUrl = CalendarUri + href;

                var propFind =
                    "<propfind xmlns='DAV:' xmlns:cd='urn:ietf:params:xml:ns:caldav'><prop><cd:calendar-home-set/></prop></propfind>";
                responseStream = ExecuteMethod(Username, Password, baseUrl, "PROPFIND", propFind, "application/x-www-form-urlencoded", "0");

                xmlDocument = new XmlDocument();
                xmlDocument.Load(responseStream);
                xmlInner = xmlDocument.InnerXml;

                innerXmlDocument = new XmlDocument();
                innerXmlDocument.LoadXml(xmlInner);
                userPrincipalElement = innerXmlDocument.GetElementsByTagName("cal:calendar-home-set")[0];
                if (userPrincipalElement != null)
                {
                    userPrincipalNode = userPrincipalElement.ChildNodes[0];
                    if (userPrincipalNode != null)
                    {
                        var calUrl = CalendarUri + userPrincipalNode.InnerText.Trim();
                        Console.WriteLine("CALENDER URL :" + calUrl);
                        propFind = "<propfind xmlns='DAV:'><prop><displayname/><getctag />calendar-data</prop></propfind>";

                        responseStream = ExecuteMethod(Username,
                                                       Password,
                                                       calUrl,
                                                       "PROPFIND",
                                                       propFind,
                                                       "application/x-www-form-urlencoded",
                                                       "1");
                        xmlDocument = new XmlDocument();
                        xmlDocument.Load(responseStream);
                        xmlInner = xmlDocument.InnerXml;

                        innerXmlDocument = new XmlDocument();
                        innerXmlDocument.LoadXml(xmlInner);

                        foreach (XmlNode row in innerXmlDocument.GetElementsByTagName("d:response"))
                        {
                            var calendarStatus = row["d:propstat"]?["d:status"]?.ChildNodes[0].Value;
                            if (calendarStatus != "HTTP/1.1 200 OK")
                            {
                                continue;
                            }

                            var calendarName = row["d:propstat"]["d:prop"]?["d:displayname"]?.ChildNodes[0].Value;
                            var calendarUrl = row["d:href"]?.ChildNodes[0].Value;

                            results.Add((calendarName, calendarUrl));
                        }
                    }
                }
            }
            catch
            {
            }

            return results;
        }

        private async Task SaveNewGameAsync(Game newGame, Uri dynamoFolder)
        {
            var generatedCalDavIdentifier = NewCalDavIdentifier();
            var httpContent = new StringContent(ConvertToCalDavEvent(newGame, generatedCalDavIdentifier), Encoding.UTF8, "text/calendar");
            var requestUri = new Uri(new Uri(CalendarUri), new Uri(dynamoFolder, $"{generatedCalDavIdentifier}.ics"));

            var response = await HttpClient.PutAsync(requestUri, httpContent);

            response.EnsureSuccessStatusCode();
        }

        private string ConvertToCalDavEvent(Game game, string calDavIdentifier)
        {
            var startTime = game.Date;
            var endTime = startTime.AddMinutes(105);

            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("BEGIN:VCALENDAR");
            stringBuilder.AppendLine("BEGIN:VEVENT");
            stringBuilder.AppendLine($"DTSTART:{startTime.ToUniversalTime():yyyy''MM''ddTHH''mm''ssZ}");
            stringBuilder.AppendLine($"DTEND:{endTime.ToUniversalTime():yyyy''MM''ddTHH''mm''ssZ}");
            stringBuilder.AppendLine($"SUMMARY:{game.Opponent}");
            stringBuilder.AppendLine($"DESCRIPTION:{game.Identifier}");
            stringBuilder.AppendLine($"LOCATION:{game.Location}");
            stringBuilder.AppendLine($"UID:{calDavIdentifier}");
            stringBuilder.AppendLine("END:VEVENT");
            stringBuilder.AppendLine("END:VCALENDAR");

            return stringBuilder.ToString();
        }

        private async Task UpdateExistingGameAsync(CalDavEvent existingEvent, Game existingGame)
        {
            var httpContent = new StringContent(ConvertToCalDavEvent(existingGame, existingEvent.CalDavIdentifier),
                                                Encoding.UTF8,
                                                "text/calendar");
            var requestUri = new Uri(new Uri(CalendarUri), existingEvent.RelativeUri);

            var response = await HttpClient.PutAsync(requestUri, httpContent);

            response.EnsureSuccessStatusCode();
        }

        private IEnumerable<CalDavEvent> GetExistingCalDavEvents(Uri dynamoFolder, Season season)
        {
            var stream = ExecuteMethod(Username,
                                       Password,
                                       dynamoFolder.AbsoluteUri,
                                       "REPORT",
                                       "<c:calendar-query xmlns:d='DAV:' xmlns:c='urn:ietf:params:xml:ns:caldav'>" +
                                       "<d:prop><d:getetag /><c:calendar-data /></d:prop>" + "<c:filter>" +
                                       "<c:comp-filter name='VCALENDAR'>" + "<c:comp-filter name='VEVENT'>" +
                                       $"<c:time-range  start='{season.StartYear}0101T000000Z' end='{season.EndYear}1231T235959Z'/>" +
                                       "</c:comp-filter></c:comp-filter></c:filter></c:calendar-query>",
                                       "application/x-www-form-urlencoded",
                                       "1");

            var xmlDocument = new XmlDocument();
            xmlDocument.Load(stream);

            foreach (XmlElement childNode in xmlDocument.ChildNodes[1].ChildNodes)
            {
                yield return ParseEvent(childNode.InnerXml);
            }
        }

        private CalDavEvent ParseEvent(string vCalendarString)
        {
            var matches = new[]
                          {
                              Regex.Match(vCalendarString, "(\\/calendars.*.ics)"),
                              Regex.Match(vCalendarString, "DESCRIPTION:(.*)"),
                              Regex.Match(vCalendarString, "UID:(.*)")
                          };
            if (matches.Any(x => x.Success == false))
            {
                return null;
            }

            var relativeUri = new Uri(matches[0].Groups[1].Value, UriKind.Relative);
            // DateTime.TryParseExact(matches[1].Groups[1].Value,
            //                        "yyyy''MM''ddTHH''mm''ssZ",
            //                        CultureInfo.InvariantCulture,
            //                        DateTimeStyles.AssumeUniversal,
            //                        out var start);
            // var location = matches[2].Groups[1].Value;
            // var summary = matches[3].Groups[1].Value;
            var description = matches[1].Groups[1].Value;
            var calDavIdentifier = matches[2].Groups[1].Value;

            return new CalDavEvent(relativeUri, description, calDavIdentifier);
        }

        private Uri FindDynamoFolder()
        {
            return new Uri(new Uri(CalendarUri), GetCalendars().SingleOrDefault(x => x.Name == "Dynamo").UrlSegment);
        }
    }
}