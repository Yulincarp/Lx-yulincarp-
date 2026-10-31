using Lx.Probe.Cache;

namespace Lx.Tests;

public class FileHashCacheTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteFile(string dir, string name, string content)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void SecondLookup_HitsCache_WithoutRecomputing()
    {
        string dir = TempDir();
        try
        {
            string file = WriteFile(dir, "a.bin", "hello");

            var cache = FileHashCache.Load(dir);
            string first = cache.Sha1Of(file);
            string second = cache.Sha1Of(file);

            Assert.Equal(first, second);
            Assert.Equal(1, cache.Hits);
            Assert.Equal(1, cache.Misses);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ChangedContent_OrTimestamp_InvalidatesCache()
    {
        string dir = TempDir();
        try
        {
            string file = WriteFile(dir, "a.bin", "hello");
            var cache = FileHashCache.Load(dir);
            string before = cache.Sha1Of(file);
            Assert.Equal(0, cache.Hits);
            Assert.Equal(1, cache.Misses);

            // 内容变、大小也变：必须重算
            File.WriteAllText(file, "hello world");
            string after = cache.Sha1Of(file);
            Assert.NotEqual(before, after);
            Assert.Equal(2, cache.Misses);

            // 内容不变、只改 mtime：大小一样但时间戳变了，也要重算（值当然相同）
            File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddSeconds(5));
            string touched = cache.Sha1Of(file);
            Assert.Equal(after, touched);
            Assert.Equal(3, cache.Misses);
            Assert.Equal(0, cache.Hits);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SaveAndReload_KeepsHitsAcrossRuns()
    {
        string dir = TempDir();
        try
        {
            string file = WriteFile(dir, "b.bin", "persist me");

            var cache = FileHashCache.Load(dir);
            string sha1 = cache.Sha1Of(file);
            cache.Save();

            FileHashCache reloaded = FileHashCache.Load(dir);
            string again = reloaded.Sha1Of(file);

            Assert.Equal(sha1, again);
            Assert.Equal(1, reloaded.Hits);
            Assert.Equal(0, reloaded.Misses);
            Assert.True(File.Exists(FileHashCache.PathFor(dir)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Remember_SkipsHashing_KnownFile()
    {
        string dir = TempDir();
        try
        {
            string file = WriteFile(dir, "c.bin", "downloaded");
            var cache = FileHashCache.Load(dir);

            cache.Remember(file, "0123456789abcdef0123456789abcdef01234567");
            string sha1 = cache.Sha1Of(file);

            Assert.Equal("0123456789abcdef0123456789abcdef01234567", sha1);
            Assert.Equal(1, cache.Hits);
            Assert.Equal(0, cache.Misses);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CorruptIndex_IsIgnored()
    {
        string dir = TempDir();
        try
        {
            string index = FileHashCache.PathFor(dir);
            Directory.CreateDirectory(Path.GetDirectoryName(index)!);
            File.WriteAllText(index, "{ not json");

            var cache = FileHashCache.Load(dir);
            string file = WriteFile(dir, "d.bin", "x");

            Assert.NotNull(cache.Sha1Of(file));
            Assert.Equal(1, cache.Misses);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
