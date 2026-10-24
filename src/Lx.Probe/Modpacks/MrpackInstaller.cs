using System.IO.Compression;
using System.Text.Json.Nodes;
using Lx.Probe.Instances;
using Lx.Probe.Resolve;

namespace Lx.Probe.Modpacks;

// .mrpack（Modrinth 整合包）的 index 结构。
public sealed record MrpackFile(string Path, string Url, string? Sha1, long Size, string? ClientEnv);

public sealed record MrpackIndex(
    string Name,
    string VersionId,
    string MinecraftVersion,
    string? FabricLoaderVersion,
    List<MrpackFile> Files,
    List<string> OverrideEntries);

// 导入整合包：建实例目录 -> 下 mods -> 解压 overrides -> 写 instances.json。
public static class MrpackInstaller
{
    public static MrpackIndex ReadIndex(string mrpackPath)
    {
        using ZipArchive zip = ZipFile.OpenRead(mrpackPath);
        ZipArchiveEntry? entry = zip.GetEntry("modrinth.index.json");
        if (entry is null)
        {
            throw new InvalidOperationException("不是有效的 .mrpack：缺 modrinth.index.json");
        }

        using Stream stream = entry.Open();
        JsonNode root = JsonNode.Parse(stream) ?? throw new InvalidOperationException("index 解析失败");

        string name = root["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(mrpackPath);
        string versionId = root["versionId"]?.GetValue<string>() ?? "1.0.0";

        string minecraft = root["dependencies"]?["minecraft"]?.GetValue<string>()
                           ?? throw new InvalidOperationException("整合包没有声明 minecraft 版本");
        string? fabricLoader = root["dependencies"]?["fabric-loader"]?.GetValue<string>();

        var files = new List<MrpackFile>();
        if (root["files"] is JsonArray fileNodes)
        {
            foreach (JsonNode? node in fileNodes)
            {
                if (node is null)
                {
                    continue;
                }

                string? path = node["path"]?.GetValue<string>();
                string? url = (node["downloads"] as JsonArray)?.FirstOrDefault()?.GetValue<string>();
                if (path is null || url is null)
                {
                    continue;
                }

                files.Add(new MrpackFile(
                    path,
                    url,
                    node["hashes"]?["sha1"]?.GetValue<string>(),
                    node["fileSize"]?.GetValue<long>() ?? 0,
                    node["env"]?["client"]?.GetValue<string>()));
            }
        }

        var overrides = new List<string>();
        foreach (ZipArchiveEntry item in zip.Entries)
        {
            if (item.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase) ||
                item.FullName.StartsWith("client-overrides/", StringComparison.OrdinalIgnoreCase))
            {
                overrides.Add(item.FullName);
            }
        }

        return new MrpackIndex(name, versionId, minecraft, fabricLoader, files, overrides);
    }

    public static async Task<Instance> ImportAsync(string mrpackPath, string installRoot, Action<string> log)
    {
        MrpackIndex index = ReadIndex(mrpackPath);

        string name;
        try
        {
            name = InstanceStore.ValidateName(index.Name);
        }
        catch (InvalidOperationException)
        {
            name = InstanceStore.ValidateName(Path.GetFileNameWithoutExtension(mrpackPath));
        }

        string instanceDir = InstanceStore.DirFor(installRoot, name);
        Directory.CreateDirectory(instanceDir);

        log($"导入「{index.Name}」{index.VersionId} → {instanceDir}");

        // 客户端不支持的文件跳过（服务器专用 mod 之类）
        var items = new List<DownloadItem>();
        foreach (MrpackFile file in index.Files)
        {
            if (string.Equals(file.ClientEnv, "unsupported", StringComparison.OrdinalIgnoreCase))
            {
                log($"跳过（客户端不支持）：{file.Path}");
                continue;
            }

            items.Add(new DownloadItem(file.Url, file.Path, file.Sha1 ?? string.Empty, file.Size));
        }

        if (items.Count > 0)
        {
            int failed = await Downloader.DownloadAllAsync(items, instanceDir).ConfigureAwait(false);
            log($"整合包文件：{items.Count} 个，失败 {failed} 个");
        }

        ExtractOverrides(mrpackPath, instanceDir, log);

        var instance = new Instance(
            name,
            index.MinecraftVersion,
            index.FabricLoaderVersion is null ? null : "fabric",
            null);

        InstanceStore.AddOrUpdate(installRoot, instance);
        log($"实例已创建：{name}（{index.MinecraftVersion}{(index.FabricLoaderVersion is null ? string.Empty : " + Fabric " + index.FabricLoaderVersion)}）");

        return instance;
    }

    private static void ExtractOverrides(string mrpackPath, string instanceDir, Action<string> log)
    {
        using ZipArchive zip = ZipFile.OpenRead(mrpackPath);
        string root = Path.GetFullPath(instanceDir) + Path.DirectorySeparatorChar;
        int count = 0;

        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string relative;
            if (entry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
            {
                relative = entry.FullName["overrides/".Length..];
            }
            else if (entry.FullName.StartsWith("client-overrides/", StringComparison.OrdinalIgnoreCase))
            {
                relative = entry.FullName["client-overrides/".Length..];
            }
            else
            {
                continue;
            }

            if (relative.Length == 0 || entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            string destination = Path.GetFullPath(Path.Combine(instanceDir, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue; // 防 zip 路径穿越
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            try
            {
                entry.ExtractToFile(destination, overwrite: true);
                count++;
            }
            catch (IOException)
            {
                // 文件被占用（游戏开着）就跳过
            }
        }

        log($"overrides 解压：{count} 个文件");
    }
}
