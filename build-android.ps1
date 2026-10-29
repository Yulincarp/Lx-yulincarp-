<#
    Build the Lx mobile remote APK.
    Requires: .NET 8 Android workload + Android SDK (platform 34, build-tools 34.0.0).
#>
param(
    [string]$AndroidSdk = "C:\Android\Sdk",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ([string]::IsNullOrWhiteSpace($version)) { throw "Directory.Build.props 里没有 Version" }

$env:ANDROID_HOME = $AndroidSdk
$env:ANDROID_SDK_ROOT = $AndroidSdk

Write-Host "构建 Android 遥控端 $version…"
dotnet build "mobile\Lx.Remote.Android\Lx.Remote.Android.csproj" `
    -c $Configuration `
    --nologo `
    -p:AndroidSdkDirectory=$AndroidSdk

$apk = Get-ChildItem "mobile\Lx.Remote.Android\bin\$Configuration\net8.0-android" -Filter "*-Signed.apk" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $apk) { throw "没有找到已签名 APK" }

$target = Join-Path $root "dist\Lx-yulin-carp-remote-$version.apk"
Copy-Item $apk.FullName $target -Force
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash.ToLowerInvariant()

Write-Host "APK: $target"
Write-Host "SHA256: $hash"
Write-Host ""
Write-Host "安装：把 APK 发到手机，允许安装未知来源；手机和电脑连同一局域网。"
Write-Host "电脑端：设置 → 手机遥控… → 复制地址。"
