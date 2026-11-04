using System;
using System.Collections.Generic;
using System.IO;

namespace PlaylistMaker.Services
{
    /// <summary>
    /// Best-effort track length straight from the container headers - no decoding, no external
    /// library. MP3 (Xing/Info frame count, falling back to a constant-bitrate estimate), FLAC
    /// (STREAMINFO), WAV (RIFF fmt/data), OGG/Opus (last granule position) and MP4/M4A (mvhd box).
    /// Anything else returns null and the UI shows a dash.
    /// </summary>
    public static class MediaDurationReader
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, TimeSpan?> Cache =
            new Dictionary<string, TimeSpan?>(StringComparer.OrdinalIgnoreCase);

        // version bits: 0 = MPEG 2.5, 2 = MPEG 2, 3 = MPEG 1 (1 is reserved)
        private static readonly int[] Mpeg1SampleRates = { 44100, 48000, 32000 };
        private static readonly int[] Mpeg2SampleRates = { 22050, 24000, 16000 };
        private static readonly int[] Mpeg25SampleRates = { 11025, 12000, 8000 };

        private static readonly int[] Mpeg1Layer3Bitrates = { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 };
        private static readonly int[] Mpeg2Layer3Bitrates = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 };

        public static TimeSpan? Read(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            lock (CacheLock)
            {
                if (Cache.TryGetValue(filePath, out TimeSpan? cached))
                {
                    return cached;
                }
            }

            TimeSpan? result = ReadCore(filePath);
            lock (CacheLock)
            {
                Cache[filePath] = result;
            }
            return result;
        }

        public static void ClearCache()
        {
            lock (CacheLock)
            {
                Cache.Clear();
            }
        }

        private static TimeSpan? ReadCore(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return null;
                }

                FileInfo info = new FileInfo(filePath);
                if (info.Length <= 0)
                {
                    return null;
                }

                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                double? seconds;
                switch (extension)
                {
                    case ".mp3":
                    case ".mp2":
                        seconds = ReadMp3(filePath);
                        break;
                    case ".flac":
                        seconds = ReadFlac(filePath);
                        break;
                    case ".wav":
                        seconds = ReadWav(filePath);
                        break;
                    case ".ogg":
                    case ".oga":
                    case ".opus":
                    case ".spx":
                        seconds = ReadOgg(filePath);
                        break;
                    case ".m4a":
                    case ".m4b":
                    case ".mp4":
                        seconds = ReadMp4(filePath);
                        break;
                    default:
                        seconds = null;
                        break;
                }

                if (seconds == null || double.IsNaN(seconds.Value) || double.IsInfinity(seconds.Value))
                {
                    return null;
                }
                if (seconds.Value <= 0.0 || seconds.Value > TimeSpan.FromHours(24).TotalSeconds)
                {
                    return null;
                }
                return TimeSpan.FromSeconds(seconds.Value);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        // ── MP3 ───────────────────────────────────────────────────────────────
        private static double? ReadMp3(string filePath)
        {
            using FileStream stream = File.OpenRead(filePath);
            long length = stream.Length;
            int headSize = (int)Math.Min(length, 1024L * 1024L);
            byte[] head = new byte[headSize];
            int read = ReadFully(stream, head, headSize);
            if (read < 4)
            {
                return null;
            }

            int start = 0;
            if (read >= 10 && head[0] == (byte)'I' && head[1] == (byte)'D' && head[2] == (byte)'3')
            {
                int bodySize = ((head[6] & 0x7F) << 21) | ((head[7] & 0x7F) << 14) | ((head[8] & 0x7F) << 7) | (head[9] & 0x7F);
                start = 10 + bodySize;
                if ((head[5] & 0x10) != 0)
                {
                    start += 10;
                }
            }

            for (int position = start; position + 4 <= read; position++)
            {
                if (head[position] != 0xFF || (head[position + 1] & 0xE0) != 0xE0)
                {
                    continue;
                }

                byte second = head[position + 1];
                byte third = head[position + 2];
                byte fourth = head[position + 3];
                int versionBits = (second >> 3) & 0x03;
                int layerBits = (second >> 1) & 0x03;
                int bitrateIndex = (third >> 4) & 0x0F;
                int sampleRateIndex = (third >> 2) & 0x03;

                if (versionBits == 1 || layerBits != 1 || bitrateIndex <= 0 || bitrateIndex >= 15 || sampleRateIndex == 3)
                {
                    continue;
                }

                int sampleRate;
                int[] bitrateTable;
                if (versionBits == 3)
                {
                    sampleRate = Mpeg1SampleRates[sampleRateIndex];
                    bitrateTable = Mpeg1Layer3Bitrates;
                }
                else if (versionBits == 2)
                {
                    sampleRate = Mpeg2SampleRates[sampleRateIndex];
                    bitrateTable = Mpeg2Layer3Bitrates;
                }
                else
                {
                    sampleRate = Mpeg25SampleRates[sampleRateIndex];
                    bitrateTable = Mpeg2Layer3Bitrates;
                }

                int bitrate = bitrateTable[bitrateIndex] * 1000;
                int channelMode = (fourth >> 6) & 0x03;
                bool isMono = channelMode == 3;
                int samplesPerFrame = versionBits == 3 ? 1152 : 576;
                int sideInfoSize;
                if (versionBits == 3)
                {
                    sideInfoSize = isMono ? 17 : 32;
                }
                else
                {
                    sideInfoSize = isMono ? 9 : 17;
                }

                int xingOffset = position + 4 + sideInfoSize;
                if (xingOffset + 12 <= read
                    && ((head[xingOffset] == (byte)'X' && head[xingOffset + 1] == (byte)'i')
                        || (head[xingOffset] == (byte)'I' && head[xingOffset + 1] == (byte)'n'))
                    && head[xingOffset + 2] == (byte)'n' && head[xingOffset + 3] == (byte)'g')
                {
                    int flags = (head[xingOffset + 4] << 24) | (head[xingOffset + 5] << 16)
                        | (head[xingOffset + 6] << 8) | head[xingOffset + 7];
                    if ((flags & 0x01) != 0)
                    {
                        long frames = ((long)head[xingOffset + 8] << 24) | ((long)head[xingOffset + 9] << 16)
                            | ((long)head[xingOffset + 10] << 8) | head[xingOffset + 11];
                        if (frames > 0)
                        {
                            return frames * (double)samplesPerFrame / sampleRate;
                        }
                    }
                }

                if (bitrate <= 0)
                {
                    return null;
                }

                long audioBytes = length - position;
                if (audioBytes > 128)
                {
                    byte[] tail = new byte[3];
                    stream.Seek(length - 128, SeekOrigin.Begin);
                    if (ReadFully(stream, tail, 3) == 3 && tail[0] == (byte)'T' && tail[1] == (byte)'A' && tail[2] == (byte)'G')
                    {
                        audioBytes -= 128;
                    }
                }
                if (audioBytes <= 0)
                {
                    return null;
                }
                return audioBytes * 8.0 / bitrate;
            }

            return null;
        }

        // ── FLAC ──────────────────────────────────────────────────────────────
        private static double? ReadFlac(string filePath)
        {
            using FileStream stream = File.OpenRead(filePath);
            byte[] signature = new byte[4];
            if (ReadFully(stream, signature, 4) != 4)
            {
                return null;
            }
            if (signature[0] != (byte)'f' || signature[1] != (byte)'L' || signature[2] != (byte)'a' || signature[3] != (byte)'C')
            {
                return null;
            }

            byte[] blockHeader = new byte[4];
            int guard = 0;
            while (guard++ < 128)
            {
                if (ReadFully(stream, blockHeader, 4) != 4)
                {
                    return null;
                }
                bool isLast = (blockHeader[0] & 0x80) != 0;
                int blockType = blockHeader[0] & 0x7F;
                int blockLength = (blockHeader[1] << 16) | (blockHeader[2] << 8) | blockHeader[3];
                if (blockLength < 0)
                {
                    return null;
                }

                if (blockType == 0)
                {
                    if (blockLength < 34)
                    {
                        return null;
                    }
                    byte[] streamInfo = new byte[34];
                    if (ReadFully(stream, streamInfo, 34) != 34)
                    {
                        return null;
                    }

                    ulong packed = 0;
                    for (int index = 10; index < 18; index++)
                    {
                        packed = (packed << 8) | streamInfo[index];
                    }
                    int sampleRate = (int)((packed >> 44) & 0xFFFFF);
                    ulong totalSamples = packed & 0xFFFFFFFFFUL;
                    if (sampleRate <= 0 || totalSamples == 0)
                    {
                        return null;
                    }
                    return totalSamples / (double)sampleRate;
                }

                if (isLast)
                {
                    return null;
                }
                stream.Seek(blockLength, SeekOrigin.Current);
            }
            return null;
        }

        // ── WAV ───────────────────────────────────────────────────────────────
        private static double? ReadWav(string filePath)
        {
            using FileStream stream = File.OpenRead(filePath);
            byte[] header = new byte[12];
            if (ReadFully(stream, header, 12) != 12)
            {
                return null;
            }
            if (header[0] != (byte)'R' || header[1] != (byte)'I' || header[2] != (byte)'F' || header[3] != (byte)'F'
                || header[8] != (byte)'W' || header[9] != (byte)'A' || header[10] != (byte)'V' || header[11] != (byte)'E')
            {
                return null;
            }

            byte[] chunkHeader = new byte[8];
            int byteRate = 0;
            int guard = 0;
            while (guard++ < 4096)
            {
                if (ReadFully(stream, chunkHeader, 8) != 8)
                {
                    return null;
                }
                int chunkSize = chunkHeader[4] | (chunkHeader[5] << 8) | (chunkHeader[6] << 16) | (chunkHeader[7] << 24);
                if (chunkSize < 0)
                {
                    return null;
                }

                bool isFormat = chunkHeader[0] == (byte)'f' && chunkHeader[1] == (byte)'m'
                    && chunkHeader[2] == (byte)'t' && chunkHeader[3] == (byte)' ';
                bool isData = chunkHeader[0] == (byte)'d' && chunkHeader[1] == (byte)'a'
                    && chunkHeader[2] == (byte)'t' && chunkHeader[3] == (byte)'a';

                if (isFormat)
                {
                    int readSize = Math.Min(chunkSize, 40);
                    byte[] format = new byte[readSize];
                    if (ReadFully(stream, format, readSize) != readSize || readSize < 12)
                    {
                        return null;
                    }
                    byteRate = format[8] | (format[9] << 8) | (format[10] << 16) | (format[11] << 24);
                    stream.Seek(chunkSize - readSize + (chunkSize & 1), SeekOrigin.Current);
                    continue;
                }

                if (isData)
                {
                    if (byteRate <= 0)
                    {
                        return null;
                    }
                    return chunkSize / (double)byteRate;
                }

                stream.Seek(chunkSize + (chunkSize & 1), SeekOrigin.Current);
            }
            return null;
        }

        // ── OGG / Opus ────────────────────────────────────────────────────────
        private static double? ReadOgg(string filePath)
        {
            using FileStream stream = File.OpenRead(filePath);
            long length = stream.Length;

            int headSize = (int)Math.Min(length, 16384L);
            byte[] head = new byte[headSize];
            if (ReadFully(stream, head, headSize) < 16)
            {
                return null;
            }

            int sampleRate = FindOggSampleRate(head, headSize);
            if (sampleRate <= 0)
            {
                return null;
            }

            int tailSize = (int)Math.Min(length, 65536L);
            byte[] tail = new byte[tailSize];
            stream.Seek(length - tailSize, SeekOrigin.Begin);
            if (ReadFully(stream, tail, tailSize) != tailSize)
            {
                return null;
            }

            for (int position = tailSize - 8; position >= 0; position--)
            {
                if (tail[position] != (byte)'O' || tail[position + 1] != (byte)'g'
                    || tail[position + 2] != (byte)'g' || tail[position + 3] != (byte)'S')
                {
                    continue;
                }
                if (position + 14 > tailSize)
                {
                    continue;
                }

                ulong granule = 0;
                for (int index = 13; index >= 6; index--)
                {
                    granule = (granule << 8) | tail[position + index];
                }
                if (granule == ulong.MaxValue || granule == 0)
                {
                    continue;
                }
                return granule / (double)sampleRate;
            }
            return null;
        }

        private static int FindOggSampleRate(byte[] head, int length)
        {
            int opus = IndexOf(head, length, "OpusHead");
            if (opus >= 0)
            {
                // Opus always reports its granule positions at 48 kHz
                return 48000;
            }

            int vorbis = IndexOf(head, length, "vorbis");
            if (vorbis >= 0 && vorbis + 15 <= length)
            {
                return head[vorbis + 11] | (head[vorbis + 12] << 8)
                    | (head[vorbis + 13] << 16) | (head[vorbis + 14] << 24);
            }
            return 0;
        }

        // ── MP4 / M4A ─────────────────────────────────────────────────────────
        private static double? ReadMp4(string filePath)
        {
            using FileStream stream = File.OpenRead(filePath);
            long length = stream.Length;

            // moov can sit at the start or the end of the file, so try the head and then the tail
            long[] windows = { 0L, Math.Max(0L, length - 4L * 1024L * 1024L) };
            int[] sizes = { (int)Math.Min(length, 4L * 1024L * 1024L), (int)Math.Min(length, 4L * 1024L * 1024L) };

            for (int index = 0; index < windows.Length; index++)
            {
                int size = sizes[index];
                if (size < 32)
                {
                    continue;
                }
                byte[] buffer = new byte[size];
                stream.Seek(windows[index], SeekOrigin.Begin);
                int read = ReadFully(stream, buffer, size);
                if (read < 32)
                {
                    continue;
                }

                int position = 0;
                while (true)
                {
                    position = IndexOf(buffer, read, "mvhd", position);
                    if (position < 0 || position + 32 > read)
                    {
                        break;
                    }

                    int version = buffer[position + 4];
                    long timescale;
                    ulong duration;
                    if (version == 1 && position + 40 <= read)
                    {
                        timescale = ReadBigEndian32(buffer, position + 24);
                        duration = ReadBigEndian64(buffer, position + 28);
                    }
                    else if (version == 0)
                    {
                        timescale = ReadBigEndian32(buffer, position + 16);
                        duration = (ulong)ReadBigEndian32(buffer, position + 20);
                    }
                    else
                    {
                        break;
                    }

                    if (timescale > 0 && duration > 0)
                    {
                        return duration / (double)timescale;
                    }
                    position += 4;
                }
            }
            return null;
        }

        // ── small helpers ─────────────────────────────────────────────────────
        private static int ReadFully(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read <= 0)
                {
                    break;
                }
                total += read;
            }
            return total;
        }

        private static int IndexOf(byte[] buffer, int length, string text)
        {
            return IndexOf(buffer, length, text, 0);
        }

        private static int IndexOf(byte[] buffer, int length, string text, int start)
        {
            int limit = length - text.Length;
            for (int position = Math.Max(0, start); position <= limit; position++)
            {
                bool matches = true;
                for (int index = 0; index < text.Length; index++)
                {
                    if (buffer[position + index] != text[index])
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                {
                    return position;
                }
            }
            return -1;
        }

        private static long ReadBigEndian32(byte[] buffer, int offset)
        {
            return ((long)buffer[offset] << 24) | ((long)buffer[offset + 1] << 16)
                | ((long)buffer[offset + 2] << 8) | buffer[offset + 3];
        }

        private static ulong ReadBigEndian64(byte[] buffer, int offset)
        {
            ulong value = 0;
            for (int index = 0; index < 8; index++)
            {
                value = (value << 8) | buffer[offset + index];
            }
            return value;
        }
    }
}
