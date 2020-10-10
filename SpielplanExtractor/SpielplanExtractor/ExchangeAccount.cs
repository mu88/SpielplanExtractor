using System;
using System.Linq;
using System.Net;
using Microsoft.Exchange.WebServices.Data;
using Task = System.Threading.Tasks.Task;

namespace SpielplanExtractor
{
    internal class ExchangeAccount : IAccount
    {
        public ExchangeAccount(string username, string password)
        {
            Service = GetService(username, password);
        }

        private ExchangeService Service { get; }

        /// <inheritdoc />
        public Task SetUpAppointmentsAsync(Season season)
        {
            return Task.Run(() =>
            {
                var dynamoFolderId = FindDynamoCalendar(Service);
                var existingAppointments = LoadExistingAppointments(season, Service, dynamoFolderId);

                foreach (var game in season.Games)
                {
                    // ignore games that have already been played
                    var isInThePast = game.Date < DateTime.Now.Date;
                    if (isInThePast)
                    {
                        continue;
                    }

                    Appointment appointment;
                    var isNewAppointment = false;
                    var existingAppointment = existingAppointments.FirstOrDefault(x => x.Body.ToString().Contains(game.Identifier));
                    if (existingAppointment == null)
                    {
                        appointment = new Appointment(Service)
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
            });
        }

        private static FindItemsResults<Appointment> LoadExistingAppointments(Season season,
                                                                              ExchangeService service,
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

        private static bool RedirectionCallback(string url)
        {
            return url.ToLower().StartsWith("https://");
        }

        private static ExchangeService GetService(string userEmailAddress, string userPassword)
        {
            var service = new ExchangeService { Credentials = new NetworkCredential(userEmailAddress, userPassword) };

            // Look up the user's EWS endpoint by using Autodiscover.
            service.AutodiscoverUrl(userEmailAddress, RedirectionCallback);

            return service;
        }

        private static FolderId FindDynamoCalendar(ExchangeService service)
        {
            var view = new FolderView(20)
                       {
                           PropertySet = new PropertySet(BasePropertySet.IdOnly) { FolderSchema.DisplayName },
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