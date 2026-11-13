#Requires -Version 5.1
<#
.SYNOPSIS
    Starts a Media Player (Sauce Edition) release on GitHub (Actions > Release). GitHub runs the checks, builds the
    Windows zip and the signed Android .apk, and publishes the release; SauceStore picks it up. Your PC does no
    building or signing.

.DESCRIPTION
    Releases are built by .github/workflows/release.yml with the Android key GitHub keeps for this repository. Don't
    sign the Android app with any other key: phones would refuse the update.

    Before running: bump <Version> in src\MediaPlayerSauce\MediaPlayerSauce.csproj, write release-notes\v<version>.md
    (one change per line) and the same changes in CHANGELOG.md, commit and push to main.
    You can also start it from your phone: github.com > MediaPlayer-SauceEdition > Actions > Release > Run workflow.
    Never run this unless Andrew asked for the release: a release reaches everyone who installed the app.

    To build the Windows app on this PC without releasing anything:
      dotnet publish src\MediaPlayerSauce -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o build\MediaPlayerSE

.EXAMPLE
    .\tools\Release.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Get-Content (Join-Path $root 'src\MediaPlayerSauce\MediaPlayerSauce.csproj') -Raw
$version = 'v' + [regex]::Match($proj, '<Version>([^<]+)</Version>').Groups[1].Value
if (git -C $root status --porcelain) { throw 'Commit and push your changes first.' }
if ((git -C $root rev-parse --abbrev-ref HEAD) -ne 'main') { throw 'Releases are made from main.' }
git -C $root fetch --quiet origin
if ((git -C $root rev-parse HEAD) -ne (git -C $root rev-parse '@{u}')) { throw 'Push your commits first.' }
if (-not (Test-Path (Join-Path $root "release-notes\$version.md"))) { Write-Host "No release-notes\$version.md: the notes will come from CHANGELOG.md's [$($version.Substring(1))] section." -ForegroundColor Yellow }
gh workflow run release.yml --repo uberwulf-hub/MediaPlayer-SauceEdition --ref main -f "version=$version"
if ($LASTEXITCODE) { throw 'Could not start the release workflow (is gh signed in?).' }
Write-Host "Started the $version release. Watch it at https://github.com/uberwulf-hub/MediaPlayer-SauceEdition/actions" -ForegroundColor Green
