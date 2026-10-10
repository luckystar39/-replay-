# 验证WPF-00: the new project builds and the exe starts up like the old one.
# ASCII only (PowerShell 5.1 reads BOM-less files as ANSI). Paths come from $PSScriptRoot.
$dir = $PSScriptRoot
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$log = Join-Path $dir 'RepRecorder.log'
$fail = 0

if (-not (Test-Path $exe)) { Write-Host 'FAIL: RepRecorderWPF.exe not found (build first)'; exit 1 }
Write-Host ('OK  exe exists: {0} bytes' -f (Get-Item $exe).Length)

# 1.0 (2026-10-10): the version has to agree in three places -- the exe's own metadata (what
# Explorer shows), the first line of the log, and the window title.  The source constant is
# read here rather than hardcoding, so bumping the version does not mean editing this script.
$appver = ([regex]::Match((Get-Content (Join-Path $dir 'Core.cs') -Raw -Encoding UTF8),
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

try { Stop-Process -Id $p.Id -Force } catch { }
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
