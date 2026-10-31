# Lx-yulincarp

一个 Minecraft Java 版启动器：从远古 Alpha/Beta 到最新版本全线覆盖，自动补齐 Java 运行环境，支持 Fabric / Forge / NeoForge、整合包导入与手机遥控。

![Windows](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-blue)

## 下载

安装包不在仓库里，统一发布在 [Releases](https://github.com/Yulincarp/Lx-yulincarp-/releases/latest)：

| 文件 | 说明 |
|---|---|
| `Lx-yulin-carp-*-Setup.exe` | Windows 安装程序（64 位），推荐 |
| `Lx-yulin-carp-*-win-x64.zip` | 免安装压缩包，解压后运行 `Lx yulin carp.exe` |
| `Lx-yulin-carp-remote-*.apk` | 手机遥控 APK，装在手机上连电脑上的启动器 |
| `update.json` | 自动更新清单；1.4.2 起客户端默认从 `releases/latest/download/update.json` 检查更新 |

> 安装包未做数字签名，首次运行可能出现 SmartScreen 提示，点「更多信息 → 仍要运行」即可。

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
