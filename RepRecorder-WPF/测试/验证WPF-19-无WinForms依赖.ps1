# 验证WPF-19: the WPF build must not depend on System.Windows.Forms, in source or in the binary.
#
# Why this script exists: on 2026-10-12 the old WinForms UI (MainForm + a dozen hand-drawn
# control classes, ~7600 lines) was removed from Core.cs. The old program survives as a
# standalone snapshot in a sibling folder of the pack (source + a runnable exe) -- that is a
# souvenir, not a dependency. Nothing on the WPF path may reach back into WinForms. This
# script is the gate that keeps it that way: if someone re-adds a `using System.Windows.Forms;`
# or puts the DLL back on the compiler command line, this turns red.
#
# ASCII only (PowerShell 5.1 reads BOM-less files as ANSI). Paths come from $PSScriptRoot --
# and that rule means no non-ASCII literals here, so the build script is located by its
# contents rather than by its (non-ASCII) name.
$here = $PSScriptRoot                  # this script lives in the tests folder
$dir  = Split-Path $here -Parent       # the app folder: the exe and its log/settings live there
$src  = Join-Path $dir 'src'
$exe  = Join-Path $dir 'RepRecorderWPF.exe'
$log  = Join-Path $dir 'RepRecorder.log'
$fail = 0

# the build script (a .bat calling csc.exe) somewhere under the app folder
$bat = Get-ChildItem -LiteralPath $dir -Filter '*.bat' -Recurse -ErrorAction SilentlyContinue |
       Where-Object { (Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue) -match 'target:winexe' } |
       Select-Object -First 1 -ExpandProperty FullName

function Check([string]$what, [bool]$ok, [string]$detail) {
  if ($ok) { Write-Host ("OK  {0}  {1}" -f $what, $detail) }
  else { Write-Host ("FAIL {0}  {1}" -f $what, $detail); $script:fail = 1 }
}

# ---------------------------------------------------------------- 1) the build command line
if (-not (Test-Path $bat)) { Write-Host ('FAIL: build script not found: ' + $bat); exit 1 }
$batTxt = Get-Content $bat -Raw
Check 'build script does not reference System.Windows.Forms' `
      ($batTxt -notmatch 'System\.Windows\.Forms') ''

# ---------------------------------------------------------------- 2) the sources
# A line that is a comment does not count as a dependency.  Two comments deliberately mention
# System.Windows.Forms as a warning to future readers ("do not bring this back"); those are
# not references, so comment lines are stripped before looking.
$codeHits = @()
foreach ($f in (Get-ChildItem -LiteralPath $src -Filter '*.cs')) {
  $n = 0
  foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
    $n++
    $t = $line.TrimStart()
    if ($t.StartsWith('//')) { continue }
    if ($t -match 'System\.Windows\.Forms' -or $t -match 'System\.Windows\.Forms\.') {
      $codeHits += ('{0}:{1}: {2}' -f $f.Name, $n, $t)
    }
  }
}
Check 'no code line in src/ uses System.Windows.Forms' ($codeHits.Count -eq 0) `
      ('hits=' + $codeHits.Count)
foreach ($h in $codeHits) { Write-Host ('      ' + $h) }

# no leftover `using System.Windows.Forms;` either (that is a code line, covered above, but
# state it separately because it is the mistake people actually make)
$usingHits = Select-String -Path (Join-Path $src '*.cs') -Pattern '^\s*using\s+System\.Windows\.Forms\s*;' -ErrorAction SilentlyContinue
Check 'no `using System.Windows.Forms;` anywhere' ($null -eq $usingHits) `
      ('hits=' + $(if ($usingHits) { $usingHits.Count } else { 0 }))

# ---------------------------------------------------------------- 3) the two files the split added
foreach ($pair in @(@('Palette.cs', 'palette (colors moved out of the old MainForm)'),
                    @('FolderPicker.cs', 'folder picker (replaces WinForms FolderBrowserDialog)'))) {
  $p = Join-Path $src $pair[0]
  Check ($pair[1] + ' exists') (Test-Path $p) ''
  Check ($pair[0] + ' is in the build command line') ($batTxt -match [regex]::Escape($pair[0])) ''
}

# ---------------------------------------------------------------- 4) the built binary
if (-not (Test-Path $exe)) { Write-Host 'FAIL: RepRecorderWPF.exe not found (build first)'; exit 1 }
$refs = @()
try {
  $asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($exe)
  $refs = @($asm.GetReferencedAssemblies() | ForEach-Object { $_.Name })
} catch {
  Write-Host ('FAIL: could not read the assembly references: ' + $_.Exception.Message)
  $fail = 1
}
if ($refs.Count -gt 0) {
  Check 'the built exe does not reference System.Windows.Forms' `
        (-not ($refs -contains 'System.Windows.Forms')) ('refs=' + ($refs -join ','))
  # and it should still reference the WPF assemblies it needs
  Check 'the built exe still references PresentationFramework' `
        ($refs -contains 'PresentationFramework') ''
}

# ---------------------------------------------------------------- 5) it still runs, and --old is inert
Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 500
if (Test-Path $log) { Remove-Item $log -Force }

# --old used to switch to the old WinForms UI.  That UI is gone; the flag must be swallowed
# rather than handed to the queue as a file path (Recorder.AddPaths treats every argument as
# a dropped .rep), so no "old" may show up in the log at all.
$p = Start-Process -FilePath $exe -ArgumentList @('--old') -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
  Start-Sleep -Milliseconds 250
  $p.Refresh()
  if ($p.HasExited) { Write-Host ('FAIL: process exited early, code={0}' -f $p.ExitCode); exit 1 }
  if ($p.MainWindowHandle -ne 0) { $h = $p.MainWindowHandle; break }
}
Check 'main window appeared even when started with --old' ($h -ne [IntPtr]::Zero) ''

Start-Sleep -Seconds 2
$txt = ''
if (Test-Path $log) { $txt = Get-Content $log -Raw -Encoding UTF8 }
Check '--old is swallowed, not treated as a dropped file' ($txt -notmatch 'old') ''
Check 'the startup log says WPF' ($txt -match 'WPF') ''

try { Stop-Process -Id $p.Id -Force } catch { }

if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
