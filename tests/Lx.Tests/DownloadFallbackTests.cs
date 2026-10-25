using Lx.Probe.Model;
using Lx.Probe.Resolve;

namespace Lx.Tests;

public class DownloadFallbackTests
{
    [Fact]
    public void Build_KeepsOfficialUrlAsFallback()
    {
        var version = new VersionJson
        {
            Libraries = new List<Library>
            {
                new()
                {
                    Name = "net.fabricmc:intermediary:23w46a",
                    Downloads = new LibraryDownloads
                    {
                        Artifact = new LibraryArtifact
                        {
                            Path = "net/fabricmc/intermediary/23w46a/intermediary-23w46a.jar",
                            Url = "https://maven.fabricmc.net/net/fabricmc/intermediary/23w46a/intermediary-23w46a.jar",
                            Sha1 = new string('a', 40),
                            Size = 123,
                        },
                    },
                },
            },
        };

        var source = new DownloadSourceSettings(DownloadSourceSettings.Bmclapi, null);
        List<DownloadItem> items = DownloadManifest.Build(
            version,
            "23w46a",
            new Dictionary<string, bool>(),
            url => DownloadUrlRewriter.Rewrite(url, source));

        DownloadItem item = Assert.Single(items);
        Assert.StartsWith("https://bmclapi2.bangbang93.com/maven/", item.Url);
        Assert.Equal(
            "https://maven.fabricmc.net/net/fabricmc/intermediary/23w46a/intermediary-23w46a.jar",
            item.FallbackUrl);
    }
}
