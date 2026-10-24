namespace Lx.Probe.Model;

// 登录后的会话。离线 / 外置 / 正版三种方式最终都产出这一个对象。
// AvailableRoles 只有外置（Yggdrasil）账号有，用于在启动器里切换角色。
public sealed record Account(
    string Type,
    string Name,
    string Uuid,
    string AccessToken,
    string? ApiRoot,
    List<AccountRole>? AvailableRoles = null);

public sealed record AccountRole(string Id, string Name);
