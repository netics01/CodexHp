using System.IO;
using System.Text;

namespace CodexHp.App.Infrastructure;

public sealed class TokenFileCursorCache
{
    private readonly Dictionary<string, CacheEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();

    public IReadOnlyList<string> ReadLines(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        lock (this.sync)
        {
            var info = new FileInfo(file);
            info.Refresh();
            if (!info.Exists)
            {
                throw new FileNotFoundException("Token activity file is not available.", file);
            }

            this.entries.TryGetValue(file, out var entry);
            if (entry is not null
                && info.Length == entry.Length
                && info.LastWriteTimeUtc == entry.LastWriteTimeUtc)
            {
                return entry.Snapshot;
            }

            var append = entry is not null
                && info.Length > entry.Length
                && info.LastWriteTimeUtc >= entry.LastWriteTimeUtc;
            if (!append)
            {
                entry = new CacheEntry();
                this.entries[file] = entry;
            }

            var offset = append ? entry!.Length : 0;
            var appendedText = ReadText(file, offset, out var endOffset);
            MergeText(entry!, appendedText);
            entry!.Length = endOffset;
            entry.LastWriteTimeUtc = File.GetLastWriteTimeUtc(file);
            entry.Snapshot = entry.Pending.Length == 0
                ? entry.CompletedLines.ToArray()
                : [.. entry.CompletedLines, entry.Pending];
            return entry.Snapshot;
        }
    }

    private static string ReadText(string file, long offset, out long endOffset)
    {
        using var stream = new FileStream(
            file,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (offset < 0 || offset > stream.Length)
        {
            offset = 0;
        }

        stream.Position = offset;
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: offset == 0,
            bufferSize: 4096,
            leaveOpen: true);
        var text = reader.ReadToEnd();
        endOffset = stream.Position;
        return text;
    }

    private static void MergeText(CacheEntry entry, string appendedText)
    {
        var combined = entry.Pending + appendedText;
        entry.Pending = string.Empty;
        if (combined.Length == 0)
        {
            return;
        }

        var parts = combined.Split('\n');
        var completeCount = parts.Length - 1;
        for (var index = 0; index < completeCount; index++)
        {
            entry.CompletedLines.Add(parts[index].TrimEnd('\r'));
        }

        if (!combined.EndsWith('\n'))
        {
            entry.Pending = parts[^1].TrimEnd('\r');
        }
    }

    private sealed class CacheEntry
    {
        public List<string> CompletedLines { get; } = [];

        public string Pending { get; set; } = string.Empty;

        public long Length { get; set; }

        public DateTime LastWriteTimeUtc { get; set; }

        public IReadOnlyList<string> Snapshot { get; set; } = Array.Empty<string>();
    }
}
