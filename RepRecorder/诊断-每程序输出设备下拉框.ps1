# 诊断：音量合成器里「每个程序」到底有没有输出设备下拉框
. 'D:\【完整包】非想天则整合包2026-06-15\RepRecorder\音量合成器自动化.ps1'
$root='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123'
$rep='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123\replay\【联机对战Rep】\[Aquila(3) ]\26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep'
$ini1=Join-Path $root 'modules\ReplayDnD\ReplayDnD.ini'; $ini2=Join-Path $root 'modules\ReplayDnD-old\ReplayDnD.ini'
$bak1=(Get-Content -LiteralPath $ini1 -Raw -Encoding UTF8); $bak2=(Get-Content -LiteralPath $ini2 -Raw -Encoding UTF8)

"Windows 版本：$((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').ProductName) $((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').DisplayVersion) (build $((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber))"

[System.IO.File]::WriteAllText($ini1,($bak1 -replace 'MuteMusic=1','MuteMusic=0'),(New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText($ini2,($bak2 -replace 'MuteMusic=1','MuteMusic=0'),(New-Object System.Text.UTF8Encoding($false)))
try{
  Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 2
  $p=Start-Process -FilePath (Join-Path $root 'th123.exe') -ArgumentList ('"'+$rep+'"') -WorkingDirectory $root -PassThru
  Start-Sleep -Seconds 15
  $win = Open-VolumeMixer
  if(-not $win){ "✗ 没打开设置"; exit 1 }

  $g = Find-AppGroup $win $p.Id
  if(-not $g){ "✗ 找不到 app 条目 SystemSettings_Audio_Mixer_App$($p.Id)_EntityItem"; exit 1 }
  "找到 app 条目：$($g.Current.Name)  autoId=$($g.Current.AutomationId)"

  # 展开
  $btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  $btn = $g.FindFirst($TS::Descendants, $btnCond)
  if($btn){ "点展开按钮：$($btn.Current.Name)"; try{ $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch { "  展开失败 $($_.Exception.Message)" }; Start-Sleep -Seconds 2 }

  "----- 这个 app 条目下的所有元素 -----"
  $kids = $g.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  foreach($k in $kids){
    $ct=$k.Current.ControlType.ProgrammaticName -replace 'ControlType\.',''
    "   [$ct] name=「$($k.Current.Name)」 autoId=$($k.Current.AutomationId)"
  }
  "----- 整页里所有 ComboBox -----"
  $cbCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)
  foreach($c in $win.FindAll($TS::Descendants, $cbCond)){
    "   ComboBox name=「$($c.Current.Name)」 autoId=$($c.Current.AutomationId) offscreen=$($c.Current.IsOffscreen)"
  }
  "----- 整页里 autoId 含 OutputList 的元素 -----"
  foreach($e in $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)){
    if($e.Current.AutomationId -like '*OutputList*' -or $e.Current.AutomationId -like '*Mixer*'){
      $ct=$e.Current.ControlType.ProgrammaticName -replace 'ControlType\.',''
      "   [$ct] name=「$($e.Current.Name)」 autoId=$($e.Current.AutomationId)"
    }
  }
} finally {
  Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  [System.IO.File]::WriteAllText($ini1,$bak1,(New-Object System.Text.UTF8Encoding($false)))
  [System.IO.File]::WriteAllText($ini2,$bak2,(New-Object System.Text.UTF8Encoding($false)))
}
