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
    /// Turns the search terms the user typed into per-track tag values: a term typed once is written
    /// into every track whose file name contains it, which is what makes one pass over a folder
    /// enough. The term is what is looked for, the artist and album are what gets written - they are
    /// deliberately independent, so typing the artist "Daft Punk" does not also search for it.
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

            // one list per tag, so a row that only fills in an album can still borrow an artist that
            // another row's search term put on the same track
            List<TermValue> artistCandidates = new List<TermValue>();
            List<TermValue> albumCandidates = new List<TermValue>();
            foreach (TagEditRow row in rows)
            {
                string? term = ResolveSearchTerm(row.SearchTerm, row.FileName);
                if (term == null)
                {
                    continue;
                }

                AddCandidate(artistCandidates, term, fillArtist ? Clean(row.Artist) : null);
                AddCandidate(albumCandidates, term, fillAlbum ? Clean(row.Album) : null);
            }

            artistCandidates = SortLongestFirst(artistCandidates);
            albumCandidates = SortLongestFirst(albumCandidates);

            foreach (TagEditRow row in rows)
            {
                List<string> haystack = BuildHaystack(row.FilePath, includeFolderNames, allFolderLevels);

                string? artist = fillArtist ? MatchLongest(haystack, artistCandidates) : null;
                string? album = fillAlbum ? MatchLongest(haystack, albumCandidates) : null;
                if (artist == null && album == null)
                {
                    // no search term of this selection turns up in this file or folder name: skipped
                    continue;
                }

                plans.Add(new TagFillPlan(row.FilePath, artist, album));
            }

            return plans;
        }

        /// <summary>The texts a search term is looked for in: the file name, plus folder names when asked.</summary>
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
        /// the user answered for always takes what was typed, every other queued file takes it when the
        /// search term turns up in its file name (or in a folder name, when asked). Matched files leave
        /// the queue, so no file is ever asked about twice. An empty search term targets the answered
        /// file's own name, which covers the discs of a multi-disc release.
        /// </summary>
        public static List<TagFillPlan> ApplyAcceptedEntry(
            IList<string> queue,
            string acceptedPath,
            string? searchTerm,
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

            string? term = ResolveSearchTerm(searchTerm, TryGetFileNameWithoutExtension(acceptedPath));
            List<TermValue> artistCandidates = new List<TermValue>();
            List<TermValue> albumCandidates = new List<TermValue>();
            if (term != null)
            {
                AddCandidate(artistCandidates, term, cleanArtist);
                AddCandidate(albumCandidates, term, cleanAlbum);
            }

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

        /// <summary>
        /// What the matching runs against for one row: the typed search term, or the file's own name
        /// when that box was left empty - a row always describes at least the file it belongs to.
        /// </summary>
        private static string? ResolveSearchTerm(string? searchTerm, string? fileName)
        {
            return Clean(searchTerm) ?? Clean(fileName);
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
        /// Adds the folder the track sits in, or every folder above it, so a search term that only
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
            // a drive ("C:\") is not a name anyone types, and matching it would tag everything
            if (segment.Length == 0 || segment.EndsWith(":", StringComparison.Ordinal))
            {
                return;
            }
            haystack.Add(segment);
        }

        /// <summary>Longest search term first, so "Daft Punk Alive" wins over "Daft Punk" for the same track.</summary>
        private static string? MatchLongest(IReadOnlyList<string> haystack, IReadOnlyList<TermValue> candidates)
        {
            foreach (TermValue candidate in candidates)
            {
                foreach (string text in haystack)
                {
                    if (text.IndexOf(candidate.Term, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return candidate.Value;
                    }
                }
            }
            return null;
        }

        private static void AddCandidate(List<TermValue> candidates, string term, string? value)
        {
            if (value == null)
            {
                return;
            }

            foreach (TermValue candidate in candidates)
            {
                if (string.Equals(candidate.Term, term, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            candidates.Add(new TermValue(term, value));
        }

        private static List<TermValue> SortLongestFirst(List<TermValue> candidates)
        {
            List<TermValue> sorted = new List<TermValue>(candidates);
            sorted.Sort((left, right) =>
            {
                int byLength = right.Term.Length.CompareTo(left.Term.Length);
                if (byLength != 0)
                {
                    return byLength;
                }
                return string.Compare(left.Term, right.Term, StringComparison.OrdinalIgnoreCase);
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

        /// <summary>One typed search term paired with the value that is written where it turns up.</summary>
        private sealed class TermValue
        {
            public TermValue(string term, string value)
            {
                Term = term;
                Value = value;
            }

            public string Term { get; }

            public string Value { get; }
        }
    }
}
