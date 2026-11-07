using System.Security.Cryptography;
using System.Text.Json;
using Lx.Probe.Updates;
using Xunit;

namespace Lx.Tests;

public sealed class UpdateDeltaTests
{
    private static string TempDir(string label)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "delta-" + label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public async Task Plan_OnlyListsChangedAndMissingFiles()
    {
        string dir = TempDir("plan");
        try
        {
            string target = Path.Combine(dir, "target");
            string host = Path.Combine(dir, "host");
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(host);

            Write(target, "same.dll", "same");
            Write(target, "changed.dll", "old-content");
            Write(host, "same.dll", "same");
            Write(host, "changed.dll", "new-content");
            Write(host, "added.dll", "brand-new");

            var manifest = new UpdateFileManifest("1.5.0", new List<UpdateFileEntry>
            {
                new("same.dll", Sha256(Path.Combine(target, "same.dll")), 4),
                new("changed.dll", Sha256(Path.Combine(host, "changed.dll")), 11),
                new("added.dll", Sha256(Path.Combine(host, "added.dll")), 9),
            }, new List<string> { "obsolete.dll" });

            string filesJson = Path.Combine(host, "files.json");
            File.WriteAllText(filesJson, JsonSerializer.Serialize(manifest));

            DeltaPlan plan = await UpdateService.PlanDeltaAsync(filesJson, target);

            Assert.Equal(1, plan.Unchanged);
            Assert.Equal(new[] { "added.dll", "changed.dll" }, plan.Download.Select(e => e.Path).OrderBy(p => p).ToArray());
            Assert.Contains("obsolete.dll", plan.Delete);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadDelta_StagesOnlyChangedFiles_AndWritesDeleteList()
    {
        string dir = TempDir("download");
        try
        {
            string target = Path.Combine(dir, "target");
            string host = Path.Combine(dir, "host");
            Directory.CreateDirectory(target);
            Write(host, "bin/new.dll", "the-new-binary");
            Write(host, "keep.txt", "stays");

            var manifest = new UpdateFileManifest("1.5.0", new List<UpdateFileEntry>
            {
                new("bin/new.dll", Sha256(Path.Combine(host, "bin", "new.dll")), 15),
                new("keep.txt", Sha256(Path.Combine(host, "keep.txt")), 5),
            }, new List<string> { "bin/old.dll" });

            string filesJson = Path.Combine(host, "files.json");
            File.WriteAllText(filesJson, JsonSerializer.Serialize(manifest));

            DeltaPlan plan = await UpdateService.PlanDeltaAsync(filesJson, target);
            var info = new UpdateInfo("1.5.0", filesJson, "deadbeef", null, filesJson);
            StagedUpdate staged = await UpdateService.DownloadDeltaAsync(info, target, plan);

            Assert.True(staged.IsDelta);
            Assert.True(File.Exists(Path.Combine(staged.StagingDir, "bin", "new.dll")));
            Assert.True(File.Exists(Path.Combine(staged.StagingDir, "keep.txt")));
            Assert.False(File.Exists(Path.Combine(staged.StagingDir, "changed.dll")));

            string deleteFile = Path.Combine(staged.StagingDir, "delete.txt");
            Assert.True(File.Exists(deleteFile));
            Assert.Contains("bin/old.dll", File.ReadAllLines(deleteFile));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadDelta_RejectsHashMismatch()
    {
        string dir = TempDir("bad");
        try
        {
            string target = Path.Combine(dir, "target");
            string host = Path.Combine(dir, "host");
            Directory.CreateDirectory(target);
            Write(host, "x.dll", "real");

            var manifest = new UpdateFileManifest("1.5.0", new List<UpdateFileEntry>
            {
                new("x.dll", new string('a', 64), 4),
            });

            string filesJson = Path.Combine(host, "files.json");
            File.WriteAllText(filesJson, JsonSerializer.Serialize(manifest));

            DeltaPlan plan = await UpdateService.PlanDeltaAsync(filesJson, target);
            var info = new UpdateInfo("1.5.0", filesJson, "x", null, filesJson);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => UpdateService.DownloadDeltaAsync(info, target, plan));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DeltaWorthwhile_OnlyForSmallChanges()
    {
        static UpdateFileEntry Entry(long size) => new("a.dll", new string('b', 64), size);

        var small = new DeltaPlan(new[] { Entry(2L * 1024 * 1024) }, 10, Array.Empty<string>());
        var huge = new DeltaPlan(new[] { Entry(180L * 1024 * 1024) }, 0, Array.Empty<string>());

        Assert.True(UpdateService.IsDeltaWorthwhile(small));
        Assert.False(UpdateService.IsDeltaWorthwhile(huge));
    }
}
