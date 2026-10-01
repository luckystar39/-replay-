# 验证-组合框高亮.ps1
# 验证「同时录制数量」下拉框选完之后，不会再留一片蓝底高亮。
# 做法：找到下拉框的真实屏幕位置 → 点开 → 选下一项 → 截图（会临时改动设置，跑完还原）。
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class C {
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }

    public static List<IntPtr> Combos = new List<IntPtr>();
    public static void FindCombos(IntPtr root) {
        Combos.Clear();
        EnumChildWindows(root, delegate(IntPtr h, IntPtr p) {
            if (Cls(h).StartsWith("WindowsForms10.COMBOBOX") && IsWindowVisible(h)) Combos.Add(h);
            return true;
        }, IntPtr.Zero);
    }
    public static string Cls(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
    public static int CX(IntPtr h) { RECT r; GetWindowRect(h, out r); return (r.L + r.R) / 2; }
    public static int CY(IntPtr h) { RECT r; GetWindowRect(h, out r); return (r.T + r.B) / 2; }
    public static int RL(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.L; }
    public static int RT(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.T; }
    public static int RW(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.R - r.L; }
    public static int RH(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.B - r.T; }
    public static bool Focus(IntPtr h) { return SetForegroundWindow(h); }
    public static int W(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.R-r.L; }
    public static int H(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.B-r.T; }
    public static void Shot(IntPtr h, IntPtr dc){ PrintWindow(h, dc, 2); }
}
"@

function Shot([IntPtr]$h, [string]$name) {
    $w = [C]::W($h); $ht = [C]::H($h)
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc(); [C]::Shot($h, $dc) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
    $bmp.Save((Join-Path $PSScriptRoot ("_看片\" + $name)), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$before = (Get-Content (Join-Path $PSScriptRoot 'RepRecorder.settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json).ParallelCount
$saved = [System.Windows.Forms.Cursor]::Position
try {
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
    [C]::Focus($h) | Out-Null
    Start-Sleep -Milliseconds 400

    [C]::FindCombos($h)
    "找到 $([C]::Combos.Count) 个下拉框（应含「同时录制数量」和「录音模式」）"
    $cb = [C]::Combos[0]
    "第 1 个位置: ($([C]::RL($cb)),$([C]::RT($cb)))  $([C]::RW($cb))x$([C]::RH($cb))"

    # 点一下开下拉
    [C]::SetCursorPos([C]::CX($cb), [C]::CY($cb)) | Out-Null
    Start-Sleep -Milliseconds 400
    [C]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    [C]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 700
    # 选下一项并确认
    [C]::keybd_event(0x28, 0, 0, [IntPtr]::Zero)   # Down
    Start-Sleep -Milliseconds 150
    [C]::keybd_event(0x28, 0, 2, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 200
    [C]::keybd_event(0x0D, 0, 0, [IntPtr]::Zero)   # Enter
    Start-Sleep -Milliseconds 120
    [C]::keybd_event(0x0D, 0, 2, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 900

    Shot $h "组合框-1-选完之后.png"
    $after = (Get-Content (Join-Path $PSScriptRoot 'RepRecorder.settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json).ParallelCount
    "  数量 $before → $after（说明确实改了选中项）"
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
}
finally {
    [C]::SetCursorPos($saved.X, $saved.Y) | Out-Null
    # 还原设置
    Start-Sleep -Milliseconds 300
    $p = Join-Path $PSScriptRoot 'RepRecorder.settings.json'
    $j = Get-Content $p -Raw -Encoding UTF8
    $j = $j -replace '"ParallelCount":\d+', ('"ParallelCount":' + $before)
    [System.IO.File]::WriteAllText($p, $j, (New-Object System.Text.UTF8Encoding($false)))
    "已还原 ParallelCount=$before，光标已归位"
}
