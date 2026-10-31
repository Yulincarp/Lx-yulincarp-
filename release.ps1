param(
    [string]$Repo = $env:GITHUB_REPOSITORY,
    [string]$Notes = "",
    # 触发本次发布的 tag；CI 里由 workflow 传进来，本地留空表示不校验
    [string]$Tag = $env:GITHUB_REF_NAME,
    # update.json 的 minVersion；留空则用当前版本号
    [string]$MinVersion = "",
    [ValidateSet("stable", "beta")]
    [string]$Channel = "stable",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Directory.Build.props is missing Version"
}

if ($Tag -and $Tag -ne "v$version") {
    throw "Tag $Tag does not match project version v$version"
}

if (-not $SkipTests) {
    Write-Host "Running tests..."
    dotnet test "tests\Lx.Tests\Lx.Tests.csproj" -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed"
    }
}

& (Join-Path $root "publish.ps1") -Runtime $Runtime
if ($LASTEXITCODE -ne 0) {
    throw "publish.ps1 failed"
}

$slug = "Lx-yulin-carp"
$out = Join-Path $root "dist\$slug-$version-$Runtime"
$zip = "$out.zip"
if (-not (Test-Path $zip)) {
    throw "publish.ps1 did not create $zip"
}

$setup = Join-Path $root "dist\Lx-yulin-carp-$version-Setup.exe"
Write-Host "Building Setup.exe..."
dotnet build "tools\Lx.Setup\Lx.Setup.csproj" -c Release --nologo "-p:PayloadZip=$zip"
if ($LASTEXITCODE -ne 0) {
    throw "Setup build failed"
}
# 优先按"当前版本"精确找；找不到再退回最新的 *-Setup.exe
$setupOutDir = Join-Path $root "tools\Lx.Setup\bin\Release\net48"
$builtSetup = Get-Item (Join-Path $setupOutDir "Lx-yulin-carp-$version-Setup.exe") -ErrorAction SilentlyContinue
if (-not $builtSetup) {
    $builtSetup = Get-ChildItem $setupOutDir -Filter "*-Setup.exe" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
}
if (-not $builtSetup) {
    throw "Setup.exe 没有产出（tools\Lx.Setup\bin\Release\net48 下找不到 *-Setup.exe）"
}
Copy-Item -LiteralPath $builtSetup.FullName -Destination $setup -Force

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant()
if (-not $Repo -or -not $Repo.Contains("/")) {
    Write-Warning "没有传 -Repo：update.json 的 url 会是占位地址，只适合本地测试"
}

$baseUrl = if ($Repo -and $Repo.Contains("/")) {
    "https://github.com/$Repo/releases/download/v$version"
} else {
    "https://example.invalid/Lx/releases/download/v$version"
}
$assetName = "$slug-$version-$Runtime.zip"

if ([string]::IsNullOrWhiteSpace($Notes)) {
    $Notes = "Lx yulin carp v$version"
}

$manifestName = if ($Channel -eq "beta") { "update-beta.json" } else { "update.json" }
$effectiveMinVersion = if ([string]::IsNullOrWhiteSpace($MinVersion)) { $version } else { $MinVersion }

$manifest = [ordered]@{
    version = $version
    url = "$baseUrl/$assetName"
    sha256 = $hash
    notes = $Notes
    minVersion = $effectiveMinVersion
    channel = $Channel
} | ConvertTo-Json -Depth 5

$manifestPath = Join-Path $root "dist\$manifestName"
[System.IO.File]::WriteAllText($manifestPath, $manifest, [System.Text.UTF8Encoding]::new($false))

Write-Host "Version: $version"
Write-Host "ZIP: $zip"
Write-Host "SETUP: $setup"
Write-Host "SHA256: $hash"
Write-Host "Manifest: $manifestPath"

# 发布页附一份校验清单：玩家下完可以直接 certutil/Get-FileHash 对一下。
# 包含 zip、Setup、update.json，以及本地构建过的 APK（CI 不构建 APK，没有就跳过）。
$checksumFiles = @($zip, $setup, $manifestPath)
$apk = Get-ChildItem (Join-Path $root "dist") -Filter "$slug-remote-$version.apk" -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($apk) {
    $checksumFiles += $apk.FullName
}

$lines = foreach ($file in $checksumFiles) {
    if (Test-Path -LiteralPath $file) {
        $fileHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant()
        "{0}  {1}" -f $fileHash, (Split-Path $file -Leaf)
    }
}

$checksumPath = Join-Path $root "dist\sha256.txt"
[System.IO.File]::WriteAllText($checksumPath, (($lines -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
Write-Host "Checksums: $checksumPath"
