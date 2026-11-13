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

# Before a deploy: copies each of $Keep (files the deploy overwrites) into $Store, notes those that aren't there, and
# moves each of $Move that's there (another mod's files) into $Store. Nothing when $Store has a list already (an earlier
# deploy of the same session): those are the originals. $true when it kept them now.
function Save-SessionFiles([string] $Store, [string[]] $Keep = @(), [string[]] $Move = @()) {
    if (Read-SessionList $Store) { return $false }
    New-Item -ItemType Directory -Force $Store | Out-Null
    $list = @()
    $n = 0
    foreach ($path in @($Keep) + @($Move)) {
        if (-not $path) { continue }
        $moved = @($Move) -contains $path
        if ($moved -and -not (Test-Path -LiteralPath $path)) { continue }  # not there: nothing to set aside
        $entry = [ordered]@{ path = $path; absent = -not (Test-Path -LiteralPath $path); moved = $moved; copy = $null }
        if (-not $entry.absent) {
            $entry.copy = "{0:D2}-{1}" -f $n++, (Split-Path -Leaf $path)
            $target = Join-Path $Store $entry.copy
            if ($moved) { Move-Item -LiteralPath $path $target } else { Copy-Item -LiteralPath $path $target }
        }
        $list += [pscustomobject] $entry
    }
    # Written last: a list on disk means every copy in it is there.
    [IO.File]::WriteAllText((Join-Path $Store 'list.json'), (ConvertTo-Json -InputObject @($list) -Depth 3), (New-Object Text.UTF8Encoding $false))
    return $true
}

# Puts every file in $Store's list back where it was, deletes the ones that weren't there, then $Store. The paths it
# put back or deleted; none without a list.
function Restore-SessionFiles([string] $Store) {
    $list = Read-SessionList $Store
    if (-not $list) { return @() }
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
