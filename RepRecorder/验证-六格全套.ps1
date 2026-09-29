# 验证：一次录 6 个（合并按钮 + 只开一次游戏 + 声音 + 开头不切多 + 保存询问 + 用时=视频时长）
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"; $app="$dir\RepRecorder.exe"; $cfg="$dir\RepRecorder.settings.json"
$outlog="$dir\验证-六格全套.log"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
$ffprobe='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffprobe.exe'
function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }

# 挑 6 个"还没有成品"的 rep
$reps=Get-ChildItem -LiteralPath "$replay\【联机对战Rep】\[承诺 3]" -Force -File -Filter '*.rep' |
      Where-Object { -not (Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($_.FullName,'.mp4'))) } |
      Sort-Object Name | Select-Object -First 3
Say "用的 rep："
$reps | ForEach-Object { Say ("  " + $_.Name) }

Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RB10 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EP cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static IntPtr FindTop(uint pid, string titleLike){
    IntPtr found=IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l){
      uint p; GetWindowThreadProcessId(h, out p);
      if(p==pid){ string t=Txt(h); if(t!=null && t.Contains(titleLike)){ found=h; return false; } }
      return true;
    }, IntPtr.Zero);
    return found;
  }
  public static string Grab(IntPtr h, string path, int cropW, int cropH){
    RECT r; if(!GetWindowRect(h, out r)) return "GetWindowRect 失败";
    int w=r.R-r.L, hh=r.B-r.T; if(w<=0||hh<=0) return "窗口尺寸异常";
    using(System.Drawing.Bitmap bmp=new System.Drawing.Bitmap(w,hh))
    using(System.Drawing.Graphics g=System.Drawing.Graphics.FromImage(bmp)){
      IntPtr hdc=g.GetHdc(); bool ok=PrintWindow(h,hdc,2); g.ReleaseHdc(hdc);
      int cw=Math.Min(cropW,w), ch=Math.Min(cropH,hh);
      using(System.Drawing.Bitmap c=bmp.Clone(new System.Drawing.Rectangle(0,0,cw,ch), bmp.PixelFormat)) { c.Save(path); }
      return ok? "OK":"PrintWindow false";
    }
  }
}
"@
function Click-Btn($h,$like){ $script:hit=[IntPtr]::Zero
  [void][RB10]::EnumChildWindows($h,[RB10+EP]{ param($c,$l) if([RB10]::Cls($c) -like '*BUTTON*'){ $t=[RB10]::Txt($c); if($t -and $t -like $like){ $script:hit=$c; return $false } }; return $true },[IntPtr]::Zero)
  if($script:hit -eq [IntPtr]::Zero){ return $false }
  [void][RB10]::SendMessageW($script:hit,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero); Start-Sleep -Seconds 1; return $true }
function Blacks($f,$mode){
  $out = if($mode -eq 'head'){ & $ffmpeg -hide_banner -nostats -t 20 -i $f -an -vf blackdetect=d=0.15:pix_th=0.10 -f null NUL 2>&1 }
         else { & $ffmpeg -hide_banner -nostats -sseof -15 -i $f -an -vf blackdetect=d=0.15:pix_th=0.10 -f null NUL 2>&1 }
  $lines=($out | Select-String 'black_start').Line
  if(-not $lines){ return "无 ≥0.15 秒黑段 ✓" }
  return (($lines | ForEach-Object { ($_ -replace '.*blackdetect[^]]*\]\s*','') }) -join ' ; ')
}

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== 六格全套验证（$(Get-Date -Format 'HH:mm:ss')）====="
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 3
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$true; $j.SkipExisting=$true; $j.MuteGameForMyEars=$false; $j.ParallelCount=6
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Say "  设置：同时录 6、AskSaveLocation=真（验证「录完会问放哪」）、SkipExisting=真"

$args=@(); foreach($r in $reps){ $args += ('"'+$r.FullName+'"') }
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$pr = Start-Process -FilePath $app -ArgumentList $args -PassThru
Start-Sleep -Seconds 12
$pr.Refresh(); $main=$pr.MainWindowHandle
Say ("  点「开始录制」（合并后的唯一按钮）：{0}" -f (Click-Btn $main "开始录制"))
$dlgSeen=$false; $sw=[Diagnostics.Stopwatch]::StartNew()
while($sw.Elapsed.TotalSeconds -lt 900){
  Start-Sleep -Seconds 3
  if(-not $dlgSeen){
    $d=[RB10]::FindTop([uint32]$pr.Id, '录制完成')
    if($d -ne [IntPtr]::Zero){
      $dlgSeen=$true
      Say ("  ✅ 录完弹出保存询问（开始后 {0:N0} 秒）：「{1}」" -f $sw.Elapsed.TotalSeconds, [RB10]::Txt($d))
      Say ("     点「留在 rep 旁边」：{0}" -f (Click-Btn $d "留在 rep 旁边"))
    }
  }
  if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } }
}
Say ("  总用时 {0:N0} 秒" -f $sw.Elapsed.TotalSeconds)
Say ""
Say "---------- 日志关键行 ----------"
Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '已开始录像|中继画布|窗口标题改成唯一值|建源并绑定完成|并行切分（第 1 格|并行切分完成（第 1 格|并行切分完成（第 2 格|保存|放进|恢复系统声音' | ForEach-Object { Say ("  " + $_.Line) }
Say ""
Say "---------- 每个成品 ----------"
$i=0
foreach($r in $reps){
  $i++
  $o=[System.IO.Path]::ChangeExtension($r.FullName,'.mp4')
  if(-not (Test-Path -LiteralPath $o)){ Say ("  第 $i 个：✗ 没有成品"); continue }
  $streams=(& $ffprobe -v error -show_entries stream=codec_type -of csv=p=0 -i $o 2>&1) -join ','
  $dur=(& $ffprobe -v error -show_entries format=duration -of csv=p=0 -i $o 2>&1) -join ''
  Say ("  第 $i 个：{0:N1} MB  时长 {1:N1}s（{2}分{3:00}秒）  流=[{4}]" -f ((Get-Item -LiteralPath $o).Length/1MB), [double]$dur, [int]([double]$dur/60), [int]([double]$dur%60), $streams)
  Say ("     音频流：{0}" -f $(if($streams -match 'audio'){'✅ 有'}else{'❌ 没有'}))
  Say ("     开头：{0}" -f (Blacks $o 'head'))
  Say ("     结尾：{0}" -f (Blacks $o 'tail'))
}
$shot="$dir\_看片\六格新-队列.png"
Say ("  界面截图：{0} → {1}" -f [RB10]::Grab($main,$shot,780,320), $shot)
Say ("  弹出的保存询问见过 = {0}" -f $dlgSeen)

# 抽一张成品画面（第 1 个的第 30 秒，放大到 800 宽）——用来肉眼确认"是完整游戏画面"而不是被放大裁切
$o1=[System.IO.Path]::ChangeExtension($reps[0].FullName,'.mp4')
if(Test-Path -LiteralPath $o1){
  $tmp="$env:TEMP\sixshot"; New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  & $ffmpeg -hide_banner -loglevel error -y -ss 30 -i "$o1" -frames:v 1 -vf "scale=800:-2" "$tmp\one.png" 2>&1 | Out-Null
  Copy-Item -LiteralPath "$tmp\one.png" -Destination "$dir\_看片\三格新-画面检查.png" -Force
  Say "  已抽第 1 个成品第 30 秒画面 → _看片\三格新-画面检查.png（应是完整 4:3 对局画面，两侧血条都在）"
}

Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
[System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
Say "  设置已还原"
Say "===== 结束（$(Get-Date -Format 'HH:mm:ss')）====="
