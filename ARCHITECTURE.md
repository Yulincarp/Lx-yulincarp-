# Lx 架构

## 项目

```text
src/Lx.Core   业务核心（net8.0 类库，命名空间仍是 Lx.Probe.*）
src/Lx.Cli    命令行入口（薄入口，引用 Lx.Core）
src/Lx.UI     WPF 界面（引用 Lx.Core）
tests/Lx.Tests 单元测试（引用 Lx.Core）
```

## 为什么命名空间还叫 Lx.Probe

这次只做“项目拆分”，不改命名空间，避免 500+ 处 using 和 XAML 一起动。
下一阶段可以批量重命名为 Lx.Core.*，作为独立提交。

## 约束

- `Lx.Core` 不引用 WPF、不读 UI 控件
- `Lx.Cli` 只做参数解析和调用 Core
- `Lx.UI` 只做界面、交互、把用户选择交给 Core
- 新业务逻辑优先放 Core，并补单元测试

## MVVM 进度

- 已完成：
  - `SettingsSummaryViewModel`：设置摘要/数据目录/CurseForge Key 状态
  - `ResourceViewModel`：本地 mods 的加载、搜索、排序、启停、删除
- 待办：
  1. `SettingsWindow` 表单 ViewModel
  2. 资源页在线搜索/安装状态 ViewModel
  3. 页面 code-behind 只保留窗口生命周期
  4. 统一错误模型和 CancellationToken
  5. 状态缓存、结构化日志、nullable 清零

当前没有直接引入 CommunityToolkit.Mvvm，先用 `INotifyPropertyChanged` 保持依赖最少。
