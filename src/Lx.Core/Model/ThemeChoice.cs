namespace Lx.Probe.Model;

// 主题选择的纯逻辑：设置里存 light / dark / system，
// "system" 具体是深还是浅由界面层读系统设置后传进来（Core 不碰注册表）。
public static class ThemeChoice
{
    public const string Light = "light";
    public const string Dark = "dark";
    public const string System = "system";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Dark => Dark,
        System => System,
        _ => Light,
    };

    // mode 是存下来的值；systemDark 是"系统当前是不是深色"。
    public static string Resolve(string? mode, bool systemDark)
    {
        string normalized = Normalize(mode);
        return normalized switch
        {
            Dark => Dark,
            System => systemDark ? Dark : Light,
            _ => Light,
        };
    }

    public static string Describe(string? value) => Normalize(value) switch
    {
        Dark => "深色",
        System => "跟随系统",
        _ => "浅色",
    };
}
