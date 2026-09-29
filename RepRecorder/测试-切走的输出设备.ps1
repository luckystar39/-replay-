# 测试：把游戏的输出设备切到备用设备，看音频会话是否真的搬家 + 重启后是否还记得
. 'D:\【完整包】非想天则整合包2026-06-15\RepRecorder\探针库-音频路由.ps1'
. 'D:\【完整包】非想天则整合包2026-06-15\RepRecorder\音量合成器自动化.ps1'

$root='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123'
$rep='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123\replay\【联机对战Rep】\[Aquila(3) ]\26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep'
$ini1=Join-Path $root 'modules\ReplayDnD\ReplayDnD.ini'
$ini2=Join-Path $root 'modules\ReplayDnD-old\ReplayDnD.ini'
$bak1=(Get-Content -LiteralPath $ini1 -Raw -Encoding UTF8); $bak2=(Get-Content -LiteralPath $ini2 -Raw -Encoding UTF8)
[System.IO.File]::WriteAllText($ini1,($bak1 -replace 'MuteMusic=1','MuteMusic=0'),(New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText($ini2,($bak2 -replace 'MuteMusic=1','MuteMusic=0'),(New-Object System.Text.UTF8Encoding($false)))

try{
  "===== 1) 设备情况 ====="
  "$([RouteProbe]::Dump())"
  $defName=[RouteProbe]::DefName
  $spareIdx=0; for($i=0;$i -lt [RouteProbe]::ActiveIds.Count;$i++){ if([RouteProbe]::ActiveIds[$i] -ne [RouteProbe]::DefId){ $spareIdx=$i; break } }
  $spareId=[RouteProbe]::ActiveIds[$spareIdx]; $spareName=[RouteProbe]::ActiveNames[$spareIdx]
  "  默认 = $defName"
  "  备用 = $spareName"

  "===== 2) 起游戏（BGM 开）====="
  Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 2
  $p=Start-Process -FilePath (Join-Path $root 'th123.exe') -ArgumentList ('"'+$rep+'"') -WorkingDirectory $root -PassThru
  $i=[RouteProbe]::WaitForSession([int]$p.Id, 45)
  "  游戏 PID=$($p.Id)，音频会话出现在设备 #$i：$(if($i -ge 0){[RouteProbe]::ActiveNames[$i]}else{'（没等到）'})"

  "===== 3) 打开音量合成器，读当前设置 ====="
  $win = Open-VolumeMixer
  if(-not $win){ "✗ 打不开设置窗口"; exit 1 }
  "  当前 th123 的输出设备 = 「$(Get-AppOutputDevice $win $p.Id)」"

  "===== 4) 改成备用设备 ====="
  "  " + (Set-AppOutputDevice $win $p.Id $spareName)
  Start-Sleep -Seconds 2
  "  改完读回来 = 「$(Get-AppOutputDevice $win $p.Id)」"
  "  会话位置：默认设备=$([RouteProbe]::SessById([RouteProbe]::DefId))"
  "            备用设备=$([RouteProbe]::SessById($spareId))"

  "===== 5) 杀掉游戏重启，看 Windows 记不记得这个设置 ====="
  $p.Refresh(); if(-not $p.HasExited){ $p.Kill() }
  Start-Sleep 2
  $p2=Start-Process -FilePath (Join-Path $root 'th123.exe') -ArgumentList ('"'+$rep+'"') -WorkingDirectory $root -PassThru
  $i2=[RouteProbe]::WaitForSession([int]$p2.Id, 45)
  "  新进程 PID=$($p2.Id) 的音频会话出现在设备 #$i2：$(if($i2 -ge 0){[RouteProbe]::ActiveNames[$i2]}else{'（没等到）'})"
  "  → 如果上面是『备用』设备，说明 Windows 记住了（持久化）✓"
  $g = Get-AppGroup $win $p2.Id
  if($g){ "  音量合成器里新进程的输出设备 = 「$(Get-AppOutputDevice $win $p2.Id)」" }

  "===== 6) 还原成默认设备 ====="
  "  " + (Set-AppOutputDevice $win $p2.Id $defName)
  Start-Sleep 2
  $p2.Refresh(); if(-not $p2.HasExited){ $p2.Kill() }
} finally {
  Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  [System.IO.File]::WriteAllText($ini1,$bak1,(New-Object System.Text.UTF8Encoding($false)))
  [System.IO.File]::WriteAllText($ini2,$bak2,(New-Object System.Text.UTF8Encoding($false)))
  "MuteMusic 已还原"
}
"===== 测试结束 ====="
