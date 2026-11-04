using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using PlaylistMaker.Models;
using PlaylistMaker.Services;

namespace PlaylistMaker.ViewModels
{
    /// <summary>State of the window: the flattened explorer tree and the playlist being built.</summary>
    public sealed class MainViewModel
    {
        private readonly FileSystemTreeService treeService = new FileSystemTreeService();

        public ObservableCollection<FileSystemNode> VisibleNodes { get; } = new ObservableCollection<FileSystemNode>();

        public List<FileSystemNode> Roots { get; private set; } = new List<FileSystemNode>();

        public ObservableCollection<PlaylistEntry> PlaylistEntries { get; } = new ObservableCollection<PlaylistEntry>();

        public PlaylistUndoStack UndoStack { get; } = new PlaylistUndoStack();

        /// <summary>Where the playlist currently in the list was loaded from / last saved to.</summary>
        public string? CurrentPlaylistPath { get; set; }

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

        public int MissingCount => PlaylistEntries.Count(entry => entry.IsMissing);

        public void Initialize()
        {
            ReloadTree();
        }

        public void ReloadTree()
        {
            Roots = treeService.BuildRoots();
            RebuildVisibleNodes();
        }

        public void RebuildVisibleNodes()
        {
            VisibleNodes.Clear();
            foreach (FileSystemNode root in Roots)
            {
                AppendVisible(root);
            }
        }

        private void AppendVisible(FileSystemNode node)
        {
            VisibleNodes.Add(node);
            if (!node.IsExpanded)
            {
                return;
            }
            foreach (FileSystemNode child in node.Children)
            {
                AppendVisible(child);
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

        // ── Undo / redo ───────────────────────────────────────────────────
        public void RecordUndo()
        {
            UndoStack.Record(SnapshotPaths());
        }

        public bool Undo()
        {
            List<string>? restored = UndoStack.Undo(SnapshotPaths());
            if (restored == null)
            {
                return false;
            }
            RestoreFromPaths(restored);
            return true;
        }

        public bool Redo()
        {
            List<string>? restored = UndoStack.Redo(SnapshotPaths());
            if (restored == null)
            {
                return false;
            }
            RestoreFromPaths(restored);
            return true;
        }

        public List<string> SnapshotPaths()
        {
            return PlaylistEntries.Select(entry => entry.FullPath).ToList();
        }

        private void RestoreFromPaths(List<string> paths)
        {
            PlaylistEntries.Clear();
            foreach (string path in paths)
            {
                PlaylistEntries.Add(new PlaylistEntry(path));
            }
        }

        // ── Playlist mutations ────────────────────────────────────────────
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
                PlaylistEntries.Add(new PlaylistEntry(path));
            }
            return collected.Count;
        }

        public void LoadPlaylist(string playlistPath)
        {
            List<string> entries = PlaylistFileService.Load(playlistPath);
            RecordUndo();
            PlaylistEntries.Clear();
            foreach (string entry in entries)
            {
                PlaylistEntries.Add(new PlaylistEntry(entry));
            }
            CurrentPlaylistPath = playlistPath;
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
                if (PlaylistEntries.Remove(entry))
                {
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>Moves the selection one slot up (-1) or down (1), keeping blocks of selected items together.</summary>
        public void MoveEntries(IReadOnlyList<PlaylistEntry> entries, int direction)
        {
            if (entries.Count == 0 || direction == 0)
            {
                return;
            }

            HashSet<PlaylistEntry> selection = new HashSet<PlaylistEntry>(entries);
            int lastIndex = PlaylistEntries.Count - 1;
            if (lastIndex < 0)
            {
                return;
            }

            bool canMove = false;
            if (direction < 0)
            {
                for (int index = 1; index <= lastIndex; index++)
                {
                    if (selection.Contains(PlaylistEntries[index]) && !selection.Contains(PlaylistEntries[index - 1]))
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
                    if (selection.Contains(PlaylistEntries[index]) && !selection.Contains(PlaylistEntries[index + 1]))
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
                for (int index = 0; index < PlaylistEntries.Count; index++)
                {
                    if (!selection.Contains(PlaylistEntries[index]) || index == 0)
                    {
                        continue;
                    }
                    if (selection.Contains(PlaylistEntries[index - 1]))
                    {
                        continue;
                    }
                    PlaylistEntries.Move(index, index - 1);
                }
            }
            else
            {
                for (int index = PlaylistEntries.Count - 1; index >= 0; index--)
                {
                    if (!selection.Contains(PlaylistEntries[index]) || index == PlaylistEntries.Count - 1)
                    {
                        continue;
                    }
                    if (selection.Contains(PlaylistEntries[index + 1]))
                    {
                        continue;
                    }
                    PlaylistEntries.Move(index, index + 1);
                }
            }
        }

        /// <summary>Re-inserts already selected entries at a new position (drag and drop inside the playlist).</summary>
        public void ReorderEntries(IReadOnlyList<PlaylistEntry> entries, int insertIndex)
        {
            List<PlaylistEntry> moving = entries.Where(entry => PlaylistEntries.Contains(entry)).ToList();
            if (moving.Count == 0)
            {
                return;
            }

            RecordUndo();
            int targetIndex = insertIndex;
            foreach (PlaylistEntry entry in moving)
            {
                int currentIndex = PlaylistEntries.IndexOf(entry);
                if (currentIndex >= 0 && currentIndex < targetIndex)
                {
                    targetIndex--;
                }
                PlaylistEntries.Remove(entry);
            }
            if (targetIndex < 0)
            {
                targetIndex = 0;
            }
            if (targetIndex > PlaylistEntries.Count)
            {
                targetIndex = PlaylistEntries.Count;
            }
            foreach (PlaylistEntry entry in moving)
            {
                PlaylistEntries.Insert(targetIndex, entry);
                targetIndex++;
            }
        }

        public void RefreshEntryStates()
        {
            foreach (PlaylistEntry entry in PlaylistEntries)
            {
                entry.Refresh();
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
