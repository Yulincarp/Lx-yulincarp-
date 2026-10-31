using System.Text.Json;
using Lx.Probe.Resolve;

namespace Lx.Probe.Cache;

// 一条缓存记录：文件大小 + 修改时间 + sha1。大小和 mtime 都没变就认为内容没变。
public sealed record HashEntry(long Size, long Ticks, string Sha1);

// 文件 sha1 的磁盘缓存：<数据目录>/cache/hashes.json。
//
// 为什么需要：每次启动都要确认 3000+ 个库/资源文件没被动过，全量算 sha1 要十几秒。
// 用「大小 + mtime」当指纹，命中就直接用上次的 sha1，只有变过或没记录的文件才重算。
public sealed class FileHashCache
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly string _indexPath;
    private readonly Dictionary<string, HashEntry> _entries;
    private readonly object _gate = new();
    private bool _dirty;

    private FileHashCache(string indexPath, Dictionary<string, HashEntry> entries)
    {
        _indexPath = indexPath;
        _entries = entries;
    }

    public int Hits { get; private set; }

    public int Misses { get; private set; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public static string PathFor(string installRoot) => Path.Combine(installRoot, "cache", "hashes.json");

    public static FileHashCache Load(string installRoot)
    {
        string path = PathFor(installRoot);
        Dictionary<string, HashEntry> entries = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (File.Exists(path))
            {
                Dictionary<string, HashEntry>? loaded =
                    JsonSerializer.Deserialize<Dictionary<string, HashEntry>>(File.ReadAllText(path), Options);

                if (loaded is not null)
                {
                    foreach (KeyValuePair<string, HashEntry> pair in loaded)
                    {
                        entries[pair.Key] = pair.Value;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 缓存坏了就当没有，下次重新建
            entries.Clear();
        }

        return new FileHashCache(path, entries);
    }

    // 和 Hasher.Sha1Of 一样返回 sha1，但命中缓存时不读文件内容。
    public string Sha1Of(string file)
    {
        var info = new FileInfo(file);
        long size = info.Length;
        long ticks = info.LastWriteTimeUtc.Ticks;
        string key = Key(file);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out HashEntry? cached) &&
                cached.Size == size &&
                cached.Ticks == ticks)
            {
                Hits++;
                return cached.Sha1;
            }
        }

        string sha1 = Hasher.Sha1Of(file);
        lock (_gate)
        {
            _entries[key] = new HashEntry(size, ticks, sha1);
            _dirty = true;
            Misses++;
        }

        return sha1;
    }

    // 下载/复制之后已知 sha1：直接记账，省一次重算。
    public void Remember(string file, string sha1)
    {
        try
        {
            var info = new FileInfo(file);
            lock (_gate)
            {
                _entries[Key(file)] = new HashEntry(info.Length, info.LastWriteTimeUtc.Ticks, sha1);
                _dirty = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 记不上就算，下次重算
        }
    }

    public void Save()
    {
        if (!_dirty)
        {
            return;
        }

        Dictionary<string, HashEntry> snapshot;
        lock (_gate)
        {
            snapshot = new Dictionary<string, HashEntry>(_entries, StringComparer.OrdinalIgnoreCase);
            _dirty = false;
        }

        // 已经不存在的文件没必要留在索引里
        foreach (string key in snapshot.Keys.ToList())
        {
            if (!File.Exists(key))
            {
                snapshot.Remove(key);
            }
        }

        try
        {
            string dir = Path.GetDirectoryName(_indexPath)!;
            Directory.CreateDirectory(dir);
            string temp = _indexPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, Options));

            // 先写临时文件再替换，进程中途被杀也不会留半截索引
            File.Move(temp, _indexPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不了缓存不影响启动，下次重算而已
        }
    }

    private static string Key(string file) => Path.GetFullPath(file).ToLowerInvariant();
}
