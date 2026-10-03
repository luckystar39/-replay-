# 验证WPF-00: the new project builds and the exe starts up like the old one.
# ASCII only (PowerShell 5.1 reads BOM-less files as ANSI). Paths come from $PSScriptRoot.
$dir = $PSScriptRoot
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$log = Join-Path $dir 'RepRecorder.log'
$fail = 0

if (-not (Test-Path $exe)) { Write-Host 'FAIL: RepRecorderWPF.exe not found (build first)'; exit 1 }
Write-Host ('OK  exe exists: {0} bytes' -f (Get-Item $exe).Length)

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

try { Stop-Process -Id $p.Id -Force } catch { }
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
