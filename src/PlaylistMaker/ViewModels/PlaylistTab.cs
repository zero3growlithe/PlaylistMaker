using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using PlaylistMaker.Models;
using PlaylistMaker.Services;

namespace PlaylistMaker.ViewModels
{
    /// <summary>One playlist document behind a tab: its entries, its own undo history and its dirty state.</summary>
    public sealed class PlaylistTab : INotifyPropertyChanged
    {
        private string name;
        private string editingName;
        private bool isRenaming;
        private bool isDirty;
        private string? filePath;

        public PlaylistTab(string name)
        {
            this.name = name;
            editingName = name;
            Entries.CollectionChanged += OnEntriesChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Tab label, also used as the default file name when saving.</summary>
        public string Name
        {
            get => name;
            set
            {
                if (name == value || string.IsNullOrWhiteSpace(value))
                {
                    return;
                }
                name = value.Trim();
                if (!isRenaming)
                {
                    editingName = name;
                }
                RaisePropertyChanged(nameof(Name));
                RaisePropertyChanged(nameof(DisplayName));
                RaisePropertyChanged(nameof(EditingName));
            }
        }

        /// <summary>Backing value of the inline rename editor.</summary>
        public string EditingName
        {
            get => editingName;
            set
            {
                if (editingName == value)
                {
                    return;
                }
                editingName = value ?? string.Empty;
                RaisePropertyChanged(nameof(EditingName));
            }
        }

        public bool IsRenaming
        {
            get => isRenaming;
            set
            {
                if (isRenaming == value)
                {
                    return;
                }
                isRenaming = value;
                RaisePropertyChanged(nameof(IsRenaming));
                RaisePropertyChanged(nameof(NameTextVisibility));
                RaisePropertyChanged(nameof(EditorVisibility));
            }
        }

        public Visibility NameTextVisibility => isRenaming ? Visibility.Collapsed : Visibility.Visible;

        public Visibility EditorVisibility => isRenaming ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>What the tab strip shows - a trailing star marks unsaved changes.</summary>
        public string DisplayName => isDirty ? name + " *" : name;

        public string HeaderToolTip => filePath ?? "Not saved yet";

        public ObservableCollection<PlaylistEntry> Entries { get; } = new ObservableCollection<PlaylistEntry>();

        public PlaylistUndoStack UndoStack { get; } = new PlaylistUndoStack();

        public string? FilePath
        {
            get => filePath;
            set
            {
                if (filePath == value)
                {
                    return;
                }
                filePath = value;
                RaisePropertyChanged(nameof(FilePath));
                RaisePropertyChanged(nameof(HeaderToolTip));
            }
        }

        public bool IsDirty
        {
            get => isDirty;
            private set
            {
                if (isDirty == value)
                {
                    return;
                }
                isDirty = value;
                RaisePropertyChanged(nameof(IsDirty));
                RaisePropertyChanged(nameof(DisplayName));
            }
        }

        /// <summary>True when closing this tab would throw work away.</summary>
        public bool NeedsSavePrompt => Entries.Count > 0 && (IsDirty || string.IsNullOrEmpty(FilePath));

        public void MarkDirty()
        {
            IsDirty = true;
        }

        public void MarkClean()
        {
            IsDirty = false;
        }

        public List<string> SnapshotPaths()
        {
            return Entries.Select(entry => entry.FullPath).ToList();
        }

        public void ReplaceEntries(IEnumerable<string> paths)
        {
            Entries.Clear();
            foreach (string path in paths)
            {
                Entries.Add(new PlaylistEntry(path));
            }
        }

        /// <summary>The entry added/removed/replaced, the Index column and the reorder arrows are recomputed from this.</summary>
        private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            for (int index = 0; index < Entries.Count; index++)
            {
                Entries[index].Index = index + 1;
            }
        }

        public void RaiseHeaderChanged()
        {
            RaisePropertyChanged(nameof(DisplayName));
            RaisePropertyChanged(nameof(HeaderToolTip));
        }

        private void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
