# verify-WPF-02: acceptance for tasks 0.4 + 0.5
#   A) an old / partial settings file (no BgScope, no PanelCollapsed, no BgImage) still starts, no crash
#   B) the --debug-port protocol: status / find / press / set / log
# ASCII only. Paths come from $PSScriptRoot.
$here = $PSScriptRoot                  # this script lives in the tests folder
$dir  = Split-Path $here -Parent       # the app folder: the exe and its log/settings live there
$src  = Join-Path $dir 'src'   # source code folder
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$cfg = Join-Path $dir 'RepRecorder.settings.json'
$bak = Join-Path $here '_settings-backup.json'
$log = Join-Path $dir 'RepRecorder.log'
$port = 7799
$fail = 0

function Dbg([string]$cmd) {
  try {
    $c = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $port)
    $w = New-Object System.IO.StreamWriter($c.GetStream()); $w.AutoFlush = $true; $w.WriteLine($cmd)
    $r = New-Object System.IO.StreamReader($c.GetStream()); $out = $r.ReadLine(); $c.Close()
    return $out
  } catch { return $null }
}
function Check([string]$what, [bool]$ok, [string]$detail) {
  if ($ok) { Write-Host ("OK  {0}  {1}" -f $what, $detail) }
  else { Write-Host ("FAIL {0}  {1}" -f $what, $detail); $script:fail = 1 }
}

Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 500

# ---------- A) partial settings file ----------
Copy-Item $cfg $bak -Force
[System.IO.File]::WriteAllText($cfg, '{"ParallelCount":6}', (New-Object System.Text.UTF8Encoding($false)))
Write-Host 'wrote a minimal settings file: {"ParallelCount":6}'
if (Test-Path $log) { Remove-Item $log -Force }

$p = Start-Process -FilePath $exe -ArgumentList @('--wpf', "--debug-port=$port") -PassThru
Start-Sleep -Seconds 5
$p.Refresh()
Check 'app did not exit' (-not $p.HasExited) ("pid=" + $p.Id)

$txt = ''
if (Test-Path $log) { $txt = Get-Content $log -Raw -Encoding UTF8 }
Check 'no crash in log' ($txt -notmatch 'Exception') ''

# ---------- B) debug port ----------
$raw = Dbg 'status'
$st = $null
if ($raw -ne $null) { try { $st = $raw | ConvertFrom-Json } catch { } }
Check 'status answers' ($st -ne $null) ("raw=" + $raw)
if ($st -ne $null) {
  Check 'status.wpf' ($st.wpf -eq $true) ('wpf=' + $st.wpf)
  Check 'status.parallel' ($st.parallel -eq 6) ('parallel=' + $st.parallel)
  # status.statusText is empty now: the bottom status bar (and its TextBlock) was removed
  # on 2026-10-05. The field is still reported by the debug API, just with no widget behind it.
  Check 'status.statusText is a string' ($st.statusText -ne $null) ('len=' + $st.statusText.Length)
}

$f = Dbg 'find:btnPanel' | ConvertFrom-Json
Check 'find:btnPanel' ($f.found -eq $true) ('found=' + $f.found + ' type=' + $f.type)

$pr = Dbg 'press:btnPanel' | ConvertFrom-Json
Check 'press:btnPanel' ($pr.ok -eq $true) ('ok=' + $pr.ok)

$nf = Dbg 'press:nope' | ConvertFrom-Json
Check 'press:missing -> error' ($nf.error -ne $null) ('error=' + $nf.error)

$lg = Dbg 'log:10' | ConvertFrom-Json
$hasStartup = $false
foreach ($l in $lg.lines) { if ($l -match 'WPF') { $hasStartup = $true } }
Check 'log tail contains WPF line' $hasStartup ('total=' + $lg.total)

$hb = Dbg 'set:btnPanel=x' | ConvertFrom-Json
Check 'set: on a button -> error' ($hb.error -ne $null) ('error=' + $hb.error)

# ---------- cleanup ----------
try { Stop-Process -Id $p.Id -Force } catch { }
Start-Sleep -Milliseconds 500
Copy-Item $bak $cfg -Force
Remove-Item $bak -Force
Write-Host 'settings restored'

if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
