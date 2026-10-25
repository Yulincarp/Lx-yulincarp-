using System.Text.Json;
using System.Text.RegularExpressions;
using Lx.Probe.Model;

namespace Lx.Probe.Resolve;

// Step 7：展开 arguments 数组——先按 rules 过滤，再把 ${...} 占位符换成真实值。
public static class ArgumentExpander
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static readonly Regex Placeholder = new(@"\$\{([^}]*)\}", RegexOptions.Compiled);

    public static List<string> Expand(
        List<JsonElement>? template,
        Dictionary<string, string> values,
        Dictionary<string, bool> features)
    {
        var result = new List<string>();
        if (template is null)
        {
            return result;
        }

        foreach (JsonElement entry in template)
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                result.Add(Replace(entry.GetString() ?? string.Empty, values));
                continue;
            }

            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            List<Rule>? rules = entry.TryGetProperty("rules", out JsonElement rulesElement)
                ? rulesElement.Deserialize<List<Rule>>(Options)
                : null;
            if (!RuleEvaluator.IsAllowed(rules, features))
            {
                continue;
            }

            if (!entry.TryGetProperty("value", out JsonElement value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                result.Add(Replace(value.GetString() ?? string.Empty, values));
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        result.Add(Replace(item.GetString() ?? string.Empty, values));
                    }
                }
            }
        }

        return result;
    }

    // 不认识的占位符只警告、换成空串，不抛异常：Mojang 每个版本都会加新的。
    private static string Replace(string text, Dictionary<string, string> values) =>
        Placeholder.Replace(text, match =>
        {
            string name = match.Groups[1].Value;
            if (values.TryGetValue(name, out string? value))
            {
                return value;
            }

            Console.WriteLine($"[warn] 不认识的占位符：${{{name}}}，按空串处理");
            return string.Empty;
        });
}
