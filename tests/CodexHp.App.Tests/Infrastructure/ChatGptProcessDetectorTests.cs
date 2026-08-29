using CodexHp.App.Infrastructure;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class ChatGptProcessDetectorTests
{
    [Theory]
    [InlineData("ChatGPT.exe", "OpenAI.Codex_2p2nqsd0c76g0", true)]
    [InlineData("chatgpt.EXE", "OpenAI.Codex_2p2nqsd0c76g0", true)]
    [InlineData("ChatGPT.exe", null, false)]
    [InlineData("ChatGPT.exe", "Someone.ChatGPT_2p2nqsd0c76g0", false)]
    [InlineData("codex.exe", "OpenAI.Codex_2p2nqsd0c76g0", false)]
    public void Official_app_requires_both_executable_and_package_family(
        string executableName,
        string? packageFamilyName,
        bool expected)
    {
        var identity = new ProcessIdentity(executableName, packageFamilyName);

        Assert.Equal(expected, ChatGptProcessDetector.IsOfficialApp(identity));
    }

    [Fact]
    public void Running_is_true_when_any_process_is_the_official_app()
    {
        var detector = new ChatGptProcessDetector(() =>
        [
            new ProcessIdentity("codex.exe", null),
            new ProcessIdentity("ChatGPT.exe", null),
            new ProcessIdentity("ChatGPT.exe", "OpenAI.Codex_2p2nqsd0c76g0")
        ]);

        Assert.True(detector.IsRunning());
    }

    [Fact]
    public void Inaccessible_process_source_is_treated_as_not_running()
    {
        var detector = new ChatGptProcessDetector(
            () => throw new InvalidOperationException("process disappeared"));

        Assert.False(detector.IsRunning());
    }
}
