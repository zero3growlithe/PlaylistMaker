using System.Windows;

namespace PlaylistMaker.Views
{
    /// <summary>What the user wants a "generate tags from file names" run to do.</summary>
    public sealed class GenerateTagsOptions
    {
        public GenerateTagsOptions(
            bool fillArtist,
            bool fillAlbum,
            bool includeFolderNames,
            bool allFolderLevels,
            bool oneFileAtATime,
            bool skipWhenArtistPresent,
            bool skipWhenAlbumPresent)
        {
            FillArtist = fillArtist;
            FillAlbum = fillAlbum;
            IncludeFolderNames = includeFolderNames;
            AllFolderLevels = allFolderLevels;
            OneFileAtATime = oneFileAtATime;
            SkipWhenArtistPresent = skipWhenArtistPresent;
            SkipWhenAlbumPresent = skipWhenAlbumPresent;
        }

        public bool FillArtist { get; }

        public bool FillAlbum { get; }

        /// <summary>True when the typed names may also match the names of the folders.</summary>
        public bool IncludeFolderNames { get; }

        /// <summary>True when every folder above the track counts, false for the folder that holds it.</summary>
        public bool AllFolderLevels { get; }

        /// <summary>True when the names should be asked for one file at a time instead of in one list.</summary>
        public bool OneFileAtATime { get; }

        public bool SkipWhenArtistPresent { get; }

        public bool SkipWhenAlbumPresent { get; }
    }

    /// <summary>
    /// Asks which tags to fill in, where the typed names are looked for, and whether tracks that
    /// already carry an artist or an album should be left out, before the per-track window opens.
    /// </summary>
    public partial class GenerateTagsOptionsWindow : Window
    {
        public GenerateTagsOptionsWindow(int trackCount)
        {
            InitializeComponent();
            HeaderText.Text = "Found " + trackCount + " track" + (trackCount == 1 ? string.Empty : "s")
                + " to tag from their file names.";
            ApplyFolderLevelState();
        }

        /// <summary>True when the "one file at a time" button closed the window.</summary>
        public bool OneFileAtATime { get; private set; }

        /// <summary>The chosen options, or null when the user cancelled.</summary>
        public static GenerateTagsOptions? Ask(Window owner, int trackCount)
        {
            GenerateTagsOptionsWindow window = new GenerateTagsOptionsWindow(trackCount);
            window.Owner = owner;
            if (window.ShowDialog() != true)
            {
                return null;
            }

            bool includeFolderNames = window.MatchFoldersCheck.IsChecked == true;
            return new GenerateTagsOptions(
                window.FillArtistCheck.IsChecked == true,
                window.FillAlbumCheck.IsChecked == true,
                includeFolderNames,
                includeFolderNames && window.FolderLevelAllRadio.IsChecked == true,
                window.OneFileAtATime,
                window.SkipArtistCheck.IsChecked == true,
                window.SkipAlbumCheck.IsChecked == true);
        }

        private void OnMatchFoldersChanged(object sender, RoutedEventArgs e)
        {
            ApplyFolderLevelState();
        }

        /// <summary>The one level / all levels choice only means something once folder names are used.</summary>
        private void ApplyFolderLevelState()
        {
            bool useFolders = MatchFoldersCheck.IsChecked == true;
            FolderLevelOneRadio.IsEnabled = useFolders;
            FolderLevelAllRadio.IsEnabled = useFolders;
        }

        private void OnAllFilesClick(object sender, RoutedEventArgs e)
        {
            if (!ValidateTagChoice())
            {
                return;
            }
            OneFileAtATime = false;
            DialogResult = true;
        }

        private void OnOneFileAtATimeClick(object sender, RoutedEventArgs e)
        {
            if (!ValidateTagChoice())
            {
                return;
            }
            OneFileAtATime = true;
            DialogResult = true;
        }

        private bool ValidateTagChoice()
        {
            if (FillArtistCheck.IsChecked == true || FillAlbumCheck.IsChecked == true)
            {
                return true;
            }

            MessageBox.Show(this, "Pick at least one tag to fill in: the artist, the album, or both.",
                "Generate ID3 tags", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
    }
}
