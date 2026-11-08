#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes a Media Player (Sauce Edition) release: runs the checks, builds the Windows app, zips it and makes the
    GitHub release that SauceStore lists.

.DESCRIPTION
    1. Refuses unless the repository is on main, everything is committed and CHANGELOG.md's "Unreleased" has lines.
    2. Runs the checks (tests\MediaPlayerSE.Tests): all must pass.
    3. Writes the version into the project, moves "Unreleased" in CHANGELOG.md to "## [X.Y.Z] - date", commits
       "Media Player (Sauce Edition) X.Y.Z" and tags vX.Y.Z with those notes.
    4. Publishes the app self-contained for 64-bit Windows (nothing to install), takes a snapshot of the published
       app as a last check, adds the licences, README, CREDITS and CHANGELOG, and zips it as MediaPlayerSE-windows.zip
       (the name saucestore.json gives).
    5. Pushes main and the tag together and makes the GitHub release with the zip (gh). SauceStore picks it up within
       about 10 minutes.
    Never run this unless Andrew asked for the release: a release reaches everyone who installed the app.

.PARAMETER Version
    The new version, X.Y.Z.

.PARAMETER WhatIf
    Builds and zips into build\ without changing the version, committing, tagging, pushing or releasing.

.EXAMPLE
    .\tools\Release.ps1 -Version 0.1.0
#>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$SkipTests,
    [switch]$WhatIf
)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path -Parent $PSScriptRoot
$Tag = "v$Version"
$Asset = 'MediaPlayerSE-windows.zip'
$GhRepo = 'uberwulf-hub/MediaPlayer-SauceEdition'
$Project = Join-Path $Repo 'src\MediaPlayerSauce\MediaPlayerSauce.csproj'
function Step([string]$m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Stop-Release([string]$m) { Write-Host "Not released: $m" -ForegroundColor Red; exit 1 }
function Invoke-Git([string[]]$GitArgs) {
    $ErrorActionPreference = 'Continue'
    $out = & git -C $Repo @GitArgs 2>&1
    if ($LASTEXITCODE -ne 0) { Stop-Release "git $($GitArgs -join ' ') failed: $out" }
    return (($out | ForEach-Object { "$_" }) -join "`n").Trim()
}

# ---- 1. the repository is ready
$changelogFile = Join-Path $Repo 'CHANGELOG.md'
$changelog = [IO.File]::ReadAllText($changelogFile) -replace "`r`n", "`n"
$m = [regex]::Match($changelog, '(?ms)^## \[Unreleased\]\n(.*?)(?=^## \[|\z)')
if (-not $m.Success -or $m.Groups[1].Value -notmatch '(?m)^\s*-\s+\S') { Stop-Release 'CHANGELOG.md has nothing under "## [Unreleased]".' }
$notes = $m.Groups[1].Value.Trim()
if (-not $WhatIf) {
    if ((Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD')) -ne 'main') { Stop-Release 'not on main.' }
    if (Invoke-Git @('status', '--porcelain')) { Stop-Release 'there are uncommitted changes.' }
    if ((Invoke-Git @('tag', '--list', $Tag))) { Stop-Release "$Tag already exists." }
}

# ---- 2. the checks
if (-not $SkipTests) {
    Step 'Running the checks'
    $ErrorActionPreference = 'Continue'
    $out = & dotnet run --project (Join-Path $Repo 'tests\MediaPlayerSE.Tests\MediaPlayerSE.Tests.csproj') -c Release 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $last = ($out | Where-Object { $_ -match 'ALL PASSED|FAILED \d+ of' } | Select-Object -Last 1)
    if ($code -ne 0 -or $last -notmatch 'ALL PASSED') { $out | Where-Object { $_ -match '^FAIL' } | ForEach-Object { Write-Host $_ -ForegroundColor Red }; Stop-Release "the checks failed ($last)." }
    Step $last
}

# ---- 3. version, changelog, commit, tag
if (-not $WhatIf) {
    $today = (Get-Date).ToString('yyyy-MM-dd')
    $proj = [IO.File]::ReadAllText($Project)
    $proj = $proj -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>"
    [IO.File]::WriteAllText($Project, $proj)
    $newLog = $changelog.Substring(0, $m.Index) + "## [Unreleased]`n`n## [$Version] - $today`n`n$notes`n`n" + $changelog.Substring($m.Index + $m.Length)
    [IO.File]::WriteAllText($changelogFile, ($newLog.TrimEnd() + "`n"))
    [void](Invoke-Git @('add', '--', 'src/MediaPlayerSauce/MediaPlayerSauce.csproj', 'CHANGELOG.md'))
    $msg = Join-Path $env:TEMP ("mpse-release-" + [Guid]::NewGuid().ToString('N') + '.txt')
    try {
        [IO.File]::WriteAllText($msg, "Media Player (Sauce Edition) $Version`n`n$notes`n")
        [void](Invoke-Git @('commit', '-F', $msg))
        [IO.File]::WriteAllText($msg, "$notes`n")
        [void](Invoke-Git @('tag', '-a', $Tag, '-F', $msg))
    } finally { Remove-Item -LiteralPath $msg -Force -ErrorAction SilentlyContinue }
    Step "Committed and tagged $Tag"
}

# ---- 4. publish and zip
$out = Join-Path $Repo 'build\MediaPlayerSE'
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
[void][IO.Directory]::CreateDirectory($out)
Step 'Building the Windows app'
$ErrorActionPreference = 'Continue'
$pub = & dotnet publish $Project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false -p:DebugType=none -o $out 2>&1 | ForEach-Object { "$_" }
$code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$exe = Join-Path $out 'MediaPlayerSE.exe'
if ($code -ne 0 -or -not (Test-Path -LiteralPath $exe)) { $pub | Select-Object -Last 15 | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }; Stop-Release 'the build failed (the commit and tag are only on this PC).' }
if (-not (Test-Path -LiteralPath (Join-Path $out 'libvlc\win-x64\libvlc.dll'))) { Stop-Release 'libVLC is missing from the build.' }
# a last check of the published app itself: it starts, draws its window off the screen and quits
$shot = Join-Path $env:TEMP ("mpse-release-" + [Guid]::NewGuid().ToString('N') + '.png')
$p = Start-Process -FilePath $exe -ArgumentList '--snapshot', "`"$shot`"", '--scene', 'Playing' -PassThru -WindowStyle Hidden
if (-not $p.WaitForExit(60000)) { try { $p.Kill() } catch { }; Stop-Release 'the published app didn''t finish its snapshot.' }
if ($p.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $shot)) { Stop-Release "the published app couldn't draw its window (exit $($p.ExitCode))." }
Remove-Item -LiteralPath $shot -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $Repo 'licenses') -Destination (Join-Path $out 'licenses') -Recurse
foreach ($f in 'README.md', 'CREDITS.md', 'CHANGELOG.md') { Copy-Item -LiteralPath (Join-Path $Repo $f) -Destination $out }
$zip = Join-Path $Repo "build\$Asset"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($out, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
Step ("Made $Asset ({0:N0} MB)" -f ((Get-Item -LiteralPath $zip).Length / 1MB))
if ($WhatIf) { Step "Nothing was committed, tagged, pushed or released (-WhatIf). The zip is $zip"; exit 0 }

# ---- 5. push and the GitHub release
[void](Invoke-Git @('push', '--atomic', 'origin', 'main', $Tag))
Step "Pushed main and $Tag"
$notesFile = Join-Path $env:TEMP ("mpse-notes-" + [Guid]::NewGuid().ToString('N') + '.md')
try {
    [IO.File]::WriteAllText($notesFile, "$notes`n")
    $ErrorActionPreference = 'Continue'
    $r = & gh release create $Tag $zip --repo $GhRepo --title "Media Player (Sauce Edition) $Version" --notes-file $notesFile --verify-tag --latest 2>&1
    if ($LASTEXITCODE -ne 0) { Stop-Release "gh release create failed: $r (the tag is pushed; run gh release create by hand)" }
} finally { Remove-Item -LiteralPath $notesFile -Force -ErrorAction SilentlyContinue }
Step "Released $Tag. SauceStore lists it within about 10 minutes."
