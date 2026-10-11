# 单录清晰度验证：画布应设成游戏窗口原生尺寸，录完用 neighbor 放大到 1440x1080
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$cfg="$dir\RepRecorder.settings.json"
$log="$dir\RepRecorder.log"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
Add-Type -AssemblyName System.Drawing
function Say($m){ Write-Host $m }
function Sharp($path){
  $bmp=[System.Drawing.Bitmap]::FromFile($path)
  try{ $sum=0.0;$n=0
    for($y=200;$y -lt [Math]::Min($bmp.Height,900);$y+=3){ for($x=1;$x -lt $bmp.Width;$x+=2){
      $a=$bmp.GetPixel($x,$y);$b=$bmp.GetPixel($x-1,$y)
      $sum += [Math]::Abs(($a.R+$a.G+$a.B)/3.0-($b.R+$b.G+$b.B)/3.0); $n++ } }
    return [Math]::Round($sum/$n,2) } finally { $bmp.Dispose() }
}
$rep=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter '*.rep' |
     Where-Object { $_.Length -ge 40KB -and $_.Length -le 150KB -and -not (Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($_.FullName,'.mp4'))) } |
     Sort-Object Length | Select-Object -First 1
if(-not $rep){ Say "❌ 找不到合适的 rep"; exit }
$o=[System.IO.Path]::ChangeExtension($rep.FullName,'.mp4')
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
if(Test-Path -LiteralPath $o){ Remove-Item -LiteralPath $o -Force }
$j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.ParallelCount=1; $j.AskSaveLocation=$false; $j.SkipExisting=$true; $j.AutoStartOnDrop=$true; $j.AutoCanvas=$true
$j.RecordGameAudio=$true
$j | Add-Member -NotePropertyName RecordVolume -NotePropertyValue 1.0 -Force
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
Say ("用 rep：" + $rep.Name)
$pr=Start-Process -FilePath "$dir\RepRecorder.exe" -ArgumentList ('"'+$rep.FullName+'"') -PassThru
$sw=[Diagnostics.Stopwatch]::StartNew()
while($sw.Elapsed.TotalSeconds -lt 600){ Start-Sleep -Seconds 5
  if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } } }
Say ("用时 {0:N0} 秒" -f $sw.Elapsed.TotalSeconds)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Say "---- 关键日志 ----"
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '原生尺寸|清晰度处理|居中适配|完成：|音频自检|真正开始|失败' | ForEach-Object { Say ("  " + $_.Line) }
if(Test-Path -LiteralPath $o){
  $info=(& $ffmpeg -hide_banner -i "$o" 2>&1 | Select-String 'Stream #') -join ' | '
  Say ("  成品 " + [math]::Round((Get-Item -LiteralPath $o).Length/1MB,1) + " MB")
  Say ("  " + ($info -replace '\s+',' '))
  & $ffmpeg -hide_banner -loglevel error -y -ss 60 -i "$o" -frames:v 1 "$dir\_看片\单录-新-原样.png" 2>&1 | Out-Null
  & $ffmpeg -hide_banner -loglevel error -y -ss 60 -i "$o" -vf "scale=960:720:flags=lanczos,scale=1440:1080:flags=neighbor" -frames:v 1 "$dir\_看片\单录-对比-柔化.png" 2>&1 | Out-Null
  Say ("  锐度：原样 " + (Sharp "$dir\_看片\单录-新-原样.png") + " / 柔化对照 " + (Sharp "$dir\_看片\单录-对比-柔化.png"))
} else { Say "❌ 没出成品" }
[System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
Say "设置已还原"