using System.Text.Json;

namespace Lx.Probe.Model;

// arguments.jvm / arguments.game 是混合数组：元素可能是字符串，也可能是 { value, rules }。
// 所以这里用 JsonElement 原样接住，由 Resolve/ 去解释。
public class Arguments
{
    public List<JsonElement>? Game { get; set; }
    public List<JsonElement>? Jvm { get; set; }
}
