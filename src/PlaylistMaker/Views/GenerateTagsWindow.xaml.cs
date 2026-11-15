using System;
using System.Collections.Generic;
using System.Windows;
using PlaylistMaker.Models;

namespace PlaylistMaker.Views
{
    /// <summary>
    /// One line per track: the file name (read only, selectable so it can be copied) and the boxes
    /// the user types the artist and album into.
    /// </summary>
    public partial class GenerateTagsWindow : Window
    {
        private readonly List<TagEditRow> rows;

        public GenerateTagsWindow(IReadOnlyList<string> filePaths, bool showArtist, bool showAlbum)
        {
            InitializeComponent();

            rows = new List<TagEditRow>();
            foreach (string filePath in filePaths)
            {
                rows.Add(new TagEditRow(filePath, showArtist, showAlbum));
            }

            string trackCount = rows.Count + " track" + (rows.Count == 1 ? string.Empty : "s");
            if (showArtist && showAlbum)
            {
                HeaderText.Text = "Type the artist and album into the boxes next to the " + trackCount + " below.";
            }
            else if (showArtist)
            {
                HeaderText.Text = "Type the artist into the boxes next to the " + trackCount + " below.";
            }
            else
            {
                HeaderText.Text = "Type the album into the boxes next to the " + trackCount + " below.";
            }

            // the list reserves room for its scroll bar, so the captions have to as well
            HeaderGrid.Margin = new Thickness(0, 0, SystemParameters.VerticalScrollBarWidth + 6, 4);
            ArtistHeaderColumn.Width = new GridLength(showArtist ? TagEditRow.ArtistColumnPixels : 0.0, GridUnitType.Pixel);
            AlbumHeaderColumn.Width = new GridLength(showAlbum ? TagEditRow.AlbumColumnPixels : 0.0, GridUnitType.Pixel);
            ArtistHeader.Visibility = showArtist ? Visibility.Visible : Visibility.Collapsed;
            AlbumHeader.Visibility = showAlbum ? Visibility.Visible : Visibility.Collapsed;

            RowList.ItemsSource = rows;
            Loaded += OnLoaded;
        }

        /// <summary>The rows with what the user typed, or null when the window was cancelled.</summary>
        public static List<TagEditRow>? Ask(Window owner, IReadOnlyList<string> filePaths, bool showArtist, bool showAlbum)
        {
            GenerateTagsWindow window = new GenerateTagsWindow(filePaths, showArtist, showAlbum);
            window.Owner = owner;
            if (window.ShowDialog() != true)
            {
                return null;
            }
            return window.rows;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (rows.Count > 0)
            {
                RowList.ScrollIntoView(rows[0]);
            }
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
