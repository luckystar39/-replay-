# 验证：①选中几行后点列表空白处，选择不再被清空 ②滚轮滚过"同时录制数量"不改它
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$log="$dir\RepRecorder.log"
function Say($m){ Write-Host $m }
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @"
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public class LV {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static IntPtr Find(IntPtr root, string clsLike){
    IntPtr hit=IntPtr.Zero;
    EnumChildWindows(root, delegate(IntPtr c, IntPtr l){
      if(Cls(c).IndexOf(clsLike, StringComparison.OrdinalIgnoreCase)>=0){ hit=c; return false; }
      return true; }, IntPtr.Zero);
    return hit;
  }
  public static string ClickAt(IntPtr h, int x, int y){
    RECT r; if(!GetWindowRect(h, out r)) return "no rect";
    SetCursorPos(r.L + x, r.T + y);
    System.Threading.Thread.Sleep(150);
    mouse_event(0x0002,0,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(60);
    mouse_event(0x0004,0,0,0,UIntPtr.Zero);
    return "clicked at (" + x + "," + y + ")";
  }
  public static string ComboText(IntPtr root){
    IntPtr cb=Find(root, "ComboBox"); if(cb==IntPtr.Zero) return "?";
    return Txt(cb);
  }
  // 读 ListView 选中行数（LVM_GETSELECTEDCOUNT = 0x1032）
  public static int SelCount(IntPtr root){
    IntPtr lv=Find(root, "SysListView32"); if(lv==IntPtr.Zero) return -1;
    return (int)SendMessageW(lv, 0x1032, IntPtr.Zero, IntPtr.Zero);
  }
  // 选中一行（LVM_SETITEMSTATE 太麻烦，用点击坐标：点第 n 行）
  public static string ClickRow(IntPtr root, int rowIndex, int rowHeight){
    IntPtr lv=Find(root, "SysListView32"); if(lv==IntPtr.Zero) return "no list";
    RECT r; if(!GetWindowRect(lv, out r)) return "no rect";
    // 表头约 25px
    return ClickAt(lv, 200, 25 + rowHeight/2 + rowIndex*rowHeight);
  }
  public static int ListHeight(IntPtr root){
    IntPtr lv=Find(root, "SysListView32"); if(lv==IntPtr.Zero) return 0;
    RECT r; GetWindowRect(lv, out r); return r.B-r.T;
  }
}
"@
# 用 8 个"已完成"的 rep（就是用户图里那批，旁边已有 mp4 → 会弹重录询问，选否即可）
$reps=Get-ChildItem -LiteralPath "$replay\【联机对战Rep】\[Burning Star]" -File -Filter '*.rep' -ErrorAction SilentlyContinue |
      Where-Object { Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($_.FullName,'.mp4')) } | Sort-Object Name | Select-Object -First 8
if($reps.Count -lt 8){ $reps = Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter '*.rep' | Where-Object { Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($_.FullName,'.mp4')) } | Select-Object -First 8 }
Say ("用 " + $reps.Count + " 个已录制的 rep（只会弹一次重录询问，点否）")
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$a=@(); foreach($r in $reps){ $a += ('"'+$r.FullName+'"') }
$pr=Start-Process -FilePath "$dir\RepRecorder.exe" -ArgumentList $a -PassThru
Start-Sleep -Seconds 14
$pr.Refresh(); $h=$pr.MainWindowHandle
# 关掉"要重录吗"的询问（点「否」）
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class DLG {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static IntPtr FindDlg(){
    IntPtr f=IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l){ if(Cls(h)=="#32770" && Txt(h).Contains("已经录制过")){ f=h; return false; } return true; }, IntPtr.Zero);
    return f; }
  public static string ClickNo(IntPtr dlg){
    IntPtr hit=IntPtr.Zero;
    EnumChildWindows(dlg, delegate(IntPtr c, IntPtr l){
      if(Cls(c)=="Button" && Txt(c).StartsWith("否")){ hit=c; return false; } return true; }, IntPtr.Zero);
    if(hit==IntPtr.Zero) return "没找到「否」";
    SendMessageW(hit,0x00F5,IntPtr.Zero,IntPtr.Zero); return "已点「否」"; }
}
"@
$d=[IntPtr]::Zero
for($i=0;$i -lt 16 -and $d -eq [IntPtr]::Zero;$i++){ Start-Sleep -Milliseconds 500; $d=[DLG]::FindDlg() }
if($d -ne [IntPtr]::Zero){ Say ("重录询问：" + [DLG]::ClickNo($d)); Start-Sleep -Seconds 2 }
Start-Sleep -Seconds 2
Say ("初始选中行数：" + [LV]::SelCount($h) + "   数量下拉框=" + [LV]::ComboText($h) + "   列表高度=" + [LV]::ListHeight($h))
# 选中第 3~7 行（Ctrl 多选太麻烦，改用"Shift 范围选择"：点第 3 行，再 Shift 点第 7 行）
Say ([LV]::ClickRow($h, 2, 21))
Start-Sleep -Milliseconds 400
Add-Type @"
using System; using System.Runtime.InteropServices;
public class KEY {
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public static void Down(byte vk){ keybd_event(vk,0,0,UIntPtr.Zero); }
  public static void Up(byte vk){ keybd_event(vk,0,2,UIntPtr.Zero); }
}
"@
[KEY]::Down(0x10)   # Shift
Say ([LV]::ClickRow($h, 6, 21))
[KEY]::Up(0x10)
Start-Sleep -Milliseconds 600
$before=[LV]::SelCount($h)
Say ("选完 5 行后：选中行数=" + $before + "   数量下拉框=" + [LV]::ComboText($h))
# 点列表下方空白处（列表高约 300+，点 y=430 这种空区）
Say ("点空白处：" + [LV]::ClickAt(([LV]::Find($h,"SysListView32")), 300, [Math]::Max(120, [LV]::ListHeight($h)-30)))
Start-Sleep -Seconds 1
$after=[LV]::SelCount($h)
Say ("点完之后：选中行数=" + $after + "   数量下拉框=" + [LV]::ComboText($h))
Say ("→ 点空白后蓝色选中数=$after（期望 0），数量下拉框=" + [LV]::ComboText($h) + "（期望 5）")
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '空白处|同时录制数量改为' | Select-Object -Last 6 | ForEach-Object { Say ("  日志| " + $_.Line) }