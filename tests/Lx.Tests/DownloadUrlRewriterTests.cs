using Lx.Probe.Resolve;

namespace Lx.Tests;

public class DownloadUrlRewriterTests
{
    private static readonly DownloadSourceSettings Bmclapi = new(DownloadSourceSettings.Bmclapi, null);
    private static readonly DownloadSourceSettings Official = new(DownloadSourceSettings.Official, null);

    [Theory]
    [InlineData("https://piston-meta.mojang.com/v1/packages/abc/1.20.4.json", "https://bmclapi2.bangbang93.com/v1/packages/abc/1.20.4.json")]
    [InlineData("https://launchermeta.mojang.com/v1/products/java-runtime/x/all.json", "https://bmclapi2.bangbang93.com/v1/products/java-runtime/x/all.json")]
    [InlineData("https://resources.download.minecraft.net/ab/abcdef", "https://bmclapi2.bangbang93.com/assets/ab/abcdef")]
    [InlineData("https://libraries.minecraft.net/org/lwjgl/lwjgl/3.3.2/lwjgl-3.3.2.jar", "https://bmclapi2.bangbang93.com/maven/org/lwjgl/lwjgl/3.3.2/lwjgl-3.3.2.jar")]
    [InlineData("https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.16.0/fabric-loader-0.16.0.jar", "https://bmclapi2.bangbang93.com/maven/net/fabricmc/fabric-loader/0.16.0/fabric-loader-0.16.0.jar")]
    public void Bmclapi_RewritesKnownHosts(string input, string expected)
    {
        Assert.Equal(expected, DownloadUrlRewriter.Rewrite(input, Bmclapi));
    }

    [Fact]
    public void Official_LeavesUrlUntouched()
    {
        const string url = "https://piston-meta.mojang.com/v1/packages/abc/1.20.4.json";
        Assert.Equal(url, DownloadUrlRewriter.Rewrite(url, Official));
    }

    [Fact]
    public void UnknownHost_LeavesUrlUntouched()
    {
        const string url = "https://example.com/file.jar";
        Assert.Equal(url, DownloadUrlRewriter.Rewrite(url, Bmclapi));
    }

    [Fact]
    public void Custom_UsesProvidedBase()
    {
        var custom = new DownloadSourceSettings(DownloadSourceSettings.Custom, "https://mirror.example.com/");
        Assert.Equal(
            "https://mirror.example.com/assets/ab/abcdef",
            DownloadUrlRewriter.Rewrite("https://resources.download.minecraft.net/ab/abcdef", custom));
    }

    [Fact]
    public void Custom_InvalidBase_FallsBackToOfficial()
    {
        var custom = new DownloadSourceSettings(DownloadSourceSettings.Custom, "not-a-url");
        const string url = "https://libraries.minecraft.net/a.jar";
        Assert.Equal(url, DownloadUrlRewriter.Rewrite(url, custom));
    }

    [Fact]
    public void Describe_ReportsSource()
    {
        Assert.Contains("BMCLAPI", DownloadUrlRewriter.Describe(Bmclapi));
        Assert.Contains("官方", DownloadUrlRewriter.Describe(Official));
    }
}
