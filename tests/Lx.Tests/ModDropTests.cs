using Lx.Probe.Mods;

namespace Lx.Tests;

public class ModDropTests
{
    private static string TempDir(string label)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, $"drop-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void AddMany_AddsJars_SkipsDuplicatesAndNonJars()
    {
        string game = TempDir("game");
        string source = TempDir("src");
        try
        {
            File.WriteAllText(Path.Combine(source, "sodium.jar"), "SODIUM");
            File.WriteAllText(Path.Combine(source, "iris.jar"), "IRIS");
            File.WriteAllText(Path.Combine(source, "readme.txt"), "not a mod");

            ModManager.AddManyResult result = ModManager.AddMany(game, new[]
            {
                Path.Combine(source, "sodium.jar"),
                Path.Combine(source, "iris.jar"),
                Path.Combine(source, "readme.txt"),
                Path.Combine(source, "missing.jar"),
            });

            Assert.Equal(2, result.Added);
            Assert.Equal(1, result.Skipped);  // readme.txt 不是 jar
            Assert.Equal(1, result.Failed);   // missing.jar 复制失败
            Assert.Equal("SODIUM", File.ReadAllText(Path.Combine(ModManager.ModsDir(game), "sodium.jar")));
            Assert.True(File.Exists(Path.Combine(ModManager.ModsDir(game), "iris.jar")));
            Assert.False(File.Exists(Path.Combine(ModManager.ModsDir(game), "readme.txt")));
        }
        finally
        {
            Directory.Delete(game, recursive: true);
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void AddMany_DoesNotOverwriteExistingFile()
    {
        string game = TempDir("game2");
        string source = TempDir("src2");
        try
        {
            Directory.CreateDirectory(ModManager.ModsDir(game));
            File.WriteAllText(Path.Combine(ModManager.ModsDir(game), "same.jar"), "OLD");

            File.WriteAllText(Path.Combine(source, "same.jar"), "NEW");
            ModManager.AddManyResult result = ModManager.AddMany(game, new[] { Path.Combine(source, "same.jar") });

            Assert.Equal(0, result.Added);
            Assert.Equal(1, result.Skipped);
            Assert.Equal("OLD", File.ReadAllText(Path.Combine(ModManager.ModsDir(game), "same.jar")));
        }
        finally
        {
            Directory.Delete(game, recursive: true);
            Directory.Delete(source, recursive: true);
        }
    }
}
