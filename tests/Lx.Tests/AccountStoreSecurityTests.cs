using Lx.Probe.Auth;
using Lx.Probe.Model;

namespace Lx.Tests;

public class AccountStoreSecurityTests
{
    [Fact]
    public void Save_EncryptsTokenAtRest()
    {
        string root = NewDir();
        try
        {
            AccountStore.Save(root, new Account(
                "yggdrasil",
                "Alice",
                "uuid",
                "plain-secret",
                "https://example.com"));

            string raw = File.ReadAllText(AccountStore.PathFor(root));
            Assert.DoesNotContain("plain-secret", raw);
            Assert.Contains("dpapi:", raw);
            Assert.Equal("plain-secret", Assert.Single(AccountStore.Load(root)).AccessToken);
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
                "Type": "yggdrasil",
                "Name": "Alice",
                "Uuid": "uuid",
                "AccessToken": "plain-secret",
                "ApiRoot": "https://example.com",
                "AvailableRoles": []
              }
            ]
            """);

            List<Account> accounts = AccountStore.Load(root);
            Assert.Equal("plain-secret", Assert.Single(accounts).AccessToken);

            string raw = File.ReadAllText(path);
            Assert.DoesNotContain("plain-secret", raw);

            string backup = path + ".bak";
            Assert.True(File.Exists(backup));
            Assert.DoesNotContain("plain-secret", File.ReadAllText(backup));
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
                "AccessToken": "dpapi:not-base64"
              }
            ]
            """);

            Account account = Assert.Single(AccountStore.Load(root));
            Assert.Equal(string.Empty, account.AccessToken);
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
