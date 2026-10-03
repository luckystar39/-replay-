# 同一条 rep 的「录游戏声 关 / 开」对照实验：各抽一帧看画面，并测音频电平。
# 目的：确认「关」那段不只是没声音，画面本身也是正常的游戏实录。
$root  = 'D:\【完整包】非想天则整合包2026-06-15'
$dir   = "$root\RepRecorder"
$log   = "$dir\RepRecorder.log"
$app   = "$dir\RepRecorder.exe"
$cfg   = "$dir\RepRecorder.settings.json"
$outlog= "$dir\验证-录游戏声对比.log"
$rep   = "$root\【完整包】非想天则整合包2026-06-15\th123\replay\【观战Rep】\[Unstrung Doll ]_[阿虚reality]\26-09-02_13-47-16_Unstrung Doll (爱丽丝)_阿虚reality(咲夜)_(0︰0_无).rep"
$out   = [System.IO.Path]::ChangeExtension($rep, '.mp4')
$ffmpeg= 'C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
$ffprobe='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffprobe.exe'

function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RC3 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
}
"@
function Find-Btn($h,$like){
  $script:hit=[IntPtr]::Zero
  $cb=[RC3+EP]{ param($ch,$l)
    if([RC3]::Cls($ch) -like '*BUTTON*'){
      $t=[RC3]::Txt($ch)
      if($t -and $t -like $like){ $script:hit=$ch; return $false }
    }
    return $true }
  [void][RC3]::EnumChildWindows($h,$cb,[IntPtr]::Zero)
  return $script:hit
}
function Click-Btn($h,$like){
  $b = Find-Btn $h $like
  if($b -eq [IntPtr]::Zero){ Say "  ✗ 找不到「$like」"; return $false }
  Say ("  点按钮「{0}」" -f [RC3]::Txt($b))
  [void][RC3]::SendMessageW($b, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
  Start-Sleep -Seconds 2
  return $true
}
function Wait-Log($pattern,$timeoutSec,$skip){
  $sw=[Diagnostics.Stopwatch]::StartNew()
  while($sw.Elapsed.TotalSeconds -lt $timeoutSec){
    Start-Sleep -Seconds 3
    if(Test-Path -LiteralPath $log){
      $lines = @(Get-Content -LiteralPath $log -Encoding UTF8)
      if($lines.Count -gt $skip){
        $tail = @($lines[$skip..($lines.Count-1)])
        if(@($tail | Select-String -Pattern $pattern).Count -gt 0){ return $tail }
      }
    }
  }
  return @()
}

function Run-Phase($gameAudio,$tag,$logCopy,$jpg,$dur){
  Say ""
  Say "########## $tag ##########"
  Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Seconds 3
  if(Test-Path -LiteralPath $out){ Remove-Item -LiteralPath $out -Force }
  $j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
  $j.RecordGameAudio = $gameAudio
  $j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8

  # 应用日志不清空：用行号定位本次新增的内容
  if(-not (Test-Path -LiteralPath $log)){ New-Item -ItemType File -Path $log | Out-Null }
  $skip = @(Get-Content -LiteralPath $log -Encoding UTF8).Count

  $pr = Start-Process -FilePath $app -ArgumentList ('"'+$rep+'"') -PassThru
  Start-Sleep -Seconds 9
  $pr.Refresh(); $h=$pr.MainWindowHandle
  if($h -eq [IntPtr]::Zero){ Say "  ✗ 主窗口没出现"; return }
  Say ("  按钮当前：{0}" -f [RC3]::Txt((Find-Btn $h '录游戏声*')))
  Click-Btn $h "开始录制" | Out-Null
  $tail = Wait-Log '队列结束|拿不到录像文件|录制出错' 300 $skip
  $tail | Select-String -Pattern '游戏声音录制|音频自检|开始录像|完成|采集|黑屏|捕获|方法|静止|窗口' | ForEach-Object { Say ("  日志| " + $_.Line) }
  $tail | Set-Content -LiteralPath (Join-Path $dir $logCopy) -Encoding UTF8

  if(Test-Path -LiteralPath $out){
    $info = (& $ffprobe -v error -show_entries format=duration,bit_rate -of csv=p=0 $out 2>&1) -join ''
    Say ("  产物：{0:N2} MB   {1}   （每秒 {2:N2} MB）" -f ((Get-Item -LiteralPath $out).Length/1MB), $info, ((Get-Item -LiteralPath $out).Length/1MB/[double](& $ffprobe -v error -show_entries format=duration -of csv=p=0 $out 2>&1)))
    $vd = (& $ffmpeg -hide_banner -i $out -af volumedetect -f null NUL 2>&1) -join "`n"
    $max = if($vd -match 'max_volume:\s*([-\d.]+) dB'){ $Matches[1] } else { '<未测到>' }
    Say "  音频 max_volume = $max dB"
    # 抽 3 帧看画面是否在动
    foreach($t in @(5,20,35)){
      $f = Join-Path $dir ("{0}-{1}s.jpg" -f $jpg,$t)
      & $ffmpeg -hide_banner -y -ss $t -i $out -frames:v 1 -q:v 3 $f 2>&1 | Out-Null
      if(Test-Path -LiteralPath $f){ Say ("  抽帧 {0}s → {1}  ({2:N0} KB)" -f $t, (Split-Path $f -Leaf), ((Get-Item -LiteralPath $f).Length/1KB)) }
    }
    # 整段画面变化量（场景切换数）——静止/黑屏会接近 0
    $sc = (& $ffmpeg -hide_banner -i $out -vf "select='gt(scene,0.02)',showinfo" -f null NUL 2>&1) -join "`n"
    $n = ([regex]::Matches($sc, 'n:\s*\d+')).Count
    Say "  画面变化明显的帧数（阈值0.02）：$n"
  } else { Say "  ✗ 没有产物" }
  Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Seconds 2
}

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== 「录游戏声 关/开」对照（$(Get-Date -Format 'HH:mm:ss')）====="
$j0 = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j0.AskSaveLocation = $false
$j0 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8

Run-Phase $false "A：录游戏声=关" "样本-关.log" "样本-关" 0
Run-Phase $true  "B：录游戏声=开" "样本-开.log" "样本-开" 0

$j2 = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j2.AskSaveLocation = $true
$j2.RecordGameAudio = $true
$j2 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
Say ""
Say "===== 对照结束（$(Get-Date -Format 'HH:mm:ss')）====="
