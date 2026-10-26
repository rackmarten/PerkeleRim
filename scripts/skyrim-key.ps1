<#
.SYNOPSIS
    Brings Skyrim to the front and presses one key there as a real key press (DirectInput sees it),
    e.g. F10 to hand the player to MSC without touching the keyboard. Optionally saves a screenshot
    of the Skyrim window afterwards. With -Console, types console commands instead: opens the console
    (~), types each line and Enter, and closes it again.
.EXAMPLE
    scripts/skyrim-key.ps1 -Key F10
    scripts/skyrim-key.ps1 -Key F5                        # quicksave (F9 quickloads)
    scripts/skyrim-key.ps1 -Screenshot shot.png -Delay 2   # no key, just a screenshot after 2 s
    scripts/skyrim-key.ps1 -Key 4                          # number keys 1 to 8 (the hotbar)
    scripts/skyrim-key.ps1 -Console 'player.additem f 50', 'player.equipitem 64b3d'
#>
param(
    [ValidateSet('', 'F10', 'F11', 'F5', 'F9', 'Escape', 'E', 'Space', 'M', 'P', 'J', 'I', 'T', '1', '2', '3', '4', '5', '6', '7', '8')] [string] $Key = '',
    [string[]] $Console,
    [string] $Screenshot,
    [double] $Delay = 0
)

$scanCodes = @{ F10 = 0x44; F11 = 0x57; F5 = 0x3F; F9 = 0x43; Escape = 0x01; E = 0x12; Space = 0x39; M = 0x32; P = 0x19; J = 0x24; I = 0x17; T = 0x14 }
foreach ($n in 1..8) { $scanCodes["$n"] = 0x01 + $n }  # DIK_1 = 0x02

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

if ($Key) {
    $scan = [byte]$scanCodes[$Key]
    [PerkeleRim.SkyrimKey]::keybd_event(0, $scan, 0x8, [UIntPtr]::Zero)          # KEYEVENTF_SCANCODE
    Start-Sleep -Milliseconds 150
    [PerkeleRim.SkyrimKey]::keybd_event(0, $scan, 0x8 -bor 0x2, [UIntPtr]::Zero)  # ... | KEYEVENTF_KEYUP
    Write-Host "Pressed $Key in Skyrim."
}

# A key by virtual key code, with its scan code, so both DirectInput and the console's text field see it.
function Send-Vk([byte] $vk, [bool] $shift = $false) {
    $scan = [byte][PerkeleRim.SkyrimKey]::MapVirtualKey($vk, 0)
    if ($shift) { [PerkeleRim.SkyrimKey]::keybd_event(0x10, 0x2A, 0, [UIntPtr]::Zero) }
    [PerkeleRim.SkyrimKey]::keybd_event($vk, $scan, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 30
    [PerkeleRim.SkyrimKey]::keybd_event($vk, $scan, 2, [UIntPtr]::Zero)
    if ($shift) { [PerkeleRim.SkyrimKey]::keybd_event(0x10, 0x2A, 2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 30
}

if ($Console) {
    $grave = [byte][PerkeleRim.SkyrimKey]::VkKeyScan([char]'`') -band 0xFF
    Send-Vk $grave
    Start-Sleep -Milliseconds 500
    foreach ($line in $Console) {
        foreach ($ch in $line.ToCharArray()) {
            $code = [PerkeleRim.SkyrimKey]::VkKeyScan($ch)
            Send-Vk ([byte]($code -band 0xFF)) (($code -band 0x100) -ne 0)
        }
        Send-Vk 0x0D  # Enter
        Start-Sleep -Milliseconds 400
        Write-Host "Console: $line"
    }
    Send-Vk $grave
    Start-Sleep -Milliseconds 300
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
