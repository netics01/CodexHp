using System.Collections;
using System.IO;
using System.Text.Json;
using CodexHp.App.Application;

namespace CodexHp.App.Infrastructure;

public sealed class CodexCredentialSource : ICodexCredentialSource
{
    private readonly IReadOnlyDictionary<string, string> environment;
    private readonly string userProfile;

    public CodexCredentialSource()
        : this(ReadEnvironment(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    public CodexCredentialSource(IReadOnlyDictionary<string, string> environment, string userProfile)
    {
        this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
        this.userProfile = string.IsNullOrWhiteSpace(userProfile)
            ? throw new ArgumentException("User profile path is required.", nameof(userProfile))
            : userProfile;
    }

    public CodexCredentials Load()
    {
        var authPath = this.GetAuthPath();
        if (!File.Exists(authPath))
        {
            throw new CodexCredentialException(
                CodexCredentialFailure.MissingFile,
                "Codex authentication cache is not available.");
        }

        string json;
        try
        {
            json = File.ReadAllText(authPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CodexCredentialException(
                CodexCredentialFailure.UnreadableFile,
                "Codex authentication cache could not be read.",
                ex);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("tokens", out var tokens)
                || tokens.ValueKind != JsonValueKind.Object)
            {
                throw new CodexCredentialException(
                    CodexCredentialFailure.InvalidFile,
                    "Codex authentication cache has an unsupported format.");
            }

            var accessToken = ReadString(tokens, "access_token", "accessToken");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new CodexCredentialException(
                    CodexCredentialFailure.MissingAccessToken,
                    "Codex access token is not available.");
            }

            return new CodexCredentials(
                accessToken,
                ReadString(tokens, "account_id", "accountId"));
        }
        catch (CodexCredentialException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new CodexCredentialException(
                CodexCredentialFailure.InvalidFile,
                "Codex authentication cache has invalid JSON.",
                ex);
        }
    }

    private string GetAuthPath()
    {
        if (this.environment.TryGetValue("CODEX_HOME", out var codexHome)
            && !string.IsNullOrWhiteSpace(codexHome))
        {
            return Path.Combine(codexHome, "auth.json");
        }

        return Path.Combine(this.userProfile, ".codex", "auth.json");
    }

    private static string? ReadString(JsonElement element, string snakeCaseKey, string camelCaseKey)
    {
        if (element.TryGetProperty(snakeCaseKey, out var snakeCase)
            && snakeCase.ValueKind == JsonValueKind.String)
        {
            return snakeCase.GetString();
        }

        if (element.TryGetProperty(camelCaseKey, out var camelCase)
            && camelCase.ValueKind == JsonValueKind.String)
        {
            return camelCase.GetString();
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> ReadEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry item in Environment.GetEnvironmentVariables())
        {
            if (item.Key is string key && item.Value is string value)
            {
                result[key] = value;
            }
        }

        return result;
    }
}
