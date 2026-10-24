using Lx.Probe.Resolve;

namespace Lx.Tests;

public class InstallCheckTests
{
    [Fact]
    public void FindMissing_ReturnsOnlyAbsentFiles()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "lx-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "libraries"));
        File.WriteAllText(Path.Combine(root, "libraries", "a.jar"), "x");

        var items = new List<DownloadItem>
        {
            new("http://x/a.jar", "libraries/a.jar", "sa", 1),
            new("http://x/b.jar", "libraries/b.jar", "sb", 2),
        };

        List<DownloadItem> missing = InstallCheck.FindMissing(items, root);

        Assert.Single(missing);
        Assert.Equal("http://x/b.jar", missing[0].Url);

        Directory.Delete(root, recursive: true);
    }
}
