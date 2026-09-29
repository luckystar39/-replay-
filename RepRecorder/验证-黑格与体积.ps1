# 验证：①第一格不再全黑（空标题兜底 + 黑屏自愈）②方形像素 SAR=1:1 ③CQ 23 后体积
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$cfg="$dir\RepRecorder.settings.json"
$log="$dir\RepRecorder.log"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
function Say($m){ Write-Host $m }
$cand=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter '*.rep' |
      Where-Object { $_.Length -ge 40KB -and $_.Length -le 150KB -and -not (Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($_.FullName,'.mp4'))) } |
      Sort-Object Length | Select-Object -First 3
if($cand.Count -lt 3){ Say "❌ 小 rep 不够"; exit }
Say ("用 3 个 rep：" + (($cand | ForEach-Object { $_.Name.Substring(0,14) }) -join ' / '))
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.ParallelCount=3; $j.AskSaveLocation=$false; $j.SkipExisting=$true; $j.AutoStartOnDrop=$true; $j.AutoCanvas=$true
$j.RecordGameAudio=$true; $j.ParallelSplitCQ=23
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
Say "---- 黑屏/标题相关的日志（应该没有'改用强制标题'才最理想，有也没关系）----"
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '强制标题|画面是黑的|一直是黑屏|绑定窗口串|切分完成|真正开始录像' | ForEach-Object { Say ("  " + $_.Line) }
Say "---- 成品 ----"
foreach($r in $cand){
  $o=[System.IO.Path]::ChangeExtension($r.FullName,'.mp4')
  if(Test-Path -LiteralPath $o){
    $fi=Get-Item -LiteralPath $o
    $sec=((& $ffmpeg -hide_banner -i "$o" 2>&1 | Select-String 'Duration: (\d+):(\d+):(\d+)') -replace '.*Duration: (\d+):(\d+):(\d+).*','$1h$2m$3s')
    $kbps=[math]::Round($fi.Length*8/1000/172,0)
    $info=(& $ffmpeg -hide_banner -i "$o" 2>&1 | Select-String 'Stream #0:0|Stream #0:1') -join ' | '
    Say ("  " + $fi.Name.Substring(0,16) + "… " + [math]::Round($fi.Length/1MB,0) + " MB  约 " + $kbps + " kbps")
    Say ("     " + ($info -replace '\s+',' '))
  } else { Say ("  ❌ 没出片：" + $r.Name) }
}
[System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
Say "设置已还原"