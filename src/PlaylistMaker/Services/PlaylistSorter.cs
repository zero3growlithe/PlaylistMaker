using System;
using System.Collections.Generic;
using System.Linq;
using PlaylistMaker.Models;

namespace PlaylistMaker.Services
{
    /// <summary>Orders the entries of an already loaded playlist. Undo/redo sees the result as a new snapshot.</summary>
    public static class PlaylistSorter
    {
        public static List<PlaylistEntry> Sort(IReadOnlyList<PlaylistEntry> entries, PlaylistSortField field, bool descending)
        {
            List<PlaylistEntry> result = entries.ToList();
            if (field == PlaylistSortField.Shuffle)
            {
                Shuffle(result);
                return result;
            }

            Comparison<PlaylistEntry> comparison = BuildComparison(field);
            result.Sort((left, right) =>
            {
                int nullRank = CompareMissingRank(field, left, right);
                if (nullRank != 0)
                {
                    return nullRank;
                }

                int primary = comparison(left, right);
                if (descending)
                {
                    primary = -primary;
                }
                if (primary != 0)
                {
                    return primary;
                }

                return NaturalStringComparer.Instance.Compare(left.FullPath, right.FullPath);
            });
            return result;
        }

        /// <summary>Entries without the value a field sorts on go last, whichever direction is used.</summary>
        private static int CompareMissingRank(PlaylistSortField field, PlaylistEntry left, PlaylistEntry right)
        {
            if (field != PlaylistSortField.TrackLength)
            {
                return 0;
            }

            bool leftMissing = left.Duration == null;
            bool rightMissing = right.Duration == null;
            if (leftMissing && rightMissing)
            {
                return 0;
            }
            if (leftMissing)
            {
                return 1;
            }
            if (rightMissing)
            {
                return -1;
            }
            return 0;
        }

        private static Comparison<PlaylistEntry> BuildComparison(PlaylistSortField field)
        {
            switch (field)
            {
                case PlaylistSortField.TrackNumberFromTags:
                    return (left, right) => left.TrackNumber.CompareTo(right.TrackNumber);
                case PlaylistSortField.FileName:
                    return (left, right) => NaturalStringComparer.Instance.Compare(left.FileName, right.FileName);
                case PlaylistSortField.TrackName:
                    return (left, right) => NaturalStringComparer.Instance.Compare(left.TrackName, right.TrackName);
                case PlaylistSortField.FileSize:
                    return (left, right) => left.FileSize.CompareTo(right.FileSize);
                case PlaylistSortField.TrackLength:
                    return (left, right) => left.Duration!.Value.CompareTo(right.Duration!.Value);
                case PlaylistSortField.AlbumName:
                    return (left, right) =>
                    {
                        int album = NaturalStringComparer.Instance.Compare(left.Album, right.Album);
                        return album != 0 ? album : NaturalStringComparer.Instance.Compare(left.TrackName, right.TrackName);
                    };
                case PlaylistSortField.ArtistName:
                    return (left, right) =>
                    {
                        int artist = NaturalStringComparer.Instance.Compare(left.Artist, right.Artist);
                        if (artist != 0)
                        {
                            return artist;
                        }
                        int album = NaturalStringComparer.Instance.Compare(left.Album, right.Album);
                        return album != 0 ? album : NaturalStringComparer.Instance.Compare(left.TrackName, right.TrackName);
                    };
                default:
                    return (left, right) => NaturalStringComparer.Instance.Compare(left.FullPath, right.FullPath);
            }
        }

        private static void Shuffle(List<PlaylistEntry> entries)
        {
            for (int index = entries.Count - 1; index > 0; index--)
            {
                int swapIndex = Random.Shared.Next(index + 1);
                PlaylistEntry temporary = entries[index];
                entries[index] = entries[swapIndex];
                entries[swapIndex] = temporary;
            }
        }
    }
}
