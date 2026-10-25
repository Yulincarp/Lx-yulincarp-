namespace Lx.Probe.Resolve;

// 一次下载失败的完整信息：UI 可以把它拼成"复制失败清单"。
public sealed record DownloadFailure(string Url, string RelativePath, int? StatusCode, string Reason);

// M2 Step 5：并发下载 + 失败重试 + 进度。仍然是"sha1 对得上就跳过"。
public static class Downloader
{
    // HttpClient 只建一个：它内部管连接池，每个文件 new 一个会耗尽端口。
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public static async Task<int> DownloadAllAsync(
        IReadOnlyList<DownloadItem> items,
        string installRoot,
        int concurrency = 8,
        Action<int, int>? onProgress = null,
        Action<DownloadFailure>? onFailure = null,
        CancellationToken cancellationToken = default)
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
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string dest = Path.Combine(installRoot, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(dest) &&
                    string.Equals(Hasher.Sha1Of(dest), item.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref skipped);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                DownloadFailure? failure = await TryDownloadAsync(item, dest, cancellationToken).ConfigureAwait(false);
                if (failure is null)
                {
                    Interlocked.Increment(ref ok);
                }
                else
                {
                    Interlocked.Increment(ref failed);
                    Console.WriteLine($"[失败] {failure.RelativePath}：{failure.Reason}");
                    onFailure?.Invoke(failure);
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

                if (onProgress is not null && (n % 10 == 0 || n == total))
                {
                    onProgress(n, total);
                }
            }
        }

        Task[] tasks = items.Select(ProcessOneAsync).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        Console.WriteLine($"成功 {ok} / 跳过 {skipped} / 失败 {failed}");
        return failed;
    }

    // 很多 maven 仓库旁边放一个 .sha1 文件，Forge 的库就是靠这个补校验值。
    private static async Task<string> TryFetchSha1Async(string url, CancellationToken cancellationToken)
    {
        try
        {
            string text = (await Http.GetStringAsync(url + ".sha1", cancellationToken).ConfigureAwait(false)).Trim();
            if (text.Length >= 40)
            {
                string candidate = text[..40];
                return candidate.All(Uri.IsHexDigit) ? candidate : string.Empty;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // 没有旁文件就放弃
        }

        return string.Empty;
    }

    // 最多试 3 次；4xx 除了 429 都不重试（重试也不会变好）。
    private static async Task<DownloadFailure?> TryDownloadAsync(
        DownloadItem item,
        string dest,
        CancellationToken cancellationToken)
    {
        string temp = dest + ".part";
        DownloadFailure? last = null;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using HttpResponseMessage response = await Http
                    .GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    int status = (int)response.StatusCode;
                    last = new DownloadFailure(item.Url, item.RelativePath, status, $"HTTP {status}");
                    if (status is >= 400 and < 500 and not 429)
                    {
                        return last;
                    }

                    throw new HttpRequestException($"HTTP {status}");
                }

                await using (Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (FileStream output = File.Create(temp))
                {
                    await body.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                string expectedSha1 = item.Sha1;
                if (expectedSha1.Length == 0)
                {
                    expectedSha1 = await TryFetchSha1Async(item.Url, cancellationToken).ConfigureAwait(false);
                }

                if (expectedSha1.Length == 0)
                {
                    Console.WriteLine($"[警告] 没有 sha1，跳过校验：{item.Url}");
                }
                else
                {
                    string actualSha1 = Hasher.Sha1Of(temp);
                    if (!string.Equals(actualSha1, expectedSha1, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(temp);
                        return new DownloadFailure(
                            item.Url,
                            item.RelativePath,
                            null,
                            $"sha1 不符（期望 {Short(expectedSha1)}，实际 {Short(actualSha1)}）");
                    }
                }

                File.Move(temp, dest, overwrite: true);
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                last = new DownloadFailure(
                    item.Url,
                    item.RelativePath,
                    last?.StatusCode,
                    $"{ex.GetType().Name}: {ex.Message}");

                if (attempt == 3)
                {
                    return last;
                }

                // 1s → 2s → 4s
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken).ConfigureAwait(false);
            }
        }

        return last;
    }

    private static string Short(string hash) => hash.Length <= 10 ? hash : hash[..10] + "...";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删不掉就留着，下次启动会覆盖
        }
    }
}
