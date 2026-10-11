# 验证界面上的「录游戏声：开/关」按钮：关掉后录像必须无声，打开后必须有声。
# 全程用【真实点击界面按钮】驱动（不是直接改设置文件），确保按下去真的生效。
# 点击方式：向按钮窗口发 BM_CLICK（不依赖窗口是否在前台；物理鼠标点击在后台进程里不可靠）。
$root  = 'D:\【完整包】非想天则整合包2026-06-15'
$dir   = "$root\RepRecorder"
$log   = "$dir\RepRecorder.log"
$app   = "$dir\RepRecorder.exe"
$cfg   = "$dir\RepRecorder.settings.json"
$outlog= "$dir\验证-录游戏声按钮.log"
$rep   = "$root\【完整包】非想天则整合包2026-06-15\th123\replay\【观战Rep】\[Unstrung Doll ]_[阿虚reality]\26-09-02_13-47-16_Unstrung Doll (爱丽丝)_阿虚reality(咲夜)_(0︰0_无).rep"
$out   = [System.IO.Path]::ChangeExtension($rep, '.mp4')
$ffmpeg= 'C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
$ffprobe='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffprobe.exe'

function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RC {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
}
"@

# 找出文字匹配的按钮句柄
function Find-Btn($h,$like){
  $script:hit=[IntPtr]::Zero
  $cb=[RC+EP]{ param($ch,$l)
    if([RC]::Cls($ch) -like '*BUTTON*'){
      $t=[RC]::Txt($ch)
      if($t -and $t -like $like){ $script:hit=$ch; return $false }
    }
    return $true }
  [void][RC]::EnumChildWindows($h,$cb,[IntPtr]::Zero)
  return $script:hit
}
# 点一下按钮，返回点击后的文字
function Click-Btn($h,$like){
  $b = Find-Btn $h $like
  if($b -eq [IntPtr]::Zero){ Say "  ✗ 找不到按钮「$like」"; return '<没找到>' }
  $before = [RC]::Txt($b)
  [void][RC]::SendMessageW($b, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)   # BM_CLICK
  Start-Sleep -Seconds 2
  $after = [RC]::Txt((Find-Btn $h $like))
  Say ("  点击「{0}」→ 现在是「{1}」" -f $before, $after)
  return $after
}

# 等日志里出现某个模式（只看 skip 行之后的新内容）
function Wait-Log($pattern,$timeoutSec,$skip){
  $sw=[Diagnostics.Stopwatch]::StartNew()
  while($sw.Elapsed.TotalSeconds -lt $timeoutSec){
    Start-Sleep -Seconds 3
    if(Test-Path -LiteralPath $log){
      $lines = @(Get-Content -LiteralPath $log -Encoding UTF8)
      if($lines.Count -gt $skip){
        $tail = @($lines[$skip..($lines.Count-1)])
        $hit = @($tail | Select-String -Pattern $pattern)
        if($hit.Count -gt 0){ return [pscustomobject]@{ Hit=$hit; Tail=$tail } }
      }
    }
  }
  return [pscustomobject]@{ Hit=@(); Tail=@() }
}

function Show-Audio($f){
  if(-not (Test-Path -LiteralPath $f)){ Say "  ✗ 产物不存在：$f"; return }
  $size = (Get-Item -LiteralPath $f).Length
  Say ("  产物：{0}  {1:N2} MB" -f (Split-Path $f -Leaf), ($size/1MB))
  $streams = (& $ffprobe -v error -select_streams a -show_entries stream=index,codec_name,channels,sample_rate -of csv=p=0 $f 2>&1) -join ' | '
  if([string]::IsNullOrWhiteSpace($streams)){ Say "  音轨：没有音频流（视频完全无声）" } else { Say "  音轨：$streams" }
  $vd = (& $ffmpeg -hide_banner -i $f -af volumedetect -f null NUL 2>&1) -join "`n"
  $max = if($vd -match 'max_volume:\s*([-\d.]+) dB'){ $Matches[1] } else { '<未测到>' }
  $mean= if($vd -match 'mean_volume:\s*([-\d.]+) dB'){ $Matches[1] } else { '<未测到>' }
  Say "  电平：max_volume = $max dB ；mean_volume = $mean dB   （≈ -91 dB 或 -inf = 真无声）"
}

function Run-One($clickTarget,$tag){
  Say ""
  Say "########## $tag ##########"
  Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Seconds 3
  if(Test-Path -LiteralPath $out){ Remove-Item -LiteralPath $out -Force }
  Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue

  $pr = Start-Process -FilePath $app -ArgumentList ('"'+$rep+'"') -PassThru
  Start-Sleep -Seconds 9
  $pr.Refresh(); $h=$pr.MainWindowHandle
  if($h -eq [IntPtr]::Zero){ Say "  ✗ 主窗口没出现"; return }
  Say ("  当前按钮：{0}" -f [RC]::Txt((Find-Btn $h '录游戏声*')))
  if($clickTarget){ [void](Click-Btn $h $clickTarget) }

  $skip = 0
  if(Test-Path -LiteralPath $log){ $skip = @(Get-Content -LiteralPath $log -Encoding UTF8).Count }
  [void](Click-Btn $h "开始录制")
  $sw=[Diagnostics.Stopwatch]::StartNew()
  $r = Wait-Log '队列结束|拿不到录像文件|录制出错' 300 $skip
  Say ("  录制用时 {0:N0} 秒" -f $sw.Elapsed.TotalSeconds)
  $r.Tail | Select-String -Pattern '录游戏声|游戏声音录制|音频自检|已静音 OBS 桌面音频|开始录像|完成|失败' | ForEach-Object { Say ("  日志| " + $_.Line) }
  Show-Audio $out
  Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Seconds 2
}

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== 验证「录游戏声」按钮（$(Get-Date -Format 'HH:mm:ss')）====="
Say "（用 BM_CLICK 直接点按钮；每段录完用 ffmpeg volumedetect 判定有没有声音）"

# 测试期间关掉"问保存位置"，避免弹窗
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation = $false
if($j.PSObject.Properties.Name -notcontains 'RecordGameAudio'){ $j | Add-Member -NotePropertyName RecordGameAudio -NotePropertyValue $true }
$j.RecordGameAudio = $true
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8

Run-One "录游戏声：开 ✓" "A：把按钮切成「关 ✕」后录制（期望：视频无声）"
Run-One "录游戏声：关 ✕" "B：把按钮切回「开 ✓」后录制（期望：视频有游戏声音）"

$j2 = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j2.AskSaveLocation = $true
$j2 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
Say ""
Say "===== 验证结束（$(Get-Date -Format 'HH:mm:ss')）====="
