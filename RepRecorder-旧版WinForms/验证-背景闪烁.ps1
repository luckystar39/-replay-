# _flash2.ps1 -- 最大化后在不同时刻各截一张窗口图，直接看背景图有没有"消失一下"。
# 每次重建的窗口只有 100~300ms，所以要在最大化后马上连拍。
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class F2 {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
    public static int W(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.R-r.L; }
    public static int H(IntPtr h){ RECT r; GetWindowRect(h, out r); return r.B-r.T; }
}
"@ -ReferencedAssemblies System.Drawing, System.Windows.Forms

$name = if ($args.Count -ge 1) { $args[0] } else { 'RepRecorder.exe' }
$exe = Join-Path $PSScriptRoot $name
$tag = [System.IO.Path]::GetFileNameWithoutExtension($name)
$out = Join-Path $PSScriptRoot '_看片\_flash'
if (!(Test-Path $out)) { New-Item -ItemType Directory -Path $out | Out-Null }

Get-Process RepRecorder -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
Start-Sleep -Milliseconds 700
$p = Start-Process -FilePath $exe -PassThru
for ($i = 0; $i -lt 60; $i++) { Start-Sleep -Milliseconds 300; $p.Refresh(); if ($p.MainWindowHandle -ne 0) { break } }
Start-Sleep -Seconds 4
$h = $p.MainWindowHandle
[F2]::ShowWindow($h, 9) | Out-Null
Start-Sleep -Milliseconds 600
[F2]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 1000, 620, 0x0004) | Out-Null
Start-Sleep -Milliseconds 900
[F2]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 700

# 复现用户的场景：先最大化（他的常态）→ 最小化 → 从任务栏恢复最大化
[F2]::ShowWindow($h, 3) | Out-Null
Start-Sleep -Seconds 2
[F2]::ShowWindow($h, 6) | Out-Null      # SW_MINIMIZE
Start-Sleep -Seconds 2

"$tag : 从最小化恢复+最大化，然后在 0/30/60/100/150/200/260/350/500/1200 ms 各截一张"
$sw = [System.Diagnostics.Stopwatch]::StartNew()
[F2]::ShowWindow($h, 3) | Out-Null       # 任务栏那个最大化按钮就是这个效果
$marks = @(0,30,60,100,150,200,260,350,500,1200)
$prev = 0
foreach ($m in $marks) {
    while ($sw.ElapsedMilliseconds -lt $m) { Start-Sleep -Milliseconds 5 }
    $w = [F2]::W($h); $ht = [F2]::H($h)
    if ($w -le 0 -or $ht -le 0) { continue }
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc(); [F2]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
    $bmp.Save((Join-Path $out ("$tag-" + $m.ToString('0000') + "ms.png")), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "   ${m}ms 处已截图  (窗口 ${w}x${ht})"
}
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
"done -> $out"
