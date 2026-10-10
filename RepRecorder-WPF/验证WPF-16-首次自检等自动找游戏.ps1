# verify-WPF-16: the first-run dialog must not contradict the settings (2026-10-09)
# ASCII ONLY + CRLF.  PowerShell 5.1 reads a BOM-less UTF-8 script as ANSI; a Chinese byte
# pair ending a comment can swallow the newline and break the whole file's parse.
#
# The bug this pins down (reported by the user): on a fresh machine the app opens, and the
# first-run dialog says "game dir: not found" with an X -- while the settings already hold a
# perfectly good game path.  Cause: the dialog is a one-shot snapshot built the instant the
# app starts, but the automatic game search only lands ~2 s later and fills GameDir in.
# The dialog has no way to refresh, so it keeps showing the stale X.
#
# The assertion is timing-independent: sample the dialog while it is up and require that it
# NEVER shows the game as missing, since the whole point is that its verdict must match what
# the settings end up saying.
#
# The fake game is two levels below the exe's parent so that only the expensive DISK WALK can
# find it -- the "nearby" layer would otherwise answer instantly and no scan would happen.
$root = 'D:\_reptest16'
$app  = Join-Path $root 'app'
$exe  = Join-Path $app 'RepRecorderWPF.exe'
$cfg  = Join-Path $app 'RepRecorder.settings.json'
$port = 7800
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

function StopApp { Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }; Start-Sleep -Milliseconds 700 }
function StartApp {
  $p = Start-Process -FilePath $exe -WorkingDirectory $app -ArgumentList @('--wpf', "--debug-port=$port") -PassThru
  for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 400; if ((Dbg 'status') -ne $null) { return $p } }
  return $p
}
# Fire a command and do NOT wait for the reply.
# Needed for anything that opens a MODAL dialog: `press:btnCheck` goes through
# Dispatcher.Invoke, so it does not return until that dialog is closed -- a normal Dbg call
# would sit there until its receive timeout, by which time the thing we wanted to watch has
# long since happened.  (The reply write then fails on the app side, which it swallows.)
function DbgFire([string]$cmd) {
  try {
    $c = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $port)
    $w = New-Object System.IO.StreamWriter($c.GetStream()); $w.AutoFlush = $true; $w.WriteLine($cmd)
    Start-Sleep -Milliseconds 120
    $c.Close()
  } catch { }
}

# ---------------------------------------------------------------- setup
Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 500
Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $app | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'RepRecorderWPF.exe') $exe -Force

$deepGame = Join-Path $root 'pkg\game\th123'
New-Item -ItemType Directory -Force -Path $deepGame | Out-Null
Set-Content -LiteralPath (Join-Path $deepGame 'th123.exe') -Value 'x' -Encoding ASCII
Set-Content -LiteralPath (Join-Path $deepGame 'th123a.dat') -Value 'x' -Encoding ASCII

# The walk ranks candidates: one sitting next to a SokuLauncher.json wins, ties go to the
# newest folder.  The real game on this machine also sits next to a SokuLauncher.json, so we
# mirror that and stamp a far-future timestamp to make our fake the deterministic winner.
# We NEED the fake adopted: it has no ModLoaderSettings.json, so exactly one env item stays
# broken and the first-run dialog really pops up.  If the real game won instead, the whole
# environment would be green, the dialog would be suppressed on purpose, and every assertion
# about its contents would pass vacuously -- exactly the kind of no-op check to avoid.
'{"SokuDirPath":"th123","SokuFileName":"th123.exe"}' |
  Set-Content -LiteralPath (Join-Path (Split-Path $deepGame) 'SokuLauncher.json') -Encoding ASCII
(Get-Item -LiteralPath $deepGame).LastWriteTime = [datetime]'2030-01-01'

# Empty game dir + first run: exactly the state the user was in.
[pscustomobject]@{ GameDir = ''; ObsExe = ''; EnvChecked = $false } |
  ConvertTo-Json -Compress | Set-Content -LiteralPath $cfg -Encoding UTF8

# ============ start it and watch the dialog ==========================================
$p = StartApp
$sawDialog = $false
$marks = @()
$scanMs = 0
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
  $f = Dbg 'findgame'
  if ($f -ne $null) { $scanMs = ($f | ConvertFrom-Json).ms }
  if ((Dbg 'find:envDialog' | ConvertFrom-Json).found -eq $true) {
    $sawDialog = $true
    $m = (Dbg 'text:env_gameMark' | ConvertFrom-Json).text
    if ($m -ne $null) { $marks += $m }
    # the game row is settled once a scan has run; stop as soon as we can judge
    if ($marks.Count -gt 0 -and $scanMs -gt 0) { break }
  }
  Start-Sleep -Milliseconds 150
}
Start-Sleep -Milliseconds 500

Check 'the first-run dialog did appear' ($sawDialog -eq $true) ('sawDialog=' + $sawDialog)
Check 'a background disk walk really ran' ($scanMs -gt 0) ('scanMs=' + $scanMs)
Check 'the dialog never claims the game is missing' (($marks -contains $([char]0x2717)) -eq $false) ('marks=' + ($marks -join ','))
Check 'and it shows the game as found' ($marks -contains $([char]0x2713)) ('marks=' + ($marks -join ','))

$g = Cfg 'GameDir'
Check 'the game dir was adopted into settings' (($g -ne $null) -and ($g -ne '') -and (Test-Path (Join-Path $g 'th123.exe'))) ('GameDir=' + $g)
Check 'the first-run check was remembered' ((Cfg 'EnvChecked') -eq $true) ('EnvChecked=' + (Cfg 'EnvChecked'))

# ============ 7) the open dialog must follow a fresh search (2026-10-10) =============
# The card used to be a one-shot snapshot: whatever it said when it opened, it said forever.
# Real scenario -- the game was pointed at somewhere that has since gone away, the user hits
# 「检查环境」, and the background search finds the pack again a second later.  The card must
# stop claiming the game is missing, or the user is left staring at a contradiction.
# GameDir is deliberately valid at startup so no search runs then; it only goes stale after.
$stale = Join-Path $root 'stale\th123'
New-Item -ItemType Directory -Force -Path $stale | Out-Null
Set-Content -LiteralPath (Join-Path $stale 'th123.exe') -Value 'x' -Encoding ASCII
[pscustomobject]@{ GameDir = $stale; ObsExe = ''; EnvChecked = $true } |
  ConvertTo-Json -Compress | Set-Content -LiteralPath $cfg -Encoding UTF8
StopApp
$p = StartApp
Remove-Item -LiteralPath (Join-Path $stale 'th123.exe') -Force       # the game "moves away"
DbgFire 'press:btnCheck'
Start-Sleep -Milliseconds 500
$first = (Dbg 'text:env_gameMark' | ConvertFrom-Json).text
$flipped = $false
for ($i = 0; $i -lt 40; $i++) {
  Start-Sleep -Milliseconds 500
  $t = (Dbg 'text:env_gameMark' | ConvertFrom-Json).text
  if ($t -eq $([char]0x2713)) { $flipped = $true; break }
}
Check 'the card starts by reporting the game as missing' ($first -eq $([char]0x2717)) ('first=' + $first)
Check 'and it picks up the fresh search on its own' ($flipped -eq $true) ('flipped=' + $flipped)
if ((Dbg 'find:envClose' | ConvertFrom-Json).found -eq $true) { Dbg 'press:envClose' | Out-Null }
StopApp

$logTxt = if (Test-Path (Join-Path $app 'RepRecorder.log')) { Get-Content (Join-Path $app 'RepRecorder.log') -Raw -Encoding UTF8 } else { '' }
Check 'no exception in log' ($logTxt -notmatch 'Exception') ''

Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 700

# ---------------------------------------------------------------- teardown
Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot '_dump-utf8-16.json'), ($report | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
