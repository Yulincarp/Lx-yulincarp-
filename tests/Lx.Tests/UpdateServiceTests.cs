using System.IO.Compression;
using System.Security.Cryptography;
using Lx.Probe;
using Lx.Probe.Updates;

namespace Lx.Tests;

public class UpdateServiceTests
{
    [Fact]
    public void IsNewer_ComparesVersions()
    {
        Assert.True(UpdateService.IsNewer("0.3.1", "0.3.0"));
        Assert.False(UpdateService.IsNewer("0.3.0", "0.3.0"));
        Assert.False(UpdateService.IsNewer("0.2.9", "0.3.0"));
    }

    [Fact]
    public void ResolveManifestUrl_DefaultsToGitHubLatest()
    {
        string root = NewDir();
        string? old = Environment.GetEnvironmentVariable("LX_UPDATE_URL");
        try
        {
            Environment.SetEnvironmentVariable("LX_UPDATE_URL", null);
            Assert.Equal(UpdateService.DefaultManifestUrl, UpdateService.ResolveManifestUrl(root));
            Assert.Contains("/releases/latest/download/update.json", UpdateService.DefaultManifestUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LX_UPDATE_URL", old);
            Delete(root);
        }
    }

    [Fact]
    public void ResolveManifestUrl_PrefersLocalFileThenEnv()
    {
        string root = NewDir();
        string? old = Environment.GetEnvironmentVariable("LX_UPDATE_URL");
        try
        {
            string local = Path.Combine(root, "update.json");
            File.WriteAllText(local, "{}");
            Environment.SetEnvironmentVariable("LX_UPDATE_URL", null);
            Assert.Equal(local, UpdateService.ResolveManifestUrl(root));

            Environment.SetEnvironmentVariable("LX_UPDATE_URL", "https://example.invalid/update.json");
            Assert.Equal("https://example.invalid/update.json", UpdateService.ResolveManifestUrl(root));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LX_UPDATE_URL", old);
            Delete(root);
        }
    }

    [Fact]
    public async Task CheckAsync_ReadsLocalManifest()
    {
        string root = NewDir();
        try
        {
            string next = NextVersion();
            string zip = Path.Combine(root, "pkg.zip");
            File.WriteAllText(zip, "not-used-by-check");
            string manifest = Path.Combine(root, "update.json");
            string sha = new string('a', 64);
            File.WriteAllText(manifest, $$"""
            {
              "version": "{{next}}",
              "url": "{{new Uri(zip).AbsoluteUri}}",
              "sha256": "{{sha}}",
              "notes": "test"
            }
            """);

            UpdateInfo? info = await UpdateService.CheckAsync(manifest);

            Assert.NotNull(info);
            Assert.Equal(next, info!.Version);
            Assert.Equal(sha, info.Sha256);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task DownloadAsync_ExtractsExeFromLocalZip()
    {
        string root = NewDir();
        try
        {
            string zip = Path.Combine(root, "pkg.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry(AppInfo.ExecutableName);
                using Stream stream = entry.Open();
                stream.Write(new byte[] { 1, 2, 3, 4 });
            }

            string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))).ToLowerInvariant();
            var info = new UpdateInfo(NextVersion(), new Uri(zip).AbsoluteUri, sha, null);

            StagedUpdate staged = await UpdateService.DownloadAsync(info, root);

            Assert.True(File.Exists(Path.Combine(staged.StagingDir, AppInfo.ExecutableName)));
            Assert.True(File.Exists(staged.ZipPath));
        }
        finally
        {
            Delete(root);
        }
    }

    private static string NextVersion()
    {
        Version current = Version.Parse(AppInfo.Version);
        return $"{current.Major}.{current.Minor}.{current.Build + 1}";
    }

    private static string NewDir()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "updates-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Delete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清不掉不影响测试
        }
    }
}
