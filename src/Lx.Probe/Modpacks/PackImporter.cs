using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lx.Probe.Instances;
using Lx.Probe.Resolve;
using SharpCompress.Archives;

namespace Lx.Probe.Modpacks;

public enum PackKind
{
    Modrinth,
    CurseForge,
    Generic,
}

// 整合包导入入口：按压缩包内容判断格式，.mrpack / CurseForge zip / 裸整合包（zip、rar、7z、tar）。
public static class PackImporter
{
    public static PackKind DetectKind(string path)
    {
        using IArchive archive = ArchiveFactory.Open(path);
        List<string> keys = archive.Entries
            .Where(entry => !entry.IsDirectory)
            .Select(entry => entry.Key.Replace('\\', '/'))
            .ToList();

        if (keys.Any(key => key.EndsWith("modrinth.index.json", StringComparison.OrdinalIgnoreCase)))
        {
            return PackKind.Modrinth;
        }

        bool hasManifest = keys.Any(key => key.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase));
        bool hasOverrides = keys.Any(key => key.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase));
        if (hasManifest && hasOverrides)
        {
            return PackKind.CurseForge;
        }

        return PackKind.Generic;
    }

    public static async Task<Instance> ImportAsync(string path, string installRoot, Action<string> log, string? curseForgeKey = null)
    {
        PackKind kind = DetectKind(path);
        log($"识别格式：{Describe(kind)}");

        return kind switch
        {
            PackKind.Modrinth => await MrpackInstaller.ImportAsync(path, installRoot, log).ConfigureAwait(false),
            PackKind.CurseForge => await ImportCurseForgeAsync(path, installRoot, log, curseForgeKey).ConfigureAwait(false),
            _ => await ImportGenericAsync(path, installRoot, log).ConfigureAwait(false),
        };
    }

    private static string Describe(PackKind kind) => kind switch
    {
        PackKind.Modrinth => "Modrinth (.mrpack)",
        PackKind.CurseForge => "CurseForge (manifest.json + overrides)",
        _ => "通用整合包（zip / rar / 7z / tar）",
    };

    // ---------- CurseForge ----------

    private static async Task<Instance> ImportCurseForgeAsync(string path, string installRoot, Action<string> log, string? apiKey)
    {
        JsonArray? fileNodes;
        string name;
        string minecraft;
        string? loader;
        string instanceDir;

        using (IArchive archive = ArchiveFactory.Open(path))
        {
            List<IArchiveEntry> entries = archive.Entries.Where(entry => !entry.IsDirectory).ToList();

            IArchiveEntry manifestEntry = entries.First(entry =>
                entry.Key.Replace('\\', '/').EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase));

            JsonNode manifest = ReadJson(manifestEntry) ?? throw new InvalidOperationException("manifest.json 解析失败");

            name = SanitizeName(manifest["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path));
            minecraft = manifest["minecraft"]?["version"]?.GetValue<string>()
                        ?? throw new InvalidOperationException("manifest 里没有 minecraft.version");

            string? loaderId = (manifest["minecraft"]?["modLoaders"] as JsonArray)?
                .FirstOrDefault(node => node?["primary"]?.GetValue<bool>() == true)?["id"]?.GetValue<string>();

            loader = loaderId is not null && loaderId.StartsWith("fabric", StringComparison.OrdinalIgnoreCase)
                ? "fabric"
                : null;

            if (loaderId is not null && loader is null)
            {
                log($"⚠ 这个包用的是 {loaderId}，当前只支持 Fabric 启动；文件会导入，但无法直接启动。");
            }

            fileNodes = manifest["files"] as JsonArray;

            instanceDir = InstanceStore.DirFor(installRoot, name);
            Directory.CreateDirectory(instanceDir);
            ExtractPrefix(archive, entries, "overrides/", instanceDir, log);
        }

        int fileCount = fileNodes?.Count ?? 0;
        if (fileCount > 0)
        {
            if (apiKey is null)
            {
                log("⚠ 没配置 CurseForge API key，跳过 mod 下载（只导入了 overrides 和配置）。");
                log("  用 --cf-key <key>，或把 key 写进 minecraft\\curseforge-key.txt（curseforge.com 免费申请）。");
            }
            else
            {
                await DownloadCurseForgeFilesAsync(fileNodes!, instanceDir, apiKey, log).ConfigureAwait(false);
            }
        }

        var instance = new Instance(name, minecraft, loader, null);
        InstanceStore.AddOrUpdate(installRoot, instance);

        log($"CurseForge 包：{name}（{minecraft}{(loader is null ? string.Empty : " + Fabric")}）");
        return instance;
    }

    private static async Task DownloadCurseForgeFilesAsync(JsonArray files, string instanceDir, string apiKey, Action<string> log)
    {
        var ids = new List<long>();
        foreach (JsonNode? node in files)
        {
            long? fileId = node?["fileID"]?.GetValue<long>();
            if (fileId is not null)
            {
                ids.Add(fileId.Value);
            }
        }

        if (ids.Count == 0)
        {
            return;
        }

        Dictionary<long, CurseForgeFile> map = await CurseForgeClient.GetFilesAsync(ids, apiKey, log).ConfigureAwait(false);

        var items = new List<DownloadItem>();
        int noUrl = 0;
        int missing = 0;
        foreach (long id in ids)
        {
            if (!map.TryGetValue(id, out CurseForgeFile? file))
            {
                missing++;
                continue;
            }

            if (file.DownloadUrl is null)
            {
                noUrl++;
                continue;
            }

            items.Add(new DownloadItem(file.DownloadUrl, "mods/" + file.FileName, file.Sha1 ?? string.Empty, file.Size));
        }

        if (noUrl > 0)
        {
            log($"⚠ {noUrl} 个文件作者禁止第三方下载（downloadUrl 为空），需要手动下载");
        }

        if (missing > 0)
        {
            log($"⚠ {missing} 个文件 CurseForge 没有返回信息");
        }

        if (items.Count == 0)
        {
            return;
        }

        log($"开始下载 {items.Count} 个文件…");
        int failed = await Downloader.DownloadAllAsync(items, instanceDir).ConfigureAwait(false);
        log($"CurseForge 文件下载完成，失败 {failed} 个");
    }

    // ---------- 通用整合包 ----------

    private static async Task<Instance> ImportGenericAsync(string path, string installRoot, Action<string> log)
    {
        using IArchive archive = ArchiveFactory.Open(path);
        List<IArchiveEntry> entries = archive.Entries
            .Where(entry => !entry.IsDirectory && !IsJunk(entry.Key))
            .ToList();

        string? commonRoot = FindCommonRoot(entries);
        if (commonRoot is not null)
        {
            log($"检测到单层目录：{commonRoot}/");
        }

        string fallbackName = commonRoot ?? Path.GetFileNameWithoutExtension(path);
        string name = SanitizeName(fallbackName);
        string instanceDir = InstanceStore.DirFor(installRoot, name);
        Directory.CreateDirectory(instanceDir);

        int written = ExtractEntries(entries, commonRoot, instanceDir, log);
        log($"解压完成：{written} 个文件");

        (string minecraft, string? loader) = DetectVersion(instanceDir);
        var instance = new Instance(name, minecraft, loader, null);
        InstanceStore.AddOrUpdate(installRoot, instance);

        log($"实例已创建：{name}（{minecraft}{(loader is null ? string.Empty : " + Fabric")}）");
        await Task.CompletedTask.ConfigureAwait(false);
        return instance;
    }

    // ---------- 压缩包工具 ----------

    private static int ExtractEntries(List<IArchiveEntry> entries, string? stripPrefix, string destRoot, Action<string> log)
    {
        string root = Path.GetFullPath(destRoot) + Path.DirectorySeparatorChar;
        int count = 0;

        foreach (IArchiveEntry entry in entries)
        {
            string key = entry.Key.Replace('\\', '/');
            if (stripPrefix is not null && key.StartsWith(stripPrefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                key = key[(stripPrefix.Length + 1)..];
            }

            if (key.Length == 0)
            {
                continue;
            }

            string destination = Path.GetFullPath(Path.Combine(destRoot, key.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            try
            {
                using Stream input = entry.OpenEntryStream();
                using FileStream output = File.Create(destination);
                input.CopyTo(output);
                count++;
            }
            catch (IOException ex)
            {
                log($"跳过（被占用）：{key}（{ex.Message}）");
            }
        }

        return count;
    }

    private static void ExtractPrefix(IArchive archive, List<IArchiveEntry> entries, string prefix, string destRoot, Action<string> log)
    {
        List<IArchiveEntry> matches = entries
            .Where(entry => entry.Key.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        int count = 0;
        foreach (IArchiveEntry entry in matches)
        {
            string key = entry.Key.Replace('\\', '/')[prefix.Length..];
            if (key.Length == 0)
            {
                continue;
            }

            string destination = Path.Combine(destRoot, key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using Stream input = entry.OpenEntryStream();
            using FileStream output = File.Create(destination);
            input.CopyTo(output);
            count++;
        }

        log($"{prefix} 解压：{count} 个文件");
    }

    private static JsonNode? ReadJson(IArchiveEntry entry)
    {
        using Stream stream = entry.OpenEntryStream();
        return JsonNode.Parse(stream);
    }

    private static bool IsJunk(string rawKey)
    {
        string key = rawKey.Replace('\\', '/');
        return key.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)
               || key.EndsWith(".DS_Store", StringComparison.OrdinalIgnoreCase)
               || key.StartsWith(".git/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindCommonRoot(List<IArchiveEntry> entries)
    {
        string? root = null;
        foreach (IArchiveEntry entry in entries)
        {
            string key = entry.Key.Replace('\\', '/');
            int slash = key.IndexOf('/');
            if (slash <= 0)
            {
                return null; // 根目录有文件，不是单层结构
            }

            string first = key[..slash];
            if (root is null)
            {
                root = first;
            }
            else if (!string.Equals(root, first, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return root;
    }

    // 从 mods 的 fabric.mod.json 里猜 Minecraft 版本；猜不到就用 1.20.4。
    private static (string Minecraft, string? Loader) DetectVersion(string instanceDir)
    {
        // Prism / MultiMC 的 mmc-pack.json 最准
        string mmc = Path.Combine(instanceDir, "mmc-pack.json");
        if (File.Exists(mmc))
        {
            try
            {
                JsonNode node = JsonNode.Parse(File.ReadAllText(mmc))!;
                string? minecraft = null;
                string? loader = null;
                foreach (JsonNode? component in node["components"] as JsonArray ?? new JsonArray())
                {
                    string? uid = component?["uid"]?.GetValue<string>();
                    string? version = component?["version"]?.GetValue<string>();
                    if (uid == "net.minecraft")
                    {
                        minecraft = version;
                    }
                    else if (uid is not null && uid.StartsWith("net.fabricmc", StringComparison.OrdinalIgnoreCase))
                    {
                        loader = "fabric";
                    }
                }

                if (minecraft is not null)
                {
                    return (minecraft, loader);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // 忽略，继续靠 mods 猜
            }
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string modsDir = Path.Combine(instanceDir, "mods");
        if (Directory.Exists(modsDir))
        {
            foreach (string jar in Directory.EnumerateFiles(modsDir, "*.jar").Take(80))
            {
                foreach (string version in ReadMinecraftDepends(jar))
                {
                    counts[version] = counts.GetValueOrDefault(version) + 1;
                }
            }
        }

        if (counts.Count > 0)
        {
            string best = counts.OrderByDescending(pair => pair.Value).First().Key;
            return (best, "fabric");
        }

        return ("1.20.4", null);
    }

    private static IEnumerable<string> ReadMinecraftDepends(string jarPath)
    {
        JsonNode? mod;
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(jarPath);
            ZipArchiveEntry? entry = zip.GetEntry("fabric.mod.json");
            if (entry is null)
            {
                yield break;
            }

            using Stream stream = entry.Open();
            mod = JsonNode.Parse(stream);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            yield break;
        }

        JsonNode? depends = mod?["depends"]?["minecraft"];
        if (depends is null)
        {
            yield break;
        }

        if (depends is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                foreach (string version in ExtractVersions(item?.GetValue<string>()))
                {
                    yield return version;
                }
            }

            yield break;
        }

        foreach (string version in ExtractVersions(depends.GetValue<string>()))
        {
            yield return version;
        }
    }

    private static IEnumerable<string> ExtractVersions(string? range)
    {
        if (range is null)
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(range, @"\d+\.\d+(?:\.\d+)?"))
        {
            yield return match.Value;
        }
    }

    private static string SanitizeName(string name)
    {
        try
        {
            return InstanceStore.ValidateName(name);
        }
        catch (InvalidOperationException)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string cleaned = string.Concat(name.Where(ch => Array.IndexOf(invalid, ch) < 0)).Trim();
            return cleaned.Length == 0 ? "整合包" : cleaned;
        }
    }
}
