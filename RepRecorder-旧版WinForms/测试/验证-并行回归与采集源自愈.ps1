# 回归：并行路径（头尾黑屏 + 用时）+ 「采集源被隐藏后单录能自愈」
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"; $app="$dir\RepRecorder.exe"; $cfg="$dir\RepRecorder.settings.json"
$outlog="$dir\验证-并行回归与采集源自愈.log"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
$ffprobe='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffprobe.exe'
function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }
function Find-Rep($leaf){ $f=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter $leaf -ErrorAction SilentlyContinue | Select-Object -First 1; if($f){return $f.FullName}; return $null }
$short = Find-Rep '26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep'
$long  = Find-Rep '26-09-17_18-11-44_Ciel(红美铃)_诺诺真好玩喵(天子)_(2︰0_胜).rep'
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RB9 {
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
  [void][RB9]::EnumChildWindows($h,[RB9+EP]{ param($c,$l) if([RB9]::Cls($c) -like '*BUTTON*'){ $t=[RB9]::Txt($c); if($t -and $t -like $like){ $script:hit=$c; return $false } }; return $true },[IntPtr]::Zero)
  if($script:hit -eq [IntPtr]::Zero){ Say "  ✗ 找不到「$like」"; return $false }
  Say ("  点按钮「{0}」" -f [RB9]::Txt($script:hit))
  [void][RB9]::SendMessageW($script:hit,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero); Start-Sleep -Seconds 2; return $true }
function Blacks($f,$mode){
  $out = if($mode -eq 'head'){ & $ffmpeg -hide_banner -nostats -t 20 -i $f -an -vf blackdetect=d=0.15:pix_th=0.10 -f null NUL 2>&1 }
         else { & $ffmpeg -hide_banner -nostats -sseof -15 -i $f -an -vf blackdetect=d=0.15:pix_th=0.10 -f null NUL 2>&1 }
  $lines = ($out | Select-String 'black_start').Line
  if(-not $lines){ return "无 ≥0.15 秒黑段 ✓" }
  return (($lines | ForEach-Object { ($_ -replace '.*blackdetect[^]]*\]\s*','') }) -join ' ; ')
}
function Dur($f){ (([double]((& $ffprobe -v error -show_entries format=duration -of csv=p=0 -i $f 2>&1) -join ''))) }
function Wait-Queue($sec){ $sw=[Diagnostics.Stopwatch]::StartNew()
  while($sw.Elapsed.TotalSeconds -lt $sec){ Start-Sleep -Seconds 3
    if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } } }
  return $sw.Elapsed.TotalSeconds }

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== 回归（$(Get-Date -Format 'HH:mm:ss')）====="
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 3
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.SkipExisting=$false; $j.MuteGameForMyEars=$false; $j.ParallelCount=2
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8

try{
  Say ""
  Say "---------- A. 并行 2 格（回归）----------"
  $outs=@()
  foreach($r in @($short,$long)){ $o=[System.IO.Path]::ChangeExtension($r,'.mp4'); if(Test-Path -LiteralPath $o){ Remove-Item -LiteralPath $o -Force }; $outs += $o }
  Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
  $pr = Start-Process -FilePath $app -ArgumentList ('"'+$short+'"','"'+$long+'"') -PassThru
  Start-Sleep -Seconds 10
  $pr.Refresh(); Click-Btn $pr.MainWindowHandle "并行录制*" | Out-Null
  Say ("  录了 {0:N0} 秒" -f (Wait-Queue 420))
  Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '并行切分（第|并行切分完成' | ForEach-Object { Say ("  日志| " + $_.Line) }
  $k=1
  foreach($o in $outs){
    if(Test-Path -LiteralPath $o){
      Say ("  第 {0} 格：{1:N1} MB  {2:N1}s" -f $k,((Get-Item -LiteralPath $o).Length/1MB),(Dur $o))
      Say ("     开头：{0}" -f (Blacks $o 'head'))
      Say ("     结尾：{0}" -f (Blacks $o 'tail'))
    } else { Say ("  第 {0} 格没有成品 ✗" -f $k) }
    $k++
  }

  Say ""
  Say "---------- B. 并行中途强杀 → 采集源被留在「隐藏」状态 → 再单录应能自愈 ----------"
  foreach($r in @($short,$long)){ $o=[System.IO.Path]::ChangeExtension($r,'.mp4'); if(Test-Path -LiteralPath $o){ Remove-Item -LiteralPath $o -Force } }
  Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
  $pr2 = Start-Process -FilePath $app -ArgumentList ('"'+$short+'"','"'+$long+'"') -PassThru
  Start-Sleep -Seconds 10
  $pr2.Refresh(); Click-Btn $pr2.MainWindowHandle "并行录制*" | Out-Null
  Say "  等 35 秒（此刻单文件采集源已被隐藏），然后强杀程序模拟崩溃…"
  Start-Sleep -Seconds 35
  Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
  Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep 3
  Say "  已强杀（没走还原流程，采集源应仍是隐藏的）"

  # 再单录
  $o1=[System.IO.Path]::ChangeExtension($short,'.mp4')
  if(Test-Path -LiteralPath $o1){ Remove-Item -LiteralPath $o1 -Force }
  Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
  $pr3 = Start-Process -FilePath $app -ArgumentList ('"'+$short+'"') -PassThru
  Start-Sleep -Seconds 10
  $pr3.Refresh(); Click-Btn $pr3.MainWindowHandle "开始录制" | Out-Null
  Say ("  单录 {0:N0} 秒" -f (Wait-Queue 260))
  Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '隐藏|重新显示|采集画面正常|采集画面全黑|完成：' | ForEach-Object { Say ("  日志| " + $_.Line) }
  if(Test-Path -LiteralPath $o1){
    $sz=(Get-Item -LiteralPath $o1).Length/1MB
    Say ("  单录成品：{0:N1} MB（全黑的话只有 ~5 MB，正常约 80 MB）→ {1}" -f $sz, $(if($sz -gt 30){'✅ 有画面'}else{'❌ 疑似全黑'}))
  } else { Say "  ✗ 没有成品" }
} finally {
  Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  [System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
  Say "  设置已还原"
}
Say "===== 结束（$(Get-Date -Format 'HH:mm:ss')）====="
