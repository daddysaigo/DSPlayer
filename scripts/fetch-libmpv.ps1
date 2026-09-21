#Requires -Version 5.1
<#
.SYNOPSIS
  Downloads libmpv (x86_64) for Windows into DSPlayer/lib.

.DESCRIPTION
  Prefers GitHub zhongfly/mpv-winbuild mpv-dev packages (libmpv-2.dll).
  Falls back to manual instructions if download fails.

.EXAMPLE
  .\scripts\fetch-libmpv.ps1
#>
[CmdletBinding()]
param(
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $RepoRoot "lib"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$TempDir = Join-Path $env:TEMP ("dsplayer-libmpv-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

function Find-SevenZip {
    foreach ($c in @(
        "${env:ProgramFiles}\7-Zip\7z.exe",
        "${env:ProgramFiles(x86)}\7-Zip\7z.exe"
    )) {
        if (Test-Path $c) { return $c }
    }
    $cmd = Get-Command 7z -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Expand-ArchiveSmart {
    param([string]$Archive, [string]$Dest)
    $ext = [IO.Path]::GetExtension($Archive).ToLowerInvariant()
    if ($ext -eq ".zip") {
        Expand-Archive -Path $Archive -DestinationPath $Dest -Force
        return
    }
    $seven = Find-SevenZip
    if (-not $seven) {
        throw "7-Zip is required to extract .7z. Install 7-Zip or place libmpv-2.dll into: $OutDir"
    }
    & $seven x -y "-o$Dest" $Archive | Out-Null
}

function Install-DllFromRoot {
    param([string]$Root)
    $names = @("libmpv-2.dll", "mpv-2.dll", "mpv-1.dll", "libmpv-1.dll")
    $dll = $null
    foreach ($n in $names) {
        $dll = Get-ChildItem -Path $Root -Filter $n -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($dll) { break }
    }
    if (-not $dll) { return $false }

    Get-ChildItem $dll.DirectoryName -Filter "*.dll" | ForEach-Object {
        Copy-Item -Force $_.FullName (Join-Path $OutDir $_.Name)
        Write-Host "Installed: $($_.Name) ($([math]::Round($_.Length/1MB,1)) MB)"
    }
    return $true
}

function Try-ZhongflyGitHub {
    Write-Host "Fetching latest zhongfly/mpv-winbuild release metadata..."
    $api = "https://api.github.com/repos/zhongfly/mpv-winbuild/releases/latest"
    $json = curl.exe -sL $api | ConvertFrom-Json
    if (-not $json.assets) {
        Write-Warning "GitHub API returned no assets."
        return $false
    }

    # Prefer generic x86_64 dev (not v3 / not lgpl-only unless needed)
    $asset = $json.assets |
        Where-Object { $_.name -match '^mpv-dev-x86_64-\d{8}-git-.+\.7z$' -and $_.name -notmatch 'v3|lgpl|aarch64|i686' } |
        Select-Object -First 1

    if (-not $asset) {
        $asset = $json.assets |
            Where-Object { $_.name -match 'mpv-dev-x86_64' -and $_.name -notmatch 'v3|lgpl' } |
            Select-Object -First 1
    }

    if (-not $asset) {
        Write-Warning "No mpv-dev-x86_64 asset found."
        return $false
    }

    $archive = Join-Path $TempDir $asset.name
    Write-Host "Downloading $($asset.browser_download_url)"
    curl.exe -L --fail --retry 3 -o $archive $asset.browser_download_url
    if (-not (Test-Path $archive) -or (Get-Item $archive).Length -lt 1MB) {
        Write-Warning "Download failed or file too small."
        return $false
    }

    $extract = Join-Path $TempDir "out"
    Expand-ArchiveSmart -Archive $archive -Dest $extract
    $installed = Install-DllFromRoot -Root $extract
    if ($installed) {
        @(
            "Asset: $($asset.name)"
            "Binary: $($asset.browser_download_url)"
            "Build scripts: https://github.com/zhongfly/mpv-winbuild"
            "mpv source and licenses: https://github.com/mpv-player/mpv"
        ) | Set-Content -Encoding UTF8 (Join-Path $OutDir "MPV_BUILD.txt")
    }
    return $installed
}

try {
    $ok = Try-ZhongflyGitHub
    if (-not $ok) {
        Write-Host ""
        Write-Host "Automatic download failed."
        Write-Host "Manual steps:"
        Write-Host "  1. Open https://github.com/zhongfly/mpv-winbuild/releases"
        Write-Host "  2. Download mpv-dev-x86_64-*.7z (not v3 unless your CPU supports it)"
        Write-Host "  3. Extract libmpv-2.dll into: $OutDir"
        Write-Host "  Or: https://sourceforge.net/projects/mpv-player-windows/files/libmpv/"
        exit 1
    }

    Write-Host ""
    Write-Host "Done. Rebuild so the DLL is copied to the output directory:"
    Write-Host "  dotnet build .\src\DSPlayer\DSPlayer.csproj -c Release"
    exit 0
}
finally {
    if (Test-Path $TempDir) {
        Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
    }
}
