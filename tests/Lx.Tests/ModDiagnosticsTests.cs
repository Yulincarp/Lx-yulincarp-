using System.IO.Compression;
using System.Text;
using Lx.Probe.Mods;

namespace Lx.Tests;

public class ModDiagnosticsTests
{
    [Fact]
    public void Scan_FindsDuplicateModId()
    {
        var mods = new[]
        {
            new ModFile("sodium-fabric.jar", "sodium", "Sodium", "1", true, 10),
            new ModFile("sodium-neoforge.jar", "sodium", "Sodium", "2", true, 20),
        };

        List<ModIssue> issues = ModDiagnostics.Scan(mods);

        ModIssue issue = Assert.Single(issues);
        Assert.Equal("duplicate", issue.Kind);
        Assert.Contains("sodium", issue.Message);
        Assert.Equal(2, issue.Files.Count);
    }

    [Fact]
    public void Scan_FindsMissingDependency()
    {
        var mods = new[]
        {
            new ModFile("iris.jar", "iris", "Iris", "1", true, 10, "fabric", new[] { "fabric-api" }),
        };

        List<ModIssue> issues = ModDiagnostics.Scan(mods);

        ModIssue issue = Assert.Single(issues);
        Assert.Equal("missing-dependency", issue.Kind);
        Assert.Contains("fabric-api", issue.Message);
    }

    [Fact]
    public void Scan_IgnoresInstalledAndRuntimeDependencies()
    {
        var mods = new[]
        {
            new ModFile("iris.jar", "iris", "Iris", "1", true, 10, "fabric", new[] { "fabric-api", "minecraft" }),
            new ModFile("fabric-api.jar", "fabric-api", "Fabric API", "1", true, 20),
        };

        Assert.Empty(ModDiagnostics.Scan(mods));
    }

    [Fact]
    public void ModManager_ReadsNeoForgeMetadata()
    {
        string gameDir = Path.Combine(AppContext.BaseDirectory, "modmeta-" + Guid.NewGuid().ToString("N"));
        string modsDir = ModManager.ModsDir(gameDir);
        Directory.CreateDirectory(modsDir);
        try
        {
            string jar = Path.Combine(modsDir, "sodium-neoforge.jar");
            using (ZipArchive zip = ZipFile.Open(jar, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = zip.CreateEntry("META-INF/neoforge.mods.toml");
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write("modLoader=\"javafml\"\n[[mods]]\nmodId=\"sodium\"\ndisplayName=\"Sodium\"\nversion=\"1.0\"\n");
            }

            ModFile mod = Assert.Single(ModManager.List(gameDir));
            Assert.Equal("sodium", mod.Id);
            Assert.Equal("Sodium", mod.Name);
            Assert.Equal("neoforge", mod.Loader);
        }
        finally
        {
            try { Directory.Delete(gameDir, recursive: true); } catch (IOException) { }
        }
    }
}
