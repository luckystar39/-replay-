# verify-WPF-07: the background settings dialog (phase 4's last piece)
#   opens from the top bar, edits live, writes through to cfg + the rendered layer,
#   and 取消 puts everything back.
# ASCII only. Run with: powershell -File <this>   (never pipe its stdout)
$dir = $PSScriptRoot
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$cfg = Join-Path $dir 'RepRecorder.settings.json'
$bak = Join-Path $dir '_settings-backup.json'
$runLog = Join-Path $dir '_run07.log'
$port = 7788
$fail = 0
Remove-Item $runLog -ErrorAction SilentlyContinue

function Dbg([string]$cmd) {
  try {
    $c = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $port)
    $c.ReceiveTimeout = 4000
    $w = New-Object System.IO.StreamWriter($c.GetStream()); $w.AutoFlush = $true; $w.WriteLine($cmd)
    $r = New-Object System.IO.StreamReader($c.GetStream()); $out = $r.ReadLine(); $c.Close()
    return $out
  } catch { return $null }
}
function Check([string]$what, [bool]$ok, [string]$detail) {
  $line = $(if ($ok) { 'OK   ' } else { 'FAIL ' }) + $what + '  ' + $detail
  Write-Host $line
  Add-Content -LiteralPath $runLog -Value $line -Encoding UTF8
  if (-not $ok) { $script:fail = 1 }
}
function CfgVal([string]$key) {
  try { return (Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json).$key } catch { return $null }
}

Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 600
Copy-Item $cfg $bak -Force
$opBefore = CfgVal 'BgOpacity'
$scopeBefore = CfgVal 'BgScope'
Write-Host ("before: BgOpacity=$opBefore BgScope=$scopeBefore")

Start-Sleep -Seconds 3      # 新 exe 先让 Defender 扫完（不然 Start-Process 会卡）
$p = Start-Process -FilePath $exe -ArgumentList @('--wpf', "--debug-port=$port") -PassThru
# 等端口起来（新 exe 要先被 Defender 扫一遍，固定 sleep 不够稳）
$up = $false
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Milliseconds 1000
  $probe = Dbg 'status'
  if ($probe -ne $null) { $up = $true; break }
  $p.Refresh(); if ($p.HasExited) { break }
}
Check 'app alive' ($up -and -not $p.HasExited) ('pid=' + $p.Id + ' waited=' + $i + 's')

$bg = Dbg 'bgstate' | ConvertFrom-Json
Check 'bgstate answers' ($bg -ne $null) ''
Check 'background image loaded' ($bg.image -eq $true) ('pixels=' + $bg.pixels)
Check 'layer opacity matches settings' ([int]$bg.opacity -eq [int]$opBefore) ('layer=' + $bg.opacity + ' cfg=' + $opBefore)

# --- open the dialog from the top bar ---
$r = Dbg 'press:btnBg' | ConvertFrom-Json
Start-Sleep -Milliseconds 800
Check 'press:btnBg ok' ($r.ok -eq $true) ('ok=' + $r.ok)
Check 'dialog is open' ((Dbg 'find:bgDialog' | ConvertFrom-Json).found -eq $true) ''
foreach ($id in @('bgImage','bgBrowse','bgClear','bgOpacity','bgBlur','bgOffX','bgOffY','bgLayout','bgScope','bgOk','bgCancel')) {
  $f = Dbg "find:$id" | ConvertFrom-Json
  Check ("control " + $id) ($f.found -eq $true) ('type=' + $f.type)
}

# --- live edit: opacity ---
$r = Dbg 'set:bgOpacity=60' | ConvertFrom-Json
Start-Sleep -Milliseconds 600
$bg = Dbg 'bgstate' | ConvertFrom-Json
Check 'opacity 60 reaches the layer' ([int]$bg.opacity -eq 60) ('layer=' + $bg.opacity)

# --- live edit: scope ---
$r = Dbg 'set:bgScope=1' | ConvertFrom-Json
Start-Sleep -Milliseconds 600
$bg = Dbg 'bgstate' | ConvertFrom-Json
Check 'scope switches to global' ($bg.scope -eq 'global') ('scope=' + $bg.scope)

# --- live edit: layout ---
$r = Dbg 'set:bgLayout=2' | ConvertFrom-Json
Start-Sleep -Milliseconds 600
$bg = Dbg 'bgstate' | ConvertFrom-Json
Check 'layout switches to stretch' ($bg.stretch -eq 'Fill') ('stretch=' + $bg.stretch)

# --- 取消 must put everything back ---
$r = Dbg 'press:bgCancel' | ConvertFrom-Json
Start-Sleep -Milliseconds 800
$bg = Dbg 'bgstate' | ConvertFrom-Json
Check 'cancel restores opacity' ([int]$bg.opacity -eq [int]$opBefore) ('layer=' + $bg.opacity + ' before=' + $opBefore)
Check 'cancel restores scope' ($bg.scope -eq $scopeBefore) ('scope=' + $bg.scope + ' before=' + $scopeBefore)
Check 'cancel restores layout' ($bg.stretch -eq 'UniformToFill') ('stretch=' + $bg.stretch)
Check 'settings file untouched by cancel' ([int](CfgVal 'BgOpacity') -eq [int]$opBefore) ('file=' + (CfgVal 'BgOpacity'))

# --- 确定 must persist ---
Dbg 'press:btnBg' | Out-Null
Start-Sleep -Milliseconds 800
Dbg 'set:bgOpacity=42' | Out-Null
Start-Sleep -Milliseconds 400
Dbg 'press:bgOk' | Out-Null
Start-Sleep -Milliseconds 800
Check 'OK writes the settings file' ([int](CfgVal 'BgOpacity') -eq 42) ('file=' + (CfgVal 'BgOpacity'))
Check 'log records the background change' ((Get-Content (Join-Path $dir 'RepRecorder.log') -Raw -Encoding UTF8) -match '42') ''
Check 'no exception in log' ((Get-Content (Join-Path $dir 'RepRecorder.log') -Raw -Encoding UTF8) -notmatch 'Exception') ''

try { Stop-Process -Id $p.Id -Force } catch { }
Start-Sleep -Milliseconds 500
Copy-Item $bak $cfg -Force
Remove-Item $bak -Force
Write-Host 'settings restored'
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
