<#
.SYNOPSIS
    Starts My Summer Car windowed at a small resolution and clicks "Play!" on Unity's
    configuration dialog if it appears, then presses Space past the news screen, so a test run needs no clicks. Combine with the spike's auto-load
    (<MSC>/PerkeleRim_autoload.txt) to go straight into the saved game.
.EXAMPLE
    scripts/launch-msc.ps1
    scripts/launch-msc.ps1 -Width 1280 -Height 720
#>
param(
    [int] $Width = 800,
    [int] $Height = 600,
    [int] $TimeoutSeconds = 60
)

. "$PSScriptRoot/common.ps1"
$msc = Get-RequiredSetting (Get-LocalConfig) 'MscPath'

if (Get-Process mysummercar -ErrorAction SilentlyContinue) { throw 'My Summer Car is already running.' }

Add-Type -Namespace PerkeleRim -Name Win32 -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
'@
$clicked = $false

$proc = Start-Process -PassThru -WorkingDirectory $msc (Join-Path $msc 'mysummercar.exe') `
    -ArgumentList '-screen-fullscreen', '0', '-screen-width', $Width, '-screen-height', $Height, '-screen-quality', 'Fastest'

$deadline = (Get-Date).AddSeconds(10)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    # The dialog is a top-level #32770 window owned by the game process, with a "Play!" button.
    $hwnd = [IntPtr]::Zero
    while (($hwnd = [PerkeleRim.Win32]::FindWindowEx([IntPtr]::Zero, $hwnd, '#32770', $null)) -ne [IntPtr]::Zero) {
        $owner = 0
        [PerkeleRim.Win32]::GetWindowThreadProcessId($hwnd, [ref] $owner) | Out-Null
        $game = Get-Process mysummercar -ErrorAction SilentlyContinue | Where-Object Id -eq $owner
        if (-not $game) { continue }
        $play = [PerkeleRim.Win32]::FindWindowEx($hwnd, [IntPtr]::Zero, 'Button', 'Play!')
        if ($play -ne [IntPtr]::Zero) {
            [PerkeleRim.Win32]::SendMessage($play, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null  # BM_CLICK
            Write-Host "Clicked Play! (pid $owner)"
            $clicked = $true
            break
        }
    }
    if ($clicked) { break }
}
# With -screen-* arguments Unity usually skips the dialog, so not finding it is fine.
if (-not $clicked) { Write-Host "No configuration dialog (skipped by the -screen arguments)." }

# The Amistech news screen waits for "press any key" before the menu (and MSCLoader) loads.
# Keep sending Space to the game window until the spike log says the mod has started.
$log = Join-Path $msc 'PerkeleRim_spike.log'
$launched = Get-Date
$deadline = $launched.AddSeconds($TimeoutSeconds * 2)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    if ((Test-Path $log) -and (Get-Item $log).LastWriteTime -gt $launched) {
        Write-Host 'Mod started (main menu reached).'
        return
    }
    $game = [PerkeleRim.Win32]::FindWindowEx([IntPtr]::Zero, [IntPtr]::Zero, 'UnityWndClass', 'My Summer Car')
    if ($game -ne [IntPtr]::Zero) {
        [PerkeleRim.Win32]::PostMessage($game, 0x0100, [IntPtr]0x20, [IntPtr]0x00390001) | Out-Null  # WM_KEYDOWN Space
        Start-Sleep -Milliseconds 100
        [PerkeleRim.Win32]::PostMessage($game, 0x0101, [IntPtr]0x20, [IntPtr]0xC0390001) | Out-Null  # WM_KEYUP Space
    }
}
throw 'Main menu not reached (spike log not updated). Is the spike build deployed?'
