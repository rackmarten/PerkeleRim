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
    scripts/launch-msc.ps1 -NoPlace   # leave the window where Windows puts it
    scripts/launch-msc.ps1 -Hidden    # a test session's partner (skytest.psd1): minimized, never focused, no key presses
.NOTES
    -Hidden skips Unity's configuration dialog and the news screen with MSCLoader's own skipConfigScreen and skipIntro
    (doorstop_config.ini, put back as soon as the mod has started, or at the next start if this was killed), starts MSC
    minimized without activating it and has it continue its save (a one-shot PerkeleRim_autoload.txt, which the mod
    deletes once used, unless the folder has its own marker). It returns once the mod has started; nothing is pressed
    or clicked, so the desktop stays usable.
    Windowed MSC is moved to the left of Skyrim's spot in the top right corner of the primary screen,
    top edges aligned (see Set-GameWindowLayout in common.ps1). Fullscreen is never moved.
#>
param(
    [int] $Width = 640,
    [int] $Height = 480,
    [string] $Quality = 'Fastest',
    [switch] $Fullscreen,
    [switch] $NoPlace,
    [switch] $Hidden,
    [int] $TimeoutSeconds = 60
)

. "$PSScriptRoot/common.ps1"
. "$RepoRoot/launcher/checks.ps1"
. "$RepoRoot/launcher/install.ps1"
. "$PSScriptRoot/test-session.ps1"
$msc = Get-RequiredSetting (Get-LocalConfig) 'MscPath'

# -Hidden: MSCLoader's own switches skip the dialog and the news screen; the player's doorstop_config.ini is kept
# meanwhile ($MscDoorstopKept, test-session.ps1; scripts/test-deploy.ps1 restore puts it back too).
$doorstop = Join-Path $msc 'doorstop_config.ini'
[void] (Restore-MscDoorstop $msc)  # a hidden start that was killed before it put it back

if (Get-Process mysummercar -ErrorAction SilentlyContinue) { throw 'My Summer Car is already running.' }

Add-Type -Namespace PerkeleRim -Name Win32 -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
[DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
[DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hwnd, int cmd);
[DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint to, bool on);
[DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
'@

if ($Hidden) {
    if (-not (Test-Path -LiteralPath $doorstop)) { throw "No ${doorstop}: is MSCLoader installed?" }
    $marker = Join-Path $msc 'PerkeleRim_autoload.txt'
    if (-not (Test-Path -LiteralPath $marker)) {
        [IO.File]::WriteAllText($marker, 'Written by the PerkeleRim launcher.', (New-Object Text.UTF8Encoding $false))  # AutoloadMarker.LauncherText
    }
    [void] (Save-MscScreenSettings $MscRegistryKey $MscScreenBackup)
    New-Item -ItemType Directory -Force (Split-Path $MscDoorstopKept) | Out-Null
    Copy-Item -LiteralPath $doorstop $MscDoorstopKept -Force
    try {
        $text = [IO.File]::ReadAllText($doorstop)
        [IO.File]::WriteAllText($doorstop, (Set-IniText $text 'MSCLoader' @{ skipIntro = 'true'; skipConfigScreen = 'true' }))
        $log = Join-Path $msc 'PerkeleRim_spike.log'
        $launched = Get-Date
        $before = [PerkeleRim.Win32]::GetForegroundWindow()  # whatever the desktop is doing: it gets the foreground back
        Start-Process -WorkingDirectory $msc -WindowStyle Minimized (Join-Path $msc 'mysummercar.exe') `
            -ArgumentList '-force-gfx-direct', '-screen-fullscreen', '0', '-screen-width', $Width, '-screen-height', $Height, '-screen-quality', $Quality
        Start-MscScreenWatcher (Join-Path $RepoRoot 'launcher/PerkeleRim.ps1')
        # Unity takes the foreground about 1.5 s after it starts, its window still hidden (Windows lets it while nobody
        # has used the keyboard or mouse for a while), and shows it later: minimized whenever it's up and not, which
        # hands the activation on (SW_MINIMIZE), and the foreground given back to the window that had it should MSC
        # still hold it. That can only happen once MSC's thread answers, 3 to 4 s after its grab while it loads.
        # Until MSC is in its game with the player (the spike's probe says scene=GAME and where he is), and a moment more.
        $deadline = $launched.AddSeconds($TimeoutSeconds * 3)
        $menuAt = $null
        $inGameAt = $null
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 250
            $game = [PerkeleRim.Win32]::FindWindowEx([IntPtr]::Zero, [IntPtr]::Zero, 'UnityWndClass', 'My Summer Car')
            if ($game -ne [IntPtr]::Zero -and [PerkeleRim.Win32]::IsWindowVisible($game) -and -not [PerkeleRim.Win32]::IsIconic($game)) {
                [void] [PerkeleRim.Win32]::ShowWindowAsync($game, 6)  # SW_MINIMIZE, without waiting on MSC's busy thread
            }
            $front = [PerkeleRim.Win32]::GetForegroundWindow()
            $owner = 0
            $thread = if ($front -ne [IntPtr]::Zero) { [PerkeleRim.Win32]::GetWindowThreadProcessId($front, [ref] $owner) } else { 0 }
            if ($owner -and (Get-Process -Id $owner -ErrorAction SilentlyContinue).ProcessName -eq 'mysummercar' -and
                $before -ne [IntPtr]::Zero -and [PerkeleRim.Win32]::IsWindow($before)) {
                # Windows lets only the foreground's own input thread pass the foreground on: share it for the call.
                $me = [PerkeleRim.Win32]::GetCurrentThreadId()
                $attached = [PerkeleRim.Win32]::AttachThreadInput($me, $thread, $true)
                [void] [PerkeleRim.Win32]::SetForegroundWindow($before)
                if ($attached) { [void] [PerkeleRim.Win32]::AttachThreadInput($me, $thread, $false) }
            }
            if (-not $menuAt -and (Test-Path $log) -and (Get-Item $log).LastWriteTime -gt $launched) {
                $menuAt = Get-Date
                [void] (Restore-MscDoorstop $msc)  # read at the start, no longer needed
            }
            if ($menuAt -and -not $inGameAt -and (Select-String -LiteralPath $log -Pattern 'scene=GAME .*pos=' -Quiet)) { $inGameAt = Get-Date }
            if ($inGameAt -and ((Get-Date) - $inGameAt).TotalSeconds -ge 3) {
                Write-Host "Mod started after $([int]($menuAt - $launched).TotalSeconds) s, MSC in its game after $([int]($inGameAt - $launched).TotalSeconds) s, minimized."
                return
            }
            if (-not (Get-Process mysummercar -ErrorAction SilentlyContinue) -and ((Get-Date) - $launched).TotalSeconds -gt 20) {
                throw 'My Summer Car exited before its main menu (its output_log.txt may say why).'
            }
        }
        if ($menuAt) { throw 'My Summer Car reached its main menu but not its game: did it continue its save (PerkeleRim_spike.log)?' }
        throw 'Main menu not reached (spike log not updated). Is the mod deployed?'
    } finally {
        [void] (Restore-MscDoorstop $msc)
    }
}

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
$place = -not $NoPlace -and -not $Fullscreen
$placed = $false
$deadline = $launched.AddSeconds($TimeoutSeconds * 3)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    if ((Test-Path $log) -and (Get-Item $log).LastWriteTime -gt $launched) {
        if ($place) { Set-GameWindowLayout }
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
        if ($place -and -not $placed) { Set-GameWindowLayout; $placed = $true }
        [PerkeleRim.Win32]::SetForegroundWindow($game) | Out-Null
        Start-Sleep -Milliseconds 200
        [PerkeleRim.Win32]::keybd_event(0x20, 0x39, 0, [UIntPtr]::Zero)  # Space down
        Start-Sleep -Milliseconds 100
        [PerkeleRim.Win32]::keybd_event(0x20, 0x39, 2, [UIntPtr]::Zero)  # KEYEVENTF_KEYUP
        Start-Sleep -Milliseconds 1400
    }
}
throw 'Main menu not reached (spike log not updated). Is the spike build deployed?'
