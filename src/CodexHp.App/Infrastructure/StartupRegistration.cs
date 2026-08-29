using Microsoft.Win32;
using CodexHp.App.Application;

namespace CodexHp.App.Infrastructure;

public interface IRegistryValueStore
{
    string? Read(string name);

    void Write(string name, string value);

    void Delete(string name);
}

public sealed class StartupRegistration : IStartupRegistration
{
    public const string ValueName = "CodexHp";
    private readonly IRegistryValueStore registry;
    private readonly string command;

    public StartupRegistration(string executablePath)
        : this(new CurrentUserRunRegistryValueStore(), executablePath)
    {
    }

    public StartupRegistration(IRegistryValueStore registry, string executablePath)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path is required.", nameof(executablePath));
        }

        this.command = $"\"{executablePath}\"";
    }

    public bool IsEnabled() => string.Equals(
        this.registry.Read(ValueName),
        this.command,
        StringComparison.OrdinalIgnoreCase);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            this.registry.Write(ValueName, this.command);
        }
        else
        {
            this.registry.Delete(ValueName);
        }
    }

    private sealed class CurrentUserRunRegistryValueStore : IRegistryValueStore
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public string? Read(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(name) as string;
        }

        public void Write(string name, string value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(name, value, RegistryValueKind.String);
        }

        public void Delete(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
    }
}
