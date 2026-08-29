using CodexHp.App.Infrastructure;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class StartupRegistrationTests
{
    [Fact]
    public void Constructor_does_not_modify_registry()
    {
        var registry = new FakeRegistryValueStore();

        _ = new StartupRegistration(registry, @"D:\Apps\CodexHp.exe");

        Assert.Empty(registry.Writes);
    }

    [Fact]
    public void SetEnabled_writes_quoted_current_executable_path()
    {
        var registry = new FakeRegistryValueStore();
        var registration = new StartupRegistration(registry, @"D:\Apps Folder\CodexHp.exe");

        registration.SetEnabled(true);

        Assert.Equal("\"D:\\Apps Folder\\CodexHp.exe\"", registry.Values[StartupRegistration.ValueName]);
        Assert.True(registration.IsEnabled());
    }

    [Fact]
    public void SetEnabled_false_removes_only_codex_hp_bar_value()
    {
        var registry = new FakeRegistryValueStore();
        registry.Values[StartupRegistration.ValueName] = "old";
        registry.Values["AnotherApp"] = "keep";
        var registration = new StartupRegistration(registry, @"D:\Apps\CodexHp.exe");

        registration.SetEnabled(false);

        Assert.False(registry.Values.ContainsKey(StartupRegistration.ValueName));
        Assert.Equal("keep", registry.Values["AnotherApp"]);
    }

    [Fact]
    public void SetEnabled_rejects_a_new_startup_entry_from_the_downloads_directory()
    {
        var registry = new FakeRegistryValueStore();
        var downloadsExecutable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "CodexHp.exe");
        var registration = new StartupRegistration(registry, downloadsExecutable);

        var exception = Assert.Throws<InvalidOperationException>(() => registration.SetEnabled(true));

        Assert.Contains("stable location", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(registry.Values.ContainsKey(StartupRegistration.ValueName));
    }

    internal sealed class FakeRegistryValueStore : IRegistryValueStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Writes { get; } = [];

        public string? Read(string name) => this.Values.GetValueOrDefault(name);

        public void Write(string name, string value)
        {
            this.Writes.Add($"write:{name}");
            this.Values[name] = value;
        }

        public void Delete(string name)
        {
            this.Writes.Add($"delete:{name}");
            this.Values.Remove(name);
        }
    }
}
