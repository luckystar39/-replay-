# verify-WPF-12: Ctrl+B collapses / expands the left control panel.

#   with keybd_event). The injection half is unchanged -- WPF reads keys through the same
#   Win32 queue -- only the "where is the window" and "what does it say" halves moved to
#   UIA + the debug port.
# ASCII only. Run with: powershell -File <this>   (never pipe its stdout)
$dir = $PSScriptRoot
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$log = Join-Path $dir 'RepRecorder.log'
$cfg = Join-Path $dir 'RepRecorder.settings.json'
$bak = Join-Path $dir '_settings-backup12.json'
$runLog = Join-Path $dir '_run12.log'
$port = 7812
$fail = 0
Remove-Item $runLog -ErrorAction SilentlyContinue

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W12 {
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  public const byte VK_CONTROL = 0x11;
  public const byte VK_B = 0x42;
  public const uint KEYUP = 0x0002;
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr h);
  // Hold Ctrl for real (that is what WPF's Keyboard.Modifiers reads), then post the B key
  // straight at the window's HWND. 2026-10-05: the old version pressed both keys globally and
  // relied on the window being foreground -- on a machine where nothing owns the foreground
  // (this one reports Shell_TrayWnd) SetForegroundWindow is refused by design and the keys go
  // to the taskbar, so the test failed while the feature was fine. Posting is deterministic.
  public static void CtrlBTo(IntPtr h) {
    keybd_event(VK_CONTROL, 0, 0, IntPtr.Zero);
    System.Threading.Thread.Sleep(80);
    SetFocus(h);
    PostMessage(h, 0x0100, (IntPtr)VK_B, IntPtr.Zero);   // WM_KEYDOWN
    System.Threading.Thread.Sleep(80);
    PostMessage(h, 0x0101, (IntPtr)VK_B, IntPtr.Zero);   // WM_KEYUP
    System.Threading.Thread.Sleep(60);
    keybd_event(VK_CONTROL, 0, KEYUP, IntPtr.Zero);
  }
  public static void CtrlB() {
    keybd_event(VK_CONTROL, 0, 0, IntPtr.Zero);
    System.Threading.Thread.Sleep(60);
    keybd_event(VK_B, 0, 0, IntPtr.Zero);
    System.Threading.Thread.Sleep(60);
    keybd_event(VK_B, 0, KEYUP, IntPtr.Zero);
    System.Threading.Thread.Sleep(60);
    keybd_event(VK_CONTROL, 0, KEYUP, IntPtr.Zero);
  }
}
"@

function Dbg([string]$cmd) {
  try {
    $c = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $port)
    $c.ReceiveTimeout = 4000
    $w = New-Object System.IO.StreamWriter($c.GetStream()); $w.AutoFlush = $true; $w.WriteLine($cmd)
    $r = New-Object System.IO.StreamReader($c.GetStream()); $out = $r.ReadLine(); $c.Close()
    return $out
  } catch { return $null }
}
function Check([string]$what, [bool]$ok, [string]$detail) {
  $line = $(if ($ok) { 'OK   ' } else { 'FAIL ' }) + $what + '  ' + $detail
  Write-Host $line
  Add-Content -LiteralPath $runLog -Value $line -Encoding UTF8
  if (-not $ok) { $script:fail = 1 }
}
function LogTxt { if (Test-Path $log) { return (Get-Content $log -Raw -Encoding UTF8) } return '' }

Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 700
Copy-Item $cfg $bak -Force
if (Test-Path $log) { Remove-Item $log -Force }

Start-Sleep -Seconds 3
$p = Start-Process -FilePath $exe -ArgumentList @("--debug-port=$port") -PassThru
$up = $false
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Milliseconds 1000
  if ((Dbg 'status') -ne $null) { $up = $true; break }
  $p.Refresh(); if ($p.HasExited) { break }
}
Check 'app alive' ($up -and -not $p.HasExited) ('pid=' + $p.Id + ' waited=' + $i + 's')
if (-not $up) { Write-Host 'FAILED'; exit 1 }

# --- find the real HWND through UIA (AutomationId 'win') and bring it to front ---
$cond = New-Object System.Windows.Automation.PropertyCondition(
          [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$w = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
       [System.Windows.Automation.TreeScope]::Children, $cond)
Check 'window found via UIA' ($w -ne $null) ''
$hwnd = [IntPtr]::Zero
if ($w -ne $null) { $hwnd = [IntPtr]$w.Current.NativeWindowHandle }
Check 'the window has a native handle' ($hwnd -ne [IntPtr]::Zero) ('hwnd=' + $hwnd)
[W12]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 800
# Informational only: Windows refuses foreground stealing when the calling process is not
# already foreground, so this can legitimately be false on an unattended machine. The keys
# below are posted to the HWND directly, so the test does not depend on this.
Write-Host ('info  foreground is ' + [W12]::GetForegroundWindow() + ' (ours=' + $hwnd + ')')

$s = Dbg 'status' | ConvertFrom-Json
# The collapsed state is a persisted setting, so a previous run (or the user) can leave the
# panel collapsed -- which made this test report four failures while Ctrl+B was working fine.
# Normalise to expanded first, then test the toggle from a known state.
if ($s.collapsed -eq $true) {
  Write-Host 'info  panel was left collapsed - expanding once to get a known start state'
  [W12]::CtrlBTo($hwnd)
  Start-Sleep -Milliseconds 1200
  $s = Dbg 'status' | ConvertFrom-Json
}
Check 'panel starts expanded' ($s.collapsed -eq $false) ('collapsed=' + $s.collapsed)
$w0 = [int]$s.panel
Check 'panel has a real width' ($w0 -gt 100) ('panel=' + $w0)

# --- Ctrl+B collapses ---
Add-Content -LiteralPath $runLog -Value 'step: Ctrl+B (collapse)' -Encoding UTF8
[W12]::CtrlBTo($hwnd)
Start-Sleep -Milliseconds 1200
$s = Dbg 'status' | ConvertFrom-Json
Check 'Ctrl+B collapses the panel' ($s.collapsed -eq $true) ('collapsed=' + $s.collapsed)
Check 'the panel actually shrank' ([int]$s.panel -lt $w0) ('panel=' + $s.panel + ' was=' + $w0)
$t = Dbg 'text:btnPanel' | ConvertFrom-Json
Check 'the toggle button now offers to expand' ($t.text.Length -gt 0 -and $t.text -ne '') ('btn=' + $t.text)

# --- Ctrl+B expands again ---
Add-Content -LiteralPath $runLog -Value 'step: Ctrl+B (expand)' -Encoding UTF8
[W12]::CtrlBTo($hwnd)
Start-Sleep -Milliseconds 1200
$s = Dbg 'status' | ConvertFrom-Json
Check 'Ctrl+B expands it again' ($s.collapsed -eq $false) ('collapsed=' + $s.collapsed)
Check 'the width came back' ([int]$s.panel -eq $w0) ('panel=' + $s.panel + ' expected=' + $w0)

# --- the collapse state survives a restart (same setting the old panel-memory scripts checked) ---
Add-Content -LiteralPath $runLog -Value 'step: collapse then restart' -Encoding UTF8
[W12]::CtrlBTo($hwnd)
Start-Sleep -Milliseconds 1200
$s = Dbg 'status' | ConvertFrom-Json
Check 'collapsed before the restart' ($s.collapsed -eq $true) ('collapsed=' + $s.collapsed)
try { Stop-Process -Id $p.Id -Force } catch { }
Start-Sleep -Milliseconds 900
$p2 = Start-Process -FilePath $exe -ArgumentList @("--debug-port=$port") -PassThru
for ($i = 0; $i -lt 25; $i++) {
  Start-Sleep -Milliseconds 1000
  if ((Dbg 'status') -ne $null) { break }
  $p2.Refresh(); if ($p2.HasExited) { break }
}
$s = Dbg 'status' | ConvertFrom-Json
Check 'the collapsed state survived the restart' ($s.collapsed -eq $true) ('collapsed=' + $s.collapsed)
Check 'no exception in log' ((LogTxt) -notmatch 'Exception') ''
try { Stop-Process -Id $p2.Id -Force } catch { }

Start-Sleep -Milliseconds 500
Copy-Item $bak $cfg -Force
Remove-Item $bak -Force
Write-Host 'settings restored'
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
