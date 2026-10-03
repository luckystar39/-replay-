# 看一眼：ScoreTracker 到底在画面上画了什么（名字？比分？），好确定两个开关该控制什么
$root='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123'
$rep='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123\replay\【联机对战Rep】\[承诺 3]\26-08-26_23-22-10_承诺 3(魔理沙)_Ciel(红美铃)_(2︰1_负).rep'
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @"
using System; using System.Text; using System.Runtime.InteropServices; using System.Drawing;
public class Cap {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
  public static string Shot(IntPtr h, string path){
    RECT cr; if(!GetClientRect(h, out cr)) return "GetClientRect 失败";
    POINT p = new POINT(); p.X = 0; p.Y = 0;
    if(!ClientToScreen(h, ref p)) return "ClientToScreen 失败";
    int w = cr.R, hh = cr.B;
    if(w <= 0 || hh <= 0) return "尺寸异常 " + w + "x" + hh;
    using(Bitmap b = new Bitmap(w, hh))
    using(Graphics g = Graphics.FromImage(b)){
      g.CopyFromScreen(p.X, p.Y, 0, 0, new System.Drawing.Size(w, hh));
      b.Save(path);
    }
    return "OK " + w + "x" + hh;
  }
}
"@
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$p=Start-Process -FilePath (Join-Path $root 'th123.exe') -ArgumentList ('"'+$rep+'"') -WorkingDirectory $root -PassThru
Start-Sleep -Seconds 8
# 把游戏窗口挪到左上角并置顶，方便全窗口截屏
$h=$p.MainWindowHandle
for($i=0;$i -lt 20 -and $h -eq [IntPtr]::Zero;$i++){ Start-Sleep -Milliseconds 500; $p.Refresh(); $h=$p.MainWindowHandle }
[void][Cap]::ShowWindow($h, 9)          # SW_RESTORE
[void][Cap]::SetWindowPos($h, [IntPtr]::new(-1), 20, 20, 0, 0, 0x0001 -bor 0x0040)   # HWND_TOPMOST + SHOWWINDOW
[void][Cap]::SetForegroundWindow($h)
Start-Sleep -Seconds 10
$dst='D:\【完整包】非想天则整合包2026-06-15\RepRecorder\_看片'
"t=18s 截图： " + [Cap]::Shot($h, "$dst\ScoreTracker-开场.png")
Start-Sleep -Seconds 25
"t=43s 截图： " + [Cap]::Shot($h, "$dst\ScoreTracker-对局中.png")
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
"（游戏已关闭）"