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
        public static List<TagFillPlan> Build(IReadOnlyList<TagEditRow> rows, bool fillArtist, bool fillAlbum)
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
                string fileName = Path.GetFileNameWithoutExtension(row.FilePath);
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = row.FileName;
                }

                string? artist = null;
                if (fillArtist)
                {
                    // a name that appears in the file name wins; otherwise the row's own entry counts
                    artist = MatchInFileName(fileName, artistCandidates) ?? Clean(row.Artist);
                }

                string? album = null;
                if (fillAlbum)
                {
                    album = MatchInFileName(fileName, albumCandidates) ?? Clean(row.Album);
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

        /// <summary>Longest match first, so "Daft Punk Alive" wins over "Daft Punk" for the same file.</summary>
        public static string? MatchInFileName(string fileName, IReadOnlyList<string> candidates)
        {
            if (string.IsNullOrEmpty(fileName) || candidates.Count == 0)
            {
                return null;
            }

            foreach (string candidate in candidates)
            {
                if (candidate.Length > 0 && fileName.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return candidate;
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
