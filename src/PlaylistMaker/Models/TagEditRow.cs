using System;
using System.IO;
using System.Windows;

namespace PlaylistMaker.Models
{
    /// <summary>
    /// One line of the "generate ID3 tags from file names" window: the file name the user reads
    /// (and may copy) plus the artist and album boxes they type into.
    /// </summary>
    public sealed class TagEditRow
    {
        public const double ArtistColumnPixels = 240.0;

        public const double AlbumColumnPixels = 240.0;

        private readonly bool showArtist;
        private readonly bool showAlbum;

        public TagEditRow(string filePath, bool showArtist, bool showAlbum)
        {
            FilePath = filePath ?? string.Empty;
            this.showArtist = showArtist;
            this.showAlbum = showAlbum;
            FileName = BuildFileName(FilePath);
            FolderPath = BuildFolderPath(FilePath);
        }

        public string FilePath { get; }

        /// <summary>The file name without the folder, which is the text the matching runs against.</summary>
        public string FileName { get; }

        public string FolderPath { get; }

        /// <summary>Artist typed by the user; empty leaves this track's artist alone.</summary>
        public string Artist { get; set; } = string.Empty;

        /// <summary>Album typed by the user; empty leaves this track's album alone.</summary>
        public string Album { get; set; } = string.Empty;

        public Visibility ArtistVisibility => showArtist ? Visibility.Visible : Visibility.Collapsed;

        public Visibility AlbumVisibility => showAlbum ? Visibility.Visible : Visibility.Collapsed;

        public GridLength ArtistColumnWidth =>
            new GridLength(showArtist ? ArtistColumnPixels : 0.0, GridUnitType.Pixel);

        public GridLength AlbumColumnWidth =>
            new GridLength(showAlbum ? AlbumColumnPixels : 0.0, GridUnitType.Pixel);

        private static string BuildFileName(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return string.Empty;
            }
            try
            {
                string name = Path.GetFileName(filePath);
                return string.IsNullOrEmpty(name) ? filePath : name;
            }
            catch (ArgumentException)
            {
                return filePath;
            }
        }

        private static string BuildFolderPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return string.Empty;
            }
            try
            {
                return Path.GetDirectoryName(filePath) ?? string.Empty;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }
    }
}
