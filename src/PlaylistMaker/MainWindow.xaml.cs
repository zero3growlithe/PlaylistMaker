using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
        private bool isSyncingTabSelection;

        public MainWindow()
        {
            InitializeComponent();
            ExplorerList.ItemsSource = viewModel.VisibleNodes;
            TabStrip.ItemsSource = viewModel.Tabs;
            viewModel.Initialize();
            BindActiveTab();
            UpdateSummary("Ready. Drag music onto the playlist or onto another tab.");
        }

        // ── Tabs ──────────────────────────────────────────────────────────
        private void BindActiveTab()
        {
            PlaylistTab tab = viewModel.ActiveTab;
            PlaylistList.ItemsSource = tab.Entries;

            if (!ReferenceEquals(TabStrip.SelectedItem, tab))
            {
                isSyncingTabSelection = true;
                TabStrip.SelectedItem = tab;
                isSyncingTabSelection = false;
            }

            Title = "Playlist Maker - " + tab.DisplayName;
            UpdateSummary(tab.Entries.Count == 0
                ? "Tab \"" + tab.Name + "\" is empty."
                : "Tab \"" + tab.Name + "\": " + tab.Entries.Count + " tracks.");
        }

        private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isSyncingTabSelection)
            {
                return;
            }
            PlaylistTab? tab = TabStrip.SelectedItem as PlaylistTab;
            if (tab == null)
            {
                return;
            }
            viewModel.SetActiveTab(tab);
            BindActiveTab();
        }

        private void OnAddTabClick(object sender, RoutedEventArgs e)
        {
            PlaylistTab tab = viewModel.CreateTab();
            BindActiveTab();
            BeginRename(tab);
            UpdateSummary("Created tab \"" + tab.Name + "\".");
        }

        private void OnCloseTabClick(object sender, RoutedEventArgs e)
        {
            Button? button = sender as Button;
            PlaylistTab? tab = button?.Tag as PlaylistTab;
            if (tab != null)
            {
                CloseTab(tab);
            }
            e.Handled = true;
        }

        private void OnTabStripMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle)
            {
                return;
            }
            PlaylistTab? tab = GetTabFromSource(e.OriginalSource);
            if (tab != null)
            {
                CloseTab(tab);
                e.Handled = true;
            }
        }

        private void OnTabStripDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
            {
                return;
            }
            PlaylistTab? tab = GetTabFromSource(e.OriginalSource);
            if (tab != null)
            {
                BeginRename(tab);
                e.Handled = true;
            }
        }

        private void BeginRename(PlaylistTab tab)
        {
            tab.EditingName = tab.Name;
            tab.IsRenaming = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                ListBoxItem? container = TabStrip.ItemContainerGenerator.ContainerFromItem(tab) as ListBoxItem;
                if (container == null)
                {
                    return;
                }
                TextBox? editor = FindChild<TextBox>(container);
                if (editor != null)
                {
                    editor.Focus();
                    editor.SelectAll();
                }
            }));
        }

        private void CommitRename(PlaylistTab tab, bool accept)
        {
            if (!tab.IsRenaming)
            {
                return;
            }
            if (accept)
            {
                string typed = (tab.EditingName ?? string.Empty).Trim();
                if (typed.Length > 0)
                {
                    tab.Name = typed;
                }
            }
            tab.EditingName = tab.Name;
            tab.IsRenaming = false;
            if (ReferenceEquals(tab, viewModel.ActiveTab))
            {
                Title = "Playlist Maker - " + tab.DisplayName;
            }
        }

        private void OnTabRenameKeyDown(object sender, KeyEventArgs e)
        {
            TextBox? editor = sender as TextBox;
            PlaylistTab? tab = editor?.DataContext as PlaylistTab;
            if (tab == null)
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                CommitRename(tab, true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CommitRename(tab, false);
                e.Handled = true;
            }
        }

        private void OnTabRenameLostFocus(object sender, RoutedEventArgs e)
        {
            TextBox? editor = sender as TextBox;
            PlaylistTab? tab = editor?.DataContext as PlaylistTab;
            if (tab != null)
            {
                CommitRename(tab, true);
            }
        }

        private void SwitchTab(int offset)
        {
            // leaving a tab mid-rename should keep what was typed instead of silently dropping it
            foreach (PlaylistTab candidate in viewModel.Tabs)
            {
                if (candidate.IsRenaming)
                {
                    CommitRename(candidate, true);
                }
            }

            int count = viewModel.Tabs.Count;
            if (count < 2)
            {
                return;
            }

            int current = viewModel.Tabs.IndexOf(viewModel.ActiveTab);
            int target = ((current + offset) % count + count) % count;
            PlaylistTab tab = viewModel.Tabs[target];
            viewModel.SetActiveTab(tab);
            BindActiveTab();
            TabStrip.ScrollIntoView(tab);
            UpdateSummary("Switched to tab \"" + tab.Name + "\".");
        }

        private void CloseTab(PlaylistTab tab)
        {
            if (tab.NeedsSavePrompt)
            {
                MessageBoxResult answer = MessageBox.Show(
                    this,
                    "Save playlist \"" + tab.Name + "\" before closing it?",
                    "Playlist Maker",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (answer == MessageBoxResult.Cancel)
                {
                    return;
                }
                if (answer == MessageBoxResult.Yes && !SaveTabWithDialog(tab))
                {
                    return;
                }
            }

            viewModel.RemoveTab(tab);
            BindActiveTab();
            UpdateSummary("Closed tab \"" + tab.Name + "\".");
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            foreach (PlaylistTab tab in viewModel.Tabs.ToList())
            {
                if (!tab.NeedsSavePrompt)
                {
                    continue;
                }

                MessageBoxResult answer = MessageBox.Show(
                    this,
                    "Save playlist \"" + tab.Name + "\" before closing Playlist Maker?",
                    "Playlist Maker",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (answer == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (answer == MessageBoxResult.Yes && !SaveTabWithDialog(tab))
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        private void OnTabStripDragOver(object sender, DragEventArgs e)
        {
            bool hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnTabStripDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }
            if (e.Data.GetDataPresent(PlaylistEntryFormat))
            {
                return;
            }

            PlaylistTab target = GetTabUnderPoint(e) ?? viewModel.ActiveTab;
            viewModel.SetActiveTab(target);
            BindActiveTab();

            string[] droppedPaths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
            HandleDroppedPaths(droppedPaths);
            e.Handled = true;
        }

        private PlaylistTab? GetTabUnderPoint(DragEventArgs e)
        {
            DependencyObject? hit = TabStrip.InputHitTest(e.GetPosition(TabStrip)) as DependencyObject;
            ListBoxItem? item = FindAncestor<ListBoxItem>(hit);
            return item?.DataContext as PlaylistTab;
        }

        private PlaylistTab? GetTabFromSource(object source)
        {
            ListBoxItem? item = FindAncestor<ListBoxItem>(source as DependencyObject);
            return item?.DataContext as PlaylistTab;
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
            UpdateSummary("Refreshed every tab.");
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

        private void OnExplorerKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddSelectionToPlaylist();
                e.Handled = true;
                return;
            }

            FileSystemNode? node = ExplorerList.SelectedItem as FileSystemNode;
            if (node == null || !node.IsFolder)
            {
                return;
            }

            if (e.Key == Key.Right && !node.IsExpanded)
            {
                viewModel.ToggleExpand(node, false);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Left && node.IsExpanded)
            {
                bool altHeld = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
                viewModel.ToggleExpand(node, altHeld);
                e.Handled = true;
            }
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

            if (selected.Count(node => node.IsFolder) > 1)
            {
                MenuItem saveEachItem = new MenuItem { Header = "Save each as playlist..." };
                saveEachItem.Click += OnSaveEachFolderAsPlaylistClick;
                menu.Items.Add(saveEachItem);
            }

            menu.Items.Add(new Separator());
            MenuItem explorerItem = new MenuItem { Header = "View in Explorer" };
            explorerItem.Click += OnViewSelectedInExplorerClick;
            menu.Items.Add(explorerItem);

            ExplorerList.ContextMenu = menu;
        }

        private void OnAddSelectedToPlaylistClick(object sender, RoutedEventArgs e)
        {
            AddSelectionToPlaylist();
        }

        /// <summary>Adds whatever is selected in the tree to the active tab (menu item and Enter share this).</summary>
        private void AddSelectionToPlaylist()
        {
            List<string> paths = CollectPathsFromSelection(GetSelectedNodes(), true);
            if (paths.Count == 0)
            {
                return;
            }
            int added = viewModel.AddPaths(paths);
            BindActiveTab();
            UpdateSummary("Added " + added + " entr" + (added == 1 ? "y" : "ies") + " to \"" + viewModel.ActiveTab.Name + "\".");
        }

        private void OnSaveSelectedAsPlaylistClick(object sender, RoutedEventArgs e)
        {
            SaveSelectionAsPlaylist();
        }

        private void OnSaveEachFolderAsPlaylistClick(object sender, RoutedEventArgs e)
        {
            SaveEachFolderAsPlaylist();
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
            if (SaveTabWithDialog(viewModel.ActiveTab))
            {
                UpdateSummary("Saved \"" + viewModel.ActiveTab.Name + "\".");
            }
        }

        private void OnLoadClick(object sender, RoutedEventArgs e)
        {
            PlaylistTab tab = viewModel.ActiveTab;
            if (tab.Entries.Count > 0)
            {
                MessageBoxResult answer = MessageBox.Show(
                    this,
                    "Replace the " + tab.Entries.Count + " entries of tab \"" + tab.Name + "\" with the loaded playlist?",
                    "Playlist Maker",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.OK)
                {
                    return;
                }
            }

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
            BindActiveTab();
            UpdateSummary("Loaded " + dialog.FileName + " into tab \"" + tab.Name + "\".");
        }

        private void OnViewInExplorerClick(object sender, RoutedEventArgs e)
        {
            ViewSelectedPlaylistEntriesInExplorer();
        }

        private void OnUndoClick(object sender, RoutedEventArgs e)
        {
            if (viewModel.Undo())
            {
                BindActiveTab();
                UpdateSummary("Undo.");
            }
        }

        private void OnRedoClick(object sender, RoutedEventArgs e)
        {
            if (viewModel.Redo())
            {
                BindActiveTab();
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

            if (e.Key == Key.Tab)
            {
                bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                SwitchTab(shiftHeld ? -1 : 1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F4)
            {
                CloseTab(viewModel.ActiveTab);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.S)
            {
                if (SaveTabWithDialog(viewModel.ActiveTab))
                {
                    UpdateSummary("Saved \"" + viewModel.ActiveTab.Name + "\".");
                }
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Z)
            {
                if (viewModel.Undo())
                {
                    BindActiveTab();
                    UpdateSummary("Undo.");
                }
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Y)
            {
                if (viewModel.Redo())
                {
                    BindActiveTab();
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
                viewModel.ActiveTab.MarkDirty();
                BindActiveTab();
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
            viewModel.ActiveTab.MarkDirty();

            List<PlaylistEntry> otherMissing = viewModel.ActiveTab.Entries
                .Where(item => item.IsMissing && !ReferenceEquals(item, entry))
                .ToList();
            if (otherMissing.Count == 0)
            {
                BindActiveTab();
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
                BindActiveTab();
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
            BindActiveTab();
            UpdateSummary("Repointed " + (fixedCount + 1) + " entr" + (fixedCount == 0 ? "y" : "ies") + "; "
                + (otherMissing.Count - fixedCount) + " still missing.");
        }

        // ── Saving ────────────────────────────────────────────────────────
        private bool SaveTabWithDialog(PlaylistTab tab)
        {
            List<string> paths = tab.SnapshotPaths();
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Save playlist \"" + tab.Name + "\"",
                Filter = MusicFormats.PlaylistSaveFilter,
                DefaultExt = ".m3u8",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = BuildSuggestedFileName(tab)
            };

            string? startDirectory = PathUtilities.GetMutualParentDirectory(paths);
            if (!string.IsNullOrEmpty(startDirectory))
            {
                dialog.InitialDirectory = startDirectory;
            }

            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            try
            {
                PlaylistFileService.Save(dialog.FileName, paths);
                tab.FilePath = dialog.FileName;
                tab.MarkClean();
                tab.RaiseHeaderChanged();
                if (ReferenceEquals(tab, viewModel.ActiveTab))
                {
                    Title = "Playlist Maker - " + tab.DisplayName;
                }
                return true;
            }
            catch (IOException exception)
            {
                ShowSaveError(exception);
                return false;
            }
            catch (UnauthorizedAccessException exception)
            {
                ShowSaveError(exception);
                return false;
            }
        }

        private void ShowSaveError(Exception exception)
        {
            MessageBox.Show(this, "Could not save the playlist:\n\n" + exception.Message,
                "Playlist Maker", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static string BuildSuggestedFileName(PlaylistTab tab)
        {
            string name = string.IsNullOrWhiteSpace(tab.Name) ? "playlist" : tab.Name.Trim();
            return SanitizeFileName(name) + ".m3u8";
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
                FileName = SuggestNameForSelection(GetSelectedNodes())
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
                ShowSaveError(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                ShowSaveError(exception);
            }
        }

        /// <summary>A single selected folder names the playlist after itself; anything else falls back.</summary>
        private static string SuggestNameForSelection(IReadOnlyList<FileSystemNode> selected)
        {
            if (selected.Count == 1 && selected[0].IsFolder)
            {
                string folderName = GetFolderDisplayName(selected[0].FullPath);
                if (folderName.Length > 0)
                {
                    return SanitizeFileName(folderName) + ".m3u8";
                }
            }
            return "playlist.m3u8";
        }

        private static string GetFolderDisplayName(string folderPath)
        {
            string trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(trimmed);
            return name ?? string.Empty;
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            string cleaned = new string(name.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray());
            return cleaned.Trim().Length == 0 ? "playlist" : cleaned.Trim();
        }

        private void SaveEachFolderAsPlaylist()
        {
            List<FileSystemNode> folders = GetSelectedNodes().Where(node => node.IsFolder).ToList();
            if (folders.Count == 0)
            {
                UpdateSummary("Select at least one folder.");
                return;
            }

            string scopeMessage = "Save each of the " + folders.Count
                + " selected folders as its own playlist: only the files directly inside them, or including subfolders?";
            bool? recursive = ScopeSelectionWindow.Ask(this, scopeMessage);
            if (recursive == null)
            {
                return;
            }

            PlaylistSortMode? mode = OrderSelectionWindow.Ask(this);
            if (mode == null)
            {
                return;
            }

            int savedCount = 0;
            int skippedCount = 0;
            int emptyCount = 0;
            List<string> savedNames = new List<string>();

            for (int index = 0; index < folders.Count; index++)
            {
                string folderPath = folders[index].FullPath;
                string folderName = GetFolderDisplayName(folderPath);
                if (folderName.Length == 0)
                {
                    folderName = folderPath;
                }

                List<string> files = CollectMusicFromFolder(folderPath, recursive.Value);
                if (files.Count == 0)
                {
                    emptyCount++;
                    continue;
                }

                List<string> ordered = PlaylistOrdering.Apply(files, mode.Value);
                string suggestedName = SanitizeFileName(folderName) + ".m3u8";

                SaveEachPlaylistResult result = SaveEachPlaylistWindow.Ask(
                    this, index + 1, folders.Count, folderName, ordered.Count, folderPath, suggestedName);

                bool nothingToWrite = result.Action == SaveEachAction.Save && string.IsNullOrWhiteSpace(result.TargetPath);
            if (result.Action == SaveEachAction.CancelAll || nothingToWrite)
                {
                    UpdateSummary("Stopped after " + savedCount + " playlist" + (savedCount == 1 ? string.Empty : "s")
                        + ". " + skippedCount + " skipped, " + emptyCount + " empty.");
                    return;
                }
                if (result.Action == SaveEachAction.Skip)
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    PlaylistFileService.Save(result.TargetPath!, ordered);
                    savedCount++;
                    savedNames.Add(Path.GetFileName(result.TargetPath));
                }
                catch (IOException exception)
                {
                    ShowSaveError(exception);
                    skippedCount++;
                }
                catch (UnauthorizedAccessException exception)
                {
                    ShowSaveError(exception);
                    skippedCount++;
                }
            }

            string summary = "Saved " + savedCount + " of " + folders.Count + " folders as playlists";
            if (savedNames.Count > 0)
            {
                summary += " (" + string.Join(", ", savedNames) + ")";
            }
            if (skippedCount > 0)
            {
                summary += "; " + skippedCount + " skipped";
            }
            if (emptyCount > 0)
            {
                summary += "; " + emptyCount + " had no music";
            }
            UpdateSummary(summary + ".");
        }

        // ── Drops and helpers ─────────────────────────────────────────────
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
            BindActiveTab();
            UpdateSummary("Added " + added + " entr" + (added == 1 ? "y" : "ies") + " to \"" + viewModel.ActiveTab.Name + "\".");
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

            int firstIndex = int.MaxValue;
            foreach (PlaylistEntry entry in selected)
            {
                int index = viewModel.ActiveTab.Entries.IndexOf(entry);
                if (index >= 0 && index < firstIndex)
                {
                    firstIndex = index;
                }
            }

            int removed = viewModel.RemoveEntries(selected);
            BindActiveTab();

            int remaining = viewModel.ActiveTab.Entries.Count;
            if (remaining > 0 && firstIndex != int.MaxValue)
            {
                int targetIndex = Math.Min(firstIndex, remaining - 1);
                PlaylistList.SelectedIndex = targetIndex;
                PlaylistList.ScrollIntoView(PlaylistList.SelectedItem);
            }

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
            PlaylistTab tab = viewModel.ActiveTab;
            int total = tab.Entries.Count;
            int missing = tab.Entries.Count(entry => entry.IsMissing);
            string summary = tab.Name + ": " + total + " track" + (total == 1 ? string.Empty : "s");
            if (missing > 0)
            {
                summary += "   " + missing + " missing";
            }
            summary += "     Ctrl+S save   Ctrl+Up/Down reorder   Delete remove   E show in Explorer   Ctrl+Z/Y undo/redo"
                + "   Ctrl+Tab / Ctrl+Shift+Tab switch tab   Ctrl+F4 close tab   Enter add from tree";
            PlaylistSummary.Text = summary;
            StatusText.Text = message;
            Title = "Playlist Maker - " + tab.DisplayName;
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

        private static T? FindChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int index = 0; index < childCount; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                T? match = child as T;
                if (match != null)
                {
                    return match;
                }
                T? nested = FindChild<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }
    }
}
