using System;
using System.Collections.Generic;
using System.IO;
using PlaylistMaker.Models;

namespace PlaylistMaker.Services
{
    /// <summary>Finds a missing entry again somewhere else, by file name.</summary>
    public static class MissingFileResolver
    {
        /// <summary>Best match for <paramref name="missingPath"/> inside <paramref name="folder"/>, or null.</summary>
        public static string? FindByName(string folder, string missingPath, bool recursive)
        {
            string fileName = Path.GetFileName(missingPath);
            if (string.IsNullOrEmpty(fileName) || !Directory.Exists(folder))
            {
                return null;
            }

            string missingParentName = Path.GetFileName(Path.GetDirectoryName(missingPath) ?? string.Empty);
            string? bestMatch = null;
            int bestScore = -1;

            foreach (string candidate in EnumerateFiles(folder, recursive))
            {
                if (!string.Equals(Path.GetFileName(candidate), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int score = 1;
                string candidateParentName = Path.GetFileName(Path.GetDirectoryName(candidate) ?? string.Empty);
                if (!string.IsNullOrEmpty(missingParentName)
                    && string.Equals(candidateParentName, missingParentName, StringComparison.OrdinalIgnoreCase))
                {
                    score = 2;
                }

                if (score > bestScore || (score == bestScore && bestMatch != null && candidate.Length < bestMatch.Length))
                {
                    bestScore = score;
                    bestMatch = candidate;
                }
                if (bestScore == 2)
                {
                    return bestMatch;
                }
            }

            return bestMatch;
        }

        /// <summary>Repoints every entry that can be found inside <paramref name="folder"/>; returns how many were fixed.</summary>
        public static int FixAll(IEnumerable<PlaylistEntry> entries, string folder, bool recursive)
        {
            int fixedCount = 0;
            foreach (PlaylistEntry entry in entries)
            {
                if (!entry.IsMissing)
                {
                    continue;
                }
                string? match = FindByName(folder, entry.FullPath, recursive);
                if (match != null)
                {
                    entry.PointTo(match);
                    fixedCount++;
                }
            }
            return fixedCount;
        }

        private static IEnumerable<string> EnumerateFiles(string folder, bool recursive)
        {
            if (!recursive)
            {
                string[] directFiles;
                try
                {
                    directFiles = Directory.GetFiles(folder);
                }
                catch (UnauthorizedAccessException)
                {
                    yield break;
                }
                catch (IOException)
                {
                    yield break;
                }

                foreach (string file in directFiles)
                {
                    yield return file;
                }
                yield break;
            }

            Stack<string> pending = new Stack<string>();
            pending.Push(folder);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                string[] subDirectories;
                string[] files;
                try
                {
                    subDirectories = Directory.GetDirectories(current);
                    files = Directory.GetFiles(current);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (string file in files)
                {
                    yield return file;
                }
                foreach (string subDirectory in subDirectories)
                {
                    pending.Push(subDirectory);
                }
            }
        }
    }
}
