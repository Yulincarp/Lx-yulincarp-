using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lx.Probe.Resolve;

namespace Lx.Probe.Auth;

// authlib-injector 自动获取：官方源优先/镜像回退，带 SHA-256 校验。
// 已存在且大小正常就直接复用；缺失或 0 字节才下载。
public static class AuthlibInjector
{
    private const string OfficialMetadata = "https://authlib-injector.yushi.moe/artifact/latest.json";
    private const string MirrorMetadata = "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static async Task<string> EnsureAsync(
        string installRoot,
        Action<string> log,
        DownloadSourceSettings? source = null,
        CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(installRoot, "authlib-injector.jar");
        if (File.Exists(path) && new FileInfo(path).Length > 100_000 && LooksLikeJar(path))
        {
            return path;
        }

        if (File.Exists(path))
        {
            TryDelete(path);
        }

        bool preferMirror = source?.Source is DownloadSourceSettings.Bmclapi or DownloadSourceSettings.Custom;
        string[] metadataUrls = preferMirror
            ? new[] { MirrorMetadata, OfficialMetadata }
            : new[] { OfficialMetadata, MirrorMetadata };

        Exception? last = null;
        foreach (string metadataUrl in metadataUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string metadataJson = await Http.GetStringAsync(metadataUrl, cancellationToken).ConfigureAwait(false);
                ArtifactMetadata? metadata = JsonSerializer.Deserialize<ArtifactMetadata>(metadataJson, Json);
                if (metadata?.DownloadUrl is not { Length: > 0 } downloadUrl)
                {
                    throw new InvalidOperationException("metadata 里没有 download_url");
                }

                log(string.IsNullOrEmpty(metadata.Version)
                    ? "下载 authlib-injector…"
                    : $"下载 authlib-injector {metadata.Version}…");
                string temp = path + ".part";
                using (HttpResponseMessage response = await Http
                    .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using FileStream output = File.Create(temp);
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                if (metadata.Checksums?.Sha256 is { Length: 64 } expected)
                {
                    byte[] bytes = await File.ReadAllBytesAsync(temp, cancellationToken).ConfigureAwait(false);
                    string actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(temp);
                        throw new InvalidOperationException(
                            $"sha256 不符（期望 {expected[..12]}…，实际 {actual[..12]}…）");
                    }
                }

                if (!LooksLikeJar(temp))
                {
                    TryDelete(temp);
                    throw new InvalidOperationException("下载到的文件不是 jar");
                }

                File.Move(temp, path, overwrite: true);
                File.WriteAllText(Path.Combine(installRoot, "authlib-injector.version"), metadata.Version ?? string.Empty);
                log($"authlib-injector 已下载：{path}");
                return path;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or JsonException)
            {
                last = ex;
                log($"[警告] authlib-injector 源失败：{ex.Message}");
            }
        }

        throw new InvalidOperationException(
            $"缺少 authlib-injector.jar，自动下载失败：{last?.Message ?? "未知错误"}。也可以手动放到 {path}");
    }

    private static bool LooksLikeJar(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return stream.ReadByte() == 'P' && stream.ReadByte() == 'K';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

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
            // 删不掉就等下面覆盖
        }
    }

    private sealed class ArtifactMetadata
    {
        public string? Version { get; set; }

        [JsonPropertyName("download_url")]
        public string? DownloadUrl { get; set; }

        public Checksums? Checksums { get; set; }
    }

    private sealed class Checksums
    {
        public string? Sha256 { get; set; }
    }
}
