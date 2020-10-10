using System;

namespace SpielplanExtractor
{
    internal class CalDavEvent
    {
        public CalDavEvent(Uri relativeUri, string description, string calDavIdentifier)
        {
            RelativeUri = relativeUri;
            Description = description;
            CalDavIdentifier = calDavIdentifier;
        }

        public string Description { get; }

        public Uri RelativeUri { get; }

        public string CalDavIdentifier { get; }
    }
}