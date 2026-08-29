using CodexHp.App.Infrastructure;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class CodexCredentialSourceTests
{
    [Fact]
    public void Load_prefers_codex_home_and_reads_snake_case_fields()
    {
        using var directories = new TemporaryDirectories();
        var codexHome = directories.Create();
        var profile = directories.Create();
        WriteAuth(codexHome, "access-from-home", "account-home", snakeCase: true);
        WriteAuth(Path.Combine(profile, ".codex"), "access-from-profile", "account-profile", snakeCase: true);
        var source = new CodexCredentialSource(
            new Dictionary<string, string> { ["CODEX_HOME"] = codexHome },
            profile);

        var credentials = source.Load();

        Assert.Equal("access-from-home", credentials.AccessToken);
        Assert.Equal("account-home", credentials.AccountId);
    }

    [Fact]
    public void Load_falls_back_to_user_profile_and_reads_camel_case_fields()
    {
        using var directories = new TemporaryDirectories();
        var profile = directories.Create();
        WriteAuth(Path.Combine(profile, ".codex"), "access-token", "account-123", snakeCase: false);
        var source = new CodexCredentialSource(new Dictionary<string, string>(), profile);

        var credentials = source.Load();

        Assert.Equal("access-token", credentials.AccessToken);
        Assert.Equal("account-123", credentials.AccountId);
    }

    [Fact]
    public void Load_allows_missing_account_id()
    {
        using var directories = new TemporaryDirectories();
        var profile = directories.Create();
        var authDirectory = Path.Combine(profile, ".codex");
        Directory.CreateDirectory(authDirectory);
        File.WriteAllText(Path.Combine(authDirectory, "auth.json"), """
        {
          "tokens": {
            "access_token": "access-token"
          }
        }
        """);

        var credentials = new CodexCredentialSource(new Dictionary<string, string>(), profile).Load();

        Assert.Null(credentials.AccountId);
    }

    [Fact]
    public void Load_classifies_missing_file_without_exposing_credentials()
    {
        using var directories = new TemporaryDirectories();
        var profile = directories.Create();

        var exception = Assert.Throws<CodexCredentialException>(
            () => new CodexCredentialSource(new Dictionary<string, string>(), profile).Load());

        Assert.Equal(CodexCredentialFailure.MissingFile, exception.Failure);
        Assert.DoesNotContain("Bearer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_classifies_invalid_json()
    {
        using var directories = new TemporaryDirectories();
        var profile = directories.Create();
        var authDirectory = Path.Combine(profile, ".codex");
        Directory.CreateDirectory(authDirectory);
        File.WriteAllText(Path.Combine(authDirectory, "auth.json"), "{ invalid");

        var exception = Assert.Throws<CodexCredentialException>(
            () => new CodexCredentialSource(new Dictionary<string, string>(), profile).Load());

        Assert.Equal(CodexCredentialFailure.InvalidFile, exception.Failure);
    }

    [Fact]
    public void Load_classifies_missing_access_token_without_exposing_account_id()
    {
        using var directories = new TemporaryDirectories();
        var profile = directories.Create();
        var authDirectory = Path.Combine(profile, ".codex");
        Directory.CreateDirectory(authDirectory);
        File.WriteAllText(Path.Combine(authDirectory, "auth.json"), """
        {
          "tokens": {
            "account_id": "secret-account-id"
          }
        }
        """);

        var exception = Assert.Throws<CodexCredentialException>(
            () => new CodexCredentialSource(new Dictionary<string, string>(), profile).Load());

        Assert.Equal(CodexCredentialFailure.MissingAccessToken, exception.Failure);
        Assert.DoesNotContain("secret-account-id", exception.Message, StringComparison.Ordinal);
    }

    private static void WriteAuth(string codexHome, string accessToken, string accountId, bool snakeCase)
    {
        Directory.CreateDirectory(codexHome);
        var accessKey = snakeCase ? "access_token" : "accessToken";
        var accountKey = snakeCase ? "account_id" : "accountId";
        File.WriteAllText(Path.Combine(codexHome, "auth.json"), $$"""
        {
          "tokens": {
            "{{accessKey}}": "{{accessToken}}",
            "{{accountKey}}": "{{accountId}}"
          }
        }
        """);
    }

    private sealed class TemporaryDirectories : IDisposable
    {
        private readonly List<string> paths = [];

        public string Create()
        {
            var path = Path.Combine(Path.GetTempPath(), "CodexHp.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            this.paths.Add(path);
            return path;
        }

        public void Dispose()
        {
            foreach (var path in this.paths)
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }
    }
}
