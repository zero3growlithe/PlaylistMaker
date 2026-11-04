using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using PlaylistMaker.Services;

namespace PlaylistMaker.Models
{
    /// <summary>One playlist line: an absolute path plus its current on-disk state and (lazily) its tags.</summary>
    public sealed class PlaylistEntry : INotifyPropertyChanged
    {
        private const string UnknownText = "\u2014";

        private int index;
        private bool metadataLoaded;
        private TrackMetadata? metadata;
        private bool durationLoaded;
        private TimeSpan? duration;
        private bool sizeLoaded;
        private long fileSize;

        public PlaylistEntry(string fullPath)
        {
            FullPath = fullPath ?? string.Empty;
            IsMissing = ComputeIsMissing(FullPath);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string FullPath { get; private set; }

        /// <summary>1-based position in the playlist; kept up to date by <see cref="ViewModels.PlaylistTab"/>.</summary>
        public int Index
        {
            get => index;
            set
            {
                if (index == value)
                {
                    return;
                }
                index = value;
                RaisePropertyChanged(nameof(Index));
            }
        }

        public string FileName => Path.GetFileName(FullPath);

        public string DisplayName
        {
            get
            {
                string name = FileName;
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

        /// <summary>Tag title when there is one, otherwise the file name without its extension.</summary>
        public string TrackName
        {
            get
            {
                string? title = Metadata?.Title;
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return title!;
                }
                string stem = Path.GetFileNameWithoutExtension(FullPath);
                return string.IsNullOrEmpty(stem) ? FullPath : stem;
            }
        }

        public string Album => Metadata?.Album ?? string.Empty;

        public string Artist => Metadata?.Artist ?? string.Empty;

        public int TrackNumber => Metadata?.TrackNumber ?? 0;

        public bool HasTags => Metadata?.FromTags ?? false;

        public TimeSpan? Duration
        {
            get
            {
                if (!durationLoaded)
                {
                    duration = MediaDurationReader.Read(FullPath);
                    durationLoaded = true;
                }
                return duration;
            }
        }

        public string DurationText
        {
            get
            {
                TimeSpan? value = Duration;
                if (value == null || value.Value <= TimeSpan.Zero)
                {
                    return UnknownText;
                }
                return FormatDuration(value.Value);
            }
        }

        public long FileSize
        {
            get
            {
                if (!sizeLoaded)
                {
                    fileSize = ReadFileSize(FullPath);
                    sizeLoaded = true;
                }
                return fileSize;
            }
        }

        public string FileSizeText => FileSize > 0 ? FormatSize(FileSize) : UnknownText;

        private TrackMetadata? Metadata
        {
            get
            {
                if (!metadataLoaded)
                {
                    metadata = TagReader.Read(FullPath);
                    metadataLoaded = true;
                }
                return metadata;
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
            metadataLoaded = false;
            metadata = null;
            durationLoaded = false;
            duration = null;
            sizeLoaded = false;
            fileSize = 0;

            RaisePropertyChanged(nameof(FullPath));
            RaisePropertyChanged(nameof(FileName));
            RaisePropertyChanged(nameof(DisplayName));
            RaisePropertyChanged(nameof(DisplayFolder));
            RaisePropertyChanged(nameof(TrackName));
            RaisePropertyChanged(nameof(Album));
            RaisePropertyChanged(nameof(Artist));
            RaisePropertyChanged(nameof(TrackNumber));
            RaisePropertyChanged(nameof(HasTags));
            RaisePropertyChanged(nameof(Duration));
            RaisePropertyChanged(nameof(DurationText));
            RaisePropertyChanged(nameof(FileSize));
            RaisePropertyChanged(nameof(FileSizeText));
            Refresh();
        }

        private static string FormatDuration(TimeSpan value)
        {
            if (value.TotalHours >= 1.0)
            {
                return ((int)value.TotalHours).ToString(CultureInfo.InvariantCulture)
                    + ":" + value.Minutes.ToString("00", CultureInfo.InvariantCulture)
                    + ":" + value.Seconds.ToString("00", CultureInfo.InvariantCulture);
            }
            return value.Minutes.ToString(CultureInfo.InvariantCulture)
                + ":" + value.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
            {
                return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            }
            if (bytes >= 1024L * 1024L)
            {
                return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            }
            if (bytes >= 1024L)
            {
                return (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
            }
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        private static long ReadFileSize(string fullPath)
        {
            try
            {
                FileInfo info = new FileInfo(fullPath);
                return info.Exists ? info.Length : 0;
            }
            catch (ArgumentException)
            {
                return 0;
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
            catch (NotSupportedException)
            {
                return 0;
            }
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
