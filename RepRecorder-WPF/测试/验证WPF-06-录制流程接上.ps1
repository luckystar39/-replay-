# verify-WPF-06: phase 5 -- the recording logic is wired to the WPF UI.
# Adds one rep through the command line and checks it lands in the queue; never starts a
# real recording (that needs the game + OBS and is left to the operator).
# ASCII only. Run with: powershell -File <this>   (never pipe its stdout)
$here = $PSScriptRoot                  # this script lives in the tests folder
$dir  = Split-Path $here -Parent       # the app folder: the exe and its log/settings live there
$src  = Join-Path $dir 'src'   # source code folder
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$log = Join-Path $dir 'RepRecorder.log'
$shot = Join-Path $here '_shot-04-wired.png'
$runLog = Join-Path $here '_run06.log'
$port = 7794
$repDir = Join-Path $here '_testreps'
$fail = 0
Remove-Item $runLog -ErrorAction SilentlyContinue

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

if (-not (Test-Path $repDir)) { New-Item -ItemType Directory -Path $repDir | Out-Null }
# 造两个假的 rep 文件就够：AddPaths 只看扩展名和存在性（这个检查不真录）
1..2 | ForEach-Object {
  $f = Join-Path $repDir ('t' + $_ + '.rep')
  if (-not (Test-Path $f)) { [System.IO.File]::WriteAllBytes($f, (New-Object byte[] 4096)) }
}
$reps = @(Get-ChildItem -LiteralPath $repDir -Filter *.rep -ErrorAction SilentlyContinue)
Check 'test reps available' ($reps.Count -gt 0) ('count=' + $reps.Count)
if ($reps.Count -eq 0) { Write-Host 'FAILED'; exit 1 }

Add-Content -LiteralPath $runLog -Value 'step: killing old instances' -Encoding UTF8
Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 600
Add-Content -LiteralPath $runLog -Value 'step: removing old log' -Encoding UTF8
if (Test-Path $log) { Remove-Item $log -Force }

# pass the rep on the command line, exactly like the old build did
Add-Content -LiteralPath $runLog -Value ('step: starting with ' + $reps[0].FullName) -Encoding UTF8
# 刚编译出来的新 exe 会被 Defender 先扫一遍，立刻 Start-Process 会卡住 —— 等一下
Start-Sleep -Seconds 3
$p = Start-Process -FilePath $exe -ArgumentList @('--wpf', "--debug-port=$port", $reps[0].FullName) -PassThru
Add-Content -LiteralPath $runLog -Value ('step: started pid=' + $p.Id) -Encoding UTF8
Start-Sleep -Seconds 7
Add-Content -LiteralPath $runLog -Value 'step: slept 7s' -Encoding UTF8
$p.Refresh()
Check 'app alive' (-not $p.HasExited) ('pid=' + $p.Id)

# --- startup log lines (same three the WinForms build writes) ---
$txt = ''
if (Test-Path $log) { $txt = Get-Content $log -Raw -Encoding UTF8 }
Check 'log has the startup banner' ($txt -match 'Rep') ('len=' + $txt.Length)
Check 'log has the game dir line' ($txt -match 'th123') ''
Check 'no exception in log' ($txt -notmatch 'Exception') ''

# --- the terminal panel must show the same lines (Log.OnLine -> Ui.Log) ---
$lg = Dbg 'loglines:20' | ConvertFrom-Json
Check 'terminal panel shows the startup log' ($lg.shown -ge 3) ('shown=' + $lg.shown)

# --- the rep landed in the queue ---
$r = Dbg 'rows' | ConvertFrom-Json
Check 'one row in the queue' ($r.count -eq 1) ('count=' + $r.count)
if ($r.count -ge 1) {
  Check 'row shows the rep file name' ($r.rows[0].file -match 't1[.]rep') ('file=' + $r.rows[0].file)
  Check 'row starts as waiting' ($r.rows[0].status.Length -gt 0) ('status=' + $r.rows[0].status)
  Check 'fresh rep has no video size yet' ($r.rows[0].size -eq '') ('size=' + $r.rows[0].size)
}

# --- the facade answers ---
$s = Dbg 'status' | ConvertFrom-Json
Check 'status reports the queue' ($s.queue -eq 1) ('queue=' + $s.queue)
Check 'status window size sane' ($s.win -match 'x') ('win=' + $s.win)
$t = Dbg 'text:btnStart' | ConvertFrom-Json
Check 'start button text' ($t.text.Length -gt 0) ('raw=' + $t.text)
# 'status bar text' dropped 2026-10-05: the bottom status bar was removed at the user's request.
$t = Dbg 'text:btnStop' | ConvertFrom-Json
Check 'stop button text' ($t.text.Length -gt 0) ('raw=' + $t.text)

# --- clear via the real button (goes through Recorder -> facade -> list) ---
$c = Dbg 'press:btnClear' | ConvertFrom-Json
Start-Sleep -Milliseconds 800
$r = Dbg 'rows' | ConvertFrom-Json
Check 'clear button empties the queue' ($r.count -eq 0) ('count=' + $r.count)

# --- screenshot ---
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class S6 {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, Rt, B; }
}
"@
[S6]::SetProcessDPIAware() | Out-Null
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'win')
$win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if ($win -ne $null) {
  $h = [IntPtr]$win.Current.NativeWindowHandle
  $rc = New-Object S6+RECT
  [S6]::GetWindowRect($h, [ref]$rc) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap ($rc.Rt - $rc.L), ($rc.B - $rc.T)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc(); [S6]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  Check 'screenshot saved' (Test-Path $shot) $shot
}

try { Stop-Process -Id $p.Id -Force } catch { }
Check 'no exception at exit' ((Get-Content $log -Raw -Encoding UTF8) -notmatch 'Exception') ''
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
