using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace PlaylistMaker.Models
{
    /// <summary>A drive, folder or file shown in the explorer tree.</summary>
    public sealed class FileSystemNode : INotifyPropertyChanged
    {
        private bool isExpanded;
        private bool isDropTarget;

        public FileSystemNode(string fullPath, string name, bool isFolder, int level, bool isMusic, bool isPlaylist)
        {
            FullPath = fullPath;
            Name = name;
            IsFolder = isFolder;
            Level = level;
            IsMusic = isMusic;
            IsPlaylist = isPlaylist;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string FullPath { get; }

        public string Name { get; }

        public bool IsFolder { get; }

        public bool IsMusic { get; }

        public bool IsPlaylist { get; }

        /// <summary>Depth in the tree; drives the indentation of the flattened list.</summary>
        public int Level { get; }

        public Thickness IndentMargin => new Thickness(Level * 16.0, 0, 0, 0);

        public bool IsFolderOrPlaylist => IsFolder || IsPlaylist;

        public bool IsExpanded
        {
            get => isExpanded;
            set
            {
                if (isExpanded == value)
                {
                    return;
                }
                isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChevronGlyph)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExpandButtonVisibility)));
            }
        }

        public string ChevronGlyph => IsExpanded ? "\u25BC" : "\u25B6";

        public Visibility ExpandButtonVisibility => IsFolder ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>True while a drag hovers over this row, so the template can highlight the drop target.</summary>
        public bool IsDropTarget
        {
            get => isDropTarget;
            set
            {
                if (isDropTarget == value)
                {
                    return;
                }
                isDropTarget = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDropTarget)));
            }
        }

        public bool ChildrenLoaded { get; set; }

        public bool ChildrenLoading { get; set; }

        public ObservableCollection<FileSystemNode> Children { get; } = new ObservableCollection<FileSystemNode>();
    }
}
