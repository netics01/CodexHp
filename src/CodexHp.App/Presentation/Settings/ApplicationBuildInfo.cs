using System.Reflection;

namespace CodexHp.App.Presentation.Settings;

internal sealed record ApplicationBuildInfo(string ApplicationTitle, string Version, string CommitHash)
{
    private const string BuildFlavorMetadataKey = "CodexHpBuildFlavor";

    public static ApplicationBuildInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var buildFlavor = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Key,
                BuildFlavorMetadataKey,
                StringComparison.OrdinalIgnoreCase))?
            .Value;
        var assemblyVersion = assembly.GetName().Version?.ToString(3) ?? "Unknown";
        return FromMetadata(informationalVersion ?? assemblyVersion, buildFlavor);
    }

    public static ApplicationBuildInfo FromMetadata(string informationalVersion, string? buildFlavor)
    {
        var (version, commitHash) = SplitInformationalVersion(informationalVersion);
        var applicationTitle = string.Equals(buildFlavor, "Official", StringComparison.OrdinalIgnoreCase)
            ? "CodexHp"
            : "CodexHp-Dev";
        return new ApplicationBuildInfo(applicationTitle, version, commitHash);
    }

    private static (string Version, string CommitHash) SplitInformationalVersion(string informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return ("Unknown", "Unknown");
        }

        var separator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return separator <= 0 || separator == informationalVersion.Length - 1
            ? (informationalVersion, "Unknown")
            : (informationalVersion[..separator], informationalVersion[(separator + 1)..]);
    }
}
