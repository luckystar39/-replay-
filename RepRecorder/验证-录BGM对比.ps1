# 补测：把当前"BGM=开"的成品存样本，再录一条"BGM=关"的，比对音频
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"; $app="$dir\RepRecorder.exe"; $cfg="$dir\RepRecorder.settings.json"
$outlog="$dir\验证-录BGM对比.log"
$ini1="$root\【完整包】非想天则整合包2026-06-15\th123\modules\ReplayDnD\ReplayDnD.ini"
$ini2="$root\【完整包】非想天则整合包2026-06-15\th123\modules\ReplayDnD-old\ReplayDnD.ini"
$rep="$root\【完整包】非想天则整合包2026-06-15\th123\replay\【联机对战Rep】\[Aquila(3) ]\26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep"
$out=[System.IO.Path]::ChangeExtension($rep,'.mp4')
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
$ffprobe='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffprobe.exe'
function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }
function SetMute($p,$v){ $t=[System.IO.File]::ReadAllText($p); $t=[regex]::Replace($t,'(?im)^\s*MuteMusic\s*=\s*\d+\s*$',"MuteMusic=$v"); [System.IO.File]::WriteAllText($p,$t) }
function MuteVal($p){ (Select-String -LiteralPath $p -Pattern 'MuteMusic\s*=\s*(\d)').Matches.Groups[1].Value }
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RB3 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
}
"@
function Click-Btn($h,$like){ $script:hit=[IntPtr]::Zero
  [void][RB3]::EnumChildWindows($h,[RB3+EP]{ param($c,$l) if([RB3]::Cls($c) -like '*BUTTON*'){ $t=[RB3]::Txt($c); if($t -and $t -like $like){ $script:hit=$c; return $false } }; return $true },[IntPtr]::Zero)
  if($script:hit -eq [IntPtr]::Zero){ Say "  ✗ 找不到「$like」"; return $false }
  Say ("  点按钮「{0}」" -f [RB3]::Txt($script:hit))
  [void][RB3]::SendMessageW($script:hit,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero); Start-Sleep -Seconds 2; return $true }

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== BGM 开/关 音频对比（$(Get-Date -Format 'HH:mm:ss')）====="
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 3
# 1) 先把当前这条（BGM=开 时录的）存成样本
if(Test-Path -LiteralPath $out){
  $sz=(Get-Item -LiteralPath $out).Length
  Copy-Item -LiteralPath $out -Destination "$dir\样本-BGM开.mp4" -Force
  Say ("  已存样本：BGM开  {0:N1} MB（{1}）" -f ($sz/1MB), (Get-Date (Get-Item -LiteralPath $out).LastWriteTime -Format 'HH:mm:ss'))
} else { Say "  ✗ 当前没有成品，无法存 BGM开 样本" }

# 2) 关 BGM 再录一条
SetMute $ini1 1; SetMute $ini2 1
Say ("  已把 ini 设成 MuteMusic=1（{0} / {1}）→ 现在录一条『关 BGM』的" -f (MuteVal $ini1), (MuteVal $ini2))
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.SkipExisting=$false
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
if(Test-Path -LiteralPath $out){ Remove-Item -LiteralPath $out -Force }
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$pr = Start-Process -FilePath $app -ArgumentList ('"'+$rep+'"') -PassThru
Start-Sleep -Seconds 10
$pr.Refresh(); $h=$pr.MainWindowHandle
Click-Btn $h "开始录制" | Out-Null
$sw=[Diagnostics.Stopwatch]::StartNew()
while($sw.Elapsed.TotalSeconds -lt 200){
  Start-Sleep -Seconds 3
  if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } }
}
Say ("  录制用时 {0:N0} 秒" -f $sw.Elapsed.TotalSeconds)
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String 'BGM|完成|游戏声音录制' | ForEach-Object { Say ("  日志| " + $_.Line) }
if(Test-Path -LiteralPath $out){
  $sz=(Get-Item -LiteralPath $out).Length
  Copy-Item -LiteralPath $out -Destination "$dir\样本-BGM关.mp4" -Force
  Say ("  已存样本：BGM关  {0:N1} MB" -f ($sz/1MB))
} else { Say "  ✗ 没有录出成品" }

# 3) 比对
Say ""
Say "########## 两段音频比对 ##########"
$a="$dir\样本-BGM开.mp4"; $b="$dir\样本-BGM关.mp4"
if((Test-Path -LiteralPath $a) -and (Test-Path -LiteralPath $b)){
  foreach($f in @($a,$b)){
    $vd=(& $ffmpeg -hide_banner -i $f -af volumedetect -f null NUL 2>&1) -join "`n"
    $mean=if($vd -match 'mean_volume:\s*([-\d.]+)'){$Matches[1]}else{'?'}
    $max =if($vd -match 'max_volume:\s*([-\d.]+)'){$Matches[1]}else{'?'}
    $d=(& $ffprobe -v error -show_entries format=duration -of csv=p=0 $f 2>&1) -join ''
    Say ("  {0}：{1:N1}s  mean={2} dB  max={3} dB" -f (Split-Path $f -Leaf), [double]$d, $mean, $max)
  }
  $tmp="$env:TEMP\bgm2"; New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  $raw=@()
  foreach($f in @($a,$b)){ $w=Join-Path $tmp ([guid]::NewGuid().ToString('N')+'.raw'); & $ffmpeg -hide_banner -y -t 30 -i $f -map 0:a:0 -ac 1 -ar 8000 -f s16le $w 2>&1 | Out-Null; $raw += ,([System.IO.File]::ReadAllBytes($w)) }
  $len=($raw | ForEach-Object { $_.Length } | Measure-Object -Minimum).Minimum
  $sum=0;$n=0
  for($k=0;$k -lt $len;$k+=4){ $x=[BitConverter]::ToInt16($raw[0],$k); $y=[BitConverter]::ToInt16($raw[1],$k); $sum += [Math]::Abs($x-$y); $n++ }
  $avg=if($n -gt 0){$sum/$n}else{0}
  Say ("  两段音频平均差 = {0:N0} → {1}" -f $avg, $(if($avg -gt 300){'✅ 明显不同（BGM 开关真的生效）'}else{'❌ 几乎一样'}))
} else { Say "  样本仍然不全：BGM开=$(Test-Path -LiteralPath $a)  BGM关=$(Test-Path -LiteralPath $b)" }
SetMute $ini1 1; SetMute $ini2 1
$j2 = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j2.AskSaveLocation=$true; $j2.SkipExisting=$true
$j2 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Say "===== 结束（$(Get-Date -Format 'HH:mm:ss')）====="
