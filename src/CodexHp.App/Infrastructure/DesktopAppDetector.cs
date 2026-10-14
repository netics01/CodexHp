using System.Diagnostics;
using System.Text;
using CodexHp.App.Application;
using CodexHp.Core.Domain;

namespace CodexHp.App.Infrastructure;

public sealed record DesktopAppState(bool IsClaudeRunning, UsageProvider? ForegroundProvider);

public interface IDesktopAppDetector
{
    DesktopAppState Read();
}

// Identifies the Codex (ChatGPT) and Claude desktop apps by their package family names, so a
// same-named executable from elsewhere, such as the bundled Claude Code CLI, is not mistaken for them.
public sealed class DesktopAppDetector : IDesktopAppDetector
{
    private readonly Func<IEnumerable<uint>> claudeProcessIds;
    private readonly Func<uint?> foregroundProcessId;
    private readonly Func<uint, string?> readPackageFamilyName;

    public DesktopAppDetector()
        : this(ReadClaudeProcessIds, ReadForegroundProcessId, ReadPackageFamilyName)
    {
    }

    public DesktopAppDetector(
        Func<IEnumerable<uint>> claudeProcessIds,
        Func<uint?> foregroundProcessId,
        Func<uint, string?> readPackageFamilyName)
    {
        this.claudeProcessIds = claudeProcessIds ?? throw new ArgumentNullException(nameof(claudeProcessIds));
        this.foregroundProcessId = foregroundProcessId ?? throw new ArgumentNullException(nameof(foregroundProcessId));
        this.readPackageFamilyName = readPackageFamilyName ?? throw new ArgumentNullException(nameof(readPackageFamilyName));
    }

    public static UsageProvider? ProviderForPackage(string? packageFamilyName) =>
        string.Equals(packageFamilyName, ChatGptProcessDetector.OfficialPackageFamilyName, StringComparison.OrdinalIgnoreCase)
            ? UsageProvider.Codex
            : string.Equals(packageFamilyName, ClaudeDesktopUsageSource.PackageFamilyName, StringComparison.OrdinalIgnoreCase)
                ? UsageProvider.Claude
                : null;

    public DesktopAppState Read()
    {
        var isClaudeRunning = false;
        try
        {
            isClaudeRunning = this.claudeProcessIds()
                .Any(id => ProviderForPackage(this.readPackageFamilyName(id)) == UsageProvider.Claude);
        }
        catch
        {
            // Processes can exit or deny access during inspection.
        }

        UsageProvider? foreground = null;
        try
        {
            if (this.foregroundProcessId() is { } processId)
            {
                foreground = ProviderForPackage(this.readPackageFamilyName(processId));
            }
        }
        catch
        {
        }

        return new DesktopAppState(isClaudeRunning, foreground);
    }

    private static IEnumerable<uint> ReadClaudeProcessIds()
    {
        var ids = new List<uint>();
        foreach (var process in Process.GetProcessesByName("Claude"))
        {
            using (process)
            {
                ids.Add((uint)process.Id);
            }
        }

        return ids;
    }

    private static uint? ReadForegroundProcessId()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == nint.Zero || NativeMethods.GetWindowThreadProcessId(window, out var processId) == 0 || processId == 0)
        {
            return null;
        }

        return processId;
    }

    internal static string? ReadPackageFamilyName(uint processId)
    {
        var processHandle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (processHandle == nint.Zero)
        {
            return null;
        }

        try
        {
            uint length = 0;
            var result = NativeMethods.GetPackageFamilyName(processHandle, ref length, null);
            if (result != NativeMethods.ErrorInsufficientBuffer || length == 0)
            {
                return null;
            }

            var buffer = new StringBuilder(checked((int)length));
            result = NativeMethods.GetPackageFamilyName(processHandle, ref length, buffer);
            return result == 0 ? buffer.ToString() : null;
        }
        finally
        {
            NativeMethods.CloseHandle(processHandle);
        }
    }
}
