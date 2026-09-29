# 验证：需求1（录制时静音麦克风、录完恢复）/ 需求3（优先WGC捕获）/ 需求2（保存位置对话框）
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class W {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
}
"@

function Find-Dialog($targetPid){
  $found=[IntPtr]::Zero
  $cb=[W+EP]{ param($h,$l)
    $p=0; [void][W]::GetWindowThreadProcessId($h,[ref]$p)
    if($p -eq $targetPid -and [W]::IsWindowVisible($h)){
      $t=[W]::Txt($h)
      if($t -like '*录制完成*'){ $script:found=$h; return $false }
    }
    return $true
  }
  [void][W]::EnumWindows($cb,[IntPtr]::Zero)
  return $script:found
}

# 1) 取消麦克风静音，便于验证"录制时静音 -> 录完恢复"
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
Start-Process -FilePath "$dir\RepRecorder.exe" -ArgumentList @('--mic','off') -Wait
"==== 取消静音后 ===="
Get-Content -LiteralPath $log -Encoding UTF8 | Select-Object -Last 3

$rep="$root\【完整包】非想天则整合包2026-06-15\th123\replay\【观战Rep】\[Timepieceit LV2]_[menghenyinxin]\26-08-15_23-57-29_menghenyinxin(红美铃)_Timepieceit LV2(红美铃)_(1︰0_无).rep"
$out=[System.IO.Path]::ChangeExtension($rep,'.mp4')
Remove-Item -LiteralPath $out -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue

"==== 开始录制 ===="
$pr=Start-Process -FilePath "$dir\RepRecorder.exe" -ArgumentList ('"'+$rep+'"') -PassThru
$dialogSeen=$false
for($i=1;$i -le 90;$i++){
  Start-Sleep -Seconds 2
  $h=Find-Dialog $pr.Id
  if($h -ne [IntPtr]::Zero -and -not $dialogSeen){
    $dialogSeen=$true
    ">> 检测到保存位置对话框，按回车选择【留在 rep 旁边】"
    [void][W]::SetForegroundWindow($h)
    Start-Sleep -Milliseconds 400
    [W]::keybd_event(0x0D,0x1C,0,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    [W]::keybd_event(0x0D,0x1C,2,[IntPtr]::Zero)
  }
  if(Test-Path $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } }
}
"==== 录制日志 ===="
Get-Content -LiteralPath $log -Encoding UTF8
"==== 录完后麦克风状态 ===="
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
Start-Process -FilePath "$dir\RepRecorder.exe" -ArgumentList @('--mic','test') -Wait
Get-Content -LiteralPath $log -Encoding UTF8 | Select-Object -Last 3
"对话框是否出现: $dialogSeen"
if(Test-Path -LiteralPath $out){ "mp4: {0:N2} MB" -f ((Get-Item -LiteralPath $out).Length/1MB) } else { "没有 mp4" }
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
