<#
.SYNOPSIS
    The launcher's first-run setup, as dialogs: find My Summer Car and Skyrim (or let the player
    pick them), wait while the player installs MSCLoader, SKSE and Address Library with their
    download pages opened, and ask once how Skyrim starts when Mod Organizer 2 manages it. The
    work itself is in install.ps1 and checks.ps1; dot-source those first. PerkeleRim.ps1 runs
    Invoke-PerkeleRimSetup whenever PerkeleRim.ini has no usable game folders.
#>

Add-Type -AssemblyName System.Windows.Forms

# A message box in front of the console window. Returns the button pressed ('OK', 'Yes', 'No', 'Cancel', 'Retry').
function Show-Message([string] $Text, [string] $Buttons = 'OK', [string] $Icon = 'Information') {
    $owner = New-Object System.Windows.Forms.Form -Property @{ TopMost = $true; ShowInTaskbar = $false }
    try { return [string] [System.Windows.Forms.MessageBox]::Show($owner, $Text, 'PerkeleRim setup', $Buttons, $Icon) }
    finally { $owner.Dispose() }
}

function Select-Folder([string] $Description, [string] $Start) {
    $owner = New-Object System.Windows.Forms.Form -Property @{ TopMost = $true; ShowInTaskbar = $false }
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog -Property @{ Description = $Description; ShowNewFolderButton = $false }
    if ($Start -and (Test-Path -LiteralPath $Start)) { $dialog.SelectedPath = $Start }
    try { if ($dialog.ShowDialog($owner) -eq 'OK') { return $dialog.SelectedPath } else { return $null } }
    finally { $dialog.Dispose(); $owner.Dispose() }
}

# Picks $Game's folder: a found one the player confirms, or one they pick. $null when they cancel.
function Select-GameFolder($Game, $Installs) {
    foreach ($install in @($Installs)) {
        $answer = Show-Message "Found $($Game.Name) in:`n`n$install`n`nUse this one?" 'YesNoCancel' 'Question'
        if ($answer -eq 'Yes') { return $install }
        if ($answer -eq 'Cancel') { return $null }
    }
    while ($true) {
        Show-Message "Pick your $($Game.Name) folder: the one with $($Game.Exe)." | Out-Null
        $picked = Select-Folder "$($Game.Name) folder (with $($Game.Exe))" ${env:ProgramFiles(x86)}
        if (-not $picked) { return $null }
        if (Select-GameInstalls @($picked) $Game.Exe) { return $picked.TrimEnd('\') }
        if ((Show-Message "$picked has no $($Game.Exe). Pick another folder?" 'RetryCancel' 'Warning') -ne 'Retry') { return $null }
    }
}

# Shows what's missing until $GetProblems (a scriptblock returning the problems) has none, opening
# the download pages they link when the player wants. $true when nothing is missing any more.
function Wait-Requirements([scriptblock] $GetProblems) {
    $opened = @{}
    while ($true) {
        $problems = @(& $GetProblems)
        if (-not $problems) { return $true }
        $list = ($problems | ForEach-Object { " - $_" }) -join "`n`n"
        $pages = @($Links.MscLoader, $Links.Skse, $Links.AddressLibrary | Where-Object { $page = $_; -not $opened[$page] -and @($problems | Where-Object { $_ -like "*$page*" }).Count -gt 0 })
        if ($pages -and (Show-Message "Not ready yet:`n`n$list`n`nOpen the download pages in your browser?" 'YesNo' 'Warning') -eq 'Yes') {
            foreach ($page in $pages) { Start-Process $page; $opened[$page] = $true }
        }
        if ((Show-Message "Not ready yet:`n`n$list`n`nInstall those, then press Retry. Cancel stops here; the launcher asks again next time." 'RetryCancel' 'Warning') -ne 'Retry') { return $false }
    }
}

# Finds both games. Returns @{ Skyrim; Msc }, or $null when the player stops. $Known: folders that
# still hold their game (from PerkeleRim.ini), which aren't asked for again.
function Invoke-PerkeleRimSetup($SteamLibraries, [hashtable] $Known) {
    $start = Show-Message ("PerkeleRim couples My Summer Car with Skyrim. Setup takes a few clicks:`n`n" +
        "1. Finds My Summer Car and Skyrim Special Edition (or you pick them).`n" +
        "2. Checks MSCLoader, SKSE and Address Library, and opens their download pages for anything missing.`n" +
        "3. Installs PerkeleRim into both games and starts them.`n`nYour games stay as they are until step 3. Start?") 'OKCancel' 'Information'
    if ($start -ne 'OK') { return $null }
    $found = @{}
    foreach ($key in 'Msc', 'Skyrim') {
        if ($Known[$key]) { $found[$key] = $Known[$key]; continue }
        $game = $Games[$key]
        $folder = Select-GameFolder $game @(Select-GameInstalls (Get-GameFolderGuesses $game $SteamLibraries) $game.Exe)
        if (-not $folder) { return $null }
        $found[$key] = $folder
    }
    return $found
}

# How Skyrim starts when Mod Organizer 2 manages it, asked once. Returns 'MO2' or 'None'.
function Select-Mo2Use([string] $Instance) {
    $answer = Show-Message ("Mod Organizer 2 ('$Instance') manages this Skyrim. Start Skyrim through MO2 for PerkeleRim?`n`n" +
        "Yes: the launcher starts Skyrim through MO2, so your MO2 mods load too. PerkeleRim's own Skyrim files go in Skyrim's Data folder, which MO2 shows to Skyrim as well, and PerkeleRim.esp is ticked in MO2's selected profile.`n" +
        "No: the launcher starts Skyrim through SKSE directly, without your MO2 mods (Address Library must then be in Skyrim's own Data folder).`n`n" +
        "ModManager in PerkeleRim.ini keeps the answer.") 'YesNo' 'Question'
    if ($answer -eq 'Yes') { return 'MO2' } else { return 'None' }
}
