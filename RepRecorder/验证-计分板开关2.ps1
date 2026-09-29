# 用真实录制路径验证计分板开关：录两次（关→开），检查 ModLoaderSettings.json 是否跟着变
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$app="$dir\RepRecorder.exe"; $cfg="$dir\RepRecorder.settings.json"; $log="$dir\RepRecorder.log"
$mls="$root\【完整包】非想天则整合包2026-06-15\th123\ModLoaderSettings.json"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$rep="$replay\【联机对战Rep】\[Aquila(3) ]\26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep"
function Say($m){ Write-Host $m }
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RB13 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static bool ClickBtn(IntPtr root, string like){
    IntPtr hit=IntPtr.Zero;
    EnumChildWindows(root, delegate(IntPtr c, IntPtr l){
      if(Cls(c).IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase)>=0){
        string t=Txt(c);
        if(!string.IsNullOrEmpty(t) && t.Contains(like)){ hit=c; return false; }
      }
      return true;
    }, IntPtr.Zero);
    if(hit==IntPtr.Zero) return false;
    SendMessageW(hit,0x00F5,IntPtr.Zero,IntPtr.Zero);
    return true;
  }
}
"@
function Flag(){
  $t=Get-Content -LiteralPath $mls -Raw -Encoding UTF8
  if($t -match '"modules/ScoreTracker/ScoreTracker\.dll":\{"enabled":(\w+)'){ return $Matches[1] }
  return '?'
}
function WaitQ($sec){ $sw=[Diagnostics.Stopwatch]::StartNew()
  while($sw.Elapsed.TotalSeconds -lt $sec){ Start-Sleep -Seconds 3
    if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } } }
  return $sw.Elapsed.TotalSeconds }
function Run-Once($show, $tag){
  $j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
  $j.AskSaveLocation=$false; $j.SkipExisting=$false; $j.MuteGameForMyEars=$false; $j.ParallelCount=1
  $j | Add-Member -NotePropertyName ShowScoreboard -NotePropertyValue $show -Force
  $j | Add-Member -NotePropertyName SettingsVersion -NotePropertyValue 2 -Force
  $j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
  $o=[System.IO.Path]::ChangeExtension($rep,'.mp4'); if(Test-Path -LiteralPath $o){ Remove-Item -LiteralPath $o -Force }
  Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
  $pr=Start-Process -FilePath $app -ArgumentList ('"'+$rep+'"') -PassThru
  Start-Sleep -Seconds 11
  $pr.Refresh()
  Say ("  [$tag] 点开始录制 = " + [RB13]::ClickBtn($pr.MainWindowHandle,'开始录制'))
  $t=WaitQ 300
  Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep 2
  Say ("  [$tag] 录完，用时 {0:N0} 秒；ModLoaderSettings 里 enabled={1}（期望 {2}）" -f $t, (Flag), $show.ToString().ToLower())
  Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '计分板|ScoreTracker' | ForEach-Object { Say ("      日志| " + $_.Line) }
}
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
$mlsBak=(Get-Content -LiteralPath $mls -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 3
Say "起手 enabled=$(Flag)"
Run-Once $false '关计分板'
Run-Once $true  '开计分板'
[System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText($mls,$mlsBak,(New-Object System.Text.UTF8Encoding($false)))
Say "已还原（enabled=$(Flag)）"