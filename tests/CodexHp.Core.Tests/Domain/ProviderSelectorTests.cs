using CodexHp.Core.Domain;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.Core.Tests.Domain;

public sealed class ProviderSelectorTests
{
    private const long Start = 1_000_000;

    [Fact]
    public void First_selection_uses_the_foreground_app_without_emphasis()
    {
        var selector = new ProviderSelector();

        var selection = selector.Update(ProviderMode.Automatic, UsageProvider.Claude, true, true, Start);

        Assert.Equal(new ProviderSelection(UsageProvider.Claude, false), selection);
    }

    [Fact]
    public void First_selection_defaults_to_Codex_when_neither_app_is_in_the_foreground()
    {
        var selection = new ProviderSelector().Update(ProviderMode.Automatic, null, true, true, Start);

        Assert.Equal(UsageProvider.Codex, selection.Provider);
    }

    [Fact]
    public void Switches_only_after_the_foreground_app_stays_for_the_dwell_time()
    {
        var selector = new ProviderSelector();
        selector.Update(ProviderMode.Automatic, UsageProvider.Codex, true, true, Start);

        Assert.Null(selector.PendingSwitchUnixMs);
        Assert.Equal(UsageProvider.Codex, selector.Update(ProviderMode.Automatic, UsageProvider.Claude, true, true, Start + 100).Provider);
        Assert.Equal(Start + 400, selector.PendingSwitchUnixMs);
        Assert.Equal(UsageProvider.Codex, selector.Update(ProviderMode.Automatic, UsageProvider.Claude, true, true, Start + 350).Provider);
        var switched = selector.Update(ProviderMode.Automatic, UsageProvider.Claude, true, true, Start + 400);

        Assert.Equal(new ProviderSelection(UsageProvider.Claude, true), switched);
        Assert.Null(selector.PendingSwitchUnixMs);
    }

    [Fact]
    public void A_brief_Alt_Tab_pass_does_not_switch()
    {
        var selector = new ProviderSelector();
        selector.Update(ProviderMode.Automatic, UsageProvider.Codex, true, true, Start);
        selector.Update(ProviderMode.Automatic, UsageProvider.Claude, true, true, Start + 100);
        selector.Update(ProviderMode.Automatic, null, true, true, Start + 900);

        var selection = selector.Update(ProviderMode.Automatic, UsageProvider.Claude, true, true, Start + 2_000);

        Assert.Equal(UsageProvider.Codex, selection.Provider);
    }

    [Fact]
    public void Other_foreground_apps_keep_the_last_provider()
    {
        var selector = new ProviderSelector();
        selector.Update(ProviderMode.Automatic, UsageProvider.Claude, true, true, Start);

        var selection = selector.Update(ProviderMode.Automatic, null, true, true, Start + 60_000);

        Assert.Equal(new ProviderSelection(UsageProvider.Claude, false), selection);
    }

    [Fact]
    public void Emphasis_ends_after_the_emphasis_window()
    {
        var selector = new ProviderSelector(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        selector.Update(ProviderMode.Automatic, UsageProvider.Codex, true, true, Start);

        Assert.True(selector.Update(ProviderMode.Automatic, UsageProvider.Claude, true, true, Start + 10).IsLabelEmphasized);
        Assert.True(selector.Update(ProviderMode.Automatic, UsageProvider.Claude, true, true, Start + 2_009).IsLabelEmphasized);
        Assert.False(selector.Update(ProviderMode.Automatic, UsageProvider.Claude, true, true, Start + 2_010).IsLabelEmphasized);
    }

    [Theory]
    [InlineData(ProviderMode.Codex, UsageProvider.Codex)]
    [InlineData(ProviderMode.Claude, UsageProvider.Claude)]
    public void Pinned_modes_ignore_the_foreground_app(ProviderMode mode, UsageProvider expected)
    {
        var selector = new ProviderSelector(TimeSpan.Zero);

        foreach (var foreground in new UsageProvider?[] { UsageProvider.Codex, UsageProvider.Claude, null })
        {
            Assert.Equal(expected, selector.Update(mode, foreground, true, true, Start).Provider);
        }
    }

    [Theory]
    [InlineData(true, false, UsageProvider.Codex)]
    [InlineData(false, true, UsageProvider.Claude)]
    public void A_single_available_provider_is_shown_regardless_of_the_foreground_app(
        bool codexAvailable, bool claudeAvailable, UsageProvider expected)
    {
        var selector = new ProviderSelector(TimeSpan.Zero);
        var other = expected == UsageProvider.Codex ? UsageProvider.Claude : UsageProvider.Codex;

        var selection = selector.Update(ProviderMode.Automatic, other, codexAvailable, claudeAvailable, Start);

        Assert.Equal(expected, selection.Provider);
    }
}
