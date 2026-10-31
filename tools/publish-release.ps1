<#
    发布 Release 资产（GitHub PAT）
    推荐：fine-grained token，只授权本仓库 Contents: Read and write
      创建：https://github.com/settings/personal-access-tokens/new
      - Repository access: Only select repositories -> Yulincarp/Lx-yulincarp-
      - Permissions -> Contents: Read and write

    用法（令牌不要粘贴进命令行，会留在 PSReadLine 历史和进程列表）：
      1) 首次保存令牌到用户目录下的私有文件：
         Read-Host "粘贴令牌" | Set-Content "$env:USERPROFILE\.lx-gh-token" -NoNewline
      2) 每次发布：
         $env:GH_TOKEN = (Get-Content "$env:USERPROFILE\.lx-gh-token" -Raw).Trim()
         .\tools\publish-release.ps1
      3) 用完清理：
         Remove-Item "$env:USERPROFILE\.lx-gh-token"; Remove-Item Env:GH_TOKEN

    若提示“禁止运行脚本”，改用：
      powershell -ExecutionPolicy Bypass -File .\tools\publish-release.ps1

    可选 -DryRun：只列出将删除/上传的资产，不发任何写请求。

    注意：不提供 -Token 参数，令牌只从环境变量 GH_TOKEN 读取，
          避免被写进命令行历史、进程列表或脚本文件里。
#>
param(
    [string]$Repo = "Yulincarp/Lx-yulincarp-",
    # 留空 = 用 Directory.Build.props 里的 Version，tag 默认 v<Version>
    [string]$Version = "",
    [string]$Tag = "",
    [switch]$DryRun
)
$ErrorActionPreference = "Stop"

# 令牌只认环境变量，不接受命令行传入
$Token = $env:GH_TOKEN

# 普通脚本会把未知命名参数静默收进 $args，这里显式拒绝，免得以为传进去了
if ($args.Count -gt 0) {
    Write-Host ("不接受额外参数：{0}" -f ($args -join ' ')) -ForegroundColor Red
    Write-Host "令牌只从环境变量 GH_TOKEN 读取，例如：`$env:GH_TOKEN = Read-Host `"粘贴令牌`"" -ForegroundColor Red
    exit 1
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    Write-Host "未找到令牌（环境变量 GH_TOKEN 为空）。" -ForegroundColor Red
    Write-Host @"
请先创建 fine-grained token（推荐，只给本仓库 Contents: Read and write）：
  https://github.com/settings/personal-access-tokens/new

然后在本窗口设置（令牌不会进命令历史）：
  `$env:GH_TOKEN = Read-Host "粘贴令牌"
"@
    exit 1
}

$Token = $Token.Trim().Trim('"').Trim([char]39)
if ($Token -match '^ghp_') {
    Write-Warning "这是 classic PAT（ghp_...），通常可读写你的所有仓库。建议改用 fine-grained token。"
} elseif ($Token -notmatch '^github_pat_') {
    Write-Host "令牌前缀不是 ghp_ 或 github_pat_，可能没复制完整（当前长度 $($Token.Length)）。" -ForegroundColor Red
    exit 1
}

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$buildProps = Get-Content (Join-Path $root "Directory.Build.props")
    $Version = $buildProps.Project.PropertyGroup.Version
}
if ([string]::IsNullOrWhiteSpace($Tag)) {
    $Tag = "v$Version"
}
$dist = Join-Path $root "dist"
$files = @(
    "Lx-yulin-carp-$Version-win-x64.zip",
    "Lx-yulin-carp-$Version-Setup.exe",
    "update.json"
)
foreach ($f in $files) {
    if (-not (Test-Path (Join-Path $dist $f))) {
        Write-Host "缺少文件：dist\$f（先跑 publish.ps1 生成）" -ForegroundColor Red
        exit 1
    }
}

# 有就一起传：校验清单和 APK（CI 不构建 APK，本地构建过才有）
foreach ($optional in @("sha256.txt", "Lx-yulin-carp-remote-$Version.apk")) {
    if (Test-Path (Join-Path $dist $optional)) {
        $files += $optional
    }
}

$api = "https://api.github.com"
$headers = @{
    Authorization          = "Bearer $Token"
    Accept                 = "application/vnd.github+json"
    "User-Agent"           = "Lx-release"
}

# 先确认令牌对该仓库真的有写权限，避免上传到一半失败
Write-Host "校验令牌与仓库权限 …"
try {
    $repoInfo = Invoke-RestMethod -Uri "$api/repos/$Repo" -Headers $headers
} catch {
    $code = $_.Exception.Response.StatusCode.value__
    switch ($code) {
        401 { Write-Host "令牌无效或已过期（401）。" -ForegroundColor Red; exit 1 }
        403 { Write-Host "令牌被拒绝（403），可能是权限不足或触发了限流。" -ForegroundColor Red; exit 1 }
        404 { Write-Host "仓库不存在，或令牌没有被授权访问 $Repo（404）。" -ForegroundColor Red; exit 1 }
        default { throw }
    }
}
if (-not ($repoInfo.permissions -and $repoInfo.permissions.push)) {
    Write-Host "令牌对 $Repo 没有 push 权限。fine-grained token 请在 Permissions -> Contents 勾 Read and write，并确认已选中该仓库。" -ForegroundColor Red
    exit 1
}
Write-Host ("仓库 OK：{0}（push={1}）" -f $repoInfo.full_name, $repoInfo.permissions.push)

Write-Host "读取 Release $Tag …"
$release = Invoke-RestMethod -Uri "$api/repos/$Repo/releases/tags/$Tag" -Headers $headers
Write-Host ("Release #{0} {1}，现有 {2} 个资产" -f $release.id, $release.name, $release.assets.Count)

# 同名旧资产先删掉（例如 10-30 那个 63 MB 的 Setup）
foreach ($asset in $release.assets) {
    if ($files -contains $asset.name) {
        if ($DryRun) {
            Write-Host ("[DryRun] 将删除旧资产：{0}（{1:N1} MB）" -f $asset.name, ($asset.size / 1MB))
        } else {
            Invoke-RestMethod -Method Delete -Uri "$api/repos/$Repo/releases/assets/$($asset.id)" -Headers $headers
            Write-Host ("已删除旧资产：{0}（{1:N1} MB）" -f $asset.name, ($asset.size / 1MB))
        }
    }
}

foreach ($f in $files) {
    $path = Join-Path $dist $f
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $name = [uri]::EscapeDataString($f)
    $upload = "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name=$name"
    if ($DryRun) {
        Write-Host ("[DryRun] 将上传 {0}（{1:N1} MB）" -f $f, ($bytes.Length / 1MB))
        continue
    }
    Write-Host ("上传 {0}（{1:N1} MB）…" -f $f, ($bytes.Length / 1MB))
    Invoke-RestMethod -Method Post -Uri $upload -Headers $headers -ContentType "application/octet-stream" -Body $bytes | Out-Null
}

if ($DryRun) {
    Write-Host "[DryRun] 结束，未做任何修改。"
    return
}

$check = Invoke-RestMethod -Uri "$api/repos/$Repo/releases/tags/$Tag" -Headers $headers
Write-Host "完成，当前资产："
$check.assets | ForEach-Object { Write-Host ("  {0}  {1:N1} MB  sha? {2}" -f $_.name, ($_.size / 1MB), $_.state) }
Write-Host ""
Write-Host "下载地址："
$check.assets | ForEach-Object { Write-Host ("  " + $_.browser_download_url) }
Write-Host ""
Write-Host "提示：发布完成后建议删除本次使用的令牌，下次再建新的。"
