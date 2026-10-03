# 并行录制端到端实测：3 个 rep 同时录 → 切分成 3 个带各自声音的视频
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"
$app="$dir\RepRecorder.exe"
$cfg="$dir\RepRecorder.settings.json"
$repdir="$root\【完整包】非想天则整合包2026-06-15\th123\replay"

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class CE {
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
[void][CE]::SetProcessDPIAware()
function Click-Btn($h,$like){
  $script:hit=[IntPtr]::Zero
  $cb=[CE+EP]{ param($ch,$l)
    $t=[CE]::Txt($ch)
    if($t -and $t -like $like){ $script:hit=$ch; return $false }
    return $true }
  [void][CE]::EnumChildWindows($h,$cb,[IntPtr]::Zero)
  if($script:hit -eq [IntPtr]::Zero){ Write-Host "  没找到「$like」" -ForegroundColor Yellow; return }
  $r=New-Object CE+RECT; [void][CE]::GetWindowRect($script:hit,[ref]$r)
  [void][CE]::SetCursorPos([int](($r.L+$r.R)/2),[int](($r.T+$r.B)/2)); Start-Sleep -Milliseconds 200
  [CE]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 80
  [CE]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero)
  Write-Host ("  已点击「" + [CE]::Txt($script:hit) + "」") -ForegroundColor Green
}

Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
if(Get-Process obs64 -ErrorAction SilentlyContinue){ Start-Process -FilePath $app -ArgumentList @('--stop-record') -Wait }

# 挑 3 个短 rep（5KB 以内），把已存在的同名 mp4 删掉，保证会被录
$cands = Get-ChildItem -LiteralPath $repdir -Recurse -File -Filter *.rep |
  Where-Object { $_.Length -ge 1000 -and $_.Length -le 12000 -and $_.FullName -notmatch '[!%&]' } |
  Sort-Object Length | Select-Object -First 6
$picked=@()
foreach($c in $cands){
  $mp4=[System.IO.Path]::ChangeExtension($c.FullName,'.mp4')
  if(Test-Path -LiteralPath $mp4){ Remove-Item -LiteralPath $mp4 -Force }
  $picked += $c.FullName
  if($picked.Count -ge 3){ break }
}
Write-Host "选中的 3 个 rep：" -ForegroundColor Cyan
$picked | ForEach-Object { Write-Host ("  " + (Split-Path $_ -Leaf)) }

Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$args = @()
foreach($r in $picked){ $args += ('"'+$r+'"') }
$pr=Start-Process -FilePath $app -ArgumentList $args -PassThru
Start-Sleep -Seconds 8
$pr.Refresh(); $h=$pr.MainWindowHandle
[void][CE]::SetForegroundWindow($h); Start-Sleep -Milliseconds 700
Write-Host "点击「并行录制×3」：" -ForegroundColor Cyan
Click-Btn $h "并行录制*"

for($i=0;$i -lt 120;$i++){
  Start-Sleep -Seconds 3
  if(Test-Path $log){
    $t=Get-Content -LiteralPath $log -Raw -Encoding UTF8
    if($t -match '并行录制结束'){ break }
    if($i % 5 -eq 0){ Write-Host ("    ...{0}s" -f ($i*3)) -ForegroundColor DarkGray }
  }
}
Write-Host "==== 等切分完成（最多 120 秒） ====" -ForegroundColor Cyan
for($k=0;$k -lt 40;$k++){
  Start-Sleep -Seconds 3
  $got=0
  foreach($r in $picked){ if(Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($r,'.mp4'))){ $got++ } }
  if($got -ge $picked.Count){ Write-Host ("  三个产物都出现了（用时约 {0}s）" -f ($k*3)) -ForegroundColor Green; break }
  if($k % 4 -eq 0){ Write-Host ("    ...{0}s  已出现 {1}/{2}" -f ($k*3), $got, $picked.Count) -ForegroundColor DarkGray }
}
Write-Host "==== 并行录制日志 ====" -ForegroundColor Cyan
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String -Pattern '并行|切分|合并|画布|轨道|出错|失败' | ForEach-Object { "  " + $_.Line }
Write-Host "==== 产物 ====" -ForegroundColor Cyan
foreach($r in $picked){
  $mp4=[System.IO.Path]::ChangeExtension($r,'.mp4')
  if(Test-Path -LiteralPath $mp4){ Write-Host ("  {0:N1} MB  {1}" -f ((Get-Item -LiteralPath $mp4).Length/1MB), (Split-Path $mp4 -Leaf)) }
  else { Write-Host ("  没有产物  " + (Split-Path $r -Leaf)) }
}
Write-Host "==== OBS 日志（音轨/多轨是否生效） ====" -ForegroundColor Cyan
$ol=Get-ChildItem "$env:APPDATA\obs-studio\logs" -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Get-Content -LiteralPath $ol.FullName -Encoding UTF8 | Select-String -Pattern 'Track|track|Hybrid|Recording Start|Recording Stop|nvenc|aac' | Select-Object -Last 12 | ForEach-Object { "  " + $_.Line }
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
