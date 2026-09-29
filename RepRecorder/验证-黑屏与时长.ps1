# 验证：开头黑屏、结尾黑屏、以及队列「用时」列
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"; $app="$dir\RepRecorder.exe"; $cfg="$dir\RepRecorder.settings.json"
$outlog="$dir\验证-黑屏与时长.log"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
$ffprobe='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffprobe.exe'
function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }
function Find-Rep($leaf){ $f=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter $leaf -ErrorAction SilentlyContinue | Select-Object -First 1; if($f){return $f.FullName}; return $null }
$short = Find-Rep '26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep'
$long  = Find-Rep '26-09-17_18-11-44_Ciel(红美铃)_诺诺真好玩喵(天子)_(2︰0_胜).rep'

Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @"
using System; using System.Text; using System.Runtime.InteropServices; using System.Drawing;
public class RB7 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static string Grab(IntPtr h, string path, int cropW, int cropH){
    RECT r; if(!GetWindowRect(h, out r)) return "GetWindowRect 失败";
    int w=r.R-r.L, hh=r.B-r.T;
    if(w<=0||hh<=0) return "窗口尺寸异常";
    using(Bitmap bmp=new Bitmap(w,hh))
    using(Graphics g=Graphics.FromImage(bmp)){
      IntPtr hdc=g.GetHdc();
      bool ok=PrintWindow(h,hdc,2);
      g.ReleaseHdc(hdc);
      int cw=Math.Min(cropW,w), ch=Math.Min(cropH,hh);
      using(Bitmap c=bmp.Clone(new Rectangle(0,0,cw,ch), bmp.PixelFormat)) { c.Save(path); }
      return ok? "OK" : "PrintWindow 返回 false（图可能不全）";
    }
  }
}
"@
function Click-Btn($h,$like){ $script:hit=[IntPtr]::Zero
  [void][RB7]::EnumChildWindows($h,[RB7+EP]{ param($c,$l) if([RB7]::Cls($c) -like '*BUTTON*'){ $t=[RB7]::Txt($c); if($t -and $t -like $like){ $script:hit=$c; return $false } }; return $true },[IntPtr]::Zero)
  if($script:hit -eq [IntPtr]::Zero){ Say "  ✗ 找不到「$like」"; return $false }
  Say ("  点按钮「{0}」" -f [RB7]::Txt($script:hit))
  [void][RB7]::SendMessageW($script:hit,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero); Start-Sleep -Seconds 2; return $true }
function Blacks($f,$mode){
  $out = if($mode -eq 'head'){ & $ffmpeg -hide_banner -nostats -t 25 -i $f -an -vf blackdetect=d=0.15:pix_th=0.10 -f null NUL 2>&1 }
         else { & $ffmpeg -hide_banner -nostats -sseof -15 -i $f -an -vf blackdetect=d=0.15:pix_th=0.10 -f null NUL 2>&1 }
  $lines = ($out | Select-String 'black_start').Line
  if(-not $lines){ return "（无 ≥0.15 秒的黑段）" }
  return (($lines | ForEach-Object { ($_ -replace '.*blackdetect[^]]*\]\s*','') }) -join ' ; ')
}
function Info($f){
  $d=(& $ffprobe -v error -show_entries format=duration -of csv=p=0 -i $f 2>&1) -join ''
  return @{ dur=[double]$d; size=((Get-Item -LiteralPath $f).Length/1MB) }
}

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== 验证：黑屏 & 用时（$(Get-Date -Format 'HH:mm:ss')）====="
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 3
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.SkipExisting=$false; $j.MuteGameForMyEars=$false; $j.ParallelCount=2
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8

try{
  Say ""
  Say "---------- A. 单录 ----------"
  $o1=[System.IO.Path]::ChangeExtension($short,'.mp4')
  if(Test-Path -LiteralPath $o1){ Remove-Item -LiteralPath $o1 -Force }
  Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
  $pr = Start-Process -FilePath $app -ArgumentList ('"'+$short+'"') -PassThru
  Start-Sleep -Seconds 10
  $pr.Refresh(); $h=$pr.MainWindowHandle
  Click-Btn $h "开始录制" | Out-Null
  $sw=[Diagnostics.Stopwatch]::StartNew()
  while($sw.Elapsed.TotalSeconds -lt 320){
    Start-Sleep -Seconds 3
    if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } }
  }
  Say ("  单录用时 {0:N0} 秒" -f $sw.Elapsed.TotalSeconds)
  Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '开录前等了|画面已经黑了|已剪掉结尾|完成：|等待画面' | ForEach-Object { Say ("  日志| " + $_.Line) }
  if(Test-Path -LiteralPath $o1){
    $i=Info $o1
    Say ("  成品：{0:N1} MB  {1:N1}s = {2}分{3:00}秒" -f $i.size, $i.dur, [int]($i.dur/60), [int]($i.dur%60))
    Say ("  开头 25 秒黑段：{0}" -f (Blacks $o1 'head'))
    Say ("  结尾 15 秒黑段：{0}" -f (Blacks $o1 'tail'))
  } else { Say "  ✗ 单录没出成品" }
  $shot="$dir\_看片\单录-队列.png"
  Say ("  截图：{0} → {1}" -f [RB7]::Grab($h,$shot,760,300), $shot)
  Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep 2

  Say ""
  Say "---------- B. 并行 2 格 ----------"
  $outs=@()
  foreach($r in @($short,$long)){ $o=[System.IO.Path]::ChangeExtension($r,'.mp4'); if(Test-Path -LiteralPath $o){ Remove-Item -LiteralPath $o -Force }; $outs += $o }
  Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
  $pr2 = Start-Process -FilePath $app -ArgumentList ('"'+$short+'"','"'+$long+'"') -PassThru
  Start-Sleep -Seconds 10
  $pr2.Refresh(); $h2=$pr2.MainWindowHandle
  Click-Btn $h2 "并行录制*" | Out-Null
  $sw2=[Diagnostics.Stopwatch]::StartNew()
  while($sw2.Elapsed.TotalSeconds -lt 420){
    Start-Sleep -Seconds 3
    if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } }
  }
  Say ("  并行用时 {0:N0} 秒" -f $sw2.Elapsed.TotalSeconds)
  Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '并行切分（第|并行切分完成|开始录像|用时' | ForEach-Object { Say ("  日志| " + $_.Line) }
  $k=1
  foreach($o in $outs){
    if(Test-Path -LiteralPath $o){
      $i=Info $o
      Say ("  第 {0} 格：{1:N1} MB  {2:N1}s = {3}分{4:00}秒" -f $k,$i.size,$i.dur,[int]($i.dur/60),[int]($i.dur%60))
      Say ("     开头 25 秒黑段：{0}" -f (Blacks $o 'head'))
      Say ("     结尾 15 秒黑段：{0}" -f (Blacks $o 'tail'))
    } else { Say ("  第 {0} 格没有成品 ✗" -f $k) }
    $k++
  }
  $shot2="$dir\_看片\并行-队列.png"
  Say ("  截图：{0} → {1}" -f [RB7]::Grab($h2,$shot2,760,300), $shot2)
} finally {
  Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  [System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
  Say "  设置已还原"
}
Say "===== 结束（$(Get-Date -Format 'HH:mm:ss')）====="
