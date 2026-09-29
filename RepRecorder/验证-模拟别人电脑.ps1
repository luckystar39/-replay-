# 「假装是别人电脑」端到端测试：
#   D:\临时-别人电脑测试\RepRecorder\  ← 就是发给对方的那个包（没有 settings.json）
#   D:\临时-别人电脑测试\【完整包】…\  ← 用 junction 模拟"对方已有的整合包"
# 要验证：① 自动找到游戏目录 ② 自动找到 OBS ③ 自动启用/读到 WebSocket ④ 找到 ffmpeg ⑤ 能真的录出一条 rep
$sim='D:\临时-别人电脑测试'
$app="$sim\RepRecorder\RepRecorder.exe"
$log="$sim\RepRecorder\RepRecorder.log"
$ffmpeg="$sim\RepRecorder\ffmpeg.exe"
function Say($m){ Write-Host $m }
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class SIM {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
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
  public static IntPtr FindDlg(string titleLike){
    IntPtr found=IntPtr.Zero;
    EnumChildWindows(IntPtr.Zero, delegate(IntPtr c, IntPtr l){ return true; }, IntPtr.Zero);
    return found; }
}
"@
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$pr=Start-Process -FilePath $app -PassThru -WorkingDirectory "$sim\RepRecorder"
Start-Sleep -Seconds 14
$pr.Refresh()
Say ("[1] 点「检查环境」= " + [SIM]::Click($pr.MainWindowHandle,'检查环境'))
Start-Sleep -Seconds 9
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Say "---- 检查环境结果（对方第一次运行应该能看到）----"
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '环境检查|游戏目录|OBS |WebSocket|ffmpeg|ReplayDnD|连接：|BGM' | ForEach-Object { Say ("  " + $_.Line) }
Say "---- 自动生成的设置文件 ----"
if(Test-Path -LiteralPath "$sim\RepRecorder\RepRecorder.settings.json"){
  (Get-Content -LiteralPath "$sim\RepRecorder\RepRecorder.settings.json" -Raw -Encoding UTF8 | ConvertFrom-Json) |
    Select-Object GameDir,ObsExe,Port,CanvasWidth,CanvasHeight,ParallelCount,SingleEdgeInset,ParallelEdgeInset | Format-List
} else { Say "  ❌ 没生成设置文件" }