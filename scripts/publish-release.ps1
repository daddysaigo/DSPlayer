#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$SkipLibMpvDownload
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\DSPlayer\DSPlayer.csproj"
$version = ([xml](Get-Content -Raw $project)).Project.PropertyGroup.Version | Select-Object -First 1
$artifacts = Join-Path $repoRoot "artifacts"
$publishDir = Join-Path $artifacts "DSPlayer-v$version-$Runtime"
$zipPath = "$publishDir.zip"

if (-not $SkipLibMpvDownload -and -not (Test-Path (Join-Path $repoRoot "lib\libmpv-2.dll"))) {
    & (Join-Path $PSScriptRoot "fetch-libmpv.ps1")
    if ($LASTEXITCODE -ne 0) { throw "libmpv download failed." }
}

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

dotnet publish $project -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Copy-Item (Join-Path $repoRoot "LICENSE") $publishDir
Copy-Item (Join-Path $repoRoot "THIRD-PARTY-NOTICES.md") $publishDir
Copy-Item (Join-Path $repoRoot "README.md") $publishDir
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Release package: $zipPath"
