# 受控验证两格并行（一短一长）：
#   ① 每格只有自己的声音（不再混入单文件采集源）
#   ② 先结束的那一格按自己的结束时间裁剪，后面不再跟着黑屏/别人的画面
$root  = 'D:\【完整包】非想天则整合包2026-06-15'
$dir   = "$root\RepRecorder"
$log   = "$dir\RepRecorder.log"
$app   = "$dir\RepRecorder.exe"
$cfg   = "$dir\RepRecorder.settings.json"
$outlog= "$dir\验证-两格并行长短rep.log"
$replay= "$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ffprobe='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffprobe.exe'
$ffmpeg ='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }
function Find-Rep($leaf){ $f=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter $leaf -ErrorAction SilentlyContinue | Select-Object -First 1; if($f){return $f.FullName}; return $null }
$short = Find-Rep '26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep'
$long  = Find-Rep '26-09-17_18-11-44_Ciel(红美铃)_诺诺真好玩喵(天子)_(2︰0_胜).rep'
if(-not $short -or -not $long){ Say "找不到 rep"; exit 1 }
$reps = @($short, $long)   # 排序后第 1 格=Aquila(短)，第 2 格=诺诺(长)

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RP {
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
  $cb=[RP+EP]{ param($ch,$l) [void]$script:ks.Add([pscustomobject]@{H=$ch;C=[RP]::Cls($ch);T=[RP]::Txt($ch)}); return $true }
  [void][RP]::EnumChildWindows($h,$cb,[IntPtr]::Zero); return $script:ks }
function Click-Btn($h,$like){ foreach($k in (Kids $h)){ if($k.C -like '*BUTTON*' -and $k.T -like $like){ Say ("  点按钮「{0}」" -f $k.T); [void][RP]::SendMessageW($k.H,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero); Start-Sleep -Seconds 2; return $true } }; Say "  ✗ 找不到「$like」"; return $false }
function Dur($f){ (& $ffprobe -v error -show_entries format=duration -of csv=p=0 $f 2>&1) -join '' }
function AudioMax($f){ $vd=(& $ffmpeg -hide_banner -i $f -af volumedetect -f null NUL 2>&1) -join "`n"; if($vd -match 'max_volume:\s*([-\d.]+)'){ return [double]$Matches[1] }; return -999 }

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== 两格并行（短+长）验证（$(Get-Date -Format 'HH:mm:ss')）====="
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.SkipExisting=$false; $j.ParallelCount=2; $j.RecordGameAudio=$true; $j.AutoPrepareObs=$true
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
foreach($r in $reps){ $o=[System.IO.Path]::ChangeExtension($r,'.mp4'); if(Test-Path -LiteralPath $o){ Remove-Item -LiteralPath $o -Force } }
Say ("  第 1 格（短，约 40 秒）= " + (Split-Path $short -Leaf))
Say ("  第 2 格（长，约 100 秒）= " + (Split-Path $long -Leaf))
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue

$pr = Start-Process -FilePath $app -ArgumentList ('"'+$short+'"'),('"'+$long+'"') -PassThru
Start-Sleep -Seconds 12
$pr.Refresh(); $h=$pr.MainWindowHandle
if($h -eq [IntPtr]::Zero){ Say "✗ 主窗口没出现"; exit 1 }
$skip = @(Get-Content -LiteralPath $log -Encoding UTF8).Count
Click-Btn $h "并行录制×2" | Out-Null
$sw=[Diagnostics.Stopwatch]::StartNew()
while($sw.Elapsed.TotalSeconds -lt 600){
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
Say ("整批用时 {0:N0} 秒" -f $sw.Elapsed.TotalSeconds)
Say ""
Say "---- 关键日志 ----"
$tail | Select-String '对局结束|隐藏|单文件用的采集源|游戏声音进录像|绑定窗口串|重新绑定完成|每格画面|切分完成|静音' | ForEach-Object { Say ("  " + $_.Line) }

Say ""
Say "---- 两个产物 ----"
$outs=@(); foreach($r in $reps){ $o=[System.IO.Path]::ChangeExtension($r,'.mp4'); if(Test-Path -LiteralPath $o){ $outs += $o } }
$idx=0
foreach($o in $outs){
  $idx++
  $a=(& $ffprobe -v error -select_streams a -show_entries stream=codec_name,channels -of csv=p=0 $o 2>&1) -join ''
  Say ("  第 {0} 格  {1,6:N0} MB  时长 {2,6:N1}s  音轨[{3}]  max_volume={4} dB" -f $idx, ((Get-Item -LiteralPath $o).Length/1MB), ([double](Dur $o)), $(if($a){$a}else{'无'}), (AudioMax $o))
}
if($outs.Count -eq 2){
  $d1=[double](Dur $outs[0]); $d2=[double](Dur $outs[1])
  Say ""
  Say ("  时长对比：短的那格 {0:N1}s，长的那格 {1:N1}s → {2}" -f $d1,$d2, $(if($d1 -lt $d2 - 20){'✅ 先结束的那格确实按自己的长度裁掉了后面多余部分'}else{'❌ 两格时长几乎一样（没裁剪成功）'}))
  $tmp="$env:TEMP\au2p"; New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  $raws=@()
  foreach($o in $outs){ $w=Join-Path $tmp ([guid]::NewGuid().ToString('N')+'.raw'); & $ffmpeg -hide_banner -y -t 30 -i $o -map 0:a:0 -ac 1 -ar 8000 -f s16le $w 2>&1 | Out-Null; $raws += ,([System.IO.File]::ReadAllBytes($w)) }
  $len=($raws | ForEach-Object { $_.Length } | Measure-Object -Minimum).Minimum
  $sum=0;$n=0
  for($k=0;$k -lt $len;$k+=4){ $x=[BitConverter]::ToInt16($raws[0],$k); $y=[BitConverter]::ToInt16($raws[1],$k); $sum += [Math]::Abs($x-$y); $n++ }
  $avg = if($n -gt 0){ $sum/$n } else { 0 }
  Say ("  两格音轨差异（前 30 秒平均差）= {0:N0} → {1}" -f $avg, $(if($avg -gt 300){'✅ 各自独立（没有互相混入）'}else{'❌ 疑似混在一起'}))
}
Say ""
Say "---- 黑帧检测（blackdetect d=0.01s，pix_th=0.10）----"
foreach($o in $outs){
  $r = (& $ffmpeg -hide_banner -i $o -vf "blackdetect=d=0.01:pix_th=0.10" -f null NUL 2>&1) -join "`n"
  $ms = [regex]::Matches($r, 'black_start:([\d.]+) black_end:([\d.]+)')
  if($ms.Count -eq 0){ Say ("  {0}：全程没有黑段" -f (Split-Path $o -Leaf).Substring(0,14)) }
  else {
    $short = 0
    foreach($m in $ms){
      $s=[double]$m.Groups[1].Value; $e=[double]$m.Groups[2].Value
      $len=$e-$s
      $flag = if($s -gt 6 -and $len -lt 0.5){ $short++; '  ← ⚠ 孤立黑帧' } else { '' }
      Say ("  {0}：{1:N2}s ~ {2:N2}s（{3:N2} 秒）{4}" -f (Split-Path $o -Leaf).Substring(0,10), $s, $e, $len, $flag)
    }
    if($short -eq 0){ Say "  → 6 秒之后没有出现孤立黑帧 ✅" } else { Say ("  → 仍有 {0} 处孤立黑帧 ❌" -f $short) }
  }
}
$j2 = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j2.AskSaveLocation=$true; $j2.SkipExisting=$true; $j2.ParallelCount=3; $j2.RecordGameAudio=$true
$j2 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Say "===== 结束（$(Get-Date -Format 'HH:mm:ss')）====="
