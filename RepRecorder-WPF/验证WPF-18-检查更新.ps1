# verify-WPF-18: the built-in update check (2026-10-10)
# ASCII ONLY + CRLF.  PowerShell 5.1 reads a BOM-less UTF-8 script as ANSI; a Chinese byte
# pair ending a comment can swallow the newline and break the whole file's parse.
#
# Sections, in the order the plan builds them:
#   1. version comparison                  (cmpver)          -- Task 1
#   2. manifest fetching + validation      (updatecheck)     -- Task 2
#   3. the startup prompt                  (updateDialog)    -- Task 3
#   4. download + swap with rollback       (updateapply)     -- Task 4
#   5. the download button                                    -- Task 5
#   6. the release script                  (release --check) -- Task 6
#
# Everything below runs against an ISOLATED copy of the exe (D:\_reptest18\app), never the
# developer's own build, because several of these assertions deliberately damage files.
$root = 'D:\_reptest18'
$app  = Join-Path $root 'app'
$exe  = Join-Path $app 'RepRecorderWPF.exe'
$cfg  = Join-Path $app 'RepRecorder.settings.json'
$port = 7804
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
function StartAppWith([string]$updateUrl) {
  # NOTE: the local list must NOT be called $args -- that is an automatic variable.
  $argList = @('--wpf', "--debug-port=$port")
  if ($updateUrl -ne '') { $argList += ('--update-url=' + $updateUrl) }
  $p = Start-Process -FilePath $exe -WorkingDirectory $app -ArgumentList $argList -PassThru
  for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 400; if ((Dbg 'status') -ne $null) { return $p } }
  return $p
}
function StartApp { return StartAppWith '' }

# ---------------------------------------------------------------- setup
Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 500
Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $app | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'RepRecorderWPF.exe') $exe -Force
[pscustomobject]@{ GameDir = ''; ObsExe = ''; FfmpegExe = ''; EnvChecked = $true } |
  ConvertTo-Json -Compress | Set-Content -LiteralPath $cfg -Encoding UTF8
$p = StartApp

# ============ 1) version comparison ==================================================
# The single most likely thing to get wrong: comparing versions as strings, where "1.10"
# sorts BELOW "1.9".  Everything here is written to fail if that ever comes back.
$r = (Dbg 'cmpver:1.10|1.9' | ConvertFrom-Json).result
Check '1.10 is newer than 1.9 (not a string compare)' ([int]$r -eq 1) ('r=' + $r)
$r = (Dbg 'cmpver:1.1|1.1.0.0' | ConvertFrom-Json).result
Check '1.1 equals 1.1.0.0' (($r -ne $null) -and ([int]$r -eq 0)) ('r=' + $r)
$r = (Dbg 'cmpver:2.0|1.9.9.9' | ConvertFrom-Json).result
Check '2.0 beats 1.9.9.9' ([int]$r -eq 1) ('r=' + $r)
$r = (Dbg 'cmpver:1.0|1.1' | ConvertFrom-Json).result
Check '1.0 is older than 1.1' ([int]$r -eq -1) ('r=' + $r)
$r = (Dbg 'cmpver:v1.1|1.1' | ConvertFrom-Json).result
Check 'a stray leading v is tolerated' (($r -ne $null) -and ([int]$r -eq 0)) ('r=' + $r)
$r = (Dbg 'cmpver:abc|1.0' | ConvertFrom-Json).result
Check 'a version with no digits is reported invalid' ([int]$r -eq -2147483648) ('r=' + $r)
$r = (Dbg 'cmpver:1.2.3|1.2' | ConvertFrom-Json).result
Check '1.2.3 beats 1.2' ([int]$r -eq 1) ('r=' + $r)

# ============ 2) manifest fetching + validation =======================================
# Manifests are fed through file:// so the checks never touch the network -- a slow or
# blocked Gitee must not turn into a red test.  Note that PowerShell 5.1's -Encoding UTF8
# writes a BOM, so every fixture here is BOM-prefixed: the parser has to cope with that
# (hand-written manifests stored as "UTF-8 with BOM" are the common case).
$mf = Join-Path $root 'manifests'
New-Item -ItemType Directory -Force -Path $mf | Out-Null
function Manifest([string]$name, [string]$body) {
  Set-Content -LiteralPath (Join-Path $mf $name) -Value $body -Encoding UTF8
  return 'file:///' + ((Join-Path $mf $name) -replace '\\','/')
}
$good = Manifest 'good.json' '{"Version":"9.9","Notes":"x","Sha256":"ab","DownloadLinks":[{"Type":"Gitee","Url":"https://example.invalid/a.zip"}]}'
$same = Manifest 'same.json' '{"Version":"1.0","Notes":"x","Sha256":"ab","DownloadLinks":[{"Type":"Gitee","Url":"https://example.invalid/a.zip"}]}'
$old  = Manifest 'old.json'  '{"Version":"0.9","Notes":"x","Sha256":"ab","DownloadLinks":[{"Type":"Gitee","Url":"https://example.invalid/a.zip"}]}'
$html = Manifest 'html.json' '<html><body>login required</body></html>'
$nov  = Manifest 'noversion.json' '{"Notes":"x"}'
$nourl= Manifest 'nourl.json' '{"Version":"9.9","Notes":"x","Sha256":"ab","DownloadLinks":[]}'
$trail= Manifest 'trailing.json' '{"Version":"9.9","Notes":"x","Sha256":"ab","DownloadLinks":[{"Type":"Gitee","Url":"https://example.invalid/a.zip"}],}'

Check 'a newer manifest is detected' ((Dbg ('updatecheck:' + $good) | ConvertFrom-Json).result -eq 'newer') ((Dbg ('updatecheck:' + $good) | ConvertFrom-Json).result)
Check 'the same version is not "newer"' ((Dbg ('updatecheck:' + $same) | ConvertFrom-Json).result -eq 'same') ((Dbg ('updatecheck:' + $same) | ConvertFrom-Json).result)
Check 'a lower version is never offered' ((Dbg ('updatecheck:' + $old) | ConvertFrom-Json).result -eq 'older') ((Dbg ('updatecheck:' + $old) | ConvertFrom-Json).result)
Check 'an HTML login page is not a manifest' ((Dbg ('updatecheck:' + $html) | ConvertFrom-Json).result -eq 'invalid') ((Dbg ('updatecheck:' + $html) | ConvertFrom-Json).result)
Check 'a manifest without Version is invalid' ((Dbg ('updatecheck:' + $nov) | ConvertFrom-Json).result -eq 'invalid') ((Dbg ('updatecheck:' + $nov) | ConvertFrom-Json).result)
Check 'a manifest without any URL is invalid' ((Dbg ('updatecheck:' + $nourl) | ConvertFrom-Json).result -eq 'invalid') ((Dbg ('updatecheck:' + $nourl) | ConvertFrom-Json).result)
# A hand-edited manifest with a trailing comma is NOT quietly tolerated -- JavaScriptSerializer
# rejects it, and the requirement is that the app says "invalid" (and logs it) rather than
# crashing or guessing.  Manifests are generated by the release script, so this only bites when
# someone edits the json by hand.
Check 'a trailing comma is rejected, not crashed on' ((Dbg ('updatecheck:' + $trail) | ConvertFrom-Json).result -eq 'invalid') ((Dbg ('updatecheck:' + $trail) | ConvertFrom-Json).result)
Check 'a missing file reports an error, it does not crash' (((Dbg 'updatecheck:file:///D:/_reptest18/nope.json' | ConvertFrom-Json).result -eq 'invalid') -and ((Dbg 'updatecheck:file:///D:/_reptest18/nope.json' | ConvertFrom-Json).error -ne $null)) ''
Check 'an empty url means "do not check"' ((Dbg 'updatecheck:' | ConvertFrom-Json).result -eq 'off') ''

# ============ 3) the startup prompt, and "later" meaning later =========================
# The app has to be started with --update-url pointing at our file:// manifest, otherwise it
# asks the (still empty) real one and nothing happens.
StopApp
$p = StartAppWith $good
# POLL for the prompt instead of sleeping a fixed 6 s: a freshly built exe can take a few
# seconds for Defender to scan, and a fixed wait turns that into a random red.
$shown = $false
for ($i = 0; $i -lt 50; $i++) {
  Start-Sleep -Milliseconds 400
  if ((Dbg 'find:updateDialog' | ConvertFrom-Json).found -eq $true) { $shown = $true; break }
}
Check 'the update dialog shows up when there is a newer version' $shown ''
$t = (Dbg 'text:updateVersion' | ConvertFrom-Json).text
Check 'it names the new version and the current one' (($t -like '*9.9*') -and ($t -like '*1.0*')) ('text=' + $t)
$wasOpen = (Dbg 'find:updateDialog' | ConvertFrom-Json).found -eq $true
Dbg 'press:updateLater' | Out-Null          # "later"
Start-Sleep -Seconds 1
Check 'the dialog closes' ($wasOpen -and ((Dbg 'find:updateDialog' | ConvertFrom-Json).found -eq $false)) ('wasOpen=' + $wasOpen)
Check 'the skipped version is remembered' ((Cfg 'SkippedUpdateVersion') -eq '9.9') ('v=' + (Cfg 'SkippedUpdateVersion'))
StopApp

# Reopen: the same version must not be offered AGAIN.
#  Clear the throttle first.  Without this the 6-hour gate skips the check entirely, so the
# dialog is absent for a reason that has nothing to do with "later" -- the assertion would
# stay green even if the whole skip feature were deleted.  (Caught in review: this was
# reported as "passing" but it was really testing the throttle, not the skip.)
Patch 'LastUpdateCheckUtc' ''
$p = StartAppWith $good
# Poll for the log line that proves a FRESH check ran and was skipped -- polling rather than a
# fixed sleep, so a slow start cannot turn this into a random red.  (The log rotates on
# startup, so this file holds only the second launch.)
$sawSkip = $false
for ($i = 0; $i -lt 50; $i++) {
  Start-Sleep -Milliseconds 400
  $lg = Get-Content (Join-Path $app 'RepRecorder.log') -Raw -Encoding UTF8
  if ($lg -match '9\.9') { $sawSkip = $true; break }
}
Check 'and the skip is what suppressed it (a fresh check really ran)' $sawSkip ''
Check 'the same version is not offered twice' ((Dbg 'find:updateDialog' | ConvertFrom-Json).found -eq $false) ''
StopApp

# ============ 4) download + swap, with rollback =======================================
# The app has to be running for the updateapply debug command (section 3 ended with StopApp).
$p = StartApp
# NEVER point this at the running program: the target here is a fake exe in a temp dir.
# The update package format is what the release script produces: RepRecorder.exe + *.md.
$tgtDir = Join-Path $root 'target'
New-Item -ItemType Directory -Force -Path $tgtDir | Out-Null
$tgt = Join-Path $tgtDir 'RepRecorder.exe'
Set-Content -LiteralPath $tgt -Value 'OLD' -Encoding ASCII

$srcDir = Join-Path $root 'src'
New-Item -ItemType Directory -Force -Path $srcDir | Out-Null
# The payload has to be a REAL PE image: Update.cs now refuses anything without an "MZ"
# header, so that a mis-packaged release cannot replace the user's program with a brick.
# Use a small genuine exe as the stand-in.
Copy-Item "$env:WINDIR\System32\where.exe" (Join-Path $srcDir 'RepRecorder.exe') -Force
Set-Content -LiteralPath (Join-Path $srcDir 'docs-1.md') -Value 'NEWDOC' -Encoding ASCII
$zipOk = Join-Path $root 'ok.zip'
Compress-Archive -Path (Join-Path $srcDir '*') -DestinationPath $zipOk -Force
$shaOk = (Get-FileHash $zipOk -Algorithm SHA256).Hash.ToLower()

$r = (Dbg ('updateapply:' + $zipOk + '|' + $tgt + '|' + $shaOk) | ConvertFrom-Json).result
Check 'a good update applies' ($r -eq 'ok') ('result=' + $r)
Check 'the target file now holds the new content' ((Get-FileHash $tgt -Algorithm SHA256).Hash -eq (Get-FileHash (Join-Path $srcDir 'RepRecorder.exe') -Algorithm SHA256).Hash) ''
Check 'the old file was kept as .old' (Test-Path ($tgt + '.old')) ''
# The real package's .md files have Chinese names; this script must stay pure ASCII, so
# match by extension rather than spelling them out.  (Source: the update zip is built by
# (built by the release script; the app copies whatever *.md it finds, keeping the name.)
$mdAfter = Get-ChildItem -LiteralPath $tgtDir -Filter '*.md' | Select-Object -First 1
Check 'the doc file was updated too' (($mdAfter -ne $null) -and ((Get-Content $mdAfter.FullName -Raw).Trim() -eq 'NEWDOC')) ''
Check 'no stray .new file was left behind' (-not (Test-Path ($tgt + '.new'))) ''

# A hash mismatch must change nothing at all.
Set-Content -LiteralPath $tgt -Value 'OLD2' -Encoding ASCII
Remove-Item ($tgt + '.old') -Force -ErrorAction SilentlyContinue
$zipBad = Join-Path $root 'bad.zip'
Copy-Item $zipOk $zipBad -Force
Add-Content -LiteralPath $zipBad -Value 'x'
$a = Dbg ('updateapply:' + $zipBad + '|' + $tgt + '|' + $shaOk) | ConvertFrom-Json
Check 'a hash mismatch is refused' (($a.result -ne 'ok') -and ($a.error -ne $null)) ('result=' + $a.result + ' err=' + $a.error)
Check 'and the target was not touched' ((Get-Content $tgt -Raw).Trim() -eq 'OLD2') ''
Check 'and no .old was left behind' (-not (Test-Path ($tgt + '.old'))) ''

# No expected hash (== manifest missing Sha256) must refuse auto-apply.
$a = Dbg ('updateapply:' + $zipOk + '|' + $tgt) | ConvertFrom-Json
Check 'a missing hash refuses auto-apply' (($a.result -ne 'ok') -and ($a.error -ne $null)) ('result=' + $a.result + ' err=' + $a.error)
Check 'and the target is still untouched' ((Get-Content $tgt -Raw).Trim() -eq 'OLD2') ''

# A target in a missing directory fails cleanly.
# NOTE: this covers the "parent dir does not exist" case (File.Copy / File.Create throws).
# A truly read-only ACL (icacls /deny) is not simulated here -- it goes through the same
# catch path, but that is inference, not something this script verifies.
$a = Dbg ('updateapply:' + $zipOk + '|D:\_reptest18\nosuchdir\x.exe|' + $shaOk) | ConvertFrom-Json
Check 'a target in a missing directory fails cleanly' (($a.result -ne 'ok') -and ($a.error -ne $null)) ('result=' + $a.result + ' err=' + $a.error)

# Spaces and Chinese in the path are the NORMAL case for this project, not an edge case.
# The repo root's own name contains Chinese, so derive the hostile path from it rather than
# spelling Chinese out here (this script must stay pure ASCII).
$cn = Join-Path (Split-Path $PSScriptRoot -Parent) '_reptest18 dir with spaces'
New-Item -ItemType Directory -Force -Path $cn | Out-Null
$tgt2 = Join-Path $cn 'RepRecorder.exe'
Set-Content -LiteralPath $tgt2 -Value 'OLD3' -Encoding ASCII
$r = (Dbg ('updateapply:' + $zipOk + '|' + $tgt2 + '|' + $shaOk) | ConvertFrom-Json).result
Check 'spaces and Chinese in the path are fine' ($r -eq 'ok') ('result=' + $r)
Check 'and it really landed there' ((Get-FileHash $tgt2 -Algorithm SHA256).Hash -eq (Get-FileHash (Join-Path $srcDir 'RepRecorder.exe') -Algorithm SHA256).Hash) ''

# A package whose RepRecorder.exe is not a real program (mis-packaged release, wrong file
# uploaded) must be refused -- otherwise the app replaces itself with a brick.
$nonPe = Join-Path $root 'src-nonpe'
New-Item -ItemType Directory -Force -Path $nonPe | Out-Null
Set-Content -LiteralPath (Join-Path $nonPe 'RepRecorder.exe') -Value 'not a program' -Encoding ASCII
$zipNonPe = Join-Path $root 'nonpe.zip'
Compress-Archive -Path (Join-Path $nonPe '*') -DestinationPath $zipNonPe -Force
$shaNonPe = (Get-FileHash $zipNonPe -Algorithm SHA256).Hash.ToLower()
Set-Content -LiteralPath $tgt -Value 'OLD5' -Encoding ASCII
$a = Dbg ('updateapply:' + $zipNonPe + '|' + $tgt + '|' + $shaNonPe) | ConvertFrom-Json
Check 'a package whose payload is not a real program is refused' (($a.result -ne 'ok') -and ($a.error -ne $null)) ('result=' + $a.result + ' err=' + $a.error)
Check 'and the target is still untouched' ((Get-Content $tgt -Raw).Trim() -eq 'OLD5') ''

# ============ 5) the download button ==================================================
# The swap itself is covered in section 4 (with a temp-dir target).  This only pins the
# WIRING: point it at a download that cannot work and require the window to survive, come
# back to life, and say something a person can act on.
$badDl = Manifest 'bad-dl.json' '{"Version":"9.9","Notes":"x","Sha256":"ab","DownloadLinks":[{"Type":"Gitee","Url":"file:///D:/_reptest18/nosuch.zip"}]}'
StopApp
# Section 3 left two bits of state behind that would silently suppress the prompt here:
# it pressed "later" (so 9.9 is remembered as skipped) and it recorded a check timestamp (so
# the 6-hour throttle kicks in).  Clear both, or this section tests nothing.
Patch 'LastUpdateCheckUtc' ''
Patch 'SkippedUpdateVersion' ''
$p = StartAppWith $badDl
$shown2 = $false
for ($i = 0; $i -lt 50; $i++) {
  Start-Sleep -Milliseconds 400
  if ((Dbg 'find:updateDialog' | ConvertFrom-Json).found -eq $true) { $shown2 = $true; break }
}
Check 'the dialog still appears when the download url is broken' $shown2 ''
Dbg 'press:updateGo' | Out-Null
Start-Sleep -Seconds 4
Check 'the app is still alive after a failed download' ((Dbg 'status') -ne $null) ''
$t = (Dbg 'text:updateStatus' | ConvertFrom-Json).text
Check 'it says something human about the failure' (($t -ne $null) -and ($t.Length -gt 0) -and ($t -notlike '*Exception*')) ('text=' + $t)
Check 'the button is usable again' ((Dbg 'find:updateGo' | ConvertFrom-Json).enabled -eq $true) ''
# This manifest's URL is file:// --  must refuse anything that is not http(s)
# instead of handing the string to the shell (a tampered manifest could point at a local file).
Dbg 'press:updatePage' | Out-Null
Start-Sleep -Milliseconds 600
$t2 = (Dbg 'text:updateStatus' | ConvertFrom-Json).text
Check 'a non-http download page is refused, and says so' (($t2 -ne $null) -and ($t2 -like '*file:*')) ('text=' + $t2)
StopApp

# ============ 6) the release script ===================================================
# --check does the "does not touch anything" half: validate the version, show what it WOULD
# patch, and produce the update package + manifest from the current build.  No rebuild, no
# sync, no source edits -- so it is safe to run here.
$dir = $PSScriptRoot
#  This whole script must stay pure ASCII (PS 5.1 reads a BOM-less UTF-8 file as ANSI, and a
# Chinese literal swallows the quote that follows it -- that already cost two debugging
# rounds).  The release script's own names are Chinese, so build them from code points.
function CN([int[]]$cp) { -join ($cp | ForEach-Object { [char]$_ }) }
# NOTE: build each Chinese name into a variable FIRST and hand only that to Join-Path.
# "Join-Path A '_ + B" does NOT parse the obvious way -- the + is taken as another
# positional argument, the call fails, and every path below silently becomes $null.
$relName   = '_' + (CN 0x53D1,0x7248)
# Point the release script at a throwaway output dir.  Without this, running this test
# OVERWRITES the real release materials with ones made for the test's fake version number --
# which then claim 1.1 while holding the 1.0 exe, so publishing them makes every user see
# "new version available" forever.  (Found for real: the manifest left behind by a test run
# said 1.1.)
$rel                = Join-Path $root 'release-out'
$env:RELEASE_OUT    = $rel
Remove-Item -LiteralPath $rel -Recurse -Force -ErrorAction SilentlyContinue
$rel       = Join-Path (Split-Path $dir -Parent) $relName
$relBat    = Join-Path $dir ((CN 0x53D1,0x7248) + '.bat')
$updName   = 'RepRecorder-1.1-' + (CN 0x66F4,0x65B0,0x5305) + '.zip'
$stepsName = (CN 0x7167,0x7740,0x505A) + '-' + (CN 0x53D1,0x7248) + '.txt'
$stepsTxt  = Join-Path $rel $stepsName
$updZip    = Join-Path $rel $updName
$jsonPath  = Join-Path $rel 'version.json'

$before = (Get-FileHash (Join-Path $dir 'Core.cs') -Algorithm SHA256).Hash
$beforeAsm = (Get-FileHash (Join-Path $dir 'AssemblyInfo.cs') -Algorithm SHA256).Hash
$out = & cmd.exe /c "`"$relBat`" --check 1.1" 2>&1 | Out-String
Check 'check mode does not touch Core.cs' ($before -eq (Get-FileHash (Join-Path $dir 'Core.cs') -Algorithm SHA256).Hash) ''
Check 'check mode does not touch AssemblyInfo.cs' ($beforeAsm -eq (Get-FileHash (Join-Path $dir 'AssemblyInfo.cs') -Algorithm SHA256).Hash) ''
Check 'it reports what it would change' ($out -match 'AppInfo.Version') ''

Check 'it produced the update package' (Test-Path $updZip) ''
Check 'it produced a version.json' (Test-Path $jsonPath) ''
Check 'it produced the checklist' (Test-Path $stepsTxt) ''

# The manifest must be readable by the app itself -- read it back the same way Update.cs does.
$j = Get-Content $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
Check 'the manifest declares the new version' (($j -ne $null) -and ($j.Version -eq '1.1')) ('v=' + $j.Version)
Check 'the manifest carries a sha256' (($j -ne $null) -and ($j.Sha256.Length -eq 64)) ('sha=' + $j.Sha256)
Check 'the sha256 matches the package it just built' `
      (($j -ne $null) -and ($j.Sha256 -eq (Get-FileHash $updZip -Algorithm SHA256).Hash.ToLower())) ''
Check 'the github link is present' (($j -ne $null) -and (($j.DownloadLinks | Where-Object { $_.Type -eq 'GitHub' }).Url.Length -gt 0)) ''
# The Gitee repo now exists (2026-10-10) and .bat has GITEE set, so the manifest must carry
# a Gitee link -- that link is the whole reason for hosting there (fast in China).
# (Before the repo existed this asserted the opposite; it flipped exactly as advertised.)
Check 'the manifest carries a gitee link now that the repo exists' (($j -ne $null) -and (($j.DownloadLinks | Where-Object { $_.Type -eq 'Gitee' }).Url.Length -gt 0)) ''

# The single most valuable assertion here: the update package must NOT drag ffmpeg along
# (217 MB) -- that would turn "one-click update" into an 85 MB download.
$updBytes = if (Test-Path $updZip) { (Get-Item $updZip).Length } else { -1 }
Check 'the update package stays small (it must not carry ffmpeg)' (($updBytes -gt 0) -and ($updBytes -lt 20MB)) ('bytes=' + $updBytes)

# Pin the package's INTERNAL layout: the updater only accepts RepRecorder.exe at the ZIP ROOT,
# and section 4 builds its fixture a different way than the release script does -- so without
# this, "the release script's package is the shape the updater wants" is only assumed.
Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
$z = [System.IO.Compression.ZipFile]::OpenRead($updZip)
try { $names = @($z.Entries | ForEach-Object { $_.FullName }) } finally { $z.Dispose() }
Check 'the package has RepRecorder.exe at its root' ($names -contains 'RepRecorder.exe') ('entries=' + ($names -join ','))
Check 'and it carries the docs but nothing bulky' (($names.Count -ge 2) -and ($names.Count -le 4)) ('n=' + $names.Count)

# ---------------------------------------------------------------- teardown
StopApp
Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $cn -Recurse -Force -ErrorAction SilentlyContinue   # lives outside $root
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot '_dump-utf8-18.json'), ($report | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
