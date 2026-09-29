# 验证"没有 N 卡也能用"：故意把切分编码器设成不存在的编码器，看是否自动退到 libx264 并成功出片
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$cfg="$dir\RepRecorder.settings.json"
$log="$dir\RepRecorder.log"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
function Say($m){ Write-Host $m }
$cand=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter '*.rep' |
      Where-Object { $_.Length -ge 40KB -and $_.Length -le 200KB -and -not (Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($_.FullName,'.mp4'))) } |
      Sort-Object Length | Select-Object -First 2
if($cand.Count -lt 2){ Say "❌ 找不到 2 个合适的 rep"; exit }
Say ("用：" + (($cand | ForEach-Object { $_.Name }) -join ' / '))
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.ParallelCount=2; $j.AskSaveLocation=$false; $j.SkipExisting=$true; $j.AutoStartOnDrop=$true
$j | Add-Member -NotePropertyName ParallelSplitEncoder -NotePropertyValue 'h264_this_does_not_exist' -Force
$j | Add-Member -NotePropertyName RoundRecord -NotePropertyValue $false -Force
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$a=@(); foreach($r in $cand){ $a += ('"'+$r.FullName+'"') }
$pr=Start-Process -FilePath "$dir\RepRecorder.exe" -ArgumentList $a -PassThru
$sw=[Diagnostics.Stopwatch]::StartNew()
while($sw.Elapsed.TotalSeconds -lt 900){ Start-Sleep -Seconds 5
  if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } } }
Say ("用时 {0:N0} 秒" -f $sw.Elapsed.TotalSeconds)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Say "---- 关键日志 ----"
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '自动改用 libx264|才成功|切分完成|切分失败|完成：' | ForEach-Object { Say ("  " + $_.Line) }
Say "---- 成品 ----"
foreach($r in $cand){
  $o=[System.IO.Path]::ChangeExtension($r.FullName,'.mp4')
  if(Test-Path -LiteralPath $o){
    $st=(& $ffmpeg -hide_banner -i "$o" 2>&1 | Select-String 'Stream #') -join ' | '
    Say ("  ✓ " + (Split-Path $o -Leaf) + "  " + [math]::Round((Get-Item -LiteralPath $o).Length/1MB,1) + " MB")
    Say ("    " + ($st -replace '\s+',' '))
  } else { Say ("  ❌ 没出片：" + $r.Name) }
}
[System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
Say "设置已还原"