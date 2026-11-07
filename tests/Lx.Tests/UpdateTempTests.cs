using Lx.Probe.Updates;
using Xunit;

namespace Lx.Tests;

public sealed class UpdateTempTests
{
    private static string TempDir(string label)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "updtmp-" + label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void CreateTempPath_ReusesPlainPartName_AndClearsStale()
    {
        string dir = TempDir("plain");
        try
        {
            string target = Path.Combine(dir, "Lx-1.5.0.zip");
            string stale = target + ".part";
            File.WriteAllText(stale, "half-downloaded");

            string temp = UpdateService.CreateTempPath(target);

            Assert.Equal(stale, temp);
            Assert.False(File.Exists(stale), "残留的 .part 应该被清掉");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CreateTempPath_FallsBack_WhenPartIsLocked()
    {
        string dir = TempDir("locked");
        try
        {
            string target = Path.Combine(dir, "Lx-1.5.0.zip");
            string locked = target + ".part";

            using (var hold = new FileStream(locked, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                string temp = UpdateService.CreateTempPath(target);

                Assert.NotEqual(locked, temp);
                Assert.StartsWith(target, temp);
                Assert.EndsWith(".part", temp);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task FailedDownload_LeavesNoPartBehind()
    {
        string dir = TempDir("fail");
        try
        {
            string missing = Path.Combine(dir, "nope.zip");
            var info = new UpdateInfo("9.9.9", new Uri(missing).AbsoluteUri, "deadbeef", null);

            await Assert.ThrowsAnyAsync<Exception>(
                () => UpdateService.DownloadAsync(info, dir));

            string updates = Path.Combine(dir, "updates");
            if (Directory.Exists(updates))
            {
                Assert.Empty(Directory.GetFiles(updates, "*.part"));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
