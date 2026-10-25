# 启动器自更新说明

> 相关代码：`src/Lx.Probe/Updates/UpdateService.cs`  
> 只有 **publish.ps1 生成的发布版** 会启用自更新；`dotnet run` 的开发版不会。

## 1. 发一个新版本

1. 修改 `Directory.Build.props`：
   ```xml
   <Version>0.3.1</Version>
   <AssemblyVersion>0.3.1.0</AssemblyVersion>
   <FileVersion>0.3.1.0</FileVersion>
   ```
2. 生成发布包：
   ```powershell
   .\publish.ps1
   ```
3. 计算 zip 的 SHA-256：
   ```powershell
   Get-FileHash .\dist\Lx-0.3.1-win-x64.zip -Algorithm SHA256
   ```
4. 写 `update.json`：
   ```json
   {
     "version": "0.3.1",
     "url": "https://你的域名/Lx-0.3.1-win-x64.zip",
     "sha256": "把上一步的哈希填这里（小写）",
     "notes": "修复了 xxx"
   }
   ```
5. 把 zip 和 `update.json` 传到静态服务器/对象存储。

## 2. 客户端怎么检查

发布版会按这个顺序找 `update.json`：

1. 环境变量 `LX_UPDATE_URL`（可以是 https，也可以是本地路径/`file:///`）
2. 数据目录下的 `update.json`（默认 `%APPDATA%\Lx\update.json`）

然后在「设置」里点「检查更新」。

- 启动时也会后台检查一次，但只写日志：  
  `[更新] 发现新版本 v0.3.1（当前 v0.3.0），可在「设置」里更新`
- 点检查后：下载 → 校验 SHA-256 → 解压 → 询问 → 退出并替换 → 启动新版本。

## 3. 本地测试

不需要服务器：

1. 用旧版发布包解压到一个测试目录，例如 `C:\LxTest\app`；
2. 新版本 zip 放 `C:\LxTest\Lx-0.3.1-win-x64.zip`；
3. 在数据目录写 `update.json`，`url` 用绝对文件 URI：
   ```json
   {
     "version": "0.3.1",
     "url": "file:///C:/LxTest/Lx-0.3.1-win-x64.zip",
     "sha256": "..."
   }
   ```
4. 用 `LX_DATA_DIR` 指向数据目录启动旧版：
   ```powershell
   $env:LX_DATA_DIR = 'C:\LxTest\data'
   C:\LxTest\app\Lx.UI.exe
   ```
5. 设置 → 检查更新。

## 4. 替换与回滚

更新时 PowerShell 会：

1. 等主进程退出；
2. 把旧 `Lx.UI.exe` / `run.cmd` 备份到数据目录：  
   `updates\backup-<版本>-<时间>\`
3. 覆盖新文件并启动；
4. 新进程 5 秒内退出 → 自动把备份复制回去并重启旧版。

手动回滚：

```powershell
Copy-Item .\updates\backup-0.3.1-20261025-194852\* .\ -Force
```

## 5. 注意

- 更新包必须是 `publish.ps1` 生成的 zip（根目录有 `Lx.UI.exe`）。
- 目标目录必须可写；装在 `Program Files` 下需要管理员权限。
- 单文件版启动时会把原生库解包到 `%LOCALAPPDATA%\Lx\bundle`；如果系统 TEMP 不可写，用发布包里的 `run.cmd` 启动。
- 更新只替换 `Lx.UI.exe` / `run.cmd`，不会动 `portable.txt` 和数据目录。
