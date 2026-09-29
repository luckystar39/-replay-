# 需求2：用 3 个实例做并行录制实测，并采样内存 / CPU / GPU 占用。
#   阶段1：3 个 rep 并行录制（录游戏声=关）→ 校验 3 个产物都是无声的（验证并行路径也受按钮控制）
#   阶段2：3 个 rep 并行录制（录游戏声=开）→ 采样内存/CPU/GPU，校验 3 个产物各带自己的原声（成品保留）
# 点击方式：向按钮窗口发 BM_CLICK（不依赖前台焦点）
$root  = 'D:\【完整包】非想天则整合包2026-06-15'
$dir   = "$root\RepRecorder"
$log   = "$dir\RepRecorder.log"
$app   = "$dir\RepRecorder.exe"
$cfg   = "$dir\RepRecorder.settings.json"
$csv   = "$dir\验证-三实例并行内存.csv"
$outlog= "$dir\验证-三实例并行内存.log"
$replay= "$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ffprobe='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffprobe.exe'
$ffmpeg ='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'

$reps = @(
  "$replay\【观战Rep】\[Unstrung Doll ]_[阿虚reality]\26-09-02_13-47-16_Unstrung Doll (爱丽丝)_阿虚reality(咲夜)_(0︰0_无).rep",
  "$replay\【联机对战Rep】\[Aquila(3) ]\26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep",
  "$replay\【观战Rep】\[Timepieceit LV2]_[menghenyinxin]\26-08-15_23-57-29_menghenyinxin(红美铃)_Timepieceit LV2(红美铃)_(1︰0_无).rep"
)

function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RC2 {
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
  $cb=[RC2+EP]{ param($ch,$l)
    if([RC2]::Cls($ch) -like '*BUTTON*'){
      $t=[RC2]::Txt($ch)
      if($t -and $t -like $like){ $script:hit=$ch; return $false }
    }
    return $true }
  [void][RC2]::EnumChildWindows($h,$cb,[IntPtr]::Zero)
  return $script:hit
}
function Click-Btn($h,$like){
  $b = Find-Btn $h $like
  if($b -eq [IntPtr]::Zero){ Say "  ✗ 找不到按钮「$like」"; return $false }
  Say ("  点按钮「{0}」" -f [RC2]::Txt($b))
  [void][RC2]::SendMessageW($b, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)   # BM_CLICK
  Start-Sleep -Seconds 2
  return $true
}
function Wait-Log($pattern,$timeoutSec,$skip){
  $sw=[Diagnostics.Stopwatch]::StartNew()
  while($sw.Elapsed.TotalSeconds -lt $timeoutSec){
    Start-Sleep -Seconds 3
    if(Test-Path -LiteralPath $log){
      $lines = @(Get-Content -LiteralPath $log -Encoding UTF8)
      if($lines.Count -gt $skip){
        $tail = @($lines[$skip..($lines.Count-1)])
        if(@($tail | Select-String -Pattern $pattern).Count -gt 0){ return $tail }
      }
    }
  }
  return @()
}
function New-Tail($skip){
  $all = @(Get-Content -LiteralPath $log -Encoding UTF8)
  if($all.Count -gt $skip){ return @($all[$skip..($all.Count-1)]) }
  return @()
}
function Audio-Max($f){
  $vd = (& $ffmpeg -hide_banner -i $f -af volumedetect -f null NUL 2>&1) -join "`n"
  if($vd -match 'max_volume:\s*([-\d.]+) dB'){ return $Matches[1] }
  return '<未测到>'
}
function Clean-Outputs(){
  foreach($r in $reps){
    $o=[System.IO.Path]::ChangeExtension($r,'.mp4')
    if(Test-Path -LiteralPath $o){ Remove-Item -LiteralPath $o -Force }
  }
}

Add-Type -AssemblyName Microsoft.VisualBasic
$mi = New-Object Microsoft.VisualBasic.Devices.ComputerInfo
$totalMb = [int]($mi.TotalPhysicalMemory/1MB)

Remove-Item -LiteralPath $outlog,$csv -Force -ErrorAction SilentlyContinue
Say "===== 三实例并行录制实测（$(Get-Date -Format 'HH:mm:ss')）====="
Say ("本机物理内存合计 {0:N0} MB；GPU：" -f $totalMb)
(& nvidia-smi --query-gpu=name,driver_version,memory.total --format=csv,noheader) | ForEach-Object { Say ("  " + $_) }

Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation = $false
$j.SkipExisting    = $false
$j.ParallelCount   = 3
$j.RecordGameAudio = $true
$j.AutoPrepareObs  = $true
$j.UseDedicatedScene = $true
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Say "设置：ParallelCount=3，AskSaveLocation=false，AutoPrepareObs=true，录游戏声=开"

Clean-Outputs
foreach($r in $reps){ Say ("  rep 存在={0}  {1} B  {2}" -f (Test-Path -LiteralPath $r), (Get-Item -LiteralPath $r).Length, (Split-Path $r -Leaf)) }
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue

$argList = ($reps | ForEach-Object { '"' + $_ + '"' }) -join ' '
$pr = Start-Process -FilePath $app -ArgumentList $argList -PassThru
Start-Sleep -Seconds 10
$pr.Refresh(); $h=$pr.MainWindowHandle
if($h -eq [IntPtr]::Zero){ Say "✗ 主窗口没出现，中止"; exit 1 }
Say ("  录游戏声按钮 = 「{0}」；并行按钮 = 「{1}」" -f [RC2]::Txt((Find-Btn $h '录游戏声*')), [RC2]::Txt((Find-Btn $h '并行录制*')))

# ================= 阶段1：并行（录游戏声=关）→ 应该无声 =================
Say ""
Say "########## 阶段1：3 实例并行录制（录游戏声=关），期望 3 个产物无声 ##########"
Click-Btn $h "录游戏声*" | Out-Null
$skip1 = @(Get-Content -LiteralPath $log -Encoding UTF8).Count
Click-Btn $h "并行录制*" | Out-Null
$sw1=[Diagnostics.Stopwatch]::StartNew()
$tail1 = Wait-Log '==== 并行录制结束 ====|并行录制出错|没有待录制的 rep' 600 $skip1
Say ("阶段1 用时 {0:N0} 秒" -f $sw1.Elapsed.TotalSeconds)
$tail1 | Set-Content -LiteralPath "$dir\样本-并行-关.log" -Encoding UTF8
$tail1 | Select-String -Pattern '并行|游戏声音|切分|完成|失败' | ForEach-Object { Say ("  " + $_.Line) }
Say "  产物（期望 max_volume ≈ -91 dB）："
foreach($r in $reps){
  $o=[System.IO.Path]::ChangeExtension($r,'.mp4')
  if(Test-Path -LiteralPath $o){ Say ("    {0}  {1:N2} MB  max_volume={2} dB" -f (Split-Path $o -Leaf), ((Get-Item -LiteralPath $o).Length/1MB), (Audio-Max $o)) }
  else { Say ("    ✗ 没有产物：" + (Split-Path $o -Leaf)) }
}

# ================= 阶段2：并行（录游戏声=开）+ 内存采样 =================
Say ""
Say "########## 阶段2：3 实例并行录制（录游戏声=开），采样内存 ##########"
Clean-Outputs
Start-Sleep -Seconds 2
Click-Btn $h "刷新" | Out-Null
Start-Sleep -Seconds 2
Click-Btn $h "录游戏声*" | Out-Null
Say "  已清掉阶段1的产物并点「刷新」（队列重新标成等待），录游戏声切回开"

$skip2 = @(Get-Content -LiteralPath $log -Encoding UTF8).Count
Click-Btn $h "并行录制*" | Out-Null
"t_sec,th_count,th_ws_mb_sum,th_ws_mb_each,obs_ws_mb,rep_ws_mb,gpu_util_pct,gpu_mem_mb,sys_avail_mb" | Set-Content -LiteralPath $csv -Encoding UTF8
$sw=[Diagnostics.Stopwatch]::StartNew()
$peakTh=0; $peakT=0; $phase='准备'; $samples=@()
while($sw.Elapsed.TotalSeconds -lt 900){
  $th  = @(Get-Process th123 -ErrorAction SilentlyContinue)
  $obs = @(Get-Process obs64 -ErrorAction SilentlyContinue)
  $rr  = @(Get-Process RepRecorder -ErrorAction SilentlyContinue)
  $thWs = if($th.Count -gt 0){ ($th | Measure-Object WorkingSet64 -Sum).Sum } else { 0 }
  $obsWs= if($obs.Count -gt 0){ ($obs | Measure-Object WorkingSet64 -Sum).Sum } else { 0 }
  $rrWs = if($rr.Count -gt 0){ ($rr | Measure-Object WorkingSet64 -Sum).Sum } else { 0 }
  $each = ($th | ForEach-Object { "{0:N0}" -f ($_.WorkingSet64/1MB) }) -join '+'
  $gpu = 'n/a'; $gmem='n/a'
  try { $g = (& nvidia-smi --query-gpu=utilization.gpu,memory.used --format=csv,noheader,nounits) -split ','; $gpu=$g[0].Trim(); $gmem=$g[1].Trim() } catch {}
  $avail = try { [int](Get-Counter '\Memory\Available MBytes').CounterSamples[0].CookedValue } catch { -1 }
  $t=[int]$sw.Elapsed.TotalSeconds
  "$t,$($th.Count),$([int]($thWs/1MB)),$each,$([int]($obsWs/1MB)),$([int]($rrWs/1MB)),$gpu,$gmem,$avail" | Add-Content -LiteralPath $csv -Encoding UTF8
  $samples += [pscustomobject]@{ t=$t; th=$th.Count; thMb=[int]($thWs/1MB); obsMb=[int]($obsWs/1MB); gpu=$gpu; gmem=$gmem; avail=$avail }
  if($thWs -gt $peakTh){ $peakTh=$thWs; $peakT=$t }
  if(Test-Path $log){
    # 只看本次新增的日志（整份日志里还留着阶段1的结束标记，不能整份匹配）
    $allNow = @(Get-Content -LiteralPath $log -Encoding UTF8)
    if($allNow.Count -gt $skip2){
      $txt = (@($allNow[$skip2..($allNow.Count-1)])) -join "`n"
      if($txt -match '==== 并行录制结束 ====|并行录制出错|没有待录制的 rep'){ $phase='结束'; break }
      if($txt -match '已开始录像'){ $phase='录制中' } elseif($txt -match '并行录制开始'){ $phase='准备中' }
    }
  }
  Start-Sleep -Seconds 5
}
$tail2 = New-Tail $skip2
Say ("阶段2 用时 {0:N0} 秒，阶段={1}，样本 {2} 个" -f $sw.Elapsed.TotalSeconds, $phase, $samples.Count)
$tail2 | Set-Content -LiteralPath "$dir\样本-并行-开.log" -Encoding UTF8

$three = @($samples | Where-Object { $_.th -eq 3 })
if($three.Count -gt 0){
  $thVals = @($three | ForEach-Object { $_.thMb })
  $obsVals= @($three | ForEach-Object { $_.obsMb })
  Say ""
  Say "---- 3 个实例同时在跑时的占用 ----"
  Say ("  3 个 th123 合计：最小 {0:N0} MB / 最大 {1:N0} MB（平均每实例 {2:N0}~{3:N0} MB）" -f `
        ($thVals | Measure-Object -Minimum).Minimum, ($thVals | Measure-Object -Maximum).Maximum, `
        (($thVals | Measure-Object -Minimum).Minimum/3), (($thVals | Measure-Object -Maximum).Maximum/3))
  Say ("  OBS 同期：{0:N0} ~ {1:N0} MB" -f ($obsVals | Measure-Object -Minimum).Minimum, ($obsVals | Measure-Object -Maximum).Maximum)
  Say ("  系统可用内存同期：{0:N0} ~ {1:N0} MB" -f (($three | ForEach-Object {$_.avail} | Measure-Object -Minimum).Minimum), (($three | ForEach-Object {$_.avail} | Measure-Object -Maximum).Maximum))
  $pk = @($samples | Where-Object { $_.t -eq $peakT })[0]
  Say ("  峰值：t={0}s 时 3 个实例共 {1:N0} MB（每实例约 {2:N0} MB）" -f $peakT, ($peakTh/1MB), ($pk.thMb/3))
} else { Say '  ⚠ 没有采到「3 个实例同时在跑」的样本' }
$gm = @($samples | Where-Object { $_.gpu -ne 'n/a' } | ForEach-Object { [int]$_.gpu })
$gmm= @($samples | Where-Object { $_.gmem -ne 'n/a' } | ForEach-Object { [int]$_.gmem })
if($gm.Count -gt 0){ Say ("  GPU 利用率：平均 {0:N0}% / 峰值 {1}%；显存峰值 {2} MB（含 OBS 与系统）" -f (($gm | Measure-Object -Average).Average), (($gm | Measure-Object -Maximum).Maximum), (($gmm | Measure-Object -Maximum).Maximum)) }
Say ("  CSV 明细：$csv")

Say ""
Say "---- 阶段2 日志关键行 ----"
$tail2 | Select-String -Pattern '并行|画布|游戏声音|切分|已还原|已恢复|拖入|静止|完成|失败|采集|黑屏' | ForEach-Object { Say ("  " + $_.Line) }

Say ""
Say "---- 阶段2 产物（期望：3 个视频，各带自己的游戏原声，留作成品）----"
foreach($r in $reps){
  $o=[System.IO.Path]::ChangeExtension($r,'.mp4')
  if(Test-Path -LiteralPath $o){
    $st = (& $ffprobe -v error -select_streams a -show_entries stream=index,codec_name,channels -of csv=p=0 $o 2>&1) -join ' | '
    $dur = (& $ffprobe -v error -show_entries format=duration -of csv=p=0 $o 2>&1) -join ''
    Say ("  {0}  {1:N2} MB  {2}s  音轨[{3}]  max_volume={4} dB" -f (Split-Path $o -Leaf), ((Get-Item -LiteralPath $o).Length/1MB), $dur, $(if($st){$st}else{'无'}), (Audio-Max $o))
  } else { Say ("  ✗ 没有产物：" + (Split-Path $o -Leaf)) }
}

$j2 = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j2.AskSaveLocation = $true
$j2.SkipExisting    = $true
$j2.RecordGameAudio = $true
$j2.ParallelCount   = 3
$j2 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Say ""
Say "===== 实测结束（$(Get-Date -Format 'HH:mm:ss')）====="
