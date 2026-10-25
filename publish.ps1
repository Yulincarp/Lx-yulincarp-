<#
    Lx yulin carp 发布脚本
    默认：自包含单文件（解压即用）
    可选：-FrameworkDependent（约 10 MB，需要 .NET 8 Desktop Runtime）
#>
param(
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent,
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Directory.Build.props 里没有 Version"
}

$selfContained = if ($FrameworkDependent) { "false" } else { "true" }
$mode = if ($FrameworkDependent) { "framework-dependent" } else { "self-contained" }
$product = "Lx yulin carp"
$slug = "Lx-yulin-carp"
$out = Join-Path $root "dist\$slug-$version-$Runtime"

if (Test-Path $out) {
    Remove-Item -Recurse -Force $out
}

Write-Host "发布 $product $version ($mode, $Runtime)…"
dotnet publish "src\Lx.UI\Lx.UI.csproj" `
    -c Release `
    -r $Runtime `
    --self-contained $selfContained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $out

# 发布产物当前叫 Lx.UI.exe；改成产品名，外观/程序集不变。
$exePath = Join-Path $out "Lx.UI.exe"
$productExe = Join-Path $out "$product.exe"
if (-not (Test-Path $exePath)) {
    throw "发布输出里没有 Lx.UI.exe"
}
Move-Item -LiteralPath $exePath -Destination $productExe -Force

# 如果系统 TEMP 不可写，单文件解包会失败；run.cmd 把解包目录指到可写的 LocalAppData
$runCmd = @"
@echo off
set "DOTNET_BUNDLE_EXTRACT_BASE_DIR=%LOCALAPPDATA%\$product\bundle"
start "" "%~dp0$product.exe"
"@
Set-Content -Path (Join-Path $out "run.cmd") -Value $runCmd -Encoding ASCII

if (-not $NoZip) {
    $zip = "$out.zip"
    if (Test-Path $zip) {
        Remove-Item -Force $zip
    }

    Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -Force
    Write-Host "ZIP: $zip"
}

Write-Host "版本: $version"
Write-Host "输出: $out"
Write-Host ""
Write-Host "提示："
Write-Host "  - 默认数据目录是 %APPDATA%\$product"
Write-Host "  - 在 exe 旁边放 portable.txt 可切换为 exe 同级 data\（便携模式）"
Write-Host "  - 设置 LX_DATA_DIR 可强制指定数据目录"
