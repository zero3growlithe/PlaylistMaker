using System;
using System.IO;
using System.Windows;

namespace PlaylistMaker.Models
{
    /// <summary>
    /// One line of the "generate ID3 tags from file names" window: the file name the user reads
    /// (and may copy), the search term that picks the tracks to fill in, and the artist and album
    /// boxes whose contents are written wherever that term turns up.
    /// </summary>
    public sealed class TagEditRow
    {
        // these match the Width of the search term, artist and album boxes in GenerateTagsWindow.xaml
        public const double SearchTermColumnPixels = 200.0;

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

        /// <summary>
        /// Text the matching looks for in the file and folder names. Empty means this file's own name,
        /// so a row you type nothing into only fills in the track it belongs to.
        /// </summary>
        public string SearchTerm { get; set; } = string.Empty;

        /// <summary>Artist to write wherever the search term turns up; empty leaves the artist alone.</summary>
        public string Artist { get; set; } = string.Empty;

        /// <summary>Album to write wherever the search term turns up; empty leaves the album alone.</summary>
        public string Album { get; set; } = string.Empty;

        public Visibility ArtistVisibility => showArtist ? Visibility.Visible : Visibility.Collapsed;

        public Visibility AlbumVisibility => showAlbum ? Visibility.Visible : Visibility.Collapsed;

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
