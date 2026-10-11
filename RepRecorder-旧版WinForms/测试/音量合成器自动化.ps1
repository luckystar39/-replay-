# 库：用 UI 自动化操作 Windows「音量合成器」给指定进程换输出设备
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$script:AE=[System.Windows.Automation.AutomationElement]
$script:TS=[System.Windows.Automation.TreeScope]
$script:CT=[System.Windows.Automation.ControlType]

function Get-SettingsWindow {
  $wins = $AE::RootElement.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition)
  foreach($w in $wins){
    if($w.Current.ClassName -eq 'ApplicationFrameWindow' -and $w.Current.Name -eq '设置'){ return $w }
  }
  return $null
}

function Open-VolumeMixer {
  Start-Process 'ms-settings:apps-volume' | Out-Null
  $w=$null
  for($i=0;$i -lt 40;$i++){ Start-Sleep -Milliseconds 500; $w=Get-SettingsWindow; if($w){ break } }
  Start-Sleep -Seconds 3
  return (Get-SettingsWindow)
}

function Find-ById($root,$autoId){
  $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $autoId)
  return $root.FindFirst($TS::Descendants, $cond)
}

function Find-AppGroup($win,$appPid){
  $id = "SystemSettings_Audio_Mixer_App${appPid}_EntityItem"
  for($i=0;$i -lt 12;$i++){
    $g = Find-ById $win $id
    if($g){ return $g }
    # 找不到就往下滚一点（应用列表可能是虚拟化的）
    $svCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Pane)
    $panes = $win.FindAll($TS::Descendants, $svCond)
    foreach($p in $panes){
      try{
        $sp=$p.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        $sp.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount, [System.Windows.Automation.ScrollAmount]::LargeIncrement)
      } catch {}
    }
    Start-Sleep -Milliseconds 700
  }
  return $null
}

function Get-AppOutputDevice($win,$appPid){
  $g = Find-AppGroup $win $appPid
  if(-not $g){ return "找不到该程序在音量合成器里的条目" }
  $combo = Find-ById $g 'SystemSettings_Audio_OutputList_ComboBox'
  if(-not $combo){ return "找不到『输出设备』下拉框（条目可能没展开）" }
  $sel = $combo.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
  foreach($s in $sel){ if($s.Current.Name){ return $s.Current.Name } }
  return "（下拉框当前没有选中项）"
}

# 把某个进程的输出设备切成名字匹配 $deviceName 的设备；返回结果说明
function Set-AppOutputDevice($win,$appPid,$deviceName){
  $g = Find-AppGroup $win $appPid
  if(-not $g){ return "找不到该程序在音量合成器里的条目" }
  $combo = Find-ById $g 'SystemSettings_Audio_OutputList_ComboBox'
  if(-not $combo){
    $btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $btn = $g.FindFirst($TS::Descendants, $btnCond)
    if($btn){ try{ $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 1200 } catch {} }
    $combo = Find-ById $g 'SystemSettings_Audio_OutputList_ComboBox'
  }
  if(-not $combo){ return "展开后仍找不到『输出设备』下拉框" }
  try{ $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 1500 } catch { return "展开下拉框失败: $($_.Exception.Message)" }
  $items = $combo.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
  $names=@(); $target=$null
  foreach($it in $items){ if($it.Current.Name){ $names += $it.Current.Name; if($it.Current.Name -like "*$deviceName*"){ $target=$it } } }
  if(-not $target){ return "下拉框里没有匹配「$deviceName」的项，实际项：$($names -join ' / ')" }
  try{ $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { return "选中失败: $($_.Exception.Message)" }
  Start-Sleep -Seconds 2
  return "已选择「$($target.Current.Name)」"
}
