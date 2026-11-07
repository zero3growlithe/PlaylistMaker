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

        /// <summary>
        /// Rewrites an existing playlist in place so every entry that can be expressed relative to
        /// the playlist file becomes relative (forward slashes), which is what makes the folder and
        /// the playlist movable as a pair. "#EXTM3U", #EXTINF and comment lines are kept byte for
        /// byte, entries on another drive stay absolute because no relative path exists for them,
        /// and the file is only written when at least one entry actually changed.
        /// </summary>
        public static RelativePathConversion ConvertToRelativePaths(string playlistPath)
        {
            string fullPlaylistPath = Path.GetFullPath(playlistPath);
            string playlistDirectory = Path.GetDirectoryName(fullPlaylistPath) ?? string.Empty;
            bool isPls = string.Equals(Path.GetExtension(fullPlaylistPath), ".pls", StringComparison.OrdinalIgnoreCase);

            bool hasByteOrderMark;
            string text = ReadPlaylistText(fullPlaylistPath, out hasByteOrderMark);
            List<string> lines = SplitLinesKeepingEndings(text);
            RelativePathConversion result = new RelativePathConversion();

            for (int index = 0; index < lines.Count; index++)
            {
                string line = lines[index];
                string body = line.TrimEnd('\r', '\n');
                string ending = line.Substring(body.Length);
                string trimmed = body.Trim().TrimStart('\uFEFF');
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int valueStart;
                string value;
                if (isPls)
                {
                    if (!trimmed.StartsWith("File", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    int separatorIndex = body.IndexOf('=');
                    if (separatorIndex < 0)
                    {
                        continue;
                    }
                    valueStart = separatorIndex + 1;
                    while (valueStart < body.Length && char.IsWhiteSpace(body[valueStart]))
                    {
                        valueStart++;
                    }
                    value = body.Substring(valueStart).Trim();
                }
                else
                {
                    valueStart = body.IndexOf(trimmed, StringComparison.Ordinal);
                    value = trimmed;
                }

                result.EntryCount++;
                if (value.Length == 0)
                {
                    continue;
                }

                string normalized = value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                if (!Path.IsPathRooted(normalized))
                {
                    result.AlreadyRelativeCount++;
                    continue;
                }

                string relative = BuildEntry(fullPlaylistPath, ResolveEntry(playlistDirectory, value));
                if (Path.IsPathRooted(relative))
                {
                    result.StillAbsoluteCount++;
                    continue;
                }

                lines[index] = body.Substring(0, valueStart) + relative + ending;
                result.ConvertedCount++;
            }

            if (result.FileChanged)
            {
                File.WriteAllText(fullPlaylistPath, string.Concat(lines), new UTF8Encoding(hasByteOrderMark));
            }

            return result;
        }

        /// <summary>
        /// Reads the playlist as UTF-8, the only encoding this app writes. A playlist in a legacy
        /// code page is refused instead of being rewritten, because re-encoding it would mangle
        /// every non-ASCII title in the file.
        /// </summary>
        private static string ReadPlaylistText(string fullPlaylistPath, out bool hasByteOrderMark)
        {
            byte[] raw = File.ReadAllBytes(fullPlaylistPath);
            hasByteOrderMark = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            int start = hasByteOrderMark ? 3 : 0;
            try
            {
                return new UTF8Encoding(false, true).GetString(raw, start, raw.Length - start);
            }
            catch (DecoderFallbackException)
            {
                throw new InvalidOperationException(
                    "it is not a UTF-8 playlist, so rewriting it could corrupt the track names");
            }
        }

        /// <summary>Splits on LF and keeps each line's own CR/LF ending, so untouched lines stay identical.</summary>
        private static List<string> SplitLinesKeepingEndings(string text)
        {
            List<string> lines = new List<string>();
            int start = 0;
            while (start < text.Length)
            {
                int breakIndex = text.IndexOf('\n', start);
                if (breakIndex < 0)
                {
                    lines.Add(text.Substring(start));
                    break;
                }
                lines.Add(text.Substring(start, breakIndex - start + 1));
                start = breakIndex + 1;
            }
            return lines;
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

    /// <summary>What turning a playlist's entries into relative paths did, so the caller can report it.</summary>
    public sealed class RelativePathConversion
    {
        /// <summary>Path lines found in the file (comments, #EXTM3U and #EXTINF excluded).</summary>
        public int EntryCount { get; set; }

        /// <summary>Entries rewritten from an absolute path into a relative one.</summary>
        public int ConvertedCount { get; set; }

        /// <summary>Entries that were relative already and were left untouched.</summary>
        public int AlreadyRelativeCount { get; set; }

        /// <summary>Entries that cannot be relative - the music sits on another drive or another share.</summary>
        public int StillAbsoluteCount { get; set; }

        /// <summary>False when the file already was as relative as it can get, in which case it was not rewritten.</summary>
        public bool FileChanged => ConvertedCount > 0;
    }
}
