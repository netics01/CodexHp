namespace CodexHp.App.Infrastructure;

public sealed record CodexCredentials(string AccessToken, string? AccountId);

public enum CodexCredentialFailure
{
    MissingFile,
    InvalidFile,
    MissingAccessToken,
    UnreadableFile,
}

public sealed class CodexCredentialException : Exception
{
    public CodexCredentialException(CodexCredentialFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        this.Failure = failure;
    }

    public CodexCredentialFailure Failure { get; }
}
