using Lx.Probe.Auth;
using Lx.Probe.Model;

namespace Lx.Tests;

public class YggdrasilAuthTests
{
    [Fact]
    public async Task RefreshAsync_WithoutRoles_ReturnsSameAccountWithoutNetwork()
    {
        var account = new Account(
            "yggdrasil",
            "Yulin_carp",
            "uuid",
            "token",
            "https://example.com",
            AvailableRoles: new List<AccountRole>());

        Account refreshed = await YggdrasilAuth.RefreshAsync(account);
        Assert.Equal(account, refreshed);
    }

    [Fact]
    public void ExpiredException_IsInvalidOperation()
    {
        var exception = new YggdrasilSessionExpiredException("请重新登录");
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Contains("重新登录", exception.Message);
    }
}
