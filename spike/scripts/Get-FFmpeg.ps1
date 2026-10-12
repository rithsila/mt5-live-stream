# Downloads a PINNED ffmpeg build for CaptureSpike.
# Build: gyan.dev "release essentials" 9.0.2 (includes libx264), published
# 2026-09-19. The SHA-256 below is the official .sha256 published next to the
# zip and was also confirmed by hashing the downloaded file locally.
#
# NOTE: gyan.dev keeps only the newest versioned package online. If this URL
# 404s after a newer ffmpeg release, pick the new versioned URL + .sha256 from
# https://www.gyan.dev/ffmpeg/builds/ and update the two constants below.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Version      = '9.0.2'
$GithubUrl    = "https://github.com/GyanD/codexffmpeg/releases/download/$Version/ffmpeg-$Version-essentials_build.zip"
$GyanUrl      = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-$Version-essentials_build.zip"
$ExpectedHash = '60F467265B1E312373DBCD92200C2618A74850F98D3D078E94296BB3FA2047BA'

$Root    = Split-Path $PSScriptRoot -Parent
$DestDir = Join-Path $Root 'ffmpeg'
$DestExe = Join-Path $DestDir 'ffmpeg.exe'
$TmpZip  = Join-Path $env:TEMP "ffmpeg-$Version-essentials_build.zip"
$TmpDir  = Join-Path $env:TEMP "ffmpeg-$Version-extract"

Write-Host "ffmpeg $Version (release essentials)"
Write-Host "Trying GitHub first..."

$downloadOk = $false
try {
    Write-Host "Downloading $GithubUrl ..."
    Invoke-WebRequest -Uri $GithubUrl -OutFile $TmpZip -UseBasicParsing
    $hash = (Get-FileHash -Path $TmpZip -Algorithm SHA256).Hash
    if ($hash -eq $ExpectedHash) {
        Write-Host "SHA-256 OK (GitHub)."
        $downloadOk = $true
    } else {
        Write-Host "SHA-256 mismatch from GitHub (got $hash), trying gyan.dev..."
        Remove-Item $TmpZip -Force -ErrorAction SilentlyContinue
    }
} catch {
    Write-Host "GitHub download failed: $_"
    Write-Host "Trying gyan.dev..."
}

if (-not $downloadOk) {
    Write-Host "Downloading $GyanUrl ..."
    Invoke-WebRequest -Uri $GyanUrl -OutFile $TmpZip -UseBasicParsing
    $hash = (Get-FileHash -Path $TmpZip -Algorithm SHA256).Hash
    if ($hash -ne $ExpectedHash) {
        Remove-Item $TmpZip -Force -ErrorAction SilentlyContinue
        throw "SHA-256 mismatch.`n  got:      $hash`n  expected: $ExpectedHash`nThe file was NOT installed."
    }
    Write-Host "SHA-256 OK (gyan.dev)."
}

if (Test-Path $TmpDir) { Remove-Item $TmpDir -Recurse -Force }
Expand-Archive -Path $TmpZip -DestinationPath $TmpDir

$exe = Get-ChildItem -Path $TmpDir -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
if (-not $exe) { throw "ffmpeg.exe not found inside the zip." }

New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
Copy-Item -Path $exe.FullName -Destination $DestExe -Force

Remove-Item $TmpDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $TmpZip -Force -ErrorAction SilentlyContinue

& $DestExe -version | Select-Object -First 1
Write-Host "Done. ffmpeg is at: $DestExe"
