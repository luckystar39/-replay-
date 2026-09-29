# 验证「同时录制数量」上限改成 6 之后：下拉只有 6 项，且 6 格并行时每格都有自己的声音
$root  = 'D:\【完整包】非想天则整合包2026-06-15'
$dir   = "$root\RepRecorder"
$log   = "$dir\RepRecorder.log"
$app   = "$dir\RepRecorder.exe"
$cfg   = "$dir\RepRecorder.settings.json"
$outlog= "$dir\验证-六实例并行含声音.log"
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
 '26-08-21_13-32-08_暮光仓鼠(红美铃)_神AI(铃仙)_(0︰0_无).rep'
)
$reps=@(); foreach($n in $names){ $p=Find-Rep $n; if($p){ $reps += $p } else { Say "✗ 缺 rep: $n" } }
if($reps.Count -lt 6){ Say "只找到 $($reps.Count) 个，中止"; exit 1 }

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RC6 {
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
  $cb=[RC6+EP]{ param($ch,$l) [void]$script:ks.Add([pscustomobject]@{H=$ch;C=[RC6]::Cls($ch);T=[RC6]::Txt($ch)}); return $true }
  [void][RC6]::EnumChildWindows($h,$cb,[IntPtr]::Zero); return $script:ks }
function Click-Btn($h,$like){ foreach($k in (Kids $h)){ if($k.C -like '*BUTTON*' -and $k.T -like $like){ Say ("  点按钮「{0}」" -f $k.T); [void][RC6]::SendMessageW($k.H,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero); Start-Sleep -Seconds 2; return $true } }; Say "  ✗ 找不到「$like」"; return $false }
function Audio-Max($f){ $vd=(& $ffmpeg -hide_banner -i $f -af volumedetect -f null NUL 2>&1) -join "`n"; if($vd -match 'max_volume:\s*([-\d.]+)'){ return [double]$Matches[1] }; return -999 }

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== 上限 6 验证：下拉项数 + 6 格并行每格有声（$(Get-Date -Format 'HH:mm:ss')）====="
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.SkipExisting=$false; $j.ParallelCount=6; $j.RecordGameAudio=$true; $j.AutoPrepareObs=$true
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
foreach($r in $reps){ $o=[System.IO.Path]::ChangeExtension($r,'.mp4'); if(Test-Path -LiteralPath $o){ Remove-Item -LiteralPath $o -Force } }
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue

$argList = ($reps | ForEach-Object { '"' + $_ + '"' }) -join ' '
$pr = Start-Process -FilePath $app -ArgumentList $argList -PassThru
Start-Sleep -Seconds 12
$pr.Refresh(); $h=$pr.MainWindowHandle
if($h -eq [IntPtr]::Zero){ Say "✗ 主窗口没出现"; exit 1 }
$cbx = (Kids $h | Where-Object { $_.C -like '*COMBOBOX*' } | Select-Object -First 1).H
$cnt = [int][RC6]::SendMessageW($cbx, 0x0146, [IntPtr]::Zero, [IntPtr]::Zero)   # CB_GETCOUNT
$cur = [int][RC6]::SendMessageW($cbx, 0x0147, [IntPtr]::Zero, [IntPtr]::Zero)   # CB_GETCURSEL
Say ("  下拉框项数 = {0}（应为 6）；当前 = 第 {1} 项" -f $cnt, ($cur+1))
Say ("  并行按钮 = 「{0}」" -f [RC6]::Txt((Kids $h | Where-Object { $_.T -like '并行录制*' } | Select-Object -First 1).H))

$skip = @(Get-Content -LiteralPath $log -Encoding UTF8).Count
Click-Btn $h "并行录制×6" | Out-Null
$sw=[Diagnostics.Stopwatch]::StartNew(); $peak=0
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
Say ("用时 {0:N0} 秒；6 个实例内存峰值 {1:N0} MB" -f $sw.Elapsed.TotalSeconds, ($peak/1MB))
Say ""
Say "---- 关键日志 ----"
$tail | Select-String '⚠|绑定窗口串|重新绑定完成|每格画面|同时录|游戏声音进录像|切分完成|静音' | ForEach-Object { Say ("  " + $_.Line) }

Say ""
Say "---- 6 个产物（每格都必须有声）----"
$outs=@()
foreach($r in $reps){ $o=[System.IO.Path]::ChangeExtension($r,'.mp4'); if(Test-Path -LiteralPath $o){ $outs += $o } }
$bad=0
foreach($o in $outs){
  $d=(& $ffprobe -v error -show_entries format=duration -of csv=p=0 $o 2>&1) -join ''
  $a=(& $ffprobe -v error -select_streams a -show_entries stream=codec_name,channels -of csv=p=0 $o 2>&1) -join ''
  $mx=Audio-Max $o
  $ok = ($mx -gt -60)
  if(-not $ok){ $bad++ }
  Say ("  {0,-12} {1,6:N0} MB {2,6}s  音轨[{3}]  max_volume={4} dB  {5}" -f (Split-Path $o -Leaf).Substring(0,8), ((Get-Item -LiteralPath $o).Length/1MB), ([math]::Round([double]$d,1)), $(if($a){$a}else{'无'}), $mx, $(if($ok){'✅ 有声'}else{'❌ 没声音'}))
}
Say ("  结论：{0} / {1} 个有声" -f ($outs.Count-$bad), $outs.Count)

# 音频互相独立（各自是各自那局的声音）
if($outs.Count -ge 2){
  Say ""
  Say "---- 音轨互相独立性（两两比对前 20 秒）----"
  $tmp="$env:TEMP\au6"; New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  $raws=@()
  foreach($o in $outs){ $w=Join-Path $tmp ([guid]::NewGuid().ToString('N')+'.raw'); & $ffmpeg -hide_banner -y -t 20 -i $o -map 0:a:0 -ac 1 -ar 8000 -f s16le $w 2>&1 | Out-Null; $raws += ,([System.IO.File]::ReadAllBytes($w)) }
  $len=($raws | ForEach-Object { $_.Length } | Measure-Object -Minimum).Minimum
  $dup=0
  for($i=0;$i -lt 6;$i++){ for($j=$i+1;$j -lt 6;$j++){
    $sum=0;$n=0
    for($k=0;$k -lt $len;$k+=4){ $x=[BitConverter]::ToInt16($raws[$i],$k); $y=[BitConverter]::ToInt16($raws[$j],$k); $sum += [Math]::Abs($x-$y); $n++ }
    $avg = if($n -gt 0){ $sum/$n } else { 0 }
    if($avg -lt 100){ $dup++; Say ("  #{0} vs #{1}: 平均差 {2:N0} → ⚠ 疑似同一路声音" -f ($i+1),($j+1),$avg) }
  }}
  if($dup -eq 0){ Say "  15 组两两比对全部不同 → ✅ 每格拿到的是自己那局的声音" }
}
$j2 = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j2.AskSaveLocation=$true; $j2.SkipExisting=$true; $j2.ParallelCount=3; $j2.RecordGameAudio=$true
$j2 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Say "===== 结束（$(Get-Date -Format 'HH:mm:ss')）====="
