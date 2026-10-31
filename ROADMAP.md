# Lx 启动器 · 后续修改与更新路线图

> 基线：2026-10-25 · `547ea96` · 41 次提交 · 31 个测试全绿
> 假设：单人开发、每天 2–4 小时、以 Windows 为主
> 目标顺序：**自己每天敢用（v0.2）→ 朋友能用（v0.3）→ 可以公开发布（v1.0）**
>
> **最新进度见 §0.1（2026-10-31 复核）与 §11（下一轮 step m10-x）；正文的勾选状态未同步，别照着正文判断进度。**

---

## 0. 一页结论

内核（下载 → 合并 → 拼命令行 → 启动）已经通了，M1/M2/M3 的硬骨头（natives、规则继承、Forge/NeoForge、整合包）都啃完了。
现在的短板不在"能不能启动"，而在**产品化**：

1. **出错看不见**：下载失败、异常只写到 `Console`，WPF 里根本没有控制台；没有日志文件。
2. **正版要每天重登**：`Account` 只存 Minecraft token（约 24 小时），没有 refresh token，也没有过期时间。
3. **发不出去**：没有安装包、没有版本号、没有自更新；安装根靠"往上找 Lx.sln"，离开仓库就找不到。
4. **加载器只能命令行**：UI 只有一个 Fabric 勾选框，Forge/NeoForge 普通用户装不了。
5. **国内网络易卡死**：只有 Forge/NeoForge 的 maven 走 BMCLAPI，原版/资源/库全直连。

**最该先做的五件事（按顺序）**：
全局异常 + 文件日志 → 账号加密 + 正版续期 → 加载器图形化 → 下载源/修复 → 单文件发布 + 自更新。

---

## 0.1 进度快照（2026-10-31 复核）

> 正文写于规划期，勾选和优先级没有随开发同步；**这一节按 git 记录与代码实测**，与正文冲突时以本节为准。

**已完成**

| 范围 | 对应提交 / 说明 |
|---|---|
| v0.2 的 A–F 六组 | m5–m8：异常与日志、账号续期与 DPAPI、加载器图形化、下载源/续传/修复、自更新、七页导航、高 DPI/主题、设置页重组 |
| H1 mod 生态 | m7-1 … m7-5：Modrinth 搜索/依赖、CurseForge、批量更新、冲突检测、备份 |
| H3 第一项 | m9-1 拆出 `Lx.Core` / `Lx.Cli`，UI 只引用 Core |
| 工程债 · nullable | 2026-10-31 清零 6 条 CS86xx（`PackImporter.KeyOf`），`Lx.Core` 全量重建 0 warning |
| H4 的一部分 | CI（`ci.yml` / `release.yml`）、单文件发布、Setup.exe、数据目录迁移、自更新 |
| C1 手机遥控 | `2b2aa51`：局域网 HTTP 服务 + 手机网页 + APK（WebView 壳） |

当前版本 **v1.4.2**（`Directory.Build.props` 是唯一来源；默认数据目录 `%APPDATA%\Lx yulin carp\.minecraft`，自更新默认查 GitHub Release 的 latest/update.json），`dist\` 里已有 `Lx-yulin-carp-1.4.2-win-x64.zip`、`Lx-yulin-carp-1.4.2-Setup.exe`、`Lx-yulin-carp-remote-1.4.1.apk`、`update.json`。

**剩余（按优先级）**

| # | 事项 | 优先级 | 现状证据 | 去向 |
|---|---|---|---|---|
| 1 | H2 整合包与服务器同步：`pack.json` 规范、服务端导出、客户端增量同步、同步快照与回滚、一键进服、托管方式 | P0 **最小版完成** | `src/Lx.Core/Sync/`（PackManifest / PackFilter / PackExporter / PackSync / SyncSnapshot）+ 实例页「服务器同步」+ CLI `--pack-export/--pack-sync/--pack-rollback/--server`；测试 90 绿，`SYNC.md` 有托管与验收步骤。待办：包签名、真机（公网托管）走一遍 | §11 |
| 2 | 实例增强：改名 / 复制 / 删除确认 / 打开目录 / 导入导出 | P0（v0.3 出口条件） | `InstanceStore` 只有增删改查 | §11 |
| 3 | 版本下拉搜索、拖入 jar 安装、Forge/NeoForge mod 元数据、实例右键菜单 | P1 | `CollectionViewSource` / `DragDrop` / `mods.toml` / `ContextMenu` 在 `src/Lx.UI` 均 0 命中 | §11 |
| 4 | H3 收尾：统一错误模型、状态缓存、结构化日志 + 脱敏、nullable 清零、两页 MVVM | P1 | `ARCHITECTURE.md` 列了 5 条待办；无 CommunityToolkit / SQLite | §11 |
| 5 | 发布收尾：打 tag、配 remote、`update.json` 指向资产、RELEASE.md 的 8 条验收 | P0 | `git tag` 只有 m1 / m2 / baseline-m3，无 remote | §11 m10-0 |
| 6 | 代码签名（SmartScreen 拦截） | P1 | `RELEASE.md` §5 写明“当前默认未签名” | §11 |
| 7 | 崩溃上报（opt-in）+ 隐私说明 / 遥测开关 | P2 | 无 Sentry / 崩溃上报代码 | §11 |
| 8 | H5 剩余：皮肤 / 披风预览、离线皮肤、账号页信息完整化 | P1 / P2 | 皮肤相关代码 0 处 | backlog |
| 9 | H6 国际化与可访问性：i18n、深浅主题、键盘导航、125%/150% 缩放 | P2 | 无 `.resx`；主题仅零星 3 处 | backlog |
| 10 | H7 Linux/macOS、C2 安卓本地 Java 版 | 暂缓 | 计划内已注明暂缓 | backlog |

**里程碑判断**：v0.2 已达成；v0.3 还差第 1、2 项；v1.0 还差第 4、6、7 项和 H6。

---

## 1. 现状盘点

### 1.1 已经跑通的（别推倒重来）

| 模块 | 现状 | 主要位置 |
|---|---|---|
| 启动内核 | version.json 解析、inheritsFrom 合并、rules/arch 过滤、classpath、natives、参数展开、命令行 | `src/Lx.Core/Resolve`、`Launch/LaunchPipeline.cs` |
| 下载 | 8 并发、sha1 跳过、`.part` 临时文件、3 次退避重试、Mojang 运行时自动下载 | `Resolve/Downloader.cs`、`Launch/JavaRuntime.cs` |
| 账号 | 离线 / 外置 Yggdrasil（含角色切换）/ 微软 device code；多账号 | `Auth/`、`accounts.json` |
| 加载器 | Fabric、Forge、NeoForge（安装器）、OptiFine/OptiFabric | `Resolve/FabricInstaller.cs`、`Loaders/` |
| 整合包 | mrpack、CurseForge zip、rar/7z/tar；更新时差异清理 | `Modpacks/` |
| mods | 列表/启用禁用/添加/删除、Modrinth 按文件哈希查更新 | `Mods/` |
| 实例 | 多游戏目录、版本/loader/账号绑定、`instances.json` | `Instances/` |
| 设置 | 内存上下限、Java 选择（自动/指定） | `Model/Settings.cs`、`SettingsWindow` |
| 测试 | 31 个纯函数测试（规则、合并、整合包、实例、账号） | `tests/Lx.Tests` |

### 1.2 关键缺口（按影响排序）

| # | 缺口 | 后果 | 证据 | 优先级 |
|---|---|---|---|---|
| 1 | 异常/下载失败不进 UI，无日志文件 | 用户只看到"没反应"，你也没法远程排错 | `Downloader` 里全是 `Console.WriteLine`；`App.xaml.cs` 里没有全局异常处理 | P0 |
| 2 | 微软 token 不续期 | 每天启动都要重新 device code 登录 | `MicrosoftAuth.LoginAsync` 只返回 mcToken；`Account` 无 RefreshToken/ExpiresAt | P0 |
| 3 | `accounts.json` 明文存 accessToken | 本机任何程序/同步盘都能读到 | `AccountStore` 注释即"明文，仅本机"；只有 CF key 用了 DPAPI | P0 |
| 4 | 无版本号/安装包/自更新 | 只能 `dotnet run`，改一次就要重新教用户拷目录 | `launcher_version` 写死 `"0.1"`；根目录靠 `Lx.sln` 反查 | P0 |
| 5 | 加载器只有 Fabric 勾选框 | Forge/NeoForge 玩家无法在界面完成 | `MainWindow.xaml` 只有 `FabricCheck` | P0 |
| 6 | 下载源不可选、无代理 | 国内直连 piston/resources 经常超时 | `ManifestService`、`DownloadManifest` 全硬编码官方 URL | P0 |
| 7 | 每次启动重算约 3800 个文件 sha1 | 冷启动白等 3–15 秒（机械盘更久） | `LaunchPipeline` 每次全量 `DownloadAllAsync` → 逐文件 `Hasher.Sha1Of` | P1 |
| 8 | 无校验/修复入口 | 文件坏了只能删整个目录重下 | `InstallCheck` 只查存在，不查完整性 | P0 |
| 9 | 无取消、无单实例、无进程监控 | 卡住只能杀进程；能重复开两个窗口；游戏退出后 UI 不知道 | `Launch_Click` 无 CancellationToken；`Process.Start` 后 `Dispose` | P1 |
| 10 | 实例不能改名/复制/打开目录/单独设置 | 管理多实例很别扭 | `InstanceStore` 只有增删改查记录；删除不删文件 | P1 |
| 11 | mod 只能看文件名（Forge），无搜索/安装 | 装 mod 仍要开浏览器手动下载 | `ModManager.ReadMetadata` 只读 `fabric.mod.json` | P1 |
| 12 | 品质细节：920 个版本平铺、日志不可复制、无 DPI/主题 | 第一印象像半成品 | `VersionBox` 直接绑 `List<ManifestVersion>` | P2 |

### 1.3 工程债（不急着还，但别再加）

- 业务代码全在叫 `Lx.Probe`（试验场）的项目里，UI 直接引用；以后拆 `Lx.Core` 会很痛。
- `MainWindow.xaml.cs` 694 行，事件 + 业务 + UI 混在一起，没有 MVVM/DI。
- 大量硬编码 URL、版本字符串、路径拼接；没有 `CancellationToken`。
- 编译有 6 条 nullable 警告（`PackImporter`），建议先清零，避免真 bug 被淹没。
- 没有 CI；测试全是纯函数，下载/安装/启动链路没有集成测试。

---

## 2. 口径说明

**优先级**

| 级别 | 含义 | 处理规则 |
|---|---|---|
| P0 | 不做就无法日常使用 / 无法发布 | 当前迭代必须完成 |
| P1 | 明显影响体验或稳定性 | 排进 v0.2/v0.3，不插队到 P0 前 |
| P2 | 锦上添花 | 有余力再做，可无限期推迟 |

**难度**

| 标记 | 工作量 | 典型任务 |
|---|---|---|
| ★ | ≤ 1 天 | 加设置项、改文案、补一个解析分支 |
| ★★ | 2–3 天 | DPAPI 迁移、镜像重写、页面拆分 |
| ★★★ | ≥ 1 周 | 自更新、mod 依赖解析、MVVM 重构 |

**完成定义（DoD）**：代码 + 可复现的验收步骤 + 关键路径有日志 + 一个独立 commit（沿用 `step mN-x: ...` 格式）+ 测试不回归。

---

## 3. 短期计划：v0.2「能日常用」（第 1–6 周）

> 只做 P0 和少量 P1；目标是一个人、一台干净 Windows，解压后全程点鼠标能装好并启动。

### 3.0 迭代安排

| 迭代 | 周次 | 主题 | 包含 |
|---|---|---|---|
| 1 | 第 1–2 周 | 看得见、登得住 | A 稳定性 + B 账号 |
| 2 | 第 3–4 周 | 装得上、发得出去 | C 发布 + D 加载器 |
| 3 | 第 5–6 周 | 下得动、用得顺 | E 网络 + F 界面骨架 |

### 3.1 A · 稳定性与可观测性（4.5 天）

| 编号 | 任务 | 难度 | 优先级 | 预估 | 要点 / 验收 |
|---|---|---|---|---|---|
| A1 | 全局异常兜底 | ★★ | P0 | 0.5d | `App` 挂 `DispatcherUnhandledException`、`AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`；弹窗显示摘要 + 日志路径，进程不静默退出 |
| A2 | 文件日志 | ★★ | P0 | 1d | 新增 `Diagnostics/Log.cs`：按天写 `logs/launcher-yyyy-MM-dd.log`，分级、线程安全、5 MB 轮转；同时推到 UI。验收：任意操作后日志可追溯 |
| A3 | 下载/安装错误进 UI | ★ | P0 | 0.5d | `Downloader` 增加 `onError(url, status, path)` 回调；失败清单可一键复制；启动失败给「重试 / 换源 / 打开日志」 |
| A4 | 单实例锁 | ★★ | P1 | 0.5d | 命名 Mutex；第二次启动用命名管道/WM_COPYDATA 唤醒并前置已有窗口。验收：双击两次只有一个窗口 |
| A5 | 游戏进程监控 | ★★ | P0 | 1d | `EnableRaisingEvents` + `Exited`：恢复按钮、显示运行时长/退出码；非 0 时提示看 `latest.log` |
| A6 | 取消启动/下载 | ★★ | P0 | 1d | 贯穿 `CancellationToken`；启动按钮变「取消」；取消后 5 秒内停下，`.part` 保留。注意 `Task.WhenAll` 要吞 `OperationCanceledException` |

### 3.2 B · 账号安全与续期（3.5 天）

| 编号 | 任务 | 难度 | 优先级 | 预估 | 要点 / 验收 |
|---|---|---|---|---|---|
| B1 | `accounts.json` DPAPI 加密 | ★★ | P0 | 1d | 用 `ProtectedData(CurrentUser)` 加密 token 字段，文件加 `"v":2`；启动时自动迁移旧明文并备份 `.bak`。验收：文件里搜不到 token；换 Windows 用户后提示重新登录而不是崩 |
| B2 | 微软 refresh token + 静默续期 | ★★★ | P0 | 1.5d | 登录响应已含 `refresh_token`（scope 有 `offline_access`），存下来 + `expires_at`；启动前 5 分钟内到期就用 refresh grant 换新 MS token，再走 XBL/XSTS/MC 换 mcToken；`invalid_grant` 时提示重新登录。验收：登录一次，隔 24h 启动不再弹设备码 |
| B3 | 外置登录失效友好化 | ★ | P1 | 0.5d | Yggdrasil refresh 401 时提示"站点登录已过期，请重新登录"，保留账号记录，不清空 |
| B4 | authlib-injector 自动获取 | ★ | P0 | 0.5d | 现在要求 `minecraft/authlib-injector.jar` 手动存在；改为首次外置登录时下载 + sha256 校验 + 版本可配。验收：新机器只填 API 根就能启动 |

### 3.3 C · 发布与自更新（4 天）

| 编号 | 任务 | 难度 | 优先级 | 预估 | 要点 / 验收 |
|---|---|---|---|---|---|
| C1 | 版本号单一来源 | ★ | P0 | 0.5d | `Directory.Build.props` 定 `<Version>0.2.0</Version>`；`launcher_version` 读程序集版本；删掉写死的 `"0.1"` |
| C2 | 数据目录与便携模式 | ★★ | P0 | 1d | 不再靠 `Lx.sln` 反查；规则：exe 同级有 `portable.txt` → `data/`，否则 `%APPDATA%\Lx yulin carp\.minecraft`；首次启动迁移旧 `minecraft/` 数据 |
| C3 | 单文件发布 + 运行时检测 | ★★ | P0 | 1d | `dotnet publish -c Release -r win-x64`；优先 framework-dependent（约 10 MB）+ 启动时检测 .NET 8 Desktop Runtime 并给下载链接；绿色版 zip。验收：干净 Win11 解压即用 |
| C4 | 自更新 | ★★★ | P0 | 1.5d | 静态 `update.json`（version/url/sha256/notes/minVersion）托管 GitHub Releases；后台检查 → 下载到 `%TEMP%` → 校验 → 启动 updater 等主进程退出 → 改名旧 exe → 覆盖 → 重启；失败回滚。验收：0.2.0 → 0.2.1 一键成功，实例/设置不丢 |
| C5 | 更新通道与回滚 | ★★ | P1 | 0.5d | `stable/beta` 两通道；保留上一版 zip；「回滚到上一版」按钮 |

> C4 的坑：运行中的 exe 不能直接覆盖，必须"改名旧文件 + 写新文件 + 延迟删除"；无签名的自更新 exe 容易触发 SmartScreen，见第 6 节。

### 3.4 D · 加载器全图形化（5 天）

| 编号 | 任务 | 难度 | 优先级 | 预估 | 要点 / 验收 |
|---|---|---|---|---|---|
| D1 | 加载器下拉 + 版本来源 | ★★ | P0 | 1.5d | Vanilla / Fabric / Quilt / Forge / NeoForge；各自 meta（Fabric meta、Quilt meta、Forge promotions、NeoForge maven）拉版本并缓存；默认最新稳定版 |
| D2 | 图形化安装进度 | ★★ | P0 | 1d | 复用 `ForgeInstaller`/`FabricInstaller`，加阶段回调（下载/解压/patcher/合并）；可展开日志、可取消 |
| D3 | Quilt 支持 | ★★ | P1 | 1d | 与 Fabric 同构（meta + maven），补 `quilt-loader` 依赖；至少测 1.20.1 |
| D4 | 实例绑定 loader/loaderVersion | ★ | P0 | 0.5d | `Instance` 增加 `LoaderVersion`；切换下拉时提示"修改当前实例 / 新建实例" |
| D5 | 安装失败回滚 | ★★ | P1 | 1d | 装到临时目录 → 成功后原子替换 `versions/<id>`；失败清理，不污染已有版本 |

> 现状：`--install-loader` 已经能装 Forge/NeoForge，D 的核心是"把已有能力包上 UI + 进度 + 回滚"，不是重写安装器。

### 3.5 E · 网络与下载（5 天）

| 编号 | 任务 | 难度 | 优先级 | 预估 | 要点 / 验收 |
|---|---|---|---|---|---|
| E1 | 下载源切换 | ★★ | P0 | 1.5d | official / BMCLAPI / 自定义；统一 URL 重写层：piston-meta、launchermeta、resources、libraries/maven；Modrinth/CF 不走镜像；源健康检查（HEAD）+ 失败自动回退。验收：BMCLAPI 下 1.20.4 全量成功 |
| E2 | 代理设置 | ★ | P1 | 0.5d | 系统代理 / 手动 HTTP 代理 / 直连；只作用于下载 HttpClient |
| E3 | Range 断点续传 | ★★ | P1 | 1d | 保留 `.part` + `.part.meta`（url/etag/size）；`Range: bytes=N-`；处理 416；完成后仍校验 sha1 |
| E4 | 校验/修复页 + 校验缓存 | ★★★ | P0 | 1.5d | 扫描版本/实例 → 缺失/损坏列表 → 一键修复；快速模式（size+mtime）与完整模式（sha1）分开；校验结果写缓存，启动时不再全量 hash（同时解决缺口 #7） |
| E5 | 首次安装向导 | ★★ | P0 | 0.5d | 选版本/加载器/目录/下载源 → 预下载（显示总量/速度/剩余）→ 完成；不要等点「启动」才黑箱下 700 MB |

### 3.6 F · 界面骨架（5 天，可与 D 并行）

> 这一轮**不重写 MVVM**，只拆分页面 + 统一资源；MVVM 留到长期 H3，避免一次改太多。

| 编号 | 任务 | 难度 | 优先级 | 预估 | 要点 / 验收 |
|---|---|---|---|---|---|
| F1 | 侧栏导航 + UserControl 拆分 | ★★ | P1 | 1.5d | 首页 / 版本 / 实例 / 资源 / 账号 / 设置 / 日志；每个页面 code-behind ≤150 行 |
| F2 | 首页 | ★★ | P1 | 1d | 实例卡片（图标/版本/加载器/最后游玩）、大启动按钮、当前账号；启动中显示阶段与进度 |
| F3 | 版本页 | ★ | P0 | 0.5d | 920 个版本可搜索、按类型（release/snapshot/old）筛选、标出"已安装"；安装按钮接 D |
| F4 | 设置页重组 | ★ | P1 | 1d | 游戏（内存/Java/JVM 参数/窗口大小）、下载（源/并发/代理）、启动器（更新通道/语言/主题）、关于 |
| F5 | 日志页 | ★ | P1 | 1d | 读 `logs/`，级别过滤、搜索、复制、打开目录；崩溃后默认跳到这里 |
| F6 | 高 DPI + 主题资源 | ★★ | P2 | 1d | PerMonitorV2；颜色/字号进 ResourceDictionary；先做浅色，深色可后补 |

### 3.7 速赢清单（穿插做，每项 ≤1 天）

| 任务 | 难度 | 优先级 | 说明 |
|---|---|---|---|
| Forge/NeoForge mod 元数据 | ★ | P1 | 读 `META-INF/mods.toml` / `neoforge.mods.toml`，列表不再只显示文件名 |
| 版本下拉可输入搜索 | ★ | P1 | `IsEditable` + `CollectionViewSource` 过滤，立刻改善 920 项体验 |
| 日志"复制全部 / 打开目录" | ★ | P1 | 排错时用户能一键发给你 |
| 实例右键菜单 | ★ | P1 | 打开文件夹、复制路径、打开日志 |
| 显示/复制完整命令行 | ★ | P2 | `--debug` 已有，UI 加只读窗口，方便对账 |
| 关闭窗口时游戏在运行 → 确认 | ★ | P1 | 防误关 |
| 记住窗口大小/位置 | ★ | P2 | 写入 settings |
| 崩溃后自动打开 latest.log | ★ | P1 | 配合 A5 |
| mods 列表显示大小/来源 | ★ | P2 | 来源可从 Modrinth 记录 |

### 3.8 v0.2 完成定义

> 2026-10-31 复核：对应功能均已由 m5–m8 实现（见 §0.1），方框仍未勾选，因为还需要按 `RELEASE.md` §6 手动验收一遍。

- [ ] 干净 Windows：解压 → 选版本/加载器 → 下载 → 启动进主菜单，全程 GUI，无命令行
- [ ] 任意一次失败都能在界面看到原因，并且 `logs/` 里有堆栈
- [ ] 正版登录一次，7 天内启动不再要求设备码
- [ ] 断网、切换镜像、取消、续传、修复文件，五种情况都不崩
- [ ] 自带版本号，能检测并完成一次 0.2.0 → 0.2.1 自更新
- [ ] 同时只开一个窗口；游戏退出后状态正确恢复
- [ ] `dotnet test` ≥ 41 个用例全绿（新增：URL 重写、续传、DPAPI 往返、版本比较）

---

## 4. 长期计划：v0.3 / v1.0（第 7–20 周）

> 短期解决"能用"，长期解决"值得用"和"敢发布"。原则：一次只推进一个 H 包，不同时开三条战线。

### 4.1 H1 · mod 生态（第 7–10 周，10 天）✅ 已完成（m7-1…m7-5）

| 任务 | 难度 | 优先级 | 预估 | 验收 |
|---|---|---|---|---|
| Modrinth 搜索/详情/安装（按游戏版本 + 加载器过滤） | ★★★ | P0 | 3d | 搜 sodium → 安装 → 自动带上依赖 → 启动成功 |
| CurseForge 搜索/安装（API key，遵守 attribution） | ★★★ | P0 | 2.5d | 与 Modrinth 共用"可安装版本"抽象 |
| 依赖递归解析 + 冲突检测 | ★★★ | P0 | 2d | 缺依赖/重复 mod id/加载器不匹配，安装前给出人话提示 |
| 批量更新 + 更新前备份 | ★★ | P1 | 1.5d | 一次更新 10 个 mod，失败可回滚 |
| 拖入 jar / 打开 mods 目录 / 禁用原因说明 | ★ | P1 | 1d | 拖拽即装，重复文件不覆盖 |

**风险**：两个平台的版本/依赖模型不同，先定一个内部 `ModVersion` 模型再写两个适配器；CurseForge 有 API 条款，不能把 key 打进客户端（让用户填或走代理服务）。

### 4.2 H2 · 整合包与服务器同步（第 9–13 周，12 天）★ 差异化　🚧 最小版已完成（m10-3…m10-6，见 `SYNC.md`）

> 这是 `02-DESIGN-NOTES` 里"给服主一键发整合包"的落点，也是唯一值得对外讲的故事。

| 任务 | 难度 | 优先级 | 预估 | 验收 |
|---|---|---|---|---|
| `pack.json` 规范（mods + hash + 配置 + 加载器版本） | ★★ | P0 | 1d | 版本号 + 文件清单 + 删除清单，向后兼容 |
| 服务端导出（从实例生成包） | ★★ | P0 | 2d | 导出后另一台机器能 1:1 还原 |
| 客户端增量同步（下载/删除/校验） | ★★★ | P0 | 3d | 服主改 3 个 mod → 玩家点同步 30 秒内对齐 |
| 同步前快照 + 失败回滚 | ★★★ | P1 | 2d | 同步失败能退回原状态 |
| 一键进服（同步后自动连服务器） | ★★ | P1 | 1d | `--server/--port` 参数 + 实例绑定 |
| 托管方式（HTTP 静态 / 对象存储） | ★★ | P1 | 1d | 先用任意静态 URL，不做账号体系 |

### 4.3 H3 · 架构与性能（第 11–15 周，12 天）⏳ 部分完成（Core/Cli 已拆，其余见 §11 阶段 2）

| 任务 | 难度 | 优先级 | 预估 | 验收 |
|---|---|---|---|---|
| 抽 `Lx.Core`（net8.0）+ `Lx.Cli` + `Lx.App` | ★★★ | P1 | 3d | UI 项目零业务逻辑；CLI 仍可用 |
| MVVM（CommunityToolkit.Mvvm）+ 轻量 DI | ★★★ | P1 | 3d | 页面无事件式业务；可单元测试 ViewModel |
| 全链路 `CancellationToken` + 统一错误模型 | ★★ | P1 | 1.5d | 取消/超时/失败有统一类型，不再到处 catch |
| 状态缓存（SQLite 或 JSON+索引） | ★★★ | P1 | 2.5d | 热启动校验 <500 ms，不再全量 sha1 |
| 结构化日志 + 敏感信息脱敏 | ★★ | P1 | 1d | 日志可附到 issue，自动隐藏 token |
| 清 nullable 警告、开 `TreatWarningsAsErrors` | ★ | P1 | 1d | 0 warning |

### 4.4 H4 · 发布工程（第 14–17 周，6 天）⏳ 部分完成（CI / 单文件 / Setup / 自更新）

| 任务 | 难度 | 优先级 | 预估 | 验收 |
|---|---|---|---|---|
| CI（GitHub Actions）：build + test + 产物 | ★★ | P1 | 1.5d | push/PR 自动跑，tag 自动出 zip + update.json |
| 安装器（Inno Setup 或 MSIX） | ★★ | P1 | 1.5d | 安装/卸载/升级/文件关联（.mrpack） |
| 代码签名 | ★★ | P1 | 1d | 减少 SmartScreen 拦截（OV 证书约 ¥1–2k/年，可选） |
| 崩溃上报（opt-in，自建或 Sentry） | ★★ | P2 | 1.5d | 用户同意才上传；含版本/日志摘要/堆栈 |
| 隐私说明 + 遥测开关 | ★ | P2 | 0.5d | 默认关闭，明示收集范围 |

### 4.5 H5 · 账号与个性化（第 15–17 周，4 天）⏳ 未开始

| 任务 | 难度 | 优先级 | 预估 | 验收 |
|---|---|---|---|---|
| 多微软账号/档案切换 | ★★ | P1 | 1.5d | 同账号多档案、快速切换 |
| 皮肤/披风预览 | ★★ | P2 | 1d | 头像 + 3D 预览（离线可缓存） |
| 离线皮肤/自定义 | ★★ | P2 | 1d | 本地皮肤注入 |
| 账号页信息完整化（类型/过期时间/站点） | ★ | P1 | 0.5d | 用户知道"为什么又要登录" |

### 4.6 H6 · 国际化与可访问性（第 16–18 周，5 天）⏳ 未开始

| 任务 | 难度 | 优先级 | 预估 | 验收 |
|---|---|---|---|---|
| i18n（resx 或 JSON），zh-CN / en | ★★ | P2 | 2d | 切换即时生效，无硬编码中文 |
| 深浅主题 + 跟随系统 | ★★ | P2 | 1.5d | 两套主题截图对比 |
| 键盘导航/焦点可见/高对比 | ★★ | P2 | 1d | 全流程可不用鼠标 |
| 字体/缩放适配（125%/150%） | ★ | P1 | 0.5d | 不裁切、不重叠 |

### 4.7 H7 · 平台扩展（明确暂缓）

Linux/macOS 不是"编译一下"：WPF 不支持，要换 Avalonia 重写 UI，还要处理 LWJGL natives、路径大小写、权限。**建议 v1.0 之前不做**，除非目标用户明确要求。

### 4.8 里程碑与出口条件

| 里程碑 | 时间 | 出口条件 |
|---|---|---|
| v0.2 内部日用 | 第 6 周末 | 3.8 清单全勾 |
| v0.3 小范围公测 | 第 13 周末 | H1 + H2 最小版 + 实例增强（改名/复制/导入/导出）+ 无 P0 缺陷 |
| v1.0 公开发布 | 第 18–20 周 | H3/H4 完成、崩溃率 <1%、启动成功率 >98%、有签名安装包与回滚通道 |

---

## 5. 建议的每周节奏

| 时间 | 动作 |
|---|---|
| 周一 | 从本表挑 1 个任务，写清验收步骤（不超过 1 天粒度） |
| 每天 | 一个可运行的小 commit；跑 `dotnet test`；日志里能看到这一步 |
| 周五 | 手动走一遍 v0.2 验收清单；打 tag（`v0.2.0-alpha.N`）；更新本文件状态 |
| 每两周 | 删一次 backlog：两周没动的需求直接砍掉或降到 P2 |

---

## 6. 风险登记

| 风险 | 影响 | 应对 |
|---|---|---|
| 无签名 exe + 自更新触发 SmartScreen/杀软 | 用户不敢装 | 尽早买 OV 签名；自更新保留手动下载兜底；不要用可疑的"静默提权" |
| 自更新替换运行中的 exe 失败 | 启动器变砖 | updater 独立进程 + 旧版备份 + 失败自动回滚；先小范围灰度 |
| 微软 OAuth 设备码体验差、refresh token 90 天不活跃失效 | 留存差 | 做好"过期前提醒"；文档写清；后续可换 WebView2 授权码流 |
| CurseForge API 条款/限流 | 功能被封 | key 不进客户端；缓存；失败降级到 Modrinth |
| BMCLAPI 非官方，可能限流/停服 | 下载失败 | 官方源自动回退；源健康检查；自定义源 |
| 数据目录放在 Program Files 无写权限 | 启动即失败 | 默认 `%APPDATA%\Lx yulin carp\.minecraft`；便携模式仅在可写目录启用 |
| 版本 JSON/加载器格式变动（1.21.5+、NeoForge 迭代快） | 启动失败 | 固定样本回归测试；未知字段忽略；错误信息带版本号 |
| 范围膨胀（mod 商店 + 主题 + 社交一起做） | 烂尾 | 一次只开一个 H 包；P2 默认不做 |

---

## 7. 明确不做（写下来防止跑偏）

- 不做盗版/绕过正版验证、不内置盗版资源站。
- 不做主题商店、付费解锁、赞助加速。
- 不做内置社交/聊天/云存档（至少 v1.0 前）。
- 不做移动端；Linux/macOS 暂缓。
- 不抄任何启动器的代码、图标、配色、文案（延续 `02-DESIGN-NOTES` 的洁净室规则）。

---

## 8. 度量（有数据才知道有没有变好）

| 指标 | 目标 | 采集方式 |
|---|---|---|
| 首次安装成功率 | ≥ 95% | 本地日志 + 可选匿名上报 |
| 启动成功率（到主菜单） | ≥ 98% | 启动结果埋点（本地统计即可） |
| 热启动耗时（已安装，点启动→进程创建） | < 3 s | 日志时间戳 |
| 文件校验耗时（热） | < 500 ms | 日志时间戳 |
| 自更新成功率 | ≥ 99% | updater 日志 |
| 崩溃率（会话） | < 1% | 全局异常计数 |

---

## 9. 接下来 7 天就可以动手

| 天 | 做什么 | 改哪里 |
|---|---|---|
| D1 | 全局异常 + 文件日志 | `App.xaml.cs`、新增 `Diagnostics/Log.cs` |
| D2 | 下载失败进 UI、可复制 | `Downloader.cs`、`MainWindow` 日志区 |
| D3 | `accounts.json` DPAPI 加密 + 迁移 | `AccountStore.cs`、`Account.cs` |
| D4 | 微软 refresh token 字段与续期 | `MicrosoftAuth.cs`、`Account.cs` |
| D5 | 加载器下拉骨架（先 Fabric/Forge/NeoForge） | `MainWindow.xaml(.cs)`、`ForgeInstaller` |
| D6 | 下载源设置 + BMCLAPI 重写层 | 新增 `Resolve/DownloadSource.cs`、`ManifestService`、`DownloadManifest` |
| D7 | 单文件发布 + 从干净目录试跑 | `Lx.UI.csproj`、新增 `publish.ps1` |

> 每天结束都要能 `dotnet build` + `dotnet test` 绿，并且有一个 commit。做不完就拆，不要攒。

---

## 10. 维护这份计划

- 完成一项就把表格里的文字加删除线或打勾，别删行（保留决策历史）。
- 新需求先写进"候选池"，等当前迭代结束再排；不允许中途插队到 P0。
- 每个季度回看一次：P2 超过 3 个月没动 → 删除；风险表新增一项 → 必须有应对。

---

## 11. 下一轮：step m10-x（2026-10-31 起）

> 沿用 §2 的 DoD：一个 step 一个 commit（`step m10-N: …`），`dotnet build` + `dotnet test` 必须绿，验收步骤写进 commit message。
> 顺序是“先能发、再能同步、最后还债”，同一时间只推进一个阶段。

### 阶段 0 · 把 1.4.0 发出去（0.5 天）

| Step | 做什么 | 预估 | 验收 |
|---|---|---|---|
| m10-0 | 配 GitHub remote、打 `v1.4.0` tag、推送触发 `release.yml`；`update.json` 的 `url` 指向 Release 资产；按 `RELEASE.md` §6 过 8 条 | 0.5d | Release 页有 zip / Setup / update.json；旧版能检测并更新到 1.4.0，失败可回滚 |

### 阶段 1 · v0.3 出口条件（约 6 天）

| Step | 做什么 | 预估 | 主要位置 | 验收 |
|---|---|---|---|---|
| m10-1 | 实例增强：改名、复制（新目录、不共享存档）、删除前确认（仅记录 / 连文件）、打开目录 ✅ | 1d | `Lx.Core/Instances/`、实例页 | 改名后 `instances.json` 与目录同步；复制出两份各自可启动；连文件删要手敲实例名 |
| m10-2 | 版本搜索 + 拖 jar 到资源页安装 ✅ | 1d | `MainWindow.xaml(.cs)`、`ModManager.AddMany` | 输入 “1.20” 立即过滤；拖入即装、重名不覆盖 |
| m10-3 | `pack.json` v1 规范 + 从实例导出包 ✅ | 1d | 新增 `Lx.Core/Sync/PackManifest.cs`、`PackFilter.cs`、`PackExporter.cs` | 导出含 mods + sha1、配置白名单、加载器版本、删除清单；另一台机器 1:1 还原 |
| m10-4 | 客户端增量同步（HTTP 静态 URL，下载 / 删除 / 校验）✅ | 1.5d | 新增 `Lx.Core/Sync/PackSync.cs` | 服主换 3 个 mod，玩家点同步只下差异，30 秒内对齐 |
| m10-5 | 同步前快照 + 失败回滚 ✅ | 1d | `Sync/SyncSnapshot.cs` | 同步中途断网，一键回滚后 sha1 与同步前一致 |
| m10-6 | 一键进服（`--server` / `--port`，实例绑定服务器）✅ | 0.5d | 实例模型 + `LaunchOptions.ExtraGameArgs` | 同步后点“进服”直接连上 |

### 阶段 2 · H3 收尾（约 5 天）

| Step | 做什么 | 预估 | 验收 |
|---|---|---|---|
| m10-7 | 统一错误模型（`LxException` + trace id）✅；`CancellationToken` 收口未做 | 1d | 取消 / 超时 / 网络失败显示同一套错误卡片，日志可追 trace id |
| m10-8 | 结构化日志（`launcher-*.jsonl`）+ token 脱敏；`TreatWarningsAsErrors` ✅ | 1d | 构建 0 warning（顺手把 SharpCompress 0.38→0.50.7 修掉 NU1902）；日志里无明文 token |
| m10-9 | 状态缓存：`cache/hashes.json` 记 路径 → sha1 / 大小 / mtime ✅ | 1.5d | 热启动校验 < 500 ms：实测 3863 个文件 3119 ms → **266 ms**（缓存命中 3863 / 重算 0） |
| m10-10 | 设置页 / 资源页 ViewModel 收尾：ViewModel 搬进 `Lx.Core`，设置校验/资源页增删筛选都归 VM ✅ | 1.5d | 两页 code-behind 只剩控件搬运；ViewModel 有单测（12 条） |

### 阶段 3 · 发布质量（按需，不阻塞 v0.3）

| Step | 做什么 | 预估 | 备注 |
|---|---|---|---|
| m10-11 | `release.yml` 自动附 `sha256.txt`、失败可重跑；tag 即发版 ✅ | 0.5d | 本地跑 release.ps1 验证通过（4 个文件校验一致）；CI 待首次 tag 触发 |
| m10-12 | 代码签名 | 0.5d | 需 OV 证书（约 ¥1–2k/年），可推迟 |
| m10-13 | 崩溃上报 opt-in + 隐私说明 / 遥测开关 ✅ | 1.5d | 默认关闭、地址留空则只存本地；实跑验证上传成功且 token 已脱敏 |

### 之后（不进本轮）

- H5：皮肤 / 披风预览、离线皮肤、多档案切换、账号页信息完整化
- H6：i18n（zh-CN / en）、深浅主题、键盘导航、125% / 150% 缩放
- C2：安卓本地 Java 版（`REMOTE.md` 已注明属后续）
- H7：Linux / macOS（v1.0 前不做）

