# verify-WPF-04: phase 2 -- the queue list (rows, columns, header, scroll, empty)
# ASCII only. Every Dbg call has a receive timeout so a stuck app cannot hang the check.
# Each assertion is flushed to _run04.log immediately (a killed run still leaves evidence).
$dir = $PSScriptRoot
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$shot = Join-Path $dir '_shot-02-queue.png'
$runLog = Join-Path $dir '_run04.log'
$port = 7796
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

# --- empty state: no rows, no hint text, list exists ---
$r = Dbg 'rows' | ConvertFrom-Json
Check 'starts empty' ($r.count -eq 0) ('count=' + $r.count)
$f = Dbg 'find:list' | ConvertFrom-Json
Check 'list exists' ($f.found -eq $true) ('type=' + $f.type)
foreach ($hid in @('colHead0','colHead1','colHead2','colHead3','colHead4','colHead5')) {
  $h = Dbg "text:$hid" | ConvertFrom-Json
  Check ("header cell " + $hid) ($h.text.Length -gt 0) ('raw=' + $h.text)
}

# --- 60 rows, statuses rotate, numeric index ---
$r = Dbg 'addrows:60' | ConvertFrom-Json
Check 'addrows:60 ok' ($r.ok -eq $true) ('count=' + $r.count)
$r = Dbg 'rows' | ConvertFrom-Json
Check 'rows reports 60' ($r.count -eq 60) ('count=' + $r.count)
Check 'row 1 index is 1' ($r.rows[0].i -eq '1') ('i=' + $r.rows[0].i)
Check 'row 60 index is 60' ($r.rows[59].i -eq '60') ('i=' + $r.rows[59].i)
$states = @{}
foreach ($row in $r.rows) { $states[$row.status] = 1 }
Check 'several distinct statuses rendered' ($states.Keys.Count -ge 4) ('distinct=' + $states.Keys.Count)

# --- screenshot for the eye ---
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class S4 {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, Rt, B; }
}
"@
[S4]::SetProcessDPIAware() | Out-Null
$uia = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'win')
$win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $uia)
if ($win -ne $null) {
  $h = [IntPtr]$win.Current.NativeWindowHandle
  $rc = New-Object S4+RECT
  [S4]::GetWindowRect($h, [ref]$rc) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap ($rc.Rt - $rc.L), ($rc.B - $rc.T)   # DPI-aware window: capture at physical size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc(); [S4]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  Check 'screenshot saved' (Test-Path $shot) $shot
}

# --- clear ---
$r = Dbg 'clear' | ConvertFrom-Json
Check 'clear ok' ($r.ok -eq $true) ('ok=' + $r.ok)
$r = Dbg 'rows' | ConvertFrom-Json
Check 'empty after clear' ($r.count -eq 0) ('count=' + $r.count)

try { Stop-Process -Id $p.Id -Force } catch { }
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
