using CodexHp.Core;
using Xunit;

namespace CodexHp.Core.Tests;

public sealed class ProjectSmokeTests
{
    [Fact]
    public void Core_assembly_is_available_to_tests()
    {
        Assert.Equal("CodexHp.Core", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
