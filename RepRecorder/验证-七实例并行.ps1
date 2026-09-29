# 验证「同时录制数量 = 7」这条分支：>6 时的无声策略、4480×480 画布、7 格是否各绑到不同窗口
$root  = 'D:\【完整包】非想天则整合包2026-06-15'
$dir   = "$root\RepRecorder"
$log   = "$dir\RepRecorder.log"
$app   = "$dir\RepRecorder.exe"
$cfg   = "$dir\RepRecorder.settings.json"
$outlog= "$dir\验证-七实例并行.log"
$replay= "$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ffprobe='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffprobe.exe'
$ffmpeg ='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }
function Find-Rep($leaf){ $f=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter $leaf -ErrorAction SilentlyContinue | Select-Object -First 1; if($f){return $f.FullName}; return $null }
$names = @(
 '26-09-02_13-47-16_Unstrung Doll (爱丽丝)_阿虚reality(咲夜)_(0︰0_无).rep',
 '26-08-22_15-09-38_Tsuki(咲夜)_xingya(红美铃)_(1︰0_无).rep',
 '26-08-26_00-05-54_meowko(衣玖)_dandelion(红美铃)_(1︰0_无).rep',
 '26-08-11_18-09-25_xingya(红美铃)_网卡怪物 (LV3)(魔理沙)_(0︰1_无).rep',
 '26-08-07_12-16-51_xingya(红美铃)_wifi 2到3(魔理沙)_(0︰0_无).rep',
 '26-08-29_12-53-13_白城失足女(蕾咪)_易☆ちゅん(蕾咪)_(0︰0_无).rep',
 '26-08-21_13-32-08_暮光仓鼠(红美铃)_神AI(铃仙)_(0︰0_无).rep'
)
$reps=@(); foreach($n in $names){ $p=Find-Rep $n; if($p){ $reps += $p } else { Say "✗ 缺 rep: $n" } }
if($reps.Count -lt 7){ Say "只找到 $($reps.Count) 个，中止"; exit 1 }

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RC7 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
}
"@
function Kids($h){ $script:ks=New-Object System.Collections.ArrayList
  $cb=[RC7+EP]{ param($ch,$l) [void]$script:ks.Add([pscustomobject]@{H=$ch;C=[RC7]::Cls($ch);T=[RC7]::Txt($ch)}); return $true }
  [void][RC7]::EnumChildWindows($h,$cb,[IntPtr]::Zero); return $script:ks }
function Click-Btn($h,$like){ foreach($k in (Kids $h)){ if($k.C -like '*BUTTON*' -and $k.T -like $like){ Say ("  点按钮「{0}」" -f $k.T); [void][RC7]::SendMessageW($k.H,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero); Start-Sleep -Seconds 2; return $true } }; Say "  ✗ 找不到「$like」"; return $false }
function Wait-Marker($pattern,$timeoutSec,$skip){ $sw=[Diagnostics.Stopwatch]::StartNew()
  while($sw.Elapsed.TotalSeconds -lt $timeoutSec){ Start-Sleep -Seconds 3
    if(Test-Path -LiteralPath $log){ $all=@(Get-Content -LiteralPath $log -Encoding UTF8)
      if($all.Count -gt $skip){ $tail=@($all[$skip..($all.Count-1)]); if(@($tail | Select-String -Pattern $pattern).Count -gt 0){ return ,$tail } } } }
  return ,@() }
function Audio-Max($f){ $vd=(& $ffmpeg -hide_banner -i $f -af volumedetect -f null NUL 2>&1) -join "`n"; if($vd -match 'max_volume:\s*([-\d.]+)'){ return $Matches[1] }; return '?' }

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== 7 实例并行验证（$(Get-Date -Format 'HH:mm:ss')）====="
$tot = (New-Object Microsoft.VisualBasic.Devices.ComputerInfo).TotalPhysicalMemory 2>$null
Add-Type -AssemblyName Microsoft.VisualBasic
Say ("本机内存 {0:N0} MB；7 个实例预计占用约 3.8 GB" -f ((New-Object Microsoft.VisualBasic.Devices.ComputerInfo).TotalPhysicalMemory/1MB))
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.SkipExisting=$false; $j.ParallelCount=7; $j.RecordGameAudio=$true; $j.AutoPrepareObs=$true
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
foreach($r in $reps){ $o=[System.IO.Path]::ChangeExtension($r,'.mp4'); if(Test-Path -LiteralPath $o){ Remove-Item -LiteralPath $o -Force } }
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue

$argList = ($reps | ForEach-Object { '"' + $_ + '"' }) -join ' '
$pr = Start-Process -FilePath $app -ArgumentList $argList -PassThru
Start-Sleep -Seconds 12
$pr.Refresh(); $h=$pr.MainWindowHandle
if($h -eq [IntPtr]::Zero){ Say "✗ 主窗口没出现"; exit 1 }
Say ("  并行按钮 = 「{0}」" -f [RC7]::Txt((Kids $h | Where-Object { $_.T -like '并行录制*' } | Select-Object -First 1).H))
$skip = @(Get-Content -LiteralPath $log -Encoding UTF8).Count
Click-Btn $h "并行录制×7" | Out-Null
$sw=[Diagnostics.Stopwatch]::StartNew()
$peak=0
while($sw.Elapsed.TotalSeconds -lt 900){
  $th=@(Get-Process th123 -ErrorAction SilentlyContinue)
  if($th.Count -gt 0){ $w=($th|Measure-Object WorkingSet64 -Sum).Sum; if($w -gt $peak){ $peak=$w } }
  if(Test-Path -LiteralPath $log){
    $all=@(Get-Content -LiteralPath $log -Encoding UTF8)
    if($all.Count -gt $skip){
      $tail=@($all[$skip..($all.Count-1)])
      if(@($tail | Select-String -Pattern '==== 并行录制结束 ====|并行录制出错').Count -gt 0){ break }
    }
  }
  Start-Sleep -Seconds 5
}
$all=@(Get-Content -LiteralPath $log -Encoding UTF8); $tail=@($all[$skip..($all.Count-1)])
Say ("用时 {0:N0} 秒；7 个实例内存峰值 {1:N0} MB" -f $sw.Elapsed.TotalSeconds, ($peak/1MB))
Say ""
Say "---- 关键日志 ----"
$tail | Select-String '⚠|第 . 格绑定窗口串|重新绑定完成|每格画面|同时录|切分完成|切分失败|Canvas|画布|无声|游戏声音进录像' | ForEach-Object { Say ("  " + $_.Line) }
Say ""
Say "---- 7 个产物 ----"
$mk=0; $sk=0
foreach($r in $reps){
  $o=[System.IO.Path]::ChangeExtension($r,'.mp4')
  if(Test-Path -LiteralPath $o){
    $d=(& $ffprobe -v error -show_entries format=duration -of csv=p=0 $o 2>&1) -join ''
    $wh=(& $ffprobe -v error -select_streams v -show_entries stream=width,height -of csv=p=0 $o 2>&1) -join ''
    $mx=Audio-Max $o
    Say ("  {0,-18} {1,7:N1} MB  {2,6}s  {3}  max_volume={4} dB" -f (Split-Path $o -Leaf).Substring(0,10), ((Get-Item -LiteralPath $o).Length/1MB), ([math]::Round([double]$d,1)), $wh, $mx)
    if($mx -eq '-91.0'){ $sk++ } else { $mk++ }
  } else { Say ("  ✗ 没有产物：" + (Split-Path $o -Leaf).Substring(0,16)) }
}
Say ("  有声 {0} 个 / 无声 {1} 个（>6 格时设计上应为全部无声）" -f $mk, $sk)
$j2 = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j2.AskSaveLocation=$true; $j2.SkipExisting=$true; $j2.ParallelCount=3; $j2.RecordGameAudio=$true
$j2 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Say "===== 结束（$(Get-Date -Format 'HH:mm:ss')）====="
