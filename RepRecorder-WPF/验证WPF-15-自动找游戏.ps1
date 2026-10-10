# verify-WPF-15: finding the game folder automatically (2026-10-09)
# ASCII ONLY + CRLF.  PowerShell 5.1 reads a BOM-less UTF-8 script as ANSI; a Chinese byte
# pair ending a comment can swallow the newline and break the whole file's parse.
#
# Why this script does NOT run the exe in place: the real game sits one level under the
# exe's parent in this repo, so the cheap "nearby" layer always hits and no scan ever runs.
# We copy the exe into an isolated tree on D: so each layer can be exercised on its own.
#
# Layers under test (see Settings.FindGameDir in Core.cs):
#   cfg      -- the path already saved in settings, nothing is searched
#   near     -- next to the exe / one level up / one level of subfolders
#   launcher -- SokuLauncher.json's SokuDirPath (the pack states where its game is)
#   scan     -- the background walk over every fixed drive
$root = 'D:\_reptest15'
$app  = Join-Path $root 'app'
$exe  = Join-Path $app 'RepRecorderWPF.exe'
$cfg  = Join-Path $app 'RepRecorder.settings.json'
$port = 7798
$fail = 0
$report = @()

function Dbg([string]$cmd) {
  try {
    $c = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $port)
    $c.ReceiveTimeout = 15000
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
function Patch([string]$key, $value) {
  $j = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
  $j | Add-Member -NotePropertyName $key -NotePropertyValue $value -Force
  $j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
}
function StopApp { Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }; Start-Sleep -Milliseconds 700 }
function StartApp {
  $p = Start-Process -FilePath $exe -WorkingDirectory $app -ArgumentList @('--wpf', "--debug-port=$port") -PassThru
  for ($i = 0; $i -lt 30; $i++) { Start-Sleep -Milliseconds 400; if ((Dbg 'status') -ne $null) { return $p } }
  return $p
}
# Poll findgame until the background walk is done (or we give up).
function WaitScan([int]$timeoutMs) {
  $t = 0
  while ($t -lt $timeoutMs) {
    $j = Dbg 'findgame'
    if ($j -ne $null) { $o = $j | ConvertFrom-Json; if ($o.scanning -eq $false) { return $o } }
    Start-Sleep -Milliseconds 500; $t += 500
  }
  return (Dbg 'findgame' | ConvertFrom-Json)
}
function MkGame([string]$dir) {
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  Set-Content -LiteralPath (Join-Path $dir 'th123.exe') -Value 'x' -Encoding ASCII
  Set-Content -LiteralPath (Join-Path $dir 'th123a.dat') -Value 'x' -Encoding ASCII   # strict marker
}

# ---------------------------------------------------------------- setup
Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 500
Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $app | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'RepRecorderWPF.exe') $exe -Force

# A fixture placed INSIDE the app dir would be picked up by the NEAR layer and mask the
# layer under test, so the fake game lives two levels down instead -- out of near's reach.
$fakeCfg = Join-Path $root 'pkg\game\th123'
MkGame $fakeCfg
$deepGame = Join-Path $root 'pkg\game\th123'     # OUTSIDE the near layer (two levels down)
MkGame $deepGame
$launcherJson = Join-Path $root 'pkg\SokuLauncher.json'
$empty = Join-Path $root 'empty'
New-Item -ItemType Directory -Force -Path $empty | Out-Null

# ============ 1) a saved game dir means nothing is searched ==========================
$settings = [pscustomobject]@{ GameDir = $fakeCfg; ObsExe = ''; EnvChecked = $true }
$settings | ConvertTo-Json -Compress | Set-Content -LiteralPath $cfg -Encoding UTF8
$p = StartApp
$o = Dbg 'findgame' | ConvertFrom-Json
Check 'a saved game dir is reported as cfg' ($o.cfgValid -eq $true) ('cfgValid=' + $o.cfgValid)
Check 'and no scan is started at all' (($o.scanning -eq $false) -and ($o.ms -eq 0)) ('scanning=' + $o.scanning + ' ms=' + $o.ms)
StopApp

# ============ 2) SokuLauncher.json decides, with no scan needed ======================
# The pack's own file states where its game is -- so it must win over a full disk walk.
# pkg sits one level under the exe's parent, so the launcher layer sees it; the game
# itself is two levels down, so the near layer must NOT be the one that finds it.
'{"SokuDirPath":"game\\th123","SokuFileName":"th123.exe"}' | Set-Content -LiteralPath $launcherJson -Encoding ASCII
$settings = [pscustomobject]@{ GameDir = ''; ObsExe = ''; EnvChecked = $true }
$settings | ConvertTo-Json -Compress | Set-Content -LiteralPath $cfg -Encoding UTF8
$p = StartApp
$o = Dbg 'findgame' | ConvertFrom-Json
Check 'the launcher file is what found the game' ($o.via -eq 'launcher') ('via=' + $o.via + ' dir=' + $o.dir)
Check 'and it resolved to the path the pack declared' ($o.dir -eq $deepGame) ('dir=' + $o.dir)
Check 'no disk walk was needed for that' (($o.scanning -eq $false) -and ($o.ms -eq 0)) ('scanning=' + $o.scanning + ' ms=' + $o.ms)
Check 'the found dir is saved back into settings' ((Cfg 'GameDir') -eq $deepGame) ('GameDir=' + (Cfg 'GameDir'))
StopApp

# ============ 3) with no launcher file, the background walk finds it ==================
Remove-Item -LiteralPath $launcherJson -Force
$settings = [pscustomobject]@{ GameDir = ''; ObsExe = ''; EnvChecked = $true }
$settings | ConvertTo-Json -Compress | Set-Content -LiteralPath $cfg -Encoding UTF8
$p = StartApp
$o = WaitScan 90000
Check 'the walk reports the scan layer' ($o.via -eq 'scan') ('via=' + $o.via)
Check 'the walk found a game' ($o.found -eq $true) ('dir=' + $o.dir)
Check 'the found dir really holds th123.exe' (($o.dir -ne $null) -and (Test-Path (Join-Path $o.dir 'th123.exe'))) ('dir=' + $o.dir)
StopApp

# ============ 4) the walk is scoped: an empty tree finds nothing =====================
$p = StartApp
$o = Dbg ('findgame:scope=' + $empty) | ConvertFrom-Json
Check 'an empty scope finds nothing' ($o.found -eq $false) ('found=' + $o.found + ' dir=' + $o.dir)
# and the same command over the deep fake must find exactly that one
$o = Dbg ('findgame:scope=' + (Join-Path $root 'pkg')) | ConvertFrom-Json
Check 'a scoped walk finds the game it was pointed at' ($o.found -eq $true) ('found=' + $o.found)
Check 'and reports the exact folder' ($o.dir -eq $deepGame) ('dir=' + $o.dir)
StopApp

# ============ 5) the walk must NOT freeze the UI ====================================
# This whole change exists so that the expensive search never blocks the window. If the
# background thread is really background, debug commands keep answering while it runs.
$settings = [pscustomobject]@{ GameDir = ''; ObsExe = ''; EnvChecked = $true }
$settings | ConvertTo-Json -Compress | Set-Content -LiteralPath $cfg -Encoding UTF8
$p = StartApp
$worst = 0
$sawScanning = $false
for ($i = 0; $i -lt 20; $i++) {
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $null = Dbg 'status'
  $sw.Stop()
  if ($sw.ElapsedMilliseconds -gt $worst) { $worst = $sw.ElapsedMilliseconds }
  $j = Dbg 'findgame'
  if ($j -ne $null) { if (($j | ConvertFrom-Json).scanning -eq $true) { $sawScanning = $true } }
  Start-Sleep -Milliseconds 300
}
Check 'the window kept answering while the walk ran' ($worst -lt 500) ('worst round trip=' + $worst + ' ms')
Check 'a background walk was actually observed' ($sawScanning -eq $true) ('sawScanning=' + $sawScanning)
$logTxt = if (Test-Path (Join-Path $app 'RepRecorder.log')) { Get-Content (Join-Path $app 'RepRecorder.log') -Raw -Encoding UTF8 } else { '' }
Check 'no exception in log' ($logTxt -notmatch 'Exception') ''
StopApp

# ============ 6) two games near the recorder: the one the pack declares must win ======
# This layer used to return the FIRST hit, and Directory.GetDirectories yields NTFS's own
# (case-insensitively alphabetical) order -- so with a stale copy sitting in an earlier-named
# folder, the app would silently pick the stale one and record strange things.  The winning
# fixture is deliberately named so it sorts LAST.
foreach ($n in @('aaa', 'bbb', 'ccc')) { MkGame (Join-Path $app ($n + '\th123')) }
MkGame (Join-Path $app 'zzz\th123')
'{"SokuDirPath":"th123","SokuFileName":"th123.exe"}' |
  Set-Content -LiteralPath (Join-Path $app 'zzz\SokuLauncher.json') -Encoding ASCII
$settings = [pscustomobject]@{ GameDir = ''; ObsExe = ''; EnvChecked = $true }
$settings | ConvertTo-Json -Compress | Set-Content -LiteralPath $cfg -Encoding UTF8
$p = StartApp
$o = Dbg 'findgame' | ConvertFrom-Json
Check 'the pack the launcher names beats earlier-named stale copies' ($o.dir -eq (Join-Path $app 'zzz\th123')) ('dir=' + $o.dir)
StopApp

# ---------------------------------------------------------------- teardown
Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot '_dump-utf8-15.json'), ($report | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
