using CodexHp.App.Infrastructure;
using Xunit;

namespace CodexHp.App.Tests.Infrastructure;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void Only_first_guard_owns_a_named_mutex_until_disposed()
    {
        var name = $"Local\\CodexHp.Tests.{Guid.NewGuid():N}";
        using var first = SingleInstanceGuard.TryAcquire(name);
        using var second = SingleInstanceGuard.TryAcquire(name);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void Disposed_owner_allows_a_later_instance()
    {
        var name = $"Local\\CodexHp.Tests.{Guid.NewGuid():N}";
        SingleInstanceGuard.TryAcquire(name)!.Dispose();

        using var later = SingleInstanceGuard.TryAcquire(name);

        Assert.NotNull(later);
    }
}
