<#
.SYNOPSIS
    Brings Skyrim to the front and presses one key there as a real key press (DirectInput sees it),
    e.g. F10 to hand the player back and forth without touching the keyboard. Optionally saves a screenshot
    of the Skyrim window afterwards.
.EXAMPLE
    scripts/skyrim-key.ps1 -Key F10
    scripts/skyrim-key.ps1 -Key F5                        # quicksave (F9 quickloads)
    scripts/skyrim-key.ps1 -Screenshot shot.png -Delay 2   # no key, just a screenshot after 2 s
    scripts/skyrim-key.ps1 -Console 'load PerkeleRim-Kumpo' # types a console command (~, the text, Enter, ~)
    scripts/skyrim-key.ps1 -Console 'completequest MQ101'
    scripts/skyrim-key.ps1 -Key 4                          # number keys 1 to 8 (the hotbar)
#>
param(
    [ValidateSet('', 'F10', 'F11', 'F5', 'F9', 'Escape', 'E', 'Space', 'M', 'P', 'J', 'I', 'T', 'Enter', 'Up', 'Down', '1', '2', '3', '4', '5', '6', '7', '8')] [string] $Key = '',
    [string] $Console,  # letters, digits, space, - _ . only (Skyrim's console ignores case)
    [string] $Screenshot,
    [double] $Delay = 0
)

$scanCodes = @{ F10 = 0x44; F11 = 0x57; F5 = 0x3F; F9 = 0x43; Escape = 0x01; E = 0x12; Space = 0x39; M = 0x32; P = 0x19; J = 0x24; I = 0x17; T = 0x14; Enter = 0x1C; Up = 0x48; Down = 0x50 }  # the arrows are extended keys
foreach ($n in 1..8) { $scanCodes["$n"] = 0x01 + $n }  # number keys (the hotbar): DIK_1 = 0x02

Add-Type -AssemblyName System.Drawing
Add-Type -Namespace PerkeleRim -Name SkyrimKey -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern short VkKeyScan(char ch);
[DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
public struct RECT { public int Left, Top, Right, Bottom; }
'@

$skyrim = Get-Process SkyrimSE -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
if (-not $skyrim) { throw 'Skyrim is not running.' }
$hwnd = $skyrim.MainWindowHandle
# Windows only lets a process take the foreground right after input; a tap of Alt counts.
[PerkeleRim.SkyrimKey]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
[PerkeleRim.SkyrimKey]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
[PerkeleRim.SkyrimKey]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 400
$front = [PerkeleRim.SkyrimKey]::GetForegroundWindow()
$frontPid = 0
[PerkeleRim.SkyrimKey]::GetWindowThreadProcessId($front, [ref] $frontPid) | Out-Null
if ($frontPid -ne $skyrim.Id) { throw 'Windows did not let Skyrim come to the front.' }
$hwnd = $front

function Send-Scan([byte] $Scan, [int] $HoldMs = 150, [switch] $Shift, [switch] $Extended) {
    $flags = 0x8 -bor ($(if ($Extended) { 0x1 } else { 0 }))  # KEYEVENTF_SCANCODE, KEYEVENTF_EXTENDEDKEY
    if ($Shift) { [PerkeleRim.SkyrimKey]::keybd_event(0, 0x2A, 0x8, [UIntPtr]::Zero); Start-Sleep -Milliseconds 30 }
    [PerkeleRim.SkyrimKey]::keybd_event(0, $Scan, $flags, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $HoldMs
    [PerkeleRim.SkyrimKey]::keybd_event(0, $Scan, $flags -bor 0x2, [UIntPtr]::Zero)  # ... | KEYEVENTF_KEYUP
    if ($Shift) { Start-Sleep -Milliseconds 30; [PerkeleRim.SkyrimKey]::keybd_event(0, 0x2A, 0x8 -bor 0x2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 40
}

if ($Key) {
    Send-Scan ([byte]$scanCodes[$Key]) -Extended:($Key -in 'Up', 'Down')
    Write-Host "Pressed $Key in Skyrim."
}

if ($Console) {
    # DirectInput set 1 scan codes of a US layout, by character.
    $rows = @{ 0x10 = 'qwertyuiop'; 0x1E = 'asdfghjkl'; 0x2C = 'zxcvbnm' }
    $chars = @{ ' ' = 0x39; '-' = 0x0C; '.' = 0x34; '0' = 0x0B }
    foreach ($start in $rows.Keys) { for ($i = 0; $i -lt $rows[$start].Length; $i++) { $chars[[string]$rows[$start][$i]] = $start + $i } }
    for ($d = 1; $d -le 9; $d++) { $chars[[string]$d] = 0x01 + $d }
    Send-Scan 0x29  # ~ opens the console
    Start-Sleep -Milliseconds 300
    foreach ($c in $Console.ToCharArray()) {
        if ($c -eq '_') { Send-Scan 0x0C -HoldMs 60 -Shift; continue }
        $code = $chars[([string]$c).ToLowerInvariant()]
        if ($null -eq $code) { throw "Can't type '$c' into Skyrim's console." }
        Send-Scan ([byte]$code) -HoldMs 60
    }
    Send-Scan 0x1C  # Enter
    Start-Sleep -Milliseconds 300
    # ~ closes it; a load or coc closes it by itself, and another ~ would open it over the loading screen.
    if ($Console -notmatch '^\s*(load|coc|coe)\s') { Send-Scan 0x29 }
    Write-Host "Typed '$Console' into Skyrim's console."
}

if ($Screenshot) {
    if ($Delay -gt 0) { Start-Sleep -Milliseconds ([int]($Delay * 1000)) }
    $rect = New-Object PerkeleRim.SkyrimKey+RECT
    [PerkeleRim.SkyrimKey]::GetWindowRect($hwnd, [ref] $rect) | Out-Null
    $bitmap = New-Object System.Drawing.Bitmap ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
    $bitmap.Save([System.IO.Path]::GetFullPath($Screenshot))
    Write-Host "Saved $Screenshot"
}
