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
            .Select(entry => KeyOf(entry).Replace('\\', '/'))
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

    public static async Task<PackImportResult> ImportAsync(
        string path,
        string installRoot,
        Action<string> log,
        string? curseForgeKey = null)
    {
        PackKind kind = DetectKind(path);
        log($"识别格式：{Describe(kind)}");

        PackImportResult result = kind switch
        {
            PackKind.Modrinth => await MrpackInstaller.ImportAsync(path, installRoot, log).ConfigureAwait(false),
            PackKind.CurseForge => await ImportCurseForgeAsync(path, installRoot, log, curseForgeKey).ConfigureAwait(false),
            _ => await ImportGenericAsync(path, installRoot, log).ConfigureAwait(false),
        };

        string instanceDir = InstanceStore.DirFor(installRoot, result.Instance.Name);
        PackRecord.Save(instanceDir, new PackRecord(path, result.Instance.Name, result.Version, result.Files));
        return result;
    }

    // 用原整合包重新导入一次，再删掉旧清单里有、新包里没有的文件（只删我们装过的）。
    public static async Task<int> UpdateAsync(string instanceDir, string installRoot, Action<string> log)
    {
        PackRecord? record = PackRecord.Load(instanceDir);
        if (record is null)
        {
            throw new InvalidOperationException("这个实例不是导入的整合包（没有 pack.json）");
        }

        if (!File.Exists(record.SourcePath))
        {
            throw new InvalidOperationException($"找不到原整合包：{record.SourcePath}");
        }

        PackImportResult result = await ImportAsync(record.SourcePath, installRoot, log).ConfigureAwait(false);

        var newFiles = new HashSet<string>(result.Files, StringComparer.OrdinalIgnoreCase);
        int deleted = 0;
        foreach (string stale in record.Files.Where(file => !newFiles.Contains(file)))
        {
            string full = Path.Combine(instanceDir, stale.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                File.Delete(full);
                deleted++;
            }
        }

        log($"更新完成：{record.PackName} {record.PackVersion} → {result.Version}，删除 {deleted} 个旧文件");
        return deleted;
    }

    private static string Describe(PackKind kind) => kind switch
    {
        PackKind.Modrinth => "Modrinth (.mrpack)",
        PackKind.CurseForge => "CurseForge (manifest.json + overrides)",
        _ => "通用整合包（zip / rar / 7z / tar）",
    };

    // ---------- CurseForge ----------

    private static async Task<PackImportResult> ImportCurseForgeAsync(
        string path,
        string installRoot,
        Action<string> log,
        string? apiKey)
    {
        JsonArray? fileNodes;
        string name;
        string minecraft;
        string packVersion;
        string? loader;
        string instanceDir;
        List<string> overrides;

        using (IArchive archive = ArchiveFactory.Open(path))
        {
            List<IArchiveEntry> entries = archive.Entries.Where(entry => !entry.IsDirectory).ToList();

            IArchiveEntry manifestEntry = entries.First(entry =>
                KeyOf(entry).Replace('\\', '/').EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase));

            JsonNode manifest = ReadJson(manifestEntry) ?? throw new InvalidOperationException("manifest.json 解析失败");

            name = SanitizeName(manifest["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path));
            minecraft = manifest["minecraft"]?["version"]?.GetValue<string>()
                        ?? throw new InvalidOperationException("manifest 里没有 minecraft.version");
            packVersion = manifest["version"]?.GetValue<string>() ?? "1.0";

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
            overrides = ExtractPrefix(entries, "overrides/", instanceDir, log);
        }

        var files = new List<string>(overrides);

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
                files.AddRange(await DownloadCurseForgeFilesAsync(fileNodes!, instanceDir, apiKey, log).ConfigureAwait(false));
            }
        }

        var instance = new Instance(name, minecraft, loader, null);
        InstanceStore.AddOrUpdate(installRoot, instance);

        log($"CurseForge 包：{name}（{minecraft}{(loader is null ? string.Empty : " + Fabric")}）");
        return new PackImportResult(instance, packVersion, files);
    }

    private static async Task<List<string>> DownloadCurseForgeFilesAsync(
        JsonArray files,
        string instanceDir,
        string apiKey,
        Action<string> log)
    {
        var written = new List<string>();
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
            return written;
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

            string relative = "mods/" + file.FileName;
            items.Add(new DownloadItem(file.DownloadUrl, relative, file.Sha1 ?? string.Empty, file.Size));
            written.Add(relative);
        }

        if (noUrl > 0)
        {
            log($"⚠ {noUrl} 个文件作者禁止第三方下载（downloadUrl 为空），需要手动下载");
        }

        if (missing > 0)
        {
            log($"⚠ {missing} 个文件 CurseForge 没有返回信息");
        }

        if (items.Count > 0)
        {
            log($"开始下载 {items.Count} 个文件…");
            int failed = await Downloader.DownloadAllAsync(items, instanceDir).ConfigureAwait(false);
            log($"CurseForge 文件下载完成，失败 {failed} 个");
        }

        return written;
    }

    // ---------- 通用整合包 ----------

    private static async Task<PackImportResult> ImportGenericAsync(string path, string installRoot, Action<string> log)
    {
        using IArchive archive = ArchiveFactory.Open(path);
        List<IArchiveEntry> entries = archive.Entries
            .Where(entry => !entry.IsDirectory && !IsJunk(KeyOf(entry)))
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

        List<string> written = ExtractEntries(entries, commonRoot, instanceDir, log);
        log($"解压完成：{written.Count} 个文件");

        (string minecraft, string? loader) = DetectVersion(instanceDir);

        string? modLoaderKind = DetectModLoader(instanceDir);
        if (modLoaderKind is not null && !string.Equals(modLoaderKind, loader, StringComparison.OrdinalIgnoreCase))
        {
            log($"⚠ mods 里是 {modLoaderKind} 模组，需要用 {modLoaderKind} 的版本启动（Fabric 加载不了这些 mod）");
        }

        var instance = new Instance(name, minecraft, loader, null);
        InstanceStore.AddOrUpdate(installRoot, instance);

        log($"实例已创建：{name}（{minecraft}{(loader is null ? string.Empty : " + Fabric")}）");
        await Task.CompletedTask.ConfigureAwait(false);
        return new PackImportResult(instance, "local", written);
    }

    // ---------- 压缩包工具 ----------

    private static List<string> ExtractEntries(List<IArchiveEntry> entries, string? stripPrefix, string destRoot, Action<string> log)
    {
        string root = Path.GetFullPath(destRoot) + Path.DirectorySeparatorChar;
        var written = new List<string>();

        foreach (IArchiveEntry entry in entries)
        {
            string key = KeyOf(entry).Replace('\\', '/');
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
                written.Add(key);
            }
            catch (IOException ex)
            {
                log($"跳过（被占用）：{key}（{ex.Message}）");
            }
        }

        return written;
    }

    private static List<string> ExtractPrefix(List<IArchiveEntry> entries, string prefix, string destRoot, Action<string> log)
    {
        var written = new List<string>();
        foreach (IArchiveEntry entry in entries)
        {
            string full = KeyOf(entry).Replace('\\', '/');
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string key = full[prefix.Length..];
            if (key.Length == 0)
            {
                continue;
            }

            string destination = Path.Combine(destRoot, key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using Stream input = entry.OpenEntryStream();
            using FileStream output = File.Create(destination);
            input.CopyTo(output);
            written.Add(key);
        }

        log($"{prefix} 解压：{written.Count} 个文件");
        return written;
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

    // SharpCompress 的 IArchiveEntry.Key 类型可空；没有 key 的条目按空字符串处理，避免调用方 NRE。
    private static string KeyOf(IArchiveEntry entry) => entry.Key ?? string.Empty;

    private static string? FindCommonRoot(List<IArchiveEntry> entries)
    {
        string? root = null;
        foreach (IArchiveEntry entry in entries)
        {
            string key = KeyOf(entry).Replace('\\', '/');
            int slash = key.IndexOf('/');
            if (slash <= 0)
            {
                return null;
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

    // 粗略判断 mods 属于哪种加载器：NeoForge/Forge 的 mod 带 META-INF/*.mods.toml。
    private static string? DetectModLoader(string instanceDir)
    {
        string modsDir = Path.Combine(instanceDir, "mods");
        if (!Directory.Exists(modsDir))
        {
            return null;
        }

        foreach (string jar in Directory.EnumerateFiles(modsDir, "*.jar").Take(40))
        {
            try
            {
                using ZipArchive zip = ZipFile.OpenRead(jar);
                if (zip.GetEntry("META-INF/neoforge.mods.toml") is not null)
                {
                    return "NeoForge";
                }

                if (zip.GetEntry("META-INF/mods.toml") is not null)
                {
                    return "Forge";
                }
            }
            catch (InvalidDataException)
            {
                // 不是 zip 就跳过
            }
            catch (IOException)
            {
                // 被占用就跳过
            }
        }

        return null;
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
