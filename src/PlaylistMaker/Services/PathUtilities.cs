using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace PlaylistMaker.Services
{
    /// <summary>Small path helpers: mutual parent directory (for the save dialog) and "show in Explorer".</summary>
    public static class PathUtilities
    {
        /// <summary>Deepest directory that contains every given file, or null when they span unrelated roots.</summary>
        public static string? GetMutualParentDirectory(IEnumerable<string> filePaths)
        {
            List<string> directories = new List<string>();
            foreach (string filePath in filePaths)
            {
                string? directory = TryGetDirectory(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    directories.Add(directory!);
                }
            }

            if (directories.Count == 0)
            {
                return null;
            }

            string common = directories[0];
            for (int index = 1; index < directories.Count; index++)
            {
                common = GetCommonAncestor(common, directories[index]);
                if (common.Length == 0)
                {
                    return null;
                }
            }
            return Directory.Exists(common) ? common : null;
        }

        public static void ShowInExplorer(string path)
        {
            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "/select,\"" + path + "\"",
                        UseShellExecute = true
                    });
                    return;
                }

                string? directory = TryGetDirectory(path);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "\"" + directory + "\"",
                        UseShellExecute = true
                    });
                }
            }
            catch (Win32Exception)
            {
            }
            catch (IOException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (ArgumentException)
            {
            }
        }

        private static string? TryGetDirectory(string path)
        {
            try
            {
                return Path.GetDirectoryName(path);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static string GetCommonAncestor(string left, string right)
        {
            string[] separators = new string[] { Path.DirectorySeparatorChar.ToString(), Path.AltDirectorySeparatorChar.ToString() };
            string[] leftParts = left.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            string[] rightParts = right.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            int limit = Math.Min(leftParts.Length, rightParts.Length);
            int matched = 0;
            while (matched < limit && string.Equals(leftParts[matched], rightParts[matched], StringComparison.OrdinalIgnoreCase))
            {
                matched++;
            }

            if (matched == 0)
            {
                return string.Empty;
            }

            string separator = Path.DirectorySeparatorChar.ToString();
            string joined = string.Join(separator, leftParts, 0, matched);
            bool leftIsUnc = left.StartsWith(separator + separator, StringComparison.Ordinal);
            if (leftIsUnc)
            {
                return separator + separator + joined;
            }

            // A bare drive ("C:") is drive-relative, not a directory - fall back to the root.
            if (matched == 1 && leftParts[0].EndsWith(":", StringComparison.Ordinal))
            {
                return leftParts[0] + separator;
            }
            return joined;
        }
    }
}
