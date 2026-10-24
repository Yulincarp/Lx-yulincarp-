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

            items.Add(new DownloadItem(url, path, sha1, artifact.Size));
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

        return items;
    }
}