using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lx.Probe.Updates;

public sealed record UpdateInfo(string Version, string Url, string Sha256, string? Notes);

public sealed record StagedUpdate(string Version, string ZipPath, string StagingDir);

// 自更新：检查 update.json -> 下载 zip -> 校验 SHA-256 -> 解压 -> 交给 PowerShell 等主进程退出后替换。
// 默认只对发布版开放；开发版（src\Lx.UI\bin）不走自更新。
public static class UpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static bool CanSelfUpdate =>
        !AppContext.BaseDirectory.Contains(Path.Combine("src", "Lx.UI", "bin"), StringComparison.OrdinalIgnoreCase);

    // update.json 地址：LX_UPDATE_URL 优先，其次数据目录下的 update.json（方便本地测试）。
    public static string? ResolveManifestUrl(string installRoot)
    {
        string? fromEnv = Environment.GetEnvironmentVariable("LX_UPDATE_URL");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        string local = Path.Combine(installRoot, "update.json");
        return File.Exists(local) ? local : null;
    }

    public static bool IsNewer(string candidate, string current) =>
        Version.TryParse(candidate, out Version? next) &&
        Version.TryParse(current, out Version? cur) &&
        next > cur;

    public static async Task<UpdateInfo?> CheckAsync(string manifestUrl, CancellationToken cancellationToken = default)
    {
        string json = await ReadTextAsync(manifestUrl, cancellationToken).ConfigureAwait(false);
        UpdateManifest? manifest = JsonSerializer.Deserialize<UpdateManifest>(json, Json);
        if (manifest?.Version is null || manifest.Url is null || manifest.Sha256 is null)
        {
            throw new InvalidOperationException("update.json 缺少 version/url/sha256");
        }

        if (!IsNewer(manifest.Version, AppInfo.Version))
        {
            return null;
        }

        string downloadUrl = manifest.Url;
        if (Uri.TryCreate(manifestUrl, UriKind.Absolute, out Uri? baseUri) &&
            Uri.TryCreate(baseUri, downloadUrl, out Uri? resolved))
        {
            downloadUrl = resolved.ToString();
        }

        return new UpdateInfo(manifest.Version, downloadUrl, manifest.Sha256, manifest.Notes);
    }

    public static async Task<StagedUpdate> DownloadAsync(
        UpdateInfo info,
        string installRoot,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string updatesDir = Path.Combine(installRoot, "updates");
        Directory.CreateDirectory(updatesDir);

        string zipPath = Path.Combine(updatesDir, $"Lx-{info.Version}.zip");
        bool zipValid = File.Exists(zipPath) &&
                        string.Equals(Sha256Of(zipPath), info.Sha256, StringComparison.OrdinalIgnoreCase);

        if (!zipValid)
        {
            string temp = zipPath + ".part";
            await DownloadToAsync(info.Url, temp, progress, cancellationToken).ConfigureAwait(false);

            string actual = Sha256Of(temp);
            if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(temp);
                throw new InvalidOperationException($"更新包 sha256 不符（期望 {info.Sha256[..12]}…，实际 {actual[..12]}…）");
            }

            File.Move(temp, zipPath, overwrite: true);
        }

        string staging = Path.Combine(updatesDir, "staging-" + info.Version);
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        Directory.CreateDirectory(staging);
        ExtractSafely(zipPath, staging);

        if (!File.Exists(Path.Combine(staging, "Lx.UI.exe")))
        {
            throw new InvalidOperationException("更新包里没有 Lx.UI.exe");
        }

        return new StagedUpdate(info.Version, zipPath, staging);
    }

    // 启动独立 PowerShell：等当前进程退出 -> 备份旧 exe -> 覆盖 -> 启动 -> 失败则回滚。
    public static Process StartUpdater(StagedUpdate update, string targetDir)
    {
        string updatesDir = Path.GetDirectoryName(update.StagingDir)
                            ?? Path.Combine(targetDir, "updates");
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string backup = Path.Combine(updatesDir, $"backup-{update.Version}-{stamp}");
        string script = Path.Combine(updatesDir, $"apply-{update.Version}.ps1");

        File.WriteAllText(script, ApplyScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments =
                $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" " +
                $"-ProcessId {Environment.ProcessId} " +
                $"-Source \"{update.StagingDir}\" " +
                $"-Target \"{targetDir.TrimEnd(Path.DirectorySeparatorChar)}\" " +
                $"-Backup \"{backup}\"",
        };

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("启动更新进程失败");
    }

    private static async Task<string> ReadTextAsync(string url, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.IsFile)
        {
            return await File.ReadAllTextAsync(uri.LocalPath, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(url))
        {
            return await File.ReadAllTextAsync(url, cancellationToken).ConfigureAwait(false);
        }

        return await Http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
    }

    private static async Task DownloadToAsync(
        string url,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.IsFile)
        {
            File.Copy(uri.LocalPath, destination, overwrite: true);
            progress?.Report(1);
            return;
        }

        if (File.Exists(url))
        {
            File.Copy(url, destination, overwrite: true);
            progress?.Report(1);
            return;
        }

        using HttpResponseMessage response = await Http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream output = File.Create(destination);

        byte[] buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            if (total is > 0)
            {
                progress?.Report((double)received / total.Value);
            }
        }
    }

    private static void ExtractSafely(string zipPath, string staging)
    {
        string root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;

        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string destination = Path.GetFullPath(Path.Combine(staging, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("更新包里包含越界路径");
            }
        }

        archive.ExtractToDirectory(staging, overwriteFiles: true);
    }

    private static string Sha256Of(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

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
            // 删不掉就算了
        }
    }

    private sealed class UpdateManifest
    {
        public string? Version { get; set; }
        public string? Url { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        public string? Notes { get; set; }
    }

    private const string ApplyScript = """
param(
    [int]$ProcessId,
    [string]$Source,
    [string]$Target,
    [string]$Backup
)

$ErrorActionPreference = 'Stop'
try {
    Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue
} catch {
    # 进程可能已经退出
}

New-Item -ItemType Directory -Force -Path $Backup | Out-Null
Copy-Item -LiteralPath (Join-Path $Target 'Lx.UI.exe') -Destination $Backup -Force -ErrorAction SilentlyContinue
if (Test-Path (Join-Path $Target 'run.cmd')) {
    Copy-Item -LiteralPath (Join-Path $Target 'run.cmd') -Destination $Backup -Force -ErrorAction SilentlyContinue
}

try {
    Copy-Item -Path (Join-Path $Source '*') -Destination $Target -Recurse -Force
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $env:LOCALAPPDATA 'Lx\bundle'

    $p = Start-Process -FilePath (Join-Path $Target 'Lx.UI.exe') -WorkingDirectory $Target -PassThru
    Start-Sleep -Seconds 5
    if ($p.HasExited) {
        throw '新版本启动后立即退出'
    }

    exit 0
} catch {
    Set-Content -Path (Join-Path $Backup 'apply-error.txt') -Value $_.Exception.ToString() -Encoding UTF8
    Copy-Item -LiteralPath (Join-Path $Backup 'Lx.UI.exe') -Destination $Target -Force -ErrorAction SilentlyContinue
    if (Test-Path (Join-Path $Backup 'run.cmd')) {
        Copy-Item -LiteralPath (Join-Path $Backup 'run.cmd') -Destination $Target -Force -ErrorAction SilentlyContinue
    }

    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $env:LOCALAPPDATA 'Lx\bundle'
    Start-Process -FilePath (Join-Path $Target 'Lx.UI.exe') -WorkingDirectory $Target | Out-Null
    exit 1
}
""";
}
