using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PlaylistMaker.Services
{
    /// <summary>Which extensions count as music and which as playlists, plus dialog filters built from them.</summary>
    public static class MusicFormats
    {
        private static readonly HashSet<string> MusicExtensionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".mp2", ".flac", ".m4a", ".m4b", ".aac", ".ogg", ".oga", ".opus",
            ".wav", ".wma", ".aiff", ".aif", ".ape", ".wv", ".alac", ".dsf", ".dff",
            ".mpc", ".spx", ".tta", ".mid", ".midi", ".mod", ".xm", ".it", ".s3m",
            ".vgm", ".vgz", ".nsf", ".spc", ".psf", ".gbs"
        };

        private static readonly HashSet<string> PlaylistExtensionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".m3u", ".m3u8", ".pls"
        };

        public static IReadOnlyCollection<string> MusicExtensions => MusicExtensionSet;

        public static IReadOnlyCollection<string> PlaylistExtensions => PlaylistExtensionSet;

        public static bool IsMusicFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }
            return MusicExtensionSet.Contains(Path.GetExtension(path));
        }

        public static bool IsPlaylistFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }
            return PlaylistExtensionSet.Contains(Path.GetExtension(path));
        }

        public static string MusicDialogFilter => BuildFilter("Music files", MusicExtensionSet);

        public static string PlaylistDialogFilter => BuildFilter("Playlists", PlaylistExtensionSet);

        public static string PlaylistSaveFilter =>
            "M3U8 playlist (*.m3u8)|*.m3u8|M3U playlist (*.m3u)|*.m3u|PLS playlist (*.pls)|*.pls";

        public static string MusicAndPlaylistDialogFilter =>
            BuildFilter("Music and playlists", MusicExtensionSet.Concat(PlaylistExtensionSet));

        private static string BuildFilter(string description, IEnumerable<string> extensions)
        {
            List<string> patterns = extensions
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
                .Select(extension => "*" + extension)
                .ToList();
            StringBuilder builder = new StringBuilder();
            builder.Append(description).Append(" (").Append(string.Join(";", patterns)).Append(")|");
            builder.Append(string.Join(";", patterns));
            return builder.ToString();
        }
    }
}
