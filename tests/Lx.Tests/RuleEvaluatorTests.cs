using System.Text.Json;
using Lx.Probe.Model;
using Lx.Probe.Resolve;

namespace Lx.Tests;

// Step 4：rules 求值的 4 个用例，全部手写 JSON，不依赖真实文件。
public class RuleEvaluatorTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static List<Rule>? Parse(string json) => JsonSerializer.Deserialize<List<Rule>>(json, Options);

    [Fact]
    public void NoRules_IsAllowed()
    {
        List<Rule>? rules = Parse("[]");

        Assert.True(RuleEvaluator.IsAllowed(rules, new Dictionary<string, bool>()));
    }

    [Fact]
    public void AllowWindows_IsAllowed()
    {
        List<Rule>? rules = Parse("""[{"action":"allow","os":{"name":"windows"}}]""");

        Assert.True(RuleEvaluator.IsAllowed(rules, new Dictionary<string, bool>()));
    }

    [Fact]
    public void AllowThenDisallow_IsDenied()
    {
        List<Rule>? rules = Parse("""[{"action":"allow","os":{"name":"windows"}},{"action":"disallow"}]""");

        Assert.False(RuleEvaluator.IsAllowed(rules, new Dictionary<string, bool>()));
    }

    [Fact]
    public void FeatureNotEnabled_RuleSkipped_IsDenied()
    {
        List<Rule>? rules = Parse("""[{"features":{"is_demo_user":true},"action":"allow"}]""");
        var features = new Dictionary<string, bool> { ["is_demo_user"] = false };

        Assert.False(RuleEvaluator.IsAllowed(rules, features));
    }

    [Fact]
    public void ArchMismatch_RuleSkipped_IsDenied()
    {
        // 1.20.4 的 jvm 参数里有 {"action":"allow","os":{"arch":"x86"}}，64 位机器上要跳过。
        List<Rule>? rules = Parse("""[{"action":"allow","os":{"arch":"x86"}}]""");

        Assert.False(RuleEvaluator.IsAllowed(rules, new Dictionary<string, bool>(), "x86_64"));
    }
}
