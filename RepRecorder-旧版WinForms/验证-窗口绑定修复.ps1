# 验证「窗口绑定格式」修复：
#   A) 单文件录制仍然正常（OBS 窗口串格式改成 标题:类名:exe 之后不能把主路径弄坏）
#   B) 并行 3 格必须是 3 场不同对局（用 PSNR 精确判定，以前第 2、3 格会撞成同一个窗口）
$root  = 'D:\【完整包】非想天则整合包2026-06-15'
$dir   = "$root\RepRecorder"
$log   = "$dir\RepRecorder.log"
$app   = "$dir\RepRecorder.exe"
$cfg   = "$dir\RepRecorder.settings.json"
$outlog= "$dir\验证-窗口绑定修复.log"
$replay= "$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
$ffprobe='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffprobe.exe'

$reps = @(
  "$replay\【联机对战Rep】\[Aquila(3) ]\26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep",
  "$replay\【观战Rep】\[Timepieceit LV2]_[menghenyinxin]\26-08-15_23-57-29_menghenyinxin(红美铃)_Timepieceit LV2(红美铃)_(1︰0_无).rep",
  "$replay\【观战Rep】\[Unstrung Doll ]_[阿虚reality]\26-09-02_13-47-16_Unstrung Doll (爱丽丝)_阿虚reality(咲夜)_(0︰0_无).rep"
)
function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RW {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
}
"@
function Find-Btn($h,$like){
  $script:hit=[IntPtr]::Zero
  $cb=[RW+EP]{ param($ch,$l)
    if([RW]::Cls($ch) -like '*BUTTON*'){ $t=[RW]::Txt($ch); if($t -and $t -like $like){ $script:hit=$ch; return $false } }
    return $true }
  [void][RW]::EnumChildWindows($h,$cb,[IntPtr]::Zero)
  return $script:hit
}
function Click-Btn($h,$like){
  $b = Find-Btn $h $like
  if($b -eq [IntPtr]::Zero){ Say "  ✗ 找不到「$like」"; return $false }
  Say ("  点按钮「{0}」" -f [RW]::Txt($b))
  [void][RW]::SendMessageW($b, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
  Start-Sleep -Seconds 2
  return $true
}
function Wait-Marker($pattern,$timeoutSec,$skip){
  $sw=[Diagnostics.Stopwatch]::StartNew()
  while($sw.Elapsed.TotalSeconds -lt $timeoutSec){
    Start-Sleep -Seconds 3
    if(Test-Path -LiteralPath $log){
      $all=@(Get-Content -LiteralPath $log -Encoding UTF8)
      if($all.Count -gt $skip){
        $tail=@($all[$skip..($all.Count-1)])
        if(@($tail | Select-String -Pattern $pattern).Count -gt 0){ return ,$tail }
      }
    }
  }
  return ,@()
}
function Clean-Out {
  foreach($r in $reps){ $o=[System.IO.Path]::ChangeExtension($r,'.mp4'); if(Test-Path -LiteralPath $o){ Remove-Item -LiteralPath $o -Force -ErrorAction SilentlyContinue } }
}

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== 窗口绑定格式修复验证（$(Get-Date -Format 'HH:mm:ss')）====="
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.SkipExisting=$false; $j.ParallelCount=3; $j.RecordGameAudio=$true; $j.AutoPrepareObs=$true
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Clean-Out
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue

$argList = ($reps | ForEach-Object { '"' + $_ + '"' }) -join ' '
$pr = Start-Process -FilePath $app -ArgumentList $argList -PassThru
Start-Sleep -Seconds 10
$pr.Refresh(); $h=$pr.MainWindowHandle
if($h -eq [IntPtr]::Zero){ Say "✗ 主窗口没出现"; exit 1 }

# ---------- A：单文件（只录队列第一个） ----------
Say ""
Say "########## A：单文件录制（验证格式改动没弄坏主路径）##########"
$skip = @(Get-Content -LiteralPath $log -Encoding UTF8).Count
Click-Btn $h "开始录制" | Out-Null
$t1 = Wait-Marker '队列结束' 420 $skip
$t1 | Select-String '采集画面正常|已把采集源绑定到窗口|完成|失败|音频自检' | ForEach-Object { Say ("  日志| " + $_.Line) }
$out1 = [System.IO.Path]::ChangeExtension($reps[0],'.mp4')
if(Test-Path -LiteralPath $out1){
  $mb = (Get-Item -LiteralPath $out1).Length/1MB
  $d  = (& $ffprobe -v error -show_entries format=duration -of csv=p=0 $out1 2>&1) -join ''
  Say ("  单文件产物：{0:N1} MB / {1}s" -f $mb, $d)
  if($mb -gt 40){ Say "  ✅ 单文件路径正常（画面有内容）" } else { Say "  ⚠ 单文件产物偏小，可能没抓到画面" }
} else { Say "  ✗ 单文件没有产物" }

# ---------- B：并行 3 格 ----------
Say ""
Say "########## B：并行 3 格（必须录到 3 场不同对局）##########"
Clean-Out
Start-Sleep -Seconds 2
Click-Btn $h "刷新" | Out-Null
Start-Sleep -Seconds 2
$skip2 = @(Get-Content -LiteralPath $log -Encoding UTF8).Count
Click-Btn $h "并行录制×3" | Out-Null
$t2 = Wait-Marker '==== 并行录制结束 ====' 900 $skip2
$t2 | Select-String '绑定窗口串|重启并重新绑定|⚠|切分完成|每格画面' | ForEach-Object { Say ("  日志| " + $_.Line) }

Say ""
Say "---- 产物与差异判定 ----"
$outs = @()
foreach($r in $reps){ $o=[System.IO.Path]::ChangeExtension($r,'.mp4'); if(Test-Path -LiteralPath $o){ $outs += $o } }
foreach($o in $outs){
  $d = (& $ffprobe -v error -show_entries format=duration -of csv=p=0 $o 2>&1) -join ''
  $a = (& $ffprobe -v error -select_streams a -show_entries stream=codec_name,channels -of csv=p=0 $o 2>&1) -join ''
  Say ("  {0}  {1:N1} MB / {2}s  音轨[{3}]" -f (Split-Path $o -Leaf), ((Get-Item -LiteralPath $o).Length/1MB), $d, $a)
}
if($outs.Count -ge 2){
  for($i=0;$i -lt $outs.Count;$i++){ for($j=$i+1;$j -lt $outs.Count;$j++){
    $sec = 45
    $r = (& $ffmpeg -hide_banner -t $sec -i $outs[$i] -t $sec -i $outs[$j] -lavfi "[0:v][1:v]psnr" -f null NUL 2>&1) -join "`n"
    $avg = if($r -match 'average:([\d.]+)'){ [double]$Matches[1] } else { -1 }
    $verdict = if($avg -lt 0){ '无法判定' } elseif($avg -ge 35){ '❌ 几乎同一画面' } elseif($avg -ge 28){ '⚠ 可能相同' } else { '✅ 不同对局' }
    Say ("  #{0} vs #{1}: PSNR 平均 {2:N1} dB → {3}" -f ($i+1),($j+1),$avg,$verdict)
  }}
}

$j2 = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j2.AskSaveLocation=$true; $j2.SkipExisting=$true
$j2 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Say ""
Say "===== 验证结束（$(Get-Date -Format 'HH:mm:ss')）====="
