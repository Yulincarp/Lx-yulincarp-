using Lx.Probe.Resolve;

namespace Lx.Tests;

public class LoaderTests
{
    [Fact]
    public void MavenPath_BuildsGroupArtifactVersionLayout()
    {
        Assert.Equal(
            "org/ow2/asm/asm/9.10.1/asm-9.10.1.jar",
            LibraryResolver.MavenPath("org.ow2.asm:asm:9.10.1"));
    }

    [Fact]
    public void MavenPath_SupportsClassifier()
    {
        Assert.Equal(
            "net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1-sources.jar",
            LibraryResolver.MavenPath("net.fabricmc:fabric-loader:0.20.1:sources"));
    }

    [Fact]
    public void MavenPath_RejectsInvalidCoordinate()
    {
        Assert.Equal(string.Empty, LibraryResolver.MavenPath("not-a-coordinate"));
    }

    [Fact]
    public void Merge_OverridesMainClassAndConcatenates()
    {
        string parent = """
            {"id":"1.20.4","mainClass":"net.minecraft.client.main.Main",
             "libraries":[{"name":"a:b:1"}],
             "arguments":{"jvm":["-cp"],"game":["--username"]}}
            """;
        string child = """
            {"id":"fabric-loader-0.20.1-1.20.4","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient",
             "libraries":[{"name":"c:d:2"}],
             "arguments":{"jvm":["-DFabricMcEmu= net.minecraft.client.main.Main "],"game":[]}}
            """;

        string merged = VersionMerger.Merge(parent, child);

        Assert.Contains("fabric-loader-0.20.1-1.20.4", merged);
        Assert.Contains("\"inheritsFrom\":\"1.20.4\"", merged);
        Assert.Contains("KnotClient", merged);
        Assert.DoesNotContain("net.minecraft.client.main.Main\"", merged);
        Assert.Contains("a:b:1", merged);
        Assert.Contains("c:d:2", merged);
        Assert.Contains("-DFabricMcEmu", merged);
    }
}
