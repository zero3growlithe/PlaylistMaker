using System.Windows;

namespace PlaylistMaker.Views
{
    /// <summary>What the user wants a "generate tags from file names" run to do.</summary>
    public sealed class GenerateTagsOptions
    {
        public GenerateTagsOptions(bool fillArtist, bool fillAlbum, bool skipWhenArtistPresent, bool skipWhenAlbumPresent)
        {
            FillArtist = fillArtist;
            FillAlbum = fillAlbum;
            SkipWhenArtistPresent = skipWhenArtistPresent;
            SkipWhenAlbumPresent = skipWhenAlbumPresent;
        }

        public bool FillArtist { get; }

        public bool FillAlbum { get; }

        public bool SkipWhenArtistPresent { get; }

        public bool SkipWhenAlbumPresent { get; }
    }

    /// <summary>
    /// Asks which tags to fill in and whether tracks that already carry an artist or an album
    /// should be left out, before the per-track window opens.
    /// </summary>
    public partial class GenerateTagsOptionsWindow : Window
    {
        public GenerateTagsOptionsWindow(int trackCount)
        {
            InitializeComponent();
            HeaderText.Text = "Found " + trackCount + " track" + (trackCount == 1 ? string.Empty : "s")
                + " to tag from their file names.";
        }

        /// <summary>The chosen options, or null when the user cancelled.</summary>
        public static GenerateTagsOptions? Ask(Window owner, int trackCount)
        {
            GenerateTagsOptionsWindow window = new GenerateTagsOptionsWindow(trackCount);
            window.Owner = owner;
            if (window.ShowDialog() != true)
            {
                return null;
            }
            return new GenerateTagsOptions(
                window.FillArtistCheck.IsChecked == true,
                window.FillAlbumCheck.IsChecked == true,
                window.SkipArtistCheck.IsChecked == true,
                window.SkipAlbumCheck.IsChecked == true);
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (FillArtistCheck.IsChecked != true && FillAlbumCheck.IsChecked != true)
            {
                MessageBox.Show(this, "Pick at least one tag to fill in: the artist, the album, or both.",
                    "Generate ID3 tags", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
        }
    }
}
