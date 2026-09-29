# 探测 v5：两种激活方式 × 四种 deviceId 写法，全都在「自己的进程」上试
. 'D:\【完整包】非想天则整合包2026-06-15\RepRecorder\探针库-音频路由.ps1'

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class AudioPolicyAlt {
    [DllImport("combase.dll", CharSet=CharSet.Unicode)] static extern int WindowsCreateString(string src, int len, out IntPtr h);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr h);
    [DllImport("combase.dll", CharSet=CharSet.Unicode)] static extern IntPtr WindowsGetStringRawBuffer(IntPtr h, out uint len);
    [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
    [DllImport("AudioSes.dll", CharSet=CharSet.Unicode)] static extern int DllGetActivationFactory(IntPtr classId, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetIIDsFn(IntPtr s, out uint c, out IntPtr i);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QueryInterfaceFn(IntPtr s, IntPtr iid, out IntPtr o);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetFn(IntPtr s, uint pid, int flow, int role, IntPtr dev);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetFn(IntPtr s, uint pid, int flow, int role, out IntPtr dev);

    static IntPtr factory = IntPtr.Zero;
    public static string Which = null;
    public static string Error = null;
    static readonly Guid IID = new Guid("ab3d4648-e242-459f-b02f-541c70306324");

    static IntPtr HS(string s) {
        IntPtr h; WindowsCreateString(s == null ? "" : s, (s == null ? "" : s).Length, out h); return h;
    }
    static IntPtr Slot(int idx) {
        IntPtr vt = Marshal.ReadIntPtr(factory);
        return Marshal.ReadIntPtr(vt, IntPtr.Size * idx);
    }

    // 方式A：DllGetActivationFactory + GetIIDs + QI（我原来的做法）
    public static bool InitA() {
        try {
            IntPtr name; int hr = WindowsCreateString("Windows.Media.Internal.AudioPolicyConfig", 40, out name);
            IntPtr fac; hr = DllGetActivationFactory(name, out fac); WindowsDeleteString(name);
            if (hr != 0 || fac == IntPtr.Zero) { Error = "DllGetActivationFactory 0x" + hr.ToString("X8"); return false; }
            IntPtr vt = Marshal.ReadIntPtr(fac);
            GetIIDsFn gi = (GetIIDsFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, IntPtr.Size*3), typeof(GetIIDsFn));
            uint cnt; IntPtr iids; hr = gi(fac, out cnt, out iids);
            IntPtr last = (IntPtr)((long)iids + 16*(cnt-1));
            QueryInterfaceFn qi = (QueryInterfaceFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt), typeof(QueryInterfaceFn));
            IntPtr iface; hr = qi(fac, last, out iface);
            if (hr != 0) { Error = "QI 0x" + hr.ToString("X8"); return false; }
            factory = iface; Which = "A(DllGetActivationFactory+QI)"; return true;
        } catch (Exception ex) { Error = ex.Message; return false; }
    }
    // 方式B：RoGetActivationFactory(name, iid)（EarTrumpet 的做法）
    public static bool InitB() {
        try {
            IntPtr name; WindowsCreateString("Windows.Media.Internal.AudioPolicyConfig", 40, out name);
            Guid iid = IID; IntPtr fac;
            int hr = RoGetActivationFactory(name, ref iid, out fac); WindowsDeleteString(name);
            if (hr != 0 || fac == IntPtr.Zero) { Error = "RoGetActivationFactory 0x" + hr.ToString("X8"); return false; }
            factory = fac; Which = "B(RoGetActivationFactory)"; return true;
        } catch (Exception ex) { Error = ex.Message; return false; }
    }
    public static int Set(uint pid, int flow, int role, string devId) {
        IntPtr h = HS(devId);
        SetFn fn = (SetFn)Marshal.GetDelegateForFunctionPointer(Slot(25), typeof(SetFn));
        int hr = fn(factory, pid, flow, role, h);
        WindowsDeleteString(h); return hr;
    }
    public static string Get(uint pid, int flow, int role, out int hrOut) {
        GetFn fn = (GetFn)Marshal.GetDelegateForFunctionPointer(Slot(26), typeof(GetFn));
        IntPtr h; int hr = fn(factory, pid, flow, role, out h); hrOut = hr;
        if (hr != 0 || h == IntPtr.Zero) return null;
        uint len; IntPtr buf = WindowsGetStringRawBuffer(h, out len);
        string s = Marshal.PtrToStringUni(buf, (int)len); WindowsDeleteString(h); return s;
    }
}
"@

"===== 设备清单 ====="
"$([RouteProbe]::Dump())"
$spareIdx=0; for($i=0;$i -lt [RouteProbe]::ActiveIds.Count;$i++){ if([RouteProbe]::ActiveIds[$i] -ne [RouteProbe]::DefId){ $spareIdx=$i; break } }
$wasapiId=[RouteProbe]::ActiveIds[$spareIdx]; $spareName=[RouteProbe]::ActiveNames[$spareIdx]
$guid = $wasapiId.Substring($wasapiId.IndexOf('}.')+2)
$forms = [ordered]@{
  'WASAPI端点ID'      = $wasapiId
  'SWD完整路径'       = "\\?\SWD#MMDEVAPI#$wasapiId#{e6327cad-dcec-4949-ae8a-991e976a79d2}"
  '光GUID'            = $guid
  'SWD不带接口GUID'   = "\\?\SWD#MMDEVAPI#$wasapiId"
  '空串'              = ""
}
"  目标设备 = $spareName"
$forms.GetEnumerator() | ForEach-Object { "     [$($_.Key)] $($_.Value)" }

"===== 让『我自己』(pid=$PID) 出声 ====="
$sp = New-Object System.Media.SoundPlayer 'C:\Windows\Media\Alarm01.wav'
$sp.PlayLooping(); Start-Sleep -Seconds 3
"  默认设备会话: $([RouteProbe]::SessDefault())"

foreach($route in @('A','B')){
  "===== 激活方式 $route ====="
  $ok = if($route -eq 'A'){ [AudioPolicyAlt]::InitA() } else { [AudioPolicyAlt]::InitB() }
  if(-not $ok){ "  ✗ $([AudioPolicyAlt]::Error)"; continue }
  "  ✅ 激活成功：$([AudioPolicyAlt]::Which)"
  foreach($f in $forms.GetEnumerator()){
    $hr=[AudioPolicyAlt]::Set([uint32]$PID, 0, 0, $f.Value)
    $h=0; $got=[AudioPolicyAlt]::Get([uint32]$PID, 0, 0, [ref]$h)
    "  Set([$($f.Key)]) → 0x{0:X8}   回读=「{1}」" -f $hr, $got
  }
}

"===== 会话有没有动 ====="
Start-Sleep -Seconds 2
$spareId2=[RouteProbe]::ActiveIds[$spareIdx]
"  默认设备: $([RouteProbe]::SessDefault())"
"  目标设备: $([RouteProbe]::SessById($spareId2))"
$sp.Stop()
"===== 结束 ====="
