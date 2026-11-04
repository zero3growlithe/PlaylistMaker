using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PlaylistMaker.Models;
using PlaylistMaker.Services;
using PlaylistMaker.ViewModels;
using PlaylistMaker.Views;

namespace PlaylistMaker
{
    public partial class MainWindow : Window
    {
        private const string PlaylistEntryFormat = "PlaylistMaker.PlaylistEntry";
        private const double DragThreshold = 6.0;

        private readonly MainViewModel viewModel = new MainViewModel();
        private Point explorerDragStart;
        private Point playlistDragStart;
        private bool isExplorerDragPending;
        private bool isPlaylistDragPending;

        public MainWindow()
        {
            InitializeComponent();
            ExplorerList.ItemsSource = viewModel.VisibleNodes;
            PlaylistList.ItemsSource = viewModel.PlaylistEntries;
            viewModel.Initialize();
            UpdateSummary("Ready.");
        }

        // ── Toolbar ───────────────────────────────────────────────────────
        private void OnShowAllFilesChanged(object sender, RoutedEventArgs e)
        {
            viewModel.ShowAllFiles = ShowAllFilesCheckBox.IsChecked == true;
            UpdateSummary(viewModel.ShowAllFiles
                ? "Showing every file in the tree."
                : "Tree filtered to music and playlists.");
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            viewModel.RefreshEntryStates();
            viewModel.ReloadTree();
            UpdateSummary("Refreshed.");
        }

        // ── Explorer tree ─────────────────────────────────────────────────
        private void OnChevronClick(object sender, RoutedEventArgs e)
        {
            Button? button = sender as Button;
            FileSystemNode? node = button?.Tag as FileSystemNode;
            if (node == null)
            {
                return;
            }

            bool altHeld = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
            viewModel.ToggleExpand(node, altHeld);
            e.Handled = true;
        }

        private void OnExplorerMouseDown(object sender, MouseButtonEventArgs e)
        {
            explorerDragStart = e.GetPosition(ExplorerList);
            isExplorerDragPending = true;
        }

        private void OnExplorerMouseMove(object sender, MouseEventArgs e)
        {
            if (!isExplorerDragPending || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point current = e.GetPosition(ExplorerList);
            if (Math.Abs(current.X - explorerDragStart.X) < DragThreshold
                && Math.Abs(current.Y - explorerDragStart.Y) < DragThreshold)
            {
                return;
            }

            List<FileSystemNode> selected = GetSelectedNodes();
            if (selected.Count == 0)
            {
                return;
            }

            string[] paths = selected.Select(node => node.FullPath).ToArray();
            isExplorerDragPending = false;
            DataObject data = new DataObject();
            data.SetData(DataFormats.FileDrop, paths);
            DragDrop.DoDragDrop(ExplorerList, data, DragDropEffects.Copy);
        }

        private void OnExplorerRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            ListBoxItem? item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item == null)
            {
                return;
            }
            if (!item.IsSelected)
            {
                ExplorerList.SelectedItems.Clear();
                item.IsSelected = true;
            }
        }

        private void OnExplorerContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            List<FileSystemNode> selected = GetSelectedNodes();
            if (selected.Count == 0)
            {
                e.Handled = true;
                return;
            }

            bool containsFolder = selected.Any(node => node.IsFolder);
            bool containsMusic = selected.Any(node => node.IsMusic);
            bool showSaveAsPlaylist = containsFolder || (containsMusic && selected.Count > 1);

            ContextMenu menu = new ContextMenu();
            MenuItem addItem = new MenuItem { Header = "Add to playlist" };
            addItem.Click += OnAddSelectedToPlaylistClick;
            menu.Items.Add(addItem);

            if (showSaveAsPlaylist)
            {
                MenuItem saveItem = new MenuItem { Header = "Save as playlist..." };
                saveItem.Click += OnSaveSelectedAsPlaylistClick;
                menu.Items.Add(saveItem);
            }

            menu.Items.Add(new Separator());
            MenuItem explorerItem = new MenuItem { Header = "View in Explorer" };
            explorerItem.Click += OnViewSelectedInExplorerClick;
            menu.Items.Add(explorerItem);

            ExplorerList.ContextMenu = menu;
        }

        private void OnAddSelectedToPlaylistClick(object sender, RoutedEventArgs e)
        {
            List<string> paths = CollectPathsFromSelection(GetSelectedNodes(), true);
            if (paths.Count == 0)
            {
                return;
            }
            int added = viewModel.AddPaths(paths);
            UpdateSummary("Added " + added + " entr" + (added == 1 ? "y" : "ies") + " to the playlist.");
        }

        private void OnSaveSelectedAsPlaylistClick(object sender, RoutedEventArgs e)
        {
            SaveSelectionAsPlaylist();
        }

        private void OnViewSelectedInExplorerClick(object sender, RoutedEventArgs e)
        {
            List<FileSystemNode> selected = GetSelectedNodes();
            if (selected.Count == 0)
            {
                return;
            }
            PathUtilities.ShowInExplorer(selected[0].FullPath);
        }

        // ── Playlist panel ────────────────────────────────────────────────
        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            SavePlaylistWithDialog();
        }

        private void OnLoadClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Load playlist",
                Filter = MusicFormats.PlaylistDialogFilter,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }
            viewModel.LoadPlaylist(dialog.FileName);
            UpdateSummary("Loaded " + dialog.FileName + ".");
        }

        private void OnViewInExplorerClick(object sender, RoutedEventArgs e)
        {
            ViewSelectedPlaylistEntriesInExplorer();
        }

        private void OnUndoClick(object sender, RoutedEventArgs e)
        {
            if (viewModel.Undo())
            {
                UpdateSummary("Undo.");
            }
        }

        private void OnRedoClick(object sender, RoutedEventArgs e)
        {
            if (viewModel.Redo())
            {
                UpdateSummary("Redo.");
            }
        }

        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            RemoveSelectedEntries();
        }

        private void OnPlaylistMouseDown(object sender, MouseButtonEventArgs e)
        {
            playlistDragStart = e.GetPosition(PlaylistList);
            isPlaylistDragPending = true;
        }

        private void OnPlaylistMouseMove(object sender, MouseEventArgs e)
        {
            if (!isPlaylistDragPending || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point current = e.GetPosition(PlaylistList);
            if (Math.Abs(current.X - playlistDragStart.X) < DragThreshold
                && Math.Abs(current.Y - playlistDragStart.Y) < DragThreshold)
            {
                return;
            }

            List<PlaylistEntry> selected = GetSelectedEntries();
            if (selected.Count == 0)
            {
                return;
            }

            isPlaylistDragPending = false;
            DataObject data = new DataObject();
            data.SetData(PlaylistEntryFormat, selected);
            data.SetData(DataFormats.FileDrop, selected.Select(entry => entry.FullPath).ToArray());
            DragDrop.DoDragDrop(PlaylistList, data, DragDropEffects.Move | DragDropEffects.Copy);
        }

        private void OnPlaylistDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            if (e.Data.GetDataPresent(PlaylistEntryFormat))
            {
                e.Effects = DragDropEffects.Move;
            }
            e.Handled = true;
        }

        private void OnPlaylistDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(PlaylistEntryFormat))
            {
                object? payload = e.Data.GetData(PlaylistEntryFormat);
                if (payload is IEnumerable<PlaylistEntry> entries)
                {
                    List<PlaylistEntry> moving = entries.ToList();
                    int insertIndex = GetPlaylistDropIndex(e);
                    viewModel.ReorderEntries(moving, insertIndex);
                    UpdateSummary("Reordered " + moving.Count + " entr" + (moving.Count == 1 ? "y" : "ies") + ".");
                }
                e.Handled = true;
                return;
            }

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            string[] droppedPaths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
            HandleDroppedPaths(droppedPaths);
            e.Handled = true;
        }

        private void OnPlaylistKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                RemoveSelectedEntries();
                e.Handled = true;
                return;
            }

            bool controlHeld = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (controlHeld && e.Key == Key.Up)
            {
                MoveSelectedEntries(-1);
                e.Handled = true;
                return;
            }
            if (controlHeld && e.Key == Key.Down)
            {
                MoveSelectedEntries(1);
                e.Handled = true;
                return;
            }
            if (!controlHeld && e.Key == Key.E)
            {
                ViewSelectedPlaylistEntriesInExplorer();
                e.Handled = true;
            }
        }

        private void OnFixMissingClick(object sender, RoutedEventArgs e)
        {
            Button? button = sender as Button;
            PlaylistEntry? entry = button?.Tag as PlaylistEntry;
            if (entry == null)
            {
                return;
            }
            FixMissingEntry(entry);
        }

        // ── Window wide shortcuts ─────────────────────────────────────────
        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool controlHeld = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (!controlHeld)
            {
                return;
            }

            if (e.Key == Key.S)
            {
                SavePlaylistWithDialog();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Z)
            {
                if (viewModel.Undo())
                {
                    UpdateSummary("Undo.");
                }
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Y)
            {
                if (viewModel.Redo())
                {
                    UpdateSummary("Redo.");
                }
                e.Handled = true;
            }
        }

        // ── Missing file repair ───────────────────────────────────────────
        private void FixMissingEntry(PlaylistEntry entry)
        {
            MissingFileWindow window = new MissingFileWindow(entry.FullPath);
            window.Owner = this;
            if (window.ShowDialog() != true)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(window.SelectedFile))
            {
                viewModel.RecordUndo();
                entry.PointTo(window.SelectedFile!);
                UpdateSummary("Entry repointed at " + window.SelectedFile + ".");
                return;
            }

            string? folder = window.SelectedFolder;
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            string? match = MissingFileResolver.FindByName(folder!, entry.FullPath, false);
            if (match == null)
            {
                MessageBoxResult searchSubfolders = MessageBox.Show(
                    this,
                    "The file was not found directly in that folder. Search subfolders as well?",
                    "Playlist Maker",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (searchSubfolders != MessageBoxResult.Yes)
                {
                    return;
                }
                match = MissingFileResolver.FindByName(folder!, entry.FullPath, true);
            }

            if (match == null)
            {
                MessageBox.Show(
                    this,
                    "Could not find \"" + Path.GetFileName(entry.FullPath) + "\" inside \"" + folder + "\".",
                    "Playlist Maker",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            viewModel.RecordUndo();
            entry.PointTo(match);

            List<PlaylistEntry> otherMissing = viewModel.PlaylistEntries
                .Where(item => item.IsMissing && !ReferenceEquals(item, entry))
                .ToList();
            if (otherMissing.Count == 0)
            {
                UpdateSummary("Entry repointed at " + match + ".");
                return;
            }

            MessageBoxResult fixOthers = MessageBox.Show(
                this,
                "Also try to fix the other " + otherMissing.Count + " missing entr" + (otherMissing.Count == 1 ? "y" : "ies")
                    + " using this folder?",
                "Playlist Maker",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (fixOthers != MessageBoxResult.Yes)
            {
                UpdateSummary("Entry repointed at " + match + ".");
                return;
            }

            MessageBoxResult recursive = MessageBox.Show(
                this,
                "Scan subfolders recursively while fixing the remaining entries?",
                "Playlist Maker",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            int fixedCount = MissingFileResolver.FixAll(otherMissing, folder!, recursive == MessageBoxResult.Yes);
            UpdateSummary("Repointed " + (fixedCount + 1) + " entr" + (fixedCount == 0 ? "y" : "ies") + "; "
                + (otherMissing.Count - fixedCount) + " still missing.");
        }

        // ── Shared helpers ────────────────────────────────────────────────
        private void SavePlaylistWithDialog()
        {
            List<string> paths = viewModel.SnapshotPaths();
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Save playlist",
                Filter = MusicFormats.PlaylistSaveFilter,
                DefaultExt = ".m3u8",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = string.IsNullOrWhiteSpace(viewModel.CurrentPlaylistPath)
                    ? "playlist.m3u8"
                    : Path.GetFileName(viewModel.CurrentPlaylistPath!)
            };

            string? startDirectory = PathUtilities.GetMutualParentDirectory(paths);
            if (!string.IsNullOrEmpty(startDirectory))
            {
                dialog.InitialDirectory = startDirectory;
            }

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                PlaylistFileService.Save(dialog.FileName, paths);
                viewModel.CurrentPlaylistPath = dialog.FileName;
                UpdateSummary("Saved " + paths.Count + " tracks to " + dialog.FileName + ".");
            }
            catch (IOException exception)
            {
                MessageBox.Show(this, "Could not save the playlist:\n\n" + exception.Message,
                    "Playlist Maker", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (UnauthorizedAccessException exception)
            {
                MessageBox.Show(this, "Could not save the playlist:\n\n" + exception.Message,
                    "Playlist Maker", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveSelectionAsPlaylist()
        {
            List<string> paths = CollectPathsFromSelection(GetSelectedNodes(), true);
            if (paths.Count == 0)
            {
                UpdateSummary("Nothing to save.");
                return;
            }

            PlaylistSortMode? mode = OrderSelectionWindow.Ask(this);
            if (mode == null)
            {
                return;
            }

            List<string> ordered = PlaylistOrdering.Apply(paths, mode.Value);
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Save as playlist",
                Filter = MusicFormats.PlaylistSaveFilter,
                DefaultExt = ".m3u8",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = SuggestPlaylistFileName(ordered)
            };

            string? startDirectory = PathUtilities.GetMutualParentDirectory(ordered);
            if (!string.IsNullOrEmpty(startDirectory))
            {
                dialog.InitialDirectory = startDirectory;
            }

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                PlaylistFileService.Save(dialog.FileName, ordered);
                UpdateSummary("Saved " + ordered.Count + " tracks to " + dialog.FileName + ".");
            }
            catch (IOException exception)
            {
                MessageBox.Show(this, "Could not save the playlist:\n\n" + exception.Message,
                    "Playlist Maker", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (UnauthorizedAccessException exception)
            {
                MessageBox.Show(this, "Could not save the playlist:\n\n" + exception.Message,
                    "Playlist Maker", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string SuggestPlaylistFileName(List<string> paths)
        {
            if (paths.Count == 1)
            {
                string? directoryName = Path.GetFileName(Path.GetDirectoryName(paths[0]) ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(directoryName))
                {
                    return directoryName + ".m3u8";
                }
            }
            return "playlist.m3u8";
        }

        private void HandleDroppedPaths(IEnumerable<string> droppedPaths)
        {
            List<string> folders = droppedPaths.Where(Directory.Exists).ToList();
            List<string> files = droppedPaths.Where(File.Exists).ToList();

            List<string> incoming = new List<string>();
            if (folders.Count > 0)
            {
                string message = folders.Count == 1
                    ? "Add music from \"" + folders[0] + "\": only the files directly inside, or including subfolders?"
                    : "Add music from " + folders.Count + " folders: only the files directly inside them, or including subfolders?";
                bool? recursive = ScopeSelectionWindow.Ask(this, message);
                if (recursive == null)
                {
                    return;
                }
                foreach (string folder in folders)
                {
                    incoming.AddRange(CollectMusicFromFolder(folder, recursive.Value));
                }
            }

            foreach (string file in files)
            {
                if (MusicFormats.IsMusicFile(file) || MusicFormats.IsPlaylistFile(file))
                {
                    incoming.Add(file);
                }
            }

            if (incoming.Count == 0)
            {
                UpdateSummary("Nothing playable in that drop.");
                return;
            }

            int added = viewModel.AddPaths(incoming);
            UpdateSummary("Added " + added + " entr" + (added == 1 ? "y" : "ies") + " from the drop.");
        }

        private List<string> CollectPathsFromSelection(List<FileSystemNode> selected, bool askScopeForFolders)
        {
            List<string> result = new List<string>();
            List<FileSystemNode> folders = selected.Where(node => node.IsFolder).ToList();
            bool recursive = false;

            if (folders.Count > 0 && askScopeForFolders)
            {
                string message = folders.Count == 1
                    ? "Use \"" + folders[0].FullPath + "\": only the files directly inside, or including subfolders?"
                    : "Use " + folders.Count + " folders: only the files directly inside them, or including subfolders?";
                bool? answer = ScopeSelectionWindow.Ask(this, message);
                if (answer == null)
                {
                    return result;
                }
                recursive = answer.Value;
            }

            foreach (FileSystemNode node in selected)
            {
                if (node.IsFolder)
                {
                    result.AddRange(CollectMusicFromFolder(node.FullPath, recursive));
                }
                else if (node.IsMusic || node.IsPlaylist)
                {
                    result.Add(node.FullPath);
                }
            }

            return result;
        }

        private static List<string> CollectMusicFromFolder(string folderPath, bool recursive)
        {
            List<string> result = new List<string>();
            try
            {
                IEnumerable<string> files = Directory.EnumerateFiles(
                    folderPath,
                    "*",
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                foreach (string file in files)
                {
                    if (MusicFormats.IsMusicFile(file))
                    {
                        result.Add(file);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
            catch (ArgumentException)
            {
            }

            result.Sort(NaturalStringComparer.Instance.Compare);
            return result;
        }

        private void RemoveSelectedEntries()
        {
            List<PlaylistEntry> selected = GetSelectedEntries();
            if (selected.Count == 0)
            {
                return;
            }
            int removed = viewModel.RemoveEntries(selected);
            UpdateSummary("Removed " + removed + " entr" + (removed == 1 ? "y" : "ies") + ".");
        }

        private void MoveSelectedEntries(int direction)
        {
            List<PlaylistEntry> selected = GetSelectedEntries();
            if (selected.Count == 0)
            {
                return;
            }
            viewModel.MoveEntries(selected, direction);
            UpdateSummary(direction < 0 ? "Moved up." : "Moved down.");
        }

        private void ViewSelectedPlaylistEntriesInExplorer()
        {
            List<PlaylistEntry> selected = GetSelectedEntries();
            if (selected.Count == 0)
            {
                return;
            }
            PathUtilities.ShowInExplorer(selected[0].FullPath);
        }

        private int GetPlaylistDropIndex(DragEventArgs e)
        {
            for (int index = 0; index < PlaylistList.Items.Count; index++)
            {
                ListBoxItem? container = PlaylistList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;
                if (container == null)
                {
                    continue;
                }
                Point position = e.GetPosition(container);
                if (position.Y >= 0 && position.Y <= container.ActualHeight)
                {
                    return position.Y < container.ActualHeight / 2.0 ? index : index + 1;
                }
            }
            return PlaylistList.Items.Count;
        }

        private List<FileSystemNode> GetSelectedNodes()
        {
            List<FileSystemNode> selected = new List<FileSystemNode>();
            foreach (object item in ExplorerList.SelectedItems)
            {
                FileSystemNode? node = item as FileSystemNode;
                if (node != null)
                {
                    selected.Add(node);
                }
            }
            return selected;
        }

        private List<PlaylistEntry> GetSelectedEntries()
        {
            List<PlaylistEntry> selected = new List<PlaylistEntry>();
            foreach (object item in PlaylistList.SelectedItems)
            {
                PlaylistEntry? entry = item as PlaylistEntry;
                if (entry != null)
                {
                    selected.Add(entry);
                }
            }
            return selected;
        }

        private void UpdateSummary(string message)
        {
            int total = viewModel.PlaylistEntries.Count;
            int missing = viewModel.MissingCount;
            string summary = total + " track" + (total == 1 ? string.Empty : "s");
            if (missing > 0)
            {
                summary += "   " + missing + " missing";
            }
            summary += "     Ctrl+S save   Ctrl+Up/Down reorder   Delete remove   E show in Explorer   Ctrl+Z/Y undo/redo";
            PlaylistSummary.Text = summary;
            StatusText.Text = message;
        }

        private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
        {
            DependencyObject? current = source;
            while (current != null)
            {
                T? match = current as T;
                if (match != null)
                {
                    return match;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
