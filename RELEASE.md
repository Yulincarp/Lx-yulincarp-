# Lx 发布工程

## 1. 版本号

唯一来源：`Directory.Build.props`

```xml
<Version>1.4.0</Version>
<AssemblyVersion>1.4.0.0</AssemblyVersion>
<FileVersion>1.4.0.0</FileVersion>
<InformationalVersion>1.4.0</InformationalVersion>
```

不要在其他地方写死版本号。

## 2. 本地出包

```powershell
cd C:\Users\百合园小瑶\Lx
# 如果本机禁止运行脚本：
Set-ExecutionPolicy -Scope Process Bypass -Force
.\release.ps1 -Repo "你的GitHub用户名/Lx" -Notes "本次更新内容"

# 或者直接：
.\release.cmd -Repo "你的GitHub用户名/Lx" -Notes "本次更新内容"
```

产物：

```text
dist\Lx-yulin-carp-1.4.0-win-x64\
dist\Lx-yulin-carp-1.4.0-win-x64.zip
dist\Lx-yulin-carp-1.4.0-Setup.exe
dist\update.json
```

`release.ps1` 会：

1. 检查 tag（CI 中）与版本号一致
2. 跑 Release 测试
3. 调 `publish.ps1` 出自包含单文件版 + zip
4. 计算 SHA-256
5. 生成 `update.json`

beta 通道：

```powershell
.\release.ps1 -Channel beta
```

生成 `update-beta.json`。

## 3. GitHub Actions

- `.github/workflows/ci.yml`：push/PR 自动 build + test
- `.github/workflows/release.yml`：推 `v*` tag 自动出包并创建 GitHub Release

发版：

```powershell
git tag v1.4.0
git push origin v1.4.0
```

工作流会上传：

- `Lx-yulin-carp-1.4.0-win-x64.zip`
- `Lx-yulin-carp-1.4.0-Setup.exe`
- `update.json`

## 4. 自更新地址

客户端按通道找：

- stable：数据目录 `update.json`
- beta：数据目录 `update-beta.json`
- 或环境变量 `LX_UPDATE_URL` 强制指定

把 `update.json` 里的 `url` 指向 Release 资产即可。GitHub Release 资产地址形如：

```text
https://github.com/OWNER/REPO/releases/download/v1.4.0/Lx-yulin-carp-1.4.0-win-x64.zip
```

## 5. 代码签名

当前默认**未签名**，首次下载可能触发 SmartScreen。正式公开发布建议购买 OV/EV 代码签名证书，并在 CI 里用 `signtool` 签：

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 ^
  /f cert.pfx /p "$env:CERT_PASSWORD" "dist\Lx-yulin-carp-1.4.0-win-x64\Lx yulin carp.exe"
```

签名后再重新压缩 zip、重新计算 `update.json` 的 sha256。

## 6. 发布检查清单

- [ ] `dotnet test` 全绿
- [ ] 干净 Windows 解压 zip 能启动
- [ ] 首次启动数据目录正确（`%APPDATA%\Lx yulin carp` 或 portable `data`）
- [ ] 自更新 0.3.x → 1.4.0 成功、失败能回滚
- [ ] 实例/账号/设置不丢
- [ ] `update.json` 的 sha256 与 zip 一致
- [ ] Release notes 写清破坏性变更
- [ ] 保留上一版 zip 和 `update.json` 以便回滚
