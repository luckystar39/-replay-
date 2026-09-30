# 验证-面板记忆.ps1
# 验证「面板宽度 / 收起状态」能不能跨重启记住，以及收起后那条窄活动栏。
# 跑三轮：收起→重启（应仍收起）→点活动栏展开→再重启（应仍展开）。
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class P {
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

    static IntPtr FindWhere(IntPtr root, string clsPrefix, string text, bool exact) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(root, delegate(IntPtr h, IntPtr p) {
            if (found != IntPtr.Zero) return false;
            if (Cls(h).StartsWith(clsPrefix)) {
                string t = Text(h);
                if (exact ? t == text : t.Contains(text)) { found = h; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static IntPtr Button(IntPtr root, string contains) { return FindWhere(root, "WindowsForms10.BUTTON", contains, false); }
    public static IntPtr ButtonExact(IntPtr root, string text) { return FindWhere(root, "WindowsForms10.BUTTON", text, true); }
    public static string Click(IntPtr h) {
        if (h == IntPtr.Zero) return "(没找到)";
        string t = Text(h); SendMessage(h, 0x00F5, IntPtr.Zero, IntPtr.Zero); return t;
    }
    public static int W(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.R-r.L; }
    public static int H(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.B-r.T; }
    public static void Shot(IntPtr h, IntPtr dc){ PrintWindow(h, dc, 2); }
}
"@

function Shot([IntPtr]$h, [string]$name) {
    $w = [P]::W($h); $ht = [P]::H($h)
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc(); [P]::Shot($h, $dc) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
    $bmp.Save((Join-Path $PSScriptRoot ("_看片\" + $name)), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Settings {
    $j = Get-Content (Join-Path $PSScriptRoot 'RepRecorder.settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    "      settings → PanelWidth=$($j.PanelWidth)  PanelCollapsed=$($j.PanelCollapsed)  (v$($j.SettingsVersion))"
}

function Launch {
    Get-Process RepRecorder -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
    Start-Sleep -Milliseconds 500
    $p = Start-Process -FilePath (Join-Path $PSScriptRoot 'RepRecorder.exe') -PassThru
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 300
        $p.Refresh()
        if ($p.HasExited) { "程序退出了 ExitCode=$($p.ExitCode)"; exit 1 }
        if ($p.MainWindowHandle -ne 0) { $h = $p.MainWindowHandle; break }
    }
    if ($h -eq [IntPtr]::Zero) { "没等到主窗口"; exit 1 }
    Start-Sleep -Seconds 2
    return @{ Proc = $p; Hwnd = $h }
}

# ---------- 第 1 轮：启动 → 收起 ----------
"=== 第 1 轮：启动，点「收起面板」 ==="
$a = Launch
Settings
"  顶栏按钮：'$([P]::Text([P]::Button($a.Hwnd, '面板')))'"
[P]::Click([P]::Button($a.Hwnd, '收起面板')) | Out-Null
Start-Sleep -Milliseconds 900
"  点完顶栏按钮：'$([P]::Text([P]::Button($a.Hwnd, '面板')))'"
"  活动栏上的 ▶ 按钮：[P]::ButtonExact → '$([P]::Text([P]::ButtonExact($a.Hwnd, '▶')))'"
Settings
Shot $a.Hwnd "记忆-1-收起.png"
Stop-Process -Id $a.Proc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

# ---------- 第 2 轮：重启，应该还是收起 ----------
"=== 第 2 轮：重启，应仍是「收起」 ==="
$b = Launch
"  顶栏按钮：'$([P]::Text([P]::Button($b.Hwnd, '面板')))'   ← 应该是「展开面板」"
Settings
Shot $b.Hwnd "记忆-2-重启后仍是收起.png"
"  点活动栏上的 ▶ 展开"
[P]::Click([P]::ButtonExact($b.Hwnd, '▶')) | Out-Null
Start-Sleep -Milliseconds 900
"  顶栏按钮：'$([P]::Text([P]::Button($b.Hwnd, '面板')))'"
Settings
Shot $b.Hwnd "记忆-3-活动栏点开后展开.png"
Stop-Process -Id $b.Proc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

# ---------- 第 3 轮：重启，应该是展开 ----------
"=== 第 3 轮：重启，应仍是「展开」 ==="
$c = Launch
"  顶栏按钮：'$([P]::Text([P]::Button($c.Hwnd, '面板')))'   ← 应该是「收起面板」"
Settings
Shot $c.Hwnd "记忆-4-重启后仍是展开.png"
Stop-Process -Id $c.Proc.Id -Force -ErrorAction SilentlyContinue
