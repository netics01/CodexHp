using System.IO;
using System.IO.Compression;
using System.Text;

namespace CodexHp.App.Infrastructure;

public sealed record ChromiumCachedResponse(string Key, long ResponseUnixMs, int StatusCode, string Headers, byte[] Body);

public sealed class ChromiumCacheFormatException : Exception
{
    public ChromiumCacheFormatException(string message)
        : base(message)
    {
    }
}

// Read-only reader for Chromium's block-file HTTP cache, as written by Electron apps.
// Layout: net/disk_cache/blockfile/disk_format.h and addr.h. Files stay open for writing by the
// owning app, so every read uses shared access and an entry is rejected if it changes mid-read.
public static class ChromiumBlockFileCache
{
    private const uint IndexMagic = 0xC103CAC3;
    private const uint BlockFileMagic = 0xC104CAC3;
    private const int IndexHeaderSize = 368;
    private const int BlockHeaderSize = 8192;
    private const int EntrySize = 256;
    private const int MaxIndexBytes = 8 * 1024 * 1024;
    private const int MaxStreamBytes = 1024 * 1024;
    private const int MaxKeyBytes = 4096;
    private const int MaxChainLength = 4096;
    private const long WindowsEpochOffsetMs = 11_644_473_600_000;
    private static readonly uint[] SupportedIndexVersions = [0x20000, 0x20001, 0x30000];

    public static IReadOnlyList<ChromiumCachedResponse> ReadResponses(string cacheDirectory, Func<string, bool> keyFilter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(keyFilter);
        var indexPath = Path.Combine(cacheDirectory, "index");
        if (!File.Exists(indexPath))
        {
            return [];
        }

        using var files = new CacheFiles(cacheDirectory);
        var index = files.ReadAll(indexPath, MaxIndexBytes);
        if (index.Length < IndexHeaderSize
            || BitConverter.ToUInt32(index, 0) != IndexMagic
            || !SupportedIndexVersions.Contains(BitConverter.ToUInt32(index, 4)))
        {
            throw new ChromiumCacheFormatException("The cache index has an unsupported format.");
        }

        var responses = new List<ChromiumCachedResponse>();
        var visited = new HashSet<uint>();
        for (var offset = IndexHeaderSize; offset + 4 <= index.Length; offset += 4)
        {
            var address = BitConverter.ToUInt32(index, offset);
            for (var chain = 0; chain < MaxChainLength && IsEntryAddress(address) && visited.Add(address); chain++)
            {
                // Entries with long inline keys span up to four consecutive blocks.
                var entry = files.ReadStream(address, EntrySize * ((int)((address >> 24) & 3) + 1));
                if (entry is null)
                {
                    break;
                }

                if (TryReadResponse(files, entry, keyFilter) is { } response)
                {
                    responses.Add(response);
                }

                address = BitConverter.ToUInt32(entry, 4);
            }
        }

        return responses;
    }

    public static long WindowsMicrosecondsToUnixMs(long windowsMicroseconds) =>
        windowsMicroseconds / 1000 - WindowsEpochOffsetMs;

    private static bool IsEntryAddress(uint address) =>
        (address & 0x80000000) != 0 && ((address >> 28) & 7) is 2 or 6;

    private static ChromiumCachedResponse? TryReadResponse(CacheFiles files, byte[] entry, Func<string, bool> keyFilter)
    {
        var keyLength = BitConverter.ToInt32(entry, 32);
        var longKeyAddress = BitConverter.ToUInt32(entry, 36);
        if (keyLength <= 0 || keyLength > MaxKeyBytes)
        {
            return null;
        }

        byte[]? keyBytes = longKeyAddress == 0
            ? 96 + keyLength <= entry.Length ? entry.AsSpan(96, keyLength).ToArray() : null
            : files.ReadStream(longKeyAddress, keyLength);
        if (keyBytes is null)
        {
            return null;
        }

        var key = Encoding.UTF8.GetString(keyBytes);
        if (!keyFilter(key))
        {
            return null;
        }

        var metadataSize = BitConverter.ToInt32(entry, 40);
        var bodySize = BitConverter.ToInt32(entry, 44);
        var metadataAddress = BitConverter.ToUInt32(entry, 56);
        var bodyAddress = BitConverter.ToUInt32(entry, 60);
        if (metadataSize <= 0 || bodySize <= 0)
        {
            return null;
        }

        var metadata = files.ReadStream(metadataAddress, metadataSize);
        var body = files.ReadStream(bodyAddress, bodySize);
        if (metadata is null || body is null || TryParseResponseInfo(metadata) is not { } info)
        {
            return null;
        }

        // The owning app may rewrite the entry while it is read; keep only a consistent snapshot.
        var recheck = files.ReadStream(metadataAddress, metadataSize);
        if (recheck is null || !recheck.AsSpan().SequenceEqual(metadata))
        {
            return null;
        }

        return new ChromiumCachedResponse(key, info.ResponseUnixMs, info.StatusCode, info.Headers, body);
    }

    // HttpResponseInfo is a base::Pickle: payload size, flags, [extra flags], request and
    // response times (Windows epoch microseconds), then NUL-separated raw headers.
    private static (long ResponseUnixMs, int StatusCode, string Headers)? TryParseResponseInfo(byte[] metadata)
    {
        if (metadata.Length < 28 || BitConverter.ToUInt32(metadata, 0) != metadata.Length - 4)
        {
            return null;
        }

        var flags = BitConverter.ToUInt32(metadata, 4);
        const uint truncated = 1 << 12;
        const uint hasExtraFlags = 1u << 31;
        if ((flags & 0xFF) != 3 || (flags & truncated) != 0)
        {
            return null;
        }

        var hasExtra = (flags & hasExtraFlags) != 0;
        var extraFlags = hasExtra ? BitConverter.ToUInt32(metadata, 8) : 0;
        var offset = hasExtra ? 12 : 8;
        if (offset + 20 > metadata.Length)
        {
            return null;
        }

        var responseMicroseconds = BitConverter.ToInt64(metadata, offset + 8);
        offset += 16 + ((extraFlags & 4) != 0 ? 8 : 0);
        if (offset + 4 > metadata.Length)
        {
            return null;
        }

        var headerLength = BitConverter.ToInt32(metadata, offset);
        if (headerLength <= 0 || offset + 4 + headerLength > metadata.Length)
        {
            return null;
        }

        var headers = Encoding.Latin1.GetString(metadata, offset + 4, headerLength);
        var statusLine = headers.Split('\0', 2)[0];
        var statusParts = statusLine.Split(' ', 3);
        if (statusParts.Length < 2 || !statusParts[0].StartsWith("HTTP/", StringComparison.Ordinal)
            || !int.TryParse(statusParts[1], out var statusCode))
        {
            return null;
        }

        return (WindowsMicrosecondsToUnixMs(responseMicroseconds), statusCode, headers);
    }

    public static string? GetHeader(string headers, string name)
    {
        foreach (var line in headers.Split('\0'))
        {
            var separator = line.IndexOf(':');
            if (separator > 0 && line.AsSpan(0, separator).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return line[(separator + 1)..].Trim();
            }
        }

        return null;
    }

    // Chromium stores the body as received; it can also hold a zstd frame regardless of headers.
    public static byte[] DecodeBody(byte[] body, string headers)
    {
        ArgumentNullException.ThrowIfNull(body);
        var encoding = GetHeader(headers ?? string.Empty, "content-encoding")?.ToLowerInvariant();
        if (body.Length >= 4 && body[0] == 0x28 && body[1] == 0xB5 && body[2] == 0x2F && body[3] == 0xFD)
        {
            var size = ZstdSharp.Decompressor.GetDecompressedSize(body);
            if (size == 0 || size > MaxStreamBytes)
            {
                throw new ChromiumCacheFormatException("The cached zstd body has an unsupported size.");
            }

            using var decompressor = new ZstdSharp.Decompressor();
            return decompressor.Unwrap(body).ToArray();
        }

        if (body.Length >= 2 && body[0] == 0x1F && body[1] == 0x8B)
        {
            return Inflate(new GZipStream(new MemoryStream(body), CompressionMode.Decompress));
        }

        return encoding switch
        {
            "br" => Inflate(new BrotliStream(new MemoryStream(body), CompressionMode.Decompress)),
            "deflate" => Inflate(new ZLibStream(new MemoryStream(body), CompressionMode.Decompress)),
            null or "" or "identity" => body,
            _ => throw new ChromiumCacheFormatException($"Unsupported content encoding '{encoding}'."),
        };
    }

    private static byte[] Inflate(Stream stream)
    {
        using (stream)
        using (var output = new MemoryStream())
        {
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                if (output.Length + read > MaxStreamBytes)
                {
                    throw new ChromiumCacheFormatException("The cached body is too large.");
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
    }

    private sealed class CacheFiles : IDisposable
    {
        private static readonly Dictionary<int, int> BlockSizes = new()
        {
            [1] = 36, [2] = 256, [3] = 1024, [4] = 4096, [5] = 8, [6] = 256, [7] = 256,
        };

        private readonly string directory;
        private readonly Dictionary<string, FileStream?> streams = new(StringComparer.OrdinalIgnoreCase);

        public CacheFiles(string directory) => this.directory = directory;

        public byte[] ReadAll(string path, int maxBytes)
        {
            var stream = this.Open(path) ?? throw new FileNotFoundException("Cache file is not available.", path);
            if (stream.Length > maxBytes)
            {
                throw new ChromiumCacheFormatException("The cache index is too large.");
            }

            var bytes = new byte[stream.Length];
            stream.Position = 0;
            stream.ReadExactly(bytes);
            return bytes;
        }

        public byte[]? ReadStream(uint address, int length)
        {
            if ((address & 0x80000000) == 0 || length <= 0 || length > MaxStreamBytes)
            {
                return null;
            }

            var kind = (int)((address >> 28) & 7);
            if (kind == 0)
            {
                var external = this.Open(Path.Combine(this.directory, $"f_{address & 0x0FFFFFFF:x6}"));
                return external is null ? null : ReadRange(external, 0, length);
            }

            if (!BlockSizes.TryGetValue(kind, out var blockSize))
            {
                return null;
            }

            var blockCount = (int)((address >> 24) & 3) + 1;
            if (length > blockSize * blockCount)
            {
                return null;
            }

            var file = this.Open(Path.Combine(this.directory, $"data_{(address >> 16) & 0xFF}"));
            if (file is null || !HasBlockFileMagic(file))
            {
                return null;
            }

            return ReadRange(file, BlockHeaderSize + (long)(address & 0xFFFF) * blockSize, length);
        }

        public void Dispose()
        {
            foreach (var stream in this.streams.Values)
            {
                stream?.Dispose();
            }
        }

        private FileStream? Open(string path)
        {
            if (!this.streams.TryGetValue(path, out var stream))
            {
                try
                {
                    stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                }
                catch (FileNotFoundException)
                {
                    stream = null;
                }

                this.streams[path] = stream;
            }

            return stream;
        }

        private static bool HasBlockFileMagic(FileStream file) =>
            ReadRange(file, 0, 4) is { } header && BitConverter.ToUInt32(header, 0) == BlockFileMagic;

        private static byte[]? ReadRange(FileStream file, long offset, int length)
        {
            if (offset < 0 || offset + length > file.Length)
            {
                return null;
            }

            var buffer = new byte[length];
            file.Position = offset;
            file.ReadExactly(buffer);
            return buffer;
        }
    }
}
