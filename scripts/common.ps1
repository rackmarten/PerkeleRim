# Shared helpers for the build/deploy/log scripts. Dot-source it: . "$PSScriptRoot/common.ps1"

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot

# Reads local.props (MSBuild XML) into a hashtable. Copy local.props.example to create it.
function Get-LocalConfig {
    $path = Join-Path $RepoRoot 'local.props'
    if (-not (Test-Path $path)) {
        throw "local.props not found. Copy local.props.example to local.props in the repo root and set your game paths."
    }
    $config = @{}
    foreach ($group in ([xml](Get-Content -Raw $path)).Project.PropertyGroup) {
        foreach ($node in $group.ChildNodes) {
            if ($node.NodeType -eq 'Element') { $config[$node.LocalName] = $node.InnerText.Trim() }
        }
    }
    return $config
}

function Get-RequiredSetting($config, $name) {
    if (-not $config[$name]) { throw "$name is not set in local.props." }
    return $config[$name]
}

# blender.exe: BlenderPath from local.props, else the newest install under Program Files.
function Get-Blender {
    $config = if (Test-Path (Join-Path $RepoRoot 'local.props')) { Get-LocalConfig } else { @{} }
    if ($config['BlenderPath']) { return $config['BlenderPath'] }
    $found = Get-ChildItem (Join-Path $env:ProgramFiles 'Blender Foundation') -Filter blender.exe -Recurse -Depth 1 -ErrorAction SilentlyContinue |
        Sort-Object { $_.Directory.Name } | Select-Object -Last 1
    if (-not $found) { throw "Blender not found. Install it (winget install BlenderFoundation.Blender) or set BlenderPath in local.props." }
    return $found.FullName
}

# Enters the x64 MSVC developer environment of the newest VS / Build Tools install with the C++ tools.
function Enter-DevShell {
    if ($env:VSCMD_ARG_TGT_ARCH -eq 'x64') { return }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found. Install Build Tools for Visual Studio 2022 (docs/DEV_SETUP.md)." }
    $vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vsPath) { throw "No Visual Studio install with the C++ x64 tools found. Add the 'Desktop development with C++' workload." }
    # A stray quote in PATH makes VsDevCmd.bat fail on "(x86)" entries; strip quotes for this process only.
    # VsDevCmd.bat also runs vswhere.exe by name, so put its folder on PATH or every build logs "not recognized".
    $env:Path = (Split-Path $vswhere) + ';' + ($env:Path -replace '"', '')
    Import-Module (Join-Path $vsPath 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll')
    Enter-VsDevShell -VsInstallPath $vsPath -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
}

# --- Game window placement ---------------------------------------------------------------------
# Dev layout on the primary screen: Skyrim in the top right corner, My Summer Car directly left of it
# with the top edges aligned. Positions come from the screen's working area (taskbar excluded) and
# the windows' actual outer sizes, so borders, title bars and other resolutions all work.

function Initialize-WindowPlacement {
    if (-not ('PerkeleRim.WinPlace' -as [type])) {
        Add-Type -Namespace PerkeleRim -Name WinPlace -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[DllImport("user32.dll")] static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, out RECT r, uint flags);
[DllImport("user32.dll")] static extern bool AdjustWindowRectEx(ref RECT r, uint style, bool menu, uint exStyle);
[DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

public static void MakeDpiAware() { SetProcessDPIAware(); }
public static bool Visible(IntPtr h) { return h != IntPtr.Zero && IsWindowVisible(h); }

// Working area of the primary screen (taskbar excluded): left, top, right, bottom.
public static int[] WorkArea() {
    RECT r; SystemParametersInfo(0x30, 0, out r, 0);   // SPI_GETWORKAREA
    return new[] { r.L, r.T, r.R, r.B };
}

// The window's visible bounds: left, top, right, bottom. Win10+ adds invisible resize borders to the
// window rect, which would leave a gap between windows placed edge to edge.
public static int[] VisibleRect(IntPtr h) {
    RECT r;
    if (DwmGetWindowAttribute(h, 9, out r, 16) != 0) GetWindowRect(h, out r);   // DWMWA_EXTENDED_FRAME_BOUNDS
    return new[] { r.L, r.T, r.R, r.B };
}

// Outer size of a captioned window with the given client size (used before the window exists).
public static int[] OuterSize(int w, int h) {
    RECT r = new RECT { L = 0, T = 0, R = w, B = h };
    AdjustWindowRectEx(ref r, 0x00C80000, false, 0);   // WS_CAPTION | WS_SYSMENU
    return new[] { r.R - r.L, r.B - r.T };
}

// Moves the window so its visible top left lands on (x, y). No resize, no z-order or focus change.
public static void MoveVisible(IntPtr h, int x, int y) {
    RECT win; GetWindowRect(h, out win);
    int[] vis = VisibleRect(h);
    SetWindowPos(h, IntPtr.Zero, x - (vis[0] - win.L), y - (vis[1] - win.T), 0, 0, 0x0001 | 0x0004 | 0x0010);   // NOSIZE | NOZORDER | NOACTIVATE
}
'@
    }
    [PerkeleRim.WinPlace]::MakeDpiAware()
}

function Get-MscWindow {
    Initialize-WindowPlacement
    $h = [PerkeleRim.WinPlace]::FindWindowEx([IntPtr]::Zero, [IntPtr]::Zero, 'UnityWndClass', 'My Summer Car')
    if ([PerkeleRim.WinPlace]::Visible($h)) { return $h }
    return [IntPtr]::Zero
}

function Get-SkyrimWindow {
    Initialize-WindowPlacement
    foreach ($p in Get-Process SkyrimSE -ErrorAction SilentlyContinue) {
        if ([PerkeleRim.WinPlace]::Visible($p.MainWindowHandle)) { return $p.MainWindowHandle }
    }
    return [IntPtr]::Zero
}

# Waits until the window exists, is visible and has stopped changing size (games resize it while starting).
# Returns $null on timeout.
function Wait-GameWindow([scriptblock] $Find, [int] $TimeoutSeconds = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $last = ''
    while ((Get-Date) -lt $deadline) {
        $h = & $Find
        if ($h -ne [IntPtr]::Zero) {
            $now = [PerkeleRim.WinPlace]::VisibleRect($h) -join ','
            if ($now -eq $last) { return $h }
            $last = $now
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

# Skyrim's windowed size from SkyrimPrefs.ini, as a guess for where to put MSC before Skyrim is up.
function Get-SkyrimPrefsSize {
    $ini = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Skyrim Special Edition\SkyrimPrefs.ini'
    $w = 1920; $h = 1080
    if (Test-Path $ini) {
        $text = Get-Content -Raw $ini
        if ($text -match '(?m)^iSize W=(\d+)') { $w = [int]$Matches[1] }
        if ($text -match '(?m)^iSize H=(\d+)') { $h = [int]$Matches[1] }
    }
    return [PerkeleRim.WinPlace]::OuterSize($w, $h)
}

# Puts whichever of the two game windows are up in the dev layout. Safe to call again as windows appear:
# with Skyrim not up yet, MSC goes where it will be once Skyrim opens (its width comes from SkyrimPrefs.ini).
function Set-GameWindowLayout {
    Initialize-WindowPlacement
    $work = [PerkeleRim.WinPlace]::WorkArea()
    $skyrim = Get-SkyrimWindow
    if ($skyrim -ne [IntPtr]::Zero) {
        $r = [PerkeleRim.WinPlace]::VisibleRect($skyrim)
        $sw = $r[2] - $r[0]
    } else {
        $sw = (Get-SkyrimPrefsSize)[0]
    }
    $skyLeft = $work[2] - $sw
    $skyTop = $work[1]
    if ($skyrim -ne [IntPtr]::Zero) { [PerkeleRim.WinPlace]::MoveVisible($skyrim, $skyLeft, $skyTop) }

    $msc = Get-MscWindow
    if ($msc -ne [IntPtr]::Zero) {
        $r = [PerkeleRim.WinPlace]::VisibleRect($msc)
        $x = [Math]::Max($work[0], $skyLeft - ($r[2] - $r[0]))
        [PerkeleRim.WinPlace]::MoveVisible($msc, $x, $skyTop)
    }
}
