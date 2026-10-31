namespace Lx.Tests;

using Lx.Probe.Model;

public class ThemeChoiceTests
{
    [Fact]
    public void Normalize_DefaultsToLight_AndAcceptsKnownValues()
    {
        Assert.Equal("light", ThemeChoice.Normalize(null));
        Assert.Equal("light", ThemeChoice.Normalize(""));
        Assert.Equal("light", ThemeChoice.Normalize("nope"));
        Assert.Equal("dark", ThemeChoice.Normalize(" DARK "));
        Assert.Equal("system", ThemeChoice.Normalize("System"));
    }

    [Theory]
    [InlineData("light", true, "light")]
    [InlineData("light", false, "light")]
    [InlineData("dark", true, "dark")]
    [InlineData("dark", false, "dark")]
    [InlineData("system", true, "dark")]
    [InlineData("system", false, "light")]
    [InlineData(null, true, "light")]
    public void Resolve_SystemMode_FollowsOs(string? mode, bool systemDark, string expected)
    {
        Assert.Equal(expected, ThemeChoice.Resolve(mode, systemDark));
    }

    [Fact]
    public void Describe_IsHumanReadable()
    {
        Assert.Equal("浅色", ThemeChoice.Describe("light"));
        Assert.Equal("深色", ThemeChoice.Describe("dark"));
        Assert.Equal("跟随系统", ThemeChoice.Describe("system"));
    }
}
