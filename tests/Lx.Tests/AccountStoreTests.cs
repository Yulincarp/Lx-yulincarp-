using Lx.Probe.Auth;
using Lx.Probe.Model;

namespace Lx.Tests;

public class AccountStoreTests
{
    [Fact]
    public void FindByName_IsCaseInsensitive_AndReturnsNullWhenMissing()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "accs-" + Guid.NewGuid().ToString("N"));
        try
        {
            AccountStore.Save(root, new Account("offline", "Steve", "u1", "0", null));
            AccountStore.Save(root, new Account("yggdrasil", "Yulin_carp", "u2", "tok", "https://littleskin.cn/api/yggdrasil"));

            Assert.Equal("u2", AccountStore.FindByName(root, "yulin_carp")?.Uuid);
            Assert.Equal("offline", AccountStore.FindByName(root, "STEVE")?.Type);
            Assert.Null(AccountStore.FindByName(root, "nobody"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Save_ReplacesSameTypeAndName()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "accs-" + Guid.NewGuid().ToString("N"));
        try
        {
            AccountStore.Save(root, new Account("offline", "Steve", "old", "0", null));
            AccountStore.Save(root, new Account("offline", "Steve", "new", "0", null));

            List<Account> all = AccountStore.Load(root);

            Assert.Single(all);
            Assert.Equal("new", all[0].Uuid);
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
