namespace PlaylistMaker.Models
{
    /// <summary>How the tracks of a "save as playlist" operation get ordered.</summary>
    public enum PlaylistSortMode
    {
        TrackNumberFromTags,
        FileNameAscending,
        FileNameDescending,
        TrackNameAscending,
        TrackNameDescending,
        Random
    }
}
