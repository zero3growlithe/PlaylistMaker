using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using PlaylistMaker.Services;

namespace PlaylistMaker.Views
{
    /// <summary>
    /// Asks for a search term plus the artist and album one file at a time, showing the whole path so
    /// the file can be copied. Accepting a file writes the typed values into every queued file whose
    /// name contains the search term, and those leave the queue - so a folder of albums is usually
    /// done in a handful of accepts instead of one row per track.
    /// </summary>
    public partial class GenerateTagsOneByOneWindow : Window
    {
        private readonly List<string> queue;
        private readonly List<TagFillPlan> plans = new List<TagFillPlan>();
        private readonly GenerateTagsOptions options;
        private readonly int totalCount;

        private string currentPath = string.Empty;
        private bool finished;

        public GenerateTagsOneByOneWindow(IReadOnlyList<string> filePaths, GenerateTagsOptions options)
        {
            InitializeComponent();
            this.options = options;
            queue = new List<string>(filePaths);
            totalCount = queue.Count;

            ArtistRow.Visibility = options.FillArtist ? Visibility.Visible : Visibility.Collapsed;
            AlbumRow.Visibility = options.FillAlbum ? Visibility.Visible : Visibility.Collapsed;

            Loaded += OnLoaded;
        }

        /// <summary>The plans the user accepted, or null when the window was cancelled.</summary>
        public static List<TagFillPlan>? Ask(Window owner, IReadOnlyList<string> filePaths, GenerateTagsOptions options)
        {
            GenerateTagsOneByOneWindow window = new GenerateTagsOneByOneWindow(filePaths, options);
            window.Owner = owner;
            if (window.ShowDialog() != true)
            {
                return null;
            }
            return window.plans;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ShowNext();
        }

        /// <summary>Shows the first file still in the queue, or closes the window when there is none.</summary>
        private void ShowNext()
        {
            if (queue.Count == 0)
            {
                Finish();
                return;
            }

            currentPath = queue[0];
            PathBox.Text = currentPath;
            FileNameText.Text = Path.GetFileName(currentPath) is { Length: > 0 } fileName ? fileName : currentPath;
            SearchTermBox.Text = string.Empty;
            ArtistBox.Text = string.Empty;
            AlbumBox.Text = string.Empty;

            int decided = totalCount - queue.Count;
            ProgressText.Text = decided + " of " + totalCount + " decided, " + queue.Count
                + (queue.Count == 1 ? " file still to go" : " files still to go")
                + " - " + plans.Count + (plans.Count == 1 ? " track lined up for writing." : " tracks lined up for writing.");

            SearchTermBox.Focus();
        }

        private void OnAcceptClick(object sender, RoutedEventArgs e)
        {
            string? searchTerm = SearchTermBox.Text;
            string? artist = options.FillArtist ? ArtistBox.Text : null;
            string? album = options.FillAlbum ? AlbumBox.Text : null;
            if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(album))
            {
                MessageBox.Show(this,
                    "Type an artist or an album first, or drop this file with Skip file / Skip folder.",
                    "Generate ID3 tags", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            List<TagFillPlan> applied = TagFillPlanner.ApplyAcceptedEntry(
                queue,
                currentPath,
                searchTerm,
                artist,
                album,
                options.IncludeFolderNames,
                options.AllFolderLevels);
            plans.AddRange(applied);
            ShowNext();
        }

        private void OnSkipFileClick(object sender, RoutedEventArgs e)
        {
            queue.Remove(currentPath);
            ShowNext();
        }

        /// <summary>Drops every file that sits in the same folder as the current one.</summary>
        private void OnSkipFolderClick(object sender, RoutedEventArgs e)
        {
            string folder = GetFolderPath(currentPath);
            if (folder.Length == 0)
            {
                queue.Remove(currentPath);
            }
            else
            {
                queue.RemoveAll(path => string.Equals(GetFolderPath(path), folder, StringComparison.OrdinalIgnoreCase));
            }
            ShowNext();
        }

        private void OnSkipAllClick(object sender, RoutedEventArgs e)
        {
            Finish();
        }

        /// <summary>Closes the window with everything accepted so far; the caller writes those tags.</summary>
        private void Finish()
        {
            if (finished)
            {
                return;
            }
            finished = true;
            DialogResult = true;
        }

        private static string GetFolderPath(string filePath)
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
