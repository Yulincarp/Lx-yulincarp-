using System.Text.Json;
using Lx.Probe.Model;

namespace Lx.Probe.Resolve;

// M2 Step 1：把 version.json 算成一张下载清单，不碰网络、不碰文件。
public sealed record DownloadItem(string Url, string RelativePath, string Sha1, long Size);

public static class DownloadManifest
{
    public static List<DownloadItem> Build(VersionJson version, string versionId, Dictionary<string, bool> features)
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

            LibraryArtifact? artifact = library.Downloads?.Artifact;
            if (artifact is null)
            {
                continue;
            }

            string? path = artifact.Path;
            string? url = artifact.Url;
            string? sha1 = artifact.Sha1;
            if (path is null || url is null || sha1 is null)
            {
                continue;
            }

            items.Add(new DownloadItem(url, "libraries/" + path, sha1, artifact.Size));
        }

        LibraryArtifact? client = version.Downloads?.Client;
        if (client is not null)
        {
            string? url = client.Url;
            string? sha1 = client.Sha1;
            if (url is not null && sha1 is not null)
            {
                items.Add(new DownloadItem(url, $"versions/{versionId}/{versionId}.jar", sha1, client.Size));
            }
        }

        AssetIndexInfo? assetIndex = version.AssetIndex;
        if (assetIndex?.Url is not null && assetIndex.Sha1 is not null && assetIndex.Id is not null)
        {
            items.Add(new DownloadItem(assetIndex.Url, $"assets/indexes/{assetIndex.Id}.json", assetIndex.Sha1, assetIndex.Size));
        }

        return items;
    }

    // 解析 assets/indexes/<id>.json，把 3811 个对象变成下载条目。
    public static List<DownloadItem> BuildAssets(string indexPath)
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
            items.Add(new DownloadItem(
                $"https://resources.download.minecraft.net/{prefix}/{hash}",
                $"assets/objects/{prefix}/{hash}",
                hash,
                pair.Value.Size));
        }

        return items;
    }
}