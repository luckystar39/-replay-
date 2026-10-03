
#   system-mute helper reads back what it wrote.

#   reflection; the debug port now exposes it directly as `sysmute` / `sysmute:on|off`.
#
# WARNING: this really does mute the machine's default output device for a moment -- the
#   original setting is read first and always put back, including on the failure paths.
# ASCII only. Run with: powershell -File <this>   (never pipe its stdout)
$dir = $PSScriptRoot
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$log = Join-Path $dir 'RepRecorder.log'
$cfg = Join-Path $dir 'RepRecorder.settings.json'
$bak = Join-Path $dir '_settings-backup13.json'
$runLog = Join-Path $dir '_run13.log'
$port = 7813
$fail = 0
Remove-Item $runLog -ErrorAction SilentlyContinue

$repDir = Join-Path $env:TEMP 'rr-refresh-wpf'
if (Test-Path $repDir) { Remove-Item $repDir -Recurse -Force }
New-Item -ItemType Directory -Path $repDir | Out-Null
foreach ($n in @('r1', 'r2')) {
  [System.IO.File]::WriteAllBytes((Join-Path $repDir ($n + '.rep')), (New-Object byte[] 4096))
}

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
$MSG_REFRESH = [string]([char]0x5DF2 + [char]0x5237 + [char]0x65B0 + [char]0x961F + [char]0x5217)
$ST_DONE     = [string]([char]0x5DF2 + [char]0x5F55 + [char]0x5236)

$muteBefore = $null
Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 700
Copy-Item $cfg $bak -Force
if (Test-Path $log) { Remove-Item $log -Force }

Start-Sleep -Seconds 3
$p = Start-Process -FilePath $exe -ArgumentList @("--debug-port=$port", $repDir) -PassThru
$up = $false
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Milliseconds 1000
  if ((Dbg 'status') -ne $null) { $up = $true; break }
  $p.Refresh(); if ($p.HasExited) { break }
}
Check 'app alive' ($up -and -not $p.HasExited) ('pid=' + $p.Id + ' waited=' + $i + 's')
if (-not $up) {
  Copy-Item $bak $cfg -Force; Remove-Item $bak -Force
  Remove-Item $repDir -Recurse -Force -ErrorAction SilentlyContinue
  Write-Host 'FAILED'; exit 1
}

# ---------- the system-mute helper round-trips ----------
Add-Content -LiteralPath $runLog -Value 'step: sysmute round-trip' -Encoding UTF8
$m = Dbg 'sysmute' | ConvertFrom-Json
Check 'sysmute reads the current state' ($m.muted -ne $null) ('muted=' + $m.muted)
$muteBefore = $m.muted

$r = Dbg 'sysmute:on' | ConvertFrom-Json
Check 'sysmute:on reports success' ($r.ok -eq $true) ('ok=' + $r.ok)
Check 'the endpoint is now muted' ($r.muted -eq $true) ('muted=' + $r.muted)
$r = Dbg 'sysmute:off' | ConvertFrom-Json
Check 'sysmute:off reports success' ($r.ok -eq $true) ('ok=' + $r.ok)
Check 'the endpoint is unmuted again' ($r.muted -eq $false) ('muted=' + $r.muted)
# put back whatever it was before we touched it
if ($muteBefore -eq $true) { Dbg 'sysmute:on' | Out-Null; Write-Host 'restored: muted' }
else { Dbg 'sysmute:off' | Out-Null; Write-Host 'restored: unmuted' }

# ---------- refresh detects a video that appeared after the queue was built ----------
Add-Content -LiteralPath $runLog -Value 'step: refresh' -Encoding UTF8
$r = Dbg 'rows' | ConvertFrom-Json
Check 'two reps queued' ($r.count -eq 2) ('count=' + $r.count)
Check 'neither is marked recorded yet' ($r.rows[0].status -ne $ST_DONE) ('status=' + $r.rows[0].status)

Dbg 'press:btnRefresh' | Out-Null
Start-Sleep -Milliseconds 1200
Check 'refresh is logged' ((LogTxt).Contains($MSG_REFRESH)) ''

# now drop a video next to r1 and refresh again
[System.IO.File]::WriteAllBytes((Join-Path $repDir 'r1.mp4'), (New-Object byte[] 8192))
Dbg 'press:btnRefresh' | Out-Null
Start-Sleep -Milliseconds 1500
$r = Dbg 'rows' | ConvertFrom-Json
$done = @($r.rows | Where-Object { $_.status -eq $ST_DONE })
Check 'refresh found the new video' ($done.Count -eq 1) ('recorded=' + $done.Count + ' of ' + $r.count)
if ($done.Count -eq 1) {
  Check 'it is the right row' ($done[0].file -match 'r1') ('file=' + $done[0].file)
}
Check 'refresh did not drop any row' ($r.count -eq 2) ('count=' + $r.count)
Check 'app alive after refresh' (-not $p.HasExited) ('pid=' + $p.Id)
Check 'no exception in log' ((LogTxt) -notmatch 'Exception') ''

try { Stop-Process -Id $p.Id -Force } catch { }
Start-Sleep -Milliseconds 500
Remove-Item $repDir -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $bak $cfg -Force
Remove-Item $bak -Force
Write-Host 'settings restored'
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
