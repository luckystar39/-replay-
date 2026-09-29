# 验证：需求0（提前询问保存位置）+ 需求6（轮录分批：2+1，末批降级单录）+ 需求2（并行最小化自动还原）
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$cfg="$dir\RepRecorder.settings.json"
$log="$dir\RepRecorder.log"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
function Say($m){ Write-Host $m }
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @"
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public class UI6 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static List<IntPtr> Dialogs(){
    List<IntPtr> res=new List<IntPtr>();
    EnumWindows(delegate(IntPtr h, IntPtr l){ if(Cls(h)=="#32770") res.Add(h); return true; }, IntPtr.Zero);
    return res;
  }
  public static List<string> Titles(){
    List<string> res=new List<string>();
    EnumWindows(delegate(IntPtr h, IntPtr l){ string t=Txt(h); if(t.Length>0) res.Add(Cls(h)+" :: "+t); return true; }, IntPtr.Zero);
    return res;
  }
  public static string ClickChild(IntPtr root, string like){
    IntPtr hit=IntPtr.Zero;
    EnumChildWindows(root, delegate(IntPtr c, IntPtr l){
      if(Cls(c)=="Button"){ string t=Txt(c); if(t.Contains(like)){ hit=c; return false; } }
      return true; }, IntPtr.Zero);
    if(hit==IntPtr.Zero) return "没找到按钮[" + like + "]";
    SendMessageW(hit,0x00F5,IntPtr.Zero,IntPtr.Zero);
    return "已点[" + like + "]";
  }
  public static bool ClickMain(IntPtr root, string like){
    IntPtr hit=IntPtr.Zero;
    EnumChildWindows(root, delegate(IntPtr c, IntPtr l){
      if(Cls(c)=="Button"){ string t=Txt(c); if(t.Contains(like)){ hit=c; return false; } }
      return true; }, IntPtr.Zero);
    if(hit==IntPtr.Zero) return false;
    SendMessageW(hit,0x00F5,IntPtr.Zero,IntPtr.Zero); return true;
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
# 挑 3 个最小的 rep（并删掉同名成品，保证是"待录"）
$small=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter '*.rep' |
       Sort-Object Length | Select-Object -First 3
foreach($r in $small){ $o=[System.IO.Path]::ChangeExtension($r.FullName,'.mp4'); if(Test-Path -LiteralPath $o){ Remove-Item -LiteralPath $o -Force } }
Say ("用 3 个最小 rep：" + (($small | ForEach-Object { "$($_.Name)($([math]::Round($_.Length/1MB,2))MB)" }) -join ' / '))
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.ParallelCount=2; $j.AskSaveLocation=$true; $j.SkipExisting=$true
$j | Add-Member -NotePropertyName RoundRecord -NotePropertyValue $true -Force
$j | Add-Member -NotePropertyName SettingsVersion -NotePropertyValue 3 -Force
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$a=@(); foreach($r in $small){ $a += ('"'+$r.FullName+'"') }
$pr=Start-Process -FilePath "$dir\RepRecorder.exe" -ArgumentList $a -PassThru
Start-Sleep -Seconds 13
$pr.Refresh()
Say ("点「开始录制」= " + [UI6]::ClickMain($pr.MainWindowHandle,'开始录制'))
# 等提前询问的弹窗
$dlg=[IntPtr]::Zero
for($i=0;$i -lt 20 -and $dlg -eq [IntPtr]::Zero;$i++){
  Start-Sleep -Milliseconds 500
  foreach($d in [UI6]::Dialogs()){ if([UI6]::Txt($d).Contains('视频要放在哪里')){ $dlg=$d; break } }
}
if($dlg -ne [IntPtr]::Zero){
  Say "✓ 出现了提前询问弹窗：" + [UI6]::Txt($dlg)
  Say ("  截图 " + [UI6]::Grab($dlg, "$dir\_看片\提前询问保存位置.png", 620, 300))
  Say "  按钮：" + (([UI6]::Dialogs() | ForEach-Object { $t=$_; ([UI6]::Txt($t)) }) -join ' | ')
  Say "  " + [UI6]::ClickChild($dlg, '和 rep 放在一起')
} else {
  Say "❌ 没等到提前询问弹窗"
  Say ("  当前顶层窗口：" + (([UI6]::Titles()) -join ' ; '))
}
# 并行批次开始后，把其中一个游戏窗口最小化 → 看是否自动还原
Start-Sleep -Seconds 25
$mins=0
foreach($p in (Get-Process th123 -ErrorAction SilentlyContinue)){
  try{ if($p.MainWindowHandle -ne [IntPtr]::Zero -and -not [UI6]::IsIconic($p.MainWindowHandle)){ [void][UI6]::ShowWindow($p.MainWindowHandle, 6); $mins++; if($mins -ge 1){ break } } }catch{}
}
Say "已最小化 $mins 个游戏窗口（测试需求2）"
# 等整轮结束
$sw=[Diagnostics.Stopwatch]::StartNew()
while($sw.Elapsed.TotalSeconds -lt 900){ Start-Sleep -Seconds 5
  if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } } }
Say ("整轮用时 {0:N0} 秒" -f $sw.Elapsed.TotalSeconds)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Say "---- 日志（关键行）----"
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '轮录开始|第 \d 批|只有 1 个|本次录好的视频|检测到你|并行：第 \d+ 格游戏窗口|完成：|队列结束' | ForEach-Object { Say ("  " + $_.Line) }
Say "---- 有没有再弹一次"录完了放哪里"（不该有）----"
$post = (Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '视频位置：'
Say ("  录完之后是否又问过位置：" + $(if($post){'❌ 又问了'}else{'✅ 没再问'}))
[System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
Say "设置已还原"