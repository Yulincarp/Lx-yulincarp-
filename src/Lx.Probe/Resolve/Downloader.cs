using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

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

                DownloadFailure? failure = await TryDownloadAsync(item, item.Url, dest, cancellationToken).ConfigureAwait(false);

                // 镜像失败（403/404/超时）就回退到官方源再试一次，成功就不算失败。
                if (failure is not null &&
                    item.FallbackUrl is { Length: > 0 } fallback &&
                    !string.Equals(fallback, item.Url, StringComparison.OrdinalIgnoreCase) &&
                    !cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine($"[回退] {item.RelativePath} → 官方源");
                    DownloadFailure? fallbackFailure = await TryDownloadAsync(item, fallback, dest, cancellationToken).ConfigureAwait(false);
                    if (fallbackFailure is null)
                    {
                        Interlocked.Increment(ref ok);
                        return;
                    }

                    failure = new DownloadFailure(
                        fallbackFailure.Url,
                        fallbackFailure.RelativePath,
                        fallbackFailure.StatusCode,
                        fallbackFailure.Reason + "（镜像和官方源都失败）");
                }

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

    // 支持 Range 断点续传：.part + .part.meta，meta 里记录 url/sha1/size。
    // 最多试 3 次；403/429 退避重试；其他 4xx 直接放弃。
    private static async Task<DownloadFailure?> TryDownloadAsync(
        DownloadItem item,
        string url,
        string dest,
        CancellationToken cancellationToken)
    {
        string temp = dest + ".part";
        string meta = temp + ".meta";
        DownloadFailure? last = null;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                long resume = ReadResumeLength(temp, meta, item, url);
                if (resume < 0)
                {
                    TryDelete(temp);
                    TryDelete(meta);
                    resume = 0;
                }

                if (resume > 0 && item.Size > 0 && resume >= item.Size)
                {
                    if (item.Sha1.Length > 0 &&
                        string.Equals(Hasher.Sha1Of(temp), item.Sha1, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(temp, dest, overwrite: true);
                        TryDelete(meta);
                        return null;
                    }

                    TryDelete(temp);
                    TryDelete(meta);
                    resume = 0;
                }

                TryWriteMeta(meta, item, url);

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (resume > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(resume, null);
                }

                using HttpResponseMessage response = await Http
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    TryDelete(temp);
                    TryDelete(meta);
                    throw new HttpRequestException("HTTP 416，重新下载");
                }

                if (!response.IsSuccessStatusCode)
                {
                    int status = (int)response.StatusCode;
                    last = new DownloadFailure(url, item.RelativePath, status, $"HTTP {status}");

                    // 403/429 多半是镜像限流，值得退避重试；其他 4xx 直接放弃。
                    if (status >= 400 && status < 500 && status != 403 && status != 429)
                    {
                        return last;
                    }

                    throw new HttpRequestException($"HTTP {status}");
                }

                bool append = resume > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                if (append)
                {
                    long? from = response.Content.Headers.ContentRange?.From;
                    if (from != resume)
                    {
                        TryDelete(temp);
                        TryDelete(meta);
                        throw new HttpRequestException("Range 起点不匹配，重新下载");
                    }
                }
                else if (resume > 0)
                {
                    // 服务端忽略了 Range，只能从头写
                    resume = 0;
                }

                await using (Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (FileStream output = new(
                    temp,
                    append ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    await body.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                string expectedSha1 = item.Sha1;
                if (expectedSha1.Length == 0)
                {
                    expectedSha1 = await TryFetchSha1Async(url, cancellationToken).ConfigureAwait(false);
                }

                if (expectedSha1.Length == 0)
                {
                    Console.WriteLine($"[警告] 没有 sha1，跳过校验：{url}");
                }
                else
                {
                    string actualSha1 = Hasher.Sha1Of(temp);
                    if (!string.Equals(actualSha1, expectedSha1, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(temp);
                        TryDelete(meta);
                        return new DownloadFailure(
                            url,
                            item.RelativePath,
                            null,
                            $"sha1 不符（期望 {Short(expectedSha1)}，实际 {Short(actualSha1)}）");
                    }
                }

                File.Move(temp, dest, overwrite: true);
                TryDelete(meta);
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                last = new DownloadFailure(
                    url,
                    item.RelativePath,
                    last?.StatusCode,
                    $"{ex.GetType().Name}: {ex.Message}");

                if (attempt == 3)
                {
                    return last;
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken).ConfigureAwait(false);
            }
        }

        return last;
    }

    private sealed record PartMeta(string Url, string Sha1, long Size);

    private static long ReadResumeLength(string temp, string metaPath, DownloadItem item, string url)
    {
        if (!File.Exists(temp))
        {
            TryDelete(metaPath);
            return 0;
        }

        try
        {
            PartMeta? meta = JsonSerializer.Deserialize<PartMeta>(File.ReadAllText(metaPath));
            if (meta is null ||
                !string.Equals(meta.Url, url, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(meta.Sha1, item.Sha1, StringComparison.OrdinalIgnoreCase) ||
                (item.Size > 0 && meta.Size > 0 && meta.Size != item.Size))
            {
                return -1;
            }

            return new FileInfo(temp).Length;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    private static void TryWriteMeta(string metaPath, DownloadItem item, string url)
    {
        try
        {
            File.WriteAllText(metaPath, JsonSerializer.Serialize(new PartMeta(url, item.Sha1, item.Size)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // meta 写不了就下次从头下
        }
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
