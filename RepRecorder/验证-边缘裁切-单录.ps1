# 验证边缘裁切：并行 3 格 + 单录 1 条，然后用列平均看最外圈还有没有"断崖"
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$app="$dir\RepRecorder.exe"; $cfg="$dir\RepRecorder.settings.json"; $log="$dir\RepRecorder.log"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
function Say($m){ Write-Host $m }
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RB14 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static bool ClickBtn(IntPtr root, string like){
    IntPtr hit=IntPtr.Zero;
    EnumChildWindows(root, delegate(IntPtr c, IntPtr l){
      if(Cls(c).IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase)>=0){
        string t=Txt(c);
        if(!string.IsNullOrEmpty(t) && t.Contains(like)){ hit=c; return false; }
      }
      return true;
    }, IntPtr.Zero);
    if(hit==IntPtr.Zero) return false;
    SendMessageW(hit,0x00F5,IntPtr.Zero,IntPtr.Zero);
    return true;
  }
}
"@
function WaitQ($sec){ $sw=[Diagnostics.Stopwatch]::StartNew()
  while($sw.Elapsed.TotalSeconds -lt $sec){ Start-Sleep -Seconds 3
    if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } } }
  return $sw.Elapsed.TotalSeconds }
function Prof($f, $tag){
  $tmp="$env:TEMP\profv"; New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  $raw="$tmp\$tag.raw"
  & $ffmpeg -hide_banner -loglevel error -y -t 40 -i "$f" -vf "fps=1,scale=1440:1" -f rawvideo -pix_fmt gray "$raw" 2>&1 | Out-Null
  $b=[System.IO.File]::ReadAllBytes($raw); $frames=[int]($b.Length/1440)
  if($frames -lt 5){ Say "  [$tag] 帧太少，跳过"; return }
  $avg=New-Object 'double[]' 1440
  for($i=0;$i -lt $frames;$i++){ for($x=0;$x -lt 1440;$x++){ $avg[$x]+=$b[$i*1440+$x] } }
  for($x=0;$x -lt 1440;$x++){ $avg[$x]=$avg[$x]/$frames }
  $maxJump=0.0; $at=0
  for($x=1;$x -lt 1440;$x++){ $d=[math]::Abs($avg[$x]-$avg[$x-1]); if($d -gt $maxJump){ $maxJump=$d; $at=$x } }
  Say ("  [{0}] 最左6列 {1} | 最右6列 {2}" -f $tag, (($avg[0..5] | ForEach-Object { [math]::Round($_,1) }) -join ','), (($avg[1434..1439] | ForEach-Object { [math]::Round($_,1) }) -join ','))
  Say ("  [{0}] 全图最大逐列跳变 = {1:N1}（在 x={2}）；中间参照 {3}" -f $tag, $maxJump, $at, (($avg[700..705] | ForEach-Object { [math]::Round($_,1) }) -join ','))
  Say ("  [{0}] → {1}" -f $tag, $(if($maxJump -le 6){'✅ 没有断崖（原来 15.0）'}else{'❌ 边缘仍有明显跳变'}))
}
$reps=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter '26-08-30_15-55-29_Aquila(3)*.rep' | Select-Object -First 2
$par=$reps | Select-Object -First 1
$one=$reps | Select-Object -First 1
Say "并行用： $($par.Name -join ' / ')"
Say "单录用： $($one.Name)"
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 3
$j=Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.SkipExisting=$false; $j.MuteGameForMyEars=$false; $j.ParallelCount=1
$j | Add-Member -NotePropertyName ParallelEdgeInset -NotePropertyValue 6 -Force
$j | Add-Member -NotePropertyName ShowScoreboard -NotePropertyValue $true -Force
$j | Add-Member -NotePropertyName SettingsVersion -NotePropertyValue 2 -Force
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
# ---- 并行 3 格 ----
$args=@(); foreach($r in $par){ $args += ('"'+$r.FullName+'"') }
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$pr=Start-Process -FilePath $app -ArgumentList $args -PassThru
Start-Sleep -Seconds 11
$pr.Refresh(); Say ("  点开始录制 = " + [RB14]::ClickBtn($pr.MainWindowHandle,'开始录制'))
Say ("  并行录了 {0:N0} 秒" -f (WaitQ 480))
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '往里裁|并行切分完成' | ForEach-Object { Say ("  日志| " + $_.Line) }
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$outs=@(); foreach($r in $par){ $o=[System.IO.Path]::ChangeExtension($r.FullName,'.mp4'); if(Test-Path -LiteralPath $o){ $outs += $o } }
foreach($o in $outs){ $st=(& $ffmpeg -hide_banner -i "$o" 2>&1 | Select-String 'Stream #').Line; Say ("  成品 " + (Split-Path $o -Leaf) + " → " + (($st -join ' | ') -replace '\s+',' ')) }
if($outs.Count -gt 0){ Prof $outs[0] '并行第1格' }
# ---- 单录 1 条 ----
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$pr2=Start-Process -FilePath $app -ArgumentList ('"'+$one.FullName+'"') -PassThru
Start-Sleep -Seconds 11
$pr2.Refresh(); Say ("  单录点开始录制 = " + [RB14]::ClickBtn($pr2.MainWindowHandle,'开始录制'))
Say ("  单录录了 {0:N0} 秒" -f (WaitQ 300))
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '适配|完成：' | ForEach-Object { Say ("  日志| " + $_.Line) }
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$o1=[System.IO.Path]::ChangeExtension($one.FullName,'.mp4')
if(Test-Path -LiteralPath $o1){ Prof $o1 '单录' } else { Say "  单录没出成品 ✗" }
[System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
Say "设置已还原"