# 并行录制实测（2 个 rep）
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"
$app="$dir\RepRecorder.exe"
$repdir="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ff="$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe"

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class CF {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public struct RECT { public int L, T, R, B; }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
}
"@
[void][CF]::SetProcessDPIAware()
function Click-Btn($h,$like){
  $script:hit=[IntPtr]::Zero
  $cb=[CF+EP]{ param($ch,$l)
    $t=[CF]::Txt($ch)
    if($t -and $t -like $like){ $script:hit=$ch; return $false }
    return $true }
  [void][CF]::EnumChildWindows($h,$cb,[IntPtr]::Zero)
  if($script:hit -eq [IntPtr]::Zero){ Write-Host "  没找到「$like」" -ForegroundColor Yellow; return }
  $r=New-Object CF+RECT; [void][CF]::GetWindowRect($script:hit,[ref]$r)
  [void][CF]::SetCursorPos([int](($r.L+$r.R)/2),[int](($r.T+$r.B)/2)); Start-Sleep -Milliseconds 200
  [CF]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 80
  [CF]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  Write-Host ("  已点击「" + [CF]::Txt($script:hit) + "」") -ForegroundColor Green
}

Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
if(Get-Process obs64 -ErrorAction SilentlyContinue){ Start-Process -FilePath $app -ArgumentList @('--stop-record') -Wait }

# 挑 2 个短 rep（<12KB），删掉已存在的同名 mp4
$cands = Get-ChildItem -LiteralPath $repdir -Recurse -File -Filter *.rep |
  Where-Object { $_.Length -ge 2000 -and $_.Length -le 12000 -and $_.FullName -notmatch '[!%&]' } |
  Sort-Object Length
$picked=@()
foreach($c in $cands){
  $mp4=[System.IO.Path]::ChangeExtension($c.FullName,'.mp4')
  if(Test-Path -LiteralPath $mp4){ Remove-Item -LiteralPath $mp4 -Force }
  $picked += $c.FullName
  if($picked.Count -ge 2){ break }
}
Write-Host "选中的 2 个 rep：" -ForegroundColor Cyan
$picked | ForEach-Object { Write-Host ("  " + (Split-Path $_ -Leaf)) }

Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$args=@()
foreach($r in $picked){ $args += ('"'+$r+'"') }
$pr=Start-Process -FilePath $app -ArgumentList $args -PassThru
Start-Sleep -Seconds 8
$pr.Refresh(); $h=$pr.MainWindowHandle
[void][CF]::SetForegroundWindow($h); Start-Sleep -Milliseconds 700
Write-Host "点击「并行录制×2」：" -ForegroundColor Cyan
Click-Btn $h "并行录制*"

for($i=0;$i -lt 120;$i++){
  Start-Sleep -Seconds 3
  if(Test-Path $log){
    if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '并行录制结束'){ break }
    if($i % 8 -eq 0){ Write-Host ("    ...{0}s" -f ($i*3)) -ForegroundColor DarkGray }
  }
}
Write-Host "==== 等切分完成 ====" -ForegroundColor Cyan
for($k=0;$k -lt 40;$k++){
  Start-Sleep -Seconds 3
  $got=0
  foreach($r in $picked){ if(Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($r,'.mp4'))){ $got++ } }
  if($got -ge $picked.Count){ Write-Host ("  2 个产物都出现了（约 {0}s）" -f ($k*3)) -ForegroundColor Green; break }
}
Write-Host "==== 日志关键行 ====" -ForegroundColor Cyan
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String -Pattern '并行|切分|静止|画布|还原|失败|出错' | ForEach-Object { "  " + $_.Line }
Write-Host "==== 产物 + 抽帧 ====" -ForegroundColor Cyan
$i=1
foreach($r in $picked){
  $mp4=[System.IO.Path]::ChangeExtension($r,'.mp4')
  if(Test-Path -LiteralPath $mp4){
    Write-Host ("  {0:N1} MB  {1}" -f ((Get-Item -LiteralPath $mp4).Length/1MB), (Split-Path $mp4 -Leaf))
    & $ff -hide_banner -v error -y -ss 25 -i "$mp4" -frames:v 1 "$dir\pframe$i.png" 2>&1 | Select-Object -First 2
  } else { Write-Host "  没有产物" }
  $i++
}
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
$cfg="$dir\RepRecorder.settings.json"
$j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$true
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
