# A 键加速重测：强制抢焦点 + 模拟点击游戏窗口中心 + 按住 A
$root='D:\【完整包】非想天则整合包2026-06-15'
$gamedir="$root\【完整包】非想天则整合包2026-06-15\th123"
$rep="$root\【完整包】非想天则整合包2026-06-15\th123\replay\【观战Rep】\[Timepieceit LV2]_[menghenyinxin]\26-08-15_23-57-29_menghenyinxin(红美铃)_Timepieceit LV2(红美铃)_(1︰0_无).rep"
$out="$root\RepRecorder\A键重测结果.txt"
Remove-Item -LiteralPath $out -Force -ErrorAction SilentlyContinue

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class F {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public struct RECT { public int L, T, R, B; }
}
"@

function Run-One([bool]$accelerate){
  Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Seconds 2
  $p=Start-Process -FilePath "$gamedir\th123.exe" -ArgumentList ('"'+$rep+'"') -WorkingDirectory $gamedir -PassThru
  $hwnd=[IntPtr]::Zero
  for($i=0;$i -lt 160;$i++){ Start-Sleep -Milliseconds 500; $p.Refresh(); if($p.HasExited){break}; if($p.MainWindowHandle -ne [IntPtr]::Zero){ $hwnd=$p.MainWindowHandle; break } }
  if($hwnd -eq [IntPtr]::Zero){ Add-Content -LiteralPath $out -Value "窗口没出现"; return }
  Start-Sleep -Seconds 8
  $t0=[System.Diagnostics.Stopwatch]::StartNew()
  if($accelerate){
    # 1) 抢焦点（AttachThreadInput + Alt 技巧），并校验
    [F]::keybd_event(0x12,0x38,0,[IntPtr]::Zero); [F]::keybd_event(0x12,0x38,2,[IntPtr]::Zero)
    $fg=[F]::GetForegroundWindow()
    $fgTid=[F]::GetWindowThreadProcessId($fg,[IntPtr]::Zero)
    $myTid=[F]::GetCurrentThreadId()
    [void][F]::AttachThreadInput($myTid,$fgTid,$true)
    [void][F]::ShowWindow($hwnd,9)
    [void][F]::BringWindowToTop($hwnd)
    $ok=[F]::SetForegroundWindow($hwnd)
    [void][F]::AttachThreadInput($myTid,$fgTid,$false)
    Start-Sleep -Milliseconds 400
    $nowFg=[F]::GetForegroundWindow()
    Add-Content -LiteralPath $out -Value ("  抢焦点: SetForegroundWindow={0}  当前前台是否游戏窗口: {1}" -f $ok, ($nowFg -eq $hwnd))
    # 2) 点一下窗口中心（模拟你说的"鼠标点一下游戏框"）
    $r=New-Object F+RECT
    if([F]::GetWindowRect($hwnd,[ref]$r)){
      $cx=[int](($r.L+$r.R)/2); $cy=[int](($r.T+$r.B)/2)
      [void][F]::SetCursorPos($cx,$cy)
      Start-Sleep -Milliseconds 200
      [F]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero)   # 左键按下
      Start-Sleep -Milliseconds 80
      [F]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)   # 左键抬起
      Add-Content -LiteralPath $out -Value ("  已点击窗口中心 ({0},{1})" -f $cx,$cy)
    }
    Start-Sleep -Milliseconds 500
    # 3) 按住 A
    [F]::keybd_event(0x41,0x1E,0,[IntPtr]::Zero)
    Add-Content -LiteralPath $out -Value "  已按住 A"
  }
  while(-not $p.HasExited -and $t0.Elapsed.TotalSeconds -lt 200){ Start-Sleep -Milliseconds 300; $p.Refresh() }
  $el=[math]::Round($t0.Elapsed.TotalSeconds,1)
  if($accelerate){ [F]::keybd_event(0x41,0x1E,2,[IntPtr]::Zero) }
  if(-not $p.HasExited){ $p.Kill(); Add-Content -LiteralPath $out -Value "  (超时强制结束)" }
  Add-Content -LiteralPath $out -Value ("  => 播放用时 {0} 秒" -f $el)
}

Add-Content -LiteralPath $out -Value ("==== {0} 开始 ====" -f (Get-Date -Format 'HH:mm:ss'))
Add-Content -LiteralPath $out -Value "[测试：抢焦点+点击+A键按住]"
Run-One $true
Add-Content -LiteralPath $out -Value "[对照：正常播放]"
Run-One $false
Add-Content -LiteralPath $out -Value "==== 完成 ===="
Get-Content -LiteralPath $out
