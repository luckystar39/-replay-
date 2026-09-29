# 实测「静音」切换 与「刷新」按钮（精确定位子窗口并真点一下）
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"
$app="$dir\RepRecorder.exe"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class C1 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public struct RECT { public int L, T, R, B; }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
}
"@
[void][C1]::SetProcessDPIAware()

function Find-ChildByText($parent,$text){
  $script:hit=[IntPtr]::Zero
  $cb=[C1+EP]{ param($h,$l)
    if([C1]::Txt($h) -eq $text){ $script:hit=$h; return $false }
    return $true
  }
  [void][C1]::EnumChildWindows($parent,$cb,[IntPtr]::Zero)
  return $script:hit
}
function Click-Child($parent,$text){
  $h=Find-ChildByText $parent $text
  if($h -eq [IntPtr]::Zero){ "  没找到按钮: $text"; return $false }
  $r=New-Object C1+RECT
  [void][C1]::GetWindowRect($h,[ref]$r)
  $x=[int](($r.L+$r.R)/2); $y=[int](($r.T+$r.B)/2)
  [void][C1]::SetCursorPos($x,$y)
  Start-Sleep -Milliseconds 200
  [C1]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 80
  [C1]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  "  已点击「$text」($x,$y)"
  return $true
}

# 造一个"已有录像"的场景：给一个没有 mp4 的 rep 放一个假 mp4
$rep="$root\【完整包】非想天则整合包2026-06-15\th123\replay\【联机对战Rep】\[型WEI]\26-09-13_21-38-44_型WEI(灵梦)_Ciel(红美铃)_(0︰0_无).rep"
$dummy=[System.IO.Path]::ChangeExtension($rep,'.mp4')
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $dummy -Force -ErrorAction SilentlyContinue
Set-Content -LiteralPath $dummy -Value "dummy" -Encoding ASCII
"造了个假视频: " + (Split-Path $dummy -Leaf)

$pr=Start-Process -FilePath $app -ArgumentList ('"'+$rep+'"') -PassThru
Start-Sleep -Seconds 8
$pr.Refresh(); $h=$pr.MainWindowHandle
[void][C1]::SetForegroundWindow($h); Start-Sleep -Milliseconds 600

"---- 场景1：已有假视频 → 应标成「已录制」且不录制 ----"
Start-Sleep -Seconds 6
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String -Pattern '加入队列|已录制|开始录像|队列结束|刷新' | ForEach-Object { "  " + $_.Line }

"---- 场景2：删掉假视频 → 点刷新 → 应变回「等待」 ----"
Remove-Item -LiteralPath $dummy -Force
[void][C1]::SetForegroundWindow($h); Start-Sleep -Milliseconds 400
[void](Click-Child $h "刷新")
Start-Sleep -Seconds 2
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String -Pattern '已刷新队列' | ForEach-Object { "  " + $_.Line }

"---- 场景3：点「静音」应切换（第一次=静音，第二次=恢复） ----"
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
[void][C1]::SetForegroundWindow($h); Start-Sleep -Milliseconds 400
[void](Click-Child $h "静音")
Start-Sleep -Seconds 2
Start-Process -FilePath $app -ArgumentList @('--sys-mute','test') -Wait
"  第1次点击后: " + ((Get-Content -LiteralPath $log -Encoding UTF8 | Select-String 'sys-mute test').Line)
[void][C1]::SetForegroundWindow($h); Start-Sleep -Milliseconds 400
[void](Click-Child $h "静音 ✓")
Start-Sleep -Seconds 2
Start-Process -FilePath $app -ArgumentList @('--sys-mute','test') -Wait
"  第2次点击后: " + ((Get-Content -LiteralPath $log -Encoding UTF8 | Select-String 'sys-mute test' | Select-Object -Last 1).Line)
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
