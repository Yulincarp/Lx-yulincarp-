using Lx.Probe.Migration;

namespace Lx.Tests;

public class DataDirectoryMigratorTests
{
    [Fact]
    public async Task Migrate_CopiesAllFilesIncludingLogs()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "move-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "old");
        string target = Path.Combine(root, "new");
        Directory.CreateDirectory(Path.Combine(source, "logs"));
        Directory.CreateDirectory(Path.Combine(source, "versions", "1.20.4"));
        File.WriteAllText(Path.Combine(source, "settings.json"), "x");
        File.WriteAllText(Path.Combine(source, "logs", "old.log"), "log");
        File.WriteAllText(Path.Combine(source, "versions", "1.20.4", "1.20.4.json"), "{}");

        try
        {
            await DataDirectoryMigrator.MigrateAsync(source, target);

            Assert.True(File.Exists(Path.Combine(target, "settings.json")));
            Assert.True(File.Exists(Path.Combine(target, "logs", "old.log")));
            Assert.True(File.Exists(Path.Combine(target, "versions", "1.20.4", "1.20.4.json")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Migrate_RejectsNonEmptyTarget()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "move-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "old");
        string target = Path.Combine(root, "new");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "settings.json"), "x");
        File.WriteAllText(Path.Combine(target, "other.txt"), "y");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DataDirectoryMigrator.MigrateAsync(source, target));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
