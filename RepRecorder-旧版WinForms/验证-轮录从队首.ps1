# 验证"轮录 = 从队列第一个 rep 开始数"（只看日志开头那行，不用等录完）
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$log="$dir\RepRecorder.log"
function Say($m){ Write-Host $m }
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class CK10 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr c, string cls, string win);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static bool Click(IntPtr root, string like){
    IntPtr hit=IntPtr.Zero;
    EnumChildWindows(root, delegate(IntPtr c, IntPtr l){
      if(Cls(c).IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase)>=0){ string t=Txt(c);
        if(!string.IsNullOrEmpty(t) && t.Contains(like)){ hit=c; return false; } }
      return true; }, IntPtr.Zero);
    if(hit==IntPtr.Zero) return false;
    SendMessageW(hit,0x00F5,IntPtr.Zero,IntPtr.Zero); return true; }
  // 选中若干行（从第 startRow 行开始选 count 行），模拟"选中下面几个"
  public static string SelectRows(IntPtr root, int startRow, int count){
    IntPtr lv=FindWindowEx(root, IntPtr.Zero, "SysListView32", null);
    if(lv==IntPtr.Zero) return "找不到列表";
    RECT r; GetWindowRect(lv, out r);
    int rowH=21;
    SetCursorPos(r.L+200, r.T+25+rowH/2+startRow*rowH);
    System.Threading.Thread.Sleep(200);
    mouse_event(0x0002,0,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(0x0004,0,0,0,UIntPtr.Zero);
    System.Threading.Thread.Sleep(200);
    // Shift + Down 选后续
    keybd_event(0x10,0,0,UIntPtr.Zero);
    for(int i=1;i<count;i++){ keybd_event(0x28,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(70); keybd_event(0x28,0,2,UIntPtr.Zero); System.Threading.Thread.Sleep(70); }
    keybd_event(0x10,0,2,UIntPtr.Zero);
    return "已选中第 " + (startRow+1) + " 行起的 " + count + " 行";
  }
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public static int SelCount(IntPtr root){
    IntPtr lv=FindWindowEx(root, IntPtr.Zero, "SysListView32", null);
    if(lv==IntPtr.Zero) return -1;
    return (int)SendMessageW(lv, 0x1032, IntPtr.Zero, IntPtr.Zero); }
}
"@
# 队列：4 个已录 + 1 个待录（顺序无所谓，程序会按文件名排序）
$done = @(Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter '*.rep' -ErrorAction SilentlyContinue |
      Where-Object { Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($_.FullName,'.mp4')) } | Sort-Object Name | Select-Object -First 4)
$todo = @(Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter '*.rep' -ErrorAction SilentlyContinue |
      Where-Object { $_.Length -ge 40KB -and $_.Length -le 150KB -and -not (Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($_.FullName,'.mp4'))) } | Select-Object -First 1)
Say ("队列共 " + ($done.Count + $todo.Count) + " 个（已录 " + $done.Count + " + 待录 " + $todo.Count + "）")
if($todo.Count -lt 1){ Say "❌ 找不到待录的 rep"; exit }
$cfg="$dir\RepRecorder.settings.json"
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
$j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.RoundRecord=$true; $j.ParallelCount=3; $j.AskSaveLocation=$false; $j.SkipExisting=$true; $j.AutoStartOnDrop=$false; $j.RecordGameAudio=$false
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$all = @(); foreach($r in ($done + $todo)){ $all += ('"'+$r.FullName+'"') }
$pr=Start-Process -FilePath "$dir\RepRecorder.exe" -ArgumentList $all -PassThru
Start-Sleep -Seconds 14
$pr.Refresh(); $h=$pr.MainWindowHandle
Say ([CK10]::SelectRows($h, 3, 2))     # 模拟"选中队列下面 2 个"
Start-Sleep -Milliseconds 600
Say ("当前蓝色选中：" + [CK10]::SelCount($h))
Say ("点「开始录制」= " + [CK10]::Click($h,'开始录制'))
Start-Sleep -Seconds 6
Say "---- 日志（关键几行）----"
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '加入队列|轮录开始|没有待录|待录 ' | ForEach-Object { Say ("  " + $_.Line) }
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
[System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
Say "（已停掉程序，设置已还原）"