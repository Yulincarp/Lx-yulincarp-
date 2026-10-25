using Lx.Probe.Migration;

namespace Lx.Tests;

public class DataMigrationTests
{
    [Fact]
    public async Task Migrate_CopiesDataAndSkipsLogs()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "migrate-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "old");
        string target = Path.Combine(root, "new");
        Directory.CreateDirectory(Path.Combine(source, "versions", "1.20.4"));
        Directory.CreateDirectory(Path.Combine(source, "logs"));
        File.WriteAllText(Path.Combine(source, "settings.json"), "old-settings");
        File.WriteAllText(Path.Combine(source, "versions", "1.20.4", "1.20.4.json"), "{}");
        File.WriteAllText(Path.Combine(source, "logs", "old.log"), "log");

        try
        {
            DataMigrationResult result = await DataMigrationService.MigrateAsync(source, target);

            Assert.Equal(2, result.FilesCopied);
            Assert.Equal("old-settings", File.ReadAllText(Path.Combine(target, "settings.json")));
            Assert.True(File.Exists(Path.Combine(target, "versions", "1.20.4", "1.20.4.json")));
            Assert.False(File.Exists(Path.Combine(target, "logs", "old.log")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Migrate_DoesNotOverwriteByDefault()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "migrate-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "old");
        string target = Path.Combine(root, "new");
        Directory.CreateDirectory(Path.Combine(source, "versions"));
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "settings.json"), "old");
        File.WriteAllText(Path.Combine(target, "settings.json"), "new");

        try
        {
            DataMigrationResult result = await DataMigrationService.MigrateAsync(source, target);

            Assert.Equal(0, result.FilesCopied);
            Assert.Equal(1, result.FilesSkipped);
            Assert.Equal("new", File.ReadAllText(Path.Combine(target, "settings.json")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
