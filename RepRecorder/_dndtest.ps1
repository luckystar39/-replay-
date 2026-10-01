# _dndtest.ps1 -- temporary: just checks whether the synthesized OLE drop lands at all.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;
public class DT {
  public delegate bool EP(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EP cb, IntPtr p);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int i);
  [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
  public static List<IntPtr> K = new List<IntPtr>();
  public static void Go(IntPtr root){ K.Clear(); EnumChildWindows(root, delegate(IntPtr h, IntPtr p){ K.Add(h); return true; }, IntPtr.Zero); }
  public static string C(IntPtr h){ StringBuilder s=new StringBuilder(256); GetClassName(h,s,256); return s.ToString(); }
  public static int[] RC(IntPtr h){ R r; GetWindowRect(h,out r); return new int[]{r.L,r.T,r.Ri-r.L,r.B-r.T}; }
  [DllImport("user32.dll", EntryPoint="SendMessageW")] public static extern IntPtr WinMsg(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static double Scale(){ IntPtr dc=GetDC(IntPtr.Zero); int p=GetDeviceCaps(dc,118); int v=GetSystemMetrics(0); ReleaseDC(IntPtr.Zero,dc); return (double)p/v; }
}
"@
$out = Join-Path $env:TEMP 'rrrepro'
$reps = @(Get-Content (Join-Path $out 'reps.txt') -Encoding UTF8 | Where-Object { $_.Trim() -ne '' })

Get-Process RepRecorder -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
Start-Sleep -Milliseconds 700
$p = Start-Process -FilePath (Join-Path $PSScriptRoot 'RepRecorder.exe') -PassThru
for ($i = 0; $i -lt 80; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh(); if ($p.MainWindowHandle -ne 0) { break } }
Start-Sleep -Seconds 5
$h = $p.MainWindowHandle
[DT]::Go($h)
$lv = [IntPtr]::Zero
foreach ($c in [DT]::K) { if ([DT]::C($c) -like '*SysListView32*') { $lv = $c; break } }
$lr = [DT]::RC($lv)
$s = [DT]::Scale()
$cx = [int](($lr[0] + $lr[2]/2) * $s)
$cy = [int](($lr[1] + $lr[3]*0.5) * $s)
"list virtual = $($lr[0]),$($lr[1]) $($lr[2])x$($lr[3]) ; scale=$s ; drop point = $cx,$cy"
"items before = " + [DT]::WinMsg($lv, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
& (Join-Path $PSScriptRoot '_dnd.exe') $cx $cy @reps
"dnd exit code = $LASTEXITCODE"
Start-Sleep -Seconds 2
"items after  = " + [DT]::WinMsg($lv, 0x1004, [IntPtr]::Zero, [IntPtr]::Zero)
Get-Process RepRecorder -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
