using CodexHp.App.Infrastructure;
using CodexHp.Core.Domain;

namespace CodexHp.App.Application;

public interface IOpenAiUsageClient
{
    Task<UsageSnapshot> FetchAsync(
        CodexCredentials credentials,
        CancellationToken cancellationToken = default);
}
