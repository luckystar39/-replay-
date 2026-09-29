# 验证「录BGM」按钮：点它 → 改 ReplayDnD.ini 的 MuteMusic → 录出来的音频真的不一样
$root  = 'D:\【完整包】非想天则整合包2026-06-15'
$dir   = "$root\RepRecorder"
$log   = "$dir\RepRecorder.log"
$app   = "$dir\RepRecorder.exe"
$cfg   = "$dir\RepRecorder.settings.json"
$outlog= "$dir\验证-录BGM按钮.log"
$ini1  = "$root\【完整包】非想天则整合包2026-06-15\th123\modules\ReplayDnD\ReplayDnD.ini"
$ini2  = "$root\【完整包】非想天则整合包2026-06-15\th123\modules\ReplayDnD-old\ReplayDnD.ini"
$rep   = "$root\【完整包】非想天则整合包2026-06-15\th123\replay\【联机对战Rep】\[Aquila(3) ]\26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep"
$out   = [System.IO.Path]::ChangeExtension($rep,'.mp4')
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }
function MuteVal($p){ if(Test-Path $p){ (Select-String -LiteralPath $p -Pattern 'MuteMusic\s*=\s*(\d)').Matches.Groups[1].Value } else { '缺文件' } }
function SetMute($p,$v){ $t=[System.IO.File]::ReadAllText($p); $t=[regex]::Replace($t,'(?im)^\s*MuteMusic\s*=\s*\d+\s*$',"MuteMusic=$v"); [System.IO.File]::WriteAllText($p,$t) }

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RB2 {
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
  $cb=[RB2+EP]{ param($ch,$l) [void]$script:ks.Add([pscustomobject]@{H=$ch;C=[RB2]::Cls($ch);T=[RB2]::Txt($ch)}); return $true }
  [void][RB2]::EnumChildWindows($h,$cb,[IntPtr]::Zero); return $script:ks }
function Click-Btn($h,$like){ foreach($k in (Kids $h)){ if($k.C -like '*BUTTON*' -and $k.T -like $like){ Say ("  点按钮「{0}」" -f $k.T); [void][RB2]::SendMessageW($k.H,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero); Start-Sleep -Seconds 2; return $true } }; Say "  ✗ 找不到「$like」"; return $false }
function Btn-Text($h,$like){ foreach($k in (Kids $h)){ if($k.C -like '*BUTTON*' -and $k.T -like $like){ return $k.T } }; return '<无>' }
function Wait-Marker($pattern,$timeoutSec,$skip){ $sw=[Diagnostics.Stopwatch]::StartNew()
  while($sw.Elapsed.TotalSeconds -lt $timeoutSec){ Start-Sleep -Seconds 3
    if(Test-Path -LiteralPath $log){ $all=@(Get-Content -LiteralPath $log -Encoding UTF8)
      if($all.Count -gt $skip){ $tail=@($all[$skip..($all.Count-1)]); if(@($tail | Select-String -Pattern $pattern).Count -gt 0){ return ,$tail } } } }
  return ,@() }

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== 「录BGM」按钮验证（$(Get-Date -Format 'HH:mm:ss')）====="
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.SkipExisting=$false; $j.RecordGameAudio=$true
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
SetMute $ini1 1; SetMute $ini2 1     # 先确保是"没有 BGM"的状态
Say ("  起始 ini：ReplayDnD MuteMusic={0}，ReplayDnD-old MuteMusic={1}" -f (MuteVal $ini1), (MuteVal $ini2))
if(Test-Path -LiteralPath $out){ Remove-Item -LiteralPath $out -Force }

# ---------- 第一次：关 BGM ----------
Say ""
Say "########## A：BGM=关（MuteMusic=1）录一次 ##########"
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$pr = Start-Process -FilePath $app -ArgumentList ('"'+$rep+'"') -PassThru
Start-Sleep -Seconds 10
$pr.Refresh(); $h=$pr.MainWindowHandle
Say ("  界面按钮：{0}" -f (Btn-Text $h '录BGM*'))
$skip = @(Get-Content -LiteralPath $log -Encoding UTF8).Count
Click-Btn $h "开始录制" | Out-Null
$t = Wait-Marker '队列结束' 300 $skip
$t | Select-String 'BGM|游戏声音录制|完成' | ForEach-Object { Say ("  日志| " + $_.Line) }
if(Test-Path -LiteralPath $out){ Copy-Item $out "$dir\样本-BGM关.mp4" -Force; Say ("  产物：{0:N1} MB" -f ((Get-Item $out).Length/1MB)) }

# ---------- 第二次：点按钮开 BGM ----------
Say ""
Say "########## B：点「录BGM」按钮 → 再录一次 ##########"
Click-Btn $h "录BGM：关 ✕" | Out-Null
Say ("  点完后按钮：{0}" -f (Btn-Text $h '录BGM*'))
Say ("  ini 现在：ReplayDnD MuteMusic={0}，ReplayDnD-old MuteMusic={1}" -f (MuteVal $ini1), (MuteVal $ini2))
if(Test-Path -LiteralPath $out){ Remove-Item -LiteralPath $out -Force }
Click-Btn $h "刷新" | Out-Null
Start-Sleep -Seconds 2
$skip2 = @(Get-Content -LiteralPath $log -Encoding UTF8).Count
Click-Btn $h "开始录制" | Out-Null
$t2 = Wait-Marker '队列结束' 300 $skip2
$t2 | Select-String 'BGM|游戏声音录制|完成' | ForEach-Object { Say ("  日志| " + $_.Line) }
if(Test-Path -LiteralPath $out){ Copy-Item $out "$dir\样本-BGM开.mp4" -Force; Say ("  产物：{0:N1} MB" -f ((Get-Item $out).Length/1MB)) }

# ---------- 比对两段音频 ----------
Say ""
Say "########## 音频比对 ##########"
$a="$dir\样本-BGM关.mp4"; $b="$dir\样本-BGM开.mp4"
if((Test-Path -LiteralPath $a) -and (Test-Path -LiteralPath $b)){
  foreach($f in @($a,$b)){
    $vd=(& $ffmpeg -hide_banner -i $f -af volumedetect -f null NUL 2>&1) -join "`n"
    $mean = if($vd -match 'mean_volume:\s*([-\d.]+)'){ $Matches[1] } else { '?' }
    $max  = if($vd -match 'max_volume:\s*([-\d.]+)'){ $Matches[1] } else { '?' }
    Say ("  {0}：mean={1} dB  max={2} dB" -f (Split-Path $f -Leaf), $mean, $max)
  }
  $tmp="$env:TEMP\bgm"; New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  $raw=@()
  foreach($f in @($a,$b)){ $w=Join-Path $tmp ([guid]::NewGuid().ToString('N')+'.raw'); & $ffmpeg -hide_banner -y -t 30 -i $f -map 0:a:0 -ac 1 -ar 8000 -f s16le $w 2>&1 | Out-Null; $raw += ,([System.IO.File]::ReadAllBytes($w)) }
  $len=($raw | ForEach-Object { $_.Length } | Measure-Object -Minimum).Minimum
  $sum=0;$n=0
  for($k=0;$k -lt $len;$k+=4){ $x=[BitConverter]::ToInt16($raw[0],$k); $y=[BitConverter]::ToInt16($raw[1],$k); $sum += [Math]::Abs($x-$y); $n++ }
  $avg = if($n -gt 0){ $sum/$n } else { 0 }
  Say ("  两段音频的平均差 = {0:N0} → {1}" -f $avg, $(if($avg -gt 300){'✅ 明显不同（BGM 开关真的生效了）'}else{'❌ 几乎一样（开关没生效？）'}))
} else { Say "  样本不全，无法比对" }

# ---------- 收尾 ----------
SetMute $ini1 1; SetMute $ini2 1
Say ""
Say ("  已把 ini 还原成 MuteMusic=1（MuteMusic 现在：{0} / {1}）" -f (MuteVal $ini1), (MuteVal $ini2))
$j2 = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j2.AskSaveLocation=$true; $j2.SkipExisting=$true
$j2 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Say "===== 结束（$(Get-Date -Format 'HH:mm:ss')）====="
