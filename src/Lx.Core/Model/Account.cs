namespace Lx.Probe.Model;

// 登录后的会话。离线 / 外置 / 正版三种方式最终都产出这一个对象。
// AvailableRoles 只有外置（Yggdrasil）账号有，用于在启动器里切换角色。
public sealed record Account(
    string Type,
    string Name,
    string Uuid,
    string AccessToken,
    string? ApiRoot,
    List<AccountRole>? AvailableRoles = null,
    string? RefreshToken = null,
    DateTimeOffset? ExpiresAt = null,
    string? ClientId = null)
{
    // 默认 ToString 会把 AccessToken 一起打出来；UI 自动化树、日志、剪贴板都可能拿到它。
    // 统一只暴露名字和类型，token 不进入任何字符串。
    public override string ToString() => $"{Name}（{Type}）";
}

public sealed record AccountRole(string Id, string Name);
