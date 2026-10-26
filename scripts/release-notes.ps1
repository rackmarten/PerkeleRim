<#
.SYNOPSIS
    Writes a release's SHA256SUMS.txt (the zip and every file in it) next to the zip, and its release
    notes: the version's CHANGELOG.md section, then how to check the download: the zip's SHA-256,
    what the release workflow checked (-Checked, scripts/release-check.ps1) and the VirusTotal
    report (scripts/virustotal.ps1's JSON). The release workflow (.github/workflows/release.yml)
    runs it; -Changelog points it at the tag's CHANGELOG.md.
.EXAMPLE
    scripts/release-notes.ps1 -Zip tools/out/release/PerkeleRim-0.2.0.zip -VirusTotal tools/out/release/virustotal.json
#>
param(
    [Parameter(Mandatory)] [string] $Zip,
    [string] $VirusTotal,
    [string] $Changelog,
    [switch] $Checked,
    [string] $Out
)

. "$PSScriptRoot/common.ps1"

$zipName = Split-Path -Leaf $Zip
if ($zipName -notmatch '^PerkeleRim-(.+)\.zip$') { throw "$zipName isn't a release zip (PerkeleRim-<version>.zip)." }
$version = $Matches[1]
$dir = Split-Path -Parent (Resolve-Path -LiteralPath $Zip).Path
if (-not $Out) { $Out = Join-Path $dir 'release-notes.md' }
if (-not $Changelog) { $Changelog = Join-Path $RepoRoot 'CHANGELOG.md' }
$repo = if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { 'rackmarten/PerkeleRim' }

# SHA256SUMS.txt in sha256sum's format: the zip, then every file inside it by its path in the zip.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipHash = (Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
$sums = @("$zipHash  $zipName")
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Zip).Path)
try {
    foreach ($entry in $archive.Entries | Where-Object { $_.Name } | Sort-Object FullName) {
        $stream = $entry.Open()
        try { $sums += "$((Get-FileHash -InputStream $stream -Algorithm SHA256).Hash.ToLowerInvariant())  $($entry.FullName)" } finally { $stream.Dispose() }
    }
} finally { $archive.Dispose() }
[IO.File]::WriteAllLines((Join-Path $dir 'SHA256SUMS.txt'), [string[]] $sums, (New-Object Text.UTF8Encoding $false))

# The version's section of CHANGELOG.md, without its heading.
$text = Get-Content -Raw -LiteralPath $Changelog
$pattern = '(?ms)^## \[' + [regex]::Escape($version) + '\][^\n]*\n(.*?)(?=^## \[|\z)'
$section = if ($text -match $pattern) { $Matches[1].Trim() } else { "See [CHANGELOG.md](https://github.com/$repo/blob/v$version/CHANGELOG.md)." }
# The changelog's links are relative to the repository's root, which a release page doesn't resolve (they 404): point them at the tag.
$section = $section -replace '\]\((?!https?://|#|mailto:)([^)\s]+)\)', "](https://github.com/$repo/blob/v$version/`$1)"

$lines = @($section, '', '## Check your download', '')
$lines += "- **SHA-256** of ``$zipName``: ``$zipHash``. In PowerShell: ``Get-FileHash $zipName``. Every file in it is listed in ``SHA256SUMS.txt``."
if ($Checked) {
    $run = if ($env:GITHUB_RUN_ID) { "[The release workflow]($env:GITHUB_SERVER_URL/$repo/actions/runs/$env:GITHUB_RUN_ID)" } else { 'The release workflow' }
    $lines += "- **Built from tag ``v$version``** on the maintainer's PC: the ESP is generated from Skyrim's own data and the My Summer Car mod compiles against MSCLoader and Unity, none of which can be put on GitHub's build machines. $run checked that the launcher, the starter saves and LICENSE are the tag's files byte for byte, and that the My Summer Car mod was built from the tag's commit."
}
if ($VirusTotal -and (Test-Path -LiteralPath $VirusTotal)) {
    $scan = Get-Content -Raw -LiteralPath $VirusTotal | ConvertFrom-Json
    if ($scan.status -eq 'completed') {
        $flagged = [int] $scan.stats.malicious + [int] $scan.stats.suspicious
        $engines = $flagged + [int] $scan.stats.undetected + [int] $scan.stats.harmless
        $lines += if ($flagged -eq 0) {
            "- **VirusTotal**: [no engine flags it]($($scan.url)) (0 of $engines)."
        } else {
            "- **VirusTotal**: [$flagged of $engines engines flag it]($($scan.url)). PerkeleRim's plugins hook into both games' code and its launcher is a PowerShell script, which a few heuristic engines mistake for malware; every file is built from this repository's source, which anyone can read and build."
        }
    } elseif ($scan.status -eq 'queued') {
        $lines += "- **VirusTotal**: the scan was still running when this was written: [its report]($($scan.url))."
    }
}
[IO.File]::WriteAllLines($Out, [string[]] $lines, (New-Object Text.UTF8Encoding $false))
Write-Host "Wrote $(Join-Path $dir 'SHA256SUMS.txt') and $Out"
