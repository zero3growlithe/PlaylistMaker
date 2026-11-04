using Microsoft.Win32;
using System.Windows;

namespace PlaylistMaker.Views
{
    /// <summary>Lets the user point a broken playlist entry at a real file, or at a folder to search.</summary>
    public partial class MissingFileWindow : Window
    {
        public MissingFileWindow(string missingPath)
        {
            InitializeComponent();
            MissingPathBox.Text = missingPath;
        }

        public string? SelectedFolder { get; private set; }

        public string? SelectedFile { get; private set; }

        private void OnSelectFolderClick(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog
            {
                Title = "Pick the folder that contains the missing file",
                Multiselect = false
            };
            bool? result = dialog.ShowDialog(this);
            if (result != true || string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                return;
            }
            SelectedFolder = dialog.FolderName;
            DialogResult = true;
        }

        private void OnSelectFileClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Pick the file",
                CheckFileExists = true,
                Multiselect = false,
                Filter = "All files (*.*)|*.*"
            };
            bool? result = dialog.ShowDialog(this);
            if (result != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }
            SelectedFile = dialog.FileName;
            DialogResult = true;
        }
    }
}
