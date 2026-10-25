using System.Text.Json;
using Lx.Probe.Model;
using Lx.Probe.Net;

namespace Lx.Probe.Resolve;

// 读版本总表、按 id 找版本、把该版本的 version.json 下到本地。
public static class ManifestService
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static async Task<VersionManifest> EnsureManifestAsync(
        string installRoot,
        Func<string, string>? rewriteUrl = null,
        CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(installRoot, "version_manifest_v2.json");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(installRoot);
            HttpClient http = ProxyConfig.GetClient(TimeSpan.FromSeconds(60), AppInfo.UserAgent);
            string url = rewriteUrl?.Invoke(ManifestUrl) ?? ManifestUrl;
            string json = await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            string temp = path + ".part";
            await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }

        return JsonSerializer.Deserialize<VersionManifest>(File.ReadAllText(path), Options)
               ?? throw new InvalidOperationException("version_manifest_v2.json 解析失败");
    }

    // 本地安装的版本（Forge / NeoForge / Fabric 生成在 versions/ 下，Mojang manifest 里没有）。
    public static List<ManifestVersion> LocalVersions(string installRoot)
    {
        string dir = Path.Combine(installRoot, "versions");
        if (!Directory.Exists(dir))
        {
            return new List<ManifestVersion>();
        }

        var result = new List<ManifestVersion>();
        foreach (string versionDir in Directory.EnumerateDirectories(dir))
        {
            string id = Path.GetFileName(versionDir);
            if (File.Exists(Path.Combine(versionDir, id + ".json")))
            {
                result.Add(new ManifestVersion { Id = id, Type = "local" });
            }
        }

        return result;
    }

    // 找不到就抛，不带默认值——拼错版本号时应该立刻报错，而不是悄悄用别的版本。
    public static ManifestVersion Find(VersionManifest manifest, string versionId)
    {
        return (manifest.Versions ?? new List<ManifestVersion>())
            .FirstOrDefault(version => string.Equals(version.Id, versionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"版本 {versionId} 不在 manifest 里，用 --list 看看有哪些");
    }

    public static async Task<string> EnsureVersionJsonAsync(
        VersionManifest manifest,
        string versionId,
        string installRoot,
        Func<string, string>? rewriteUrl = null,
        CancellationToken cancellationToken = default)
    {
        string dir = Path.Combine(installRoot, "versions", versionId);
        string path = Path.Combine(dir, versionId + ".json");

        ManifestVersion? entry = (manifest.Versions ?? new List<ManifestVersion>())
            .FirstOrDefault(version => string.Equals(version.Id, versionId, StringComparison.OrdinalIgnoreCase));

        // Forge / NeoForge 装完的版本不在 Mojang manifest 里，直接读本地。
        if (entry is null)
        {
            if (File.Exists(path))
            {
                return path;
            }

            throw new InvalidOperationException($"版本 {versionId} 不在 manifest 里，本地也没有 {path}");
        }

        if (File.Exists(path) &&
            entry.Sha1 is not null &&
            string.Equals(Hasher.Sha1Of(path), entry.Sha1, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (entry.Url is null)
        {
            throw new InvalidOperationException($"版本 {versionId} 在 manifest 里没有 url");
        }

        Directory.CreateDirectory(dir);
        HttpClient http = ProxyConfig.GetClient(TimeSpan.FromSeconds(60), AppInfo.UserAgent);
        string url = rewriteUrl?.Invoke(entry.Url) ?? entry.Url;
        string json = await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        string temp = path + ".part";
        await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);

        return path;
    }
}
