using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lx.Probe.Mods;

// 一个 mod 文件（.jar 或 .jar.disabled）。
// Dependencies 目前来自 fabric.mod.json 的 depends；Forge/NeoForge 只读 id/name/version。
public sealed record ModFile(
    string FileName,
    string? Id,
    string? Name,
    string? Version,
    bool Enabled,
    long Size,
    string? Loader = null,
    IReadOnlyList<string>? Dependencies = null)
{
    public string DisplayName => string.IsNullOrEmpty(Name) ? FileName : Name!;
}

// mods 目录的增删改查。启用/禁用靠改后缀：xxx.jar ↔ xxx.jar.disabled。
public static class ModManager
{
    public const string DisabledSuffix = ".disabled";

    public static string ModsDir(string gameDir) => Path.Combine(gameDir, "mods");

    // 批量添加的结果：新增 / 已存在跳过 / 不是 jar 或复制失败。
    public sealed record AddManyResult(int Added, int Skipped, int Failed, List<string> Messages)
    {
        public bool AnyAdded => Added > 0;
    }

    // 一次拖入多个 jar 时用：重名不覆盖，非 jar 跳过，单个失败不影响其它文件。
    public static AddManyResult AddMany(string gameDir, IEnumerable<string> sources)
    {
        string dir = ModsDir(gameDir);
        Directory.CreateDirectory(dir);

        int added = 0;
        int skipped = 0;
        int failed = 0;
        var messages = new List<string>();

        foreach (string source in sources)
        {
            string name = Path.GetFileName(source);
            if (!source.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                messages.Add($"[跳过] {name}：不是 .jar");
                continue;
            }

            string target = Path.Combine(dir, name);
            if (File.Exists(target))
            {
                skipped++;
                messages.Add($"[跳过] {name}：mods 里已经有了，不覆盖");
                continue;
            }

            try
            {
                File.Copy(source, target);
                added++;
                messages.Add($"[已添加] {name}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
                messages.Add($"[失败] {name}：{ex.Message}");
            }
        }

        return new AddManyResult(added, skipped, failed, messages);
    }

    public static List<ModFile> List(string gameDir)
    {
        string dir = ModsDir(gameDir);
        var result = new List<ModFile>();
        if (!Directory.Exists(dir))
        {
            return result;
        }

        foreach (string path in Directory.EnumerateFiles(dir).OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
        {
            string fileName = Path.GetFileName(path);
            bool disabled = fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            bool jar = disabled
                ? fileName.EndsWith(".jar" + DisabledSuffix, StringComparison.OrdinalIgnoreCase)
                : fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
            if (!jar)
            {
                continue;
            }

            (string? id, string? name, string? version, string? loader, List<string>? dependencies) = ReadMetadata(path);
            result.Add(new ModFile(fileName, id, name, version, !disabled, new FileInfo(path).Length, loader, dependencies));
        }

        return result;
    }

    public static bool SetEnabled(string gameDir, string fileName, bool enabled)
    {
        string? path = Find(gameDir, fileName);
        if (path is null)
        {
            return false;
        }

        bool currentlyEnabled = !path.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
        if (currentlyEnabled == enabled)
        {
            return true;
        }

        string target = enabled
            ? path[..^DisabledSuffix.Length]
            : path + DisabledSuffix;

        File.Move(path, target);
        return true;
    }

    public static void Add(string gameDir, string sourceJar)
    {
        if (!sourceJar.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("只能添加 .jar 文件");
        }

        string dir = ModsDir(gameDir);
        Directory.CreateDirectory(dir);

        string target = Path.Combine(dir, Path.GetFileName(sourceJar));
        if (File.Exists(target))
        {
            throw new InvalidOperationException($"mods 里已经有 {Path.GetFileName(sourceJar)}，先删掉或改名");
        }

        File.Copy(sourceJar, target);
    }

    public static bool Remove(string gameDir, string fileName)
    {
        string? path = Find(gameDir, fileName);
        if (path is null)
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private static string? Find(string gameDir, string fileName)
    {
        string dir = ModsDir(gameDir);
        if (!Directory.Exists(dir))
        {
            return null;
        }

        return Directory.EnumerateFiles(dir)
            .FirstOrDefault(path =>
            {
                string name = Path.GetFileName(path);
                return string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(name, fileName + DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            });
    }

    // 从 jar 里读 metadata：Fabric 优先 fabric.mod.json，Forge/NeoForge 读 mods.toml。
    private static (string? Id, string? Name, string? Version, string? Loader, List<string>? Dependencies) ReadMetadata(string path)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(path);

            ZipArchiveEntry? fabric = FindEntry(zip, "fabric.mod.json");
            if (fabric is not null)
            {
                using Stream stream = fabric.Open();
                JsonNode? node = JsonNode.Parse(stream);
                List<string>? dependencies = null;
                if (node?["depends"] is JsonObject depends)
                {
                    dependencies = depends.Select(pair => pair.Key).ToList();
                }

                return (
                    node?["id"]?.GetValue<string>(),
                    node?["name"]?.GetValue<string>(),
                    node?["version"]?.GetValue<string>(),
                    "fabric",
                    dependencies);
            }

            ZipArchiveEntry? neoforge = FindEntry(zip, "META-INF/neoforge.mods.toml");
            ZipArchiveEntry? forge = FindEntry(zip, "META-INF/mods.toml");
            ZipArchiveEntry? toml = neoforge ?? forge;
            if (toml is not null)
            {
                using Stream stream = toml.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string text = reader.ReadToEnd();
                string? version = MatchToml(text, "version");
                if (version is not null && version.StartsWith("${", StringComparison.Ordinal))
                {
                    version = null;
                }

                return (
                    MatchToml(text, "modId"),
                    MatchToml(text, "displayName"),
                    version,
                    neoforge is not null ? "neoforge" : "forge",
                    null);
            }

            return (null, null, null, null, null);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            return (null, null, null, null, null);
        }
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string name) =>
        zip.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase));

    private static string? MatchToml(string text, string key)
    {
        Match match = Regex.Match(
            text,
            "^\\s*" + Regex.Escape(key) + "\\s*=\\s*\"([^\"]+)\"",
            RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }
}
