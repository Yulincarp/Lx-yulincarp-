using System.IO.Compression;
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
