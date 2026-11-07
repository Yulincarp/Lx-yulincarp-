using Lx.Probe.Model;
using Lx.Probe.Updates;
using Xunit;

namespace Lx.Tests;

public sealed class UpdateChannelTests
{
    private static string TempRoot()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "channel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void UseChannel(string root, string channel) =>
        SettingsStore.Save(root, SettingsStore.Load(root) with { UpdateChannel = channel });

    private static void Cleanup(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void StableChannel_UsesStableManifest()
    {
        string root = TempRoot();
        try
        {
            UseChannel(root, "stable");
            string url = UpdateService.ResolveManifestUrl(root);
            Assert.EndsWith("/update.json", url);
            Assert.DoesNotContain("update-beta", url);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void BetaChannel_UsesBetaManifest()
    {
        string root = TempRoot();
        try
        {
            UseChannel(root, "beta");
            string url = UpdateService.ResolveManifestUrl(root);
            Assert.EndsWith("update-beta.json", url);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void LocalManifestInDataDir_WinsOverRemote()
    {
        string root = TempRoot();
        try
        {
            UseChannel(root, "beta");
            File.WriteAllText(Path.Combine(root, "update-beta.json"), "{}");
            string url = UpdateService.ResolveManifestUrl(root);
            Assert.Equal(Path.Combine(root, "update-beta.json"), url);
        }
        finally
        {
            Cleanup(root);
        }
    }
}
