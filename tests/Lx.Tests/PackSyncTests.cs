using System.Text.Json;
using Lx.Probe.Instances;
using Lx.Probe.Sync;

namespace Lx.Tests;

public class PackSyncTests
{
    private static string TempDir(string label)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, $"sync-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Read(string root, string relative) =>
        File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Manifest_ReadsLegacyPackJson_AndKeepsFilesList()
    {
        string dir = TempDir("legacy");
        try
        {
            File.WriteAllText(PackManifest.PathFor(dir), """
            {
              "SourcePath": "C:\\packs\\pve.zip",
              "PackName": "栖月港",
              "PackVersion": "1.2",
              "Files": ["mods/a.jar", "config/x.toml"]
            }
            """);

            PackManifest manifest = PackManifest.Load(PackManifest.PathFor(dir))!;

            Assert.True(manifest.IsLegacy);
            Assert.Equal("栖月港", manifest.PackName);
            Assert.Equal(2, manifest.AllPaths.Count());
            Assert.Equal(2, manifest.Files.Count);
            Assert.All(manifest.Entries, entry => Assert.False(entry.HasHash));
            Assert.Equal(PackManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Manifest_Save_RoundTripsEntriesAndServer()
    {
        string dir = TempDir("roundtrip");
        try
        {
            var manifest = new PackManifest
            {
                PackName = "PVE",
                PackVersion = "2.0",
                MinecraftVersion = "1.20.4",
                Loader = "fabric",
                LoaderVersion = "0.15.11",
                Server = new PackServerInfo("mc.example.com", 25566),
                Entries = new List<PackFileEntry> { new("mods/a.jar", "abc", 12) },
            }.Normalize();

            manifest.Save(PackManifest.PathFor(dir));
            PackManifest loaded = PackManifest.Load(PackManifest.PathFor(dir));

            Assert.Equal("mc.example.com", loaded.Server?.Address);
            Assert.Equal(25566, loaded.Server?.Port);
            Assert.Equal(new[] { "mods/a.jar" }, loaded.Files);
            Assert.Equal("abc", loaded.Entries.Single().Sha1);

            // 旧读取器（导入记录）仍能读懂
            var record = JsonSerializer.Deserialize<Lx.Probe.Modpacks.PackRecord>(
                File.ReadAllText(PackManifest.PathFor(dir)),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.Equal("PVE", record?.PackName);
            Assert.Single(record!.Files);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Manifest_RejectsTraversalAbsoluteAndFutureSchema()
    {
        Assert.Throws<InvalidOperationException>(() => PackManifest.NormalizePath("../../../windows/system32/x.dll"));
        Assert.Throws<InvalidOperationException>(() => PackManifest.NormalizePath("C:/windows/x.dll"));
        Assert.Equal("mods/a.jar", PackManifest.NormalizePath("./mods\\a.jar"));

        Assert.Throws<InvalidOperationException>(() => PackManifest.Parse("""{"SchemaVersion":99,"PackName":"x"}"""));
    }

    [Fact]
    public void Filter_IncludesPackFiles_ButNeverPlayerData()
    {
        var filter = new PackFilter();

        Assert.True(filter.IsIncluded("mods/sodium.jar"));
        Assert.True(filter.IsIncluded("config/create.toml"));
        Assert.True(filter.IsIncluded("kubejs/server_scripts/x.js"));

        Assert.False(filter.IsIncluded("saves/世界/level.dat"));
        Assert.False(filter.IsIncluded("options.txt"));
        Assert.False(filter.IsIncluded("logs/latest.log"));
        Assert.False(filter.IsIncluded("pack.json"));
        Assert.False(filter.IsIncluded(".lx-snapshots/20260101/snapshot.json"));
        Assert.False(filter.IsIncluded("mods/a.jar.bak"));
        Assert.False(filter.IsIncluded("server.properties"));
    }

    [Fact]
    public void Export_WritesManifestWithHashes_AndRemovesStaleFiles()
    {
        string instance = TempDir("instance");
        string output = TempDir("host");
        try
        {
            Write(instance, "mods/a.jar", "AAA");
            Write(instance, "config/x.toml", "cfg");
            Write(instance, "options.txt", "player-only");
            Write(instance, "saves/w/level.dat", "world");

            PackExportResult first = PackExporter.Export(instance, output, new PackExportOptions(
                PackName: "PVE",
                PackVersion: "1",
                MinecraftVersion: "1.20.4",
                Loader: "fabric",
                LoaderVersion: "0.15.11",
                Server: new PackServerInfo("mc.example.com")));

            Assert.Equal(2, first.FileCount);
            Assert.True(File.Exists(Path.Combine(output, "mods", "a.jar")));
            Assert.False(File.Exists(Path.Combine(output, "options.txt")));
            Assert.False(File.Exists(Path.Combine(output, "saves", "w", "level.dat")));

            PackManifest manifest = PackManifest.Load(first.ManifestPath);
            Assert.All(manifest.Entries, entry => Assert.True(entry.HasHash));
            Assert.Equal("AAA", File.ReadAllText(Path.Combine(output, "mods", "a.jar")));

            // 第二次导出：删掉 a.jar，输出目录里的旧副本也要没
            File.Delete(Path.Combine(instance, "mods", "a.jar"));
            PackExportResult second = PackExporter.Export(instance, output, new PackExportOptions(PackName: "PVE", PackVersion: "2"));

            Assert.Equal(1, second.FileCount);
            Assert.False(File.Exists(Path.Combine(output, "mods", "a.jar")));
        }
        finally
        {
            Directory.Delete(instance, recursive: true);
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public async Task Sync_AppliesIncrementalChanges_FromLocalDirectory()
    {
        string author = TempDir("author");
        string host = TempDir("host");
        string player = TempDir("player");
        try
        {
            Write(author, "mods/a.jar", "A1");
            Write(author, "mods/b.jar", "B1");
            Write(author, "config/x.toml", "cfg1");
            PackExporter.Export(author, host, new PackExportOptions(PackName: "PVE", PackVersion: "1"));

            // 玩家本地：a 一样、b 旧、多一个后来被删掉的 c
            Write(player, "mods/a.jar", "A1");
            Write(player, "mods/b.jar", "B-old");
            Write(player, "mods/c.jar", "C");
            Write(player, "pack.json", new PackManifest
            {
                PackName = "PVE",
                PackVersion = "0",
                Entries = new List<PackFileEntry>
                {
                    new("mods/a.jar", "x", 0),
                    new("mods/b.jar", "x", 0),
                    new("mods/c.jar", "x", 0),
                },
            }.ToJson());

            // 作者改 2 个 mod 并删 1 个
            Write(author, "mods/b.jar", "B2");
            Write(author, "config/x.toml", "cfg2");
            File.Delete(Path.Combine(author, "mods", "b.jar.bak"));
            PackExporter.Export(author, host, new PackExportOptions(PackName: "PVE", PackVersion: "2"));

            PackManifest remote = await PackSync.LoadRemoteAsync(PackManifest.PathFor(host));
            PackSyncPlan plan = PackSync.Plan(player, remote, host);

            // a 未变；b、config 要下；c 要删
            Assert.Equal(2, plan.Download.Count);
            Assert.Equal(new[] { "mods/c.jar" }, plan.Delete);
            Assert.Equal(1, plan.Unchanged);

            PackSyncResult result = await PackSync.SyncAsync(player, PackManifest.PathFor(host));

            Assert.True(result.Success, result.Summary);
            Assert.Equal("B2", Read(player, "mods/b.jar"));
            Assert.Equal("cfg2", Read(player, "config/x.toml"));
            Assert.False(File.Exists(Path.Combine(player, "mods", "c.jar")));
            Assert.Equal("2", PackManifest.Load(PackManifest.PathFor(player)).PackVersion);
            Assert.NotNull(result.SnapshotId);

            // 再同步一次：应当零差异
            PackSyncPlan again = PackSync.Plan(player, remote with { PackVersion = "2" }, host);
            Assert.Empty(again.Download);
            Assert.Empty(again.Delete);
        }
        finally
        {
            Directory.Delete(author, recursive: true);
            Directory.Delete(host, recursive: true);
            Directory.Delete(player, recursive: true);
        }
    }

    [Fact]
    public async Task Sync_Failure_LeavesSnapshot_ThatRollsBackExactly()
    {
        string author = TempDir("author2");
        string host = TempDir("host2");
        string player = TempDir("player2");
        try
        {
            Write(author, "mods/a.jar", "A1");
            Write(author, "config/x.toml", "cfg1");
            PackExporter.Export(author, host, new PackExportOptions(PackName: "PVE", PackVersion: "1"));

            Write(player, "mods/a.jar", "A-old");
            Write(player, "config/x.toml", "cfg-old");
            Write(player, "pack.json", new PackManifest
            {
                PackName = "PVE",
                PackVersion = "0",
                Entries = new List<PackFileEntry>
                {
                    new("mods/a.jar", "x", 0),
                    new("config/x.toml", "x", 0),
                },
            }.ToJson());

            // 故意把托管目录里的文件删掉：下载/复制会失败
            File.Delete(Path.Combine(host, "config", "x.toml"));

            string before = File.ReadAllText(Path.Combine(player, "mods", "a.jar"));

            PackSyncResult result = await PackSync.SyncAsync(player, PackManifest.PathFor(host));

            Assert.False(result.Success);
            Assert.True(result.Failed > 0);
            Assert.Equal("0", PackManifest.Load(PackManifest.PathFor(player)).PackVersion);

            int restored = PackSync.Rollback(player, result.SnapshotId);

            Assert.True(restored > 0);
            Assert.Equal("A-old", File.ReadAllText(Path.Combine(player, "mods", "a.jar")));
            Assert.Equal("cfg-old", Read(player, "config/x.toml"));
            Assert.Equal(before, File.ReadAllText(Path.Combine(player, "mods", "a.jar")));

            SyncSnapshot snapshot = SyncSnapshotStore.Latest(player)!;
            Assert.Equal(0, SyncSnapshotStore.Verify(player, snapshot));
        }
        finally
        {
            Directory.Delete(author, recursive: true);
            Directory.Delete(host, recursive: true);
            Directory.Delete(player, recursive: true);
        }
    }

    [Fact]
    public void Snapshot_ListAndRollback_HandlesNewFiles()
    {
        string dir = TempDir("snap");
        try
        {
            Write(dir, "mods/a.jar", "old");
            SyncSnapshot snapshot = SyncSnapshotStore.Create(dir, new[] { "mods/a.jar", "mods/new.jar" }, null);

            Write(dir, "mods/a.jar", "new");
            Write(dir, "mods/new.jar", "created during sync");

            SyncSnapshotStore.Rollback(dir, snapshot.Id);

            Assert.Equal("old", Read(dir, "mods/a.jar"));
            Assert.False(File.Exists(Path.Combine(dir, "mods", "new.jar")));
            Assert.Single(SyncSnapshotStore.List(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveUrl_EscapesSegments_AndPrefersExplicitUrl()
    {
        var entry = new PackFileEntry("mods/中文 名.jar", "x", 1);

        string url = PackSync.ResolveUrl("https://host/pack/", entry);
        Assert.Equal("https://host/pack/mods/%E4%B8%AD%E6%96%87%20%E5%90%8D.jar", url);

        var explicitUrl = new PackFileEntry("mods/a.jar", "x", 1, "https://cdn.example.com/a.jar");
        Assert.Equal("https://cdn.example.com/a.jar", PackSync.ResolveUrl("https://host/pack", explicitUrl));
    }

    [Fact]
    public void ServerInfo_ParsesForms_AndBuildsGameArgs()
    {
        Assert.Equal(new PackServerInfo("mc.example.com"), PackServerInfo.Parse("mc.example.com"));
        Assert.Equal(new PackServerInfo("mc.example.com", 25566), PackServerInfo.Parse(" mc.example.com:25566 "));
        Assert.Equal(new PackServerInfo("::1", 25565), PackServerInfo.Parse("[::1]"));
        Assert.Throws<InvalidOperationException>(() => PackServerInfo.Parse("  "));

        Assert.Equal(
            new[] { "--server", "mc.example.com", "--port", "25566" },
            PackServerInfo.Parse("mc.example.com:25566").ToGameArgs());
    }

    [Fact]
    public void Instance_RoundTripsServerBinding()
    {
        string root = TempDir("inst");
        try
        {
            var instance = new Instance("PVE", "1.20.4", "fabric", "Steve", "0.15.11",
                PackUrl: "https://host/pack.json", ServerAddress: "mc.example.com", ServerPort: 25566);
            InstanceStore.AddOrUpdate(root, instance);

            Instance loaded = InstanceStore.Load(root).Single();
            Assert.Equal("https://host/pack.json", loaded.PackUrl);
            Assert.Equal("mc.example.com", loaded.ServerAddress);
            Assert.Equal(25566, loaded.ServerPort);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
