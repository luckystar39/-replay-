# THROWAWAY-ish: the one thing the offline suite cannot cover -- a REAL download from the
# published release, through the app's own code path, with the Chinese filename in the URL.
# Not part of the verify suite: it needs the network and a published release.
# Pure ASCII + LF.  Run it after every release.
#
#   pass A "published": the manifest exactly as published (Gitee first in the list)
#   pass B "github":    only the GitHub link offered -- proves the FALLBACK works too,
#                       which nothing else covers (the app stops at the first link that works)
#   plus a reachability check on FullPackage (85 MB: HEAD only, not downloaded here)
$ErrorActionPreference = 'Continue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$root  = 'D:\_reptest18net'
$port  = 7805
$fail  = 0
$devExe = Join-Path $PSScriptRoot 'RepRecorderWPF.exe'
$manifestUrl = 'https://gitee.com/xingya30/reprecorder-update/raw/master/version.json'
$wantSha     = 'ab569144e7758852a20bf531f5aadb7f003cfe1a5e2a5885e4be5532fca46aa0'

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
function CN([int[]]$cp) { -join ($cp | ForEach-Object { [char]$_ }) }

$cnBusy = CN 0x6B63, 0x5728, 0x4E0B, 0x8F7D      # "downloading"
$cnOkt  = CN 0x5DF2, 0x66F4, 0x65B0              # "now updated"
$cnDone = CN 0x66F4, 0x65B0, 0x5B8C, 0x6210      # "update complete"
$cnBoot = CN 0x542F, 0x52A8                      # "startup"

# ---------------------------------------------------------------- 1) the published manifest
# Decode the bytes as UTF-8 MYSELF: WebClient.DownloadString falls back to the system ANSI code
# page when the response has no charset, which turns the Chinese into mojibake AND eats the
# closing quote after it, so the JSON stops parsing.  The app reads the stream with an explicit
# Encoding.UTF8, so that is a harness bug, not a product bug.
$wc = New-Object Net.WebClient
$wc.Headers.Add('User-Agent', 'RepRecorder/1.0')
$bytes = $wc.DownloadData($manifestUrl)
$live = [Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF, [char]0x200B).Trim()
$okJson = $true
try { $j = $live | ConvertFrom-Json } catch { $okJson = $false }
Check 'the published manifest is reachable and parses as utf-8 json' $okJson ('bytes=' + $bytes.Length)
if (-not $okJson) { Write-Host 'NETCHECK: ABORT (manifest did not parse)'; StopApp; exit 1 }
Check 'it declares 1.0, the version that is installed' ($j.Version -eq '1.0') ('Version=' + $j.Version)
Check 'its sha256 is the one the published package really has' ($j.Sha256 -eq $wantSha) ('sha=' + $j.Sha256.Substring(0, 12) + '...')
$gt = @($j.DownloadLinks | Where-Object { $_.Type -eq 'Gitee' })
$gh = @($j.DownloadLinks | Where-Object { $_.Type -eq 'GitHub' })
Check 'there is exactly one gitee link' ($gt.Count -eq 1) ''
Check 'and one github link' ($gh.Count -eq 1) ''
Check 'the gitee link points at the update repo' (($gt.Count -eq 1) -and ($gt[0].Url.StartsWith('https://gitee.com/xingya30/reprecorder-update/releases/download/v1.0/'))) ''
$ghHead = ''
$ghLen = 0
if ($gh.Count -eq 1) {
  $ghLen = $gh[0].Url.Length
  $ghHead = $gh[0].Url.Substring(0, [Math]::Min(60, $ghLen))
}
Check 'the github link points at the renamed repo (not the old one)' (($gh.Count -eq 1) -and ($gh[0].Url.StartsWith('https://github.com/luckystar39/soku-RepRecorder/releases/download/v1.0/'))) $ghHead
# INVERTED 2026-10-10.  This used to assert the url DID carry non-ascii characters, because the
# Chinese filename was the risk under test ("does WebRequest cope with it?").  WebRequest copes
# fine -- the problem turned out to be that GitHub DROPS non-ascii from an uploaded asset name,
# so the file lands under a different name and the advertised url 404s.
# The GITHUB urls must therefore be plain ASCII (the release script generates them that way from
# 1.1 on).  Gitee is exempt on purpose: it keeps the uploaded name as-is, and the live 1.0 asset
# really is named RepRecorder-1.0-<chinese>.zip and really does serve bytes.
$nonAscii = 0
foreach ($ch in $gh[0].Url.ToCharArray()) { if ([int]$ch -gt 127) { $nonAscii++ } }
foreach ($ch in $j.FullPackage.ToCharArray()) { if ([int]$ch -gt 127) { $nonAscii++ } }
Check 'the github urls are ascii-only' ($nonAscii -eq 0) ('non-ascii chars=' + $nonAscii + ' (github url len=' + $ghLen + ')')

# ---------------------------------------------------------------- 2) the full package link
# The app never downloads this one (the button just opens a browser), so a HEAD is the right
# check: it catches the 404 that a private repo / wrong asset name would give.
if ($j.FullPackage.Length -gt 0) {
  try {
    $req = [Net.WebRequest]::Create($j.FullPackage)
    $req.Method = 'HEAD'
    $req.Timeout = 30000
    $req.AllowAutoRedirect = $true
    $req.UserAgent = 'RepRecorder/1.0'
    $resp = $req.GetResponse()
    Check 'the full-package link opens (no 404)' ([int]$resp.StatusCode -eq 200) ('status=' + [int]$resp.StatusCode + ' bytes=' + $resp.ContentLength)
    $resp.Close()
  } catch { Check 'the full-package link opens (no 404)' $false $_.Exception.Message }
} else {
  Check 'the full-package link opens (no 404)' $false 'FullPackage is empty in the manifest'
}

# Every advertised download link must actually serve bytes.  A missing or mis-named asset on a
# release shows up here as a plain 404, instead of surfacing later as a mysterious "update failed"
# (that is exactly how the v1.0 GitHub release was wrong: tag named in Chinese, update package
# never attached, full package uploaded under the name RepRecorder-.zip).
foreach ($l in $j.DownloadLinks) {
  $kind = '' + $l.Type
  $okHead = $false; $info = ''
  try {
    $req = [Net.WebRequest]::Create($l.Url)
    $req.Method = 'HEAD'; $req.Timeout = 30000; $req.AllowAutoRedirect = $true; $req.UserAgent = 'RepRecorder/1.0'
    $resp = $req.GetResponse()
    $okHead = ([int]$resp.StatusCode -eq 200)
    $info = 'status=' + [int]$resp.StatusCode + ' bytes=' + $resp.ContentLength
    $resp.Close()
  } catch { $info = $_.Exception.Message }
  Check ($kind + ': the advertised download link really serves bytes') $okHead $info
}

# ---------------------------------------------------------------- 3) two real install passes
function RunPass([string]$name, $links) {
  $app = Join-Path $root ('app-' + $name)
  Remove-Item -LiteralPath $app -Recurse -Force -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force -Path $app | Out-Null
  $exe = Join-Path $app 'RepRecorderWPF.exe'
  Copy-Item $devExe $exe -Force
  $devHash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
  [pscustomobject]@{ GameDir = ''; ObsExe = ''; FfmpegExe = ''; EnvChecked = $true; LastUpdateCheckUtc = ''; SkippedUpdateVersion = '' } |
    ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $app 'RepRecorder.settings.json') -Encoding UTF8

  # The fixture keeps the REAL sha and the REAL urls; only the version is raised so the app
  # sees an update.  NOTE: it must be $j (the parsed manifest), not $live (the raw JSON text) --
  # $live.Sha256 is silently $null, and the whole point of requiring a sha256 is that the app
  # then refuses to auto-apply.  That mistake made both passes "fail" while the app was fine.
  $fx = [pscustomobject]@{ Version = '9.9'; Notes = 'network self-test'; Sha256 = $j.Sha256; FullPackage = $j.FullPackage; DownloadLinks = $links }
  $fxPath = Join-Path $root ('manifest-' + $name + '.json')
  $fx | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $fxPath -Encoding UTF8
  $fxUrl = 'file:///' + $fxPath.Replace('\', '/')

  Start-Process -FilePath $exe -WorkingDirectory $app -ArgumentList @("--wpf", "--debug-port=$port", ("--update-url=" + $fxUrl)) | Out-Null
  for ($i = 0; $i -lt 50; $i++) { Start-Sleep -Milliseconds 400; if ((Dbg 'status') -ne $null) { break } }
  $shown = $false
  for ($i = 0; $i -lt 50; $i++) {
    Start-Sleep -Milliseconds 400
    if ((Dbg 'find:updateDialog' | ConvertFrom-Json).found -eq $true) { $shown = $true; break }
  }
  Check ($name + ': the update dialog appears') $shown ''

  # Wait for the BUTTON to come back enabled -- it happens exactly once, when the worker thread
  # is finished, success or failure.  (Waiting for the version number to show up in the log was
  # a fake green: the startup check logs it before anything is downloaded.)
  $t0 = Get-Date
  Dbg 'press:updateGo' | Out-Null
  $statusTxt = ''; $finished = $false
  for ($i = 0; $i -lt 240; $i++) {
    Start-Sleep -Milliseconds 500
    $enabled = (Dbg 'find:updateGo' | ConvertFrom-Json).enabled
    $statusTxt = (Dbg 'text:updateStatus' | ConvertFrom-Json).text
    if (($enabled -eq $true) -and ($statusTxt -ne '') -and ($statusTxt -notmatch $cnBusy)) { $finished = $true; break }
  }
  $secs = [int]((Get-Date) - $t0).TotalSeconds
  $log = Get-Content (Join-Path $app 'RepRecorder.log') -Raw -Encoding UTF8
  Check ($name + ': the download finished instead of hanging') $finished ('after ' + $secs + 's')
  Check ($name + ': the app says it updated (not "update failed")') ($statusTxt -match $cnOkt) ('status len=' + $statusTxt.Length)
  $newHash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
  # Gate on .old: without a swap the file trivially equals itself, so a bare hash comparison
  # stays green even when nothing happened (it did, on the very first run of this script).
  Check ($name + ': the installed exe was really replaced') ((Test-Path ($exe + '.old')) -and ($newHash -eq $devHash)) ('sha=' + $newHash.Substring(0, 12) + '...')
  Check ($name + ': the old exe was kept as .old') (Test-Path ($exe + '.old')) ''
  Check ($name + ': the readme files came along') ((Get-ChildItem $app -Filter '*.md').Count -ge 1) ''
  Check ($name + ': the log says the update completed') ($log -match $cnDone) ''

  # The console prints Chinese as mojibake under this shell's code page, so keep the two lines
  # that matter (and the log) in a utf-8 file to read afterwards.
  Set-Content -LiteralPath (Join-Path $root ('outcome-' + $name + '.txt')) -Encoding UTF8 -Value @(
    ('case: ' + $name), ('status: ' + $statusTxt), '--- log ---', $log)

  # Restart button.  Do NOT probe it over the debug port: Restart() relaunches the exe with no
  # arguments (correct for real users), so the new process has no debug port.
  $t1 = Get-Date
  Dbg 'press:updateGo' | Out-Null
  $restarted = $false; $newPath = ''
  for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 500
    $ps = @(Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt $t1 })
    if ($ps.Count -ge 1) { $restarted = $true; try { $newPath = $ps[0].Path } catch { }; break }
  }
  Check ($name + ': the restart button brings the app back up') $restarted ''
  $leaf = ''
  if ($newPath.Length -gt 0) { $leaf = Split-Path -Leaf $newPath }
  Check ($name + ': and it started the replaced exe') ($newPath -eq $exe) ('path=' + $leaf)
  Check ($name + ': the restarted app got through startup') ((Get-Content (Join-Path $app 'RepRecorder.log') -Raw -Encoding UTF8) -match $cnBoot) ''
  StopApp
}

Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 500
New-Item -ItemType Directory -Force -Path $root | Out-Null

RunPass 'published' $j.DownloadLinks
RunPass 'github'    $gh

Write-Host ''
if ($fail -eq 0) { Write-Host 'NETCHECK: PASS' } else { Write-Host 'NETCHECK: FAIL' }
