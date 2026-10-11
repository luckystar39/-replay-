# 验证WPF-00: the new project builds and the exe starts up like the old one.
# ASCII only (PowerShell 5.1 reads BOM-less files as ANSI). Paths come from $PSScriptRoot.
$here = $PSScriptRoot                  # this script lives in the tests folder
$dir  = Split-Path $here -Parent       # the app folder: the exe and its log/settings live there
$src  = Join-Path $dir 'src'   # source code folder
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$log = Join-Path $dir 'RepRecorder.log'
$fail = 0

if (-not (Test-Path $exe)) { Write-Host 'FAIL: RepRecorderWPF.exe not found (build first)'; exit 1 }
Write-Host ('OK  exe exists: {0} bytes' -f (Get-Item $exe).Length)

# 1.0 (2026-10-10): the version has to agree in three places -- the exe's own metadata (what
# Explorer shows), the first line of the log, and the window title.  The source constant is
# read here rather than hardcoding, so bumping the version does not mean editing this script.
$appver = ([regex]::Match((Get-Content (Join-Path $src 'Core.cs') -Raw -Encoding UTF8),
            'const string Version = "([^"]+)"').Groups[1].Value)
if ($appver -eq '') { Write-Host 'FAIL: could not read AppInfo.Version out of Core.cs'; $fail = 1 }
else {
  Write-Host ('OK  source version = ' + $appver)
  $fv = (Get-Item $exe).VersionInfo.FileVersion
  if ($fv -like ($appver + '*')) { Write-Host ('OK  exe metadata version = ' + $fv) }
  else { Write-Host ('FAIL: exe FileVersion is ' + $fv + ', expected it to start with ' + $appver); $fail = 1 }
  if ((Get-Item $exe).VersionInfo.FileDescription -ne '') { Write-Host 'OK  exe has a description' }
  else { Write-Host 'FAIL: exe has no FileDescription (Explorer would show a blank)'; $fail = 1 }
}

Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 500
if (Test-Path $log) { Remove-Item $log -Force }

$p = Start-Process -FilePath $exe -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
  Start-Sleep -Milliseconds 250
  $p.Refresh()
  if ($p.HasExited) { Write-Host ('FAIL: process exited early, code={0}' -f $p.ExitCode); exit 1 }
  if ($p.MainWindowHandle -ne 0) { $h = $p.MainWindowHandle; break }
}
if ($h -eq [IntPtr]::Zero) { Write-Host 'FAIL: no main window after 10 s'; $fail = 1 }
else { Write-Host 'OK  main window appeared' }

Start-Sleep -Seconds 2
$txt = ''
if (Test-Path $log) { $txt = Get-Content $log -Raw -Encoding UTF8 }
foreach ($needle in @('Rep', 'OBS', 'BGM')) {
  if ($txt -match $needle) { Write-Host ("OK  log contains '{0}'" -f $needle) }
  else { Write-Host ("FAIL: log missing '{0}'" -f $needle); $fail = 1 }
}
# The startup line carries the version -- that is the whole point of putting it there: when
# someone sends you a log, the first line says which build they are running.
if ($appver -ne '' -and $txt -match ('v' + [regex]::Escape($appver))) {
  Write-Host 'OK  the log states the version'
} else {
  Write-Host ('FAIL: log does not state version ' + $appver); $fail = 1
}

# ---------------------------------------------------------------- no Low-integrity exe
# 2026-10-11, user rule: "every exe must be Medium, never Low".  A Low exe runs as a low-integrity
# process, and such a process cannot create files in a %TEMP% that carries a no-write-up label --
# that is exactly how the one-click update failed on 2026-10-10 (the A/B proof is in 验证WPF-18
# section 8: same exe, same manifest, only the label differs -> Low fails, Medium installs).
# NOTE: a Low file CANNOT be fixed from here -- changing a label needs WRITE_OWNER, which this
# account does not have in the Low part of the pack tree -- so the gate refuses and prints the
# one-time elevated command instead of trying.
function IntLabel($path) {
  $l = (icacls $path 2>&1 | Where-Object { $_ -match 'Mandatory Label' }) -join ''
  if ($l -match 'Low') { return 'LOW' }
  if ($l -match 'Medium') { return 'Medium' }
  if ($l -eq '') { return 'no label (unrestricted)' }
  return 'other'
}
$elevHint = '      fix once from an elevated prompt:  icacls "<pack root>" /setintegritylevel (OI)(CI)Medium /T /C'
$devExe = $exe
$devLab = IntLabel $devExe
if ($devLab -eq 'LOW') {
  Write-Host 'FAIL: the exe just built is LOW integrity -- anyone running it cannot self-update'
  Write-Host $elevHint
  $fail = 1
} else {
  Write-Host ('OK  the built exe is not low integrity ({0})' -f $devLab)
}
# The copy that actually reaches users (a folder holding exactly RepRecorder.exe), if there is one.
$distDir = Get-ChildItem -LiteralPath (Split-Path $dir -Parent) -Directory -ErrorAction SilentlyContinue |
           Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'RepRecorder.exe') } | Select-Object -First 1
if ($distDir -ne $null) {
  $distExe = Join-Path $distDir.FullName 'RepRecorder.exe'
  $distLab = IntLabel $distExe
  if ($distLab -eq 'LOW') {
    Write-Host ('FAIL: the file that is handed to users is LOW integrity: ' + $distExe)
    Write-Host $elevHint
    $fail = 1
  } else {
    Write-Host ('OK  the distributed exe is not low integrity ({0})' -f $distLab)
  }
} else {
  Write-Host 'OK  no distributed copy in this tree to check'
}

try { Stop-Process -Id $p.Id -Force } catch { }
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
