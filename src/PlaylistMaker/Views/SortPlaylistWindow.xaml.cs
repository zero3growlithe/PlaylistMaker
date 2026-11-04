using System;
using System.Windows;
using PlaylistMaker.Models;

namespace PlaylistMaker.Views
{
    /// <summary>Asks how the current playlist should be ordered: a field, a direction, or shuffle.</summary>
    public partial class SortPlaylistWindow : Window
    {
        private const int ShuffleIndex = (int)PlaylistSortField.Shuffle;

        public SortPlaylistWindow()
        {
            InitializeComponent();
            FieldBox.SelectedIndex = (int)PlaylistSortField.TrackNumberFromTags;
            FieldBox.SelectionChanged += OnFieldChanged;
        }

        public PlaylistSortField SelectedField { get; private set; } = PlaylistSortField.TrackNumberFromTags;

        public bool Descending { get; private set; }

        public static bool Ask(Window owner, out PlaylistSortField field, out bool descending)
        {
            SortPlaylistWindow window = new SortPlaylistWindow();
            window.Owner = owner;
            if (window.ShowDialog() != true)
            {
                field = PlaylistSortField.TrackNumberFromTags;
                descending = false;
                return false;
            }

            field = window.SelectedField;
            descending = window.Descending;
            return true;
        }

        private void OnFieldChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            bool isShuffle = FieldBox.SelectedIndex == ShuffleIndex;
            AscendingRadio.IsEnabled = !isShuffle;
            DescendingRadio.IsEnabled = !isShuffle;
        }

        private void OnSortClick(object sender, RoutedEventArgs e)
        {
            int selectedIndex = FieldBox.SelectedIndex;
            if (selectedIndex < 0)
            {
                selectedIndex = 0;
            }
            if (selectedIndex > ShuffleIndex)
            {
                selectedIndex = ShuffleIndex;
            }

            SelectedField = (PlaylistSortField)selectedIndex;
            Descending = DescendingRadio.IsChecked == true;
            DialogResult = true;
        }
    }
}
