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
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [StructLayout(LayoutKind.Sequential)] public struct RECT2 { public int L, T, Rt, B; }
  [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT2 rcMonitor; public RECT2 rcWork; public uint dwFlags; }
  [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
  [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr mon, int type, out uint x, out uint y);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
  [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, Rt, B; }
  public static int WW(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.Rt-r.L; }
  public static int WH(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.B-r.T; }
  // The monitor's real DPI. GetDpiForWindow lies (returns 96) for a DPI-unaware window,
  // so the two values have to be compared against each other, not against a constant.
  public static int MonitorDpi(IntPtr h){
    uint x, y;
    if (GetDpiForMonitor(MonitorFromWindow(h, 2), 0, out x, out y) == 0) return (int)x;
    return 0;
  }
}
"@
[W1]::SetProcessDPIAware() | Out-Null

$here = $PSScriptRoot                  # this script lives in the tests folder
$dir  = Split-Path $here -Parent       # the app folder: the exe and its log/settings live there
$src  = Join-Path $dir 'src'   # source code folder
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
  $mdpi = [W1]::MonitorDpi($h)
  Write-Host ("     window {0}x{1} px, window-dpi={2}, monitor-dpi={3}" -f [W1]::WW($h), [W1]::WH($h), $dpi, $mdpi)
  # The property under test is "the process is DPI aware", NOT "this machine is a 300%
  # display". 2026-10-07 this went red purely because the display had been changed to
  # 150%: dpi came back 144 and the old assertion wanted >=192.
  # A DPI-UNAWARE window always reports 96 from GetDpiForWindow, whatever the monitor is
  # (see the note in the testing-limits memory), so the honest test is:
  # does the window agree with the monitor's real DPI?
  if ($mdpi -le 96) { Write-Host 'OK  display is at 100% -- DPI awareness is not observable here' }
  elseif ($dpi -eq $mdpi) { Write-Host ('OK  DPI aware (window reports the real {0} dpi, not the virtualized 96)' -f $mdpi) }
  else { Write-Host ('FAIL: window-dpi={0} but monitor-dpi={1} -- process is not DPI aware' -f $dpi, $mdpi); $fail = 1 }

  # --- rounded corners (2026-10-07) ---
  # Two separate things have to hold, and the first one alone proves nothing:
  #   1. our code asks DWM for round corners (DWMWA_WINDOW_CORNER_PREFERENCE = 2)
  #   2. DWM actually honours it -- i.e. the corner pixel is NOT covered by the window
  # Setting the attribute and getting rounded corners are different events (the probe that
  # first tried this from outside had to wait for DWM to catch up), so both are checked.
  #
  # The corner check deliberately does NOT compare colours: the desktop wallpaper and this
  # app's dark chrome can be nearly the same shade, and an earlier colour-matching version
  # of this very check reported "square" on a window that was visibly rounded.
  # Instead: sample the corner, then move the window aside and sample the SAME screen pixel.
  # If the two match, that pixel was never covered -- which is what a rounded corner means.
  $pref = -1
  [W1]::DwmGetWindowAttribute($h, 33, [ref]$pref, 4) | Out-Null
  if ($pref -eq 2) { Write-Host ('OK  asks DWM for rounded corners (pref=' + $pref + ')') }
  else { Write-Host ('FAIL: corner preference = ' + $pref + ' (0=DEFAULT 1=SQUARE 2=ROUND 3=ROUNDSMALL)'); $fail = 1 }

  # Same-place, self-calibrating check: flip DWM between SQUARE and ROUND and compare the
  # very same corner pixels. The earlier version sampled the corner, slid the window away and
  # sampled again -- with a video playing on the desktop the two grabs are seconds apart, so
  # the "desktop" changes on its own and the comparison was noise (it went red on 2026-10-07
  # at corner=144/interior=360 purely because the wallpaper contrast happened to be low).
  # Comparing the same pixels in the two DWM states cancels the wallpaper out entirely:
  # a square corner is window chrome in BOTH states, so it cannot differ.
  $rc = New-Object W1+RECT
  [W1]::GetWindowRect($h, [ref]$rc) | Out-Null
  function CornerPatch {
    $b = New-Object System.Drawing.Bitmap 8, 8
    $gg = [System.Drawing.Graphics]::FromImage($b)
    $gg.CopyFromScreen(($rc.L + 2), ($rc.T + 2), 0, 0, (New-Object System.Drawing.Size 8, 8))
    $gg.Dispose()
    $s = 0
    for ($i = 0; $i -lt 8; $i++) {
      for ($j = 0; $j -lt 8; $j++) { $c = $b.GetPixel($i, $j); $s += ($c.R + $c.G + $c.B) }
    }
    $b.Dispose()
    return [int]($s / 64)
  }
  $sq = 1                                   # DWMWCP_DONOTROUND
  [W1]::DwmSetWindowAttribute($h, 33, [ref]$sq, 4) | Out-Null
  Start-Sleep -Milliseconds 900
  $sqv = CornerPatch
  $rd = 2                                   # DWMWCP_ROUND  (what the app asks for)
  [W1]::DwmSetWindowAttribute($h, 33, [ref]$rd, 4) | Out-Null
  Start-Sleep -Milliseconds 900
  $rdv = CornerPatch
  # Reported, NOT asserted. Three attempts at automating "is the corner really round" all
  # proved unreliable on this machine, because the desktop is a full-screen video:
  #   · corner colour vs interior colour  -> failed on a dark wallpaper
  #   · corner before/after sliding the window away -> the video itself changes in between
  #   · DWM SQUARE vs ROUND on the same pixels -> Win11 does not reliably honour DONOTROUND
  #     (the default is already round), so both states came back the same
  # The visual check was done by hand (the corner is visibly rounded, radius ~8 DIP). What
  # IS asserted above -- that the app asks for DWMWCP_ROUND -- is the part our code owns.
  Write-Host ('     corner patch: square=' + $sqv + '  round=' + $rdv + '  (informational, see the note in this script)')

  # --- resize cost: 30 steps, drag-emulated ---
  # Base size was hardcoded 3540x1944 -- that is 1180x648 DIP at 300%. Derive it from the
  # real DPI instead, so the window stays the same size in DIP (and the old 47 ms WinForms
  # baseline stays comparable) whatever the display scaling happens to be.
  $scale = $mdpi / 96.0
  if ($scale -le 0) { $scale = 1.0 }
  $baseW = [int](1180 * $scale); $baseH = [int](648 * $scale)
  Write-Host ("     resize base {0}x{1} px  (= 1180x648 DIP at {2:N0}%)" -f $baseW, $baseH, ($scale * 100))
  [W1]::SendMessage($h, 0x0231, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
  $p.Refresh(); $c0 = $p.TotalProcessorTime.TotalMilliseconds
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  for ($i = 0; $i -lt 30; $i++) {
    $d = (($i % 20) - 10) * 3
    [W1]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, $baseW + $d * 3, $baseH + $d * 3, 0x0004) | Out-Null
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
  $shot = Join-Path $here '_shot-01-shell.png'
  $bmp.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  Write-Host ('     screenshot -> {0}' -f $shot)

  # --- maximised: the window must land ON the work area, not 11px past it -------------
  # 2026-10-07, from the user's "top is stuck to the ceiling and looks bad".
  # WindowStyle=None + WindowChrome means our client area IS the whole window, but Windows
  # still treats it as a framed window and pushes the resize frame off-screen when maximised
  # -- so the frame it pushes off is the UI itself. Measured before the fix: the window sat
  # at -11,-11 2582x1550 on a 0,0 2560x1528 work area, i.e. 11 px sliced off all four edges,
  # which cut the top half of the title text away (that is what "stuck to the ceiling" was).
  # Both directions are checked:
  #   overhang  > 1  =>  part of the UI is off-screen (the bug)
  #   gap       > 1  =>  the desktop shows around a maximised window
  # 1 px of slack because the fix deliberately asks for one pixel less than the work area
  # (asking for exactly the work area is what triggers the frame push-out, see UiWindow.Hook).
  [W1]::ShowWindow($h, 3) | Out-Null          # SW_MAXIMIZE
  Start-Sleep -Milliseconds 1200
  $mz = New-Object W1+RECT
  [W1]::GetWindowRect($h, [ref]$mz) | Out-Null
  $mi = New-Object W1+MONITORINFO
  $mi.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($mi)
  [W1]::GetMonitorInfo([W1]::MonitorFromWindow($h, 2), [ref]$mi) | Out-Null
  $w = $mi.rcWork
  $over = New-Object int[] 4
  $over[0] = $w.L - $mz.L; $over[1] = $w.T - $mz.T
  $over[2] = $mz.Rt - $w.Rt; $over[3] = $mz.B - $w.B
  $gap = New-Object int[] 4
  $gap[0] = $mz.L - $w.L; $gap[1] = $mz.T - $w.T
  $gap[2] = $w.Rt - $mz.Rt; $gap[3] = $w.B - $mz.B
  $ov = ($over | Measure-Object -Maximum).Maximum
  $gp = ($gap | Measure-Object -Maximum).Maximum
  $detail = ('win=' + $mz.L + ',' + $mz.T + ' ' + ($mz.Rt-$mz.L) + 'x' + ($mz.B-$mz.T) +
             '  work=' + $w.L + ',' + $w.T + ' ' + ($w.Rt-$w.L) + 'x' + ($w.B-$w.T) +
             '  overhang=' + $ov + '  gap=' + $gp)
  if ($ov -le 1) { Write-Host ('OK  maximised window does not overhang the work area  ' + $detail) }
  else { Write-Host ('FAIL: maximised window overhangs by ' + $ov + ' px (UI is off-screen)  ' + $detail); $fail = 1 }
  if ($gp -le 1) { Write-Host ('OK  maximised window covers the work area  ' + $detail) }
  else { Write-Host ('FAIL: maximised window leaves a ' + $gp + ' px gap of desktop  ' + $detail); $fail = 1 }
}

try { Stop-Process -Id $p.Id -Force } catch { }
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
