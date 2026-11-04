using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PlaylistMaker.Models;

namespace PlaylistMaker.Services
{
    /// <summary>
    /// Reads the title and track number straight out of ID3v2 (MP3) and Vorbis comments
    /// (FLAC). Everything else falls back to a guess made from the file name, which is
    /// enough for ordering a playlist - no external tag library is involved.
    /// </summary>
    public static class TagReader
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, TrackMetadata> Cache =
            new Dictionary<string, TrackMetadata>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex LeadingNumberPattern =
            new Regex(@"^\s*(\d{1,4})\s*[-._)\]]?\s*(?<rest>.*)$", RegexOptions.Compiled);

        public static TrackMetadata Read(string filePath)
        {
            lock (CacheLock)
            {
                if (Cache.TryGetValue(filePath, out TrackMetadata? cached))
                {
                    return cached;
                }
            }

            TrackMetadata metadata = ReadCore(filePath);
            lock (CacheLock)
            {
                Cache[filePath] = metadata;
            }
            return metadata;
        }

        public static void ClearCache()
        {
            lock (CacheLock)
            {
                Cache.Clear();
            }
        }

        private static TrackMetadata ReadCore(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            try
            {
                if (string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".mp2", StringComparison.OrdinalIgnoreCase))
                {
                    TrackMetadata? fromTags = ReadId3(filePath);
                    if (fromTags != null)
                    {
                        return fromTags;
                    }
                }
                else if (string.Equals(extension, ".flac", StringComparison.OrdinalIgnoreCase))
                {
                    TrackMetadata? fromTags = ReadFlacVorbisComment(filePath);
                    if (fromTags != null)
                    {
                        return fromTags;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (ArgumentException)
            {
            }

            return FromFileName(filePath);
        }

        private static TrackMetadata? ReadId3(string filePath)
        {
            using FileStream stream = File.OpenRead(filePath);
            byte[] header = new byte[10];
            if (stream.Read(header, 0, 10) != 10)
            {
                return null;
            }
            if (header[0] != 'I' || header[1] != 'D' || header[2] != '3')
            {
                return null;
            }

            int majorVersion = header[3];
            if (majorVersion < 3 || majorVersion > 4)
            {
                return null;
            }

            int bodySize = ((header[6] & 0x7F) << 21) | ((header[7] & 0x7F) << 14) | ((header[8] & 0x7F) << 7) | (header[9] & 0x7F);
            if (bodySize <= 0 || bodySize > 64 * 1024 * 1024)
            {
                return null;
            }

            byte[] body = new byte[bodySize];
            int totalRead = 0;
            while (totalRead < bodySize)
            {
                int read = stream.Read(body, totalRead, bodySize - totalRead);
                if (read <= 0)
                {
                    break;
                }
                totalRead += read;
            }

            string? title = null;
            string? trackText = null;
            int position = 0;
            while (position + 10 <= totalRead)
            {
                string frameId = Encoding.ASCII.GetString(body, position, 4);
                if (frameId.Length == 0 || frameId[0] == '\0' || !char.IsLetterOrDigit(frameId[0]))
                {
                    break;
                }

                long frameSize;
                if (majorVersion >= 4)
                {
                    frameSize = ((long)(body[position + 4] & 0x7F) << 21)
                        | ((long)(body[position + 5] & 0x7F) << 14)
                        | ((long)(body[position + 6] & 0x7F) << 7)
                        | (long)(body[position + 7] & 0x7F);
                }
                else
                {
                    frameSize = ((long)body[position + 4] << 24)
                        | ((long)body[position + 5] << 16)
                        | ((long)body[position + 6] << 8)
                        | (long)body[position + 7];
                }

                if (frameSize <= 0 || position + 10 + frameSize > totalRead)
                {
                    break;
                }

                int payloadOffset = position + 10;
                int payloadLength = (int)frameSize;
                if (frameId == "TIT2" && title == null)
                {
                    title = DecodeTextFrame(body, payloadOffset, payloadLength);
                }
                else if (frameId == "TRCK" && trackText == null)
                {
                    trackText = DecodeTextFrame(body, payloadOffset, payloadLength);
                }

                position += 10 + payloadLength;
            }

            if (title == null && trackText == null)
            {
                return null;
            }
            return new TrackMetadata(title, ParseTrackNumber(trackText), true);
        }

        private static string? DecodeTextFrame(byte[] data, int offset, int length)
        {
            if (length <= 1 || offset < 0 || offset + length > data.Length)
            {
                return null;
            }

            byte encoding = data[offset];
            int textOffset = offset + 1;
            int textLength = length - 1;
            string? text = null;
            switch (encoding)
            {
                case 1:
                    text = DecodeUtf16WithBom(data, textOffset, textLength);
                    break;
                case 2:
                    text = Encoding.BigEndianUnicode.GetString(data, textOffset, textLength);
                    break;
                case 3:
                    text = Encoding.UTF8.GetString(data, textOffset, textLength);
                    break;
                default:
                    text = Encoding.Latin1.GetString(data, textOffset, textLength);
                    break;
            }

            if (text == null)
            {
                return null;
            }
            string cleaned = text.Replace("\0", string.Empty).Trim().Trim('\uFEFF');
            return cleaned.Length == 0 ? null : cleaned;
        }

        private static string DecodeUtf16WithBom(byte[] data, int offset, int length)
        {
            if (length >= 2 && data[offset] == 0xFF && data[offset + 1] == 0xFE)
            {
                return Encoding.Unicode.GetString(data, offset + 2, length - 2);
            }
            if (length >= 2 && data[offset] == 0xFE && data[offset + 1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(data, offset + 2, length - 2);
            }
            return Encoding.Unicode.GetString(data, offset, length);
        }

        private static TrackMetadata? ReadFlacVorbisComment(string filePath)
        {
            using FileStream stream = File.OpenRead(filePath);
            byte[] signature = new byte[4];
            if (stream.Read(signature, 0, 4) != 4)
            {
                return null;
            }
            if (signature[0] != (byte)'f' || signature[1] != (byte)'L'
                || signature[2] != (byte)'a' || signature[3] != (byte)'C')
            {
                return null;
            }

            bool isLastBlock = false;
            int guard = 0;
            while (!isLastBlock && guard < 128)
            {
                guard++;
                byte[] blockHeader = new byte[4];
                if (stream.Read(blockHeader, 0, 4) != 4)
                {
                    return null;
                }
                isLastBlock = (blockHeader[0] & 0x80) != 0;
                int blockType = blockHeader[0] & 0x7F;
                int blockLength = (blockHeader[1] << 16) | (blockHeader[2] << 8) | blockHeader[3];
                if (blockLength < 0 || blockLength > 32 * 1024 * 1024)
                {
                    return null;
                }

                if (blockType != 4)
                {
                    stream.Seek(blockLength, SeekOrigin.Current);
                    continue;
                }

                byte[] block = new byte[blockLength];
                int read = 0;
                while (read < blockLength)
                {
                    int chunk = stream.Read(block, read, blockLength - read);
                    if (chunk <= 0)
                    {
                        break;
                    }
                    read += chunk;
                }
                return ParseVorbisComment(block, read);
            }
            return null;
        }

        private static TrackMetadata? ParseVorbisComment(byte[] block, int length)
        {
            int cursor = 0;
            if (cursor + 4 > length)
            {
                return null;
            }
            int vendorLength = BitConverter.ToInt32(block, cursor);
            if (vendorLength < 0)
            {
                return null;
            }
            cursor += 4 + vendorLength;
            if (cursor + 4 > length)
            {
                return null;
            }
            int commentCount = BitConverter.ToInt32(block, cursor);
            if (commentCount < 0 || commentCount > 4096)
            {
                return null;
            }
            cursor += 4;

            string? title = null;
            string? trackText = null;
            for (int index = 0; index < commentCount; index++)
            {
                if (cursor + 4 > length)
                {
                    break;
                }
                int commentLength = BitConverter.ToInt32(block, cursor);
                if (commentLength < 0)
                {
                    break;
                }
                cursor += 4;
                if (cursor + commentLength > length)
                {
                    break;
                }

                string comment = Encoding.UTF8.GetString(block, cursor, commentLength);
                cursor += commentLength;
                int separator = comment.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string key = comment.Substring(0, separator).ToUpperInvariant();
                string value = comment.Substring(separator + 1).Trim();
                if (key == "TITLE" && title == null)
                {
                    title = value;
                }
                else if ((key == "TRACKNUMBER" || key == "TRACKNUM") && trackText == null)
                {
                    trackText = value;
                }
            }

            if (title == null && trackText == null)
            {
                return null;
            }
            return new TrackMetadata(title, ParseTrackNumber(trackText), true);
        }

        private static TrackMetadata FromFileName(string filePath)
        {
            string stem = Path.GetFileNameWithoutExtension(filePath);
            int trackNumber = 0;
            string title = stem;

            Match match = LeadingNumberPattern.Match(stem);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int parsedNumber))
                {
                    trackNumber = parsedNumber;
                }
                string rest = match.Groups["rest"].Value.Trim();
                if (rest.Length > 0)
                {
                    title = rest;
                }
            }

            return new TrackMetadata(title, trackNumber, false);
        }

        private static int ParseTrackNumber(string? trackText)
        {
            if (string.IsNullOrWhiteSpace(trackText))
            {
                return 0;
            }
            string value = trackText.Trim();
            int separator = value.IndexOf('/');
            if (separator > 0)
            {
                value = value.Substring(0, separator);
            }
            value = value.Trim();
            if (int.TryParse(value, out int number))
            {
                return number;
            }
            return 0;
        }
    }
}
