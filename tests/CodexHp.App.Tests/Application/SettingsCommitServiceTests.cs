using CodexHp.App.Application;
using CodexHp.Core.Settings;
using Xunit;

namespace CodexHp.App.Tests.Application;

public sealed class SettingsCommitServiceTests
{
    [Fact]
    public void Commit_rolls_back_startup_registration_when_settings_save_fails()
    {
        var startup = new FakeStartupRegistration(initiallyEnabled: true);
        var store = new FakeSettingsStore { SaveException = new IOException("save failed") };
        var service = new SettingsCommitService(store, startup);
        var desired = AppSettings.Default with { StartWithWindows = false };

        Assert.Throws<IOException>(() => service.Commit(desired));

        Assert.True(startup.IsEnabled());
        Assert.Equal([false, true], startup.SetCalls);
    }

    [Fact]
    public void Commit_does_not_save_when_startup_registration_fails()
    {
        var startup = new FakeStartupRegistration(initiallyEnabled: true)
        {
            SetException = new InvalidOperationException("registry failed"),
        };
        var store = new FakeSettingsStore();
        var service = new SettingsCommitService(store, startup);

        Assert.Throws<InvalidOperationException>(
            () => service.Commit(AppSettings.Default with { StartWithWindows = false }));

        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void Commit_applies_registration_and_saves_validated_settings()
    {
        var startup = new FakeStartupRegistration(initiallyEnabled: false);
        var store = new FakeSettingsStore();
        var service = new SettingsCommitService(store, startup);
        var desired = AppSettings.Default with
        {
            StartWithWindows = true,
            Appearance = AppSettings.Default.Appearance with { GraphBarWidth = 0 },
        };

        var committed = service.Commit(desired);

        Assert.True(startup.IsEnabled());
        Assert.Equal(AppSettings.Default.Appearance.GraphBarWidth, committed.Appearance.GraphBarWidth);
        Assert.Equal(committed, store.Saved);
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public Exception? SaveException { get; init; }

        public int SaveCount { get; private set; }

        public AppSettings? Saved { get; private set; }

        public AppSettings Load() => AppSettings.Default;

        public void Save(AppSettings settings)
        {
            this.SaveCount++;
            if (this.SaveException is not null)
            {
                throw this.SaveException;
            }

            this.Saved = settings;
        }
    }

    private sealed class FakeStartupRegistration(bool initiallyEnabled) : IStartupRegistration
    {
        private bool enabled = initiallyEnabled;

        public Exception? SetException { get; init; }

        public List<bool> SetCalls { get; } = [];

        public bool IsEnabled() => this.enabled;

        public void SetEnabled(bool enabled)
        {
            this.SetCalls.Add(enabled);
            if (this.SetException is not null)
            {
                throw this.SetException;
            }

            this.enabled = enabled;
        }
    }
}
