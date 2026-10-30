param(
    [string]$Token = $env:GH_TOKEN,
    [string]$Repo = "Yulincarp/Lx-yulincarp-",
    # 留空 = 用 Directory.Build.props 里的 Version，tag 默认 v<Version>
    [string]$Version = "",
    [string]$Tag = ""
)
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Token)) {
    throw "先设置环境变量 GH_TOKEN（GitHub PAT，classic 勾 repo，或 fine-grained 勾 Contents: Read and write）"
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
    if (-not (Test-Path (Join-Path $dist $f))) { throw "缺少文件：dist\$f" }
}

$api = "https://api.github.com"
$headers = @{
    Authorization          = "Bearer $Token"
    Accept                 = "application/vnd.github+json"
    "User-Agent"           = "Lx-release"
}

Write-Host "读取 Release $Tag …"
$release = Invoke-RestMethod -Uri "$api/repos/$Repo/releases/tags/$Tag" -Headers $headers
Write-Host ("Release #{0} {1}，现有 {2} 个资产" -f $release.id, $release.name, $release.assets.Count)

# 同名旧资产先删掉（例如 10-30 那个 63 MB 的 Setup）
foreach ($asset in $release.assets) {
    if ($files -contains $asset.name) {
        Invoke-RestMethod -Method Delete -Uri "$api/repos/$Repo/releases/assets/$($asset.id)" -Headers $headers
        Write-Host ("已删除旧资产：{0}（{1:N1} MB）" -f $asset.name, ($asset.size / 1MB))
    }
}

foreach ($f in $files) {
    $path = Join-Path $dist $f
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $name = [uri]::EscapeDataString($f)
    $upload = "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name=$name"
    Write-Host ("上传 {0}（{1:N1} MB）…" -f $f, ($bytes.Length / 1MB))
    Invoke-RestMethod -Method Post -Uri $upload -Headers $headers -ContentType "application/octet-stream" -Body $bytes | Out-Null
}

$check = Invoke-RestMethod -Uri "$api/repos/$Repo/releases/tags/$Tag" -Headers $headers
Write-Host "完成，当前资产："
$check.assets | ForEach-Object { Write-Host ("  {0}  {1:N1} MB  sha? {2}" -f $_.name, ($_.size / 1MB), $_.state) }
Write-Host ""
Write-Host "下载地址："
$check.assets | ForEach-Object { Write-Host ("  " + $_.browser_download_url) }
