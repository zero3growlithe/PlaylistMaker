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
                List<string> haystack = BuildHaystack(row, includeFolderNames, allFolderLevels);

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
        private static List<string> BuildHaystack(TagEditRow row, bool includeFolderNames, bool allFolderLevels)
        {
            List<string> haystack = new List<string>();
            string fileName = Path.GetFileNameWithoutExtension(row.FilePath);
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = row.FileName;
            }
            if (!string.IsNullOrEmpty(fileName))
            {
                haystack.Add(fileName);
            }

            if (includeFolderNames)
            {
                AddFolderNames(row.FolderPath, allFolderLevels, haystack);
            }
            return haystack;
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
