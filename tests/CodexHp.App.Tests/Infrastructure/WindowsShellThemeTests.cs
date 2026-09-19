using CodexHp.App.Infrastructure;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class WindowsShellThemeTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Uses_the_system_setting_even_when_the_app_theme_is_different(int systemValue, int appValue)
    {
        var reads = new List<string>();
        var theme = WindowsShellTheme.Read(name =>
        {
            reads.Add(name);
            return name == "SystemUsesLightTheme" ? systemValue : appValue;
        });
        Assert.Equal(systemValue == 1 ? TrayIconTheme.Light : TrayIconTheme.Dark, theme);
        Assert.Equal(["SystemUsesLightTheme"], reads);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData(2)]
    public void Missing_or_invalid_values_leave_the_choice_to_the_existing_theme(object? value) =>
        Assert.Null(WindowsShellTheme.Read(_ => value));

    [Fact]
    public void Unreadable_registry_does_not_break_the_tray_icon()
    {
        Assert.Null(WindowsShellTheme.Read(_ => throw new UnauthorizedAccessException()));
        Assert.Null(WindowsShellTheme.Read(_ => throw new System.Security.SecurityException()));
        Assert.Null(WindowsShellTheme.Read(_ => throw new System.IO.IOException()));
    }
}
