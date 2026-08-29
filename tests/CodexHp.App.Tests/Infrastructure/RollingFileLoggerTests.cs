using CodexHp.App.Application;
using CodexHp.App.Infrastructure;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class RollingFileLoggerTests : IDisposable
{
    private readonly string localAppData = Path.Combine(
        Path.GetTempPath(),
        "CodexHp.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Log_creates_directory_and_rotates_to_at_most_three_files()
    {
        var logger = new RollingFileLogger(
            this.localAppData,
            maxFileBytes: 120,
            maxFiles: 3,
            clock: () => new DateTimeOffset(2026, 8, 15, 12, 34, 56, TimeSpan.Zero));

        for (var index = 0; index < 12; index++)
        {
            logger.Log(DiagnosticLevel.Information, "test", $"message-{index:D2}-with-padding-1234567890");
        }

        var files = Directory.GetFiles(Path.Combine(this.localAppData, "CodexHp", "Logs"), "CodexHp*.log");
        Assert.InRange(files.Length, 2, 3);
        Assert.All(files, file => Assert.NotEmpty(File.ReadAllText(file)));
    }

    [Fact]
    public void Log_redacts_authentication_and_account_values()
    {
        var logger = new RollingFileLogger(this.localAppData);

        logger.Log(
            DiagnosticLevel.Error,
            "usage",
            "Authorization: Bearer bearer-secret account_id=account-secret access_token=access-secret",
            new InvalidOperationException("refresh_token=refresh-secret"));

        var text = string.Join(
            Environment.NewLine,
            Directory.GetFiles(Path.Combine(this.localAppData, "CodexHp", "Logs"), "*.log")
                .Select(File.ReadAllText));
        Assert.DoesNotContain("bearer-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("account-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("access-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-secret", text, StringComparison.Ordinal);
        Assert.Contains("<redacted>", text, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.localAppData))
        {
            Directory.Delete(this.localAppData, recursive: true);
        }
    }
}
