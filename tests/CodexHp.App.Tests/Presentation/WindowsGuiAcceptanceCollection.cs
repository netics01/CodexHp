using Xunit;

namespace CodexHp.App.Tests.Presentation;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WindowsGuiAcceptanceCollection
{
    public const string Name = "Windows GUI acceptance";
}
