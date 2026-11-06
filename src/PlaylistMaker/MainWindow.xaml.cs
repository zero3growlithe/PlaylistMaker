using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        private const string PreferredDropEffectFormat = "Preferred DropEffect";
        private const string PerformedDropEffectFormat = "Performed DropEffect";
        private const double DragThreshold = 6.0;

        private readonly MainViewModel viewModel = new MainViewModel();
        private readonly UserSettings userSettings;
        private readonly PlaylistColumnSet columnSet;
        private Point explorerDragStart;
        private Point playlistDragStart;
        private bool isExplorerDragPending;
        private bool isPlaylistDragPending;
        private bool isSyncingTabSelection;
        private bool isFileOperationDrop;
        private FileSystemNode? dropTargetNode;
        private string? dropHintText;
        private string statusTextBeforeDrop = string.Empty;

        public MainWindow()
        {
            InitializeComponent();

            userSettings = UserSettings.Load();
            columnSet = new PlaylistColumnSet(PlaylistGridView, BuildColumnDefinitions());
            columnSet.ApplySavedLayout(userSettings);
            // dragging a header reorders GridView.Columns, so that is where a new order becomes persistent
            PlaylistGridView.Columns.CollectionChanged += OnColumnsCollectionChanged;

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

        private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            bool anyRenaming = viewModel.Tabs.Any(tab => tab.IsRenaming);
            if (!anyRenaming)
            {
                return;
            }

            TextBox? editor = FindAncestor<TextBox>(e.OriginalSource as DependencyObject);
            if (editor != null && editor.DataContext is PlaylistTab editedTab && editedTab.IsRenaming)
            {
                return;
            }

            // commits the typed name, exactly like clicking another tab or pressing Enter;
            // the click itself is left alone so it still selects whatever it landed on
            CommitPendingRenames();
        }

        private void CommitPendingRenames()
        {
            List<PlaylistTab> renamingTabs = viewModel.Tabs.Where(tab => tab.IsRenaming).ToList();
            foreach (PlaylistTab tab in renamingTabs)
            {
                CommitRename(tab, true);
            }
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
            CommitPendingRenames();

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
            SaveColumnLayout();

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
            ToggleExpansion(node, altHeld, false);
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

            if (e.Key != Key.Right && e.Key != Key.Left)
            {
                return;
            }

            FileSystemNode? node = ExplorerList.SelectedItem as FileSystemNode;
            if (node == null)
            {
                return;
            }

            if (e.Key == Key.Right)
            {
                if (node.IsFolder && !node.IsExpanded)
                {
                    ToggleExpansion(node, false, true);
                }
                e.Handled = true;
                return;
            }

            bool altHeld = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
            if (node.IsFolder && node.IsExpanded)
            {
                ToggleExpansion(node, altHeld, true);
            }
            else if (node.IsFolder)
            {
                SelectParentFolder(node);
            }
            e.Handled = true;
        }

        /// <summary>
        /// Expands or collapses a folder while keeping the selection on the same rows.
        /// <see cref="MainViewModel.RebuildVisibleNodes"/> flushes and refills VisibleNodes,
        /// which makes the ListBox drop its selection, so it has to be rebuilt from paths.
        /// </summary>
        private void ToggleExpansion(FileSystemNode node, bool collapseRecursively, bool keepNodeSelected)
        {
            bool wasSelected = false;
            foreach (FileSystemNode selected in GetSelectedNodes())
            {
                if (ReferenceEquals(selected, node))
                {
                    wasSelected = true;
                    break;
                }
            }

            // the flattened list is updated row by row, so the ListBox keeps its selection and its
            // current row on its own - no reselection needed (that was what reset the anchor)
            viewModel.ToggleExpand(node, collapseRecursively);

            if (keepNodeSelected)
            {
                if (!wasSelected || ExplorerList.SelectedItems.Count == 0)
                {
                    ExplorerList.SelectedItems.Clear();
                    ExplorerList.SelectedItem = node;
                }
                FocusExplorerRow(node);
            }

            ExplorerList.ScrollIntoView(node);
        }

        /// <summary>
        /// Gives the row keyboard focus, which is what makes Up/Down continue from there instead of
        /// restarting at the top after the item containers were touched.
        /// </summary>
        private void FocusExplorerRow(FileSystemNode node)
        {
            ExplorerList.UpdateLayout();
            ListBoxItem? container = ExplorerList.ItemContainerGenerator.ContainerFromItem(node) as ListBoxItem;
            if (container == null)
            {
                ExplorerList.ScrollIntoView(node);
                ExplorerList.UpdateLayout();
                container = ExplorerList.ItemContainerGenerator.ContainerFromItem(node) as ListBoxItem;
            }

            if (container != null)
            {
                container.Focus();
            }
        }

        /// <summary>Left on a folded folder walks up the tree to the folder that contains it.</summary>
        private void SelectParentFolder(FileSystemNode node)
        {
            string trimmedPath = node.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? parentPath = Path.GetDirectoryName(trimmedPath);
            if (string.IsNullOrEmpty(parentPath))
            {
                return;
            }

            string wantedParent = parentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            FileSystemNode? parent = viewModel.VisibleNodes.FirstOrDefault(candidate =>
                string.Equals(candidate.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                              wantedParent,
                              StringComparison.OrdinalIgnoreCase));
            if (parent == null)
            {
                return;
            }

            ExplorerList.SelectedItems.Clear();
            ExplorerList.SelectedItem = parent;
            ExplorerList.ScrollIntoView(parent);
            FocusExplorerRow(parent);
        }

        private void OnExplorerMouseDown(object sender, MouseButtonEventArgs e)
        {
            // a press on the chevron is a click, not the start of a drag
            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
            {
                isExplorerDragPending = false;
                return;
            }

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
            // Explorer and other shell targets read this to decide what a modifier-free drop does;
            // leaving the tree alone must never move anything, so the default is a copy
            data.SetData(PreferredDropEffectFormat, BuildDropEffect(DragDropEffects.Copy));

            isFileOperationDrop = false;
            DragDropEffects performed = DragDrop.DoDragDrop(ExplorerList, data, DragDropEffects.Copy | DragDropEffects.Move);
            bool handledHere = isFileOperationDrop;
            isFileOperationDrop = false;

            if (!handledHere && performed == DragDropEffects.Move)
            {
                // Shift-dragging out to Explorer moves the files behind our back, so the rows the
                // tree is showing are now stale and the playlist entries may point at nothing
                RefreshAfterExternalMove(paths);
            }
        }

        /// <summary>Reloads the folders an external move emptied and re-checks the playlist entries.</summary>
        private void RefreshAfterExternalMove(IEnumerable<string> movedPaths)
        {
            List<string> folders = new List<string>();
            foreach (string path in movedPaths)
            {
                string? parent = Path.GetDirectoryName(PathUtilities.TrimTrailingSeparator(path));
                if (!string.IsNullOrEmpty(parent))
                {
                    folders.Add(parent!);
                }
            }

            viewModel.RefreshFolders(folders);
            viewModel.RefreshEntryStates();
            UpdateSummary("Moved out of the tree - folders reloaded, playlist entries re-checked.");
        }

        private static MemoryStream BuildDropEffect(DragDropEffects effect)
        {
            return new MemoryStream(BitConverter.GetBytes((int)effect));
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

        // ── Columns ───────────────────────────────────────────────────────
        private List<PlaylistColumnDefinition> BuildColumnDefinitions()
        {
            return new List<PlaylistColumnDefinition>
            {
                new PlaylistColumnDefinition("index", "Index", ColumnIndex, 46),
                new PlaylistColumnDefinition("trackname", "Track name", ColumnTrackName, 260),
                new PlaylistColumnDefinition("path", "Path", ColumnPath, 300),
                new PlaylistColumnDefinition("album", "Album", ColumnAlbum, 150),
                new PlaylistColumnDefinition("artist", "Artist", ColumnArtist, 140),
                new PlaylistColumnDefinition("length", "Length", ColumnTrackLength, 66),
                new PlaylistColumnDefinition("size", "Size", ColumnFileSize, 78)
            };
        }

        /// <summary>Right-click on any column header: a checkbox per column plus "show all".</summary>
        private void OnColumnHeaderContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            ContextMenu menu = new ContextMenu();

            foreach (PlaylistColumnDefinition definition in columnSet.Columns)
            {
                PlaylistColumnDefinition column = definition;
                MenuItem item = new MenuItem
                {
                    Header = column.Header,
                    IsCheckable = true,
                    IsChecked = column.IsVisible,
                    StaysOpenOnClick = true
                };
                item.Click += (itemSender, itemArgs) => ToggleColumn(column);
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
            MenuItem showAll = new MenuItem { Header = "Show all columns" };
            showAll.Click += OnShowAllColumnsClick;
            menu.Items.Add(showAll);

            GridViewColumnHeader? header = sender as GridViewColumnHeader;
            if (header != null)
            {
                header.ContextMenu = menu;
            }
        }

        private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Move && columnSet.IsApplyingLayout)
            {
                return;
            }
            SaveColumnLayout();
        }

        private void ToggleColumn(PlaylistColumnDefinition definition)
        {
            if (!columnSet.ToggleVisibility(definition))
            {
                UpdateSummary("At least one column has to stay visible - use \"Show all columns\" to bring a hidden one back.");
                return;
            }

            SaveColumnLayout();
            UpdateSummary((definition.IsVisible ? "Showing " : "Hiding ") + definition.Header + ".");
        }

        private void OnShowAllColumnsClick(object sender, RoutedEventArgs e)
        {
            columnSet.ShowAllColumns();
            SaveColumnLayout();
            UpdateSummary("All columns are visible again.");
        }

        private void SaveColumnLayout()
        {
            columnSet.SaveInto(userSettings);
            userSettings.Save();
        }

        // ── Sorting ───────────────────────────────────────────────────────
        private void OnSortClick(object sender, RoutedEventArgs e)
        {
            if (viewModel.ActiveTab.Entries.Count == 0)
            {
                UpdateSummary("This playlist is empty - nothing to sort.");
                return;
            }

            if (!SortPlaylistWindow.Ask(this, out PlaylistSortField field, out bool descending))
            {
                return;
            }

            List<PlaylistEntry> previouslySelected = GetSelectedEntries();
            List<PlaylistEntry> ordered = PlaylistSorter.Sort(viewModel.ActiveTab.Entries.ToList(), field, descending);
            viewModel.SortActiveTab(ordered);
            BindActiveTab();

            foreach (PlaylistEntry entry in previouslySelected)
            {
                if (viewModel.ActiveTab.Entries.Contains(entry))
                {
                    PlaylistList.SelectedItems.Add(entry);
                }
            }

            UpdateSummary("Sorted by " + DescribeSort(field, descending) + ".");
        }

        private static string DescribeSort(PlaylistSortField field, bool descending)
        {
            if (field == PlaylistSortField.Shuffle)
            {
                return "shuffle";
            }

            string name;
            switch (field)
            {
                case PlaylistSortField.TrackNumberFromTags:
                    name = "track number";
                    break;
                case PlaylistSortField.FileName:
                    name = "file name";
                    break;
                case PlaylistSortField.TrackName:
                    name = "track name";
                    break;
                case PlaylistSortField.FileSize:
                    name = "file size";
                    break;
                case PlaylistSortField.TrackLength:
                    name = "track length";
                    break;
                case PlaylistSortField.AlbumName:
                    name = "album name";
                    break;
                case PlaylistSortField.ArtistName:
                    name = "artist name";
                    break;
                default:
                    name = "path";
                    break;
            }
            return name + (descending ? " (descending)" : " (ascending)");
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
                    savedNames.Add(Path.GetFileName(result.TargetPath!) ?? string.Empty);
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

        // ── Drops onto the tree: real file and folder operations ──────────
        /// <summary>
        /// A drag over the tree is a pending file operation, so the row that would receive it is
        /// highlighted and the status line says what a drop would do. Drops that cannot work (a
        /// folder onto itself, or no folder under the pointer) show the "no" cursor instead.
        /// </summary>
        private void OnExplorerDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = DragDropEffects.None;

            FileSystemNode? target = ResolveDropTargetNode(e);
            string[] sources = GetDraggedPaths(e);
            if (target == null || sources.Length == 0
                || FileTransferService.IsTargetInsideSources(sources, target.FullPath))
            {
                SetDropTarget(null);
                ShowDropHint(null);
                return;
            }

            FileTransferMode mode = GetDropMode();
            e.Effects = mode == FileTransferMode.Move ? DragDropEffects.Move : DragDropEffects.Copy;
            SetDropTarget(target);
            ShowDropHint(BuildDropHint(target, sources, mode));
        }

        private void OnExplorerDragLeave(object sender, DragEventArgs e)
        {
            SetDropTarget(null);
            ShowDropHint(null);
        }

        private void OnExplorerDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            string[] sources = GetDraggedPaths(e);
            FileSystemNode? target = ResolveDropTargetNode(e);
            FileTransferMode mode = GetDropMode();
            SetDropTarget(null);
            ShowDropHint(null);

            if (target == null || sources.Length == 0)
            {
                UpdateSummary("Drop ignored - put the pointer on a folder row.");
                return;
            }

            // tells DoDragDrop in OnExplorerMouseMove that this drop was already carried out here
            isFileOperationDrop = true;
            bool transferred = PerformFileTransfer(target, sources, mode);
            if (transferred && mode == FileTransferMode.Move)
            {
                MarkMovePerformed(e);
            }
        }

        /// <summary>
        /// Shell bookkeeping: a target that carries the move out itself has to say so, otherwise the
        /// source (Explorer, Total Commander) tries to delete the originals a second time.
        /// </summary>
        private static void MarkMovePerformed(DragEventArgs e)
        {
            try
            {
                e.Data.SetData(PerformedDropEffectFormat, BuildDropEffect(DragDropEffects.Move));
            }
            catch (COMException)
            {
                // a few drag sources hand out a read-only data object; nothing to do about that
            }
            catch (NotSupportedException)
            {
            }
        }

        /// <summary>The folder a drop lands in: a folder row itself, or the folder holding a file row.</summary>
        private FileSystemNode? ResolveDropTargetNode(DragEventArgs e)
        {
            DependencyObject? hit = ExplorerList.InputHitTest(e.GetPosition(ExplorerList)) as DependencyObject;
            ListBoxItem? item = FindAncestor<ListBoxItem>(hit);
            FileSystemNode? node = item?.DataContext as FileSystemNode;
            if (node == null)
            {
                return null;
            }
            if (node.IsFolder)
            {
                return node;
            }

            string? parentPath = Path.GetDirectoryName(PathUtilities.TrimTrailingSeparator(node.FullPath));
            return string.IsNullOrEmpty(parentPath) ? null : viewModel.FindLoadedNode(parentPath);
        }

        private static string[] GetDraggedPaths(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return Array.Empty<string>();
            }
            return e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
        }

        /// <summary>Windows convention: a plain drop relocates, Ctrl keeps a copy at the source.</summary>
        private static FileTransferMode GetDropMode()
        {
            bool copyRequested = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            return copyRequested ? FileTransferMode.Copy : FileTransferMode.Move;
        }

        private void SetDropTarget(FileSystemNode? node)
        {
            if (ReferenceEquals(dropTargetNode, node))
            {
                return;
            }
            if (dropTargetNode != null)
            {
                dropTargetNode.IsDropTarget = false;
            }
            dropTargetNode = node;
            if (dropTargetNode != null)
            {
                dropTargetNode.IsDropTarget = true;
            }
        }

        private static string BuildDropHint(FileSystemNode target, string[] sources, FileTransferMode mode)
        {
            string verb = mode == FileTransferMode.Move ? "Move" : "Copy";
            string items = sources.Length == 1 ? "1 item" : sources.Length + " items";
            string hint = verb + " " + items + " into \"" + target.Name + "\"";
            return mode == FileTransferMode.Move
                ? hint + " - hold Ctrl to copy instead"
                : hint + " - release Ctrl to move instead";
        }

        /// <summary>
        /// DragOver fires on every mouse move, so the hint is only rewritten when it changes, and the
        /// status line from before the drag comes back when the pointer leaves the tree.
        /// </summary>
        private void ShowDropHint(string? hint)
        {
            if (string.Equals(dropHintText, hint, StringComparison.Ordinal))
            {
                return;
            }

            if (hint == null)
            {
                dropHintText = null;
                StatusText.Text = statusTextBeforeDrop;
                return;
            }

            if (dropHintText == null)
            {
                statusTextBeforeDrop = StatusText.Text;
            }
            dropHintText = hint;
            StatusText.Text = hint;
        }

        /// <summary>Runs a drop as a real file operation; true when at least one item really moved or was copied.</summary>
        private bool PerformFileTransfer(FileSystemNode target, string[] sourcePaths, FileTransferMode mode)
        {
            List<string> sources = new List<string>();
            foreach (string path in sourcePaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }
                string trimmed = PathUtilities.TrimTrailingSeparator(path);
                if (File.Exists(trimmed) || Directory.Exists(trimmed))
                {
                    sources.Add(trimmed);
                }
            }

            string targetDirectory = PathUtilities.TrimTrailingSeparator(target.FullPath);
            if (sources.Count == 0)
            {
                UpdateSummary("Nothing to transfer - that item is gone from disk.");
                return false;
            }
            if (!Directory.Exists(targetDirectory))
            {
                UpdateSummary("\"" + targetDirectory + "\" no longer exists.");
                return false;
            }
            if (FileTransferService.IsTargetInsideSources(sources, targetDirectory))
            {
                UpdateSummary("A folder cannot be moved into itself.");
                return false;
            }

            FileConflictPolicy policy = FileConflictPolicy.KeepBoth;
            List<string> conflicts = FileTransferService.FindConflicts(sources, targetDirectory);
            if (conflicts.Count > 0)
            {
                FileConflictPolicy? answer = AskConflictPolicy(conflicts, targetDirectory);
                if (answer == null)
                {
                    UpdateSummary("Transfer cancelled.");
                    return false;
                }
                policy = answer.Value;
            }

            // the rows are about to be rewritten, so remember what was selected before they move
            List<string> selectionBefore = GetSelectedNodes().Select(node => node.FullPath).ToList();

            FileTransferResult result = FileTransferService.Transfer(sources, targetDirectory, mode, policy);
            if (result.Outcomes.Count == 0 && result.Failures.Count == 0)
            {
                UpdateSummary("Nothing to do - already in \"" + targetDirectory + "\".");
                return false;
            }

            int repointedEntries = mode == FileTransferMode.Move
                ? viewModel.RebasePlaylistEntries(result.Outcomes)
                : 0;

            SyncTreeAfterTransfer(result.Outcomes, targetDirectory, selectionBefore);

            string verb = mode == FileTransferMode.Move ? "Moved" : "Copied";
            string message = verb + " " + result.Outcomes.Count
                + (result.Outcomes.Count == 1 ? " item" : " items") + " to \"" + targetDirectory + "\"";
            if (result.SkippedCount > 0)
            {
                message += "   " + result.SkippedCount + " already there";
            }
            if (repointedEntries > 0)
            {
                message += "   " + repointedEntries + " playlist "
                    + (repointedEntries == 1 ? "entry" : "entries") + " re-pointed";
            }
            if (result.Failures.Count > 0)
            {
                message += "   " + result.Failures.Count + " failed";
            }

            UpdateSummary(message);
            ShowTransferFailures(result.Failures);
            return result.Outcomes.Count > 0;
        }

        /// <summary>
        /// Brings the tree back in line with the disk: reload the folders that changed, keep the
        /// expanded branch of every moved folder, open the destination, and put the selection back
        /// on the rows that just moved so the next keystroke acts on what is new.
        /// </summary>
        private void SyncTreeAfterTransfer(
            IReadOnlyList<FileTransferOutcome> outcomes,
            string targetDirectory,
            List<string> selectionBefore)
        {
            List<BranchMove> branches = new List<BranchMove>();
            List<string> foldersToReload = new List<string>();
            foreach (FileTransferOutcome outcome in outcomes)
            {
                FileSystemNode? sourceNode = viewModel.FindLoadedNode(outcome.SourcePath);
                if (sourceNode != null && sourceNode.IsFolder && sourceNode.ChildrenLoaded)
                {
                    branches.Add(new BranchMove(sourceNode, outcome.TargetPath));
                }

                string? sourceParent = Path.GetDirectoryName(outcome.SourcePath);
                if (!string.IsNullOrEmpty(sourceParent))
                {
                    foldersToReload.Add(sourceParent!);
                }
            }
            foldersToReload.Add(targetDirectory);

            viewModel.RefreshFolders(foldersToReload);

            // a reload rebuilds the destination's children, so the node is looked up again here
            FileSystemNode? targetFolder = viewModel.FindLoadedNode(targetDirectory);
            viewModel.ExpandFolder(targetFolder);
            targetFolder = viewModel.FindLoadedNode(targetDirectory);
            foreach (BranchMove branch in branches)
            {
                viewModel.InstallMovedBranch(targetFolder, branch.Node, branch.NewPath);
            }

            List<string> wanted = new List<string>();
            foreach (string path in selectionBefore)
            {
                string mappedPath;
                if (FileTransferService.TryMapPath(outcomes, path, out mappedPath))
                {
                    wanted.Add(mappedPath);
                }
            }
            foreach (FileTransferOutcome outcome in outcomes)
            {
                bool known = false;
                foreach (string existing in wanted)
                {
                    if (PathUtilities.PathsEqual(existing, outcome.TargetPath))
                    {
                        known = true;
                        break;
                    }
                }
                if (!known)
                {
                    wanted.Add(outcome.TargetPath);
                }
            }

            SelectExplorerPaths(wanted);
        }

        /// <summary>Selects the given paths in the tree and leaves keyboard focus on the first one.</summary>
        private void SelectExplorerPaths(IReadOnlyList<string> paths)
        {
            ExplorerList.SelectedItems.Clear();
            FileSystemNode? firstSelected = null;
            foreach (string path in paths)
            {
                FileSystemNode? node = viewModel.VisibleNodes.FirstOrDefault(
                    candidate => PathUtilities.PathsEqual(candidate.FullPath, path));
                if (node == null)
                {
                    continue;
                }
                ExplorerList.SelectedItems.Add(node);
                if (firstSelected == null)
                {
                    firstSelected = node;
                }
            }

            if (firstSelected == null)
            {
                ExplorerList.Focus();
                return;
            }

            ExplorerList.ScrollIntoView(firstSelected);
            FocusExplorerRow(firstSelected);
        }

        private FileConflictPolicy? AskConflictPolicy(IReadOnlyList<string> conflicts, string targetDirectory)
        {
            string message = conflicts.Count == 1
                ? "\"" + Path.GetFileName(conflicts[0]) + "\" already exists in \"" + targetDirectory + "\"."
                : conflicts.Count + " of these items already exist in \"" + targetDirectory + "\".";
            message += Environment.NewLine + Environment.NewLine
                + "Yes - replace what is already there" + Environment.NewLine
                + "No - keep both (the incoming item gets a number)" + Environment.NewLine
                + "Cancel - change nothing";

            MessageBoxResult answer = MessageBox.Show(
                this, message, "Items already exist", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
            {
                return FileConflictPolicy.Replace;
            }
            if (answer == MessageBoxResult.No)
            {
                return FileConflictPolicy.KeepBoth;
            }
            return null;
        }

        private void ShowTransferFailures(IReadOnlyList<string> failures)
        {
            if (failures.Count == 0)
            {
                return;
            }

            string message = failures.Count == 1
                ? "One item could not be transferred:"
                : failures.Count + " items could not be transferred:";
            int shown = Math.Min(failures.Count, 8);
            for (int index = 0; index < shown; index++)
            {
                message += Environment.NewLine + failures[index];
            }
            if (failures.Count > shown)
            {
                message += Environment.NewLine + "...";
            }

            MessageBox.Show(this, message, "File transfer", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                // a click on text can originate from a Run, which is a ContentElement - VisualTreeHelper
                // throws for those, so fall back to the logical tree for non-visuals
                bool isVisual = current is Visual || current is System.Windows.Media.Media3D.Visual3D;
                current = isVisual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
            }
            return null;
        }

        /// <summary>A folder whose tree node has to be rebuilt at the path it was moved to.</summary>
        private sealed class BranchMove
        {
            public BranchMove(FileSystemNode node, string newPath)
            {
                Node = node;
                NewPath = newPath;
            }

            public FileSystemNode Node { get; }

            public string NewPath { get; }
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
