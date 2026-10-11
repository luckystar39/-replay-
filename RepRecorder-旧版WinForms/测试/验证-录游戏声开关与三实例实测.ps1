# Part A: 验证「录游戏声」开关（关掉后录像应无声）
# Part B: 并行录制可行性实测：1 个实例 vs 3 个实例的内存/CPU/GPU 占用
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"
$app="$dir\RepRecorder.exe"
$cfg="$dir\RepRecorder.settings.json"
$gamedir="$root\【完整包】非想天则整合包2026-06-15\th123"
$repdir="$gamedir\replay\【联机对战Rep】"

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class CD {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint fl);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public struct RECT { public int L, T, R, B; }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
}
"@
[void][CD]::SetProcessDPIAware()
function Click-Btn($h,$like){
  $script:hit=[IntPtr]::Zero
  $cb=[CD+EP]{ param($ch,$l)
    $t=[CD]::Txt($ch)
    if($t -and $t -like $like){ $script:hit=$ch; return $false }
    return $true }
  [void][CD]::EnumChildWindows($h,$cb,[IntPtr]::Zero)
  if($script:hit -eq [IntPtr]::Zero){ Write-Host "  没找到「$like」" -ForegroundColor Yellow; return }
  $r=New-Object CD+RECT; [void][CD]::GetWindowRect($script:hit,[ref]$r)
  [void][CD]::SetCursorPos([int](($r.L+$r.R)/2),[int](($r.T+$r.B)/2)); Start-Sleep -Milliseconds 200
  [CD]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 80
  [CD]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  Write-Host ("  已点击「" + [CD]::Txt($script:hit) + "」") -ForegroundColor Green
}

Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
$j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false
if($j.PSObject.Properties.Name -contains 'RecordGameAudio'){ $j.RecordGameAudio=$true } else { $j | Add-Member -NotePropertyName RecordGameAudio -NotePropertyValue $true }
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8

Write-Host "`n########## Part A：验证「录游戏声」开关 ##########" -ForegroundColor Magenta
$repA="$repdir\[型WEI]\26-09-13_21-38-44_型WEI(灵梦)_Ciel(红美铃)_(0︰0_无).rep"
$outA=[System.IO.Path]::ChangeExtension($repA,'.mp4')
Remove-Item -LiteralPath $outA -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$pr=Start-Process -FilePath $app -ArgumentList ('"'+$repA+'"') -PassThru
Start-Sleep -Seconds 7
$pr.Refresh(); $h=$pr.MainWindowHandle
[void][CD]::SetForegroundWindow($h); Start-Sleep -Milliseconds 600
Write-Host "1) 点一下「录游戏声：开 ✓」把它关掉："
Click-Btn $h "录游戏声：开 ✓"
Start-Sleep -Seconds 2
Write-Host "2) 点开始录制："
Click-Btn $h "开始录制"
for($i=0;$i -lt 60;$i++){
  Start-Sleep -Seconds 3
  if(Test-Path $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } }
}
Write-Host "---- 日志关键行 ----"
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String -Pattern '录游戏声|游戏声音录制|音频自检|完成|开始录像' | ForEach-Object { "  " + $_.Line }
if(Test-Path -LiteralPath $outA){ Write-Host ("  产物: {0:N1} MB" -f ((Get-Item -LiteralPath $outA).Length/1MB)) }
Write-Host "3) 再点一下把它打开（应为未静音）："
[void][CD]::SetForegroundWindow($h); Start-Sleep -Milliseconds 400
Click-Btn $h "录游戏声：关 ✕"
Start-Sleep -Seconds 2
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String -Pattern '录游戏声|游戏声音录制' | Select-Object -Last 2 | ForEach-Object { "  " + $_.Line }
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3

Write-Host "`n########## Part B：并行实例占用实测 ##########" -ForegroundColor Magenta
$rep1="$repdir\[型WEI]\26-09-13_21-38-44_型WEI(灵梦)_Ciel(红美铃)_(0︰0_无).rep"
$rep2="$repdir\[Aquila(3) ]\26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep"
$rep3="$repdir\..\【观战Rep】\[Unstrung Doll ]_[阿虚reality]\26-09-02_13-47-16_Unstrung Doll (爱丽丝)_阿虚reality(咲夜)_(0︰0_无).rep"
foreach($r in @($rep1,$rep2,$rep3)){ Write-Host ("  rep 存在={0}  {1}" -f (Test-Path -LiteralPath $r), (Split-Path $r -Leaf)) }

function Measure-One($tag){
  $ps=@(Get-Process th123 -ErrorAction SilentlyContinue)
  if($ps.Count -eq 0){ Write-Host "  [$tag] 没有游戏进程"; return }
  $ws=($ps | Measure-Object WorkingSet64 -Sum)
  $cpu1=($ps | Measure-Object CPU -Sum).Sum; $t1=Get-Date
  Start-Sleep -Seconds 5
  $ps2=@(Get-Process th123 -ErrorAction SilentlyContinue)
  $cpu2=($ps2 | Measure-Object CPU -Sum).Sum; $t2=Get-Date
  $pct=($cpu2-$cpu1)/(($t2-$t1).TotalSeconds*[Environment]::ProcessorCount)*100
  $gpu=(& nvidia-smi --query-gpu=utilization.gpu,memory.used --format=csv,noheader) 2>$null
  Write-Host ("  [{0}] 实例数={1}  内存合计={2:N0} MB   每实例: {3}   CPU占用≈{4:N1}%    GPU: {5}" -f `
    $tag, $ps.Count, ($ws.Sum/1MB), (($ps | ForEach-Object { "{0:N0}MB" -f ($_.WorkingSet64/1MB) }) -join ' + '), $pct, $gpu)
}

Write-Host "---- 基线：单个实例 ----"
$p1=Start-Process -FilePath "$gamedir\th123.exe" -ArgumentList ('"'+$rep1+'"') -WorkingDirectory $gamedir -PassThru
Start-Sleep -Seconds 12
Measure-One "1 个"
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3

Write-Host "---- 三实例并行 ----"
$procs=@()
$i=0
foreach($r in @($rep1,$rep2,$rep3)){
  $procs += Start-Process -FilePath "$gamedir\th123.exe" -ArgumentList ('"'+$r+'"') -WorkingDirectory $gamedir -PassThru
  Start-Sleep -Milliseconds 500
  $i++
}
Start-Sleep -Seconds 12
foreach($pp in $procs){
  try{
    $pp.Refresh()
    if($pp.MainWindowHandle -ne [IntPtr]::Zero){
      [void][CD]::SetWindowPos($pp.MainWindowHandle,[IntPtr]::Zero,(($i-1)*0),0,640,480,0x0040)
    }
  }catch{}
}
Measure-One "3 个 (t=12s)"
Start-Sleep -Seconds 10
Measure-One "3 个 (t=22s)"
Write-Host "---- 等它们自己播完 ----"
$sw=[System.Diagnostics.Stopwatch]::StartNew()
while((Get-Process th123 -ErrorAction SilentlyContinue) -and $sw.Elapsed.TotalSeconds -lt 120){ Start-Sleep -Seconds 3 }
Write-Host ("  三个实例全部结束用时 {0:N0} 秒（单个 rep 正常约 30 秒）" -f $sw.Elapsed.TotalSeconds)
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
$j2=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j2.AskSaveLocation=$true
$j2 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
