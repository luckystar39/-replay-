# 观察 ScoreTracker：开场/回合间/结束都截图，并按 F3 试着打开它的菜单（看有哪些可调项）
$root='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123'
$rep='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123\replay\【联机对战Rep】\[承诺 3]\26-08-26_23-22-10_承诺 3(魔理沙)_Ciel(红美铃)_(2︰1_负).rep'
$dst='D:\【完整包】非想天则整合包2026-06-15\RepRecorder\_看片'
"当前 ModLoaderSettings 里 ScoreTracker 是否启用："
(Get-Content -LiteralPath "$root\ModLoaderSettings.json" -Raw -Encoding UTF8) -match '"modules/ScoreTracker/ScoreTracker.dll":\{"enabled":(\w+)' | Out-Null
"  enabled = $($Matches[1])"
"当前 ScoreTracker.ini 的显示时机："
Get-Content -LiteralPath "$root\modules\ScoreTracker\ScoreTracker.ini" -Encoding UTF8 | Select-String 'Show on' | ForEach-Object { "  " + $_.Line.Trim() }

Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @"
using System; using System.Runtime.InteropServices; using System.Drawing;
public class Cap2 {
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
  public static string Shot(IntPtr h, string path){
    RECT cr; if(!GetClientRect(h, out cr)) return "fail";
    POINT p=new POINT(); p.X=0; p.Y=0; if(!ClientToScreen(h, ref p)) return "fail";
    int w=cr.R, hh=cr.B; if(w<=0||hh<=0) return "bad";
    using(Bitmap b=new Bitmap(w,hh)) using(Graphics g=Graphics.FromImage(b)){
      g.CopyFromScreen(p.X,p.Y,0,0,new System.Drawing.Size(w,hh)); b.Save(path); }
    return "OK";
  }
  public static void PressF3(){ keybd_event(0x72,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(60); keybd_event(0x72,0,2,UIntPtr.Zero); }
}
"@
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$p=Start-Process -FilePath (Join-Path $root 'th123.exe') -ArgumentList ('"'+$rep+'"') -WorkingDirectory $root -PassThru
Start-Sleep -Seconds 3
$h=$p.MainWindowHandle
for($i=0;$i -lt 20 -and $h -eq [IntPtr]::Zero;$i++){ Start-Sleep -Milliseconds 400; $p.Refresh(); $h=$p.MainWindowHandle }
[void][Cap2]::ShowWindow($h,9)
[void][Cap2]::SetWindowPos($h,[IntPtr]::new(-1),20,20,0,0,0x0001 -bor 0x0040)
[void][Cap2]::SetForegroundWindow($h)
foreach($t in @(4,9,14,40,70)){
  Start-Sleep -Seconds ($t - $(if($script:t){$script:t}else{0})); $script:t=$t
  "[Cap2]::Shot → " + [Cap2]::Shot($h, "$dst\ST-t$t.png")
}
# 快结束时按 F3（如果那时还在对局里，F3 是它的菜单；不行就说明只在非对局时开）
[Cap2]::PressF3(); Start-Sleep -Seconds 2
"[Cap2]::Shot(F3后) → " + [Cap2]::Shot($h, "$dst\ST-F3.png")
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
"（游戏已关闭）"