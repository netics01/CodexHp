using System.Globalization;
using System.Text.RegularExpressions;

namespace CodexHp.App.Application;

public sealed record AvailableUpdate
{
    private AvailableUpdate(Version version, Uri releaseUri)
    {
        this.Version = version;
        this.ReleaseUri = releaseUri;
    }

    public Version Version { get; }
    public Uri ReleaseUri { get; }

    public static AvailableUpdate FromTag(string tag)
    {
        if (tag.Length > 128) throw new FormatException("Release tag is too long.");
        var match = Regex.Match(tag,
            @"\Av?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:\+[0-9A-Za-z.-]+)?\z",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new FormatException("Release tag is not a stable semantic version.");
        var parts = new int[3];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(match.Groups[i + 1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out parts[i]))
                throw new FormatException("Release version component is out of range.");
        // Never launch an arbitrary URL from the network response.
        return new(new Version(parts[0], parts[1], parts[2]),
            new Uri("https://github.com/netics01/codexhp/releases/tag/" + Uri.EscapeDataString(tag)));
    }
}
