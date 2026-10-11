# 验证：①轮录两批时第二批不继承旧源/旧音轨 ②每格音源状态日志 ③清晰度回到 lanczos
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
Say ("用 3 个 rep（同时数量=2 → 2+1 两批）：" + (($cand | ForEach-Object { $_.Name.Substring(0,12) }) -join ' / '))
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.ParallelCount=2; $j.AskSaveLocation=$false; $j.SkipExisting=$true; $j.AutoStartOnDrop=$true
$j.RecordGameAudio=$true; $j.RoundRecord=$true
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$a=@(); foreach($r in $cand){ $a += ('"'+$r.FullName+'"') }
$pr=Start-Process -FilePath "$dir\RepRecorder.exe" -ArgumentList $a -PassThru
$sw=[Diagnostics.Stopwatch]::StartNew()
while($sw.Elapsed.TotalSeconds -lt 1200){ Start-Sleep -Seconds 5
  if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } } }
Say ("整轮用时 {0:N0} 秒" -f $sw.Elapsed.TotalSeconds)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Say "---- 两批的关键日志 ----"
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '轮录开始|第 \d 批|已清理上一批|音源状态|绑定窗口串|游戏声音进录像|切分完成|队列结束' | ForEach-Object { Say ("  " + $_.Line) }
Say "---- 成品（音轨数 + SAR）----"
foreach($r in $cand){
  $o=[System.IO.Path]::ChangeExtension($r.FullName,'.mp4')
  if(Test-Path -LiteralPath $o){
    $info=(& $ffmpeg -hide_banner -i "$o" 2>&1 | Select-String 'Stream #') -join ' | '
    Say ("  " + (Split-Path $o -Leaf).Substring(0,14) + "… " + [math]::Round((Get-Item -LiteralPath $o).Length/1MB,0) + " MB")
    Say ("     " + ($info -replace '\s+',' '))
  } else { Say ("  ❌ 没出片：" + $r.Name) }
}
[System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
Say "设置已还原"