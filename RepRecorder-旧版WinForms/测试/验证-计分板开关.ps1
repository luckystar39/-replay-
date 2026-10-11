# 验证「显示计分板（名字+比分）」勾选框：点一下要能真的改到 ModLoaderSettings.json
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$app="$dir\RepRecorder.exe"; $cfg="$dir\RepRecorder.settings.json"; $log="$dir\RepRecorder.log"
$mls="$root\【完整包】非想天则整合包2026-06-15\th123\ModLoaderSettings.json"
function Say($m){ Write-Host $m }
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RB12 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static bool ClickText(IntPtr root, string like){
    IntPtr hit=IntPtr.Zero;
    EnumChildWindows(root, delegate(IntPtr c, IntPtr l){
      string cls=Cls(c);
      if(cls=="Button"){ string t=Txt(c); if(!string.IsNullOrEmpty(t) && t.Contains(like)){ hit=c; return false; } }
      return true;
    }, IntPtr.Zero);
    if(hit==IntPtr.Zero) return false;
    SendMessageW(hit, 0x00F5, IntPtr.Zero, IntPtr.Zero);
    return true;
  }
  public static string Grab(IntPtr h, string path, int cw, int ch){
    RECT r; if(!GetWindowRect(h, out r)) return "fail";
    int w=r.R-r.L, hh=r.B-r.T; if(w<=0||hh<=0) return "bad";
    using(System.Drawing.Bitmap b=new System.Drawing.Bitmap(w,hh))
    using(System.Drawing.Graphics g=System.Drawing.Graphics.FromImage(b)){
      IntPtr hdc=g.GetHdc(); PrintWindow(h,hdc,2); g.ReleaseHdc(hdc);
      using(System.Drawing.Bitmap c=b.Clone(new System.Drawing.Rectangle(0,0,Math.Min(cw,w),Math.Min(hh,hh)), b.PixelFormat)) c.Save(path);
      return "OK";
    }
  }
}
"@
function ScoreFlag(){
  $t=Get-Content -LiteralPath $mls -Raw -Encoding UTF8
  if($t -match '"modules/ScoreTracker/ScoreTracker\.dll":\{"enabled":(\w+)'){ return $Matches[1] }
  return '?'
}
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
$mlsBak=(Get-Content -LiteralPath $mls -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
# 让设置里 ShowScoreboard=true 起手
$j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j | Add-Member -NotePropertyName ShowScoreboard -NotePropertyValue $true -Force
$j | Add-Member -NotePropertyName SettingsVersion -NotePropertyValue 2 -Force
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Say "起手：设置里 ShowScoreboard=true，ModLoaderSettings 里 enabled=$(ScoreFlag)"
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$pr=Start-Process -FilePath $app -PassThru
Start-Sleep -Seconds 12
$pr.Refresh(); $h=$pr.MainWindowHandle
Say "  点一下「显示计分板（名字+比分）」→ $([RB12]::ClickText($h,'显示计分板'))"
Start-Sleep -Seconds 3
Say "  现在 ModLoaderSettings 里 enabled=$(ScoreFlag)  （应为 false）"
Say "  再点回来 → $([RB12]::ClickText($h,'显示计分板'))"
Start-Sleep -Seconds 3
Say "  现在 ModLoaderSettings 里 enabled=$(ScoreFlag)  （应回到 true）"
Say "  界面截图：$([RB12]::Grab($h, "$dir\_看片\计分板开关.png", 900, 200))"
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '计分板' | ForEach-Object { Say ("  日志| " + $_.Line) }
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
[System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText($mls,$mlsBak,(New-Object System.Text.UTF8Encoding($false)))
Say "已还原设置（ModLoaderSettings 和 settings.json 都还原了）"
