# verify-WPF-11: opening a rep that already has a video next to it must ask
#   "record it again?" and, on No, mark the row as already-recorded and skip it.

#   works, because WPF's MessageBox IS a native #32770 dialog; only the window lookup changed).
# Buttons are answered with WM_COMMAND/IDNO (7) rather than by button text, so the test does
#   not depend on the OS display language.
#
# NOTE: the debug port is unusable while the prompt is up. Read commands go through
#   Dispatcher.Invoke (Ui.OnUi) and a modal MessageBox owns the UI thread, so a query just
#   hangs until the box is answered. The dialog is therefore waited for with plain Win32.
# ASCII only. Run with: powershell -File <this>   (never pipe its stdout)
$dir = $PSScriptRoot
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$log = Join-Path $dir 'RepRecorder.log'
$cfg = Join-Path $dir 'RepRecorder.settings.json'
$bak = Join-Path $dir '_settings-backup11.json'
$runLog = Join-Path $dir '_run11.log'
$port = 7811
$fail = 0
Remove-Item $runLog -ErrorAction SilentlyContinue

# ASCII-only path: the game folder has brackets and spaces in it
$repDir = Join-Path $env:TEMP 'rr-ask-wpf'
if (Test-Path $repDir) { Remove-Item $repDir -Recurse -Force }
New-Item -ItemType Directory -Path $repDir | Out-Null
$repFile = Join-Path $repDir 'ask1.rep'
[System.IO.File]::WriteAllBytes($repFile, (New-Object byte[] 4096))
[System.IO.File]::WriteAllBytes((Join-Path $repDir 'ask1.mp4'), (New-Object byte[] 4096))

Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class W11 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static List<IntPtr> Dialogs(uint want) {
    List<IntPtr> found = new List<IntPtr>();
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid != want) return true;
      StringBuilder sb = new StringBuilder(64); GetClassName(h, sb, 64);
      if (sb.ToString() == "#32770" && IsWindowVisible(h)) found.Add(h);
      return true;
    }, IntPtr.Zero);
    return found;
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
# Chinese literals spelled as code points so this file stays pure ASCII
$MSG_ASKED = [string]([char]0x6B63 + [char]0x5728 + [char]0x8BE2 + [char]0x95EE)
$MSG_SKIP  = [string]([char]0x4F60 + [char]0x9009 + [char]0x62E9 + [char]0x8DF3 + [char]0x8FC7)
$ST_DONE   = [string]([char]0x5DF2 + [char]0x5F55 + [char]0x5236)

Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 700
Copy-Item $cfg $bak -Force
if (Test-Path $log) { Remove-Item $log -Force }

Start-Sleep -Seconds 3
$p = Start-Process -FilePath $exe -ArgumentList @("--debug-port=$port", $repFile) -PassThru

Add-Content -LiteralPath $runLog -Value 'step: waiting for the #32770 prompt' -Encoding UTF8
$dlg = @()
for ($i = 0; $i -lt 40; $i++) {
  $p.Refresh(); if ($p.HasExited) { break }
  $dlg = [W11]::Dialogs([uint32]$p.Id)
  if ($dlg.Count -gt 0) { break }
  Start-Sleep -Milliseconds 750
}
Check 'app alive' (-not $p.HasExited) ('pid=' + $p.Id)
Check 'the "record again?" dialog opened' ($dlg.Count -gt 0) ('dialogs=' + $dlg.Count + ' waited=' + $i)
Check 'the prompt is in the log' ((LogTxt).Contains($MSG_ASKED)) ''
if ($dlg.Count -eq 0) {
  try { Stop-Process -Id $p.Id -Force } catch { }
  Copy-Item $bak $cfg -Force; Remove-Item $bak -Force
  Remove-Item $repDir -Recurse -Force -ErrorAction SilentlyContinue
  Write-Host 'FAILED'; exit 1
}

Add-Content -LiteralPath $runLog -Value 'step: answering No (IDNO)' -Encoding UTF8
[W11]::SendMessage($dlg[0], 0x0111, [IntPtr]7, [IntPtr]::Zero) | Out-Null
Start-Sleep -Milliseconds 1500

# the UI thread is free again -- the debug port answers from here on
$up = $false
for ($i = 0; $i -lt 20; $i++) {
  if ((Dbg 'status') -ne $null) { $up = $true; break }
  Start-Sleep -Milliseconds 500
}
Check 'the debug port answers again after the prompt' ($up) ('waited=' + $i)

$dlg2 = [W11]::Dialogs([uint32]$p.Id)
Check 'the dialog closed after No' ($dlg2.Count -eq 0) ('dialogs=' + $dlg2.Count)
Check 'the skip is logged' ((LogTxt).Contains($MSG_SKIP)) ''

$r = Dbg 'rows' | ConvertFrom-Json
Check 'the row stayed in the queue' ($r.count -eq 1) ('count=' + $r.count)
if ($r.count -ge 1) {
  Check 'the row is now marked as already-recorded' ($r.rows[0].status -eq $ST_DONE) ('status=' + $r.rows[0].status)
}
Check 'the app is alive after answering' (-not $p.HasExited) ('pid=' + $p.Id)
Check 'no exception in log' ((LogTxt) -notmatch 'Exception') ''

try { Stop-Process -Id $p.Id -Force } catch { }
Start-Sleep -Milliseconds 500
Remove-Item $repDir -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $bak $cfg -Force
Remove-Item $bak -Force
Write-Host 'settings restored'
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
