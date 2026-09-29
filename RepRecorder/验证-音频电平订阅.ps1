# 验证音频电平订阅：正常录制 vs 端点静音，看 OBS 的进程音频采集电平是否变化
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"
$app="$dir\RepRecorder.exe"
$rep="$root\【完整包】非想天则整合包2026-06-15\th123\replay\【观战Rep】\[Timepieceit LV2]_[menghenyinxin]\26-08-15_23-57-29_menghenyinxin(红美铃)_Timepieceit LV2(红美铃)_(1︰0_无).rep"
$out=[System.IO.Path]::ChangeExtension($rep,'.mp4')

# 1) 确保 OBS 在跑
if(-not (Get-Process obs64 -ErrorAction SilentlyContinue)){
  Start-Process -FilePath 'D:\obs\obs-studio\bin\64bit\obs64.exe' -WorkingDirectory 'D:\obs\obs-studio\bin\64bit'
  Start-Sleep -Seconds 18
}
"OBS 在运行: " + [bool](Get-Process obs64 -ErrorAction SilentlyContinue)

# 2) 先确认电平事件确实在推（此时游戏没开，应该能看到全局音频源）
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
Start-Process -FilePath $app -ArgumentList @('--audio-meter','5') -Wait
"---- 空载时的电平 ----"
Get-Content -LiteralPath $log -Encoding UTF8

# 3) 开始一次真实录制
Remove-Item -LiteralPath $out -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$pr=Start-Process -FilePath $app -ArgumentList ('"'+$rep+'"') -PassThru
$ok=$false
for($i=0;$i -lt 60;$i++){
  Start-Sleep -Seconds 1
  if(Test-Path $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '开始录像'){ $ok=$true; break } }
}
"已开始录像: $ok"
Start-Sleep -Seconds 6

# 4) 正常播放时读电平
Start-Process -FilePath $app -ArgumentList @('--audio-meter','6') -Wait

# 5) 端点静音后再读
Start-Process -FilePath $app -ArgumentList @('--sys-mute','on') -Wait
Start-Sleep -Seconds 2
Start-Process -FilePath $app -ArgumentList @('--audio-meter','6') -Wait
Start-Process -FilePath $app -ArgumentList @('--sys-mute','off') -Wait

# 6) 等录制结束
for($i=0;$i -lt 90;$i++){
  Start-Sleep -Seconds 3
  if(Test-Path $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } }
}
"---- 完整日志（只看关键行） ----"
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String -Pattern 'audio-meter|sys-mute|音频|开始录像|完成|自检' | ForEach-Object { $_.Line }
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
