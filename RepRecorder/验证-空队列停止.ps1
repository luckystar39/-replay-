# 验证：空队列（没在录制）时点「停止」应该什么都不做，状态保持「就绪」
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"
function Say($m){ Write-Host $m }
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class ST {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static IntPtr FindBtn(IntPtr root, string like){
    IntPtr hit=IntPtr.Zero;
    EnumChildWindows(root, delegate(IntPtr c, IntPtr l){
      if(Cls(c).IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase)>=0){ string t=Txt(c);
        if(!string.IsNullOrEmpty(t) && t.Contains(like)){ hit=c; return false; } }
      return true; }, IntPtr.Zero);
    return hit;
  }
  public static string State(IntPtr root){
    IntPtr st=FindBtn(root,"停止"); IntPtr go=FindBtn(root,"开始录制");
    return "停止按钮=" + (st==IntPtr.Zero?"找不到":(IsWindowEnabled(st)?"可点":"灰的")) +
           " / 开始录制=" + (go==IntPtr.Zero?"找不到":(IsWindowEnabled(go)?"可点":"灰的"));
  }
  public static string Click(IntPtr root, string like){
    IntPtr h=FindBtn(root,like); if(h==IntPtr.Zero) return "找不到[" + like + "]";
    SendMessageW(h,0x00F5,IntPtr.Zero,IntPtr.Zero); return "已点[" + like + "]";
  }
  public static string StatusText(IntPtr root){
    string res="";
    EnumChildWindows(root, delegate(IntPtr c, IntPtr l){
      if(Cls(c).IndexOf("Static", StringComparison.OrdinalIgnoreCase)>=0){ }
      return true; }, IntPtr.Zero);
    return res;
  }
}
"@
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$pr=Start-Process -FilePath "$dir\RepRecorder.exe" -PassThru
Start-Sleep -Seconds 13
$pr.Refresh()
Say ("启动后：" + [ST]::State($pr.MainWindowHandle) + "   （停止应该是灰的）")
Say "点「停止」（空队列）：" + [ST]::Click($pr.MainWindowHandle, '停止')
Start-Sleep -Seconds 2
Say ("点完之后：" + [ST]::State($pr.MainWindowHandle) + "   （还是灰的、开始录制仍可点 = 正确）")
Say "再顺手点「开始录制」（空队列，应拒绝）：" + [ST]::Click($pr.MainWindowHandle, '开始录制')
Start-Sleep -Seconds 2
Say ("之后：" + [ST]::State($pr.MainWindowHandle))
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Say "---- 日志 ----"
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '不需要停止|队列是空的|已请求停止|正在停止' | ForEach-Object { Say ("  " + $_.Line) }