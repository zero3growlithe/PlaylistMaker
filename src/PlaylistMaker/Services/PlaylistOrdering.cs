using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlaylistMaker.Models;

namespace PlaylistMaker.Services
{
    /// <summary>Applies one of the "save as playlist" orderings to a set of tracks.</summary>
    public static class PlaylistOrdering
    {
        public static List<string> Apply(IEnumerable<string> paths, PlaylistSortMode mode)
        {
            List<string> source = paths.ToList();
            switch (mode)
            {
                case PlaylistSortMode.TrackNumberFromTags:
                {
                    return source
                        .OrderBy(TrackNumberOrLargest, Comparer<int>.Default)
                        .ThenBy(GetTitle, NaturalStringComparer.Instance)
                        .ThenBy(GetFileName, NaturalStringComparer.Instance)
                        .ToList();
                }
                case PlaylistSortMode.FileNameDescending:
                {
                    return source
                        .OrderByDescending(GetFileName, NaturalStringComparer.Instance)
                        .ToList();
                }
                case PlaylistSortMode.TrackNameAscending:
                {
                    return source
                        .OrderBy(GetTitle, NaturalStringComparer.Instance)
                        .ThenBy(GetFileName, NaturalStringComparer.Instance)
                        .ToList();
                }
                case PlaylistSortMode.TrackNameDescending:
                {
                    return source
                        .OrderByDescending(GetTitle, NaturalStringComparer.Instance)
                        .ThenByDescending(GetFileName, NaturalStringComparer.Instance)
                        .ToList();
                }
                case PlaylistSortMode.Random:
                {
                    List<string> shuffled = source.ToList();
                    Random random = Random.Shared;
                    for (int index = shuffled.Count - 1; index > 0; index--)
                    {
                        int swapIndex = random.Next(index + 1);
                        string temporary = shuffled[index];
                        shuffled[index] = shuffled[swapIndex];
                        shuffled[swapIndex] = temporary;
                    }
                    return shuffled;
                }
                default:
                {
                    return source
                        .OrderBy(GetFileName, NaturalStringComparer.Instance)
                        .ToList();
                }
            }
        }

        private static int TrackNumberOrLargest(string path)
        {
            int trackNumber = TagReader.Read(path).TrackNumber;
            return trackNumber <= 0 ? int.MaxValue : trackNumber;
        }

        private static string GetTitle(string path)
        {
            TrackMetadata metadata = TagReader.Read(path);
            if (!string.IsNullOrWhiteSpace(metadata.Title))
            {
                return metadata.Title!;
            }
            return Path.GetFileNameWithoutExtension(path);
        }

        private static string GetFileName(string path)
        {
            string fileName = Path.GetFileName(path);
            return string.IsNullOrEmpty(fileName) ? path : fileName;
        }
    }
}
