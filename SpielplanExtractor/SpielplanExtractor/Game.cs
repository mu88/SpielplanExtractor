using System;

namespace SpielplanExtractor
{
    internal class Game
    {
        public Game(DateTime dateTime,
                    string location,
                    string opponent,
                    string identifier)
        {
            Date = dateTime;
            Location = location;
            Opponent = opponent;
            Identifier = identifier;
        }

        public DateTime Date { get; }

        public string Location { get; }

        public string Opponent { get; }

        public string Identifier { get; }
    }
}