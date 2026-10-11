# verify-WPF-05: phase 3 -- the terminal panel (append, wrap, collapse, copy menu)
# ASCII only. Run it with:  powershell -File <this>       (never pipe its stdout --
# the app inherits the handle and keeps the pipe open, so the pipeline hangs)
$here = $PSScriptRoot                  # this script lives in the tests folder
$dir  = Split-Path $here -Parent       # the app folder: the exe and its log/settings live there
$src  = Join-Path $dir 'src'   # source code folder
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$shot = Join-Path $here '_shot-03-log.png'
$runLog = Join-Path $here '_run05.log'
$port = 7795
$fail = 0
Remove-Item $runLog -ErrorAction SilentlyContinue

function Dbg([string]$cmd) {
  try {
    $c = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $port)
    $c.ReceiveTimeout = 3000
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

Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 500
$p = Start-Process -FilePath $exe -ArgumentList @('--wpf', "--debug-port=$port") -PassThru
Start-Sleep -Seconds 5
$p.Refresh()
Check 'app alive' (-not $p.HasExited) ('pid=' + $p.Id)
Check 'logBox exists' ((Dbg 'find:logBox' | ConvertFrom-Json).found -eq $true) ''

$r = Dbg 'loglines' | ConvertFrom-Json
Check 'starts with the startup log (Log.OnLine wired)' ($r.shown -ge 3) ('shown=' + $r.shown)
Check 'not collapsed at start' ($r.collapsed -eq $false) ('collapsed=' + $r.collapsed)

# --- one very long line must wrap into several shown lines ---
$before5 = $r.shown
Dbg 'addlog:1' | Out-Null
$r = Dbg 'loglines:50' | ConvertFrom-Json
Check 'one long line wraps into several shown lines' ($r.shown -ge $before5 + 2) ('shown=' + $r.shown + ' before=' + $before5)

# --- 50 lines ---
$before = (Dbg 'loglines' | ConvertFrom-Json).shown
Dbg 'addlog:50' | Out-Null
$after = (Dbg 'loglines' | ConvertFrom-Json).shown
Check 'addlog:50 grows the panel' ($after -gt $before + 20) ('before=' + $before + ' after=' + $after)
$tail = Dbg 'loglines:12' | ConvertFrom-Json
$joined = ($tail.lines -join ' ')
Check 'newest log line is present' ($joined -match 'test log line 50') ('tail=' + $tail.lines[$tail.lines.Count - 1].Substring(0, [Math]::Min(40, $tail.lines[$tail.lines.Count - 1].Length)))

# --- collapse / expand ---
$r = Dbg 'press:logHeader' | ConvertFrom-Json
Start-Sleep -Milliseconds 400
$s = Dbg 'loglines:2' | ConvertFrom-Json
Check 'collapsed after click' ($s.collapsed -eq $true) ('collapsed=' + $s.collapsed)
$t = Dbg 'text:logHeaderArrow' | ConvertFrom-Json
Check 'header arrow switched' ($t.text.IndexOf([char]0x25B6) -ge 0) ('raw=' + $t.text)
$t = Dbg 'text:logHeaderTail' | ConvertFrom-Json
Check 'header shows the last line' ($t.text -match 'test log line 50') ('raw=' + $t.text)
$r = Dbg 'press:logHeader' | ConvertFrom-Json
Start-Sleep -Milliseconds 400
$s = Dbg 'loglines:2' | ConvertFrom-Json
Check 'expanded again' ($s.collapsed -eq $false) ('collapsed=' + $s.collapsed)

# --- screenshot ---
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class S5 {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, Rt, B; }
}
"@
[S5]::SetProcessDPIAware() | Out-Null
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'win')
$win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if ($win -ne $null) {
  $h = [IntPtr]$win.Current.NativeWindowHandle
  $rc = New-Object S5+RECT
  [S5]::GetWindowRect($h, [ref]$rc) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap ($rc.Rt - $rc.L), ($rc.B - $rc.T)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc(); [S5]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  Check 'screenshot saved' (Test-Path $shot) $shot
} else { Check 'window found for screenshot' $false 'no win' }

try { Stop-Process -Id $p.Id -Force } catch { }
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
