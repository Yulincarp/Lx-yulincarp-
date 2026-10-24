using System.IO.Compression;
using Lx.Probe.Instances;
using Lx.Probe.Modpacks;

namespace Lx.Tests;

public class ModpackTests
{
    private static string MakePack(string dir, string indexJson)
    {
        string path = Path.Combine(dir, "pack.mrpack");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        ZipArchiveEntry index = zip.CreateEntry("modrinth.index.json");
        using (var writer = new StreamWriter(index.Open()))
        {
            writer.Write(indexJson);
        }

        ZipArchiveEntry overrideEntry = zip.CreateEntry("overrides/config/test.txt");
        using (var writer = new StreamWriter(overrideEntry.Open()))
        {
            writer.Write("hello");
        }

        return path;
    }

    [Fact]
    public void ReadIndex_ParsesDependenciesFilesAndOverrides()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = MakePack(dir, """
                {"formatVersion":1,"game":"minecraft","versionId":"2.0.0","name":"测试包",
                 "files":[{"path":"mods/a.jar","hashes":{"sha1":"abc"},"env":{"client":"required"},
                           "downloads":["https://example.com/a.jar"],"fileSize":100}],
                 "dependencies":{"minecraft":"1.20.4","fabric-loader":"0.20.1"}}
                """);

            MrpackIndex index = MrpackInstaller.ReadIndex(path);

            Assert.Equal("测试包", index.Name);
            Assert.Equal("2.0.0", index.VersionId);
            Assert.Equal("1.20.4", index.MinecraftVersion);
            Assert.Equal("0.20.1", index.FabricLoaderVersion);
            Assert.Single(index.Files);
            Assert.Equal("mods/a.jar", index.Files[0].Path);
            Assert.Contains(index.OverrideEntries, entry => entry == "overrides/config/test.txt");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReadIndex_RejectsNonMrpack()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "bad.mrpack");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                zip.CreateEntry("readme.txt");
            }

            Assert.Throws<InvalidOperationException>(() => MrpackInstaller.ReadIndex(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class PackImporterTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "packs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    [Fact]
    public void DetectKind_RecognizesModrinthCurseForgeAndGeneric()
    {
        string dir = TempDir();
        try
        {
            string modrinth = Path.Combine(dir, "a.mrpack");
            using (var zip = ZipFile.Open(modrinth, ZipArchiveMode.Create))
            {
                AddEntry(zip, "modrinth.index.json", "{}");
            }

            string curseforge = Path.Combine(dir, "b.zip");
            using (var zip = ZipFile.Open(curseforge, ZipArchiveMode.Create))
            {
                AddEntry(zip, "manifest.json", "{}");
                AddEntry(zip, "overrides/config/x.txt", "x");
            }

            string generic = Path.Combine(dir, "c.zip");
            using (var zip = ZipFile.Open(generic, ZipArchiveMode.Create))
            {
                AddEntry(zip, "mods/a.jar", "x");
            }

            Assert.Equal(PackKind.Modrinth, PackImporter.DetectKind(modrinth));
            Assert.Equal(PackKind.CurseForge, PackImporter.DetectKind(curseforge));
            Assert.Equal(PackKind.Generic, PackImporter.DetectKind(generic));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ImportGeneric_StripsSingleRootAndGuessesVersion()
    {
        string dir = TempDir();
        string installRoot = Path.Combine(dir, "install");
        try
        {
            string pack = Path.Combine(dir, "pack.zip");
            using (var zip = ZipFile.Open(pack, ZipArchiveMode.Create))
            {
                AddEntry(zip, "MyPack/fabric.mod.json", "{\"id\":\"x\",\"depends\":{\"minecraft\":\"1.20.1\"}}");
            }

            // 把 fabric.mod.json 放进 mods/ 才会有版本猜测
            string pack2 = Path.Combine(dir, "pack2.zip");
            using (var zip = ZipFile.Open(pack2, ZipArchiveMode.Create))
            {
                using Stream entryStream = zip.CreateEntry("MyPack/mods/example.jar").Open();
                using var inner = new ZipArchive(entryStream, ZipArchiveMode.Create, leaveOpen: true);
                AddEntry(inner, "fabric.mod.json", "{\"id\":\"example\",\"depends\":{\"minecraft\":\"1.20.1\"}}");
            }

            PackImportResult result = await PackImporter.ImportAsync(pack2, installRoot, _ => { });
            Instance imported = result.Instance;

            Assert.Equal("MyPack", imported.Name);
            Assert.Equal("1.20.1", imported.VersionId);
            Assert.Equal("fabric", imported.Loader);
            Assert.True(File.Exists(Path.Combine(installRoot, "instances", "MyPack", "mods", "example.jar")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
public class CurseForgeKeyTests
{
    [Fact]
    public void SaveKey_EncryptsWithDpapiOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(AppContext.BaseDirectory, "cfkey-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CurseForgeClient.SaveKey(root, "secret-123");

            string raw = File.ReadAllText(CurseForgeClient.KeyFile(root));
            Assert.StartsWith("dpapi:", raw);
            Assert.DoesNotContain("secret-123", raw);
            Assert.Equal("secret-123", CurseForgeClient.ResolveKey(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveKey_PrefersOverrideThenFile()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "cfkey-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CurseForgeClient.SaveKey(root, "file-key");
            Assert.Equal("file-key", CurseForgeClient.ResolveKey(root));
            Assert.Equal("override", CurseForgeClient.ResolveKey(root, " override "));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}