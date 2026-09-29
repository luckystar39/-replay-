# 探测 v4：只改「调用者自己」的进程，看 API 到底行不行
. 'D:\【完整包】非想天则整合包2026-06-15\RepRecorder\探针库-音频路由.ps1'

"===== 1) 设备清单 ====="
"$([RouteProbe]::Dump())"
if(-not [RouteProbe]::Init()){ "  ✗ $([RouteProbe]::InitError)"; exit 1 }
"  ✅ 接口 IID = $([RouteProbe]::Iid)"

"===== 2) 让『我自己』(当前进程 pid=$PID) 发出声音 ====="
$sp = New-Object System.Media.SoundPlayer 'C:\Windows\Media\Alarm01.wav'
$sp.PlayLooping()
Start-Sleep -Seconds 3
$selfSess = [RouteProbe]::SessDefault()
"  默认设备会话: $selfSess"
"  我自己有会话吗: $($selfSess -match "pid=$PID,")"

$spareIdx = 0; for($i=0;$i -lt [RouteProbe]::ActiveIds.Count;$i++){ if([RouteProbe]::ActiveIds[$i] -ne [RouteProbe]::DefId){ $spareIdx=$i; break } }
$spareId=[RouteProbe]::ActiveIds[$spareIdx]; $spareName=[RouteProbe]::ActiveNames[$spareIdx]
"  目标设备 = $spareName"

"===== 3) 给『我自己』换设备 ====="
for($r=0;$r -le 2;$r++){
  $hr=[RouteProbe]::Set([uint32]$PID, $r, $spareId)
  "  Set(自己, role=$r) → 0x{0:X8}" -f $hr
}
$h=0; $got=[RouteProbe]::Get([uint32]$PID, 0, [ref]$h)
"  回读 → 0x{0:X8} 值={1} 与目标一致={2}" -f $h, $got, ($got -eq $spareId)

"===== 4) 我自己的声音有没有搬家 ====="
Start-Sleep -Seconds 2
"  默认设备  : $([RouteProbe]::SessDefault())"
"  目标设备  : $([RouteProbe]::SessById($spareId))"

"===== 5) 还原（设回默认设备）====="
for($r=0;$r -le 2;$r++){ $hr=[RouteProbe]::Set([uint32]$PID, $r, [RouteProbe]::DefId); "  Set(默认, role=$r) → 0x{0:X8}" -f $hr }
$sp.Stop()
"===== 探测结束 ====="
