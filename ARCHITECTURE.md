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

## 下一步（MVVM）

1. 引入 CommunityToolkit.Mvvm
2. 先拆设置页/资源页的 ViewModel
3. 页面 code-behind 只保留窗口生命周期
4. 统一错误模型和 CancellationToken
5. 状态缓存、结构化日志、nullable 清零
