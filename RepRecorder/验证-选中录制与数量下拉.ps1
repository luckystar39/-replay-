# 验证新功能：①同时录制数量 1~10 下拉；②「录制选中的」按钮（可多选、按数量分批、1 个时降级单录、已有录像也重录）
$root  = 'D:\【完整包】非想天则整合包2026-06-15'
$dir   = "$root\RepRecorder"
$log   = "$dir\RepRecorder.log"
$app   = "$dir\RepRecorder.exe"
$cfg   = "$dir\RepRecorder.settings.json"
$outlog= "$dir\验证-选中录制与数量下拉.log"
$replay= "$root\【完整包】非想天则整合包2026-06-15\th123\replay"
$ffprobe='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffprobe.exe'

function Say($m){ $m | Tee-Object -FilePath $outlog -Append | Out-Null; Write-Host $m }
function Find-Rep($leaf){
  $f = Get-ChildItem -LiteralPath $replay -Recurse -Force -File -Filter $leaf -ErrorAction SilentlyContinue | Select-Object -First 1
  if($f){ return $f.FullName }
  return $null
}
$names = @(
  '26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep',
  '26-08-15_23-57-29_menghenyinxin(红美铃)_Timepieceit LV2(红美铃)_(1︰0_无).rep',
  '26-09-02_13-47-16_Unstrung Doll (爱丽丝)_阿虚reality(咲夜)_(0︰0_无).rep',
  '26-08-09_21-00-57_黛薇儿(蕾咪)_xingya(红美铃)_(1︰0_无).rep'
)
$reps = @(); foreach($n in $names){ $p = Find-Rep $n; if($p){ $reps += $p } else { Say "✗ 找不到 rep: $n" } }
if($reps.Count -lt 4){ Say "只找到 $($reps.Count) 个 rep，中止"; exit 1 }

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class RA {
  public delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EP cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
  [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int a, bool b, uint pid);
  [DllImport("kernel32.dll")] public static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr a, IntPtr s, uint t, uint p);
  [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr h, IntPtr a, byte[] b, int s, out IntPtr w);
  [DllImport("kernel32.dll")] public static extern bool VirtualFreeEx(IntPtr h, IntPtr a, IntPtr s, uint t);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
  public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); return sb.ToString(); }
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassNameW(h,sb,256); return sb.ToString(); }
  [StructLayout(LayoutKind.Sequential)]
  public struct LVITEM { public uint mask; public int iItem; public int iSubItem; public uint state; public uint stateMask;
                         public IntPtr pszText; public int cchTextMax; public int iImage; public IntPtr lParam; public int iIndent; }
  // 跨进程给 ListView 的某一行打上"选中"状态
  // 注意：OpenProcess 只能要 0x0438（要 ALL_ACCESS 会被拒 err=5）；选中数用 LVM_GETSELECTEDCOUNT=0x1032
  public static bool SelectRow(IntPtr lv, uint pid, int index) {
    IntPtr h = OpenProcess(0x0438, false, pid);
    if (h == IntPtr.Zero) return false;
    int size = Marshal.SizeOf(typeof(LVITEM));
    IntPtr mem = VirtualAllocEx(h, IntPtr.Zero, (IntPtr)size, 0x3000, 0x04);
    if (mem == IntPtr.Zero) { CloseHandle(h); return false; }
    LVITEM it = new LVITEM();
    it.mask = 0x2; it.iItem = index; it.iSubItem = 0;
    it.state = 0x2 | 0x1; it.stateMask = 0x2 | 0x1;   // LVIS_SELECTED | LVIS_FOCUSED
    byte[] buf = new byte[size];
    IntPtr tmp = Marshal.AllocHGlobal(size);
    Marshal.StructureToPtr(it, tmp, false);
    Marshal.Copy(tmp, buf, 0, size);
    Marshal.FreeHGlobal(tmp);
    IntPtr wr;
    WriteProcessMemory(h, mem, buf, size, out wr);
    SendMessageW(lv, 0x102B, (IntPtr)index, mem);     // LVM_SETITEMSTATE
    VirtualFreeEx(h, mem, IntPtr.Zero, 0x8000);
    CloseHandle(h);
    return true;
  }
}
"@

$script:mainW=[IntPtr]::Zero
function Children($h){
  $script:kids = New-Object System.Collections.ArrayList
  $cb=[RA+EP]{ param($ch,$l)
    [void]$script:kids.Add([pscustomobject]@{ H=$ch; C=[RA]::Cls($ch); T=[RA]::Txt($ch) })
    return $true }
  [void][RA]::EnumChildWindows($h,$cb,[IntPtr]::Zero)
  return $script:kids
}
function Find-ByClass($h,$like){
  foreach($k in (Children $h)){ if($k.C -like $like){ return $k.H } }
  return [IntPtr]::Zero
}
function Find-Btn($h,$like){
  foreach($k in (Children $h)){ if($k.C -like '*BUTTON*' -and $k.T -like $like){ return $k.H } }
  return [IntPtr]::Zero
}
function Click-Btn($h,$like){
  $b = Find-Btn $h $like
  if($b -eq [IntPtr]::Zero){ Say "  ✗ 找不到按钮「$like」"; return $false }
  Say ("  点按钮「{0}」" -f [RA]::Txt($b))
  [void][RA]::SendMessageW($b, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)   # BM_CLICK
  Start-Sleep -Seconds 2
  return $true
}
function Wait-Marker($pattern,$timeoutSec,$skip){
  $sw=[Diagnostics.Stopwatch]::StartNew()
  while($sw.Elapsed.TotalSeconds -lt $timeoutSec){
    Start-Sleep -Seconds 3
    if(Test-Path -LiteralPath $log){
      $all=@(Get-Content -LiteralPath $log -Encoding UTF8)
      if($all.Count -gt $skip){
        $tail=@($all[$skip..($all.Count-1)])
        if(@($tail | Select-String -Pattern $pattern).Count -gt 0){ return ,$tail }
      }
    }
  }
  return ,@()
}
function Clean-Out { foreach($r in $reps){ $o=[System.IO.Path]::ChangeExtension($r,'.mp4'); if(Test-Path -LiteralPath $o){ Remove-Item -LiteralPath $o -Force -ErrorAction SilentlyContinue } } }
function Out-Info($r){
  $o=[System.IO.Path]::ChangeExtension($r,'.mp4')
  if(Test-Path -LiteralPath $o){ $d=(& $ffprobe -v error -show_entries format=duration -of csv=p=0 $o 2>&1) -join ''; return ("有  {0:N1} MB / {1}s" -f ((Get-Item -LiteralPath $o).Length/1MB), $d) }
  return '无'
}

Remove-Item -LiteralPath $outlog -Force -ErrorAction SilentlyContinue
Say "===== 新功能验证：数量下拉 1~10 + 录制选中的（$(Get-Date -Format 'HH:mm:ss')）====="
Get-Process RepRecorder,th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j.AskSaveLocation=$false; $j.SkipExisting=$true; $j.ParallelCount=3; $j.RecordGameAudio=$false; $j.AutoPrepareObs=$true
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Clean-Out
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue

$argList = ($reps | ForEach-Object { '"' + $_ + '"' }) -join ' '
$pr = Start-Process -FilePath $app -ArgumentList $argList -PassThru
Start-Sleep -Seconds 10
$pr.Refresh(); $h=$pr.MainWindowHandle
if($h -eq [IntPtr]::Zero){ Say "✗ 主窗口没出现"; exit 1 }

# ---------------- A：界面控件检查 ----------------
Say ""
Say "########## A：界面控件检查 ##########"
$btns = @(Children $h | Where-Object { $_.C -like '*BUTTON*' -and $_.T } | ForEach-Object { $_.T })
Say ("  按钮： " + ($btns -join ' | '))
$cbx = Find-ByClass $h '*COMBOBOX*'
if($cbx -eq [IntPtr]::Zero){ Say "  ✗ 没找到下拉框" } else {
  $cnt = [int][RA]::SendMessageW($cbx, 0x0146, [IntPtr]::Zero, [IntPtr]::Zero)      # CB_GETCOUNT
  $cur = [int][RA]::SendMessageW($cbx, 0x0147, [IntPtr]::Zero, [IntPtr]::Zero)      # CB_GETCURSEL
  Say ("  下拉框项数 = {0}（应为 10）；当前选中第 {1} 项 → 值 {2}" -f $cnt, ($cur+1), ($cur+1))
  # 程序化改成第 2 项（值 2），再给父窗口发 CBN_SELCHANGE 让 WinForms 触发事件
  [void][RA]::SendMessageW($cbx, 0x014E, [IntPtr]1, [IntPtr]::Zero)                 # CB_SETCURSEL
  $id = [RA]::GetDlgCtrlID($cbx)
  [void][RA]::SendMessageW([RA]::GetParent($cbx), 0x0111, [IntPtr]((1 -shl 16) -bor $id), $cbx)   # 发给下拉框的父窗口
  Start-Sleep -Seconds 2
  $cur2 = [int][RA]::SendMessageW($cbx, 0x0147, [IntPtr]::Zero, [IntPtr]::Zero)
  Say ("  改为后：第 {0} 项 → 值 {1}；并行按钮文字 = 「{2}」" -f ($cur2+1), ($cur2+1), [RA]::Txt((Find-Btn $h '并行录制*')))
  Get-Content -LiteralPath $log -Encoding UTF8 | Select-String '同时录制数量' | ForEach-Object { Say ("  日志| " + $_.Line) }
  $saved = (Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json).ParallelCount
  Say ("  设置文件里的 ParallelCount = {0}（应为 2）" -f $saved)
}

# ---------------- B：选中 2 行并行录 ----------------
Say ""
Say "########## B：选中第 2、3 行 → 并行录 2 个 ##########"
$lv = Find-ByClass $h '*SysListView32*'
if($lv -eq [IntPtr]::Zero){ Say "  ✗ 没找到列表控件"; exit 1 }
Clean-Out
[void][RA]::SelectRow($lv, $pr.Id, 1)
[void][RA]::SelectRow($lv, $pr.Id, 2)
Start-Sleep -Seconds 1
Say ("  已选中第 2、3 行（列表自报选中数 = {0}，应为 2）" -f [RA]::SelCount($lv))
$skip = @(Get-Content -LiteralPath $log -Encoding UTF8).Count
Click-Btn $h "录制选中的" | Out-Null
$t = Wait-Marker '选中批次全部结束' 600 $skip
$t | Select-String '录制选中的|第 . 批|并行录制开始|切分完成|选中批次' | ForEach-Object { Say ("  日志| " + $_.Line) }
Say "  产物情况（应为：第 1、4 个没有，第 2、3 个有）："
for($i=0;$i -lt 4;$i++){ Say ("    {0}. {1} → {2}" -f ($i+1), (Split-Path $reps[$i] -Leaf).Substring(0,16), (Out-Info $reps[$i])) }

# ---------------- C：只选 1 行（期望降级单录 + 已有录像也重录）----------------
Say ""
Say "########## C：只选第 2 行（已有录像）→ 应降级单录并重录 ##########"
[void][RA]::SelectRow($lv, $pr.Id, 1)
Start-Sleep -Seconds 1
Say ("  已选中第 2 行（选中数 = {0}，应为 1）" -f [RA]::SelCount($lv))
$skip2 = @(Get-Content -LiteralPath $log -Encoding UTF8).Count
Click-Btn $h "录制选中的" | Out-Null
$t2 = Wait-Marker '选中批次全部结束|队列结束' 420 $skip2
$t2 | Select-String '录制选中的|只有 1 个|明确选中|完成|开始录像|跳过' | ForEach-Object { Say ("  日志| " + $_.Line) }
Say ("  第 2 个产物 → {0}" -f (Out-Info $reps[1]))

$j2 = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j2.AskSaveLocation=$true; $j2.RecordGameAudio=$true; $j2.ParallelCount=3
$j2 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
Get-Process RepRecorder -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Say ""
Say "===== 验证结束（$(Get-Date -Format 'HH:mm:ss')）====="
