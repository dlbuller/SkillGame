<#
  publish-update.ps1 - cut a new SkillGame release for the in-app updater.

  What it does:
    1. Builds the app (Release, x64).
    2. Stamps version.txt and zips the build output to update\SkillGame-<version>.zip.
    3. Computes the SHA-256 + size and writes update\version.json (what the app reads).
    4. Writes update\RELEASE_NOTES.md and prepends the notes to CHANGELOG.md.
    5. Commits and pushes, so raw.githubusercontent serves it immediately.

  Usage:
    .\tools\publish-update.ps1 -Version 1.1.0 -Notes "First line.`nSecond line."
    .\tools\publish-update.ps1 -Version 1.1.0 -NotesFile notes.txt
    add -NoPush to stage everything but not push.

  NOTE: committing the zip to the repo is the zero-setup path (free, works now). If the
  history gets heavy, switch to GitHub Releases: upload the zip as a release asset and set
  the "zip" URL in version.json to its download link - nothing else in the app changes.
#>
param(
  [Parameter(Mandatory=$true)][string]$Version,
  [string]$Notes,
  [string]$NotesFile,
  [switch]$NoPush
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

if (-not $Notes -and $NotesFile) { $Notes = Get-Content $NotesFile -Raw }
if (-not $Notes) { throw "Provide -Notes or -NotesFile." }
$date = Get-Date -Format "yyyy-MM-dd"
$raw  = "https://raw.githubusercontent.com/dlbuller/SkillGame/main/update"
$up   = Join-Path $repo "update"
New-Item -ItemType Directory -Force -Path $up | Out-Null

Write-Host "Building $Version (Release, x64)..." -ForegroundColor Cyan
& "C:\Program Files\dotnet\dotnet.exe" build "SkillGameWpf\SkillGameWpf.csproj" -c Release -p:Platform=x64 -p:Version=$Version -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$bin = "SkillGameWpf\bin\x64\Release\net8.0-windows"
if (-not (Test-Path $bin)) { $bin = "SkillGameWpf\bin\Release\net8.0-windows" }
Set-Content -Path (Join-Path $bin "version.txt") -Value $Version -Encoding ascii

$zipName = "SkillGame-$Version.zip"
$zipPath = Join-Path $up $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath }
Write-Host "Zipping $bin -> $zipName" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $bin "*") -DestinationPath $zipPath

$sha  = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
$size = (Get-Item $zipPath).Length

$manifest = [ordered]@{
  version = $Version; date = $date; zip = "$raw/$zipName"
  sha256 = $sha; size = $size; notes = $Notes
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $up "version.json") -Encoding utf8
Set-Content (Join-Path $up "RELEASE_NOTES.md") -Value "# SkillGame v$Version`n`n$Notes" -Encoding utf8

# Maintain versions.json — the full release list the in-app picker reads (newest first).
$vf = Join-Path $up "versions.json"
$all = @()
if (Test-Path $vf) { $all = @(Get-Content $vf -Raw | ConvertFrom-Json) }
$all = @([pscustomobject]$manifest) + @($all | Where-Object { $_.version -ne $Version })
$all | ConvertTo-Json -Depth 5 | Set-Content $vf -Encoding utf8

# prepend to CHANGELOG
$cl = Join-Path $repo "CHANGELOG.md"
$entry = "## v$Version - $date`n$Notes`n`n"
$body = if (Test-Path $cl) { Get-Content $cl -Raw } else { "# SkillGame changelog`n`n" }
$idx = $body.IndexOf("## ")
if ($idx -ge 0) { $body = $body.Insert($idx, $entry) } else { $body += "`n$entry" }
Set-Content $cl -Value $body -Encoding utf8

Write-Host "v$Version  sha256=$sha  size=$size bytes" -ForegroundColor Green
git add update CHANGELOG.md
git commit -m "Release v$Version"
if (-not $NoPush) { git push origin main; Write-Host "Pushed - the updater will see v$Version within a few minutes." -ForegroundColor Green }
else { Write-Host "Committed (not pushed). Run 'git push origin main' when ready." -ForegroundColor Yellow }
