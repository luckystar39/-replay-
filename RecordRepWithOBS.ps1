<#
.SYNOPSIS
  用 OBS Studio（obs-websocket）把非想天则 .rep 自动录制成视频文件。

.DESCRIPTION
  流程：确保 OBS 在运行 -> 连接 obs-websocket -> 用 th123.exe 打开 rep 播放
        -> 开始录像 -> 等游戏播放完自动关闭（ReplayDnD.ini 的 AutoShutdown=1）
        -> 停止录像 -> 把录像文件改名成与 rep 同名，放到 rep 同目录。

  一次性准备（只做一次）：
    1) 安装 OBS：  winget install --id OBSProject.OBSStudio -e
    2) OBS 里建好场景，添加源：
         - 推荐「游戏源」，模式选「捕获特定窗口」并绑定 th123.exe；
         - 若以后装了 Intel/wined3d 特效补丁，游戏源会失效，改用「窗口采集」选非想天则窗口。
       右键该源 -> 变换 -> 适应屏幕。
       OBS -> 设置 -> 视频：基础(画布)分辨率与输出分辨率都设 1440x1080，FPS 60（4:3 素材，放大到 1440x1080）。
       OBS -> 设置 -> 输出 -> 录像：编码器选 NVIDIA NVENC AV1 或 H.264，
         速率控制 CQP，CQ 20~23（或 CBR 10~15 Mbps），录像格式选 MP4。
    3) OBS -> 工具 -> WebSocket 服务器设置 -> 勾选「启用 WebSocket 服务器」（端口默认 4455）。
       密码脚本会自动从 OBS 配置文件读取，也可以用 -Password 手动指定。
    4) 游戏侧：启用 ReplayDnD-old（双击 rep 播放的版本），ReplayDnD.ini 里 AutoShutdown=1。
       想让录像带 BGM，把 ReplayDnD.ini 的 MuteMusic 改成 0（注意 B 站版权检测）。

  注意：这是实时录屏，一段 5 分钟的 rep 就要录 5 分钟；录制期间不要切换窗口、不要动鼠标乱点。
        想要 20 倍速的离线快录只有 ReplayDnD 内置的 render 功能（本机 N 卡不可用，见 RepRenderTest.ps1）。

.PARAMETER Rep
  单个 .rep 文件路径。

.PARAMETER Folder
  批量模式：递归录制该文件夹下所有 .rep（已有同名 mp4 的会跳过，除非加 -Force）。

.PARAMETER GameDir
  th123 文件夹路径（含 th123.exe）。默认自动在本脚本所在目录下寻找。

.PARAMETER ObsExe
  obs64.exe 路径。默认自动查找。

.PARAMETER Password
  obs-websocket 密码。留空则自动读取 OBS 配置文件。

.PARAMETER Port
  obs-websocket 端口，默认 4455。

.PARAMETER Force
  覆盖已存在的同名 mp4。

.PARAMETER MaxMinutes
  单个 rep 的录制上限（分钟），默认 12。超时后强制停录并结束游戏。

.PARAMETER PreRollSeconds
  游戏窗口出现后、开始录像前的等待秒数，默认 3。

.PARAMETER PostRollSeconds
  游戏退出后、停止录像前的等待秒数，默认 2。

.PARAMETER EnableWebSocket
  若 OBS 里没启用 WebSocket 服务器，自动改 OBS 配置文件打开它（需要 OBS 处于关闭状态）。

.PARAMETER CloseObs
  全部录完后关闭 OBS。

.EXAMPLE
  .\RecordRepWithOBS.ps1 -Rep "D:\replay\a.rep"
.EXAMPLE
  .\RecordRepWithOBS.ps1 -Folder "D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123\replay\【联机对战Rep】"
.EXAMPLE
  .\RecordRepWithOBS.ps1 -Folder ".\replay" -Force -MaxMinutes 15 -CloseObs
#>
param(
  [string]$Rep,
  [string]$Folder,
  [string]$GameDir,
  [string]$ObsExe,
  [string]$Password,
  [int]$Port = 4455,
  [switch]$Force,
  [double]$MaxMinutes = 12,
  [int]$PreRollSeconds = 3,
  [int]$PostRollSeconds = 2,
  [switch]$EnableWebSocket,
  [switch]$CloseObs
)

$ErrorActionPreference = 'Stop'

function Say  { param($m,$c='Gray')  Write-Host $m -ForegroundColor $c }
function Step { param($m) Write-Host "==> $m" -ForegroundColor Cyan }
function Warn { param($m) Write-Host "!!  $m" -ForegroundColor Yellow }
function Fail { param($m) Write-Host "XX  $m" -ForegroundColor Red }

# ---------------------------------------------------------------- 定位文件
function Find-GameDir {
  param($hint)
  if ($hint) {
    if (Test-Path -LiteralPath (Join-Path $hint 'th123.exe')) { return (Resolve-Path -LiteralPath $hint).Path }
    Warn "指定的 GameDir 里没有 th123.exe：$hint"
  }
  $cands = New-Object System.Collections.Generic.List[string]
  if ($PSScriptRoot) {
    $cands.Add((Join-Path $PSScriptRoot 'th123'))
    Get-ChildItem -LiteralPath $PSScriptRoot -Directory -ErrorAction SilentlyContinue |
      ForEach-Object { $cands.Add((Join-Path $_.FullName 'th123')) }
  }
  foreach ($c in $cands) { if (Test-Path -LiteralPath (Join-Path $c 'th123.exe')) { return (Resolve-Path -LiteralPath $c).Path } }
  return $null
}

function Find-ObsExe {
  param($hint)
  if ($hint -and (Test-Path -LiteralPath $hint)) { return $hint }
  $cmd = Get-Command obs64.exe -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  foreach ($p in @("$env:ProgramFiles\obs-studio\bin\64bit\obs64.exe",
                   "${env:ProgramFiles(x86)}\obs-studio\bin\64bit\obs64.exe")) {
    if (Test-Path -LiteralPath $p) { return $p }
  }
  return $null
}

# ---------------------------------------------------------------- obs-websocket 客户端
function Get-B64Sha256 {
  param([string]$s)
  $sha = [System.Security.Cryptography.SHA256]::Create()
  try { return [Convert]::ToBase64String($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($s))) }
  finally { $sha.Dispose() }
}

function Receive-ObsMessage {
  param($ws)
  $buf = New-Object byte[] 65536
  $ms  = New-Object System.IO.MemoryStream
  while ($true) {
    $seg = New-Object 'System.ArraySegment[byte]' -ArgumentList (,$buf)
    $res = $ws.ReceiveAsync($seg, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
    if ($res.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) { throw 'OBS 关闭了 WebSocket 连接' }
    if ($res.Count -gt 0) { $ms.Write($buf, 0, $res.Count) }
    if ($res.EndOfMessage) { break }
  }
  $txt = [System.Text.Encoding]::UTF8.GetString($ms.ToArray())
  $ms.Dispose()
  if ([string]::IsNullOrWhiteSpace($txt)) { return $null }
  return ($txt | ConvertFrom-Json)
}

function Send-ObsMessage {
  param($ws, $obj)
  $json  = $obj | ConvertTo-Json -Compress -Depth 10
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
  $seg   = New-Object 'System.ArraySegment[byte]' -ArgumentList (,$bytes)
  $ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true,
                [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
}

function Connect-Obs {
  param([string]$Uri, [string]$Pwd)
  $ws = New-Object System.Net.WebSockets.ClientWebSocket
  $uriObj = New-Object System.Uri($Uri)
  $ws.ConnectAsync($uriObj, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()

  $hello = Receive-ObsMessage $ws
  if ($null -eq $hello -or $hello.op -ne 0) { throw 'OBS 未返回 Hello 握手包' }
  Say ("    obs-websocket " + $hello.d.obsWebSocketVersion) 'DarkGray'

  $identify = @{ rpcVersion = 1 }
  if ($hello.d.authentication) {
    if (-not $Pwd) { throw 'OBS 要求密码，但脚本没拿到密码：请用 -Password 指定，或在 OBS 里关闭认证' }
    $secret = Get-B64Sha256 ($Pwd + $hello.d.authentication.salt)
    $identify.authentication = Get-B64Sha256 ($secret + $hello.d.authentication.challenge)
  }
  Send-ObsMessage $ws @{ op = 1; d = $identify }
  $ok = Receive-ObsMessage $ws
  if ($null -eq $ok -or $ok.op -ne 2) {
    $reason = ''
    if ($ok -and $ok.d -and $ok.d.error) { $reason = $ok.d.error }
    throw ("WebSocket 认证失败 " + $reason)
  }
  return $ws
}

function Invoke-Obs {
  param($ws, [string]$Type, $Data)
  $script:obsReqId++
  $reqId = "req$($script:obsReqId)"
  $inner = @{ requestType = $Type; requestId = $reqId }
  if ($null -ne $Data) { $inner.requestData = $Data }
  Send-ObsMessage $ws @{ op = 6; d = $inner }
  while ($true) {
    $msg = Receive-ObsMessage $ws
    if ($null -eq $msg) { continue }
    if ($msg.op -eq 7 -and $msg.d.requestId -eq $reqId) {
      if (-not $msg.d.requestStatus.result) {
        throw ("OBS 请求 " + $Type + " 失败：code=" + $msg.d.requestStatus.code + " " + $msg.d.requestStatus.comment)
      }
      return $msg.d.responseData
    }
    # op=5 是事件，这里不需要，忽略
  }
}

# ---------------------------------------------------------------- 前置检查
Step '检查运行环境'

$GameDir = Find-GameDir $GameDir
if (-not $GameDir) { Fail '找不到 th123.exe，请用 -GameDir 指定游戏 th123 文件夹'; exit 1 }
$gameExe = Join-Path $GameDir 'th123.exe'
Say "    游戏目录 : $GameDir"

$obsConfigPath = Join-Path $env:APPDATA 'obs-studio\plugin_config\obs-websocket\config.json'
$obsConf = $null
if (Test-Path -LiteralPath $obsConfigPath) {
  $obsConf = Get-Content -LiteralPath $obsConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
}

$ObsExe = Find-ObsExe $ObsExe
if (-not $ObsExe) {
  Fail '没找到 obs64.exe。先安装 OBS：'
  Say  '    winget install --id OBSProject.OBSStudio -e' 'White'
  exit 1
}
Say "    OBS      : $ObsExe"

if ($obsConf) {
  if (-not $Password -and $obsConf.server_password) { $Password = $obsConf.server_password }
  if ($obsConf.server_port) { $Port = [int]$obsConf.server_port }
  if (-not $obsConf.server_enabled) {
    if ($EnableWebSocket) {
      $running = Get-Process obs64 -ErrorAction SilentlyContinue
      if ($running) { Fail '需要先手动关闭 OBS，再加 -EnableWebSocket 重跑（脚本要改 OBS 配置）'; exit 1 }
      $obsConf.server_enabled = $true
      $obsConf | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $obsConfigPath -Encoding UTF8
      Say ("    已为你打开 obs-websocket 服务器（端口 $Port）") 'Green'
    } else {
      Fail 'OBS 里没有启用 WebSocket 服务器。'
      Say  '    打开 OBS -> 工具 -> WebSocket 服务器设置 -> 勾选「启用 WebSocket 服务器」，' 'White'
      Say  '    然后重跑本脚本；或先关掉 OBS，加参数 -EnableWebSocket 让脚本自动开。' 'White'
      exit 1
    }
  }
} elseif ($EnableWebSocket) {
  Fail '没找到 OBS 配置文件，请先启动一次 OBS 再重跑。'
  exit 1
}

# 收集要录的 rep
$reps = New-Object System.Collections.Generic.List[string]
if ($Rep) {
  if (-not (Test-Path -LiteralPath $Rep)) { Fail "rep 不存在：$Rep"; exit 1 }
  $reps.Add((Resolve-Path -LiteralPath $Rep).Path)
}
if ($Folder) {
  if (-not (Test-Path -LiteralPath $Folder)) { Fail "文件夹不存在：$Folder"; exit 1 }
  Get-ChildItem -LiteralPath $Folder -Recurse -Force -File -Filter *.rep |
    Sort-Object FullName | ForEach-Object { $reps.Add($_.FullName) }
}
if ($reps.Count -eq 0) {
  Warn '没有指定要录的 rep。用法示例：'
  Say  '    .\RecordRepWithOBS.ps1 -Rep "D:\...\xxx.rep"' 'White'
  Say  '    .\RecordRepWithOBS.ps1 -Folder "D:\...\replay\【联机对战Rep】"' 'White'
  exit 1
}
if ($reps.Count -gt 1 -and ($PreRollSeconds -lt 3)) { $PreRollSeconds = 3 }
Say "    待录 rep : $($reps.Count) 个"

# ---------------------------------------------------------------- 启动 / 连接 OBS
if (-not (Get-Process obs64 -ErrorAction SilentlyContinue)) {
  Step '启动 OBS'
  Start-Process -FilePath $ObsExe -ArgumentList '--minimize-to-tray' -WorkingDirectory (Split-Path $ObsExe -Parent) | Out-Null
  Start-Sleep -Seconds 3
}

Step "连接 obs-websocket (127.0.0.1:$Port)"
$ws = $null
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline) {
  try { $ws = Connect-Obs -Uri "ws://127.0.0.1:$Port" -Pwd $Password; break }
  catch { Start-Sleep -Seconds 2 }
}
if (-not $ws) { Fail '连不上 obs-websocket（检查 OBS 是否已启动、服务器是否启用、端口/密码是否一致）'; exit 1 }
$script:obsReqId = 0
Say '    已连接' 'Green'

# 场景里有没有可用的采集源
try {
  $inputs = (Invoke-Obs $ws 'GetInputList').inputs
  $kinds  = @($inputs | ForEach-Object { $_.unversionedInputKind })
  Say ("    现有采集源 : " + (($kinds | Sort-Object -Unique) -join ', ')) 'DarkGray'
  if (-not ($kinds -match 'game_capture|window_capture|monitor_capture')) {
    Warn '没有找到游戏源/窗口采集/显示器采集，录像可能是黑屏！请先在 OBS 里加好源。'
  }
} catch { }

# 正在录像的话先停掉
try {
  $st = Invoke-Obs $ws 'GetRecordStatus'
  if ($st.outputActive) { Warn 'OBS 正在录像，先停止它'; Invoke-Obs $ws 'StopRecord' | Out-Null; Start-Sleep -Seconds 1 }
} catch { }

function Test-Mp4Index {
  param([string]$path)
  try {
    $fi = Get-Item -LiteralPath $path
    $fs = [System.IO.File]::OpenRead($path)
    try {
      $n  = [int][Math]::Min(1048576, $fi.Length)
      $b1 = New-Object byte[] $n; [void]$fs.Read($b1, 0, $n)
      $fs.Seek([Math]::Max(0, $fi.Length - 1048576), [System.IO.SeekOrigin]::Begin) | Out-Null
      $n2 = [int][Math]::Min(1048576, $fi.Length)
      $b2 = New-Object byte[] $n2; [void]$fs.Read($b2, 0, $n2)
      $s = [System.Text.Encoding]::ASCII.GetString($b1) + [System.Text.Encoding]::ASCII.GetString($b2)
      return $s.Contains('moov')
    } finally { $fs.Close() }
  } catch { return $false }
}

# ---------------------------------------------------------------- 主循环
$done = 0; $failed = 0; $skipped = 0
foreach ($r in $reps) {
  $leaf = Split-Path $r -Leaf
  $out  = [System.IO.Path]::ChangeExtension($r, '.mp4')
  if ((Test-Path -LiteralPath $out) -and (-not $Force)) {
    Warn "已存在，跳过：$leaf"
    $skipped++
    continue
  }
  Step "录制 $leaf"

  Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 800

  # 让 OBS 把录像直接写到 rep 所在目录（失败也不影响，最后会移动文件）
  try { Invoke-Obs $ws 'SetRecordDirectory' @{ recordDirectory = (Split-Path $r -Parent) } | Out-Null } catch { }

  $proc = Start-Process -FilePath $gameExe -ArgumentList ('"' + $r + '"') -WorkingDirectory $GameDir -PassThru
  $wDeadline = (Get-Date).AddSeconds(60); $shown = $false
  while ((Get-Date) -lt $wDeadline) {
    Start-Sleep -Milliseconds 500
    $pp = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    if (-not $pp) { break }
    if ($pp.MainWindowHandle -ne 0) { $shown = $true; break }
  }
  if (-not $shown) { Warn '游戏窗口没出现，仍继续尝试录像' }
  if ($PreRollSeconds -gt 0) { Start-Sleep -Seconds $PreRollSeconds }

  Invoke-Obs $ws 'StartRecord' | Out-Null
  Say "    录像中…… 播完自动停止（上限 $MaxMinutes 分钟）" 'DarkGray'

  $stopAt = (Get-Date).AddMinutes($MaxMinutes); $ended = $false
  while ((Get-Date) -lt $stopAt) {
    Start-Sleep -Seconds 1
    if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) { $ended = $true; break }
  }
  if ($ended) {
    if ($PostRollSeconds -gt 0) { Start-Sleep -Seconds $PostRollSeconds }
  } else {
    Warn "超过 $MaxMinutes 分钟，强制结束游戏并停止录像"
    Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
  }

  $recordPath = $null
  try {
    $resp = Invoke-Obs $ws 'StopRecord'
    if ($resp -and $resp.outputPath) { $recordPath = $resp.outputPath }
  } catch { Fail "停止录像失败：$_" }

  # 兜底：从 OBS 录像目录里找最近修改的录像文件
  if (-not $recordPath) {
    try {
      $dir = (Invoke-Obs $ws 'GetRecordDirectory').recordDirectory
      if ($dir -and (Test-Path -LiteralPath $dir)) {
        $newest = Get-ChildItem -LiteralPath $dir -File |
                  Where-Object { $_.Extension -match 'mp4|mkv|flv|mov' -and $_.LastWriteTime -gt (Get-Date).AddMinutes(-1) } |
                  Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($newest) { $recordPath = $newest.FullName }
      }
    } catch { }
  }

  if ($recordPath -and (Test-Path -LiteralPath $recordPath)) {
    $src = (Resolve-Path -LiteralPath $recordPath).Path
    $srcExt = [System.IO.Path]::GetExtension($src)
    $target = $out
    if ($srcExt -and $srcExt.ToLower() -ne '.mp4') { $target = [System.IO.Path]::ChangeExtension($r, $srcExt) }
    if ($src -ne $target) {
      if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
      Move-Item -LiteralPath $src -Destination $target -Force
    }
    $fi = Get-Item -LiteralPath $target
    if ($target.ToLower().EndsWith('.mp4') -and (-not (Test-Mp4Index $target))) {
      Warn "文件已生成但缺少 moov 索引（可能是录像没正常结束）：$($fi.Name)"
    }
    Say ("    完成：{0}  ({1:N1} MB)" -f $fi.Name, ($fi.Length / 1MB)) 'Green'
    if (-not $target.ToLower().EndsWith('.mp4')) {
      Warn "OBS 录像格式不是 MP4，已保存为 $srcExt。建议在 OBS->设置->输出->录像 里改成 MP4；或自行转封装："
      Say  ("    ffmpeg -i `"$target`" -c copy `"$([System.IO.Path]::ChangeExtension($target,'.mp4'))`"") 'White'
    }
    $done++
  } else {
    Fail "拿不到录像文件路径（检查 OBS 是否真的在录像、输出目录是否存在）：$leaf"
    $failed++
  }
}

# ---------------------------------------------------------------- 收尾
try { $ws.Dispose() } catch { }
if ($CloseObs) { Get-Process obs64 -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null }; Say '已请求关闭 OBS' }

Say ''
Say ("完成 {0} 个，失败 {1} 个，跳过 {2} 个" -f $done, $failed, $skipped) $(if ($failed -gt 0) { 'Yellow' } else { 'Green' })
Say '提示：录到的是 640x480 原画面经 OBS 缩放后的效果；若要更好的观感，可把 OBS 输出分辨率设为 1440x1080，' 'DarkGray'
Say '      或用 ffmpeg(NVENC) 后处理放大：' 'DarkGray'
Say '      ffmpeg -i in.mp4 -vf "scale=1440:1080:flags=lanczos" -c:v h264_nvenc -preset p6 -tune hq -rc vbr -cq 23 -b:v 0 -c:a copy out_1080p.mp4' 'DarkGray'
exit $failed
