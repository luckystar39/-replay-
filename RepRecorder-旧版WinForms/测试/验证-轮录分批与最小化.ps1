# 验证需求6（7 个 rep，数量自动=6 → 6+1 两批，末批降级单录）+ 需求2（并行最小化自动还原）
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$cfg="$dir\RepRecorder.settings.json"
$log="$dir\RepRecorder.log"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
function Say($m){ Write-Host $m }
Add-Type @"
using System; using System.Runtime.InteropServices;
public class MN {
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
# 有效但很短的小 rep：40KB ~ 500KB，且没有成品
$cand=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter '*.rep' |
      Where-Object { $_.Length -ge 40KB -and $_.Length -le 500KB -and -not (Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($_.FullName,'.mp4'))) } |
      Sort-Object Length | Select-Object -First 7
Say ("7 个 rep（大小）：" + (($cand | ForEach-Object { "$([math]::Round($_.Length/1KB,0))KB" }) -join ', '))
if($cand.Count -lt 7){ Say "❌ 小 rep 不够 7 个"; exit }
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.SkipExisting=$true; $j.AutoStartOnDrop=$true; $j.ParallelCount=1
$j | Add-Member -NotePropertyName RoundRecord -NotePropertyValue $true -Force
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$a=@(); foreach($r in $cand){ $a += ('"'+$r.FullName+'"') }
$pr=Start-Process -FilePath "$dir\RepRecorder.exe" -ArgumentList $a -PassThru
# 等并行批次起来后最小化一个游戏窗口
$sawParallel=$false; $didMin=$false
$sw=[Diagnostics.Stopwatch]::StartNew()
while($sw.Elapsed.TotalSeconds -lt 1500){
  Start-Sleep -Seconds 4
  if(-not $sawParallel -and (Test-Path -LiteralPath $log)){
    if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '并行录制：已开始录像'){ $sawParallel=$true }
  }
  if($sawParallel -and -not $didMin){
    foreach($p in (Get-Process th123 -ErrorAction SilentlyContinue)){
      try{ $p.Refresh(); if($p.MainWindowHandle -ne [IntPtr]::Zero -and -not [MN]::IsIconic($p.MainWindowHandle)){
        [void][MN]::ShowWindow($p.MainWindowHandle, 6); $didMin=$true
        Say "已在并行录制中把 1 个游戏窗口最小化（测试需求2）"
        break } }catch{}
    }
  }
  if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } }
}
Say ("整轮用时 {0:N0} 秒" -f $sw.Elapsed.TotalSeconds)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Say "---- 关键日志 ----"
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '轮录开始|第 \d 批|只有 1 个|自动设为|检测到第|已开始录像|切分完成|完成：|队列结束' | ForEach-Object { Say ("  " + $_.Line) }
Say "---- 成品 ----"
$mk=0
foreach($r in $cand){ $o=[System.IO.Path]::ChangeExtension($r.FullName,'.mp4')
  if(Test-Path -LiteralPath $o){ $mk++; Say ("  $([System.IO.Path]::GetFileName($o))  $([math]::Round((Get-Item -LiteralPath $o).Length/1MB,1)) MB") } }
Say ("  共 $mk / 7 个成品")
[System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
Say "设置已还原"