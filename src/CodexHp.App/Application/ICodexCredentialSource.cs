using CodexHp.App.Infrastructure;

namespace CodexHp.App.Application;

public interface ICodexCredentialSource
{
    CodexCredentials Load();
}
