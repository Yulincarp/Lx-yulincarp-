using Lx.Probe.Model;

namespace Lx.Probe.Resolve;

// rules 求值：规则是按顺序覆盖的，不是"有一条 allow 就放行"。
public static class RuleEvaluator
{
    public static bool IsAllowed(List<Rule>? rules, Dictionary<string, bool> features)
    {
        if (rules is null || rules.Count == 0)
        {
            return true; // 没有 rules = 允许
        }

        bool allow = false; // 有 rules = 默认拒绝
        foreach (Rule rule in rules)
        {
            if (rule.Os?.Name is { Length: > 0 } osName && osName != "windows")
            {
                continue; // 不是 Windows 的规则，跳过
            }

            if (rule.Features is { Count: > 0 } &&
                !rule.Features.All(kv => features.TryGetValue(kv.Key, out bool actual) && actual == kv.Value))
            {
                continue; // 当前环境不满足 features，跳过
            }

            allow = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
        }

        return allow;
    }
}
