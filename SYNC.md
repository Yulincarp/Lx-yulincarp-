# Lx · 整合包与服务器同步（H2）

> 一句话：服主在自己电脑上「导出包」→ 传到任意静态托管 → 玩家填一个地址点「同步」，只下差异文件；同步前自动留快照，坏了点「回滚」；同步完点「进服」直接连服务器。

代码位置：

| 做什么 | 在哪 |
|---|---|
| `pack.json` 规范 / 读写 / 路径校验 | `src/Lx.Core/Sync/PackManifest.cs` |
| 哪些文件算整合包（白名单/黑名单） | `src/Lx.Core/Sync/PackFilter.cs` |
| 从实例导出可托管目录 | `src/Lx.Core/Sync/PackExporter.cs` |
| 增量同步（比对 / 下载 / 删除） | `src/Lx.Core/Sync/PackSync.cs` |
| 同步快照 / 回滚 / 校验 | `src/Lx.Core/Sync/SyncSnapshot.cs` |
| 界面（实例页「服务器同步」） | `src/Lx.UI/MainWindow.xaml(.cs)` |
| 命令行 | `src/Lx.Cli/Program.cs` |

---

## 1. `pack.json` v1

`pack.json` 同时干两件事：实例里记录"这个实例由哪个包管理"，托管目录里当"客户端该有哪些文件"的清单。

```json
{
  "SchemaVersion": 1,
  "PackName": "栖月港",
  "PackVersion": "2026.10.31",
  "MinecraftVersion": "1.20.4",
  "Loader": "fabric",
  "LoaderVersion": "0.15.11",
  "Server": { "Address": "mc.example.com", "Port": 25566 },
  "Files": ["config/create.toml", "mods/sodium.jar"],
  "Entries": [
    { "Path": "config/create.toml", "Sha1": "fde31e…", "Size": 7 },
    { "Path": "mods/sodium.jar", "Sha1": "3527f9…", "Size": 10 }
  ],
  "Delete": []
}
```

| 字段 | 说明 |
|---|---|
| `SchemaVersion` | 目前 `1`；读到更高版本会提示"先更新启动器" |
| `PackName` / `PackVersion` | 显示用；版本号变化是玩家那边"有新版本"的唯一依据 |
| `MinecraftVersion` / `Loader` / `LoaderVersion` | 同步后写回实例，避免玩家自己选错版本 |
| `Server` | 可选；填了就能「进服」，客户端用 `--server` / `--port` |
| `Entries` | 受管文件：相对路径（`/` 分隔）+ sha1 + 大小；`Url` 可覆盖单文件地址 |
| `Files` | v0 兼容字段，保存时始终与 `Entries` 的路径一致 |
| `Delete` | 作者显式要求删的文件；客户端还会自动对比上一版清单补全 |
| `SourcePath` | 只在实例里出现（记录从哪导入/同步的），导出时不写 |

**向后兼容**：v0 的 `pack.json`（只有 `SourcePath` / `PackName` / `PackVersion` / `Files`）可以直接读，此时没有哈希，同步按"本地有就当对上"处理；也可以调用 `FillHashes` 补一遍哈希。

**安全**：清单里所有路径都过 `PackManifest.NormalizePath`，`..`、绝对路径、盘符一律拒绝，防止清单把文件写到实例目录外。

---

## 2. 服主：导出包

界面：实例页 → 选中实例 → **导出包** → 选一个目录。
会在里面生成 `<实例名>-pack/`：

```
栖月港-pack/
├── pack.json            ← 清单（含 sha1/大小/加载器版本/服务器）
├── mods/*.jar
└── config/**
```

命令行（等价）：

```powershell
dotnet src/Lx.Cli/bin/Debug/net8.0/Lx.Cli.dll `
  --root "$env:APPDATA\Lx" `
  --game-dir "$env:APPDATA\Lx\instances\栖月港" `
  --pack-export --out D:\publish\栖月港 `
  --pack-version 2026.10.31 `
  --server mc.example.com:25566
```

默认收：`mods/ config/ defaultconfigs/ kubejs/ scripts/ datapacks/ patchouli_books/ customnpcs/`。
默认不收（玩家自己的东西，永远不覆盖）：`saves/ logs/ crash-reports/ screenshots/ resourcepacks/ shaderpacks/`、`options.txt`、`servers.dat`、快照目录 `*.bak / *.disabled` 等。规则在 `PackFilter.DefaultExcludes`，改之前想清楚"这是包的一部分还是玩家的"。

导出是幂等的：同目录再导一次，上一版有、这一版没有的文件会从托管目录删掉，避免玩家拿到过期文件。

---

## 3. 托管

只要能把目录按原路径提供成 HTTP 即可，没有账号体系、不需要服务端程序：

| 方式 | 注意 |
|---|---|
| Nginx / Apache / IIS | 大文件建议开 Range；`pack.json` 别缓存（`Cache-Control: no-cache`） |
| 对象存储（OSS/COS/S3/R2） | 上传整个目录；`pack.json` 设 `no-cache`，jar 可以长缓存 |
| GitHub Pages / Cloudflare Pages | 单文件 <100 MB，mods 多的话会超，建议对象存储 |

玩家填的地址就是 `<基地址>/pack.json`，例如 `https://cdn.example.com/packs/pve/pack.json`。
服务端的地址也可以写进托管目录的 `pack.json`（导出时带 `--server`），玩家同步后自动填好。

---

## 4. 玩家：同步 + 进服

界面：实例页 → 填同步地址 → **同步**。

同步做的事：

1. 读远端 `pack.json`；
2. 逐文件比 `sha1 + 大小`，只下载不一致/缺失的（HTTP 走 `Downloader`：断点续传、3 次退避重试、镜像回退、sha1 校验）；
3. 删掉"上一版清单有、这一版没有"的文件（**只删清单管过的**，不碰存档和玩家自己的 mod）；
4. 成功才写新 `pack.json`；失败保持旧版本，并提示可回滚；
5. 把 `MinecraftVersion / Loader / LoaderVersion / Server` 写回实例。

命令行：

```powershell
dotnet ... --root ... --game-dir ... --pack-sync https://cdn.example.com/packs/pve/pack.json
# 只看计划、不下载：
dotnet ... --game-dir ... --pack-sync <地址> --pack-dry-run
```

同步完成后点 **进服**：启动参数追加 `--server <地址> --port <端口>`（同一参数会先摘掉旧值，避免重复）。

---

## 5. 快照与回滚

每次同步（哪怕是"零差异"）都会在 `<实例>/.lx-snapshots/<时间戳>/` 留一份：

- `files/`：将要被覆盖/删除的旧文件；
- `snapshot.json`：每个文件的旧 sha1 + 旧 `pack.json`。

回滚 = 旧文件放回去 + 同步时新建的文件删掉 + `pack.json` 还原，然后逐个比对 sha1，结果为 0 才算"回到同步前"。

界面：**回滚**（默认最近一次）/ **快照**（看列表）。
命令行：

```powershell
dotnet ... --game-dir <实例> --snapshots
dotnet ... --game-dir <实例> --pack-rollback
dotnet ... --game-dir <实例> --pack-rollback --snapshot 20261031-031606
```

默认保留最近 10 份，更旧的自动删。快照体积 ≈ 受影响的 mods 大小，同步前会先提示。

---

## 6. 当前限制（下一轮再补）

- 只做"整文件 sha1 比对"，没有二进制差分/压缩包内比对；网络差时首次同步仍是全量。
- 没有包签名：`sha1` 只能证明"下全了"，不能证明"是服主发的"。要防篡改得再加一步签名（Ed25519），下轮做。
- 不做账号体系/权限，托管方自己控制 URL 可见性。
- 同步会覆盖 `config/**`：玩家自己改过的配置会被服主的版本替换（这是"1:1 还原"的代价，已在快照里留底）。
- 尚未做"进服前强制校验"：玩家可以跳过同步直接启动。

---

## 7. 验收清单（手工，10 分钟）

1. 实例里放 2 个 mod、1 个 config，导出到 `D:\publish\test`；
2. 用 `python -m http.server 8000 -d D:\publish\test` 起静态服务；
3. 另建空实例，同步地址填 `http://127.0.0.1:8000/pack.json`，点同步 → 文件出现、mods 数量正确；
4. 删掉导出目录里一个 mod、改一个 config，重新导出；
5. 玩家再点同步 → 只下载变化项，旧 mod 被删；
6. 点回滚 → sha1 回到第 3 步的状态（日志里"回滚校验通过"）；
7. `pack.json` 带 `--server` 导出时，点进服能直接连上（没有服务器就只验证命令行里有 `--server`）。
