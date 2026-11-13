<#
.SYNOPSIS
    Helpers for test sessions (scripts/test-deploy.ps1, scripts/launch-msc.ps1 -Hidden): keeping a game's files while a
    session deploys over them, and the ini edit My Summer Car's hidden start needs. File work only, no games, so
    scripts/test.ps1 covers them (scripts/tests). Dot-source it. Runs on Windows PowerShell 5.1 too.
#>

# The list of kept files in $Store (list.json): one entry per path, with absent (wasn't there) or moved (set aside), and
# the copy's file name.
function Read-SessionList([string] $Store) {
    $file = Join-Path $Store 'list.json'
    if (-not (Test-Path -LiteralPath $file)) { return $null }
    return @(Get-Content -Raw -LiteralPath $file | ConvertFrom-Json)
}

function Write-SessionList([string] $Store, $List) {
    [IO.File]::WriteAllText((Join-Path $Store 'list.json'), (ConvertTo-Json -InputObject @($List) -Depth 3), (New-Object Text.UTF8Encoding $false))
}

function Get-FileStamp([string] $Path) {
    if (Test-Path -LiteralPath $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash } else { '' }
}

# Before a deploy: copies each of $Keep (files the deploy overwrites) into $Store, notes those that aren't there, and
# moves each of $Move that's there (another mod's files) into $Store; the list is written after each, so it always says
# where everything is. A second deploy of the same session keeps the first copies (those are the originals) and only
# sets aside what came back. It refuses when the game's files changed since the deploy before (a session that was
# never stopped, and a new PerkeleRim installed meanwhile): restoring then would put old files over new ones.
# $true when it started a new list.
function Save-SessionFiles([string] $Store, [string[]] $Keep = @(), [string[]] $Move = @()) {
    $list = Read-SessionList $Store
    $new = -not $list
    if ($new) {
        $list = @()
        New-Item -ItemType Directory -Force $Store | Out-Null
    } else {
        $changed = @(Get-ChangedSinceDeploy $Store)
        if ($changed) {
            throw "The game's files changed since an earlier test session deployed (it was never stopped): $($changed -join ', '). Look at them, then run scripts/test-deploy.ps1 restore (puts back what was there before that session) or scripts/test-deploy.ps1 forget (keeps them as they are now)."
        }
    }
    $n = @($list).Count
    foreach ($path in @($Keep) + @($Move)) {
        if (-not $path) { continue }
        $moved = @($Move) -contains $path
        $entry = @($list | Where-Object { $_.path -eq $path }) | Select-Object -First 1
        if ($entry) {
            # Set aside before, and there again: someone put it back meanwhile, so the kept copy may be older.
            if ($moved -and (Test-Path -LiteralPath $path)) { throw "$path was set aside by this test session and is back: look at it, then scripts/test-deploy.ps1 restore or forget." }
            continue
        }
        if ($moved -and -not (Test-Path -LiteralPath $path)) { continue }  # not there: nothing to set aside
        $entry = [pscustomobject]@{ path = $path; absent = -not (Test-Path -LiteralPath $path); moved = $moved; copy = $null }
        if (-not $entry.absent) {
            $entry.copy = "{0:D2}-{1}" -f $n++, (Split-Path -Leaf $path)
            $target = Join-Path $Store $entry.copy
            if ($moved) {
                Copy-Item -LiteralPath $path $target  # the list names it before the original goes
                $list = @($list) + $entry
                Write-SessionList $Store $list
                Remove-Item -LiteralPath $path -Force
                continue
            }
            Copy-Item -LiteralPath $path $target
        }
        $list = @($list) + $entry
        Write-SessionList $Store $list
    }
    if ($new -and -not @($list).Count) { Write-SessionList $Store @() }
    return $new
}

# After a deploy: what it wrote over the kept paths, so a later deploy or restore can tell whether anyone changed them.
function Save-DeployStamps([string] $Store) {
    $stamps = [ordered]@{}
    foreach ($entry in @(Read-SessionList $Store)) { if (-not $entry.moved) { $stamps[$entry.path] = Get-FileStamp $entry.path } }
    [IO.File]::WriteAllText((Join-Path $Store 'deployed.json'), (ConvertTo-Json -InputObject $stamps), (New-Object Text.UTF8Encoding $false))
}

# The kept paths whose file isn't what the session's deploy wrote there.
function Get-ChangedSinceDeploy([string] $Store) {
    $file = Join-Path $Store 'deployed.json'
    if (-not (Test-Path -LiteralPath $file)) { return @() }
    $stamps = Get-Content -Raw -LiteralPath $file | ConvertFrom-Json
    foreach ($property in $stamps.PSObject.Properties) {
        if ((Get-FileStamp $property.Name) -ne $property.Value) { $property.Name }
    }
}

# Puts every file in $Store's list back where it was, deletes the ones that weren't there, then $Store. The paths it
# put back or deleted; none without a list. Refuses, keeping everything, when a file changed since the deploy (-Force:
# puts them back all the same).
function Restore-SessionFiles([string] $Store, [switch] $Force) {
    $list = Read-SessionList $Store
    if ($null -eq $list) { return @() }
    $changed = @(Get-ChangedSinceDeploy $Store)
    if ($changed -and -not $Force) {
        throw "Not put back: $($changed -join ', ') changed since the test session deployed (a new PerkeleRim installed?). Look at them, then scripts/test-deploy.ps1 restore-anyway or forget."
    }
    $done = @()
    foreach ($entry in $list) {
        if ($entry.absent) {
            if (Test-Path -LiteralPath $entry.path) { Remove-Item -LiteralPath $entry.path -Force; $done += $entry.path }
            continue
        }
        $copy = Join-Path $Store $entry.copy
        if (-not (Test-Path -LiteralPath $copy)) { throw "The kept copy of $($entry.path) is missing ($copy): left as it is." }
        New-Item -ItemType Directory -Force (Split-Path $entry.path) | Out-Null
        Copy-Item -LiteralPath $copy $entry.path -Force
        $done += $entry.path
    }
    Remove-Item -LiteralPath $Store -Recurse -Force
    return $done
}

# My Summer Car's doorstop_config.ini as it was before a hidden start (launch-msc.ps1 -Hidden keeps it here while its
# own copy has MSCLoader's skip switches on). $true when it put one back.
$MscDoorstopKept = Join-Path $env:LOCALAPPDATA 'PerkeleRim\doorstop_config.ini.test-backup'
function Restore-MscDoorstop([string] $Msc) {
    if (-not (Test-Path -LiteralPath $MscDoorstopKept)) { return $false }
    Copy-Item -LiteralPath $MscDoorstopKept (Join-Path $Msc 'doorstop_config.ini') -Force
    Remove-Item -LiteralPath $MscDoorstopKept -Force
    return $true
}

# $Text (an ini) with each of $Values set in [$Section], replacing a key that's there and adding one that isn't (at
# the section's end, or in a new section at the end). Other lines stay as they are, line endings included.
function Set-IniText([string] $Text, [string] $Section, [hashtable] $Values) {
    $newline = if ($Text -match "`r`n") { "`r`n" } else { "`n" }
    $lines = [Collections.Generic.List[string]] @($Text -split "`r?`n")
    if ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -eq '') { $lines.RemoveAt($lines.Count - 1); $trailing = $true } else { $trailing = $false }
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i].Trim() -ieq "[$Section]") { $start = $i; break } }
    if ($start -lt 0) {
        $lines.Add("[$Section]")
        $start = $lines.Count - 1
    }
    $end = $lines.Count
    for ($i = $start + 1; $i -lt $lines.Count; $i++) { if ($lines[$i].Trim().StartsWith('[')) { $end = $i; break } }
    foreach ($key in @($Values.Keys | Sort-Object)) {
        $found = $false
        for ($i = $start + 1; $i -lt $end; $i++) {
            if ($lines[$i] -match "^\s*$([regex]::Escape($key))\s*=") { $lines[$i] = "$key = $($Values[$key])"; $found = $true; break }
        }
        if (-not $found) {
            $at = $end
            while ($at -gt $start + 1 -and $lines[$at - 1].Trim() -eq '') { $at-- }  # before the blank lines that end the section
            $lines.Insert($at, "$key = $($Values[$key])")
            $end++
        }
    }
    return ($lines -join $newline) + $(if ($trailing) { $newline } else { '' })
}
