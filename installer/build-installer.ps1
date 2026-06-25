[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true,
    [switch]$SkipPublish,
    [switch]$PreserveReleases,
    [switch]$Msi,
    [string]$UpdateUrl,
    [string]$VpkPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\VmManager.App\VmManager.App.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\$Runtime"
$releaseDir = Join-Path $repoRoot "artifacts\velopack"
$toolDir = Join-Path $repoRoot "artifacts\tools"
$iconPath = Join-Path $repoRoot "src\VmManager.App\Assets\AppIcon.ico"

[xml]$project = Get-Content -Raw $projectPath
$version = @($project.Project.PropertyGroup |
    ForEach-Object { $_.Version } |
    Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
    Select-Object -First 1)

if (-not $version) {
    throw "Could not find a <Version> value in $projectPath."
}

$version = [string]$version

if (-not $SkipPublish) {
    if (Test-Path $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    $publishArgs = @(
        "publish", $projectPath,
        "--configuration", $Configuration,
        "--runtime", $Runtime,
        "--output", $publishDir,
        "-p:SelfContained=$SelfContained",
        "-p:PublishSingleFile=false",
        "-p:VelopackUpdateUrl=$UpdateUrl",
        "-maxcpucount:1"
    )

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path (Join-Path $publishDir "VmManager.App.exe"))) {
    throw "Published executable was not found in $publishDir. Run without -SkipPublish or check the publish output."
}

if (-not $VpkPath) {
    $vpkCommand = Get-Command vpk -ErrorAction SilentlyContinue
    if ($vpkCommand) {
        $VpkPath = $vpkCommand.Source
    }
}

if (-not $VpkPath) {
    New-Item -ItemType Directory -Force -Path $toolDir | Out-Null
    & dotnet tool update vpk --tool-path $toolDir
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install or update the vpk tool."
    }

    $VpkPath = Join-Path $toolDir "vpk.exe"
}

if (-not (Test-Path $VpkPath)) {
    throw "vpk was not found at $VpkPath."
}

if ((Test-Path $releaseDir) -and -not $PreserveReleases) {
    Remove-Item -LiteralPath $releaseDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

$packArgs = @(
    "pack",
    "--packId", "LittleBitsSoftware.VmManager",
    "--packTitle", "VM Manager",
    "--packVersion", $version,
    "--packDir", $publishDir,
    "--mainExe", "VmManager.App.exe",
    "--outputDir", $releaseDir,
    "--runtime", $Runtime,
    "--icon", $iconPath
)

if ($Msi) {
    $packArgs += "--msi"
}

& $VpkPath @packArgs
if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed with exit code $LASTEXITCODE."
}

$setupPath = Join-Path $releaseDir "LittleBitsSoftware.VmManager-win-Setup.exe"
if (Test-Path $setupPath) {
    Write-Host "Created installer: $setupPath"
} else {
    Write-Host "Velopack release assets created in: $releaseDir"
}
