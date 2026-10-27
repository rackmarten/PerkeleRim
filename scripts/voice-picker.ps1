<#
.SYNOPSIS
    Builds and opens the MSC voice picker: a local page for listening to My Summer Car's clips by
    MSC's own groups (Hangover, Swearing, Drunk, Teimo...) and copying a reference to the ones you
    mean, like "Hangover #3 (hangover03)".
.DESCRIPTION
    Reads your own My Summer Car install (MscPath in local.props) offline; MSC doesn't need to run.
    Everything it writes lands in tools/out/voices/ (gitignored): the clips as Ogg Opus (WAV without
    ffmpeg), index.json and the page. Never commit any of it. A rerun only writes clips that are
    missing, so it takes seconds.

    Needs tools/setup.ps1's AssetStudioModCLI in tools/.deps and the .NET SDK 8. ffmpeg (winget
    install Gyan.FFmpeg) keeps the clips small. -Transcribe also needs uv and an NVIDIA GPU: it runs
    Whisper (tools/voice-picker/transcribe.py) for the Finnish text and an English translation, which
    the page searches.
.EXAMPLE
    pwsh -NoProfile -File scripts/voice-picker.ps1
.EXAMPLE
    pwsh -NoProfile -File scripts/voice-picker.ps1 -Transcribe -Groups Hangover,Swearing,Drunk
#>
param(
    # Clips longer than this (music, radio, the drunk monologues) are listed without audio; 0 for all.
    [double] $MaxSeconds = 60,
    # Run Whisper over the clips (only those not transcribed yet).
    [switch] $Transcribe,
    # With -Transcribe: only these groups (MSC's group names, as on the page).
    [string[]] $Groups = @(),
    [switch] $NoOpen
)

. "$PSScriptRoot/common.ps1"
$tool = Join-Path $RepoRoot 'tools/voice-picker'
$out = Join-Path $RepoRoot 'tools/out/voices'
$deps = Join-Path $RepoRoot 'tools/.deps/AssetStudioModCLI'
if (-not (Test-Path (Join-Path $deps 'AssetStudio.dll'))) { throw 'AssetStudioModCLI not in tools/.deps: run tools/setup.ps1 first (tools/README.md).' }

$exporter = Join-Path $deps 'VoiceExport.exe'
$stale = -not (Test-Path $exporter) -or
    (Get-ChildItem $tool -Filter *.cs | Where-Object LastWriteTime -gt (Get-Item $exporter).LastWriteTime)
if ($stale) {
    dotnet build (Join-Path $tool 'VoiceExport.csproj') -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'VoiceExport build failed.' }
}

$exportArgs = @((Join-Path (Get-RequiredSetting (Get-LocalConfig) 'MscPath') 'mysummercar_Data'), $out,
    '--max-seconds', $MaxSeconds.ToString([cultureinfo]::InvariantCulture))
$ffmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
if ($ffmpeg) { $exportArgs += '--ffmpeg', $ffmpeg.Source }
else { Write-Warning 'ffmpeg not found: writing WAVs (about 10 times bigger). winget install Gyan.FFmpeg to get Ogg Opus.' }
& $exporter @exportArgs | Where-Object { $_ -notmatch '^\[\d' }
if ($LASTEXITCODE -ne 0) { throw 'VoiceExport failed.' }
Copy-Item (Join-Path $tool 'picker.html') (Join-Path $out 'index.html') -Force

if ($Transcribe) {
    if (-not (Get-Command uv -ErrorAction SilentlyContinue)) { throw 'uv not found: winget install astral-sh.uv' }
    $pyArgs = @()
    if ($Groups) { $pyArgs += '--groups', ($Groups -join ',') }
    uv run (Join-Path $tool 'transcribe.py') @pyArgs
    if ($LASTEXITCODE -ne 0) { throw 'transcribe.py failed.' }
}

$page = Join-Path $out 'index.html'
Write-Host "Voice picker: $page"
if (-not $NoOpen) { Start-Process $page }
