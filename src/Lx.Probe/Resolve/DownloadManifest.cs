using System.Text.Json;
using Lx.Probe.Model;

namespace Lx.Probe.Resolve;

// M2 Step 1：把 version.json 算成一张下载清单，不碰网络、不碰文件。
public sealed record DownloadItem(string Url, string RelativePath, string Sha1, long Size);

public static class DownloadManifest
{
    public static List<DownloadItem> Build(
        VersionJson version,
        string versionId,
        Dictionary<string, bool> features,
        Func<string, string>? rewriteUrl = null)
    {
        var items = new List<DownloadItem>();

        foreach (Library library in version.Libraries ?? new List<Library>())
        {
            if (!RuleEvaluator.IsAllowed(library.Rules, features))
            {
                continue;
            }

            string? name = library.Name;
            if (name is not null &&
                name.Contains(":natives-", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(NativesExtractor.ArchSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!LibraryResolver.TryGetArtifact(library, out LibraryArtifact artifact))
            {
                continue;
            }
            string? path = artifact.Path;
            string? url = artifact.Url;
            string? sha1 = artifact.Sha1;
            if (path is null || url is null || sha1 is null)
            {
                continue; // 没有 sha1 的库宁可跳过，也不下可疑文件
            }

            string finalUrl = rewriteUrl?.Invoke(url) ?? url;
            items.Add(new DownloadItem(finalUrl, "libraries/" + path, sha1, artifact.Size));
        }

        LibraryArtifact? client = version.Downloads?.Client;
        if (client is not null)
        {
            string? url = client.Url;
            string? sha1 = client.Sha1;
            if (url is not null && sha1 is not null)
            {
                string clientVersionId = version.InheritsFrom ?? versionId;
                string finalUrl = rewriteUrl?.Invoke(url) ?? url;
                items.Add(new DownloadItem(finalUrl, $"versions/{clientVersionId}/{clientVersionId}.jar", sha1, client.Size));
            }
        }

        AssetIndexInfo? assetIndex = version.AssetIndex;
        if (assetIndex?.Url is not null && assetIndex.Sha1 is not null && assetIndex.Id is not null)
        {
            string finalUrl = rewriteUrl?.Invoke(assetIndex.Url) ?? assetIndex.Url;
            items.Add(new DownloadItem(finalUrl, $"assets/indexes/{assetIndex.Id}.json", assetIndex.Sha1, assetIndex.Size));
        }

        return items;
    }

    // 解析 assets/indexes/<id>.json，把 3811 个对象变成下载条目。
    public static List<DownloadItem> BuildAssets(string indexPath, Func<string, string>? rewriteUrl = null)
    {
        string json = File.ReadAllText(indexPath);
        AssetIndexFile? index = JsonSerializer.Deserialize<AssetIndexFile>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (index?.Objects is null)
        {
            return new List<DownloadItem>();
        }

        var items = new List<DownloadItem>(index.Objects.Count);
        foreach (KeyValuePair<string, AssetObject> pair in index.Objects)
        {
            string? hash = pair.Value.Hash;
            if (hash is null || hash.Length < 2)
            {
                continue;
            }

            string prefix = hash[..2];
            string url = $"https://resources.download.minecraft.net/{prefix}/{hash}";
            items.Add(new DownloadItem(
                rewriteUrl?.Invoke(url) ?? url,
                $"assets/objects/{prefix}/{hash}",
                hash,
                pair.Value.Size));
        }

        return items;
    }
}
