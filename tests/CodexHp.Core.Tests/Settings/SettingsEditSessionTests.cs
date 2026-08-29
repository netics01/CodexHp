using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.Core.Tests.Settings;

public sealed class SettingsEditSessionTests
{
    [Fact]
    public void Cancel_restores_the_settings_that_were_active_when_editing_started()
    {
        var original = AppSettings.Default;
        var session = new SettingsEditSession(original);
        session.Preview(original with
        {
            Colors = original.Colors with { ManaBar = ColorValue.Parse("#010203") },
            Appearance = original.Appearance with { OverlayWidth = 420 },
            Location = new OverlayLocationSettings("DISPLAY2", 10, 20),
        });

        var restored = session.Cancel();

        Assert.Equal(original, restored);
        Assert.Equal(original, session.Working);
    }

    [Fact]
    public void Confirm_promotes_working_settings_to_the_new_baseline()
    {
        var session = new SettingsEditSession(AppSettings.Default);
        var confirmed = AppSettings.Default with
        {
            Appearance = AppSettings.Default.Appearance with { OverlayWidth = 420 },
        };
        session.Preview(confirmed);

        Assert.Equal(confirmed, session.Confirm());
        session.Preview(confirmed with { StartWithWindows = false });

        Assert.Equal(confirmed, session.Cancel());
    }
}
