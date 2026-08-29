using System.IO;
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

        var fullExecutablePath = Path.GetFullPath(executablePath);
        this.command = $"\"{fullExecutablePath}\"";
        this.CanEnable = !IsUnderDirectory(fullExecutablePath, GetDownloadsDirectory())
            && !IsUnderDirectory(fullExecutablePath, Path.GetTempPath());
    }

    public bool CanEnable { get; }

    public bool IsEnabled() => string.Equals(
        this.registry.Read(ValueName),
        this.command,
        StringComparison.OrdinalIgnoreCase);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (!this.CanEnable)
            {
                throw new InvalidOperationException(
                    "Install or move CodexHp to a stable location before enabling Windows startup.");
            }

            this.registry.Write(ValueName, this.command);
        }
        else
        {
            this.registry.Delete(ValueName);
        }
    }

    private static string GetDownloadsDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads");

    private static bool IsUnderDirectory(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return fullPath.StartsWith(
            fullDirectory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
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
