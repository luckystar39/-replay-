# 验证：录像里叠「玩家名字 / 比分」，两个开关各自生效；ScoreTracker 设置临时改、录完还原
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"; $app="$dir\RepRecorder.exe"; $cfg="$dir\RepRecorder.settings.json"
$outlog="$dir\验证-名字比分叠加.log"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$stIni="$root\【完整包】非想天则整合包2026-06-15\th123\modules\ScoreTracker\ScoreTracker.ini"
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }
function Find-Rep($leaf){ $f=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter $leaf -ErrorAction SilentlyContinue | Select-Object -First 1; if($f){return $f.FullName}; return $null }
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RB11 {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static string Grab(IntPtr h, string path, int cw, int ch){
    RECT r; if(!GetWindowRect(h, out r)) return "fail";
    int w=r.R-r.L, hh=r.B-r.T; if(w<=0||hh<=0) return "bad size";
    using(System.Drawing.Bitmap b=new System.Drawing.Bitmap(w,hh))
    using(System.Drawing.Graphics g=System.Drawing.Graphics.FromImage(b)){
      IntPtr hdc=g.GetHdc(); PrintWindow(h,hdc,2); g.ReleaseHdc(hdc);
      using(System.Drawing.Bitmap c=b.Clone(new System.Drawing.Rectangle(0,0,Math.Min(cw,w),Math.Min(ch,hh)), b.PixelFormat)) c.Save(path);
      return "OK";
    }
  }
}
"@
function Click-Btn($h,$like){ $script:hit=[IntPtr]::Zero
  [void][RB11]::EnumChildWindows($h,[RB11+EP]{ param($c,$l) if([RB11]::Cls($c) -like '*BUTTON*'){ $t=[RB11]::Txt($c); if($t -and $t -like $like){ $script:hit=$c; return $false } }; return $true },[IntPtr]::Zero)
  if($script:hit -eq [IntPtr]::Zero){ return $false }
  [void][RB11]::SendMessageW($script:hit,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero); Start-Sleep -Seconds 1; return $true }
function Wait-Queue($sec){ $sw=[Diagnostics.Stopwatch]::StartNew()
  while($sw.Elapsed.TotalSeconds -lt $sec){ Start-Sleep -Seconds 3
    if(Test-Path -LiteralPath $log){ if((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -match '队列结束'){ break } } }
  return $sw.Elapsed.TotalSeconds }

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== 名字/比分叠加验证（$(Get-Date -Format 'HH:mm:ss')）====="
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
$stBak=(Get-Content -LiteralPath $stIni -Raw -Encoding UTF8)
$short = Find-Rep '26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep'
$kud = (Get-ChildItem -LiteralPath "$replay\【联机对战Rep】\[kud(3)]" -File -Filter '*.rep' |
        Where-Object { -not (Test-Path -LiteralPath ([System.IO.Path]::ChangeExtension($_.FullName,'.mp4'))) } |
        Sort-Object Name | Select-Object -First 1).FullName
Say "  用这两个 rep："; Say ("    A: " + (Split-Path $short -Leaf)); Say ("    B: " + (Split-Path $kud -Leaf))

Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 3

function Run-One($rep, $names, $score, $tag){
  $j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
  $j.AskSaveLocation=$false; $j.SkipExisting=$false; $j.MuteGameForMyEars=$false; $j.ParallelCount=1
  $j | Add-Member -NotePropertyName ShowPlayerNames -NotePropertyValue $names -Force
  $j | Add-Member -NotePropertyName ShowMatchScore -NotePropertyValue $score -Force
  $j | Add-Member -NotePropertyName SettingsVersion -NotePropertyValue 2 -Force
  $j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
  $o=[System.IO.Path]::ChangeExtension($rep,'.mp4'); if(Test-Path -LiteralPath $o){ Remove-Item -LiteralPath $o -Force }
  Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
  $pr = Start-Process -FilePath $app -ArgumentList ('"'+$rep+'"') -PassThru
  Start-Sleep -Seconds 11
  $pr.Refresh(); Click-Btn $pr.MainWindowHandle "开始录制" | Out-Null
  $t = Wait-Queue 300
  Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep 2
  Say ("  [$tag] 名字=$names 比分=$score  用时 {0:N0} 秒" -f $t)
  Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '已叠加文字|不叠字|没开|临时关掉|已还原 ScoreTracker' | ForEach-Object { Say ("      日志| " + $_.Line) }
  if(Test-Path -LiteralPath $o){
    $tmp="$env:TEMP\ov"; New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    & $ffmpeg -hide_banner -loglevel error -y -ss 15 -i "$o" -frames:v 1 -vf "scale=900:-2" "$tmp\$tag.png" 2>&1 | Out-Null
    Copy-Item -LiteralPath "$tmp\$tag.png" -Destination "$dir\_看片\叠加-$tag.png" -Force
    Say ("      画面已抽 → _看片\叠加-$tag.png")
  } else { Say "      ✗ 没出成品" }
}

try{
  Run-One $short $true  $true  '名字+比分'
  Run-One $kud   $false $true  '只比分'
} finally {
  Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
  [System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
  $now=(Get-Content -LiteralPath $stIni -Raw -Encoding UTF8)
  Say ("  ScoreTracker.ini 是否已还原 = {0}" -f ($now -eq $stBak))
  [System.IO.File]::WriteAllText($stIni,$stBak,(New-Object System.Text.UTF8Encoding($false)))
  Say "  设置已还原"
}
Say "===== 结束（$(Get-Date -Format 'HH:mm:ss')）====="
