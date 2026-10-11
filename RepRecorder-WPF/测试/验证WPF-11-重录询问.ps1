# 验证WPF-11: opening a rep that already has a video next to it must ASK first, and each of the
#   three answers must do the right thing to the queue row.
#
#   0 覆写：这些文件重录（会覆盖原来的视频）  -> 等待
#   1 再录：再录一次（旧的保留，新的另存一份）-> 等待
#   2 跳过：跳过它们，只录没有录过的          -> 已录制
#
# ⚠ 2026-10-11 改的：这个框以前是系统 MessageBox（是/否两键），用户要求加第三个选择，
#   于是换成了程序自己的 WPF 小窗（ReRecordDialog），按钮 id 是 rrOverwrite / rrAgain / rrSkip。
#   两个后果，正是这份脚本改掉的地方：
#     1) 窗口不再是原生 #32770，靠 Win32 找窗口 + WM_COMMAND 应答那一套用不了了；
#     2) 换成 WPF 模态窗之后【调试端口仍然答话】（WPF 的 ShowDialog 会继续泵消息循环，
#        而原生 MessageBox 会把 UI 线程整条占死）—— 所以现在直接 press:rrXxx 就行，比原来简单。
# ASCII only. Run with: powershell -File <this>   (never pipe its stdout)
$here = $PSScriptRoot                  # this script lives in the tests folder
$dir  = Split-Path $here -Parent       # the app folder: the exe and its log/settings live here
$exe  = Join-Path $dir 'RepRecorderWPF.exe'
$log  = Join-Path $dir 'RepRecorder.log'
$cfg  = Join-Path $dir 'RepRecorder.settings.json'
$bak  = Join-Path $here '_settings-backup11.json'
$runLog = Join-Path $here '_run11.log'
$port = 7811
$fail = 0
Remove-Item $runLog -ErrorAction SilentlyContinue

# ASCII-only path: the game folder has brackets and spaces in it
$repDir = Join-Path $env:TEMP 'rr-ask-wpf'
if (Test-Path $repDir) { Remove-Item $repDir -Recurse -Force }
New-Item -ItemType Directory -Path $repDir | Out-Null
$repFile = Join-Path $repDir 'ask1.rep'
$oldMp4  = Join-Path $repDir 'ask1.mp4'
[System.IO.File]::WriteAllBytes($repFile, (New-Object byte[] 4096))
[System.IO.File]::WriteAllBytes($oldMp4, (New-Object byte[] 4096))

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
function LogTxt { if (Test-Path $log) { return (Get-Content $log -Raw -Encoding UTF8) } return '' }
function StopApp { Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }; Start-Sleep -Milliseconds 700 }

# Chinese literals spelled as code points so this file stays pure ASCII
$MSG_ASKED = [string]([char]0x6B63 + [char]0x5728 + [char]0x8BE2 + [char]0x95EE)                       # 正在询问
$MSG_AGAIN = [string]([char]0x4F60 + [char]0x9009 + [char]0x62E9 + [char]0x518D + [char]0x5F55 + [char]0x4E00 + [char]0x6B21)   # 你选择再录一次
$MSG_SKIP  = [string]([char]0x4F60 + [char]0x9009 + [char]0x62E9 + [char]0x8DF3 + [char]0x8FC7)         # 你选择跳过
$MSG_OVER  = [string]([char]0x4F60 + [char]0x9009 + [char]0x62E9 + [char]0x91CD + [char]0x5F55)         # 你选择重录
$ST_WAIT   = [string]([char]0x7B49 + [char]0x5F85)                                                     # 等待
$ST_DONE   = [string]([char]0x5DF2 + [char]0x5F55 + [char]0x5236)                                     # 已录制
# row detail needles: 再录一次（旧的保留 -> 再录一次 / 重录（会覆盖 -> 重录
$DET_AGAIN = [string]([char]0x518D + [char]0x5F55 + [char]0x4E00 + [char]0x6B21)                       # 再录一次
$DET_OVER  = [string]([char]0x91CD + [char]0x5F55)                                                     # 重录
$DET_SKIP  = [string]([char]0x8DF3 + [char]0x8FC7)                                                     # 跳过

# One run of the app, one answer to the prompt.
function RunPass([string]$tag, [string]$button, [string]$expectStatus, [string]$expectDetailNeedle, [string]$expectLogNeedle) {
  StopApp
  Remove-Item $log -ErrorAction SilentlyContinue
  # put the old video back in case a previous pass touched it
  [System.IO.File]::WriteAllBytes($oldMp4, (New-Object byte[] 4096))
  $p = Start-Process -FilePath $exe -ArgumentList @("--debug-port=$port", $repFile) -PassThru

  $shown = $false
  for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 500
    if ($p.HasExited) { break }
    $q = Dbg 'find:reRecordDialog'
    if ($null -ne $q -and $q -match '"found":true') { $shown = $true; break }
  }
  Check ($tag + ': app alive') (-not $p.HasExited) ('pid=' + $p.Id)
  Check ($tag + ': the three-way dialog opened') $shown ''
  Check ($tag + ': the prompt is in the log') ((LogTxt).Contains($MSG_ASKED)) ''
  if (-not $shown) { StopApp; return }

  Dbg ('press:' + $button) | Out-Null
  Start-Sleep -Milliseconds 1500

  $q = Dbg 'find:reRecordDialog'
  Check ($tag + ': the dialog closed after the answer') ($null -ne $q -and $q -match '"found":false') ('q=' + $q)

  $r = Dbg 'rows' | ConvertFrom-Json
  Check ($tag + ': the row stayed in the queue') ($r.count -eq 1) ('count=' + $r.count)
  if ($r.count -ge 1) {
    Check ($tag + ': the row status is the expected one') ($r.rows[0].status -eq $expectStatus) ('status=' + $r.rows[0].status)
    Check ($tag + ': the row says what was chosen') ($r.rows[0].detail.Contains($expectDetailNeedle)) ('detail=' + $r.rows[0].detail)
  }
  Check ($tag + ': the choice is logged') ((LogTxt).Contains($expectLogNeedle)) ''
  # the whole point of the middle option: the recording that is already there must survive
  Check ($tag + ': the existing video is still on disk') (Test-Path $oldMp4) ''
  Check ($tag + ': no exception in log') ((LogTxt) -notmatch 'Exception') ''
  StopApp
}

Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 700
Copy-Item $cfg $bak -Force

try {
  RunPass 'again' 'rrAgain' $ST_WAIT $DET_AGAIN $MSG_AGAIN
  RunPass 'overwrite' 'rrOverwrite' $ST_WAIT $DET_OVER $MSG_OVER
  RunPass 'skip' 'rrSkip' $ST_DONE $DET_SKIP $MSG_SKIP
}
finally {
  StopApp
  if (Test-Path $bak) { Copy-Item $bak $cfg -Force; Remove-Item $bak -Force }
  Remove-Item $repDir -Recurse -Force -ErrorAction SilentlyContinue
  Write-Host 'settings restored'
}
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
