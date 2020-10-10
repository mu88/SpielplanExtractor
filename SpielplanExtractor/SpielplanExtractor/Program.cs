using System;
using System.Threading.Tasks;

namespace SpielplanExtractor
{
    internal static class Program
    {
        private static async Task Main()
        {
            Console.WriteLine("Enter your username:");
            var username = Console.ReadLine(); // e. g. 'bla@bla.onmicrosoft.com'

            Console.WriteLine("Enter your password:");
            var password = Console.ReadLine();

            var enteredPlatform = "Posteo"; // 'Posteo' or 'Exchange'
            if (!Enum.TryParse<Platform>(enteredPlatform, out var platform))
            {
                throw new ArgumentOutOfRangeException();
            }

            IAccount account;
            switch (platform)
            {
                case Platform.Posteo:
                    account = new PosteoAccount(username, password);
                    break;
                case Platform.Exchange:
                    account = new ExchangeAccount(username, password);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var seasonParser = new KickerSeasonParser();
            var season = seasonParser.ConstructSeason();

            await account.SetUpAppointmentsAsync(season);
        }
    }
}