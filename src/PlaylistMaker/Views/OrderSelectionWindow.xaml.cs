using System.Windows;
using PlaylistMaker.Models;

namespace PlaylistMaker.Views
{
    /// <summary>Picks the ordering used when a folder or a selection is written out as a new playlist.</summary>
    public partial class OrderSelectionWindow : Window
    {
        public OrderSelectionWindow()
        {
            InitializeComponent();
        }

        public PlaylistSortMode SelectedMode
        {
            get
            {
                if (FileNameAscendingRadio.IsChecked == true)
                {
                    return PlaylistSortMode.FileNameAscending;
                }
                if (FileNameDescendingRadio.IsChecked == true)
                {
                    return PlaylistSortMode.FileNameDescending;
                }
                if (TrackNameAscendingRadio.IsChecked == true)
                {
                    return PlaylistSortMode.TrackNameAscending;
                }
                if (TrackNameDescendingRadio.IsChecked == true)
                {
                    return PlaylistSortMode.TrackNameDescending;
                }
                if (RandomRadio.IsChecked == true)
                {
                    return PlaylistSortMode.Random;
                }
                return PlaylistSortMode.TrackNumberFromTags;
            }
        }

        public static PlaylistSortMode? Ask(Window owner)
        {
            OrderSelectionWindow window = new OrderSelectionWindow();
            window.Owner = owner;
            if (window.ShowDialog() != true)
            {
                return null;
            }
            return window.SelectedMode;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
