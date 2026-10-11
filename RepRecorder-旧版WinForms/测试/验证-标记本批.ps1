# 验证"点空白 → 蓝色取消、变成绿色标记、数量=5"
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$log="$dir\RepRecorder.log"
function Say($m){ Write-Host $m }
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @"
using System; using System.Text; using System.Runtime.InteropServices;
public class U2 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static IntPtr Find(IntPtr root, string clsLike){
    IntPtr hit=IntPtr.Zero;
    EnumChildWindows(root, delegate(IntPtr c, IntPtr l){
      if(Cls(c).IndexOf(clsLike, StringComparison.OrdinalIgnoreCase)>=0){ hit=c; return false; } return true; }, IntPtr.Zero);
    return hit; }
  public static IntPtr FindDlg(){
    IntPtr f=IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l){ if(Cls(h)=="#32770" && Txt(h).Contains("已经录制过")){ f=h; return false; } return true; }, IntPtr.Zero);
    return f; }
  public static string ClickNo(IntPtr dlg){
    IntPtr hit=IntPtr.Zero;
    EnumChildWindows(dlg, delegate(IntPtr c, IntPtr l){ if(Cls(c)=="Button" && Txt(c).StartsWith("否")){ hit=c; return false; } return true; }, IntPtr.Zero);
    if(hit==IntPtr.Zero) return "没找到否"; SendMessageW(hit,0x00F5,IntPtr.Zero,IntPtr.Zero); return "已点否"; }
  public static int SelCount(IntPtr root){
    IntPtr lv=Find(root,"SysListView32"); if(lv==IntPtr.Zero) return -1;
    return (int)SendMessageW(lv, 0x1032, IntPtr.Zero, IntPtr.Zero); }
  public static string ComboText(IntPtr root){
    IntPtr cb=Find(root,"ComboBox"); return cb==IntPtr.Zero?"?":Txt(cb); }
  public static string ClickRow(IntPtr root, int row, int rowH){
    IntPtr lv=Find(root,"SysListView32"); RECT r; GetWindowRect(lv, out r);
    SetCursorPos(r.L+200, r.T+25+rowH/2+row*rowH);
    System.Threading.Thread.Sleep(150);
    mouse_event(0x0002,0,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(0x0004,0,0,0,UIntPtr.Zero);
    return "click row " + row; }
  public static string ShiftDown(int times){
    keybd_event(0x10,0,0,UIntPtr.Zero);
    for(int i=0;i<times;i++){ keybd_event(0x28,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(80); keybd_event(0x28,0,2,UIntPtr.Zero); System.Threading.Thread.Sleep(80); }
    keybd_event(0x10,0,2,UIntPtr.Zero);
    return "shift down x" + times; }
  public static string ClickEmpty(IntPtr root, int rowH){
    IntPtr lv=Find(root,"SysListView32"); RECT r; GetWindowRect(lv, out r);
    SetCursorPos(r.L+300, r.T+25+rowH*8+20);
    System.Threading.Thread.Sleep(150);
    mouse_event(0x0002,0,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(0x0004,0,0,0,UIntPtr.Zero);
    return "click empty"; }
}
"@
$reps=Get-ChildItem -LiteralPath "$replay\【联机对战Rep】\[Burning Star]" -File -Filter '*.rep' -ErrorAction SilentlyContinue |
      Where-Object { Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($_.FullName,'.mp4')) } | Sort-Object Name | Select-Object -First 8
if($reps.Count -lt 8){
  $reps=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter '*.rep' -ErrorAction SilentlyContinue |
        Where-Object { $_.Length -gt 5KB } | Sort-Object FullName | Select-Object -First 8
}
"Say `"用 $($reps.Count) 个 rep`""
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$a=@(); foreach($r in $reps){ $a += ('"'+$r.FullName+'"') }
$pr=Start-Process -FilePath "$dir\RepRecorder.exe" -ArgumentList $a -PassThru
Start-Sleep -Seconds 14
$pr.Refresh(); $h=$pr.MainWindowHandle
$d=[IntPtr]::Zero
for($i=0;$i -lt 16 -and $d -eq [IntPtr]::Zero;$i++){ Start-Sleep -Milliseconds 500; $d=[U2]::FindDlg() }
if($d -ne [IntPtr]::Zero){ Say ("重录询问：" + [U2]::ClickNo($d)); Start-Sleep -Seconds 2 }
[void][U2]::SetForegroundWindow($h); Start-Sleep -Milliseconds 300
Say ([U2]::ClickRow($h, 2, 21)); Start-Sleep -Milliseconds 300
Say ([U2]::ShiftDown(4)); Start-Sleep -Milliseconds 600
Say ("选中行数=" + [U2]::SelCount($h) + "  数量=" + [U2]::ComboText($h))
Say ([U2]::ClickEmpty($h, 21)); Start-Sleep -Seconds 1
Say ("点空白后：蓝色选中=" + [U2]::SelCount($h) + "（期望 0）  数量=" + [U2]::ComboText($h) + "（期望 5）")
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '空白处|同时录制数量改为' | Select-Object -Last 4 | ForEach-Object { Say ("  日志| " + $_.Line) }