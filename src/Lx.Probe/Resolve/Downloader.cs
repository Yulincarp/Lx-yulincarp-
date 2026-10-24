using System.Security.Cryptography;

namespace Lx.Probe.Resolve;

// M2 Step 5：并发下载 + 失败重试 + 进度。仍然是"sha1 对得上就跳过"。
public static class Downloader
{
    // HttpClient 只建一个：它内部管连接池，每个文件 new 一个会耗尽端口。
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public static async Task<int> DownloadAllAsync(
        IReadOnlyList<DownloadItem> items,
        string installRoot,
        int concurrency = 8)
    {
        int ok = 0;
        int skipped = 0;
        int failed = 0;
        int done = 0;
        int total = items.Count;

        // 信号量 = 同时最多 8 个请求，不是一把全发出去。
        using var gate = new SemaphoreSlim(concurrency);

        async Task ProcessOneAsync(DownloadItem item)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                string dest = Path.Combine(installRoot, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(dest) &&
                    string.Equals(Sha1Of(dest), item.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref skipped);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                bool success = await TryDownloadAsync(item, dest).ConfigureAwait(false);
                if (success)
                {
                    Interlocked.Increment(ref ok);
                }
                else
                {
                    Interlocked.Increment(ref failed);
                }
            }
            finally
            {
                gate.Release();
                int n = Interlocked.Increment(ref done);
                if (n % 50 == 0 || n == total)
                {
                    Console.WriteLine($"[{n}/{total}]");
                }
            }
        }

        Task[] tasks = items.Select(ProcessOneAsync).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        Console.WriteLine($"成功 {ok} / 跳过 {skipped} / 失败 {failed}");
        return failed;
    }

    // 最多试 3 次；4xx 除了 429 都不重试（重试也不会变好）。
    private static async Task<bool> TryDownloadAsync(DownloadItem item, string dest)
    {
        string temp = dest + ".part";

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using HttpResponseMessage response = await Http
                    .GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    int status = (int)response.StatusCode;
                    if (status is >= 400 and < 500 and not 429)
                    {
                        Console.WriteLine($"[失败] {status} {item.Url}");
                        return false;
                    }

                    throw new HttpRequestException($"HTTP {status}");
                }

                await using (Stream body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                await using (FileStream output = File.Create(temp))
                {
                    await body.CopyToAsync(output).ConfigureAwait(false);
                }

                if (!string.Equals(Sha1Of(temp), item.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[sha1 不符] {item.Url}");
                    return false;
                }

                File.Move(temp, dest, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                if (attempt == 3)
                {
                    Console.WriteLine($"[失败] {ex.GetType().Name} {item.Url}");
                    return false;
                }

                // 1s → 2s → 4s
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1))).ConfigureAwait(false);
            }
        }

        return false;
    }

    private static string Sha1Of(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA1.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}