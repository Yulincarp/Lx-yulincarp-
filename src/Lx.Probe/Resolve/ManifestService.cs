using System.Text.Json;
using Lx.Probe.Model;

namespace Lx.Probe.Resolve;

// 读版本总表、按 id 找版本、把该版本的 version.json 下到本地。
public static class ManifestService
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static async Task<VersionManifest> EnsureManifestAsync(string installRoot)
    {
        string path = Path.Combine(installRoot, "version_manifest_v2.json");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(installRoot);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            string json = await http.GetStringAsync(ManifestUrl).ConfigureAwait(false);
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
        }

        return JsonSerializer.Deserialize<VersionManifest>(File.ReadAllText(path), Options)
               ?? throw new InvalidOperationException("version_manifest_v2.json 解析失败");
    }

    // 找不到就抛，不带默认值——拼错版本号时应该立刻报错，而不是悄悄用别的版本。
    public static ManifestVersion Find(VersionManifest manifest, string versionId)
    {
        return (manifest.Versions ?? new List<ManifestVersion>())
            .FirstOrDefault(version => string.Equals(version.Id, versionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"版本 {versionId} 不在 manifest 里，用 --list 看看有哪些");
    }

    public static async Task<string> EnsureVersionJsonAsync(VersionManifest manifest, string versionId, string installRoot)
    {
        ManifestVersion entry = Find(manifest, versionId);
        string dir = Path.Combine(installRoot, "versions", versionId);
        string path = Path.Combine(dir, versionId + ".json");

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
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        string json = await http.GetStringAsync(entry.Url).ConfigureAwait(false);
        await File.WriteAllTextAsync(path, json).ConfigureAwait(false);

        return path;
    }
}
