# 定向验证：「录制选中的」只选中 1 个时 → 降级成单录；而且已有录像也要重录（forceRecord）
$root  = 'D:\【完整包】非想天则整合包2026-06-15'
$dir   = "$root\RepRecorder"
$log   = "$dir\RepRecorder.log"
$app   = "$dir\RepRecorder.exe"
$cfg   = "$dir\RepRecorder.settings.json"
$outlog= "$dir\验证-选中单个降级单录.log"
$replay= "$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ffprobe='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffprobe.exe'
function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }
function Find-Rep($leaf){ $f=Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter $leaf -ErrorAction SilentlyContinue | Select-Object -First 1; if($f){return $f.FullName}; return $null }
$r1 = Find-Rep '26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep'
$r2 = Find-Rep '26-08-09_21-00-57_黛薇儿(蕾咪)_xingya(红美铃)_(1︰0_无).rep'
if(-not $r1 -or -not $r2){ Say "找不到 rep"; exit 1 }

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RB {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int a, bool b, uint pid);
  [DllImport("kernel32.dll")] public static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr a, IntPtr s, uint t, uint p);
  [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr h, IntPtr a, byte[] b, int s, out IntPtr w);
  [DllImport("kernel32.dll")] public static extern bool VirtualFreeEx(IntPtr h, IntPtr a, IntPtr s, uint t);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static int RowCount(IntPtr lv){ return (int)SendMessageW(lv, 0x1004, IntPtr.Zero, IntPtr.Zero); }
  public static int SelCount(IntPtr lv){ return (int)SendMessageW(lv, 0x1032, IntPtr.Zero, IntPtr.Zero); }
  [StructLayout(LayoutKind.Sequential)]
  public struct LVITEM { public uint mask; public int iItem; public int iSubItem; public uint state; public uint stateMask;
                         public IntPtr pszText; public int cchTextMax; public int iImage; public IntPtr lParam; public int iIndent; }
  public static void SetSel(IntPtr lv, uint pid, int index, bool sel){
    IntPtr h = OpenProcess(0x0438, false, pid);
    if (h == IntPtr.Zero) return;
    int size = Marshal.SizeOf(typeof(LVITEM));
    IntPtr mem = VirtualAllocEx(h, IntPtr.Zero, (IntPtr)size, 0x3000, 0x04);
    if (mem == IntPtr.Zero) { CloseHandle(h); return; }
    LVITEM it = new LVITEM();
    it.mask = 0x8; it.iItem = index; it.iSubItem = 0;
    it.state = sel ? (uint)(0x2 | 0x1) : 0u;
    it.stateMask = 0x2 | 0x1;
    byte[] buf = new byte[size];
    IntPtr tmp = Marshal.AllocHGlobal(size);
    Marshal.StructureToPtr(it, tmp, false);
    Marshal.Copy(tmp, buf, 0, size);
    Marshal.FreeHGlobal(tmp);
    IntPtr wr;
    if (WriteProcessMemory(h, mem, buf, size, out wr)) SendMessageW(lv, 0x102B, (IntPtr)index, mem);
    VirtualFreeEx(h, mem, IntPtr.Zero, 0x8000);
    CloseHandle(h);
  }
}
"@
function Kids($h){ $script:ks=New-Object System.Collections.ArrayList
  $cb=[RB+EP]{ param($ch,$l) [void]$script:ks.Add([pscustomobject]@{H=$ch;C=[RB]::Cls($ch);T=[RB]::Txt($ch)}); return $true }
  [void][RB]::EnumChildWindows($h,$cb,[IntPtr]::Zero); return $script:ks }
function Find-Cls($h,$like){ foreach($k in (Kids $h)){ if($k.C -like $like){ return $k.H } }; return [IntPtr]::Zero }
function Click-Btn($h,$like){ foreach($k in (Kids $h)){ if($k.C -like '*BUTTON*' -and $k.T -like $like){ Say ("  点按钮「{0}」" -f $k.T); [void][RB]::SendMessageW($k.H,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero); Start-Sleep -Seconds 2; return $true } }; Say "  ✗ 找不到按钮「$like」"; return $false }
function Wait-Marker($pattern,$timeoutSec,$skip){ $sw=[Diagnostics.Stopwatch]::StartNew()
  while($sw.Elapsed.TotalSeconds -lt $timeoutSec){ Start-Sleep -Seconds 3
    if(Test-Path -LiteralPath $log){ $all=@(Get-Content -LiteralPath $log -Encoding UTF8)
      if($all.Count -gt $skip){ $tail=@($all[$skip..($all.Count-1)]); if(@($tail | Select-String -Pattern $pattern).Count -gt 0){ return ,$tail } } } }
  return ,@() }

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== 「录制选中的」只选 1 个 → 应降级单录且重录已有录像（$(Get-Date -Format 'HH:mm:ss')）====="
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.SkipExisting=$true; $j.ParallelCount=3; $j.RecordGameAudio=$false
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
# 先造一个"已有录像"：r2 保留它的 mp4，r1 删掉
$o1=[System.IO.Path]::ChangeExtension($r1,'.mp4'); if(Test-Path -LiteralPath $o1){ Remove-Item -LiteralPath $o1 -Force }
$o2=[System.IO.Path]::ChangeExtension($r2,'.mp4')
"$($o2)：{0}" -f $(if(Test-Path -LiteralPath $o2){'已存在（用来验证"重录"）'}else{'不存在'}) | ForEach-Object { Say $_ }
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue

$pr = Start-Process -FilePath $app -ArgumentList ('"'+$r1+'"'),('"'+$r2+'"') -PassThru
Start-Sleep -Seconds 10
$pr.Refresh(); $h=$pr.MainWindowHandle
$lv = Find-Cls $h '*SysListView32*'
Say ("  列表行数 = {0}（排序后：1=黛薇儿(已有录像) 2=Aquila）" -f [RB]::RowCount($lv))
for($i=0;$i -lt [RB]::RowCount($lv);$i++){ [RB]::SetSel($lv,$pr.Id,$i,$false) }   # 先全部取消
Start-Sleep -Milliseconds 300
[RB]::SetSel($lv,$pr.Id,0,$true)                                                  # 只选第 1 行（黛薇儿，已有录像）
Start-Sleep -Milliseconds 500
Say ("  当前选中数 = {0}（应为 1）" -f [RB]::SelCount($lv))

$skip = @(Get-Content -LiteralPath $log -Encoding UTF8).Count
Click-Btn $h "录制选中的" | Out-Null
$t = Wait-Marker '选中批次全部结束' 420 $skip
$t | Select-String '录制选中的|第 . 批|只有 1 个|明确选中|开始录像|完成|跳过' | ForEach-Object { Say ("  日志| " + $_.Line) }
if(Test-Path -LiteralPath $o2){
  $d=(& $ffprobe -v error -show_entries format=duration -of csv=p=0 $o2 2>&1) -join ''
  Say ("  重录后的产物：{0:N1} MB / {1}s" -f ((Get-Item -LiteralPath $o2).Length/1MB), $d)
} else { Say "  ✗ 没有产物" }
$j2 = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j2.AskSaveLocation=$true; $j2.RecordGameAudio=$true
$j2 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Say "===== 结束（$(Get-Date -Format 'HH:mm:ss')）====="
