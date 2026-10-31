using System.Text.Json;

namespace Lx.Probe.Localization;

// 轻量 i18n：词条在 Localization/strings.json（编译进程序集），
// 每条有 zh / en 两份，XAML 用 {ui:T S123} 绑定，切换语言时 Changed 驱动刷新。
public static class L10n
{
    public const string ZhCn = "zh-CN";
    public const string En = "en";
    public const string DefaultLanguage = ZhCn;

    private static readonly Dictionary<string, Entry> Table = Load();

    public static string Language { get; private set; } = DefaultLanguage;

    public static event Action? Changed;

    public static IReadOnlyList<string> Available => new[] { ZhCn, En };

    public static string DisplayName(string code) => Normalize(code) == En ? "English" : "简体中文";

    public static string Normalize(string? code) =>
        string.Equals(code, En, StringComparison.OrdinalIgnoreCase) ? En : DefaultLanguage;

    public static void SetLanguage(string? code)
    {
        Language = Normalize(code);
        Changed?.Invoke();
    }

    public static string T(string key)
    {
        if (!Table.TryGetValue(key, out Entry? entry))
        {
            return key;
        }

        string value = Language == En ? entry.En : entry.Zh;
        return value.Length > 0 ? value : entry.Zh;
    }

    public static string T(string key, params object[] args) => string.Format(T(key), args);

    public static bool Has(string key) => Table.ContainsKey(key);

    private static Dictionary<string, Entry> Load()
    {
        var table = new Dictionary<string, Entry>(StringComparer.Ordinal);

        using Stream? stream = typeof(L10n).Assembly.GetManifestResourceStream("Lx.Core.Localization.strings.json");
        if (stream is null)
        {
            return table;
        }

        using JsonDocument document = JsonDocument.Parse(stream);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string zh = property.Value.TryGetProperty("zh", out JsonElement zhElement) ? zhElement.GetString() ?? string.Empty : string.Empty;
            string en = property.Value.TryGetProperty("en", out JsonElement enElement) ? enElement.GetString() ?? zh : zh;
            table[property.Name] = new Entry(zh, en);
        }

        return table;
    }

    private sealed record Entry(string Zh, string En);
}
