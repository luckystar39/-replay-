# verify-WPF-03: phase 1 -- the left control panel (cards, controls, collapse, width memory)
# ASCII ONLY. No Chinese literals anywhere: a BOM-less UTF-8 script is read as ANSI by
# PowerShell 5.1 and a trailing Chinese char eats the closing quote of the string.
# Chinese text is asserted with \uXXXX regex escapes instead.
# Text dumps go to _dump-utf8.json so a human can read the real characters.
$dir = $PSScriptRoot
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$cfg = Join-Path $dir 'RepRecorder.settings.json'
$bak = Join-Path $dir '_settings-backup.json'
$dump = Join-Path $dir '_dump-utf8.json'
$port = 7798
$fail = 0
$report = @()

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
  if ($ok) { Write-Host ("OK  {0}  {1}" -f $what, $detail) }
  else { Write-Host ("FAIL {0}  {1}" -f $what, $detail); $script:fail = 1 }
  $script:report += [pscustomobject]@{ what = $what; ok = $ok; detail = $detail }
  Add-Content -LiteralPath (Join-Path $dir '_run03.log') -Value (($(if ($ok) {'OK  '} else {'FAIL'})) + $what + '  ' + $detail) -Encoding UTF8
}
function CfgVal([string]$key) {
  try { return (Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json).$key } catch { return $null }
}
$CJK = '[\u4e00-\u9fff]'

Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 500
Copy-Item $cfg $bak -Force
$expectParallel = 6
try { $expectParallel = (Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json).ParallelCount } catch { }
Write-Host ('settings ParallelCount before test = ' + $expectParallel)

$p = Start-Process -FilePath $exe -ArgumentList @('--wpf', "--debug-port=$port") -PassThru
Start-Sleep -Seconds 5
$p.Refresh()
Check 'app alive' (-not $p.HasExited) ('pid=' + $p.Id)

$ids = @('panelLeft','splitterV','btnPanel','btnBg','cardRecord','cardParallel','cardQueue','cardSound','cardEnv',
         'btnStart','btnStop','btnClear','btnRemove','btnMark','btnRefresh','btnPrepare','btnCheck','btnSet','btnOpenLog',
         # chkAuto / lblParHint removed 2026-10-05 at the user's request
         # (no more "drag in then auto start"; no more "(= the N you selected)" line).
         'btnSound','btnGameAudio','btnBgm','cbParallel','cbMic','chkPrepare','chkCloseObs','chkRound',
         'lblMemHint','hint','activityBar','btnExpand')
$missing = @()
foreach ($id in $ids) {
  $r = Dbg "find:$id" | ConvertFrom-Json
  if ($r.found -ne $true) { $missing += $id }
}
Check 'all left-panel controls present' ($missing.Count -eq 0) ('missing=' + ($missing -join ','))

# --- texts (assert with escapes; keep the real text for the human) ---
$tStart = Dbg 'text:btnStart' | ConvertFrom-Json
Check 'btnStart has a playlist glyph + CJK' (($tStart.text.IndexOf([char]0x25B6) -ge 0) -and ($tStart.text -match $CJK)) ('raw=' + $tStart.text)
$tMem = Dbg 'text:lblMemHint' | ConvertFrom-Json
Check 'mem hint looks like "<n.n> GB"' ($tMem.text -match '\d\.\d GB') ('raw=' + $tMem.text)
$tPar = Dbg 'text:cbParallel' | ConvertFrom-Json
Check 'cbParallel shows the saved value' ($tPar.text -eq "$expectParallel") ('raw=' + $tPar.text + ' expect=' + $expectParallel)
$tMic = Dbg 'text:cbMic' | ConvertFrom-Json
Check 'cbMic has a selection' ($tMic.text.Length -gt 0) ('raw=' + $tMic.text)

# --- parallel count -> settings ---
$r = Dbg 'set:cbParallel=2' | ConvertFrom-Json
Check 'set cbParallel=2' ($r.ok -eq $true) ('ok=' + $r.ok)
Start-Sleep -Milliseconds 400
Check 'settings ParallelCount==3' ((CfgVal 'ParallelCount') -eq 3) ('file=' + (CfgVal 'ParallelCount'))
$tMem = Dbg 'text:lblMemHint' | ConvertFrom-Json
Check 'mem hint follows (1.6 GB)' ($tMem.text -match '1\.6') ('raw=' + $tMem.text)

# --- collapse / expand ---
Dbg 'set:cbParallel=5' | Out-Null
Start-Sleep -Milliseconds 300
$s = Dbg 'status' | ConvertFrom-Json
Check 'panel width > 120' ($s.panel -gt 120) ('panel=' + $s.panel)
Check 'not collapsed' ($s.collapsed -eq $false) ('collapsed=' + $s.collapsed)

$r = Dbg 'press:btnPanel' | ConvertFrom-Json
Start-Sleep -Milliseconds 700
$s = Dbg 'status' | ConvertFrom-Json
Check 'collapsed after press' ($s.collapsed -eq $true) ('collapsed=' + $s.collapsed)
Check 'panel width 0 when collapsed' ($s.panel -eq 0) ('panel=' + $s.panel)
Check 'settings PanelCollapsed==true' ((CfgVal 'PanelCollapsed') -eq $true) ('file=' + (CfgVal 'PanelCollapsed'))
$tBtn = Dbg 'text:btnPanel' | ConvertFrom-Json
Check 'top button shows the expand arrow (U+25B6)' ($tBtn.text.IndexOf([char]0x25B6) -ge 0) ('raw=' + $tBtn.text)

$r = Dbg 'press:btnExpand' | ConvertFrom-Json
Start-Sleep -Milliseconds 700
$s = Dbg 'status' | ConvertFrom-Json
Check 'expanded again' ($s.collapsed -eq $false) ('collapsed=' + $s.collapsed)
Check 'panel width restored' ($s.panel -gt 120) ('panel=' + $s.panel)
$tBtn = Dbg 'text:btnPanel' | ConvertFrom-Json
Check 'top button back to the collapse arrow (U+25C0)' ($tBtn.text.IndexOf([char]0x25C0) -ge 0) ('raw=' + $tBtn.text)

Check 'no crash in log' ((Get-Content (Join-Path $dir 'RepRecorder.log') -Raw -Encoding UTF8) -notmatch 'Exception') ''

try { Stop-Process -Id $p.Id -Force } catch { }
Start-Sleep -Milliseconds 500
Copy-Item $bak $cfg -Force
Remove-Item $bak -Force
Write-Host 'settings restored'
[System.IO.File]::WriteAllText($dump, ($report | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('dump -> ' + $dump)

if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
