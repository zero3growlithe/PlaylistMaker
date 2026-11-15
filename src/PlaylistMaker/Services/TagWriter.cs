using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PlaylistMaker.Services
{
    public enum TagWriteOutcome
    {
        Written,
        UnsupportedFormat,
        Failed
    }

    /// <summary>What happened to one file the tag writer was pointed at.</summary>
    public sealed class TagWriteResult
    {
        public TagWriteResult(string filePath, TagWriteOutcome outcome, string? message)
        {
            FilePath = filePath ?? string.Empty;
            Outcome = outcome;
            Message = message;
        }

        public string FilePath { get; }

        public TagWriteOutcome Outcome { get; }

        /// <summary>Why the write did not happen, or null when it did.</summary>
        public string? Message { get; }

        public bool Written => Outcome == TagWriteOutcome.Written;
    }

    /// <summary>
    /// Writes artist and album into the tags of a file, without any external tag library: ID3v2 for
    /// MP3 (the existing tag is read back, the other frames are kept, only TPE1/TALB are replaced and
    /// an ID3v1 tag is refreshed when the file still carries one) and the Vorbis comment block for FLAC.
    /// Every other format is reported as unsupported instead of being touched.
    /// </summary>
    public static class TagWriter
    {
        private const int MaximumTagBody = 64 * 1024 * 1024;

        private static readonly string[] WritableExtensions = new string[] { ".mp3", ".mp2", ".flac" };

        public static bool CanWrite(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            string extension = Path.GetExtension(filePath);
            foreach (string candidate in WritableExtensions)
            {
                if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static TagWriteResult Write(string filePath, string? artist, string? album)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return new TagWriteResult(filePath, TagWriteOutcome.Failed, "No file name.");
            }

            string? cleanArtist = Clean(artist);
            string? cleanAlbum = Clean(album);
            if (cleanArtist == null && cleanAlbum == null)
            {
                return new TagWriteResult(filePath, TagWriteOutcome.Failed, "Nothing to write.");
            }

            if (!File.Exists(filePath))
            {
                return new TagWriteResult(filePath, TagWriteOutcome.Failed, "The file is gone.");
            }

            try
            {
                string extension = Path.GetExtension(filePath);
                if (string.Equals(extension, ".flac", StringComparison.OrdinalIgnoreCase))
                {
                    return WriteFlacComment(filePath, cleanArtist, cleanAlbum);
                }
                if (string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".mp2", StringComparison.OrdinalIgnoreCase))
                {
                    return WriteId3(filePath, cleanArtist, cleanAlbum);
                }
                return new TagWriteResult(filePath, TagWriteOutcome.UnsupportedFormat,
                    "Tag writing is only implemented for MP3 and FLAC files.");
            }
            catch (IOException exception)
            {
                return new TagWriteResult(filePath, TagWriteOutcome.Failed, exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                return new TagWriteResult(filePath, TagWriteOutcome.Failed, exception.Message);
            }
            catch (NotSupportedException exception)
            {
                return new TagWriteResult(filePath, TagWriteOutcome.Failed, exception.Message);
            }
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

        // ── ID3v2 (MP3) ───────────────────────────────────────────────────
        private static TagWriteResult WriteId3(string filePath, string? artist, string? album)
        {
            List<Id3Frame> frames = new List<Id3Frame>();
            int majorVersion = 3;
            long audioStart = 0;

            using (FileStream reader = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] header = new byte[10];
                int headerRead = ReadFully(reader, header, 0, header.Length);
                if (headerRead == header.Length && header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'3')
                {
                    int version = header[3];
                    int flags = header[5];
                    long bodySize = ReadSynchsafe(header, 6);
                    bool hasFooter = (flags & 0x10) != 0;
                    audioStart = 10 + bodySize + (hasFooter ? 10 : 0);
                    majorVersion = version == 4 ? 4 : 3;

                    // Frames are only carried over from a plain v2.3/v2.4 tag. An unsynchronised,
                    // extended or v2.2 tag cannot be re-serialised byte for byte, so it is replaced
                    // by a clean tag instead of risking a corrupted one.
                    bool canPreserve = (version == 3 || version == 4)
                        && flags == 0
                        && bodySize > 0
                        && bodySize <= MaximumTagBody;
                    if (canPreserve)
                    {
                        byte[] body = new byte[(int)bodySize];
                        int bodyRead = ReadFully(reader, body, 0, body.Length);
                        frames = ParseId3Frames(body, bodyRead, version);
                    }
                }
                else
                {
                    audioStart = 0;
                }
            }

            bool changed = false;
            if (artist != null)
            {
                ReplaceTextFrame(frames, "TPE1", artist);
                changed = true;
            }
            if (album != null)
            {
                ReplaceTextFrame(frames, "TALB", album);
                changed = true;
            }
            if (!changed)
            {
                return new TagWriteResult(filePath, TagWriteOutcome.Failed, "Nothing to write.");
            }

            byte[] tag = BuildId3Tag(frames, majorVersion);
            WriteThroughTemporaryFile(filePath, (input, output) =>
            {
                output.Write(tag, 0, tag.Length);

                long length = input.Length;
                long audioEnd = length;
                byte[]? id3v1 = null;
                if (length - 128 >= audioStart)
                {
                    input.Seek(length - 128, SeekOrigin.Begin);
                    byte[] tail = new byte[128];
                    if (ReadFully(input, tail, 0, tail.Length) == tail.Length
                        && tail[0] == (byte)'T' && tail[1] == (byte)'A' && tail[2] == (byte)'G')
                    {
                        id3v1 = RefreshId3v1(tail, artist, album);
                        audioEnd = length - 128;
                    }
                }

                input.Seek(audioStart, SeekOrigin.Begin);
                CopyBytes(input, output, audioEnd - audioStart);
                if (id3v1 != null)
                {
                    output.Write(id3v1, 0, id3v1.Length);
                }
            });

            return new TagWriteResult(filePath, TagWriteOutcome.Written, null);
        }

        private sealed class Id3Frame
        {
            public Id3Frame(string id, byte[] flags, byte[] payload)
            {
                Id = id;
                Flags = flags;
                Payload = payload;
            }

            public string Id { get; }

            public byte[] Flags { get; }

            public byte[] Payload { get; }
        }

        private static List<Id3Frame> ParseId3Frames(byte[] body, int length, int version)
        {
            List<Id3Frame> frames = new List<Id3Frame>();
            int position = 0;
            while (position + 10 <= length)
            {
                if (body[position] == 0)
                {
                    break;
                }

                string id = Encoding.ASCII.GetString(body, position, 4);
                if (!IsFrameId(id))
                {
                    break;
                }

                long size = version >= 4
                    ? ReadSynchsafe(body, position + 4)
                    : ((long)body[position + 4] << 24) | ((long)body[position + 5] << 16)
                      | ((long)body[position + 6] << 8) | body[position + 7];
                if (size <= 0 || size > MaximumTagBody || position + 10 + size > length)
                {
                    break;
                }

                byte[] flags = new byte[] { body[position + 8], body[position + 9] };
                byte[] payload = new byte[(int)size];
                Array.Copy(body, position + 10, payload, 0, payload.Length);
                frames.Add(new Id3Frame(id, flags, payload));
                position += 10 + (int)size;
            }
            return frames;
        }

        private static bool IsFrameId(string id)
        {
            if (id.Length != 4)
            {
                return false;
            }
            foreach (char character in id)
            {
                bool isUpperLetter = character >= 'A' && character <= 'Z';
                bool isDigit = character >= '0' && character <= '9';
                if (!isUpperLetter && !isDigit)
                {
                    return false;
                }
            }
            return true;
        }

        private static void ReplaceTextFrame(List<Id3Frame> frames, string frameId, string value)
        {
            frames.RemoveAll(frame => string.Equals(frame.Id, frameId, StringComparison.Ordinal));
            byte[] payload = BuildTextFramePayload(value);
            frames.Add(new Id3Frame(frameId, new byte[2], payload));
        }

        private static byte[] BuildTextFramePayload(string value)
        {
            byte[] encoded = Encoding.Unicode.GetBytes(value);
            byte[] payload = new byte[3 + encoded.Length];
            payload[0] = 1; // UTF-16 with a byte order mark
            payload[1] = 0xFF;
            payload[2] = 0xFE;
            Array.Copy(encoded, 0, payload, 3, encoded.Length);
            return payload;
        }

        private static byte[] BuildId3Tag(List<Id3Frame> frames, int majorVersion)
        {
            using (MemoryStream body = new MemoryStream())
            {
                foreach (Id3Frame frame in frames)
                {
                    byte[] idBytes = Encoding.ASCII.GetBytes(frame.Id);
                    body.Write(idBytes, 0, 4);
                    if (majorVersion >= 4)
                    {
                        body.Write(BuildSynchsafe(frame.Payload.Length), 0, 4);
                    }
                    else
                    {
                        byte[] sizeBytes = new byte[4];
                        sizeBytes[0] = (byte)((frame.Payload.Length >> 24) & 0xFF);
                        sizeBytes[1] = (byte)((frame.Payload.Length >> 16) & 0xFF);
                        sizeBytes[2] = (byte)((frame.Payload.Length >> 8) & 0xFF);
                        sizeBytes[3] = (byte)(frame.Payload.Length & 0xFF);
                        body.Write(sizeBytes, 0, 4);
                    }
                    body.Write(frame.Flags, 0, 2);
                    body.Write(frame.Payload, 0, frame.Payload.Length);
                }

                byte[] bodyBytes = body.ToArray();
                byte[] tag = new byte[10 + bodyBytes.Length];
                tag[0] = (byte)'I';
                tag[1] = (byte)'D';
                tag[2] = (byte)'3';
                tag[3] = (byte)majorVersion;
                tag[4] = 0;
                tag[5] = 0;
                WriteSynchsafe(tag, 6, bodyBytes.Length);
                Array.Copy(bodyBytes, 0, tag, 10, bodyBytes.Length);
                return tag;
            }
        }

        /// <summary>Refreshes the artist/album fields of an old ID3v1 tag so old players see them too.</summary>
        private static byte[] RefreshId3v1(byte[] tag, string? artist, string? album)
        {
            byte[] copy = new byte[tag.Length];
            Array.Copy(tag, copy, tag.Length);
            if (artist != null)
            {
                WriteId3v1Field(copy, 33, artist);
            }
            if (album != null)
            {
                WriteId3v1Field(copy, 63, album);
            }
            return copy;
        }

        /// <summary>ID3v1 keeps 30 single byte characters per field, padded with zeros.</summary>
        private static void WriteId3v1Field(byte[] tag, int offset, string value)
        {
            for (int index = 0; index < 30; index++)
            {
                tag[offset + index] = 0;
            }

            byte[] encoded = Encoding.Latin1.GetBytes(value);
            int limit = Math.Min(encoded.Length, 30);
            Array.Copy(encoded, 0, tag, offset, limit);
        }

        // ── Vorbis comment (FLAC) ─────────────────────────────────────────
        private sealed class FlacBlock
        {
            public FlacBlock(int type, byte[] data)
            {
                Type = type;
                Data = data;
            }

            public int Type { get; }

            public byte[] Data { get; }
        }

        private static TagWriteResult WriteFlacComment(string filePath, string? artist, string? album)
        {
            const int VorbisCommentBlockType = 4;
            const string Vendor = "Playlist Maker";

            List<FlacBlock> blocks = new List<FlacBlock>();
            long audioStart;

            using (FileStream reader = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] signature = new byte[4];
                if (ReadFully(reader, signature, 0, signature.Length) != signature.Length
                    || signature[0] != (byte)'f' || signature[1] != (byte)'L'
                    || signature[2] != (byte)'a' || signature[3] != (byte)'C')
                {
                    return new TagWriteResult(filePath, TagWriteOutcome.Failed,
                        "The file does not start with a FLAC stream (an ID3 tag in front of it is not supported).");
                }

                bool isLast = false;
                int guard = 0;
                while (!isLast && guard < 1024)
                {
                    guard++;
                    byte[] blockHeader = new byte[4];
                    if (ReadFully(reader, blockHeader, 0, blockHeader.Length) != blockHeader.Length)
                    {
                        return new TagWriteResult(filePath, TagWriteOutcome.Failed, "The FLAC metadata is truncated.");
                    }

                    isLast = (blockHeader[0] & 0x80) != 0;
                    int type = blockHeader[0] & 0x7F;
                    int length = (blockHeader[1] << 16) | (blockHeader[2] << 8) | blockHeader[3];
                    if (length < 0 || length > MaximumTagBody)
                    {
                        return new TagWriteResult(filePath, TagWriteOutcome.Failed, "A FLAC metadata block is too large.");
                    }

                    byte[] data = new byte[length];
                    if (length > 0 && ReadFully(reader, data, 0, length) != length)
                    {
                        return new TagWriteResult(filePath, TagWriteOutcome.Failed, "The FLAC metadata is truncated.");
                    }
                    blocks.Add(new FlacBlock(type, data));
                }

                audioStart = reader.Position;
            }

            if (blocks.Count == 0)
            {
                return new TagWriteResult(filePath, TagWriteOutcome.Failed, "The FLAC metadata is empty.");
            }

            int commentIndex = blocks.FindIndex(block => block.Type == VorbisCommentBlockType);
            List<string> comments = commentIndex >= 0
                ? ParseVorbisComments(blocks[commentIndex].Data)
                : new List<string>();

            comments = ReplaceVorbisEntry(comments, "ARTIST", artist);
            comments = ReplaceVorbisEntry(comments, "ALBUM", album);

            byte[] commentBlock = BuildVorbisCommentBlock(Vendor, comments);
            FlacBlock replacement = new FlacBlock(VorbisCommentBlockType, commentBlock);
            if (commentIndex >= 0)
            {
                blocks[commentIndex] = replacement;
            }
            else
            {
                // STREAMINFO has to stay the first block
                blocks.Insert(1, replacement);
            }

            WriteThroughTemporaryFile(filePath, (input, output) =>
            {
                byte[] signature = new byte[] { (byte)'f', (byte)'L', (byte)'a', (byte)'C' };
                output.Write(signature, 0, signature.Length);
                for (int index = 0; index < blocks.Count; index++)
                {
                    FlacBlock block = blocks[index];
                    bool isLast = index == blocks.Count - 1;
                    byte[] header = new byte[4];
                    header[0] = (byte)((isLast ? 0x80 : 0x00) | (block.Type & 0x7F));
                    header[1] = (byte)((block.Data.Length >> 16) & 0xFF);
                    header[2] = (byte)((block.Data.Length >> 8) & 0xFF);
                    header[3] = (byte)(block.Data.Length & 0xFF);
                    output.Write(header, 0, header.Length);
                    output.Write(block.Data, 0, block.Data.Length);
                }

                input.Seek(audioStart, SeekOrigin.Begin);
                CopyBytes(input, output, input.Length - audioStart);
            });

            return new TagWriteResult(filePath, TagWriteOutcome.Written, null);
        }

        private static List<string> ParseVorbisComments(byte[] data)
        {
            List<string> comments = new List<string>();
            if (data.Length < 8)
            {
                return comments;
            }

            int position = 0;
            int vendorLength = ReadInt32LittleEndian(data, position);
            if (vendorLength < 0 || vendorLength > data.Length)
            {
                return comments;
            }
            position += 4 + vendorLength;
            if (position + 4 > data.Length)
            {
                return comments;
            }

            int count = ReadInt32LittleEndian(data, position);
            position += 4;
            for (int index = 0; index < count && position + 4 <= data.Length; index++)
            {
                int length = ReadInt32LittleEndian(data, position);
                position += 4;
                if (length < 0 || position + length > data.Length)
                {
                    break;
                }
                comments.Add(Encoding.UTF8.GetString(data, position, length));
                position += length;
            }
            return comments;
        }

        private static List<string> ReplaceVorbisEntry(List<string> comments, string key, string? value)
        {
            if (value == null)
            {
                return comments;
            }

            List<string> result = new List<string>();
            string prefix = key + "=";
            foreach (string comment in comments)
            {
                int separator = comment.IndexOf('=');
                string commentKey = separator > 0 ? comment.Substring(0, separator) : comment;
                if (string.Equals(commentKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                result.Add(comment);
            }
            result.Add(prefix + value);
            return result;
        }

        private static byte[] BuildVorbisCommentBlock(string vendor, List<string> comments)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                byte[] vendorBytes = Encoding.UTF8.GetBytes(vendor);
                WriteInt32LittleEndian(stream, vendorBytes.Length);
                stream.Write(vendorBytes, 0, vendorBytes.Length);
                WriteInt32LittleEndian(stream, comments.Count);
                foreach (string comment in comments)
                {
                    byte[] commentBytes = Encoding.UTF8.GetBytes(comment);
                    WriteInt32LittleEndian(stream, commentBytes.Length);
                    stream.Write(commentBytes, 0, commentBytes.Length);
                }
                return stream.ToArray();
            }
        }

        private static int ReadInt32LittleEndian(byte[] data, int offset)
        {
            return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
        }

        private static void WriteInt32LittleEndian(Stream stream, int value)
        {
            byte[] bytes = new byte[4];
            bytes[0] = (byte)(value & 0xFF);
            bytes[1] = (byte)((value >> 8) & 0xFF);
            bytes[2] = (byte)((value >> 16) & 0xFF);
            bytes[3] = (byte)((value >> 24) & 0xFF);
            stream.Write(bytes, 0, bytes.Length);
        }

        // ── Shared plumbing ───────────────────────────────────────────────
        /// <summary>
        /// Writes the new file next to the old one and swaps it in, so a failure halfway through
        /// never leaves the user with a half written track.
        /// </summary>
        private static void WriteThroughTemporaryFile(string filePath, Action<FileStream, FileStream> writer)
        {
            string temporaryPath = filePath + ".tagtmp";
            try
            {
                using (FileStream input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    writer(input, output);
                }
                ReplaceFile(temporaryPath, filePath);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static void ReplaceFile(string temporaryPath, string targetPath)
        {
            try
            {
                File.Replace(temporaryPath, targetPath, null);
                return;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (NotSupportedException)
            {
            }

            try
            {
                File.SetAttributes(targetPath, FileAttributes.Normal);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            File.Copy(temporaryPath, targetPath, true);
            File.Delete(temporaryPath);
        }

        private static void CopyBytes(Stream input, Stream output, long count)
        {
            byte[] buffer = new byte[81920];
            long remaining = count;
            while (remaining > 0)
            {
                int chunk = (int)Math.Min(buffer.Length, remaining);
                int read = input.Read(buffer, 0, chunk);
                if (read <= 0)
                {
                    break;
                }
                output.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private static int ReadFully(Stream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, offset + total, count - total);
                if (read <= 0)
                {
                    break;
                }
                total += read;
            }
            return total;
        }

        private static long ReadSynchsafe(byte[] data, int offset)
        {
            return ((long)(data[offset] & 0x7F) << 21)
                | ((long)(data[offset + 1] & 0x7F) << 14)
                | ((long)(data[offset + 2] & 0x7F) << 7)
                | (data[offset + 3] & 0x7F);
        }

        private static void WriteSynchsafe(byte[] target, int offset, int value)
        {
            target[offset] = (byte)((value >> 21) & 0x7F);
            target[offset + 1] = (byte)((value >> 14) & 0x7F);
            target[offset + 2] = (byte)((value >> 7) & 0x7F);
            target[offset + 3] = (byte)(value & 0x7F);
        }

        private static byte[] BuildSynchsafe(int value)
        {
            byte[] bytes = new byte[4];
            WriteSynchsafe(bytes, 0, value);
            return bytes;
        }
    }
}
