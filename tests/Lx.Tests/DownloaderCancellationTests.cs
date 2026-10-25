using Lx.Probe.Resolve;

namespace Lx.Tests;

public class DownloaderCancellationTests
{
    [Fact]
    public async Task CanceledToken_StopsBeforeNetworkAndLeavesNoFile()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "dl-cancel", Guid.NewGuid().ToString("N"));
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var item = new DownloadItem("http://127.0.0.1:1/x.jar", "libraries/x.jar", new string('0', 40), 1);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Downloader.DownloadAllAsync(new[] { item }, dir, 1, cancellationToken: cts.Token));

            Assert.False(File.Exists(Path.Combine(dir, "libraries", "x.jar")));
        }
        finally
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 清理失败不影响测试结果
            }
        }
    }
}
