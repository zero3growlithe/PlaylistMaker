using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using PlaylistMaker.Services;

namespace PlaylistMaker.Views
{
    public enum SaveEachAction
    {
        Save,
        Skip,
        CancelAll
    }

    /// <summary>Outcome of one step of "save each folder as playlist".</summary>
    public sealed class SaveEachPlaylistResult
    {
        public SaveEachPlaylistResult(SaveEachAction action, string? targetPath)
        {
            Action = action;
            TargetPath = targetPath;
        }

        public SaveEachAction Action { get; }

        public string? TargetPath { get; }
    }

    /// <summary>One step of "save each folder as playlist": name the file, skip the folder, or abort the run.</summary>
    public partial class SaveEachPlaylistWindow : Window
    {
        private readonly string targetDirectory;

        public SaveEachPlaylistWindow(int index, int total, string folderName, int trackCount,
                                      string targetDirectory, string suggestedFileName)
        {
            InitializeComponent();
            this.targetDirectory = targetDirectory;
            HeaderText.Text = "Folder " + index + " of " + total + ": " + folderName;
            DetailText.Text = trackCount + " track" + (trackCount == 1 ? string.Empty : "s")
                + " will be written as one playlist.";
            FolderText.Text = "Saved next to the music in: " + targetDirectory;
            FileNameBox.Text = suggestedFileName;
        }

        public SaveEachAction ResultAction { get; private set; } = SaveEachAction.CancelAll;

        public string? SelectedPath { get; private set; }

        public static SaveEachPlaylistResult Ask(Window owner, int index, int total, string folderName,
                                                 int trackCount, string targetDirectory, string suggestedFileName)
        {
            SaveEachPlaylistWindow window = new SaveEachPlaylistWindow(
                index, total, folderName, trackCount, targetDirectory, suggestedFileName);
            window.Owner = owner;
            window.ShowDialog();
            return new SaveEachPlaylistResult(window.ResultAction, window.SelectedPath);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            string typed = (FileNameBox.Text ?? string.Empty).Trim();
            if (typed.Length == 0)
            {
                MessageBox.Show(this, "Enter a file name.", "Playlist Maker",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string fileName = typed;
            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, "That file name contains characters Windows does not allow.",
                    "Playlist Maker", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!MusicFormats.IsPlaylistFile(fileName))
            {
                fileName += ".m3u8";
            }

            string fullPath;
            try
            {
                fullPath = Path.IsPathRooted(fileName) ? fileName : Path.Combine(targetDirectory, fileName);
            }
            catch (ArgumentException)
            {
                MessageBox.Show(this, "That path is not valid.", "Playlist Maker",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (File.Exists(fullPath))
            {
                MessageBoxResult overwrite = MessageBox.Show(
                    this,
                    "The file already exists:\n\n" + fullPath + "\n\nOverwrite it?",
                    "Playlist Maker",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (overwrite != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            SelectedPath = fullPath;
            ResultAction = SaveEachAction.Save;
            DialogResult = true;
        }

        private void OnSkipClick(object sender, RoutedEventArgs e)
        {
            ResultAction = SaveEachAction.Skip;
            DialogResult = false;
        }

        private void OnCancelAllClick(object sender, RoutedEventArgs e)
        {
            ResultAction = SaveEachAction.CancelAll;
            DialogResult = false;
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Save playlist as",
                Filter = MusicFormats.PlaylistSaveFilter,
                DefaultExt = ".m3u8",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = FileNameBox.Text,
                InitialDirectory = targetDirectory
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }
            SelectedPath = dialog.FileName;
            ResultAction = SaveEachAction.Save;
            DialogResult = true;
        }
    }
}
