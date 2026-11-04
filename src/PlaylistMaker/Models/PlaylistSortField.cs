namespace PlaylistMaker.Models
{
    /// <summary>What the "Sort" button can order the current playlist by. Order matches the dialog list.</summary>
    public enum PlaylistSortField
    {
        TrackNumberFromTags,
        FileName,
        TrackName,
        FileSize,
        TrackLength,
        AlbumName,
        ArtistName,
        Shuffle
    }
}
