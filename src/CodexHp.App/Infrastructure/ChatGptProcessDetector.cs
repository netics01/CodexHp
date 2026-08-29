using System.Diagnostics;
using System.Text;
using CodexHp.App.Application;

namespace CodexHp.App.Infrastructure;

public sealed record ProcessIdentity(string ExecutableName, string? PackageFamilyName);

public sealed class ChatGptProcessDetector : IChatGptProcessDetector
{
    public const string OfficialExecutableName = "ChatGPT.exe";
    public const string OfficialPackageFamilyName = "OpenAI.Codex_2p2nqsd0c76g0";

    private readonly Func<IReadOnlyList<ProcessIdentity>> _snapshotProvider;

    public ChatGptProcessDetector()
        : this(ReadWindowsProcesses)
    {
    }

    public ChatGptProcessDetector(Func<IReadOnlyList<ProcessIdentity>> snapshotProvider)
    {
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
    }

    public bool IsRunning()
    {
        try
        {
            return _snapshotProvider().Any(IsOfficialApp);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsOfficialApp(ProcessIdentity identity) =>
        string.Equals(identity.ExecutableName, OfficialExecutableName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(identity.PackageFamilyName, OfficialPackageFamilyName, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ProcessIdentity> ReadWindowsProcesses()
    {
        var identities = new List<ProcessIdentity>();
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            using (process)
            {
                try
                {
                    identities.Add(new ProcessIdentity(
                        OfficialExecutableName,
                        ReadPackageFamilyName((uint)process.Id)));
                }
                catch
                {
                    // The process can exit or deny access between enumeration and inspection.
                }
            }
        }

        return identities;
    }

    private static string? ReadPackageFamilyName(uint processId)
    {
        var processHandle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryLimitedInformation,
            false,
            processId);
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
