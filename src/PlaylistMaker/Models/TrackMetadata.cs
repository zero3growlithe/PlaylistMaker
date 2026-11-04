namespace PlaylistMaker.Models
{
    /// <summary>What we can learn about a track without decoding the audio.</summary>
    public sealed class TrackMetadata
    {
        public TrackMetadata(string? title, int trackNumber, bool fromTags, string? album = null, string? artist = null)
        {
            Title = title;
            TrackNumber = trackNumber;
            FromTags = fromTags;
            Album = album;
            Artist = artist;
        }

        public string? Title { get; }

        public int TrackNumber { get; }

        /// <summary>True when the values came from real tags, false when guessed from the file name.</summary>
        public bool FromTags { get; }

        public string? Album { get; }

        public string? Artist { get; }
    }
}
