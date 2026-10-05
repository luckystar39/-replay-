# 验证WPF-01: the WPF shell (--wpf) comes up, its AutomationIds are reachable via UIA,
# the process is DPI aware, and a resize step is cheap.
# ASCII only. Paths come from $PSScriptRoot.
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W1 {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, Rt, B; }
  public static int WW(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.Rt-r.L; }
  public static int WH(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.B-r.T; }
}
"@
[W1]::SetProcessDPIAware() | Out-Null

$dir = $PSScriptRoot
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$fail = 0

Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 600
$p = Start-Process -FilePath $exe -ArgumentList '--wpf' -PassThru
Start-Sleep -Seconds 4
$p.Refresh()
if ($p.HasExited) { Write-Host ('FAIL: exited early, code={0}' -f $p.ExitCode); exit 1 }
Write-Host ('OK  process alive, pid={0}' -f $p.Id)

# --- window via UIA ---
$auto = [System.Windows.Automation.AutomationElement]
$root = $auto::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition($auto::AutomationIdProperty, 'win')
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if ($win -eq $null) { Write-Host 'FAIL: no UIA element with AutomationId=win'; $fail = 1; }
else { Write-Host ('OK  window found by AutomationId=win, name="{0}"' -f $win.Current.Name) }

# --- the shell's AutomationIds ---
# statusText / statusEnv dropped 2026-10-05: the user had the bottom status bar removed.
$want = @('btnPanel', 'btnBg', 'capMin', 'capMax', 'capClose', 'panelLeft', 'splitterV', 'splitterH', 'logBox')
foreach ($id in $want) {
  $c = New-Object System.Windows.Automation.PropertyCondition($auto::AutomationIdProperty, $id)
  $e = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
  if ($e -eq $null) { Write-Host ("FAIL: AutomationId '{0}' not reachable via UIA" -f $id); $fail = 1 }
  else { Write-Host ("OK  {0} -> {1}" -f $id, $e.Current.ControlType.ProgrammaticName) }
}

$h = [IntPtr]$win.Current.NativeWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Host 'FAIL: no native window handle'; $fail = 1 }
else {
  $dpi = [W1]::GetDpiForWindow($h)
  Write-Host ("     window {0}x{1} px, dpi={2}" -f [W1]::WW($h), [W1]::WH($h), $dpi)
  if ($dpi -lt 192) { Write-Host ('FAIL: dpi={0} -- process is not DPI aware on this 300% display' -f $dpi); $fail = 1 }
  else { Write-Host 'OK  DPI aware (window is really that many physical pixels)' }

  # --- resize cost: 30 steps, drag-emulated ---
  [W1]::SendMessage($h, 0x0231, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
  $p.Refresh(); $c0 = $p.TotalProcessorTime.TotalMilliseconds
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  for ($i = 0; $i -lt 30; $i++) {
    $d = (($i % 20) - 10) * 3
    [W1]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 3540 + $d * 3, 1944 + $d * 3, 0x0004) | Out-Null
  }
  $sw.Stop()
  $p.Refresh(); $c1 = $p.TotalProcessorTime.TotalMilliseconds
  [W1]::SendMessage($h, 0x0232, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
  $cpu = ($c1 - $c0) / 30; $wall = $sw.Elapsed.TotalMilliseconds / 30
  Write-Host ("     resize step: CPU {0:N1} ms  wall {1:N1} ms   (WinForms old: 47 ms wall)" -f $cpu, $wall)
  if ($wall -gt 25) { Write-Host ('FAIL: wall {0:N1} ms per step is too slow' -f $wall); $fail = 1 }
  else { Write-Host 'OK  resize is fast' }

  # --- screenshot for the eye ---
  $bmp = New-Object System.Drawing.Bitmap ([W1]::WW($h)), ([W1]::WH($h))
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc(); [W1]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
  $shot = Join-Path $dir '_shot-01-shell.png'
  $bmp.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  Write-Host ('     screenshot -> {0}' -f $shot)
}

try { Stop-Process -Id $p.Id -Force } catch { }
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
