using Lx.Probe.Auth;
using Lx.Probe.Model;

namespace Lx.Tests;

public class AccountStoreSecurityTests
{
    [Fact]
    public void Save_EncryptsTokensAtRest()
    {
        string root = NewDir();
        try
        {
            AccountStore.Save(root, new Account(
                "microsoft",
                "Alice",
                "uuid",
                "plain-access",
                null,
                RefreshToken: "plain-refresh",
                ExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
                ClientId: "client-id"));

            string raw = File.ReadAllText(AccountStore.PathFor(root));
            Assert.DoesNotContain("plain-access", raw);
            Assert.DoesNotContain("plain-refresh", raw);
            Assert.Contains("dpapi:", raw);

            Account loaded = Assert.Single(AccountStore.Load(root));
            Assert.Equal("plain-access", loaded.AccessToken);
            Assert.Equal("plain-refresh", loaded.RefreshToken);
            Assert.Equal("client-id", loaded.ClientId);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Load_MigratesPlaintextFile()
    {
        string root = NewDir();
        try
        {
            Directory.CreateDirectory(root);
            string path = AccountStore.PathFor(root);
            File.WriteAllText(path, """
            [
              {
                "Type": "microsoft",
                "Name": "Alice",
                "Uuid": "uuid",
                "AccessToken": "plain-access",
                "ApiRoot": null,
                "RefreshToken": "plain-refresh",
                "ClientId": "client-id"
              }
            ]
            """);

            List<Account> accounts = AccountStore.Load(root);
            Account loaded = Assert.Single(accounts);
            Assert.Equal("plain-access", loaded.AccessToken);
            Assert.Equal("plain-refresh", loaded.RefreshToken);

            string raw = File.ReadAllText(path);
            Assert.DoesNotContain("plain-access", raw);
            Assert.DoesNotContain("plain-refresh", raw);

            string backup = path + ".bak";
            Assert.True(File.Exists(backup));
            string backupText = File.ReadAllText(backup);
            Assert.DoesNotContain("plain-access", backupText);
            Assert.DoesNotContain("plain-refresh", backupText);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Load_UndecryptableToken_ReturnsEmptyWithoutThrowing()
    {
        string root = NewDir();
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(AccountStore.PathFor(root), """
            [
              {
                "Type": "microsoft",
                "Name": "Bob",
                "Uuid": "uuid",
                "AccessToken": "dpapi:not-base64",
                "RefreshToken": "dpapi:also-not-base64"
              }
            ]
            """);

            Account account = Assert.Single(AccountStore.Load(root));
            Assert.Equal(string.Empty, account.AccessToken);
            Assert.Null(account.RefreshToken);
        }
        finally
        {
            Delete(root);
        }
    }

    private static string NewDir() =>
        Path.Combine(AppContext.BaseDirectory, "acc-sec-" + Guid.NewGuid().ToString("N"));

    private static void Delete(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时目录清不掉不影响测试结果
        }
    }
}

public class MicrosoftRefreshPolicyTests
{
    [Fact]
    public void NeedsRefresh_ExpiredOrNearExpiry_ReturnsTrue()
    {
        Account account = MicrosoftAccount(DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.True(MicrosoftAuth.NeedsRefresh(account));
        Assert.True(MicrosoftAuth.IsExpired(account));
    }

    [Fact]
    public void NeedsRefresh_FreshToken_ReturnsFalse()
    {
        Account account = MicrosoftAccount(DateTimeOffset.UtcNow.AddHours(1));
        Assert.False(MicrosoftAuth.NeedsRefresh(account));
        Assert.False(MicrosoftAuth.IsExpired(account));
    }

    [Fact]
    public void NeedsRefresh_WithoutRefreshToken_ReturnsFalse()
    {
        Account account = MicrosoftAccount(DateTimeOffset.UtcNow.AddMinutes(-1)) with
        {
            RefreshToken = null,
            ClientId = null,
        };

        Assert.False(MicrosoftAuth.NeedsRefresh(account));
        Assert.True(MicrosoftAuth.IsExpired(account));
    }

    private static Account MicrosoftAccount(DateTimeOffset expiresAt) => new(
        "microsoft",
        "Alice",
        "uuid",
        "mc-token",
        null,
        RefreshToken: "refresh-token",
        ExpiresAt: expiresAt,
        ClientId: "client-id");
}
