# verify-WPF-17: finding ffmpeg when the user put it somewhere else (2026-10-10)
# ASCII ONLY + CRLF.  PowerShell 5.1 reads a BOM-less UTF-8 script as ANSI; a Chinese byte
# pair ending a comment can swallow the newline and break the whole file's parse.
#
# Why this needs care: ffmpeg.exe is NOT a distinctive filename.  On the dev machine a
# 6-level walk of D: turns up five of them and only one is the user's own -- the rest are
# bundled inside other apps (resources\ subfolders), and one of those is an AUDIO-ONLY
# build.  So the walk may not just take the first hit: it has to rank candidates and then
# actually run them to prove they can encode video (libx264).
#
# The fixtures are two ~4 KB stand-ins compiled on the spot, so the test neither depends on
# what is installed on this machine nor copies 227 MB around:
#   goodf.exe -- prints "libx264" when asked for encoders  (looks usable)
#   badf.exe  -- prints nothing useful                      (must be rejected)
$root = 'D:\_reptest17'
$app  = Join-Path $root 'app'
$exe  = Join-Path $app 'RepRecorderWPF.exe'
$cfg  = Join-Path $app 'RepRecorder.settings.json'
$port = 7801
$fail = 0
$report = @()

function Dbg([string]$cmd) {
  try {
    $c = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $port)
    $c.ReceiveTimeout = 25000
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
  for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 400; if ((Dbg 'status') -ne $null) { return $p } }
  return $p
}

# ---------------------------------------------------------------- setup
Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 500
Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $app | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'RepRecorderWPF.exe') $exe -Force

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$goodSrc = Join-Path $root 'goodf.cs'
$badSrc  = Join-Path $root 'badf.cs'
Set-Content -LiteralPath $goodSrc -Encoding ASCII -Value @(
  'class P { static void Main() { System.Console.WriteLine(" V..... libx264          H.264 fake encoder"); } }')
Set-Content -LiteralPath $badSrc -Encoding ASCII -Value @(
  'class P { static void Main() { System.Console.WriteLine("this program has no encoders"); } }')
$good = Join-Path $root 'goodf.exe'
$bad  = Join-Path $root 'badf.exe'
& $csc /nologo /target:exe /out:$good $goodSrc | Out-Null
& $csc /nologo /target:exe /out:$bad  $badSrc  | Out-Null
if (-not (Test-Path $good)) { Write-Host 'FAIL: could not compile the fixture ffmpeg stand-ins'; exit 1 }
if (-not (Test-Path $bad))  { Write-Host 'FAIL: could not compile the fixture ffmpeg stand-ins'; exit 1 }
Write-Host 'fixtures compiled'

# tree1: a bundled-looking copy AND a user-looking copy -- the user-looking one must win
$t1a = Join-Path $root 'tree1\someapp\resources\bin'
$t1b = Join-Path $root 'tree1\ffmpeg\bin'
New-Item -ItemType Directory -Force -Path $t1a, $t1b | Out-Null
Copy-Item $good (Join-Path $t1a 'ffmpeg.exe') -Force
Copy-Item $good (Join-Path $t1b 'ffmpeg.exe') -Force
# tree2: only a fake that is NOT really ffmpeg, sitting in the most user-looking spot
$t2 = Join-Path $root 'tree2\ffmpeg\bin'
New-Item -ItemType Directory -Force -Path $t2 | Out-Null
Copy-Item $bad (Join-Path $t2 'ffmpeg.exe') -Force
# tree3: the impostor ranks first, a usable one is deeper -- must fall through to it
$t3a = Join-Path $root 'tree3\ffmpeg\bin'
$t3b = Join-Path $root 'tree3\zz\bin'
New-Item -ItemType Directory -Force -Path $t3a, $t3b | Out-Null
Copy-Item $bad  (Join-Path $t3a 'ffmpeg.exe') -Force
Copy-Item $good (Join-Path $t3b 'ffmpeg.exe') -Force
$empty = Join-Path $root 'empty'
New-Item -ItemType Directory -Force -Path $empty | Out-Null

[pscustomobject]@{ GameDir = ''; ObsExe = ''; FfmpegExe = ''; EnvChecked = $true } |
  ConvertTo-Json -Compress | Set-Content -LiteralPath $cfg -Encoding UTF8

$p = StartApp

# ============ 1) ranking ============================================================
$o = Dbg ('findffmpeg:scope=' + (Join-Path $root 'tree1')) | ConvertFrom-Json
Check 'a user-looking install beats an app-bundled copy' ($o.path -eq (Join-Path $t1b 'ffmpeg.exe')) ('path=' + $o.path)
Check 'and both were seen' ($o.count -eq 2) ('count=' + $o.count)

# ============ 2) validation =========================================================
$o = Dbg ('findffmpeg:scope=' + (Join-Path $root 'tree2')) | ConvertFrom-Json
Check 'a file named ffmpeg.exe that cannot encode is rejected' ($o.found -eq $false) ('path=' + $o.path)

# ============ 3) validation must fall through, not give up ==========================
$o = Dbg ('findffmpeg:scope=' + (Join-Path $root 'tree3')) | ConvertFrom-Json
Check 'the impostor is skipped for a usable one further down' ($o.path -eq (Join-Path $t3b 'ffmpeg.exe')) ('path=' + $o.path)

# ============ 4) nothing there means nothing found ==================================
$o = Dbg ('findffmpeg:scope=' + $empty) | ConvertFrom-Json
Check 'an empty tree finds nothing' ($o.found -eq $false) ('found=' + $o.found)

# ============ 5) the settings field wins ============================================
Patch 'FfmpegExe' (Join-Path $t1b 'ffmpeg.exe')
StopApp
$p = StartApp
$o = Dbg 'findffmpeg' | ConvertFrom-Json
Check 'a path saved in settings is what gets used' ($o.via -eq 'cfg') ('via=' + $o.via)
Check 'and it is the one that was saved' ($o.path -eq (Join-Path $t1b 'ffmpeg.exe')) ('path=' + $o.path)
StopApp

# ============ 6) it is remembered, so later launches do not re-search ===============
# This is the whole point of giving ffmpeg a settings field at all: before this change it
# was never stored anywhere and got searched again on every single call.
Patch 'FfmpegExe' ''
StopApp
$p = StartApp
$saved = Cfg 'FfmpegExe'
Check 'a found ffmpeg is written back into settings' (($saved -ne $null) -and ($saved -ne '')) ('FfmpegExe=' + $saved)
Check 'and the saved path is a real file' (($saved -ne $null) -and (Test-Path -LiteralPath $saved)) ('FfmpegExe=' + $saved)

$logTxt = if (Test-Path (Join-Path $app 'RepRecorder.log')) { Get-Content (Join-Path $app 'RepRecorder.log') -Raw -Encoding UTF8 } else { '' }
Check 'no exception in log' ($logTxt -notmatch 'Exception') ''
StopApp

# ---------------------------------------------------------------- teardown
Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot '_dump-utf8-17.json'), ($report | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
