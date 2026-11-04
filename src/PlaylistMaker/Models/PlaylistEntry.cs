using System;
using System.ComponentModel;
using System.IO;

namespace PlaylistMaker.Models
{
    /// <summary>One playlist line: an absolute path plus its current on-disk state.</summary>
    public sealed class PlaylistEntry : INotifyPropertyChanged
    {
        public PlaylistEntry(string fullPath)
        {
            FullPath = fullPath ?? string.Empty;
            IsMissing = ComputeIsMissing(FullPath);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string FullPath { get; private set; }

        public string DisplayName
        {
            get
            {
                string name = Path.GetFileName(FullPath);
                return string.IsNullOrEmpty(name) ? FullPath : name;
            }
        }

        public string DisplayFolder
        {
            get
            {
                try
                {
                    return Path.GetDirectoryName(FullPath) ?? string.Empty;
                }
                catch (ArgumentException)
                {
                    return string.Empty;
                }
            }
        }

        public bool IsMissing { get; private set; }

        /// <summary>True when a file exists at <see cref="FullPath"/> right now.</summary>
        public bool Exists => !IsMissing;

        public void Refresh()
        {
            IsMissing = ComputeIsMissing(FullPath);
            RaisePropertyChanged(nameof(IsMissing));
            RaisePropertyChanged(nameof(Exists));
        }

        public void PointTo(string fullPath)
        {
            FullPath = fullPath ?? string.Empty;
            RaisePropertyChanged(nameof(FullPath));
            RaisePropertyChanged(nameof(DisplayName));
            RaisePropertyChanged(nameof(DisplayFolder));
            Refresh();
        }

        private static bool ComputeIsMissing(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return true;
            }
            try
            {
                return !File.Exists(fullPath);
            }
            catch (ArgumentException)
            {
                return true;
            }
            catch (NotSupportedException)
            {
                return true;
            }
            catch (IOException)
            {
                return true;
            }
        }

        private void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
