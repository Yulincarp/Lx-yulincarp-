using Lx.Probe.Instances;

namespace Lx.Tests;

public class InstanceServiceTests
{
    private static string TempRoot()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "svc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(string root, string instance, string relative, string content)
    {
        string path = Path.Combine(root, "instances", instance, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static bool Exists(string root, string instance, string relative) =>
        File.Exists(Path.Combine(root, "instances", instance, relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Rename_MovesDirectory_AndRecord()
    {
        string root = TempRoot();
        try
        {
            InstanceStore.AddOrUpdate(root, new Instance("旧名", "1.20.4", "fabric", "S", "0.15.11"));
            Write(root, "旧名", "mods/a.jar", "A");
            Write(root, "旧名", "saves/w/level.dat", "W");

            Instance renamed = InstanceService.Rename(root, "旧名", "新名");

            Assert.Equal("新名", renamed.Name);
            Assert.False(Directory.Exists(InstanceStore.DirFor(root, "旧名")));
            Assert.True(Exists(root, "新名", "mods/a.jar"));
            Assert.True(Exists(root, "新名", "saves/w/level.dat")); // 改名要连存档一起带走
            Assert.Equal("新名", InstanceStore.Load(root).Single().Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rename_RejectsDuplicateEmptyAndPathChars()
    {
        string root = TempRoot();
        try
        {
            InstanceStore.AddOrUpdate(root, new Instance("A", "1.20.4", null, null));
            InstanceStore.AddOrUpdate(root, new Instance("B", "1.20.4", null, null));

            Assert.Throws<InvalidOperationException>(() => InstanceService.Rename(root, "A", "B"));
            Assert.Throws<InvalidOperationException>(() => InstanceService.Rename(root, "A", "  "));
            Assert.Throws<InvalidOperationException>(() => InstanceService.Rename(root, "A", "a/b"));
            Assert.Throws<InvalidOperationException>(() => InstanceService.Rename(root, "不存在", "C"));

            // 改成同一个名字（大小写不同）应当原样返回
            Instance same = InstanceService.Rename(root, "A", "a");
            Assert.Equal("A", same.Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Copy_SkipsSavesSnapshotsAndLogs()
    {
        string root = TempRoot();
        try
        {
            InstanceStore.AddOrUpdate(root, new Instance("源", "1.20.4", "fabric", "S", "0.15.11",
                PackUrl: "https://host/pack.json", ServerAddress: "mc.example.com", ServerPort: 25566));

            Write(root, "源", "mods/a.jar", "A");
            Write(root, "源", "config/x.toml", "cfg");
            Write(root, "源", "saves/w/level.dat", "W");
            Write(root, "源", ".lx-snapshots/s1/snapshot.json", "{}");
            Write(root, "源", "logs/latest.log", "log");
            Write(root, "源", "crash-reports/crash.txt", "crash");

            Instance clone = InstanceService.Copy(root, "源", "副本");

            Assert.Equal("副本", clone.Name);
            Assert.Equal("https://host/pack.json", clone.PackUrl);
            Assert.Equal("mc.example.com", clone.ServerAddress);

            Assert.True(Exists(root, "副本", "mods/a.jar"));
            Assert.True(Exists(root, "副本", "config/x.toml"));
            Assert.False(Exists(root, "副本", "saves/w/level.dat"));
            Assert.False(Exists(root, "副本", ".lx-snapshots/s1/snapshot.json"));
            Assert.False(Exists(root, "副本", "logs/latest.log"));
            Assert.False(Exists(root, "副本", "crash-reports/crash.txt"));

            // 原实例不能被削
            Assert.True(Exists(root, "源", "saves/w/level.dat"));
            Assert.Equal(2, InstanceStore.Load(root).Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Copy_CanIncludeSaves_AndRejectsExistingTarget()
    {
        string root = TempRoot();
        try
        {
            InstanceStore.AddOrUpdate(root, new Instance("源", "1.20.4", null, null));
            Write(root, "源", "mods/a.jar", "A");
            Write(root, "源", "saves/w/level.dat", "W");

            InstanceService.Copy(root, "源", "带存档", includeSaves: true);
            Assert.True(Exists(root, "带存档", "saves/w/level.dat"));

            Assert.Throws<InvalidOperationException>(() => InstanceService.Copy(root, "源", "带存档"));
            Assert.Throws<InvalidOperationException>(() => InstanceService.Copy(root, "不存在", "X"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Delete_RecordOnly_KeepsFiles_AndWithFiles_RemovesDirectory()
    {
        string root = TempRoot();
        try
        {
            InstanceStore.AddOrUpdate(root, new Instance("保留", "1.20.4", null, null));
            InstanceStore.AddOrUpdate(root, new Instance("清空", "1.20.4", null, null));
            Write(root, "保留", "mods/a.jar", "A");
            Write(root, "清空", "mods/b.jar", "B");

            Assert.True(InstanceService.Delete(root, "保留", deleteFiles: false));
            Assert.True(Directory.Exists(InstanceStore.DirFor(root, "保留"))); // 只删记录，目录必须还在
            Assert.False(InstanceService.Delete(root, "保留", deleteFiles: false)); // 第二次找不到记录

            Assert.True(InstanceService.Delete(root, "清空", deleteFiles: true));
            Assert.False(Directory.Exists(InstanceStore.DirFor(root, "清空")));

            Assert.Empty(InstanceStore.Load(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
