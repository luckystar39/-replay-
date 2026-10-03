# 诊断：单录开录前"等画面"为什么等不到（打印每次截图的最大亮度与捕获方式）
$root='D:\【完整包】非想天则整合包2026-06-15'
$dir="$root\RepRecorder"
$log="$dir\RepRecorder.log"; $app="$dir\RepRecorder.exe"; $cfg="$dir\RepRecorder.settings.json"
$outlog="$dir\诊断-单录等画面.log"
$replay="$root\【完整包】非想天则整合包2026-06-15\th123\replay"
function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }
function Find-Rep($leaf){ $f=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter $leaf -ErrorAction SilentlyContinue | Select-Object -First 1; if($f){return $f.FullName}; return $null }
$rep = Find-Rep '26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep'
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RB8 {
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
  [void][RB8]::EnumChildWindows($h,[RB8+EP]{ param($c,$l) if([RB8]::Cls($c) -like '*BUTTON*'){ $t=[RB8]::Txt($c); if($t -and $t -like $like){ $script:hit=$c; return $false } }; return $true },[IntPtr]::Zero)
  if($script:hit -eq [IntPtr]::Zero){ Say "  ✗ 找不到「$like」"; return $false }
  Say ("  点按钮「{0}」" -f [RB8]::Txt($script:hit))
  [void][RB8]::SendMessageW($script:hit,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero); Start-Sleep -Seconds 2; return $true }

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
$cfgbak=(Get-Content -LiteralPath $cfg -Raw -Encoding UTF8)
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 3
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.SkipExisting=$false; $j.MuteGameForMyEars=$false; $j.ParallelCount=1
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
$o=[System.IO.Path]::ChangeExtension($rep,'.mp4'); if(Test-Path -LiteralPath $o){ Remove-Item -LiteralPath $o -Force }
Say "===== 诊断单录等画面（$(Get-Date -Format 'HH:mm:ss')）====="
$pr = Start-Process -FilePath $app -ArgumentList ('"'+$rep+'"') -PassThru
Start-Sleep -Seconds 10
$pr.Refresh(); $h=$pr.MainWindowHandle
Click-Btn $h "开始录制" | Out-Null
$sw=[Diagnostics.Stopwatch]::StartNew()
$seen=@()
while($sw.Elapsed.TotalSeconds -lt 150){
  Start-Sleep -Seconds 2
  if(Test-Path -LiteralPath $log){
    $txt=Get-Content -LiteralPath $log -Raw -Encoding UTF8
    foreach($m in [regex]::Matches($txt,'(?m)^\d\d:\d\d:\d\d\s+.*(等画面|开录前等了|开始录像|采集画面|失败|完成：).*$')){
      $line=$m.Value.Trim()
      if($seen -notcontains $line){ $seen += $line; Say ("  日志| " + $line) }
    }
    if($txt -match '队列结束'){ break }
  }
}
Say ("  总用时 {0:N0} 秒" -f $sw.Elapsed.TotalSeconds)
$o=[System.IO.Path]::ChangeExtension($rep,'.mp4')
if(Test-Path -LiteralPath $o){ $i=Get-Item -LiteralPath $o; Say ("  成品：{0:N1} MB" -f ($i.Length/1MB)) } else { Say "  ✗ 没有成品" }
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
[System.IO.File]::WriteAllText($cfg,$cfgbak,(New-Object System.Text.UTF8Encoding($false)))
Say "===== 结束 ====="
