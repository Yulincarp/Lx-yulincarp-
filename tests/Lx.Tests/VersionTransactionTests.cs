using Lx.Probe.Loaders;

namespace Lx.Tests;

public class VersionTransactionTests
{
    [Fact]
    public void Rollback_RemovesOnlyNewVersionDirectories()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "vtx-" + Guid.NewGuid().ToString("N"));
        string versions = Path.Combine(root, "versions");
        Directory.CreateDirectory(Path.Combine(versions, "old-version"));
        try
        {
            VersionTransaction transaction = VersionTransaction.Begin(root);
            Directory.CreateDirectory(Path.Combine(versions, "broken-version"));

            transaction.Rollback(null);

            Assert.True(Directory.Exists(Path.Combine(versions, "old-version")));
            Assert.False(Directory.Exists(Path.Combine(versions, "broken-version")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Commit_KeepsNewVersionDirectories()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "vtx-" + Guid.NewGuid().ToString("N"));
        string versions = Path.Combine(root, "versions");
        Directory.CreateDirectory(versions);
        try
        {
            VersionTransaction transaction = VersionTransaction.Begin(root);
            Directory.CreateDirectory(Path.Combine(versions, "new-version"));
            transaction.Commit();

            Assert.True(Directory.Exists(Path.Combine(versions, "new-version")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
