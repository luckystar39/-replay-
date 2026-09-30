# 验证-背景设置.ps1
# 点顶栏的「背景…」，把设置对话框截下来。
# 注意：对话框是模态的（ShowDialog 会阻塞 UI 线程），所以必须用 PostMessage 而不是 SendMessage，
#       否则脚本会一直卡在那个调用上。
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class D {
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }

    public static string Text(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
    public static string Cls(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }

    // WinForms 的 Form 类名是 WindowsForms10.Window.xxx，不是 Win32 的 #32770，
    // 所以只能枚举顶层窗口按标题找。
    public delegate bool EnumTopProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumTopProc cb, IntPtr p);
    public static IntPtr FindTopWindow(string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr p) {
            if (found != IntPtr.Zero) return false;
            if (Text(h) == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr FindBtn(IntPtr root, string contains, bool exact) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(root, delegate(IntPtr h, IntPtr p) {
            if (found != IntPtr.Zero) return false;
            if (Cls(h).StartsWith("WindowsForms10.BUTTON")) {
                string t = Text(h);
                if (exact ? t == contains : t.Contains(contains)) { found = h; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static string Click(IntPtr h) { if (h == IntPtr.Zero) return "(没找到)"; string t = Text(h); PostMessage(h, 0x00F5, IntPtr.Zero, IntPtr.Zero); return t; }
    public static int W(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.R-r.L; }
    public static int H(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.B-r.T; }
    public static void Shot(IntPtr h, IntPtr dc){ PrintWindow(h, dc, 2); }
    public static void Close(IntPtr h){ PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }
}
"@

function Shot([IntPtr]$h, [string]$name) {
    $w = [D]::W($h); $ht = [D]::H($h)
    if ($w -le 0 -or $ht -le 0) { "  $name 尺寸异常"; return }
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc(); [D]::Shot($h, $dc) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
    $bmp.Save((Join-Path $PSScriptRoot ("_看片\" + $name)), [System.Drawing.Imaging.ImageFormat]::Png)
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
    if ($proc.MainWindowHandle -ne 0) { $h = $proc.MainWindowHandle; break }
}
Start-Sleep -Seconds 2

"点「背景…」按钮：'$([D]::Click([D]::FindBtn($h, '背景', $false)))'"
Start-Sleep -Seconds 2

$dlg = [D]::FindTopWindow("背景设置")
if ($dlg -eq [IntPtr]::Zero) { "没找到背景设置对话框 ✗" }
else {
    "找到对话框 ✓  $([D]::W($dlg))x$([D]::H($dlg))"
    Shot $dlg "背景-4-设置对话框.png"
    [D]::Close($dlg) | Out-Null
    Start-Sleep -Milliseconds 800
}
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
