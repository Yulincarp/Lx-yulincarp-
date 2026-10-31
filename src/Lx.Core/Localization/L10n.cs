namespace Lx.Probe.Localization;

// 轻量 i18n：内置 zh-CN / en 两套词条，按 key 取；缺词条回退中文，再缺就显示 key 本身。
// 界面用 {ui:T Key} 绑定，切换语言时发出 Changed，标签实时更新。
public static class L10n
{
    public const string ZhCn = "zh-CN";
    public const string En = "en";
    public const string DefaultLanguage = ZhCn;

    private static readonly Dictionary<string, Dictionary<string, string>> Builtin = new(StringComparer.OrdinalIgnoreCase)
    {
        [ZhCn] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Nav_Home"] = "首页",
            ["Nav_Versions"] = "版本",
            ["Nav_Instances"] = "实例",
            ["Nav_Resources"] = "资源",
            ["Nav_Accounts"] = "账号",
            ["Nav_Settings"] = "设置",
            ["Nav_Logs"] = "日志",

            ["Title_Home"] = "首页",
            ["Title_Versions"] = "版本与加载器",
            ["Title_Instances"] = "实例",
            ["Title_Resources"] = "资源",
            ["Title_Accounts"] = "账号",
            ["Title_Settings"] = "设置",
            ["Title_Logs"] = "日志",

            ["Btn_Launch"] = "启动游戏",
            ["Btn_Running"] = "游戏运行中…",
            ["Btn_Cancel"] = "取消",
            ["Btn_Verify"] = "校验/修复",
            ["Btn_OpenLogs"] = "打开日志",
            ["Btn_CopyFailures"] = "复制失败清单",
            ["Btn_CopyFailuresCount"] = "复制失败清单 ({0})",
            ["Btn_OpenSettings"] = "打开设置…",
            ["Btn_ChangeDataDir"] = "更改数据目录…",
            ["Btn_Remote"] = "手机遥控…",
            ["Btn_ImportOldData"] = "从旧版导入数据…",
            ["Btn_Save"] = "保存",
            ["Btn_Privacy"] = "隐私说明",

            ["Lang_Name"] = "简体中文",
        },
        [En] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Nav_Home"] = "Home",
            ["Nav_Versions"] = "Versions",
            ["Nav_Instances"] = "Instances",
            ["Nav_Resources"] = "Resources",
            ["Nav_Accounts"] = "Accounts",
            ["Nav_Settings"] = "Settings",
            ["Nav_Logs"] = "Logs",

            ["Title_Home"] = "Home",
            ["Title_Versions"] = "Versions & Loaders",
            ["Title_Instances"] = "Instances",
            ["Title_Resources"] = "Resources",
            ["Title_Accounts"] = "Accounts",
            ["Title_Settings"] = "Settings",
            ["Title_Logs"] = "Logs",

            ["Btn_Launch"] = "Launch",
            ["Btn_Running"] = "Running…",
            ["Btn_Cancel"] = "Cancel",
            ["Btn_Verify"] = "Verify / Repair",
            ["Btn_OpenLogs"] = "Open logs",
            ["Btn_CopyFailures"] = "Copy failures",
            ["Btn_CopyFailuresCount"] = "Copy failures ({0})",
            ["Btn_OpenSettings"] = "Open settings…",
            ["Btn_ChangeDataDir"] = "Change data folder…",
            ["Btn_Remote"] = "Remote control…",
            ["Btn_ImportOldData"] = "Import old data…",
            ["Btn_Save"] = "Save",
            ["Btn_Privacy"] = "Privacy",

            ["Lang_Name"] = "English",
        },
    };

    private static readonly Dictionary<string, string> Strings = new(Builtin[ZhCn], StringComparer.Ordinal);

    public static string Language { get; private set; } = DefaultLanguage;

    public static event Action? Changed;

    public static IReadOnlyList<string> Available => new[] { ZhCn, En };

    public static string DisplayName(string code) =>
        Builtin.TryGetValue(Normalize(code), out Dictionary<string, string>? table) && table.TryGetValue("Lang_Name", out string? name)
            ? name
            : code;

    public static string Normalize(string? code) =>
        code != null && Builtin.ContainsKey(code) ? Builtin.Keys.First(key => string.Equals(key, code, StringComparison.OrdinalIgnoreCase)) : DefaultLanguage;

    public static void SetLanguage(string? code)
    {
        string normalized = Normalize(code);
        Language = normalized;

        Strings.Clear();
        if (Builtin.TryGetValue(normalized, out Dictionary<string, string>? table))
        {
            foreach (KeyValuePair<string, string> pair in table)
            {
                Strings[pair.Key] = pair.Value;
            }
        }

        Changed?.Invoke();
    }

    public static string T(string key)
    {
        if (Strings.TryGetValue(key, out string? value))
        {
            return value;
        }

        // 英文缺词条时回退中文，避免界面出现半截英文半截 key
        return Builtin[ZhCn].TryGetValue(key, out string? fallback) ? fallback : key;
    }

    public static string T(string key, params object[] args) => string.Format(T(key), args);
}
