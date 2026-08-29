using CodexHp.App.Infrastructure;

namespace CodexHp.App.Application;

public interface IOpenAiServiceStatusClient
{
    Task<OpenAiServiceStatusSnapshot> FetchAsync(CancellationToken cancellationToken = default);
}
