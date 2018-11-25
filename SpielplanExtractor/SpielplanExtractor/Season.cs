using System;
using System.Collections.ObjectModel;

namespace SpielplanExtractor
{
    internal class Season
    {
        public Season(string seasonString)
        {
            Games = new Collection<Game>();

            var seasonStrings = seasonString.Split('/');
            StartYear = Convert.ToInt32(seasonStrings[0]);
            EndYear = Convert.ToInt32(seasonStrings[1]);
        }

        public int EndYear { get; }

        public int StartYear { get; }

        public Collection<Game> Games { get; }
    }
}