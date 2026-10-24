namespace Lx.Probe.Model;

// libraries[].rules 里的一条规则，以及它可能带的 os / features 条件。
public class Rule
{
    public string? Action { get; set; }
    public RuleOs? Os { get; set; }
    public Dictionary<string, bool>? Features { get; set; }
}

public class RuleOs
{
    public string? Name { get; set; }
    public string? Arch { get; set; }
}
