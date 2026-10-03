# 关键验证：录制中「静音游戏的音频会话」，OBS 录出来的成品还有没有游戏声音？
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"; $app="$dir\RepRecorder.exe"; $cfg="$dir\RepRecorder.settings.json"
$outlog="$dir\验证-会话静音对采集的影响.log"
$ini1="$root\【完整包】非想天则整合包2026-06-15\th123\modules\ReplayDnD\ReplayDnD.ini"
$ini2="$root\【完整包】非想天则整合包2026-06-15\th123\modules\ReplayDnD-old\ReplayDnD.ini"
$rep="$root\【完整包】非想天则整合包2026-06-15\th123\replay\【联机对战Rep】\[Aquila(3) ]\26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep"
$out=[System.IO.Path]::ChangeExtension($rep,'.mp4')
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
$ffprobe='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffprobe.exe'
function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }
function SetMute($p,$v){ $t=[System.IO.File]::ReadAllText($p); $t=[regex]::Replace($t,'(?im)^\s*MuteMusic\s*=\s*\d+\s*$',"MuteMusic=$v"); [System.IO.File]::WriteAllText($p,$t) }
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RB4 {
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
  [void][RB4]::EnumChildWindows($h,[RB4+EP]{ param($c,$l) if([RB4]::Cls($c) -like '*BUTTON*'){ $t=[RB4]::Txt($c); if($t -and $t -like $like){ $script:hit=$c; return $false } }; return $true },[IntPtr]::Zero)
  if($script:hit -eq [IntPtr]::Zero){ Say "  ✗ 找不到「$like」"; return $false }
  Say ("  点按钮「{0}」" -f [RB4]::Txt($script:hit))
  [void][RB4]::SendMessageW($script:hit,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero); Start-Sleep -Seconds 2; return $true }

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== 会话静音 vs OBS 采集（$(Get-Date -Format 'HH:mm:ss')）====="
$bak1=(Get-Content -LiteralPath $ini1 -Raw -Encoding UTF8); $bak2=(Get-Content -LiteralPath $ini2 -Raw -Encoding UTF8)
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 3
# BGM 打开，保证有稳定声源
SetMute $ini1 0; SetMute $ini2 0
Say "  MuteMusic=0（BGM 开，保证有声音可比）"
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.SkipExisting=$false
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
if(Test-Path -LiteralPath $out){ Remove-Item -LiteralPath $out -Force }
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue

# 反射加载程序自己的「静音某个进程的音频会话」实现
$asm=[Reflection.Assembly]::LoadFrom($app)
$t=$asm.GetType('RepRecorder.GameAudio')
if(-not $t){ $t = ($asm.GetTypes() | Where-Object { $_.Name -eq 'GameAudio' } | Select-Object -First 1) }
$mSet=if($t){ $t.GetMethod('SetProcessMute') } else { $null }
Say "  反射到 $($t.FullName).SetProcessMute = $($mSet -ne $null)"
if(-not $mSet){ Say "  ✗ 拿不到静音方法，测试无法进行"; exit 1 }

$pr = Start-Process -FilePath $app -ArgumentList ('"'+$rep+'"') -PassThru
Start-Sleep -Seconds 10
$pr.Refresh(); $h=$pr.MainWindowHandle
Click-Btn $h "开始录制" | Out-Null

# 等游戏起来并出声，然后静音它的音频会话
$gpid=0; $muted=$false
$sw=[Diagnostics.Stopwatch]::StartNew()
while($sw.Elapsed.TotalSeconds -lt 200){
  Start-Sleep -Seconds 2
  if(-not $muted){
    $g=Get-Process th123 -ErrorAction SilentlyContinue | Select-Object -First 1
    if($g){
      $gpid=$g.Id
      $args=@([int]$gpid,$true,$null)
      $ok=$mSet.Invoke($null,$args)
      Say ("  静音游戏会话（pid={0}）→ 返回 {1}，说明：{2}" -f $gpid,$ok,$args[2])
      if($ok){ $muted=$true; Say ("  静音时刻：录制开始后 {0:N0} 秒" -f $sw.Elapsed.TotalSeconds) }
    }
  }
  if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } }
}
Say ("  录制总用时 {0:N0} 秒" -f $sw.Elapsed.TotalSeconds)
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '完成|游戏声音|静音|错误|失败' | ForEach-Object { Say ("  日志| " + $_.Line) }

Say ""
Say "########## 成品音频 ##########"
if(Test-Path -LiteralPath $out){
  $sz=(Get-Item -LiteralPath $out).Length
  $vd=(& $ffmpeg -hide_banner -i $out -af volumedetect -f null NUL 2>&1) -join "`n"
  $mean=if($vd -match 'mean_volume:\s*([-\d.]+)'){$Matches[1]}else{'?'}
  $max =if($vd -match 'max_volume:\s*([-\d.]+)'){$Matches[1]}else{'?'}
  $d=(& $ffprobe -v error -show_entries format=duration -of csv=p=0 $out 2>&1) -join ''
  Say ("  本次（录制中静音了游戏会话）：{0:N1} MB  {1:N1}s  mean={2} dB  max={3} dB" -f ($sz/1MB),[double]$d,$mean,$max)
  # 对照：同样 BGM 开、但没静音会话的样本
  $ref="$dir\样本-BGM开.mp4"
  if(Test-Path -LiteralPath $ref){
    $vd2=(& $ffmpeg -hide_banner -i $ref -af volumedetect -f null NUL 2>&1) -join "`n"
    $mean2=if($vd2 -match 'mean_volume:\s*([-\d.]+)'){$Matches[1]}else{'?'}
    Say ("  对照（BGM开、没静音会话）      ：   {0}  mean={1} dB" -f (Split-Path $ref -Leaf),$mean2)
    $m1=[double]$mean; $m2=[double]$mean2
    if($m1 -lt ($m2 - 20)){ Say "  → ❌ 静音会话后成品几乎没声音了：OBS 的采集被静音掐断" }
    elseif([Math]::Abs($m1-$m2) -lt 6){ Say "  → ✅ 成品音量与对照相当：静音会话不影响 OBS 采集" }
    else { Say "  → ⚠ 体积/音量有差异，需要人工再听一遍" }
  }
} else { Say "  ✗ 没有录出成品" }

# 还原
$args=@([int]$gpid,$false,$null); if($gpid -gt 0){ try{ $mSet.Invoke($null,$args) | Out-Null }catch{} }
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
[System.IO.File]::WriteAllText($ini1,$bak1,(New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText($ini2,$bak2,(New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
Say "  设置已还原（MuteMusic=1 / AskSaveLocation=1 / SkipExisting=1）"
Say "===== 结束（$(Get-Date -Format 'HH:mm:ss')）====="
