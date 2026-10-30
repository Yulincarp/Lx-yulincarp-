using System.Text.Json;
using System.Text.Json.Serialization;
using Lx.Probe.Instances;
using Lx.Probe.Modpacks;
using Lx.Probe.Resolve;

namespace Lx.Probe.Sync;

// 清单里的一个受管文件。Path 相对实例目录，统一用 '/' 分隔。
// Sha1 为空表示"清单没给哈希"（v0 的 pack.json），同步时按本机文件现算。
public sealed record PackFileEntry(string Path, string Sha1, long Size, string? Url = null)
{
    [JsonIgnore]
    public bool HasHash => !string.IsNullOrWhiteSpace(Sha1);
}

// 一键进服用的服务器信息；Port 缺省 25565。
public sealed record PackServerInfo(string Address, int Port = 25565)
{
    [JsonIgnore]
    public string Display => Port == 25565 ? Address : $"{Address}:{Port}";

    // 支持 "mc.example.com" / "mc.example.com:25566" / "[::1]:25566"。
    public static PackServerInfo Parse(string? text)
    {
        string value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            throw new InvalidOperationException("服务器地址不能为空");
        }

        if (value[0] == '[')
        {
            int close = value.IndexOf(']');
            if (close > 1)
            {
                string v6Host = value[1..close];
                if (close + 1 < value.Length && value[close + 1] == ':' &&
                    int.TryParse(value[(close + 2)..], out int v6Port) && IsValidPort(v6Port))
                {
                    return new PackServerInfo(v6Host, v6Port);
                }

                return new PackServerInfo(v6Host);
            }
        }

        int colon = value.IndexOf(':');
        if (colon > 0 && colon == value.LastIndexOf(':') &&
            int.TryParse(value[(colon + 1)..], out int port) && IsValidPort(port))
        {
            return new PackServerInfo(value[..colon], port);
        }

        return new PackServerInfo(value);
    }

    private static bool IsValidPort(int port) => port is > 0 and <= 65535;

    // 一键进服：Minecraft 客户端认的是 --server + --port 两个独立参数。
    public IReadOnlyList<string> ToGameArgs() => new[]
    {
        "--server",
        Address,
        "--port",
        Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

// pack.json v1：一个实例"由哪个整合包管理"的记录，同时也是静态托管给客户端读的清单。
//
// 向后兼容：v0 的 pack.json 只有 SourcePath / PackName / PackVersion / Files；
// 新读取器把这些当"没有哈希、没有删除清单"的旧清单，哈希在同步时现算；
// 旧读取器（PackRecord）只认这四个字段，多余字段被忽略。
public sealed record PackManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string PackName { get; init; } = string.Empty;

    public string PackVersion { get; init; } = string.Empty;

    // 由哪个本地文件/URL 装的（只在实例里保留，导出时不写）。
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourcePath { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MinecraftVersion { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Loader { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LoaderVersion { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PackServerInfo? Server { get; init; }

    // v0 兼容字段：只有路径。保存时始终与 Entries 同步。
    public List<string> Files { get; init; } = new();

    public List<PackFileEntry> Entries { get; init; } = new();

    // 作者显式要求删除的文件（上一版有、这一版没有的，客户端也会自己算出来）。
    public List<string> Delete { get; init; } = new();

    // v0 清单一个哈希都没有；这种情况下同步只按"文件在不在"判断，不反复重下。
    [JsonIgnore]
    public bool IsLegacy => !AllEntries.Any(entry => entry.HasHash);

    [JsonIgnore]
    public IEnumerable<PackFileEntry> AllEntries =>
        Entries.Count > 0
            ? Entries
            : Files.Select(path => new PackFileEntry(path, string.Empty, 0));

    [JsonIgnore]
    public IEnumerable<string> AllPaths => AllEntries.Select(entry => entry.Path);

    public PackFileEntry? Find(string relativePath) =>
        AllEntries.FirstOrDefault(entry => string.Equals(entry.Path, relativePath, StringComparison.OrdinalIgnoreCase));

    public PackManifest Normalize()
    {
        var entries = AllEntries
            .Select(entry => entry with { Path = NormalizePath(entry.Path) })
            .Where(entry => entry.Path.Length > 0)
            .DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            Files = entries.Select(entry => entry.Path).ToList(),
            Entries = entries,
            Delete = Delete
                .Select(NormalizePath)
                .Where(path => path.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    // 拒绝绝对路径和 ".."，所有来自网络的路径都先过这一关。
    public static string NormalizePath(string? path)
    {
        string value = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (value.StartsWith('/') || value.StartsWith("./", StringComparison.Ordinal))
        {
            value = value.TrimStart('/');
            if (value.StartsWith("./", StringComparison.Ordinal))
            {
                value = value[2..];
            }
        }

        if (value.Length > 1 && value[1] == ':')
        {
            throw new InvalidOperationException($"清单里有绝对路径：{path}");
        }

        var parts = new List<string>();
        foreach (string segment in value.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                throw new InvalidOperationException($"清单里有越界路径：{path}");
            }

            parts.Add(segment);
        }

        return string.Join('/', parts);
    }

    public static string PathFor(string instanceDir) => Path.Combine(instanceDir, "pack.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(Normalize(), Options);

    public static PackManifest Parse(string json)
    {
        PackManifest? manifest = JsonSerializer.Deserialize<PackManifest>(json, Options);
        if (manifest is null)
        {
            throw new InvalidOperationException("pack.json 是空的");
        }

        if (manifest.SchemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"整合包清单版本 {manifest.SchemaVersion} 太新，请先更新启动器（本版只支持到 {CurrentSchemaVersion}）");
        }

        return manifest.Normalize();
    }

    public static PackManifest Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"找不到整合包清单：{path}");
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"pack.json 格式不对：{ex.Message}", ex);
        }
    }

    public static PackManifest? TryLoad(string path)
    {
        try
        {
            return File.Exists(path) ? Load(path) : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, ToJson());
    }

    // 把导入器写下的 v0 记录升级成 v1 清单（哈希先留空，第一次同步时补齐）。
    public static PackManifest FromRecord(PackRecord record)
    {
        var entries = record.Files
            .Select(NormalizePath)
            .Where(path => path.Length > 0)
            .Select(path => new PackFileEntry(path, string.Empty, 0))
            .ToList();

        return new PackManifest
        {
            PackName = record.PackName,
            PackVersion = record.PackVersion,
            SourcePath = record.SourcePath,
            Files = entries.Select(entry => entry.Path).ToList(),
            Entries = entries,
        };
    }

    // 给实例里已有的 pack.json 补哈希：只算缺哈希的文件，已存在且大小不变的跳过。
    public PackManifest FillHashes(string instanceDir, Action<string>? log = null)
    {
        var entries = new List<PackFileEntry>(Entries.Count);
        foreach (PackFileEntry entry in AllEntries)
        {
            string full = Path.Combine(instanceDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (entry.HasHash)
            {
                entries.Add(entry);
                continue;
            }

            if (!File.Exists(full))
            {
                log?.Invoke($"[清单] 本机缺少 {entry.Path}，同步时会重新下载");
                entries.Add(entry);
                continue;
            }

            var info = new FileInfo(full);
            entries.Add(entry with { Sha1 = Hasher.Sha1Of(full), Size = info.Length });
        }

        PackManifest filled = (this with { Entries = entries, Files = entries.Select(e => e.Path).ToList() }).Normalize();
        filled.Save(PathFor(instanceDir));
        return filled;
    }

    // 记到 instances.json 上的同步来源，方便下次"再点一次同步"。
    public static PackManifest? LoadForInstance(string installRoot, Instance instance)
    {
        string dir = InstanceStore.DirFor(installRoot, instance.Name);
        PackManifest? manifest = TryLoad(PathFor(dir));
        if (manifest is null)
        {
            return null;
        }

        // 实例记录上的服务器绑定优先，清单里的作为兜底。
        return instance.ServerAddress is { Length: > 0 } address
            ? manifest with
            {
                Server = new PackServerInfo(address, instance.ServerPort ?? 25565),
                Loader = manifest.Loader ?? instance.Loader,
                LoaderVersion = manifest.LoaderVersion ?? instance.LoaderVersion,
                MinecraftVersion = manifest.MinecraftVersion ?? instance.VersionId,
            }
            : manifest with
            {
                Loader = manifest.Loader ?? instance.Loader,
                LoaderVersion = manifest.LoaderVersion ?? instance.LoaderVersion,
                MinecraftVersion = manifest.MinecraftVersion ?? instance.VersionId,
            };
    }
}
