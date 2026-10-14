using System.IO;
using System.Text;

namespace CodexHp.App.Tests.Infrastructure;

// Writes a minimal Chromium block-file cache: one index, one data_1 file of 256-byte entry
// blocks, and external f_* files for metadata and bodies.
internal sealed class ChromiumCacheFixture
{
    private const uint IndexMagic = 0xC103CAC3;
    private const uint BlockFileMagic = 0xC104CAC3;
    private readonly List<(string Key, byte[] Metadata, byte[] Body)> entries = [];

    public ChromiumCacheFixture Add(string key, long responseUnixMs, string headers, byte[] body, uint flags = 3)
    {
        this.entries.Add((key, CreateMetadata(responseUnixMs, headers, flags), body));
        return this;
    }

    public void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        var index = new byte[368 + 64 * 4];
        BitConverter.GetBytes(IndexMagic).CopyTo(index, 0);
        BitConverter.GetBytes(0x30000u).CopyTo(index, 4);
        var data = new byte[8192 + this.entries.Count * 256];
        BitConverter.GetBytes(BlockFileMagic).CopyTo(data, 0);
        var externalNumber = 1u;
        for (var i = 0; i < this.entries.Count; i++)
        {
            var (key, metadata, body) = this.entries[i];
            var entryAddress = 0x80000000u | (2u << 28) | (1u << 16) | (uint)i;
            BitConverter.GetBytes(entryAddress).CopyTo(index, 368 + i * 4);
            var entry = data.AsSpan(8192 + i * 256, 256);
            var keyBytes = Encoding.UTF8.GetBytes(key);
            BitConverter.GetBytes(keyBytes.Length).CopyTo(entry[32..]);
            BitConverter.GetBytes(metadata.Length).CopyTo(entry[40..]);
            BitConverter.GetBytes(body.Length).CopyTo(entry[44..]);
            var metadataAddress = 0x80000000u | externalNumber++;
            var bodyAddress = 0x80000000u | externalNumber++;
            BitConverter.GetBytes(metadataAddress).CopyTo(entry[56..]);
            BitConverter.GetBytes(bodyAddress).CopyTo(entry[60..]);
            keyBytes.CopyTo(entry[96..]);
            File.WriteAllBytes(Path.Combine(directory, $"f_{metadataAddress & 0x0FFFFFFF:x6}"), metadata);
            File.WriteAllBytes(Path.Combine(directory, $"f_{bodyAddress & 0x0FFFFFFF:x6}"), body);
        }

        File.WriteAllBytes(Path.Combine(directory, "index"), index);
        File.WriteAllBytes(Path.Combine(directory, "data_1"), data);
    }

    public static byte[] Zstd(string text)
    {
        using var compressor = new ZstdSharp.Compressor();
        return compressor.Wrap(Encoding.UTF8.GetBytes(text)).ToArray();
    }

    private static byte[] CreateMetadata(long responseUnixMs, string headers, uint flags)
    {
        var windowsMicroseconds = (responseUnixMs + 11_644_473_600_000) * 1000;
        var headerBytes = Encoding.Latin1.GetBytes(headers.Replace("\r\n", "\0") + "\0");
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(0u);
        writer.Write(flags);
        writer.Write(windowsMicroseconds - 1000);
        writer.Write(windowsMicroseconds);
        writer.Write(headerBytes.Length);
        writer.Write(headerBytes);
        writer.Flush();
        var bytes = payload.ToArray();
        BitConverter.GetBytes((uint)(bytes.Length - 4)).CopyTo(bytes, 0);
        return bytes;
    }
}
