using Lx.Probe.Model;
using Lx.Probe.Resolve;

namespace Lx.Tests;

public class ManifestServiceTests
{
    private static VersionManifest Sample() => new()
    {
        Latest = new ManifestLatest { Release = "1.21" },
        Versions = new List<ManifestVersion>
        {
            new() { Id = "1.20.4", Type = "release" },
            new() { Id = "1.21", Type = "release" },
        },
    };

    [Fact]
    public void Find_ReturnsMatchingVersion()
    {
        Assert.Equal("1.21", ManifestService.Find(Sample(), "1.21").Id);
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        Assert.Equal("1.20.4", ManifestService.Find(Sample(), "1.20.4").Id);
    }

    [Fact]
    public void Find_UnknownVersion_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => ManifestService.Find(Sample(), "9.9.9"));
        Assert.Contains("9.9.9", ex.Message);
    }
}
