namespace Lx.Probe.Model;

// 登录后的会话。离线 / 外置 / 正版三种方式最终都产出这一个对象。
public sealed record Account(
    string Type,
    string Name,
    string Uuid,
    string AccessToken,
    string? ApiRoot);
