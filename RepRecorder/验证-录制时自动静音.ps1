# 验证：新增的「录制时自动静音（听不到游戏）」—— 单录 + 并行 + 强制结束后的自动恢复
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"; $app="$dir\RepRecorder.exe"; $cfg="$dir\RepRecorder.settings.json"
$flag="$dir\RepRecorder.已静音待恢复"
$outlog="$dir\验证-录制时自动静音.log"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ini1="$root\【完整包】非想天则整合包2026-06-15\th123\modules\ReplayDnD\ReplayDnD.ini"
$ini2="$root\【完整包】非想天则整合包2026-06-15\th123\modules\ReplayDnD-old\ReplayDnD.ini"
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }
function Find-Rep($leaf){ $f=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter $leaf -ErrorAction SilentlyContinue | Select-Object -First 1; if($f){return $f.FullName}; return $null }
function SetIni($p,$v){ $t=[System.IO.File]::ReadAllText($p); $t=[regex]::Replace($t,'(?im)^\s*MuteMusic\s*=\s*\d+\s*$',"MuteMusic=$v"); [System.IO.File]::WriteAllText($p,$t) }
$short = Find-Rep '26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep'
$long  = Find-Rep '26-09-17_18-11-44_Ciel(红美铃)_诺诺真好玩喵(天子)_(2︰0_胜).rep'

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RB5 {
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
  [void][RB5]::EnumChildWindows($h,[RB5+EP]{ param($c,$l) if([RB5]::Cls($c) -like '*BUTTON*'){ $t=[RB5]::Txt($c); if($t -and $t -like $like){ $script:hit=$c; return $false } }; return $true },[IntPtr]::Zero)
  if($script:hit -eq [IntPtr]::Zero){ Say "  ✗ 找不到「$like」"; return $false }
  Say ("  点按钮「{0}」" -f [RB5]::Txt($script:hit))
  [void][RB5]::SendMessageW($script:hit,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero); Start-Sleep -Seconds 2; return $true }
function Child-Check($h){ # 列出勾选框文字，确认新开关真的在界面上
  $script:names=@()
  [void][RB5]::EnumChildWindows($h,[RB5+EP]{ param($c,$l) if([RB5]::Cls($c) -eq 'Button'){ $t=[RB5]::Txt($c); if($t){ $script:names += $t } }; return $true },[IntPtr]::Zero)
  return $script:names }

# 用程序自己的实现读/写"系统静音"，避免重复造轮子
$asm=[Reflection.Assembly]::LoadFrom($app)
$tGA=$asm.GetType('RepRecorder.GameAudio')
$mSet=$tGA.GetMethod('SetSystemMute'); $mGet=$tGA.GetMethod('GetSystemMute')
function SysMuted(){ $a=@($null,$null); [void]$mGet.Invoke($null,$a); return [bool]$a[0] }
function SetSysMute($v){ $a=@([bool]$v,$null); [void]$mSet.Invoke($null,$a); return $a[1] }
function MeanVol($f){ $vd=(& $ffmpeg -hide_banner -i $f -af volumedetect -f null NUL 2>&1) -join "`n"; if($vd -match 'mean_volume:\s*([-\d.]+)'){return [double]$Matches[1]}; return 999 }

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== 验证「录制时自动静音」（$(Get-Date -Format 'HH:mm:ss')）====="
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
$bak1=(Get-Content -LiteralPath $ini1 -Raw -Encoding UTF8); $bak2=(Get-Content -LiteralPath $ini2 -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 3
SetIni $ini1 0; SetIni $ini2 0   # BGM 开，保证有稳定声源
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.SkipExisting=$false; $j.MuteGameForMyEars=$true; $j.ParallelCount=2
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Say "  设置：MuteGameForMyEars=true（自动静音）  ParallelCount=2  BGM=开"

try{
  # ---------- A. 单录 ----------
  Say ""
  Say "---------- A. 单录（1 个 rep）----------"
  $o1=[System.IO.Path]::ChangeExtension($short,'.mp4')
  if(Test-Path -LiteralPath $o1){ Remove-Item -LiteralPath $o1 -Force }
  Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
  Say ("  录制前：系统静音 = {0}，标记文件存在 = {1}" -f (SysMuted), (Test-Path -LiteralPath $flag))
  $pr = Start-Process -FilePath $app -ArgumentList ('"'+$short+'"') -PassThru
  Start-Sleep -Seconds 10
  $pr.Refresh(); $h=$pr.MainWindowHandle
  $boxes = Child-Check $h
  Say ("  界面上勾选框：{0}" -f ($boxes -join ' | '))
  Click-Btn $h "开始录制" | Out-Null
  $seenMuted=$false; $sw=[Diagnostics.Stopwatch]::StartNew()
  while($sw.Elapsed.TotalSeconds -lt 220){
    Start-Sleep -Seconds 3
    if(-not $seenMuted -and (SysMuted)){ $seenMuted=$true; Say ("  录制中检测到系统已被静音 ✓（开始后 {0:N0} 秒）" -f $sw.Elapsed.TotalSeconds) }
    if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } }
  }
  Start-Sleep 2
  Say ("  录制中确实静音过 = {0}" -f $seenMuted)
  Say ("  录完后：系统静音 = {0}（应为 False）、标记文件存在 = {1}（应为 False）" -f (SysMuted), (Test-Path -LiteralPath $flag))
  if(Test-Path -LiteralPath $o1){
    Say ("  成品音量 mean = {0:N1} dB（BGM 开的对照值约 -23.7 dB）" -f (MeanVol $o1))
  } else { Say "  ✗ 单录没出成品" }
  Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep 2

  # ---------- B. 并行 2 格 ----------
  Say ""
  Say "---------- B. 并行 2 格 ----------"
  $outs=@()
  foreach($r in @($short,$long)){ $o=[System.IO.Path]::ChangeExtension($r,'.mp4'); if(Test-Path -LiteralPath $o){ Remove-Item -LiteralPath $o -Force }; $outs += $o }
  Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
  $pr2 = Start-Process -FilePath $app -ArgumentList ('"'+$short+'"','"'+$long+'"') -PassThru
  Start-Sleep -Seconds 10
  $pr2.Refresh(); $h2=$pr2.MainWindowHandle
  Click-Btn $h2 "并行录制" | Out-Null
  $seenMuted2=$false; $sw2=[Diagnostics.Stopwatch]::StartNew()
  while($sw2.Elapsed.TotalSeconds -lt 300){
    Start-Sleep -Seconds 3
    if(-not $seenMuted2 -and (SysMuted)){ $seenMuted2=$true; Say ("  并行录制中检测到系统已被静音 ✓（开始后 {0:N0} 秒）" -f $sw2.Elapsed.TotalSeconds) }
    if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } }
  }
  Start-Sleep 2
  Say ("  并行中确实静音过 = {0}" -f $seenMuted2)
  Say ("  录完后：系统静音 = {0}（应为 False）、标记文件存在 = {1}（应为 False）" -f (SysMuted), (Test-Path -LiteralPath $flag))
  foreach($o in $outs){
    if(Test-Path -LiteralPath $o){ Say ("  第 {0} 格成品：{1:N1} MB  mean={2:N1} dB" -f ([array]::IndexOf($outs,$o)+1), ((Get-Item -LiteralPath $o).Length/1MB), (MeanVol $o)) }
    else { Say ("  第 {0} 格没有成品 ✗" -f ([array]::IndexOf($outs,$o)+1)) }
  }
  Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep 2

  # ---------- C. 被强制结束后的自动恢复 ----------
  Say ""
  Say "---------- C. 模拟「静音录制中被强杀」，下次启动应自动恢复声音 ----------"
  Say ("  手动静音 → {0}" -f (SetSysMute $true))
  [System.IO.File]::WriteAllText($flag, (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), (New-Object System.Text.UTF8Encoding($false)))
  Say ("  已造出残留标记文件；当前系统静音 = {0}" -f (SysMuted))
  Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
  $pr3 = Start-Process -FilePath $app -PassThru
  Start-Sleep -Seconds 12
  Say ("  启动后：系统静音 = {0}（应为 False）、标记文件还在 = {1}（应为 False）" -f (SysMuted), (Test-Path -LiteralPath $flag))
  if(Test-Path -LiteralPath $log){ Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '自动帮你把声音打开|没来得及恢复' | ForEach-Object { Say ("  日志| " + $_.Line) } }
  Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
} finally {
  Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  if(SysMuted){ SetSysMute $false | Out-Null }
  if(Test-Path -LiteralPath $flag){ Remove-Item -LiteralPath $flag -Force }
  [System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
  [System.IO.File]::WriteAllText($ini1,$bak1,(New-Object System.Text.UTF8Encoding($false)))
  [System.IO.File]::WriteAllText($ini2,$bak2,(New-Object System.Text.UTF8Encoding($false)))
  Say "  设置与 ini 已还原，声音已恢复"
}
Say "===== 结束（$(Get-Date -Format 'HH:mm:ss')）====="
