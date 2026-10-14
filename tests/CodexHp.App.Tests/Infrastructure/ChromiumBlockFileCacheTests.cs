using System.IO.Compression;
using System.Text;
using CodexHp.App.Infrastructure;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class ChromiumBlockFileCacheTests : IDisposable
{
    private const long ResponseUnixMs = 1_791_985_185_000;
    private const string JsonHeaders = "HTTP/1.1 200\r\ncontent-type: application/json\r\ncontent-encoding: zstd";
    private readonly string root = Path.Combine(Path.GetTempPath(), "CodexHp.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Reads_matching_responses_with_time_status_and_body()
    {
        new ChromiumCacheFixture()
            .Add("1/0/https://example.test/a", ResponseUnixMs, JsonHeaders, ChromiumCacheFixture.Zstd("{\"a\":1}"))
            .Add("1/0/https://example.test/b", ResponseUnixMs, JsonHeaders, ChromiumCacheFixture.Zstd("{\"b\":2}"))
            .Write(this.root);

        var response = Assert.Single(ChromiumBlockFileCache.ReadResponses(this.root, key => key.EndsWith("/a")));

        Assert.Equal("1/0/https://example.test/a", response.Key);
        Assert.Equal(ResponseUnixMs, response.ResponseUnixMs);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(ChromiumBlockFileCache.DecodeBody(response.Body, response.Headers)));
    }

    [Fact]
    public void Skips_truncated_responses()
    {
        new ChromiumCacheFixture()
            .Add("https://example.test/a", ResponseUnixMs, JsonHeaders, ChromiumCacheFixture.Zstd("{}"), flags: 3 | (1 << 12))
            .Write(this.root);

        Assert.Empty(ChromiumBlockFileCache.ReadResponses(this.root, _ => true));
    }

    [Fact]
    public void Rejects_an_unknown_index_format()
    {
        Directory.CreateDirectory(this.root);
        File.WriteAllBytes(Path.Combine(this.root, "index"), new byte[512]);

        Assert.Throws<ChromiumCacheFormatException>(() => ChromiumBlockFileCache.ReadResponses(this.root, _ => true));
    }

    [Fact]
    public void Missing_cache_returns_no_responses()
    {
        Assert.Empty(ChromiumBlockFileCache.ReadResponses(this.root, _ => true));
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    [InlineData("identity")]
    public void Decodes_common_content_encodings(string encoding)
    {
        var text = "{\"value\":42}";
        var raw = Encoding.UTF8.GetBytes(text);
        var body = encoding switch
        {
            "gzip" => Compress(raw, stream => new GZipStream(stream, CompressionLevel.Fastest)),
            "br" => Compress(raw, stream => new BrotliStream(stream, CompressionLevel.Fastest)),
            _ => raw,
        };

        var decoded = ChromiumBlockFileCache.DecodeBody(body, $"HTTP/1.1 200\0content-encoding: {encoding}");

        Assert.Equal(text, Encoding.UTF8.GetString(decoded));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, recursive: true);
        }
    }

    private static byte[] Compress(byte[] raw, Func<Stream, Stream> create)
    {
        using var output = new MemoryStream();
        using (var stream = create(output))
        {
            stream.Write(raw);
        }

        return output.ToArray();
    }
}
