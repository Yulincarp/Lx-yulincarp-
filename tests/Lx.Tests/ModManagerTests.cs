using System.IO.Compression;
using Lx.Probe.Mods;

namespace Lx.Tests;

public class ModManagerTests
{
    private static string TempGameDir()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "mods-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ModManager.ModsDir(dir));
        return dir;
    }

    private static string WriteMod(string dir, string fileName, string modJson)
    {
        string path = Path.Combine(dir, fileName);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        ZipArchiveEntry entry = zip.CreateEntry("fabric.mod.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(modJson);
        return path;
    }

    [Fact]
    public void List_ReadsFabricMetadata()
    {
        string gameDir = TempGameDir();
        try
        {
            WriteMod(ModManager.ModsDir(gameDir), "sodium.jar",
                """{"id":"sodium","name":"Sodium","version":"0.5.8"}""");

            List<ModFile> mods = ModManager.List(gameDir);

            ModFile mod = Assert.Single(mods);
            Assert.True(mod.Enabled);
            Assert.Equal("sodium", mod.Id);
            Assert.Equal("Sodium", mod.Name);
            Assert.Equal("0.5.8", mod.Version);
        }
        finally
        {
            Directory.Delete(gameDir, recursive: true);
        }
    }

    [Fact]
    public void SetEnabled_RenamesBothWays()
    {
        string gameDir = TempGameDir();
        try
        {
            WriteMod(ModManager.ModsDir(gameDir), "sodium.jar", """{"id":"sodium"}""");

            Assert.True(ModManager.SetEnabled(gameDir, "sodium.jar", false));
            Assert.False(ModManager.List(gameDir)[0].Enabled);
            Assert.Equal("sodium.jar.disabled", ModManager.List(gameDir)[0].FileName);

            Assert.True(ModManager.SetEnabled(gameDir, "sodium.jar", true));
            Assert.True(ModManager.List(gameDir)[0].Enabled);
        }
        finally
        {
            Directory.Delete(gameDir, recursive: true);
        }
    }

    [Fact]
    public void AddAndRemove_WorkAndRejectDuplicates()
    {
        string gameDir = TempGameDir();
        string source = Path.Combine(gameDir, "source");
        Directory.CreateDirectory(source);
        try
        {
            string jar = WriteMod(source, "lithium.jar", """{"id":"lithium"}""");

            ModManager.Add(gameDir, jar);
            Assert.Single(ModManager.List(gameDir));
            Assert.Throws<InvalidOperationException>(() => ModManager.Add(gameDir, jar));

            Assert.True(ModManager.Remove(gameDir, "lithium.jar"));
            Assert.Empty(ModManager.List(gameDir));
        }
        finally
        {
            Directory.Delete(gameDir, recursive: true);
        }
    }
}
