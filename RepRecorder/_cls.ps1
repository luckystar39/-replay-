# _cls.ps1 -- temporary: list the app's child windows so the harness can find the queue list.
Add-Type @"
using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;
public class CL {
  public delegate bool EP(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EP cb, IntPtr p);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
  public static List<IntPtr> K = new List<IntPtr>();
  public static void Go(IntPtr root){ K.Clear(); EnumChildWindows(root, delegate(IntPtr h, IntPtr p){ K.Add(h); return true; }, IntPtr.Zero); }
  public static string C(IntPtr h){ StringBuilder s=new StringBuilder(256); GetClassName(h,s,256); return s.ToString(); }
  public static string T(IntPtr h){ StringBuilder s=new StringBuilder(256); GetWindowText(h,s,256); return s.ToString(); }
  public static bool V(IntPtr h){ return IsWindowVisible(h); }
  public static int[] R2(IntPtr h){ R r; GetWindowRect(h,out r); return new int[]{r.L,r.T,r.Ri-r.L,r.B-r.T}; }
}
"@
$p = Get-Process RepRecorder -ErrorAction SilentlyContinue
if (-not $p) { "RepRecorder not running"; exit }
$h = $p.MainWindowHandle
"main hwnd = $h"
""
"--- top level windows of the process ---"
$all = Get-Process -Id $p.Id
foreach ($w in [CL]::K) { }
[CL]::Go([IntPtr]::Zero) | Out-Null
""
"--- children of main ---"
[CL]::Go($h)
"count = " + [CL]::K.Count
foreach ($c in [CL]::K) {
  $r = [CL]::R2($c)
  "{0,-22} vis={1,-5} {2,5},{3,5} {4,5}x{5,-5}  [{6}]" -f [CL]::C($c), [CL]::V($c), $r[0], $r[1], $r[2], $r[3], [CL]::T($c)
}
