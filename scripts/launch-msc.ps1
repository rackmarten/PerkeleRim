<#
.SYNOPSIS
    Starts My Summer Car windowed at its lowest resolution and quality (640x480, "Fastest") and clicks "Play!" on Unity's
    configuration dialog if it appears, then presses Space past the news screen, so a test run needs no clicks. Combine with the spike's auto-load
    (<MSC>/PerkeleRim_autoload.txt) to go straight into the saved game.
.EXAMPLE
    scripts/launch-msc.ps1
    scripts/launch-msc.ps1 -Width 1280 -Height 720
#>
param(
    [int] $Width = 640,
    [int] $Height = 480,
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
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
'@

$log = Join-Path $msc 'PerkeleRim_spike.log'
$launched = Get-Date
Start-Process -WorkingDirectory $msc (Join-Path $msc 'mysummercar.exe') `
    -ArgumentList '-screen-fullscreen', '0', '-screen-width', $Width, '-screen-height', $Height, '-screen-quality', 'Fastest'

# Until the spike log says the mod has started (main menu reached):
#  - click "Play!" on Unity's configuration dialog. The -screen-* arguments don't skip it in MSC's
#    Unity version, and on a cold start it can take over a minute to appear. Found by class and title:
#    PowerShell passes $null to a string parameter as "", which would match nothing.
#  - press Space on the game window once it's visible (Unity creates it hidden before the dialog):
#    the Amistech news screen waits for "press any key" before the menu (and MSCLoader) loads.
$clicked = $false
$deadline = $launched.AddSeconds($TimeoutSeconds * 3)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    if ((Test-Path $log) -and (Get-Item $log).LastWriteTime -gt $launched) {
        Write-Host 'Mod started (main menu reached).'
        return
    }
    if (-not $clicked) {
        $dialog = [PerkeleRim.Win32]::FindWindowEx([IntPtr]::Zero, [IntPtr]::Zero, '#32770', 'My Summer Car Configuration')
        $owner = 0
        if ($dialog -ne [IntPtr]::Zero) { [PerkeleRim.Win32]::GetWindowThreadProcessId($dialog, [ref] $owner) | Out-Null }
        # Steam may restart the game under a new pid, so any mysummercar process counts.
        $play = if ($owner -and (Get-Process mysummercar -ErrorAction SilentlyContinue | Where-Object Id -eq $owner)) { [PerkeleRim.Win32]::FindWindowEx($dialog, [IntPtr]::Zero, 'Button', 'Play!') } else { [IntPtr]::Zero }
        if ($play -ne [IntPtr]::Zero) {
            [PerkeleRim.Win32]::SendMessage($play, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null  # BM_CLICK
            Write-Host "Clicked Play! after $([int]((Get-Date) - $launched).TotalSeconds) s"
            $clicked = $true
        }
    }
    $game = [PerkeleRim.Win32]::FindWindowEx([IntPtr]::Zero, [IntPtr]::Zero, 'UnityWndClass', 'My Summer Car')
    if ($game -ne [IntPtr]::Zero -and [PerkeleRim.Win32]::IsWindowVisible($game)) {
        [PerkeleRim.Win32]::PostMessage($game, 0x0100, [IntPtr]0x20, [IntPtr]0x00390001) | Out-Null  # WM_KEYDOWN Space
        Start-Sleep -Milliseconds 100
        [PerkeleRim.Win32]::PostMessage($game, 0x0101, [IntPtr]0x20, [IntPtr]0xC0390001) | Out-Null  # WM_KEYUP Space
        Start-Sleep -Milliseconds 1400
    }
}
throw 'Main menu not reached (spike log not updated). Is the spike build deployed?'
