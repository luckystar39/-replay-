# verify-WPF-08: phase 7 -- the switchover. No argument must now open the WPF UI,
#   and --old must still bring back the old WinForms UI (the fallback).
# ASCII only. Run with: powershell -File <this>   (never pipe its stdout)
$dir = $PSScriptRoot
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$log = Join-Path $dir 'RepRecorder.log'
$cfg = Join-Path $dir 'RepRecorder.settings.json'
$bak = Join-Path $dir '_settings-backup08.json'
$runLog = Join-Path $dir '_run08.log'
$port = 7801
$fail = 0
Remove-Item $runLog -ErrorAction SilentlyContinue

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

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
# the top-level window owned by a given pid, via UIA
# Skip shell helper windows: Windows creates a 'SysShadow' top-level window per process
# for window drop shadows, and FindFirst sometimes returns THAT instead of the real
# window -- which made the WinForms check below fail intermittently (2026-10-04).
function WinOf([int]$pid_) {
  try {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
              [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pid_)
    $all = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
             [System.Windows.Automation.TreeScope]::Children, $cond)
    foreach ($e in $all) {
      $cls = ''
      try { $cls = $e.Current.ClassName } catch { }
      if ($cls -ne 'SysShadow') { return $e }
    }
    return $null
  } catch { return $null }
}
function KillAll {
  Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
  Start-Sleep -Milliseconds 700
}

KillAll
Copy-Item $cfg $bak -Force

# ============================================================ A: no argument -> WPF
Add-Content -LiteralPath $runLog -Value 'step: A start with no --wpf / no --old' -Encoding UTF8
if (Test-Path $log) { Remove-Item $log -Force }
Start-Sleep -Seconds 3          # let Defender scan the fresh exe
$p = Start-Process -FilePath $exe -ArgumentList @("--debug-port=$port") -PassThru
$up = $false
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Milliseconds 1000
  if ((Dbg 'status') -ne $null) { $up = $true; break }
  $p.Refresh(); if ($p.HasExited) { break }
}
Check 'A: no-arg launch is alive' ($up -and -not $p.HasExited) ('pid=' + $p.Id + ' waited=' + $i + 's')
$s = Dbg 'status' | ConvertFrom-Json
Check 'A: debug port answers (so it is the WPF shell)' ($s -ne $null) ''
$w = WinOf $p.Id
# UIA reports WPF top-level windows as class "Window"; the point is that it is NOT the
# WinForms host class -- that is what tells the two UIs apart.
Check 'A: window is not the WinForms host' ($w -ne $null -and $w.Current.ClassName -notlike 'WindowsForms10*') `
      ('class=' + $(if ($w) { $w.Current.ClassName } else { '<none>' }))
$cond = New-Object System.Windows.Automation.PropertyCondition(
          [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'win')
$wp = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants, $cond)
Check 'A: WPF shell exposes AutomationId win' ($wp -ne $null) ''
$txt = ''
if (Test-Path $log) { $txt = Get-Content $log -Raw -Encoding UTF8 }
Check 'A: log says the WPF UI is in use' ($txt -match 'WPF') ('len=' + $txt.Length)
Check 'A: no exception in log' ($txt -notmatch 'Exception') ''
try { Stop-Process -Id $p.Id -Force } catch { }
Start-Sleep -Milliseconds 700
Check 'A: no debug port left behind (clean exit)' ((Dbg 'status') -eq $null) ''

# ============================================================ B: --old -> WinForms
KillAll
Add-Content -LiteralPath $runLog -Value 'step: B start with --old' -Encoding UTF8
Start-Sleep -Seconds 2
$p2 = Start-Process -FilePath $exe -ArgumentList @('--old') -PassThru
Start-Sleep -Seconds 6
$p2.Refresh()
Check 'B: --old launch is alive' (-not $p2.HasExited) ('pid=' + $p2.Id)
$w2 = WinOf $p2.Id
$cls = $(if ($w2) { $w2.Current.ClassName } else { '<none>' })
Check 'B: window class is WinForms' ($cls -like 'WindowsForms10*') ('class=' + $cls)
Check 'B: no debug port under --old' ((Dbg 'status') -eq $null) ''
try { Stop-Process -Id $p2.Id -Force } catch { }

# ============================================================ C: --wpf still accepted
KillAll
Add-Content -LiteralPath $runLog -Value 'step: C start with the legacy --wpf flag' -Encoding UTF8
Start-Sleep -Seconds 2
$p3 = Start-Process -FilePath $exe -ArgumentList @('--wpf', "--debug-port=$port") -PassThru
$up3 = $false
for ($i = 0; $i -lt 25; $i++) {
  Start-Sleep -Milliseconds 1000
  if ((Dbg 'status') -ne $null) { $up3 = $true; break }
  $p3.Refresh(); if ($p3.HasExited) { break }
}
Check 'C: legacy --wpf still opens the WPF shell' ($up3 -and -not $p3.HasExited) ('waited=' + $i + 's')
try { Stop-Process -Id $p3.Id -Force } catch { }
KillAll

Copy-Item $bak $cfg -Force
Remove-Item $bak -Force
Write-Host 'settings restored'
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
