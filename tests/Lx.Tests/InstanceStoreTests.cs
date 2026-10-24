using Lx.Probe.Instances;

namespace Lx.Tests;

public class InstanceStoreTests
{
    private static string TempRoot()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "inst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void AddOrUpdate_RoundTripsAndReplacesSameName()
    {
        string root = TempRoot();
        try
        {
            InstanceStore.AddOrUpdate(root, new Instance("生存", "1.20.4", "fabric", "Yulin_carp"));
            InstanceStore.AddOrUpdate(root, new Instance("生电", "1.20.4", null, "Steve"));
            InstanceStore.AddOrUpdate(root, new Instance("生存", "1.21", null, "Steve"));

            List<Instance> all = InstanceStore.Load(root);

            Assert.Equal(2, all.Count);
            Instance survival = all.Single(i => i.Name == "生存");
            Assert.Equal("1.21", survival.VersionId);
            Assert.True(Directory.Exists(InstanceStore.DirFor(root, "生电")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Remove_DeletesEntryButKeepsFolder()
    {
        string root = TempRoot();
        try
        {
            InstanceStore.AddOrUpdate(root, new Instance("生存", "1.20.4", null, null));

            Assert.True(InstanceStore.Remove(root, "生存"));
            Assert.Empty(InstanceStore.Load(root));
            Assert.True(Directory.Exists(InstanceStore.DirFor(root, "生存")));
            Assert.False(InstanceStore.Remove(root, "不存在"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidateName_RejectsEmptyAndPathChars()
    {
        Assert.Throws<InvalidOperationException>(() => InstanceStore.ValidateName("  "));
        Assert.Throws<InvalidOperationException>(() => InstanceStore.ValidateName("a/b"));
        Assert.Equal("生存", InstanceStore.ValidateName(" 生存 "));
    }
}
