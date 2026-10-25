using Lx.Probe.Instances;

namespace Lx.Probe.Modpacks;

// 导入结果：实例 + 包版本 + 写进实例目录的相对路径（用于后续更新时清理旧文件）。
public sealed record PackImportResult(Instance Instance, string Version, List<string> Files);
