using System;
using System.Collections.Generic;
using System.IO;

namespace PlaylistMaker.Services
{
    /// <summary>
    /// Orders tracks the way a folder tree reads: the tracks sitting directly in the folder come
    /// first, then the tracks of the first subfolder, then the next subfolder, and so on, depth
    /// first. Sorting a whole album folder by file name instead mixes the discs of a multi-disc
    /// release together, because "01 - intro.mp3" and "CD2/01 - intro.mp3" compete for the same slot.
    /// </summary>
    public sealed class FolderSequenceComparer : IComparer<string>
    {
        private readonly string[] commonRoot;
        private readonly Dictionary<string, string[]> folderCache;

        public FolderSequenceComparer(string[] commonRoot, IDictionary<string, string[]> knownFolders)
        {
            this.commonRoot = commonRoot ?? Array.Empty<string>();
            this.folderCache = new Dictionary<string, string[]>(knownFolders, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds a comparer that knows where the folders of <paramref name="paths"/> start, so the
        /// shared parent of all of them is not part of the comparison.
        /// </summary>
        public static FolderSequenceComparer ForPaths(IEnumerable<string> paths)
        {
            List<string> pathList = new List<string>();
            Dictionary<string, string[]> folders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }
                pathList.Add(path);
                string key = GetFolderKey(path);
                if (!folders.ContainsKey(key))
                {
                    folders[key] = SplitFolder(key);
                }
            }

            string[] root = FindCommonRoot(folders.Values);
            return new FolderSequenceComparer(root, folders);
        }

        public int Compare(string? left, string? right)
        {
            string[] leftFolders = GetRelativeFolders(left);
            string[] rightFolders = GetRelativeFolders(right);

            int shared = Math.Min(leftFolders.Length, rightFolders.Length);
            for (int index = 0; index < shared; index++)
            {
                int byFolder = NaturalStringComparer.Instance.Compare(leftFolders[index], rightFolders[index]);
                if (byFolder != 0)
                {
                    return byFolder;
                }
            }

            if (leftFolders.Length != rightFolders.Length)
            {
                // the shorter chain is the folder itself, which always reads before its own subfolders
                return leftFolders.Length < rightFolders.Length ? -1 : 1;
            }

            int byFileName = NaturalStringComparer.Instance.Compare(GetFileName(left), GetFileName(right));
            if (byFileName != 0)
            {
                return byFileName;
            }
            return NaturalStringComparer.Instance.Compare(left, right);
        }

        private string[] GetRelativeFolders(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Array.Empty<string>();
            }

            string key = GetFolderKey(path);
            if (!folderCache.TryGetValue(key, out string[]? segments))
            {
                segments = SplitFolder(key);
                folderCache[key] = segments;
            }

            if (commonRoot.Length == 0 || segments.Length < commonRoot.Length)
            {
                return segments;
            }

            for (int index = 0; index < commonRoot.Length; index++)
            {
                if (!string.Equals(segments[index], commonRoot[index], StringComparison.OrdinalIgnoreCase))
                {
                    return segments;
                }
            }

            string[] relative = new string[segments.Length - commonRoot.Length];
            Array.Copy(segments, commonRoot.Length, relative, 0, relative.Length);
            return relative;
        }

        private static string GetFolderKey(string path)
        {
            string folder;
            try
            {
                folder = Path.GetDirectoryName(path) ?? string.Empty;
            }
            catch (ArgumentException)
            {
                folder = string.Empty;
            }
            return PathUtilities.TrimTrailingSeparator(folder);
        }

        private static string GetFileName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }
            try
            {
                return Path.GetFileName(path) ?? path;
            }
            catch (ArgumentException)
            {
                return path;
            }
        }

        private static string[] SplitFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return Array.Empty<string>();
            }

            string normalised = folder.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            normalised = PathUtilities.TrimTrailingSeparator(normalised);
            if (normalised.Length == 0)
            {
                return Array.Empty<string>();
            }
            return normalised.Split(Path.DirectorySeparatorChar);
        }

        private static string[] FindCommonRoot(IEnumerable<string[]> folders)
        {
            string[]? root = null;
            foreach (string[] segments in folders)
            {
                if (root == null)
                {
                    root = segments;
                    continue;
                }

                int shared = 0;
                int limit = Math.Min(root.Length, segments.Length);
                while (shared < limit
                       && string.Equals(root[shared], segments[shared], StringComparison.OrdinalIgnoreCase))
                {
                    shared++;
                }

                if (shared == root.Length)
                {
                    continue;
                }

                string[] trimmed = new string[shared];
                Array.Copy(root, 0, trimmed, 0, shared);
                root = trimmed;
            }

            return root ?? Array.Empty<string>();
        }
    }
}
