# 验证-面板收缩.ps1
# 验证左侧控制面板的「收起 / 展开」：点顶栏那个按钮，各截一张图。
# 用法：powershell -File 验证-面板收缩.ps1
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Ui {
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }

    public static string Text(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
    public static string Cls(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }

    public static IntPtr FindButton(IntPtr root, string contains) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(root, delegate(IntPtr h, IntPtr p) {
            if (found != IntPtr.Zero) return false;
            if (Cls(h).StartsWith("WindowsForms10.BUTTON") && Text(h).Contains(contains)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string Click(IntPtr h) {
        if (h == IntPtr.Zero) return "(没找到按钮)";
        string t = Text(h);
        SendMessage(h, 0x00F5, IntPtr.Zero, IntPtr.Zero);   // BM_CLICK
        return t;
    }

    public static int W(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.R-r.L; }
    public static int H(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.B-r.T; }
    public static void Shot(IntPtr h, IntPtr dc){ PrintWindow(h, dc, 2); }
}
"@

function Shot([IntPtr]$h, [string]$name) {
    $w = [Ui]::W($h); $ht = [Ui]::H($h)
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc()
    [Ui]::Shot($h, $dc) | Out-Null
    $g.ReleaseHdc($dc); $g.Dispose()
    $p = Join-Path $PSScriptRoot ("_看片\" + $name)
    $bmp.Save($p, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "  已存 $name  (${w}x${ht})"
}

Get-Process RepRecorder -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
Start-Sleep -Milliseconds 500

$proc = Start-Process -FilePath (Join-Path $PSScriptRoot 'RepRecorder.exe') -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 300
    $proc.Refresh()
    if ($proc.HasExited) { "程序退出了，ExitCode=$($proc.ExitCode)"; exit 1 }
    if ($proc.MainWindowHandle -ne 0) { $h = $proc.MainWindowHandle; break }
}
if ($h -eq [IntPtr]::Zero) { "没等到主窗口"; $proc.Kill(); exit 1 }
Start-Sleep -Seconds 2

"[1] 初始（展开）状态"
Shot $h "面板-1-展开.png"

$btn = [Ui]::FindButton($h, "面板")
"  找到按钮：'$([Ui]::Text($btn))'"

"[2] 点一下 → 应该收起"
[Ui]::Click($btn) | Out-Null
Start-Sleep -Milliseconds 900
$btn = [Ui]::FindButton($h, "面板")           # 重新找：文字变了
"  按钮现在写着：'$([Ui]::Text($btn))'"
Shot $h "面板-2-收起.png"

"[3] 再点一下 → 应该展开并还原宽度"
[Ui]::Click($btn) | Out-Null
Start-Sleep -Milliseconds 900
$btn = [Ui]::FindButton($h, "面板")
"  按钮现在写着：'$([Ui]::Text($btn))'"
Shot $h "面板-3-展开还原.png"

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
