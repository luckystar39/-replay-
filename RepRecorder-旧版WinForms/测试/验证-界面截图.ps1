# 验证-界面截图.ps1
# 启动 RepRecorder，等窗口出来，用 PrintWindow(h, dc, 2) 把整窗截图存到 _看片\。
# 用法：powershell -File 验证-界面截图.ps1 [输出文件名]
param([string]$Out = "界面-合并版.png")

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Cap {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    public static int W(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.R - r.L; }
    public static int H(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.B - r.T; }
    public static void Shot(IntPtr h, IntPtr dc) { PrintWindow(h, dc, 2); }
    public static void Focus(IntPtr h) { SetForegroundWindow(h); }
}
"@

$exe = Join-Path $PSScriptRoot 'RepRecorder.exe'
$dir = Join-Path $PSScriptRoot '_看片'
if (-not (Test-Path $dir)) { New-Item -ItemType Directory $dir | Out-Null }
$outPath = Join-Path $dir $Out

Get-Process RepRecorder -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
Start-Sleep -Milliseconds 500

$p = Start-Process -FilePath $exe -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 300
    $p.Refresh()
    if ($p.HasExited) { "程序已退出，ExitCode=$($p.ExitCode)"; exit 1 }
    if ($p.MainWindowHandle -ne 0) { $h = $p.MainWindowHandle; break }
}
if ($h -eq [IntPtr]::Zero) { "没等到主窗口"; $p.Kill(); exit 1 }

Start-Sleep -Seconds 2          # 等 Shown 里的布局/主题都跑完
[Cap]::Focus($h) | Out-Null
Start-Sleep -Milliseconds 400

$w = [Cap]::W($h); $ht = [Cap]::H($h)
"窗口尺寸: ${w} x ${ht}"
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
[Cap]::Shot($h, $dc) | Out-Null
$g.ReleaseHdc($dc)
$g.Dispose()
$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"已保存: $outPath"

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
