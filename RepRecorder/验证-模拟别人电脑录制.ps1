# 在"模拟别人电脑"环境里真的录一条 rep（验证端到端可用）
$sim='D:\临时-别人电脑测试'
$app="$sim\RepRecorder\RepRecorder.exe"
$cfg="$sim\RepRecorder\RepRecorder.settings.json"
$log="$sim\RepRecorder\RepRecorder.log"
$real='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123\replay'
function Say($m){ Write-Host $m }
$rep=Get-ChildItem -LiteralPath $real -Recurse -Force -File -Filter '*.rep' |
     Where-Object { $_.Length -ge 40KB -and $_.Length -le 120KB -and -not (Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($_.FullName,'.mp4'))) } |
     Sort-Object Length | Select-Object -First 1
if(-not $rep){ Say "❌ 找不到合适的 rep"; exit }
Say ("用 rep：" + $rep.Name)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.AutoStartOnDrop=$true; $j.ParallelCount=1; $j.SkipExisting=$true
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$pr=Start-Process -FilePath $app -ArgumentList ('"'+$rep.FullName+'"') -PassThru -WorkingDirectory "$sim\RepRecorder"
$sw=[Diagnostics.Stopwatch]::StartNew()
while($sw.Elapsed.TotalSeconds -lt 420){ Start-Sleep -Seconds 5
  if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } } }
Say ("用时 {0:N0} 秒" -f $sw.Elapsed.TotalSeconds)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$o=[System.IO.Path]::ChangeExtension($rep.FullName,'.mp4')
Say "---- 日志关键行 ----"
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '开始录像|完成：|音频自检|失败' | ForEach-Object { Say ("  " + $_.Line) }
if(Test-Path -LiteralPath $o){
  Say ("---- 成品：{0}  {1:N1} MB" -f (Split-Path $o -Leaf), ((Get-Item -LiteralPath $o).Length/1MB))
  $ff="$sim\RepRecorder\ffmpeg.exe"
  (& $ff -hide_banner -i "$o" 2>&1 | Select-String 'Stream #') | ForEach-Object { Say ("  " + ($_.Line -replace '\s+',' ')) }
} else { Say "❌ 没出成品" }