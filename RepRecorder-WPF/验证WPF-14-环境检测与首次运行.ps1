# verify-WPF-14: the environment self-check and the first-run experience (2026-10-08)
# ASCII ONLY + CRLF.  PowerShell 5.1 reads a BOM-less UTF-8 script as ANSI; a Chinese byte
# pair ending a comment can swallow the newline and break the whole file's parse.
$dir = $PSScriptRoot
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$cfg = Join-Path $dir 'RepRecorder.settings.json'
$bak = Join-Path $dir '_settings-backup14.json'
$dump = Join-Path $dir '_dump-utf8.json'
$port = 7797
$fail = 0
$report = @()

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
  if ($ok) { Write-Host ("OK  {0}  {1}" -f $what, $detail) }
  else { Write-Host ("FAIL {0}  {1}" -f $what, $detail); $script:fail = 1 }
  $script:report += [pscustomobject]@{ what = $what; ok = $ok; detail = $detail }
}
function Cfg([string]$key) {
  try { return (Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json).$key } catch { return $null }
}
function Patch([string]$key, [string]$value) {
  $j = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
  $j | Add-Member -NotePropertyName $key -NotePropertyValue $value -Force
  $j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
}
function StopApp { Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }; Start-Sleep -Milliseconds 700 }
function StartApp {
  $p = Start-Process -FilePath $exe -ArgumentList @('--wpf', "--debug-port=$port") -PassThru
  for ($i = 0; $i -lt 30; $i++) { Start-Sleep -Milliseconds 400; if ((Dbg 'status') -ne $null) { return $p } }
  return $p
}

# A fake "game" folder: th123.exe exists (so the app-counts-it-as-found game dir is kept) but
# there is no ModLoaderSettings.json -> exactly one env item goes missing, which is what we
# need to exercise the "something is wrong" path without touching the real game.
$fake = Join-Path $env:TEMP 'repenv14'
$fakeGame = Join-Path $fake 'th123'
New-Item -ItemType Directory -Force -Path $fakeGame | Out-Null
Set-Content -LiteralPath (Join-Path $fakeGame 'th123.exe') -Value 'x' -Encoding ASCII

StopApp
Copy-Item $cfg $bak -Force

# ============ 1) healthy machine ======================================================
$p = StartApp
$e = Dbg 'env' | ConvertFrom-Json
Check 'env reports five items' ($e.items.Count -eq 5) ('n=' + $e.items.Count)
Check 'a healthy machine reports allOk' ($e.allOk -eq $true) ('allOk=' + $e.allOk)
$line = (Dbg 'text:envLine' | ConvertFrom-Json).text
Check 'the env line is actually rendered in the UI' ($line.Length -gt 0) ('text=' + $line)
Check 'no dialog pops when nothing is wrong' ((Dbg 'find:envDialog' | ConvertFrom-Json).found -eq $false) ''
Check 'the first-run check was remembered' ((Cfg 'EnvChecked') -eq $true) ('EnvChecked=' + (Cfg 'EnvChecked'))
StopApp

# ============ 2) a machine that is missing something ==================================
Patch 'GameDir' $fakeGame
Patch 'EnvChecked' $false
$p = StartApp
$e = Dbg 'env' | ConvertFrom-Json
$mod = $e.items | Where-Object { $_.key -eq 'mod' }
Check 'a missing mod is reported' ($mod.ok -eq $false) ('mod=' + $mod.value)
Check 'allOk goes false' ($e.allOk -eq $false) ('allOk=' + $e.allOk)
Check 'the first-run dialog pops up' ((Dbg 'find:envDialog' | ConvertFrom-Json).found -eq $true) ''
$line = (Dbg 'text:envLine' | ConvertFrom-Json).text
Check 'the env line now shows a problem' ($line.Length -gt 0) ('text=' + $line)
Check 'the dialog carries the recall id' ((Dbg 'find:envClose' | ConvertFrom-Json).found -eq $true) ''
Dbg 'press:envClose' | Out-Null
Start-Sleep -Milliseconds 700
Check 'closing it works' ((Dbg 'find:envDialog' | ConvertFrom-Json).found -eq $false) ''
StopApp

# EnvChecked was set by the launch above.  Relaunch WITHOUT resetting it: the env is still
# broken, but the user already saw the dialog once, so it must stay quiet this time.
$p = StartApp
Check 'the first-run self-check does not nag twice' ((Dbg 'find:envDialog' | ConvertFrom-Json).found -eq $false) ('EnvChecked=' + (Cfg 'EnvChecked'))
StopApp

# ============ 3) recording is refused when the game dir is not usable =================
Patch 'EnvChecked' $true          # keep the startup dialog out of the way
$p = StartApp
[System.IO.File]::WriteAllText((Join-Path $dir '_env-probe.rep'), 'x')
Dbg 'addrows:[{"file":"_env-probe.rep","status":"waiting"}]' | Out-Null
Remove-Item -LiteralPath (Join-Path $fakeGame 'th123.exe') -Force   # the game dir goes bad while running
Start-Sleep -Milliseconds 500
Dbg 'press:btnStart' | Out-Null
Start-Sleep -Seconds 2
$s = Dbg 'status' | ConvertFrom-Json
Check 'recording is refused when the game dir is broken' ($s.recording -eq $false) ('recording=' + $s.recording)
Check 'and the user is told why (dialog)' ((Dbg 'find:envDialog' | ConvertFrom-Json).found -eq $true) ''
if ((Dbg 'find:envClose' | ConvertFrom-Json).found -eq $true) { Dbg 'press:envClose' | Out-Null }
StopApp

# ============ 4) the env command must not crash without settings ======================
$logTxt = if (Test-Path (Join-Path $dir 'RepRecorder.log')) { Get-Content (Join-Path $dir 'RepRecorder.log') -Raw -Encoding UTF8 } else { '' }
Check 'no exception in log' ($logTxt -notmatch 'Exception') ''

# ============ 5) the OBS registry lookup must also read HKCU (2026-10-10) ==============
# FindObsExe used to read only HKLM, so an OBS installed for one user only was invisible.
# On a machine that already has a real OBS the "who won?" answer can never prove the HKCU
# half works -- the real one always wins -- so drop a throw-away uninstall entry under HKCU
# and require it to turn up in the candidate list that this one step produces.
$fakeObsRoot = Join-Path $env:TEMP 'repobs-verify'
$fakeObs = Join-Path $fakeObsRoot 'bin\64bit\obs64.exe'
$rkey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\RepRecorderVerifyObs'
New-Item -ItemType Directory -Force -Path (Split-Path $fakeObs) | Out-Null
Set-Content -LiteralPath $fakeObs -Value 'x' -Encoding ASCII
try {
  New-Item -Path $rkey -Force | Out-Null
  Set-ItemProperty -Path $rkey -Name DisplayName -Value 'OBS Studio (verify)'
  Set-ItemProperty -Path $rkey -Name DisplayIcon -Value $fakeObs
  Patch 'EnvChecked' $true
  $p = StartApp
  $r = Dbg 'obsreg' | ConvertFrom-Json
  Check 'the OBS registry step reads HKCU too' ($r.cands -contains $fakeObs) ('cands=' + ($r.cands -join ' | '))
  StopApp
} finally {
  # always leave the user's registry alone, even if an assertion blew up
  Remove-Item -Path $rkey -Recurse -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $fakeObsRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Remove-Item -LiteralPath (Join-Path $dir '_env-probe.rep') -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $fake -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $bak $cfg -Force
Remove-Item $bak -Force
Write-Host 'settings restored'
[System.IO.File]::WriteAllText($dump, ($report | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('dump -> ' + $dump)
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
