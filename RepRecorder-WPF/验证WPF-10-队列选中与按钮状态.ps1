# verify-WPF-10: the two wires the port had dropped, plus the empty-queue guards.

#   2. clearing the blue selection must NOT reset that count
#   3. the wheel must not change the count combo (old: cbParallel.MouseWheel -> Handled)
#   4. empty queue: start enabled / stop disabled, and pressing start just logs

#   they read the selection with LVM_GETSELECTEDCOUNT(0x1032), which WPF has no equivalent for --
#   the debug port's selcount / select / selectrange / clearselect are the replacements.
# ASCII only. Run with: powershell -File <this>   (never pipe its stdout)
$dir = $PSScriptRoot
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$log = Join-Path $dir 'RepRecorder.log'
$cfg = Join-Path $dir 'RepRecorder.settings.json'
$bak = Join-Path $dir '_settings-backup10.json'
$runLog = Join-Path $dir '_run10.log'
$port = 7810
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
function LogTxt { if (Test-Path $log) { return (Get-Content $log -Raw -Encoding UTF8) } return '' }
function GetParallel { return [int](Dbg 'text:cbParallel' | ConvertFrom-Json).text }
# Chinese literals spelled as code points -- this file stays pure ASCII (see the notes on
# PowerShell 5.1 reading BOM-less UTF-8 as ANSI, which eats the closing quote).
$MSG_EMPTY = [string]([char]0x961F + [char]0x5217 + [char]0x662F + [char]0x7A7A + [char]0x7684)                 
$MSG_COUNT = [string]([char]0x540C + [char]0x65F6 + [char]0x5F55 + [char]0x5236 + [char]0x6570 + [char]0x91CF + [char]0x6539 + [char]0x4E3A)  

Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 700
Copy-Item $cfg $bak -Force
if (Test-Path $log) { Remove-Item $log -Force }

Start-Sleep -Seconds 3          # let Defender scan the fresh exe
$p = Start-Process -FilePath $exe -ArgumentList @("--debug-port=$port") -PassThru
$up = $false
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Milliseconds 1000
  if ((Dbg 'status') -ne $null) { $up = $true; break }
  $p.Refresh(); if ($p.HasExited) { break }
}
Check 'app alive' ($up -and -not $p.HasExited) ('pid=' + $p.Id + ' waited=' + $i + 's')
if (-not $up) { Write-Host 'FAILED'; exit 1 }

# ============================================================ empty queue
Add-Content -LiteralPath $runLog -Value 'step: empty queue guards' -Encoding UTF8
$s = Dbg 'status' | ConvertFrom-Json
Check 'starts with an empty queue' ($s.queue -eq 0) ('queue=' + $s.queue)
Check 'stop is disabled while idle' ((Dbg 'enabled:btnStop' | ConvertFrom-Json).enabled -eq $false) ''
Check 'start is enabled while idle' ((Dbg 'enabled:btnStart' | ConvertFrom-Json).enabled -eq $true) ''
Check 'status.recording is false while idle' ($s.recording -eq $false) ('recording=' + $s.recording)

# With round-record ON, an empty-queue start takes a different log branch
# ('round record: nothing left to record'). We want the plain empty-queue branch,
# so switch round-record off first -- settings get restored from the backup at the end.
if ((Dbg 'text:chkRound' | ConvertFrom-Json).text -eq 'true') {
  Dbg 'press:chkRound' | Out-Null
  Start-Sleep -Milliseconds 500
  Add-Content -LiteralPath $runLog -Value 'step: round-record turned off for the empty-queue check' -Encoding UTF8
}
Dbg 'press:btnStart' | Out-Null
Start-Sleep -Milliseconds 1200
$after = Dbg 'status' | ConvertFrom-Json
Check 'pressing start on an empty queue says so' ((LogTxt).Contains($MSG_EMPTY)) ('loglen=' + (LogTxt).Length)
Check 'empty-queue start does not enter recording' ($after.recording -eq $false) ('recording=' + $after.recording)
Check 'empty-queue start leaves the window alive' (-not $p.HasExited) ('pid=' + $p.Id)

# ============================================================ selection drives the count
Add-Content -LiteralPath $runLog -Value 'step: selection -> ??????' -Encoding UTF8
$r = Dbg 'addrows:6' | ConvertFrom-Json
Check 'six rows added' ($r.count -eq 6) ('count=' + $r.count)
Check 'nothing selected yet' ((Dbg 'selcount' | ConvertFrom-Json).sel -eq 0) ''
$base = GetParallel
Add-Content -LiteralPath $runLog -Value ('step: parallel baseline=' + $base) -Encoding UTF8

$r = Dbg 'selectrange:2-6' | ConvertFrom-Json
Check 'selectrange accepted' ($r.ok -eq $true) ''
Start-Sleep -Milliseconds 500
$sc = Dbg 'selcount' | ConvertFrom-Json
Check 'five rows are selected' ($sc.sel -eq 5) ('sel=' + $sc.sel)
$now = GetParallel
Check 'parallel count follows the selection' ($now -eq 5) ('combo=' + $now + ' before=' + $base)
Check 'the count change is logged' ((LogTxt).Contains($MSG_COUNT)) ('loglen=' + (LogTxt).Length)


$r = Dbg 'clearselect' | ConvertFrom-Json
Start-Sleep -Milliseconds 500
$sc = Dbg 'selcount' | ConvertFrom-Json
Check 'clearselect empties the blue selection' ($sc.sel -eq 0) ('sel=' + $sc.sel)
Check 'clearing selection KEEPS the parallel count' ((GetParallel) -eq 5) ('combo=' + (GetParallel))

# ---- single row ----
Dbg 'select:1' | Out-Null
Start-Sleep -Milliseconds 500
Check 'single-row select gives sel=1' ((Dbg 'selcount' | ConvertFrom-Json).sel -eq 1) ''
Check 'parallel count follows to 1' ((GetParallel) -eq 1) ('combo=' + (GetParallel))

# ---- select:-1 is the same as clearselect ----
Dbg 'select:-1' | Out-Null
Start-Sleep -Milliseconds 400
Check 'select:-1 clears' ((Dbg 'selcount' | ConvertFrom-Json).sel -eq 0) ''

# ---- bad index must not crash ----
$r = Dbg 'select:999' | ConvertFrom-Json
Check 'out-of-range select is still ok' ($r.ok -eq $true) ('sel=' + (Dbg 'selcount' | ConvertFrom-Json).sel)
Check 'app survived the whole selection pass' (-not $p.HasExited) ('pid=' + $p.Id)
Check 'no exception in log' ((LogTxt) -notmatch 'Exception') ''

try { Stop-Process -Id $p.Id -Force } catch { }
Start-Sleep -Milliseconds 500
Copy-Item $bak $cfg -Force
Remove-Item $bak -Force
Write-Host 'settings restored'
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
