# 最终回归：不自动开始 → 点「开始录制」→ 程序自己拉起 OBS（UseShellExecute=false 的新启动路径）→ 录完
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"
$app="$dir\RepRecorder.exe"
$cfg="$dir\RepRecorder.settings.json"

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class C4 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public struct RECT { public int L, T, R, B; }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
}
"@
[void][C4]::SetProcessDPIAware()
function Click-Child($parent,$text){
  $script:hit=[IntPtr]::Zero
  $cb=[C4+EP]{ param($ch,$l)
    if([C4]::Txt($ch) -eq $text){ $script:hit=$ch; return $false }
    return $true }
  [void][C4]::EnumChildWindows($parent,$cb,[IntPtr]::Zero)
  if($script:hit -eq [IntPtr]::Zero){ "  没找到「$text」"; return $false }
  $r=New-Object C4+RECT; [void][C4]::GetWindowRect($script:hit,[ref]$r)
  [void][C4]::SetCursorPos([int](($r.L+$r.R)/2),[int](($r.T+$r.B)/2)); Start-Sleep -Milliseconds 200
  [C4]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 80
  [C4]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  "  已点击「$text」"
  return $true
}

# 关掉 OBS + 游戏，临时关掉保存位置询问
Get-Process obs64,th123,RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3
$j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
"OBS 已关闭: " + (-not [bool](Get-Process obs64 -ErrorAction SilentlyContinue))

$rep="$root\【完整包】非想天则整合包2026-06-15\th123\replay\【联机对战Rep】\[型WEI]\26-09-13_21-38-44_型WEI(灵梦)_Ciel(红美铃)_(0︰0_无).rep"
$out=[System.IO.Path]::ChangeExtension($rep,'.mp4')
Remove-Item -LiteralPath $out -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$pr=Start-Process -FilePath $app -ArgumentList ('"'+$rep+'"') -PassThru
Start-Sleep -Seconds 7
$pr.Refresh(); $h=$pr.MainWindowHandle
[void][C4]::SetForegroundWindow($h); Start-Sleep -Milliseconds 600

"==== 步骤1：拖入后（不应开始录制） ===="
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String -Pattern '加入队列|OBS 未运行|开始录像' | ForEach-Object { "  " + $_.Line }
"  OBS 运行中: " + [bool](Get-Process obs64 -ErrorAction SilentlyContinue) + "   游戏运行中: " + [bool](Get-Process th123 -ErrorAction SilentlyContinue)

"==== 步骤2：点「开始录制」 ===="
[void](Click-Child $h "开始录制")
for($i=1;$i -le 70;$i++){
  Start-Sleep -Seconds 3
  if(Test-Path $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } }
}
"==== 日志 ===="
Get-Content -LiteralPath $log -Encoding UTF8
"==== 产物 ===="
if(Test-Path -LiteralPath $out){ "  mp4: {0:N1} MB" -f ((Get-Item -LiteralPath $out).Length/1MB) } else { "  没有 mp4" }
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
$j2=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j2.AskSaveLocation=$true
$j2 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
"已还原 AskSaveLocation=true"
