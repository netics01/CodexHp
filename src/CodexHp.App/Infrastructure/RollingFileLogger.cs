using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CodexHp.App.Application;

namespace CodexHp.App.Infrastructure;

public sealed partial class RollingFileLogger : IDiagnosticLogger
{
    private const long DefaultMaxFileBytes = 1024 * 1024;
    private const int DefaultMaxFiles = 3;
    private readonly long maxFileBytes;
    private readonly int maxFiles;
    private readonly Func<DateTimeOffset> clock;
    private readonly object sync = new();

    public RollingFileLogger(
        string? localAppData = null,
        long maxFileBytes = DefaultMaxFileBytes,
        int maxFiles = DefaultMaxFiles,
        Func<DateTimeOffset>? clock = null)
    {
        if (maxFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        }

        if (maxFiles <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFiles));
        }

        var root = string.IsNullOrWhiteSpace(localAppData)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppData;
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("Local application data path is not available.");
        }

        this.LogsDirectory = Path.Combine(root, "CodexHp", "Logs");
        this.maxFileBytes = maxFileBytes;
        this.maxFiles = maxFiles;
        this.clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string LogsDirectory { get; }

    public void Log(DiagnosticLevel level, string component, string message, Exception? exception = null)
    {
        try
        {
            var sanitizedComponent = Sanitize(NormalizeLine(component));
            var sanitizedMessage = Sanitize(NormalizeLine(message));
            var exceptionSummary = exception is null
                ? string.Empty
                : $" | {exception.GetType().Name}: {Sanitize(NormalizeLine(exception.Message))}";
            var line = $"{this.clock():O} [{level}] [{sanitizedComponent}] {sanitizedMessage}{exceptionSummary}{Environment.NewLine}";
            var bytes = Encoding.UTF8.GetByteCount(line);

            lock (this.sync)
            {
                Directory.CreateDirectory(this.LogsDirectory);
                var currentPath = this.LogPath(0);
                if (File.Exists(currentPath)
                    && new FileInfo(currentPath).Length > 0
                    && new FileInfo(currentPath).Length + bytes > this.maxFileBytes)
                {
                    this.Rotate();
                }

                File.AppendAllText(currentPath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string NormalizeLine(string? value) =>
        (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');

    private static string Sanitize(string value)
    {
        var withoutBearer = BearerPattern().Replace(value, "Bearer <redacted>");
        return NamedSecretPattern().Replace(withoutBearer, match => $"{match.Groups[1].Value}=<redacted>");
    }

    private void Rotate()
    {
        for (var index = this.maxFiles - 1; index >= 1; index--)
        {
            var destination = this.LogPath(index);
            var source = this.LogPath(index - 1);
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }
    }

    private string LogPath(int index) => index == 0
        ? Path.Combine(this.LogsDirectory, "CodexHp.log")
        : Path.Combine(this.LogsDirectory, $"CodexHp.{index}.log");

    [GeneratedRegex(@"(?i)\bBearer\s+[^\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(
        @"(?i)\b(access[_-]?token|refresh[_-]?token|account[_-]?id)\s*[:=]\s*[\""']?[^\""'\s,;}]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex NamedSecretPattern();
}
