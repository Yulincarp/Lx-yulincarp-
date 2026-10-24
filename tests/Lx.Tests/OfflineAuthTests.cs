using Lx.Probe.Auth;
using Lx.Probe.Model;

namespace Lx.Tests;

public class OfflineAuthTests
{
    [Fact]
    public void UuidFor_Notch_MatchesKnownOfflineUuid()
    {
        // 和 Java 的 UUID.nameUUIDFromBytes("OfflinePlayer:Notch") 一致
        Assert.Equal("b50ad385829d3141a2167e7d7539ba7f", OfflineAuth.UuidFor("Notch"));
    }

    [Fact]
    public void UuidFor_IsStableAnd32HexChars()
    {
        string uuid = OfflineAuth.UuidFor("Steve");

        Assert.Equal(uuid, OfflineAuth.UuidFor("Steve"));
        Assert.Equal(32, uuid.Length);
        Assert.Matches("^[0-9a-f]{32}$", uuid);
    }

    [Fact]
    public void Create_EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => OfflineAuth.Create(" "));
    }

    [Fact]
    public void AccountStore_SaveThenLoad_RoundTrips()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "acc-" + Guid.NewGuid().ToString("N"));
        try
        {
            AccountStore.Save(root, new Account("offline", "Steve", "abc", "0", null));

            List<Account> loaded = AccountStore.Load(root);

            Assert.Single(loaded);
            Assert.Equal("offline", loaded[0].Type);
            Assert.Equal("Steve", loaded[0].Name);
            Assert.Equal("abc", loaded[0].Uuid);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
