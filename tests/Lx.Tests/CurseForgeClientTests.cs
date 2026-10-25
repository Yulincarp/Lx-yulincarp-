using Lx.Probe.Modpacks;

namespace Lx.Tests;

public class CurseForgeClientTests
{
    [Theory]
    [InlineData("forge", 1)]
    [InlineData("fabric", 4)]
    [InlineData("quilt", 5)]
    [InlineData("neoforge", 6)]
    [InlineData("vanilla", 0)]
    public void LoaderType_MapsKnownLoaders(string loader, int expected)
    {
        Assert.Equal(expected, CurseForgeClient.LoaderType(loader));
    }
}
