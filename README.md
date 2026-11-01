# Lx-yulincarp

一个 Minecraft Java 版启动器：从远古 Alpha/Beta 到最新版本全线覆盖，自动补齐 Java 运行环境，支持 Fabric / Forge / NeoForge、整合包导入与手机遥控。

![Windows](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-blue)

## 支持版本

**从远古版本到最新版，全线覆盖：**

| 版本区间 | 说明 |
|---|---|
| Alpha / Beta 远古版 | 支持启动 |
| 1.x 全系列 | 1.0 ～ 1.21 各正式版 |
| 26.x 新版 | 已支持到 **26.4** |

> 旧版本依赖的运行环境会自动补齐，无需手动装 Java。

## 下载

安装包不在仓库里，统一发布在 [Releases](https://github.com/Yulincarp/Lx-yulincarp-/releases/latest)（当前最新为 **v1.4.2**）：

| 文件 | 说明 |
|---|---|
| `Lx-yulin-carp-*-Setup.exe` | Windows 安装程序（64 位，约 74 MB），推荐 |
| `Lx-yulin-carp-*-win-x64.zip` | 免安装压缩包，解压后运行 `Lx yulin carp.exe` |
| `Lx-yulin-carp-remote-*.apk` | 手机遥控 APK，装在手机上连电脑上的启动器 |
| `update.json` | 自动更新清单；1.4.2 起客户端默认从 `releases/latest/download/update.json` 检查更新 |

> 安装包未做数字签名，首次运行可能出现 SmartScreen 提示，点「更多信息 → 仍要运行」即可。

## 安装

1. 下载 `Lx-yulin-carp-1.4.2-Setup.exe`
2. 双击运行，按提示完成安装
3. 如出现「Windows 已保护你的电脑」，点 **更多信息** → **仍要运行**
4. 启动器首次运行会自动准备所需组件

安装后的数据目录默认是 `%APPDATA%\Lx yulin carp\.minecraft`；想放到别处可在启动器「设置 → 更改数据目录」里迁移。

## 从源码构建

需要 .NET 8 SDK。

```powershell
dotnet build Lx.sln -c Release
dotnet test tests/Lx.Tests/Lx.Tests.csproj -c Release
```

出安装包（自包含单文件 + zip + Setup + update.json）：

```powershell
.\release.ps1 -Repo "Yulincarp/Lx-yulincarp-" -Notes "本次更新内容"
```

手机遥控 APK（需要 Android workload）：

```powershell
.\build-android.ps1
```

## 目录结构

```text
src/Lx.Core      启动内核、下载、账号、加载器、整合包、自更新
src/Lx.UI        WPF 界面
src/Lx.Cli       命令行入口
src/Lx.Remote    手机遥控 HTTP 服务
mobile/          Android WebView 外壳（遥控端）
tests/Lx.Tests   单元测试
tools/Lx.Setup   安装程序
```

## 文档

- [ROADMAP.md](ROADMAP.md) — 后续路线与进度
- [ARCHITECTURE.md](ARCHITECTURE.md) — 项目结构与约定
- [RELEASE.md](RELEASE.md) — 版本号、出包、发布清单
- [UPDATE.md](UPDATE.md) — 自更新与回滚
- [REMOTE.md](REMOTE.md) — 手机遥控与 APK

## 数据目录

默认 `%APPDATA%\Lx yulin carp\.minecraft`。想放到别处可在「设置 → 更改数据目录」里迁移；测试时也可以用环境变量 `LX_DATA_DIR` 指定。

## 自动更新

1.4.2 起默认检查 `https://github.com/Yulincarp/Lx-yulincarp-/releases/latest/download/update.json`；可用环境变量 `LX_UPDATE_URL` 或数据目录里的 `update.json` 覆盖（自建镜像时用得上）。

> 网络无法访问 GitHub 时，自动更新会失败；这种情况请手动从 Releases 下载覆盖安装。

## 系统要求

| 项目 | 要求 |
|---|---|
| 系统 | Windows 10 / 11（64 位） |
| 内存 | 4 GB 以上（跑新版本建议 8 GB+） |
| 磁盘 | 约 200 MB（不含游戏本体与存档） |

## 版本记录

### v1.4.2
- 自更新默认查 GitHub Release 的 latest/update.json，装过这版就能自动发现新版本
- 仍可用 LX_UPDATE_URL 或数据目录里的 update.json 覆盖

### v1.4.1
- 默认数据目录改为 `%APPDATA%\Lx yulin carp\.minecraft`
- 「迁移旧数据」能识别 1.4.0 及更早的默认目录，老数据不会丢
- 安装器与启动器版本号同步为 1.4.1

### v1.4.0
- 支持远古版本至 26.4 全线启动
- 安装程序与自动更新组件

## 常见问题

**Q：启动时提示缺少 Java？**
A：启动器会自动下载对应版本所需的 Java 运行环境，保持网络畅通即可。

**Q：杀毒软件报毒？**
A：安装包未做数字签名，部分杀软会误报。可加入信任列表，或核对文件大小是否为 73.97 MB。

**Q：支持正版登录吗？**
A：暂不支持
