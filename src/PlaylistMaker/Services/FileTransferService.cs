using System;
using System.Collections.Generic;
using System.IO;

namespace PlaylistMaker.Services
{
    /// <summary>What a drop does with the payload: relocate the originals, or leave copies behind.</summary>
    public enum FileTransferMode
    {
        Move,
        Copy
    }

    /// <summary>How a name clash at the destination is resolved.</summary>
    public enum FileConflictPolicy
    {
        Replace,
        KeepBoth
    }

    /// <summary>One item that really moved or was copied, source and where it landed.</summary>
    public sealed class FileTransferOutcome
    {
        public FileTransferOutcome(string sourcePath, string targetPath)
        {
            SourcePath = sourcePath;
            TargetPath = targetPath;
        }

        public string SourcePath { get; }

        public string TargetPath { get; }
    }

    /// <summary>Everything a transfer did, so the caller can re-point playlists and the tree afterwards.</summary>
    public sealed class FileTransferResult
    {
        public List<FileTransferOutcome> Outcomes { get; } = new List<FileTransferOutcome>();

        public List<string> Failures { get; } = new List<string>();

        /// <summary>Items that were already in the target folder, so nothing had to happen to them.</summary>
        public int SkippedCount { get; set; }
    }

    /// <summary>
    /// The real on-disk work behind the tree's drag and drop: moves, copies, cross-volume
    /// fallbacks and name clashes. The UI never calls File.Move or Directory.Move itself.
    /// </summary>
    public static class FileTransferService
    {
        /// <summary>Sources that would land on an existing name in the target folder.</summary>
        public static List<string> FindConflicts(IReadOnlyList<string> sourcePaths, string targetDirectory)
        {
            List<string> conflicts = new List<string>();
            foreach (string sourcePath in sourcePaths)
            {
                if (IsAlreadyInTarget(sourcePath, targetDirectory))
                {
                    continue;
                }
                string desired = Path.Combine(targetDirectory, GetEntryName(sourcePath));
                if (File.Exists(desired) || Directory.Exists(desired))
                {
                    conflicts.Add(sourcePath);
                }
            }
            return conflicts;
        }

        /// <summary>
        /// True when the target is one of the sources or sits inside one of them - dropping a
        /// folder onto itself (or onto its own subfolder) would otherwise destroy it.
        /// </summary>
        public static bool IsTargetInsideSources(IReadOnlyList<string> sourcePaths, string targetDirectory)
        {
            foreach (string sourcePath in sourcePaths)
            {
                if (!Directory.Exists(sourcePath))
                {
                    continue;
                }
                if (PathUtilities.PathsEqual(sourcePath, targetDirectory))
                {
                    return true;
                }
                if (PathUtilities.IsUnder(targetDirectory, sourcePath))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True when the source already lives in the target folder, so moving it is a no-op.</summary>
        public static bool IsAlreadyInTarget(string sourcePath, string targetDirectory)
        {
            string source = PathUtilities.TrimTrailingSeparator(sourcePath);
            string? parent = Path.GetDirectoryName(source);
            if (string.IsNullOrEmpty(parent))
            {
                return false;
            }
            return PathUtilities.PathsEqual(parent!, targetDirectory);
        }

        public static FileTransferResult Transfer(
            IReadOnlyList<string> sourcePaths,
            string targetDirectory,
            FileTransferMode mode,
            FileConflictPolicy conflictPolicy)
        {
            FileTransferResult result = new FileTransferResult();
            string target = PathUtilities.TrimTrailingSeparator(targetDirectory);

            foreach (string sourcePath in sourcePaths)
            {
                string source = PathUtilities.TrimTrailingSeparator(sourcePath);
                bool isFolder = Directory.Exists(source);
                if (!isFolder && !File.Exists(source))
                {
                    result.Failures.Add(source + " - no longer exists");
                    continue;
                }

                if (IsAlreadyInTarget(source, target))
                {
                    result.SkippedCount++;
                    continue;
                }

                string name = GetEntryName(source);
                string destination = Path.Combine(target, name);
                if (File.Exists(destination) || Directory.Exists(destination))
                {
                    if (conflictPolicy == FileConflictPolicy.Replace)
                    {
                        string? replaceFailure = TryDeleteEntry(destination);
                        if (replaceFailure != null)
                        {
                            result.Failures.Add(name + " - the existing item could not be replaced: " + replaceFailure);
                            continue;
                        }
                    }
                    else
                    {
                        try
                        {
                            destination = BuildKeepBothPath(target, name, isFolder);
                        }
                        catch (IOException exception)
                        {
                            result.Failures.Add(name + " - " + exception.Message);
                            continue;
                        }
                    }
                }

                string? failure = TryTransferEntry(source, destination, isFolder, mode);
                if (failure != null)
                {
                    result.Failures.Add(name + " - " + failure);
                    continue;
                }
                result.Outcomes.Add(new FileTransferOutcome(source, destination));
            }

            return result;
        }

        /// <summary>
        /// Where a path ended up after a move. Paths inside a moved folder follow their folder,
        /// which is what lets a playlist keep pointing at its tracks after a folder is relocated.
        /// </summary>
        public static bool TryMapPath(IEnumerable<FileTransferOutcome> outcomes, string path, out string mappedPath)
        {
            string source = PathUtilities.TrimTrailingSeparator(path);
            foreach (FileTransferOutcome outcome in outcomes)
            {
                if (PathUtilities.PathsEqual(source, outcome.SourcePath))
                {
                    mappedPath = outcome.TargetPath;
                    return true;
                }
                if (PathUtilities.IsUnder(source, outcome.SourcePath))
                {
                    string relativePath = source.Substring(outcome.SourcePath.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    mappedPath = Path.Combine(outcome.TargetPath, relativePath);
                    return true;
                }
            }
            mappedPath = path;
            return false;
        }

        private static string? TryTransferEntry(string source, string destination, bool isFolder, FileTransferMode mode)
        {
            try
            {
                if (mode == FileTransferMode.Move)
                {
                    MoveEntry(source, destination, isFolder);
                }
                else
                {
                    CopyEntry(source, destination, isFolder);
                }
                return null;
            }
            catch (UnauthorizedAccessException exception)
            {
                return "access denied (" + exception.Message + ")";
            }
            catch (IOException exception)
            {
                return exception.Message;
            }
            catch (ArgumentException exception)
            {
                return exception.Message;
            }
        }

        /// <summary>A move that crosses volumes fails with an IOException, so fall back to copy + delete.</summary>
        private static void MoveEntry(string source, string destination, bool isFolder)
        {
            try
            {
                if (isFolder)
                {
                    Directory.Move(source, destination);
                }
                else
                {
                    File.Move(source, destination);
                }
            }
            catch (IOException)
            {
                CopyEntry(source, destination, isFolder);
                try
                {
                    if (isFolder)
                    {
                        Directory.Delete(source, true);
                    }
                    else
                    {
                        File.Delete(source);
                    }
                }
                catch (IOException exception)
                {
                    throw new IOException("copied, but the original could not be removed (" + exception.Message + ")", exception);
                }
                catch (UnauthorizedAccessException exception)
                {
                    throw new IOException("copied, but the original could not be removed (" + exception.Message + ")", exception);
                }
            }
        }

        private static void CopyEntry(string source, string destination, bool isFolder)
        {
            if (!isFolder)
            {
                File.Copy(source, destination, true);
                return;
            }

            Directory.CreateDirectory(destination);
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(source, file);
                string copiedFile = Path.Combine(destination, relativePath);
                string? copiedFolder = Path.GetDirectoryName(copiedFile);
                if (!string.IsNullOrEmpty(copiedFolder))
                {
                    Directory.CreateDirectory(copiedFolder!);
                }
                File.Copy(file, copiedFile, true);
            }
        }

        private static string? TryDeleteEntry(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
                return null;
            }
            catch (UnauthorizedAccessException exception)
            {
                return exception.Message;
            }
            catch (IOException exception)
            {
                return exception.Message;
            }
        }

        /// <summary>"song (2).mp3" style name, the same way Explorer resolves a clash without asking twice.</summary>
        private static string BuildKeepBothPath(string targetDirectory, string name, bool isFolder)
        {
            string extension = isFolder ? string.Empty : Path.GetExtension(name);
            string stem = isFolder ? name : Path.GetFileNameWithoutExtension(name);
            if (string.IsNullOrEmpty(stem))
            {
                stem = name;
            }

            for (int copy = 2; copy < 1000; copy++)
            {
                string candidate = Path.Combine(targetDirectory, stem + " (" + copy + ")" + extension);
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new IOException("no free name left for \"" + name + "\"");
        }

        private static string GetEntryName(string sourcePath)
        {
            string trimmed = PathUtilities.TrimTrailingSeparator(sourcePath);
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }
    }
}
