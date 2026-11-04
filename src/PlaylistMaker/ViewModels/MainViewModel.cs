using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using PlaylistMaker.Models;
using PlaylistMaker.Services;

namespace PlaylistMaker.ViewModels
{
    /// <summary>State of the window: the flattened explorer tree plus the open playlist tabs.</summary>
    public sealed class MainViewModel
    {
        private readonly FileSystemTreeService treeService = new FileSystemTreeService();
        private PlaylistTab activeTab;
        private int tabCounter;

        public MainViewModel()
        {
            tabCounter = 1;
            activeTab = new PlaylistTab("Playlist 1");
            Tabs.Add(activeTab);
        }

        public ObservableCollection<FileSystemNode> VisibleNodes { get; } = new ObservableCollection<FileSystemNode>();

        public List<FileSystemNode> Roots { get; private set; } = new List<FileSystemNode>();

        public ObservableCollection<PlaylistTab> Tabs { get; } = new ObservableCollection<PlaylistTab>();

        public PlaylistTab ActiveTab => activeTab;

        public bool ShowAllFiles
        {
            get => treeService.ShowAllFiles;
            set
            {
                if (treeService.ShowAllFiles == value)
                {
                    return;
                }
                treeService.ShowAllFiles = value;
                ReloadTree();
            }
        }

        public int MissingCount => activeTab.Entries.Count(entry => entry.IsMissing);

        public void Initialize()
        {
            ReloadTree();
        }

        // ── Tabs ──────────────────────────────────────────────────────────
        public PlaylistTab CreateTab()
        {
            tabCounter++;
            PlaylistTab tab = new PlaylistTab("Playlist " + tabCounter);
            Tabs.Add(tab);
            activeTab = tab;
            return tab;
        }

        public void SetActiveTab(PlaylistTab tab)
        {
            if (tab != null && Tabs.Contains(tab))
            {
                activeTab = tab;
            }
        }

        /// <summary>Removes a tab; a fresh empty one takes its place when it was the last one.</summary>
        public PlaylistTab RemoveTab(PlaylistTab tab)
        {
            if (tab == null || !Tabs.Contains(tab))
            {
                return activeTab;
            }

            int index = Tabs.IndexOf(tab);
            Tabs.Remove(tab);
            if (Tabs.Count == 0)
            {
                tabCounter++;
                PlaylistTab replacement = new PlaylistTab("Playlist " + tabCounter);
                Tabs.Add(replacement);
                activeTab = replacement;
                return replacement;
            }

            if (ReferenceEquals(activeTab, tab))
            {
                int nextIndex = index >= Tabs.Count ? Tabs.Count - 1 : index;
                activeTab = Tabs[nextIndex];
            }
            return activeTab;
        }

        // ── Explorer tree ─────────────────────────────────────────────────
        public void ReloadTree()
        {
            Roots = treeService.BuildRoots();
            RebuildVisibleNodes();
        }

        /// <summary>
        /// Rebuilds the flattened tree with targeted inserts, moves and removes instead of a
        /// Clear() + refill. Clearing makes the ListBox throw away both its selection and its
        /// current row, and the current row is what keyboard navigation anchors on - a full clear
        /// is why Up/Down used to jump back to the top of the tree after an expand or collapse.
        /// </summary>
        public void RebuildVisibleNodes()
        {
            List<FileSystemNode> target = new List<FileSystemNode>();
            foreach (FileSystemNode root in Roots)
            {
                AppendVisible(root, target);
            }
            ApplyVisibleNodes(target);
        }

        private void ApplyVisibleNodes(List<FileSystemNode> target)
        {
            HashSet<FileSystemNode> wanted = new HashSet<FileSystemNode>(target);

            for (int index = VisibleNodes.Count - 1; index >= 0; index--)
            {
                if (!wanted.Contains(VisibleNodes[index]))
                {
                    VisibleNodes.RemoveAt(index);
                }
            }

            for (int index = 0; index < target.Count; index++)
            {
                if (index < VisibleNodes.Count && ReferenceEquals(VisibleNodes[index], target[index]))
                {
                    continue;
                }

                int existingIndex = -1;
                for (int scan = index + 1; scan < VisibleNodes.Count; scan++)
                {
                    if (ReferenceEquals(VisibleNodes[scan], target[index]))
                    {
                        existingIndex = scan;
                        break;
                    }
                }

                if (existingIndex >= 0)
                {
                    VisibleNodes.Move(existingIndex, index);
                }
                else
                {
                    VisibleNodes.Insert(index, target[index]);
                }
            }

            while (VisibleNodes.Count > target.Count)
            {
                VisibleNodes.RemoveAt(VisibleNodes.Count - 1);
            }
        }

        private void AppendVisible(FileSystemNode node, List<FileSystemNode> target)
        {
            target.Add(node);
            if (!node.IsExpanded)
            {
                return;
            }
            foreach (FileSystemNode child in node.Children)
            {
                AppendVisible(child, target);
            }
        }

        /// <summary>Expands a folder (loading its children once) or collapses it. Alt-collapse also closes every descendant.</summary>
        public void ToggleExpand(FileSystemNode node, bool collapseRecursively)
        {
            if (node == null || !node.IsFolder)
            {
                return;
            }

            if (node.IsExpanded)
            {
                if (collapseRecursively)
                {
                    CollapseRecursive(node);
                }
                node.IsExpanded = false;
            }
            else
            {
                if (!node.ChildrenLoaded)
                {
                    List<FileSystemNode> children = treeService.LoadChildren(node);
                    node.Children.Clear();
                    foreach (FileSystemNode child in children)
                    {
                        node.Children.Add(child);
                    }
                    node.ChildrenLoaded = true;
                }
                node.IsExpanded = true;
            }

            RebuildVisibleNodes();
        }

        private static void CollapseRecursive(FileSystemNode node)
        {
            foreach (FileSystemNode child in node.Children)
            {
                CollapseRecursive(child);
                child.IsExpanded = false;
            }
        }

        // ── Undo / redo (per tab) ─────────────────────────────────────────
        public void RecordUndo()
        {
            activeTab.UndoStack.Record(activeTab.SnapshotPaths());
        }

        public bool Undo()
        {
            List<string>? restored = activeTab.UndoStack.Undo(activeTab.SnapshotPaths());
            if (restored == null)
            {
                return false;
            }
            activeTab.ReplaceEntries(restored);
            activeTab.MarkDirty();
            return true;
        }

        public bool Redo()
        {
            List<string>? restored = activeTab.UndoStack.Redo(activeTab.SnapshotPaths());
            if (restored == null)
            {
                return false;
            }
            activeTab.ReplaceEntries(restored);
            activeTab.MarkDirty();
            return true;
        }

        public List<string> SnapshotPaths()
        {
            return activeTab.SnapshotPaths();
        }

        // ── Playlist mutations (always on the active tab) ─────────────────
        /// <summary>Adds music files and the contents of playlist files; unknown paths are ignored.</summary>
        public int AddPaths(IEnumerable<string> paths)
        {
            List<string> collected = new List<string>();
            foreach (string path in paths)
            {
                CollectPaths(path, collected);
            }
            if (collected.Count == 0)
            {
                return 0;
            }

            RecordUndo();
            foreach (string path in collected)
            {
                activeTab.Entries.Add(new PlaylistEntry(path));
            }
            activeTab.MarkDirty();
            return collected.Count;
        }

        public void LoadPlaylist(string playlistPath)
        {
            List<string> entries = PlaylistFileService.Load(playlistPath);
            bool tabWasEmpty = activeTab.Entries.Count == 0;
            RecordUndo();
            activeTab.ReplaceEntries(entries);
            activeTab.FilePath = playlistPath;
            activeTab.MarkClean();
            if (tabWasEmpty)
            {
                string loadedName = Path.GetFileNameWithoutExtension(playlistPath);
                if (!string.IsNullOrWhiteSpace(loadedName))
                {
                    activeTab.Name = loadedName;
                }
            }
        }

        public int RemoveEntries(IEnumerable<PlaylistEntry> entries)
        {
            List<PlaylistEntry> targets = entries.ToList();
            if (targets.Count == 0)
            {
                return 0;
            }

            RecordUndo();
            int removed = 0;
            foreach (PlaylistEntry entry in targets)
            {
                if (activeTab.Entries.Remove(entry))
                {
                    removed++;
                }
            }
            if (removed > 0)
            {
                activeTab.MarkDirty();
            }
            return removed;
        }

        /// <summary>Replaces the active playlist with an already ordered list (undo snapshot is taken first).</summary>
        public void SortActiveTab(IReadOnlyList<PlaylistEntry> ordered)
        {
            if (ordered.Count == 0)
            {
                return;
            }

            RecordUndo();
            ObservableCollection<PlaylistEntry> list = activeTab.Entries;
            list.Clear();
            foreach (PlaylistEntry entry in ordered)
            {
                list.Add(entry);
            }
            activeTab.MarkDirty();
        }

        /// <summary>Moves the selection one slot up (-1) or down (1), keeping blocks of selected items together.</summary>
        public void MoveEntries(IReadOnlyList<PlaylistEntry> entries, int direction)
        {
            if (entries.Count == 0 || direction == 0)
            {
                return;
            }

            HashSet<PlaylistEntry> selection = new HashSet<PlaylistEntry>(entries);
            ObservableCollection<PlaylistEntry> list = activeTab.Entries;
            int lastIndex = list.Count - 1;
            if (lastIndex < 0)
            {
                return;
            }

            bool canMove = false;
            if (direction < 0)
            {
                for (int index = 1; index <= lastIndex; index++)
                {
                    if (selection.Contains(list[index]) && !selection.Contains(list[index - 1]))
                    {
                        canMove = true;
                        break;
                    }
                }
            }
            else
            {
                for (int index = 0; index < lastIndex; index++)
                {
                    if (selection.Contains(list[index]) && !selection.Contains(list[index + 1]))
                    {
                        canMove = true;
                        break;
                    }
                }
            }

            if (!canMove)
            {
                return;
            }

            RecordUndo();
            if (direction < 0)
            {
                for (int index = 0; index < list.Count; index++)
                {
                    if (!selection.Contains(list[index]) || index == 0)
                    {
                        continue;
                    }
                    if (selection.Contains(list[index - 1]))
                    {
                        continue;
                    }
                    list.Move(index, index - 1);
                }
            }
            else
            {
                for (int index = list.Count - 1; index >= 0; index--)
                {
                    if (!selection.Contains(list[index]) || index == list.Count - 1)
                    {
                        continue;
                    }
                    if (selection.Contains(list[index + 1]))
                    {
                        continue;
                    }
                    list.Move(index, index + 1);
                }
            }

            activeTab.MarkDirty();
        }

        /// <summary>Re-inserts already selected entries at a new position (drag and drop inside the playlist).</summary>
        public void ReorderEntries(IReadOnlyList<PlaylistEntry> entries, int insertIndex)
        {
            ObservableCollection<PlaylistEntry> list = activeTab.Entries;
            List<PlaylistEntry> moving = entries.Where(entry => list.Contains(entry)).ToList();
            if (moving.Count == 0)
            {
                return;
            }

            RecordUndo();
            int targetIndex = insertIndex;
            foreach (PlaylistEntry entry in moving)
            {
                int currentIndex = list.IndexOf(entry);
                if (currentIndex >= 0 && currentIndex < targetIndex)
                {
                    targetIndex--;
                }
                list.Remove(entry);
            }
            if (targetIndex < 0)
            {
                targetIndex = 0;
            }
            if (targetIndex > list.Count)
            {
                targetIndex = list.Count;
            }
            foreach (PlaylistEntry entry in moving)
            {
                list.Insert(targetIndex, entry);
                targetIndex++;
            }
            activeTab.MarkDirty();
        }

        public void RefreshEntryStates()
        {
            foreach (PlaylistTab tab in Tabs)
            {
                foreach (PlaylistEntry entry in tab.Entries)
                {
                    entry.Refresh();
                }
            }
        }

        private static void CollectPaths(string path, List<string> sink)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (MusicFormats.IsPlaylistFile(path))
            {
                try
                {
                    sink.AddRange(PlaylistFileService.Load(path));
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (System.Security.SecurityException)
                {
                }
                return;
            }

            if (MusicFormats.IsMusicFile(path))
            {
                sink.Add(path);
            }
        }
    }
}
