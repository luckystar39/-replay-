# 验证这一轮的改动：①清晰度（neighbor）②并发切分提速 ③录像音量（增益）④音频仍在
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$cfg="$dir\RepRecorder.settings.json"
$log="$dir\RepRecorder.log"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
function Say($m){ Write-Host $m }
function Sharp($path){
  $bmp=[System.Drawing.Bitmap]::FromFile($path)
  try{ $sum=0.0;$n=0
    for($y=200;$y -lt [Math]::Min($bmp.Height,900);$y+=3){ for($x=1;$x -lt $bmp.Width;$x+=2){
      $a=$bmp.GetPixel($x,$y);$b=$bmp.GetPixel($x-1,$y)
      $sum += [Math]::Abs(($a.R+$a.G+$a.B)/3.0-($b.R+$b.G+$b.B)/3.0); $n++ } }
    return [Math]::Round($sum/$n,2) } finally { $bmp.Dispose() }
}
Add-Type -AssemblyName System.Drawing
$cand=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter '*.rep' |
      Where-Object { $_.Length -ge 40KB -and $_.Length -le 150KB -and -not (Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($_.FullName,'.mp4'))) } |
      Sort-Object Length | Select-Object -First 4
if($cand.Count -lt 4){ Say "❌ 小 rep 不够"; exit }
Say ("用 4 个 rep：" + (($cand | ForEach-Object { $_.Name.Substring(0,12) }) -join ' / '))
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.ParallelCount=4; $j.AskSaveLocation=$false; $j.SkipExisting=$true; $j.AutoStartOnDrop=$true; $j.AutoCanvas=$true
$j.RecordGameAudio=$true      # 打开录音，验证增益和音轨
$j | Add-Member -NotePropertyName RecordVolume -NotePropertyValue 2.0 -Force
$j | Add-Member -NotePropertyName SplitParallel -NotePropertyValue 3 -Force
$j | Add-Member -NotePropertyName SettingsVersion -NotePropertyValue 4 -Force
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$a=@(); foreach($r in $cand){ $a += ('"'+$r.FullName+'"') }
$pr=Start-Process -FilePath "$dir\RepRecorder.exe" -ArgumentList $a -PassThru
$sw=[Diagnostics.Stopwatch]::StartNew()
while($sw.Elapsed.TotalSeconds -lt 900){ Start-Sleep -Seconds 5
  if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } } }
Say ("整轮用时 {0:N0} 秒" -f $sw.Elapsed.TotalSeconds)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Say "---- 日志：切分耗时/音量/画布 ----"
$lines=Get-Content -LiteralPath $log -Encoding UTF8
$lines | Select-String '画布|同时跑|切分完成|切分失败|录像音量|游戏声音录制|音频自检' | ForEach-Object { Say ("  " + $_.Line) }
# 切分总耗时：从"同时跑"到最后一个"切分完成"
$t0=$null;$t1=$null
foreach($l in $lines){ if($l -match '^\s*(\d\d:\d\d:\d\d).*同时跑'){ $t0=[datetime]::Parse($Matches[1]) }; if($l -match '^\s*(\d\d:\d\d:\d\d).*切分完成'){ $t1=[datetime]::Parse($Matches[1]) } }
if($t0 -and $t1){ Say ("  → 切分总耗时约 " + [int]($t1-$t0).TotalSeconds + " 秒（4 格，同时 3 个）") }
Say "---- 成品 + 清晰度（同一帧裁出来对比 neighbor）----"
$i=0
foreach($r in $cand){
  $o=[System.IO.Path]::ChangeExtension($r.FullName,'.mp4')
  if(Test-Path -LiteralPath $o){
    $i++
    $info=(& $ffmpeg -hide_banner -i "$o" 2>&1 | Select-String 'Stream #') -join ' | '
    Say ("  " + (Split-Path $o -Leaf).Substring(0,20) + " … " + [math]::Round((Get-Item -LiteralPath $o).Length/1MB,1) + " MB")
    Say ("     " + ($info -replace '\s+',' '))
    $vol=(& $ffmpeg -hide_banner -i "$o" -af volumedetect -f null NUL 2>&1 | Select-String 'mean_volume|max_volume') -join ' / '
    Say ("     音量：" + ($vol -replace '\s+',' '))
    if($i -le 1){
      # 同一帧：neighbor 版（成品） vs 用 lanczos 重做一遍
      & $ffmpeg -hide_banner -loglevel error -y -ss 60 -i "$o" -frames:v 1 "$dir\_看片\清晰度-新-neighbor.png" 2>&1 | Out-Null
      & $ffmpeg -hide_banner -loglevel error -y -ss 60 -i "$o" -vf "scale=960:720:flags=lanczos,scale=1440:1080:flags=neighbor" -frames:v 1 "$dir\_看片\清晰度-对比-柔化.png" 2>&1 | Out-Null
      & $ffmpeg -hide_banner -loglevel error -y -ss 60 -i "$o" -frames:v 1 -vf "scale=1440:1080:flags=neighbor" "$dir\_看片\清晰度-新-原样.png" 2>&1 | Out-Null
      Say ("     锐度（越大越硬）：原样 " + (Sharp "$dir\_看片\清晰度-新-原样.png") + " / 先柔化再看 " + (Sharp "$dir\_看片\清晰度-对比-柔化.png"))
    }
  } else { Say ("  ❌ 没出片：" + $r.Name) }
}
Say ("共 $i / 4 个成品")
[System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
Say "设置已还原"