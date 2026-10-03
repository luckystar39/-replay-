# 发现：Windows 设置→音量合成器 的 UI 结构（看能不能自动化给 th123 换输出设备）
$root='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123'
$rep='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123\replay\【联机对战Rep】\[Aquila(3) ]\26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep'
$ini1=Join-Path $root 'modules\ReplayDnD\ReplayDnD.ini'
$ini2=Join-Path $root 'modules\ReplayDnD-old\ReplayDnD.ini'
$bak1=(Get-Content -LiteralPath $ini1 -Raw -Encoding UTF8); $bak2=(Get-Content -LiteralPath $ini2 -Raw -Encoding UTF8)
[System.IO.File]::WriteAllText($ini1,($bak1 -replace 'MuteMusic=1','MuteMusic=0'),(New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText($ini2,($bak2 -replace 'MuteMusic=1','MuteMusic=0'),(New-Object System.Text.UTF8Encoding($false)))

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE=[System.Windows.Automation.AutomationElement]

try{
  Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 2
  Start-Process -FilePath (Join-Path $root 'th123.exe') -ArgumentList ('"'+$rep+'"') -WorkingDirectory $root | Out-Null
  "游戏已启动，等 16 秒让它出声…"
  Start-Sleep -Seconds 16

  "打开 ms-settings:apps-volume …"
  Start-Process 'ms-settings:apps-volume' | Out-Null
  Start-Sleep -Seconds 8

  # 找设置窗口
  $wins = $AE::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
  $cand=@()
  foreach($w in $wins){
    $n=$w.Current.Name; $c=$w.Current.ClassName; $wpid=$w.Current.ProcessId
    if($n -and $n.Length -gt 0){ $cand += "$c | $n | pid=$wpid" }
  }
  "顶层窗口："; $cand | ForEach-Object { "   $_" }

  $settings = $wins | Where-Object { $_.Current.ClassName -eq 'ApplicationFrameWindow' -and ($_.Current.Name -match '设置|Settings') } | Select-Object -First 1
  if(-not $settings){ "⚠ 没找到设置窗口，改找 SystemSettings 进程的窗口"; 
     $settings = $wins | Where-Object { (Get-Process -Id $_.Current.ProcessId -ErrorAction SilentlyContinue).ProcessName -eq 'SystemSettings' } | Select-Object -First 1 }
  if(-not $settings){ "✗ 没有设置窗口可用"; exit 1 }
  "设置窗口 = $($settings.Current.Name)"

  # 找 th123 相关元素
  $all = $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  "元素总数 = $($all.Count)"
  $hit=@()
  foreach($e in $all){
    $n=$e.Current.Name
    if($n -and ($n -match 'th123|非想天则|扬声器|P32A2V|输出设备|Output device')){ $hit += $e }
  }
  "命中相关元素 $($hit.Count) 个："
  foreach($e in $hit | Select-Object -First 60){
    $ct=$e.Current.ControlType.ProgrammaticName -replace 'ControlType\.',''
    "   [$ct] name=「$($e.Current.Name)」 autoId=$($e.Current.AutomationId) class=$($e.Current.ClassName)"
  }
} finally {
  Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  [System.IO.File]::WriteAllText($ini1,$bak1,(New-Object System.Text.UTF8Encoding($false)))
  [System.IO.File]::WriteAllText($ini2,$bak2,(New-Object System.Text.UTF8Encoding($false)))
  "MuteMusic 已还原"
}
