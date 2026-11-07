using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lx.Probe.Model;
using Lx.Probe.Net;

namespace Lx.Probe.Updates;

public sealed record UpdateInfo(string Version, string Url, string Sha256, string? Notes, string? FilesUrl = null);

// 差量更新：只拉变化的文件。IsDelta=false 时走整包 zip。
public sealed record StagedUpdate(string Version, string ZipPath, string StagingDir, bool IsDelta = false);

// files.json：发布端产出的逐文件清单，客户端拿它和本地文件比对。
public sealed record UpdateFileEntry(string Path, string Sha256, long Size);

public sealed record UpdateFileManifest(
    string Version,
    List<UpdateFileEntry> Files,
    List<string>? Delete = null);

// 比对结果：哪些要下、哪些已经有了、哪些要删。
public sealed record DeltaPlan(
    IReadOnlyList<UpdateFileEntry> Download,
    int Unchanged,
    IReadOnlyList<string> Delete);

// 自更新：检查 update.json -> 下载 zip -> 校验 SHA-256 -> 解压 -> 交给 PowerShell 等主进程退出后替换。
// 默认只对发布版开放；开发版（src\Lx.UI\bin）不走自更新。
public static class UpdateService
{
    private static HttpClient Http => ProxyConfig.GetClient(TimeSpan.FromMinutes(30), AppInfo.UserAgent);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static bool CanSelfUpdate =>
        !AppContext.BaseDirectory.Contains(Path.Combine("src", "Lx.UI", "bin"), StringComparison.OrdinalIgnoreCase);

    // 默认查询地址：GitHub Release 的 latest 资产，跟着每次发版自动指向最新。
    // 国内直连 github.com 可能不稳，用户/测试仍可用 LX_UPDATE_URL 覆盖成本地文件或镜像。
    public const string DefaultManifestUrl =
        "https://github.com/Yulincarp/Lx-yulincarp-/releases/latest/download/update.json";

    public const string DefaultBetaManifestUrl =
        "https://github.com/Yulincarp/Lx-yulincarp-/releases/latest/download/update-beta.json";

    // 差量的"值不值得"阈值：单文件发布下，差量要下的是未压缩的 exe，
    // 反而比压缩过的整包大（191MB vs 74MB）。所以超过这个体积就别走差量了。
    public const long DeltaWorthwhileBytes = 20L * 1024 * 1024;

    // update.json 地址：LX_UPDATE_URL 优先，其次数据目录下的 update.json（方便本地测试），最后走内置默认地址。
    public static string ResolveManifestUrl(string installRoot)
    {
        string? fromEnv = Environment.GetEnvironmentVariable("LX_UPDATE_URL");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        bool beta = string.Equals(
            SettingsStore.Load(installRoot).UpdateChannel,
            "beta",
            StringComparison.OrdinalIgnoreCase);
        string fileName = beta ? "update-beta.json" : "update.json";
        string local = Path.Combine(installRoot, fileName);
        if (File.Exists(local))
        {
            return local;
        }

        return beta ? DefaultBetaManifestUrl : DefaultManifestUrl;
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

        string? filesUrl = manifest.Files;
        if (filesUrl is not null &&
            Uri.TryCreate(manifestUrl, UriKind.Absolute, out Uri? manifestBase) &&
            Uri.TryCreate(manifestBase, filesUrl, out Uri? resolvedFiles))
        {
            filesUrl = resolvedFiles.ToString();
        }

        return new UpdateInfo(manifest.Version, downloadUrl, manifest.Sha256, manifest.Notes, filesUrl);
    }

    // 读 files.json 并和本地目录比对：返回要下载/删除的清单。
    // 只看文件哈希，不看时间戳——发版时整个目录重打，时间戳没有参考价值。
    public static async Task<DeltaPlan> PlanDeltaAsync(
        string filesUrl,
        string targetDir,
        CancellationToken cancellationToken = default)
    {
        string json = await ReadTextAsync(filesUrl, cancellationToken).ConfigureAwait(false);
        UpdateFileManifest? manifest = JsonSerializer.Deserialize<UpdateFileManifest>(json, Json);
        if (manifest?.Files is null || manifest.Files.Count == 0)
        {
            throw new InvalidOperationException("files.json 里没有文件清单");
        }

        var downloads = new List<UpdateFileEntry>();
        int unchanged = 0;
        string root = Path.GetFullPath(targetDir);

        foreach (UpdateFileEntry entry in manifest.Files)
        {
            string relative = entry.Path.Replace('\\', '/').TrimStart('/');
            if (relative.Length == 0 || relative.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"files.json 里有非法路径：{entry.Path}");
            }

            string local = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(local) &&
                string.Equals(Sha256Of(local), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                unchanged++;
                continue;
            }

            downloads.Add(entry with { Path = relative });
        }

        return new DeltaPlan(downloads, unchanged, manifest.Delete ?? new List<string>());
    }

    // 变化量小于阈值才走差量；否则整包更省流量（zip 是压缩过的，单个 exe 不是）。
    public static bool IsDeltaWorthwhile(DeltaPlan plan) =>
        plan.Download.Sum(entry => entry.Size) <= DeltaWorthwhileBytes;

    // 差量下载：只把变化的文件放进 staging，别的原样保留。
    public static async Task<StagedUpdate> DownloadDeltaAsync(
        UpdateInfo info,
        string targetDir,
        DeltaPlan plan,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string updatesDir = Path.Combine(targetDir, "updates");
        string staging = Path.Combine(updatesDir, "staging-" + info.Version + "-delta");
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        Directory.CreateDirectory(staging);

        string baseUrl = info.FilesUrl ?? info.Url;
        int done = 0;
        int total = Math.Max(plan.Download.Count, 1);

        foreach (UpdateFileEntry entry in plan.Download)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destination = Path.Combine(staging, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            string url = new Uri(new Uri(baseUrl), entry.Path).ToString();
            string temp = destination + ".part";
            await DownloadToAsync(url, temp, null, cancellationToken).ConfigureAwait(false);

            string actual = Sha256Of(temp);
            if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(temp);
                throw new InvalidOperationException($"{entry.Path} 校验失败（期望 {entry.Sha256[..12]}…）");
            }

            File.Move(temp, destination, overwrite: true);
            done++;
            progress?.Report((double)done / total);
        }

        // 删除清单写进 staging，交给 PowerShell 在替换后执行
        if (plan.Delete.Count > 0)
        {
            File.WriteAllLines(Path.Combine(staging, "delete.txt"), plan.Delete);
        }

        return new StagedUpdate(info.Version, string.Empty, staging, IsDelta: true);
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

        string exeName = AppInfo.ExecutableName;
        if (!File.Exists(Path.Combine(staging, exeName)))
        {
            throw new InvalidOperationException($"更新包里没有 {exeName}");
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
                $"-Backup \"{backup}\" " +
                $"-ExeName \"{AppInfo.ExecutableName}\"",
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

        // 可选的差量清单地址（相对 update.json 或绝对 URL）
        public string? Files { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        public string? Notes { get; set; }
    }

    private const string ApplyScript = """
param(
    [int]$ProcessId,
    [string]$Source,
    [string]$Target,
    [string]$Backup,
    [string]$ExeName
)

$ErrorActionPreference = 'Stop'
try {
    Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue
} catch {
    # 进程可能已经退出
}

New-Item -ItemType Directory -Force -Path $Backup | Out-Null

# 备份这次要覆盖的每一个文件（不只是 exe）：整包更新时就是整个目录，
# 差量更新时只有变化的那些，两种情况用同一套恢复逻辑。
$staged = Get-ChildItem -Path $Source -Recurse -File | Where-Object { $_.Name -ne 'delete.txt' }
foreach ($f in $staged) {
    $rel = [System.IO.Path]::GetRelativePath($Source, $f.FullName)
    $dest = Join-Path $Backup $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
    Copy-Item -LiteralPath $f.FullName -Destination $dest -Force
}

# 新版本删掉的文件也要先备份，回滚时再放回去
$deletedDir = Join-Path $Backup '.deleted'
$deleteList = Join-Path $Source 'delete.txt'
if (Test-Path $deleteList) {
    foreach ($rel in (Get-Content -LiteralPath $deleteList)) {
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }
        $path = Join-Path $Target $rel
        if (Test-Path -LiteralPath $path) {
            $dest = Join-Path $deletedDir $rel
            New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
            Copy-Item -LiteralPath $path -Destination $dest -Force
            Remove-Item -LiteralPath $path -Force
        }
    }
}

try {
    Copy-Item -Path (Join-Path $Source '*') -Destination $Target -Recurse -Force -Exclude 'delete.txt'
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $env:LOCALAPPDATA 'Lx yulin carp\bundle'

    $p = Start-Process -FilePath (Join-Path $Target $ExeName) -WorkingDirectory $Target -PassThru
    Start-Sleep -Seconds 5
    if ($p.HasExited) {
        throw '新版本启动后立即退出'
    }

    exit 0
} catch {
    Set-Content -Path (Join-Path $Backup 'apply-error.txt') -Value $_.Exception.ToString() -Encoding UTF8

    # 把备份里的每个文件按原路径放回去
    $saved = Get-ChildItem -Path $Backup -Recurse -File | Where-Object {
        $_.Name -ne 'apply-error.txt' -and $_.FullName -notlike (Join-Path $deletedDir '*')
    }
    foreach ($f in $saved) {
        $rel = [System.IO.Path]::GetRelativePath($Backup, $f.FullName)
        $dest = Join-Path $Target $rel
        New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
        Copy-Item -LiteralPath $f.FullName -Destination $dest -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path $deletedDir) {
        Get-ChildItem -Path $deletedDir -Recurse -File | ForEach-Object {
            $rel = [System.IO.Path]::GetRelativePath($deletedDir, $_.FullName)
            $dest = Join-Path $Target $rel
            New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $dest -Force -ErrorAction SilentlyContinue
        }
    }

    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $env:LOCALAPPDATA 'Lx yulin carp\bundle'
    Start-Process -FilePath (Join-Path $Target $ExeName) -WorkingDirectory $Target | Out-Null
    exit 1
}
""";
}
