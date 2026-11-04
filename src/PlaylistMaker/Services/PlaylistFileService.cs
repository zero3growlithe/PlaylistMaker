using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PlaylistMaker.Models;

namespace PlaylistMaker.Services
{
    /// <summary>Loads and writes M3U / M3U8 / PLS playlists. Entries go out relative when the playlist lives on the same drive.</summary>
    public static class PlaylistFileService
    {
        public static List<string> Load(string playlistPath)
        {
            List<string> entries = new List<string>();
            string fullPlaylistPath = Path.GetFullPath(playlistPath);
            string playlistDirectory = Path.GetDirectoryName(fullPlaylistPath) ?? string.Empty;
            bool isPls = string.Equals(Path.GetExtension(playlistPath), ".pls", StringComparison.OrdinalIgnoreCase);

            foreach (string rawLine in File.ReadAllLines(fullPlaylistPath))
            {
                string line = rawLine.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (isPls)
                {
                    if (!line.StartsWith("File", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    int separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }
                    line = line.Substring(separatorIndex + 1).Trim();
                }

                string resolved = ResolveEntry(playlistDirectory, line);
                if (resolved.Length > 0)
                {
                    entries.Add(resolved);
                }
            }

            return entries;
        }

        public static void Save(string playlistPath, IReadOnlyList<string> absolutePaths)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("#EXTM3U\r\n");
            foreach (string absolutePath in absolutePaths)
            {
                TrackMetadata metadata = TagReader.Read(absolutePath);
                string title = metadata.Title ?? Path.GetFileNameWithoutExtension(absolutePath);
                builder.Append("#EXTINF:-1,").Append(title).Append("\r\n");
                builder.Append(BuildEntry(playlistPath, absolutePath)).Append("\r\n");
            }

            File.WriteAllText(playlistPath, builder.ToString(), new UTF8Encoding(false));
        }

        /// <summary>Relative path with forward slashes when possible, absolute path otherwise (other drive, UNC mismatch).</summary>
        public static string BuildEntry(string playlistPath, string absolutePath)
        {
            try
            {
                string playlistDirectory = Path.GetDirectoryName(Path.GetFullPath(playlistPath)) ?? string.Empty;
                string relative = Path.GetRelativePath(playlistDirectory, absolutePath);
                if (!Path.IsPathRooted(relative))
                {
                    return relative.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
            catch (PathTooLongException)
            {
            }

            return absolutePath;
        }

        private static string ResolveEntry(string playlistDirectory, string entry)
        {
            string normalized = entry.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            try
            {
                if (Path.IsPathRooted(normalized))
                {
                    return Path.GetFullPath(normalized);
                }
                if (playlistDirectory.Length == 0)
                {
                    return Path.GetFullPath(normalized);
                }
                return Path.GetFullPath(Path.Combine(playlistDirectory, normalized));
            }
            catch (ArgumentException)
            {
                return entry;
            }
            catch (NotSupportedException)
            {
                return entry;
            }
            catch (PathTooLongException)
            {
                return entry;
            }
        }
    }
}
