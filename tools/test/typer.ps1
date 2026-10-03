param([string]$Text, [string]$Marker)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Kb {
  [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
  [StructLayout(LayoutKind.Explicit, Size=40)] public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
  [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  public static void Send(ushort vk, ushort scan, uint flags) {
    INPUT[] a = new INPUT[1]; a[0].type = 1; a[0].ki.wVk = vk; a[0].ki.wScan = scan; a[0].ki.dwFlags = flags;
    SendInput(1, a, Marshal.SizeOf(typeof(INPUT)));
  }
}
"@
$deadline = (Get-Date).AddSeconds(240)
while (-not (Test-Path $Marker) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
if (-not (Test-Path $Marker)) { "marker not found"; exit 1 }
$p = Get-Process "Iron Lung" | Select-Object -First 1
[Kb]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 700
if ([Kb]::GetForegroundWindow() -ne $p.MainWindowHandle) { "game window is not in foreground - aborting, nothing typed"; exit 2 }
foreach ($ch in $Text.ToCharArray()) {
  if ([Kb]::GetForegroundWindow() -ne $p.MainWindowHandle) { "focus lost - aborting"; exit 3 }
  [Kb]::Send(0, [uint16][char]$ch, 4); Start-Sleep -Milliseconds 40
  [Kb]::Send(0, [uint16][char]$ch, 6); Start-Sleep -Milliseconds 60
}
Start-Sleep -Milliseconds 300
[Kb]::Send(0x0D, 0, 0); Start-Sleep -Milliseconds 60; [Kb]::Send(0x0D, 0, 2)
"typed"
