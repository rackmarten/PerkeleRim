<#
.SYNOPSIS
    Starts My Summer Car windowed at its lowest resolution and quality (640x480, "Fastest") with single-threaded
    rendering (-force-gfx-direct, which the hand layer needs) and clicks "Play!" on Unity's
    configuration dialog if it appears, then presses Space past the news screen, so a test run needs no clicks. Combine with the spike's auto-load
    (<MSC>/PerkeleRim_autoload.txt) to go straight into the saved game. MSC's own screen settings
    are saved first and put back once it closes, as the player launcher does (launcher/install.ps1).
.EXAMPLE
    scripts/launch-msc.ps1
    scripts/launch-msc.ps1 -Width 1280 -Height 720
    scripts/launch-msc.ps1 -Width 2560 -Height 1440 -Fullscreen -Quality Fantastic   # for filming MSC itself
#>
param(
    [int] $Width = 640,
    [int] $Height = 480,
    [string] $Quality = 'Fastest',
    [switch] $Fullscreen,
    [int] $TimeoutSeconds = 60
)

. "$PSScriptRoot/common.ps1"
. "$RepoRoot/launcher/checks.ps1"
. "$RepoRoot/launcher/install.ps1"
$msc = Get-RequiredSetting (Get-LocalConfig) 'MscPath'

if (Get-Process mysummercar -ErrorAction SilentlyContinue) { throw 'My Summer Car is already running.' }

Add-Type -Namespace PerkeleRim -Name Win32 -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
'@

# Unity keeps the screen mode MSC ran in as its own settings when it quits: the player launcher's
# hidden watcher puts the ones from before back once MSC has closed.
[void] (Save-MscScreenSettings $MscRegistryKey $MscScreenBackup)
$log = Join-Path $msc 'PerkeleRim_spike.log'
$launched = Get-Date
Start-Process -WorkingDirectory $msc (Join-Path $msc 'mysummercar.exe') `
    -ArgumentList '-force-gfx-direct', '-screen-fullscreen', ([int] $Fullscreen.IsPresent), '-screen-width', $Width, '-screen-height', $Height, '-screen-quality', $Quality
Start-MscScreenWatcher (Join-Path $RepoRoot 'launcher/PerkeleRim.ps1')

# Until the spike log says the mod has started (main menu reached):
#  - click "Play!" on Unity's configuration dialog. The -screen-* arguments don't skip it in MSC's
#    Unity version, and on a cold start it can take over a minute to appear. Found by class and title:
#    PowerShell passes $null to a string parameter as "", which would match nothing.
#  - press Space on the game window once it's visible (Unity creates it hidden before the dialog):
#    the Amistech news screen waits for "press any key" before the menu (and MSCLoader) loads. A real
#    key press on the focused window: with -force-gfx-direct, Unity ignores posted key messages.
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
        [PerkeleRim.Win32]::SetForegroundWindow($game) | Out-Null
        Start-Sleep -Milliseconds 200
        [PerkeleRim.Win32]::keybd_event(0x20, 0x39, 0, [UIntPtr]::Zero)  # Space down
        Start-Sleep -Milliseconds 100
        [PerkeleRim.Win32]::keybd_event(0x20, 0x39, 2, [UIntPtr]::Zero)  # KEYEVENTF_KEYUP
        Start-Sleep -Milliseconds 1400
    }
}
throw 'Main menu not reached (spike log not updated). Is the spike build deployed?'
