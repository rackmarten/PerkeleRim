<#
.SYNOPSIS
    Copies your current My Summer Car save into launcher/saves/MSC as the release zip's starter
    save, with Finn's needs (thirst, hunger, urine, fatigue, stress, drunk, dirtiness) at 0.
    Everything else (money, the world, items) stays as saved. Settings (options.txt), MSCLoader's
    Mods.txt and Steam's file are left out. Save in MSC first; run scripts/package.ps1 after.

    -Skyrim instead copies your newest Skyrim save (.ess and its SKSE co-save) into
    launcher/saves/Skyrim as PerkeleRim-Kumpo.ess / .skse. Make it before the first handover, with only
    the base game, the free Creation Club content and PerkeleRim.esp loaded, so it loads anywhere.
.EXAMPLE
    scripts/starter-save.ps1
    scripts/starter-save.ps1 -Skyrim
#>
param([string[]] $Reset = @('PlayerThirst', 'PlayerHunger', 'PlayerUrine', 'PlayerFatigue', 'PlayerStress', 'PlayerDrunk', 'PlayerDirtiness'),
    [switch] $Skyrim)

. "$PSScriptRoot/common.ps1"

if ($Skyrim) {
    $saves = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games/Skyrim Special Edition/Saves'
    $newest = Get-ChildItem $saves -Filter *.ess | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $newest) { throw "No Skyrim save in $saves." }
    $coSave = [IO.Path]::ChangeExtension($newest.FullName, '.skse')
    if (-not (Test-Path $coSave)) { throw "$($newest.Name) has no SKSE co-save next to it." }
    $dest = Join-Path $RepoRoot 'launcher/saves/Skyrim'
    if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item $newest.FullName (Join-Path $dest 'PerkeleRim-Kumpo.ess')
    Copy-Item $coSave (Join-Path $dest 'PerkeleRim-Kumpo.skse')
    Write-Host "Starter Skyrim save in $dest (from $($newest.Name), $($newest.LastWriteTime))"
    return
}

$source = Join-Path $env:USERPROFILE 'AppData\LocalLow\Amistech\My Summer Car'
$dest = Join-Path $RepoRoot 'launcher/saves/MSC'
if (-not (Test-Path (Join-Path $source 'defaultES2File.txt'))) { throw "No My Summer Car save in $source." }

if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
New-Item -ItemType Directory -Force $dest | Out-Null
Get-ChildItem $source -File | Where-Object Name -notin 'options.txt', 'Mods.txt', 'steam_autocloud.vdf' |
    Copy-Item -Destination $dest

# Easy Save 2 entry: '~', key length, key, int32 entry length, 0xFF, type hash, value.
# A float's type hash is 6B D7 3E 6E and its value the 4 bytes after it.
$save = Join-Path $dest 'defaultES2File.txt'
$bytes = [IO.File]::ReadAllBytes($save)
$text = [Text.Encoding]::GetEncoding(28591).GetString($bytes)  # Latin-1: one char per byte
foreach ($key in $Reset) {
    $at = $text.IndexOf("~$([char]$key.Length)$key", [StringComparison]::Ordinal)
    if ($at -lt 0) { throw "$key not found in the save." }
    $type = $at + 2 + $key.Length + 5
    if ($bytes[$type - 1] -ne 0xFF -or $bytes[$type] -ne 0x6B -or $bytes[$type + 1] -ne 0xD7 -or
        $bytes[$type + 2] -ne 0x3E -or $bytes[$type + 3] -ne 0x6E) { throw "$key is not a float in the save." }
    $value = $type + 4
    Write-Host ("{0,-16} {1,8:0.##} -> 0" -f $key, [BitConverter]::ToSingle($bytes, $value))
    [Array]::Clear($bytes, $value, 4)
}
[IO.File]::WriteAllBytes($save, $bytes)
Write-Host "Starter save in $dest"
