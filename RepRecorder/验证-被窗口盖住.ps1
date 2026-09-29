# 关键测试：用别的窗口【盖住】游戏窗口（不最小化）时，画面还能不能录到
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"
$app="$dir\RepRecorder.exe"
$cfg="$dir\RepRecorder.settings.json"

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class C9 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public struct RECT { public int L, T, R, B; }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
}
"@
[void][C9]::SetProcessDPIAware()
function Click-Btn($h,$text){
  $script:hit=[IntPtr]::Zero
  $cb=[C9+EP]{ param($ch,$l)
    if([C9]::Txt($ch) -eq $text){ $script:hit=$ch; return $false }
    return $true }
  [void][C9]::EnumChildWindows($h,$cb,[IntPtr]::Zero)
  if($script:hit -eq [IntPtr]::Zero){ Write-Host "  没找到「$text」" -ForegroundColor Yellow; return }
  $r=New-Object C9+RECT; [void][C9]::GetWindowRect($script:hit,[ref]$r)
  [void][C9]::SetCursorPos([int](($r.L+$r.R)/2),[int](($r.T+$r.B)/2)); Start-Sleep -Milliseconds 200
  [C9]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 80
  [C9]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  Write-Host "  已点击「$text」" -ForegroundColor Green
}

Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
if(Get-Process obs64 -ErrorAction SilentlyContinue){ Start-Process -FilePath $app -ArgumentList @('--stop-record') -Wait }
$j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8

# 用一个长一点的 rep，方便观察
$rep="$root\【完整包】非想天则整合包2026-06-15\th123\replay\【观战Rep】\[Timepieceit LV2]_[menghenyinxin]\26-08-15_23-57-29_menghenyinxin(红美铃)_Timepieceit LV2(红美铃)_(1︰0_无).rep"
$out=[System.IO.Path]::ChangeExtension($rep,'.mp4')
Remove-Item -LiteralPath $out -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$pr=Start-Process -FilePath $app -ArgumentList ('"'+$rep+'"') -PassThru
Start-Sleep -Seconds 7
$pr.Refresh(); $h=$pr.MainWindowHandle
[void][C9]::SetForegroundWindow($h); Start-Sleep -Milliseconds 600
Click-Btn $h "开始录制"
$started=$false
for($i=0;$i -lt 60;$i++){
  Start-Sleep -Seconds 1
  if(Test-Path $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '开始录像'){ $started=$true; break } }
}
Write-Host ("  开始录像: " + $started) -ForegroundColor Cyan
Start-Sleep -Seconds 5

Write-Host "==== 用一个最大化窗口盖住游戏（不最小化） ====" -ForegroundColor Cyan
$np=Start-Process notepad -PassThru
Start-Sleep -Seconds 3
$np.Refresh()
[void][C9]::ShowWindow($np.MainWindowHandle,3)   # SW_MAXIMIZE
[void][C9]::SetForegroundWindow($np.MainWindowHandle)
Write-Host "  Notepad 已最大化并置于前台" -ForegroundColor Yellow
for($i=1;$i -le 8;$i++){
  Start-Sleep -Seconds 3
  $black=(Get-Content -LiteralPath $log -Encoding UTF8 | Select-String -Pattern '画面变黑' | Measure-Object).Count
  Write-Host ("    t={0,2}s  累计『画面变黑』告警数={1}" -f ($i*3), $black)
}
Write-Host "==== 现在最小化游戏（看能否自动还原） ====" -ForegroundColor Cyan
$g=Get-Process th123 -ErrorAction SilentlyContinue
if($g){
  [void][C9]::ShowWindow($g.MainWindowHandle,6)
  for($i=1;$i -le 5;$i++){
    Start-Sleep -Seconds 2
    $g2=Get-Process th123 -ErrorAction SilentlyContinue
    if(-not $g2){ break }
    $g2.Refresh()
    Write-Host ("    t={0,2}s  仍是最小化={1}" -f ($i*2), [C9]::IsIconic($g2.MainWindowHandle))
  }
}
for($i=0;$i -lt 80;$i++){
  Start-Sleep -Seconds 3
  if(Test-Path $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } }
}
Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host "==== 日志（只看关键行） ====" -ForegroundColor Cyan
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String -Pattern '开始录像|画面变黑|最小化|保留|自检|完成|恢复' | ForEach-Object { "  " + $_.Line }
Write-Host "==== 产物 ====" -ForegroundColor Cyan
if(Test-Path -LiteralPath $out){ Write-Host ("  mp4: {0:N1} MB" -f ((Get-Item -LiteralPath $out).Length/1MB)) } else { Write-Host "  没有 mp4" }
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
$j2=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j2.AskSaveLocation=$true
$j2 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
