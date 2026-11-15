using System;
using System.Collections.Generic;
using System.IO;
using PlaylistMaker.Models;

namespace PlaylistMaker.Services
{
    /// <summary>One track plus the artist/album that should end up in its tags.</summary>
    public sealed class TagFillPlan
    {
        public TagFillPlan(string filePath, string? artist, string? album)
        {
            FilePath = filePath ?? string.Empty;
            Artist = artist;
            Album = album;
        }

        public string FilePath { get; }

        /// <summary>Artist to write, or null when the artist tag of this track is left alone.</summary>
        public string? Artist { get; }

        /// <summary>Album to write, or null when the album tag of this track is left alone.</summary>
        public string? Album { get; }
    }

    /// <summary>
    /// Turns the names the user typed into per-track tag values: a name typed once is written into
    /// every track whose file name contains it, which is what makes one pass over a folder enough.
    /// </summary>
    public static class TagFillPlanner
    {
        public static List<TagFillPlan> Build(
            IReadOnlyList<TagEditRow> rows,
            bool fillArtist,
            bool fillAlbum,
            bool includeFolderNames,
            bool allFolderLevels)
        {
            List<TagFillPlan> plans = new List<TagFillPlan>();
            if (rows.Count == 0)
            {
                return plans;
            }

            List<string> artistCandidates = new List<string>();
            List<string> albumCandidates = new List<string>();
            foreach (TagEditRow row in rows)
            {
                if (fillArtist)
                {
                    AddCandidate(artistCandidates, row.Artist);
                }
                if (fillAlbum)
                {
                    AddCandidate(albumCandidates, row.Album);
                }
            }

            artistCandidates = SortLongestFirst(artistCandidates);
            albumCandidates = SortLongestFirst(albumCandidates);

            foreach (TagEditRow row in rows)
            {
                List<string> haystack = BuildHaystack(row.FilePath, includeFolderNames, allFolderLevels);

                string? artist = null;
                if (fillArtist)
                {
                    // a name that appears in the file name or in a folder name wins, otherwise the
                    // row's own entry counts
                    artist = MatchLongest(haystack, artistCandidates) ?? Clean(row.Artist);
                }

                string? album = null;
                if (fillAlbum)
                {
                    album = MatchLongest(haystack, albumCandidates) ?? Clean(row.Album);
                }

                if (artist == null && album == null)
                {
                    // nothing typed anywhere near this file name: the track is skipped
                    continue;
                }

                plans.Add(new TagFillPlan(row.FilePath, artist, album));
            }

            return plans;
        }

        /// <summary>The texts a typed name is looked for in: the file name, plus folder names when asked.</summary>
        public static List<string> BuildHaystack(string filePath, bool includeFolderNames, bool allFolderLevels)
        {
            List<string> haystack = new List<string>();
            string fileName = TryGetFileNameWithoutExtension(filePath);
            if (!string.IsNullOrEmpty(fileName))
            {
                haystack.Add(fileName);
            }

            if (includeFolderNames)
            {
                AddFolderNames(TryGetFolderPath(filePath), allFolderLevels, haystack);
            }
            return haystack;
        }

        /// <summary>
        /// Applies one accepted answer from the one-file-at-a-time window to the whole queue: the file
        /// the user answered for always takes what was typed, every other queued file takes it when its
        /// file name (or a folder name, when asked) contains it. Matched files leave the queue, so no
        /// file is ever asked about twice.
        /// </summary>
        public static List<TagFillPlan> ApplyAcceptedEntry(
            IList<string> queue,
            string acceptedPath,
            string? artist,
            string? album,
            bool includeFolderNames,
            bool allFolderLevels)
        {
            List<TagFillPlan> plans = new List<TagFillPlan>();
            string? cleanArtist = Clean(artist);
            string? cleanAlbum = Clean(album);
            if (cleanArtist == null && cleanAlbum == null)
            {
                return plans;
            }

            List<string> artistCandidates = cleanArtist == null
                ? new List<string>()
                : new List<string> { cleanArtist };
            List<string> albumCandidates = cleanAlbum == null
                ? new List<string>()
                : new List<string> { cleanAlbum };

            List<string> matched = new List<string>();
            foreach (string path in queue)
            {
                bool isAcceptedFile = string.Equals(path, acceptedPath, StringComparison.OrdinalIgnoreCase);
                string? plannedArtist = cleanArtist == null
                    ? null
                    : (isAcceptedFile ? cleanArtist : MatchLongest(BuildHaystack(path, includeFolderNames, allFolderLevels), artistCandidates));
                string? plannedAlbum = cleanAlbum == null
                    ? null
                    : (isAcceptedFile ? cleanAlbum : MatchLongest(BuildHaystack(path, includeFolderNames, allFolderLevels), albumCandidates));

                if (plannedArtist == null && plannedAlbum == null)
                {
                    continue;
                }

                plans.Add(new TagFillPlan(path, plannedArtist, plannedAlbum));
                matched.Add(path);
            }

            foreach (string path in matched)
            {
                queue.Remove(path);
            }
            return plans;
        }

        private static string TryGetFileNameWithoutExtension(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return string.Empty;
            }
            try
            {
                return Path.GetFileNameWithoutExtension(filePath) ?? string.Empty;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        private static string TryGetFolderPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return string.Empty;
            }
            try
            {
                return Path.GetDirectoryName(filePath) ?? string.Empty;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Adds the folder the track sits in, or every folder above it, so an album name that only
        /// appears in a folder name still reaches all the tracks below that folder.
        /// </summary>
        private static void AddFolderNames(string folderPath, bool allFolderLevels, List<string> haystack)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            string normalised = folderPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            normalised = PathUtilities.TrimTrailingSeparator(normalised);
            if (normalised.Length == 0)
            {
                return;
            }

            string[] segments = normalised.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return;
            }

            if (!allFolderLevels)
            {
                AddFolderName(segments[segments.Length - 1], haystack);
                return;
            }

            foreach (string segment in segments)
            {
                AddFolderName(segment, haystack);
            }
        }

        private static void AddFolderName(string segment, List<string> haystack)
        {
            // a drive ("C:") is not a name anyone types, and matching it would tag everything
            if (segment.Length == 0 || segment.EndsWith(":", StringComparison.Ordinal))
            {
                return;
            }
            haystack.Add(segment);
        }

        /// <summary>Longest candidate first, so "Daft Punk Alive" wins over "Daft Punk" for the same track.</summary>
        public static string? MatchLongest(IReadOnlyList<string> haystack, IReadOnlyList<string> candidates)
        {
            foreach (string candidate in candidates)
            {
                if (candidate.Length == 0)
                {
                    continue;
                }
                foreach (string text in haystack)
                {
                    if (text.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return candidate;
                    }
                }
            }
            return null;
        }

        private static void AddCandidate(List<string> candidates, string? value)
        {
            string? clean = Clean(value);
            if (clean == null)
            {
                return;
            }
            foreach (string candidate in candidates)
            {
                if (string.Equals(candidate, clean, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            candidates.Add(clean);
        }

        private static List<string> SortLongestFirst(List<string> candidates)
        {
            List<string> sorted = new List<string>(candidates);
            sorted.Sort((left, right) =>
            {
                int byLength = right.Length.CompareTo(left.Length);
                if (byLength != 0)
                {
                    return byLength;
                }
                return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            });
            return sorted;
        }

        private static string? Clean(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            string trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }
}
