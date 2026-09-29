# 验证：界面改动（1/3/4/6 的界面部分）+ 需求5 的"已经录过了要重录吗"询问
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
function Say($m){ Write-Host $m }
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @"
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public class UI5 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static IntPtr FindWindow(string titleLike){
    IntPtr found=IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l){
      if(Txt(h).Contains(titleLike)){ found=h; return false; }
      return true; }, IntPtr.Zero);
    return found;
  }
  public static List<string> ChildTexts(IntPtr root){
    List<string> res=new List<string>();
    EnumChildWindows(root, delegate(IntPtr c, IntPtr l){
      string cl=Cls(c); if(cl=="Button"){ string t=Txt(c); if(t.Length>0) res.Add(t); }
      return true; }, IntPtr.Zero);
    return res;
  }
  public static string ClickChild(IntPtr root, string like){
    IntPtr hit=IntPtr.Zero;
    EnumChildWindows(root, delegate(IntPtr c, IntPtr l){
      if(Cls(c)=="Button"){ string t=Txt(c); if(t.Contains(like)){ hit=c; return false; } }
      return true; }, IntPtr.Zero);
    if(hit==IntPtr.Zero) return "没找到按钮 " + like;
    SendMessageW(hit,0x00F5,IntPtr.Zero,IntPtr.Zero);
    return "已点 " + like;
  }
  public static string Grab(IntPtr h, string path, int cw, int ch){
    RECT r; if(!GetWindowRect(h, out r)) return "fail";
    int w=r.R-r.L, hh=r.B-r.T; if(w<=0||hh<=0) return "bad";
    using(System.Drawing.Bitmap b=new System.Drawing.Bitmap(w,hh))
    using(System.Drawing.Graphics g=System.Drawing.Graphics.FromImage(b)){
      IntPtr hdc=g.GetHdc(); PrintWindow(h,hdc,2); g.ReleaseHdc(hdc);
      using(System.Drawing.Bitmap c=b.Clone(new System.Drawing.Rectangle(0,0,Math.Min(cw,w),Math.Min(ch,hh)), b.PixelFormat)) c.Save(path);
      return "OK";
    }
  }
}
"@
# 用【已经录制过】的 rep（旁边有 mp4）→ 应该弹"要重录吗"
$rec=Get-ChildItem -LiteralPath "$replay\【联机对战Rep】\[承诺 3]" -File -Filter '*.rep' |
     Where-Object { Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($_.FullName,'.mp4')) } | Sort-Object Name | Select-Object -First 3
Say ("用已录制过的： " + (($rec | ForEach-Object { $_.Name }) -join ' / '))
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Remove-Item -LiteralPath "$dir\RepRecorder.log" -Force -ErrorAction SilentlyContinue
$a=@(); foreach($r in $rec){ $a += ('"'+$r.FullName+'"') }
$pr=Start-Process -FilePath "$dir\RepRecorder.exe" -ArgumentList $a -PassThru
Start-Sleep -Seconds 13
$pr.Refresh()
# 1) 主界面截图
Say ("主界面截图 " + [UI5]::Grab($pr.MainWindowHandle, "$dir\_看片\新界面-七项改动.png", 1700, 240))
# 2) 找"已录制过"的询问弹窗
$dlg=[IntPtr]::Zero
for($i=0;$i -lt 12 -and $dlg -eq [IntPtr]::Zero;$i++){ $dlg=[UI5]::FindWindow("已经录制过了"); Start-Sleep -Milliseconds 500 }
if($dlg -ne [IntPtr]::Zero){
  Say "找到询问弹窗：" + [UI5]::Txt($dlg)
  Say ("  弹窗截图 " + [UI5]::Grab($dlg, "$dir\_看片\重录询问.png", 780, 320))
  Say "  弹窗里的按钮：" + (([UI5]::ChildTexts($dlg)) -join ' | ')
  Say "  " + [UI5]::ClickChild($dlg, '否')
  Start-Sleep -Seconds 2
} else { Say "❌ 没找到询问弹窗" }
Get-Content -LiteralPath "$dir\RepRecorder.log" -Encoding UTF8 | Select-String '检测到|你选择|加入队列|放进来' | ForEach-Object { Say ("  日志| " + $_.Line) }
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
Say "（已关闭程序）"