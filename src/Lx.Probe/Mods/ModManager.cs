using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lx.Probe.Mods;

// 一个 mod 文件（.jar 或 .jar.disabled）。
public sealed record ModFile(string FileName, string? Id, string? Name, string? Version, bool Enabled, long Size);

// mods 目录的增删改查。启用/禁用靠改后缀：xxx.jar ↔ xxx.jar.disabled。
public static class ModManager
{
    public const string DisabledSuffix = ".disabled";

    public static string ModsDir(string gameDir) => Path.Combine(gameDir, "mods");

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

            (string? id, string? name, string? version) = ReadMetadata(path);
            result.Add(new ModFile(fileName, id, name, version, !disabled, new FileInfo(path).Length));
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

    // 从 jar 里的 fabric.mod.json 读 id/name/version；读不到就返回 null，不影响列表。
    private static (string? Id, string? Name, string? Version) ReadMetadata(string path)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(path);
            ZipArchiveEntry? entry = zip.GetEntry("fabric.mod.json");
            if (entry is null)
            {
                return (null, null, null);
            }

            using Stream stream = entry.Open();
            JsonNode? node = JsonNode.Parse(stream);
            return (
                node?["id"]?.GetValue<string>(),
                node?["name"]?.GetValue<string>(),
                node?["version"]?.GetValue<string>());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            return (null, null, null);
        }
    }
}
