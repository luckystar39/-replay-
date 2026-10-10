# THROWAWAY: the one thing the offline suite cannot cover -- a REAL download from Gitee,
# through the app's own code path, with the Chinese filename in the URL.
# Not part of the verify suite: it needs the network and the published release.
# Pure ASCII + LF.  Delete when done.
$ErrorActionPreference = 'Continue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$root = 'D:\_reptest18net'
$app  = Join-Path $root 'app'
$exe  = Join-Path $app 'RepRecorderWPF.exe'
$cfg  = Join-Path $app 'RepRecorder.settings.json'
$port = 7805
$fail = 0

function Dbg([string]$cmd) {
  try {
    $c = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $port)
    $c.ReceiveTimeout = 30000
    $w = New-Object System.IO.StreamWriter($c.GetStream()); $w.AutoFlush = $true; $w.WriteLine($cmd)
    $r = New-Object System.IO.StreamReader($c.GetStream()); $out = $r.ReadLine(); $c.Close()
    return $out
  } catch { return $null }
}
function Check([string]$what, [bool]$ok, [string]$detail) {
  if ($ok) { Write-Host ("OK   {0}  {1}" -f $what, $detail) }
  else { Write-Host ("FAIL {0}  {1}" -f $what, $detail); $script:fail = 1 }
}
function StopApp { Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }; Start-Sleep -Milliseconds 700 }

Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 500
Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $app | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'RepRecorderWPF.exe') $exe -Force
$devHash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
[pscustomobject]@{ GameDir = ''; ObsExe = ''; FfmpegExe = ''; EnvChecked = $true; LastUpdateCheckUtc = ''; SkippedUpdateVersion = '' } |
  ConvertTo-Json -Compress | Set-Content -LiteralPath $cfg -Encoding UTF8

# 1) fetch the REAL published manifest, over the network, exactly like the app does
#    Decode the bytes as UTF-8 MYSELF: WebClient.DownloadString falls back to the system ANSI
#    code page when the response has no charset, which turns the Chinese into mojibake AND
#    eats the closing quote after it (so the JSON stops parsing).  The app reads the stream
#    with an explicit Encoding.UTF8, so this is a harness bug, not a product bug.
$wc = New-Object Net.WebClient
$wc.Headers.Add('User-Agent', 'RepRecorder/1.0')
$bytes = $wc.DownloadData('https://gitee.com/xingya30/reprecorder-update/raw/master/version.json')
$live = [Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF, [char]0x200B).Trim()
Write-Host ("     manifest: " + $bytes.Length + " bytes, ends with: " + $live.Substring($live.Length - 3))
$okJson = $true
try { $j = $live | ConvertFrom-Json } catch { $okJson = $false }
Check 'the published manifest is reachable and parses as utf-8 json' $okJson ''
if (-not $okJson) { Write-Host "NETCHECK: ABORT (manifest did not parse)"; StopApp; exit 1 }
Check 'it advertises the same build that is installed' ($j.Version -eq '1.0') ('Version=' + $j.Version + ' links=' + $j.DownloadLinks.Count)
Check 'it carries a full sha256 of the package' ($j.Sha256.Length -eq 64) ('sha=' + $j.Sha256.Substring(0, 12) + '...')
$gt = @($j.DownloadLinks | Where-Object { $_.Type -eq 'Gitee' })
Check 'there is a gitee link to download from' ($gt.Count -eq 1) ''

# 2) rewrite just the VERSION so the app sees an update, keeping the real URL + real sha.
#    The URL string keeps its raw Chinese characters, which is the point of this whole check.
$j.Version = '9.9'
$j.Notes = 'network self-test'
$fixture = Join-Path $root 'net-manifest.json'
$j | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $fixture -Encoding UTF8
$fixUrl = 'file:///' + $fixture.Replace('\', '/')
$rawUrl = @($j.DownloadLinks | Where-Object { $_.Type -eq 'Gitee' })[0].Url
$hasCn = $false
foreach ($ch in $rawUrl.ToCharArray()) { if ([int]$ch -gt 127) { $hasCn = $true } }
Check 'the download url really carries non-ascii characters' $hasCn ('len=' + $rawUrl.Length)

# 3) start the app on that manifest and wait for the prompt
$p = Start-Process -FilePath $exe -WorkingDirectory $app -ArgumentList @('--wpf', "--debug-port=$port", ('--update-url=' + $fixUrl)) -PassThru
$shown = $false
for ($i = 0; $i -lt 50; $i++) { Start-Sleep -Milliseconds 400; if ((Dbg 'status') -ne $null) { break } }
for ($i = 0; $i -lt 50; $i++) {
  Start-Sleep -Milliseconds 400
  if ((Dbg 'find:updateDialog' | ConvertFrom-Json).found -eq $true) { $shown = $true; break }
}
Check 'the update dialog appears' $shown ''

# 4) press the download button and let it do the real thing.
#    Wait for the BUTTON to come back enabled -- that happens exactly once, when the worker
#    thread is finished, success or failure.  (First version waited for "9.9" to show up in the
#    log: the log already said "found new version 9.9" at startup, so it broke out after 1 s
#    and reported success before anything had been downloaded.  Classic fake green.)
$logPath = Join-Path $app 'RepRecorder.log'
$cnBusy  = -join (@(0x6B63, 0x5728, 0x4E0B, 0x8F7D) | ForEach-Object { [char]$_ })   # "downloading"
$cnOkTxt = -join (@(0x5DF2, 0x66F4, 0x65B0) | ForEach-Object { [char]$_ })           # "now updated"
$cnDone  = -join (@(0x66F4, 0x65B0, 0x5B8C, 0x6210) | ForEach-Object { [char]$_ })   # "update complete"

$t0 = Get-Date
Dbg 'press:updateGo' | Out-Null
$statusTxt = ''; $btnTxt = ''; $finished = $false
for ($i = 0; $i -lt 240; $i++) {
  Start-Sleep -Milliseconds 500
  $btnTxt = (Dbg 'text:updateGo' | ConvertFrom-Json).text
  $enabled = (Dbg 'find:updateGo' | ConvertFrom-Json).enabled
  $statusTxt = (Dbg 'text:updateStatus' | ConvertFrom-Json).text
  if (($enabled -eq $true) -and ($statusTxt -ne '') -and ($statusTxt -notmatch $cnBusy)) { $finished = $true; break }
}
$secs = [int]((Get-Date) - $t0).TotalSeconds
$log = Get-Content $logPath -Raw -Encoding UTF8
Check 'the download finished instead of hanging' $finished ('after ' + $secs + 's')

# The console prints Chinese as mojibake under this shell's code page, so put the two lines
# that matter into a utf-8 file and read them from there.
Set-Content -LiteralPath (Join-Path $root 'outcome.txt') -Encoding UTF8 -Value @(
  ('status: ' + $statusTxt), ('button: ' + $btnTxt), '--- log ---', $log)
Check 'the app says it updated (not "update failed")' ($statusTxt -match $cnOkTxt) ('status len=' + $statusTxt.Length)

# 5) the payload really landed: same bytes as the published build
$newHash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
# NOTE: gate this on .old existing.  Without the swap the file trivially equals itself, so a
# bare hash comparison stays green even when nothing happened at all (it did, first run).
Check 'the installed exe was really replaced by the published build' ((Test-Path ($exe + '.old')) -and ($newHash -eq $devHash)) ('sha=' + $newHash.Substring(0, 12) + '...')
Check 'the old exe was kept as .old' (Test-Path ($exe + '.old')) ''
Check 'the readme files came along with it' ((Get-ChildItem $app -Filter '*.md').Count -ge 1) ''
Check 'and the log says the update completed' ($log -match $cnDone) ''

# 6) the restart button.
#    NOTE: do NOT probe this over the debug port -- Restart() relaunches the exe with NO
#    arguments (correct for real users, who never pass --debug-port), so the new process has no
#    debug port and "status" returns null even though the app is up and fine.  Look for a real
#    process instead, started from the exe we just replaced.
$t1 = Get-Date
Dbg 'press:updateGo' | Out-Null
$restarted = $false; $newPath = ''
for ($i = 0; $i -lt 40; $i++) {
  Start-Sleep -Milliseconds 500
  $ps = @(Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt $t1 })
  if ($ps.Count -ge 1) { $restarted = $true; try { $newPath = $ps[0].Path } catch { } ; break }
}
Check 'the restart button brings the app back up' $restarted ''
Check 'and the process it started is the replaced exe' ($newPath -eq $exe) ('path=' + $newPath)
# The rotated log now holds only the new instance, so a fresh startup block is more evidence.
$newLog = Get-Content $logPath -Raw -Encoding UTF8
$cnBoot = -join (@(0x542F, 0x52A8) | ForEach-Object { [char]$_ })                    # "startup"
Check 'the restarted app got through startup cleanly' ($newLog -match $cnBoot) ('log bytes=' + $newLog.Length)
Set-Content -LiteralPath (Join-Path $root 'outcome.txt') -Encoding UTF8 -Value @(
  ('status: ' + $statusTxt), ('button: ' + $btnTxt), ('restarted path: ' + $newPath),
  '--- log of the restarted instance ---', $newLog)

StopApp
Write-Host ""
if ($fail -eq 0) { Write-Host "NETCHECK: PASS" } else { Write-Host "NETCHECK: FAIL" }
