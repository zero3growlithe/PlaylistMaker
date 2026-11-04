using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using PlaylistMaker.Models;

namespace PlaylistMaker.Services
{
    /// <summary>Builds the drive/folder/file tree for the explorer panel, one level at a time.</summary>
    public sealed class FileSystemTreeService
    {
        /// <summary>When true the tree shows every file; when false only music and playlists.</summary>
        public bool ShowAllFiles { get; set; }

        public List<FileSystemNode> BuildRoots()
        {
            List<FileSystemNode> roots = new List<FileSystemNode>();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                bool isReady = false;
                try
                {
                    isReady = drive.IsReady;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                if (!isReady)
                {
                    continue;
                }

                string label = string.Empty;
                try
                {
                    label = drive.VolumeLabel;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (SecurityException)
                {
                }

                string name = string.IsNullOrWhiteSpace(label) ? drive.Name : drive.Name + "   " + label;
                roots.Add(new FileSystemNode(drive.Name, name, true, 0, false, false));
            }

            roots.Sort((left, right) => NaturalStringComparer.Instance.Compare(left.Name, right.Name));
            return roots;
        }

        public List<FileSystemNode> LoadChildren(FileSystemNode parent)
        {
            List<FileSystemNode> children = new List<FileSystemNode>();
            try
            {
                DirectoryInfo directory = new DirectoryInfo(parent.FullPath);
                foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
                {
                    bool isFolder = entry is DirectoryInfo;
                    if (isFolder)
                    {
                        if (!ShowAllFiles && IsHiddenOrSystem(entry))
                        {
                            continue;
                        }
                    }
                    else if (!ShowAllFiles && !MusicFormats.IsMusicFile(entry.Name) && !MusicFormats.IsPlaylistFile(entry.Name))
                    {
                        continue;
                    }

                    children.Add(new FileSystemNode(
                        entry.FullName,
                        entry.Name,
                        isFolder,
                        parent.Level + 1,
                        !isFolder && MusicFormats.IsMusicFile(entry.Name),
                        !isFolder && MusicFormats.IsPlaylistFile(entry.Name)));
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
            catch (SecurityException)
            {
            }

            children.Sort(CompareNodes);
            return children;
        }

        private static int CompareNodes(FileSystemNode left, FileSystemNode right)
        {
            if (left.IsFolder != right.IsFolder)
            {
                return left.IsFolder ? -1 : 1;
            }
            return NaturalStringComparer.Instance.Compare(left.Name, right.Name);
        }

        private static bool IsHiddenOrSystem(FileSystemInfo entry)
        {
            try
            {
                FileAttributes attributes = entry.Attributes;
                return (attributes & FileAttributes.Hidden) == FileAttributes.Hidden
                    || (attributes & FileAttributes.System) == FileAttributes.System;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
