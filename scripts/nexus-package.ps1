<#
.SYNOPSIS
    Gathers everything the Nexus Mods upload needs into tools/out/nexus/: the release zip
    (from scripts/package.ps1), the page's images and the page text from docs/nexus/.
    The fields to fill in are listed in docs/nexus/README.md. Run scripts/package.ps1 first.
.EXAMPLE
    scripts/nexus-package.ps1
#>
param([string] $Version)

. "$PSScriptRoot/common.ps1"

if (-not $Version) {
    $Version = (Select-String -Path (Join-Path $RepoRoot 'skse/CMakeLists.txt') -Pattern '^\s*VERSION\s+(\S+)').Matches[0].Groups[1].Value
}
$zip = Join-Path $RepoRoot "tools/out/release/PerkeleRim-$Version.zip"
if (-not (Test-Path $zip)) { throw "$zip not found. Run scripts/package.ps1 first." }

$out = Join-Path $RepoRoot 'tools/out/nexus'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force (Join-Path $out 'images') | Out-Null

Copy-Item $zip $out
Copy-Item (Join-Path $RepoRoot 'docs/nexus/description.bbcode') $out
Copy-Item (Join-Path $RepoRoot 'docs/nexus/README.md') (Join-Path $out 'FIELDS.md')
# Primary image first, then the gallery, numbered in upload order.
$images = 'trailer.jpg', 'kekmet-aurora.jpg', 'van-break.jpg', 'thalmor-chase.jpg', 'rolled-van.jpg', 'kekmet-crash.jpg'
for ($i = 0; $i -lt $images.Count; $i++) {
    Copy-Item (Join-Path $RepoRoot "docs/media/$($images[$i])") (Join-Path $out "images/$($i + 1)-$($images[$i])")
}

Write-Host "Nexus upload for $Version is in $out (fields: FIELDS.md)"
