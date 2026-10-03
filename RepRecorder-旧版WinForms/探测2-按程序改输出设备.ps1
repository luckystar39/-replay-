# 探测 v2：用真实端点ID + 确认游戏真的有音频会话后再 Set
$root='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123'
$rep='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123\replay\【联机对战Rep】\[Aquila(3) ]\26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep'

Add-Type @"
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack=4)]
public struct PROPERTYKEY { public Guid fmtid; public int pid; }
[StructLayout(LayoutKind.Explicit)]
public struct PROPVARIANT {
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(8)] public IntPtr p;
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] public class MMDeviceEnumeratorComObject { }
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceEnumerator {
    int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection devices);
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    int GetDevice(string id, out IMMDevice device);
    int RegisterEndpointNotificationCallback(IntPtr client);
    int UnregisterEndpointNotificationCallback(IntPtr client);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceCollection {
    int GetCount(out int count);
    int Item(int index, out IMMDevice device);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDevice {
    int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetState(out int state);
}
[ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyStore {
    int GetCount(out int c);
    int GetAt(int i, out PROPERTYKEY key);
    int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
    int Commit();
}
[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionManager2 {
    int GetAudioSessionControl(ref Guid AudioSessionGuid, int StreamFlags, out IntPtr SessionControl);
    int GetSimpleAudioVolume(ref Guid AudioSessionGuid, int StreamFlags, out IntPtr AudioVolume);
    int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
    int RegisterSessionNotification(IntPtr SessionNotification);
    int UnregisterSessionNotification(IntPtr SessionNotification);
    int RegisterDuckNotification(string sessionID, IntPtr duckNotification);
    int UnregisterDuckNotification(IntPtr duckNotification);
}
[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionEnumerator {
    int GetCount(out int SessionCount);
    int GetSession(int SessionCount, out IAudioSessionControl Session);
}
[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionControl {
    int GetState(out int state);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    int GetGroupingParam(out Guid groupingParam);
    int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
    int RegisterAudioSessionNotification(IntPtr client);
    int UnregisterAudioSessionNotification(IntPtr client);
}
[ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionControl2 {
    int GetState(out int state);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    int GetGroupingParam(out Guid groupingParam);
    int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
    int RegisterAudioSessionNotification(IntPtr client);
    int UnregisterAudioSessionNotification(IntPtr client);
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);
    int GetProcessId(out uint retVal);
    int IsSystemSoundsSession();
    int SetDuckingPreference(bool optOut);
}

public static class AudioProbe {
    [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PROPVARIANT pv);

    public static string DeviceName(IMMDevice dev) {
        IPropertyStore ps = null;
        try {
            if (dev.OpenPropertyStore(0, out ps) != 0 || ps == null) return "?";
            PROPERTYKEY k = new PROPERTYKEY();
            k.fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"); k.pid = 14;   // PKEY_Device_FriendlyName
            PROPVARIANT v;
            if (ps.GetValue(ref k, out v) != 0) return "?";
            string s = (v.vt == 31) ? Marshal.PtrToStringUni(v.p) : "?";
            PropVariantClear(ref v);
            return s;
        } catch { return "?"; }
        finally { if (ps != null) { try { Marshal.ReleaseComObject(ps); } catch {} } }
    }

    // 返回「该设备上有哪些进程的音频会话」
    public static string SessionsOn(IMMDevice dev) {
        object mo = null; IAudioSessionManager2 mgr = null; IAudioSessionEnumerator en = null;
        try {
            Guid iid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
            if (dev.Activate(ref iid, 23, IntPtr.Zero, out mo) != 0) return "activate失败";
            mgr = (IAudioSessionManager2)mo;
            if (mgr.GetSessionEnumerator(out en) != 0) return "枚举失败";
            int n; en.GetCount(out n);
            string s = "";
            for (int i = 0; i < n; i++) {
                IAudioSessionControl c = null;
                try {
                    en.GetSession(i, out c);
                    if (c == null) continue;
                    IAudioSessionControl2 c2 = c as IAudioSessionControl2;
                    if (c2 == null) continue;
                    uint pid; c2.GetProcessId(out pid);
                    string pn = "?";
                    try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch {}
                    int st; c2.GetState(out st);
                    s += pn + "(pid=" + pid + ",状态" + st + ") ";
                } catch {}
                finally { if (c != null) { try { Marshal.ReleaseComObject(c); } catch {} } }
            }
            return s.Trim();
        } catch (Exception ex) { return "异常:" + ex.Message; }
        finally {
            if (en != null) { try { Marshal.ReleaseComObject(en); } catch {} }
            if (mgr != null) { try { Marshal.ReleaseComObject(mgr); } catch {} }
            if (mo != null && !object.ReferenceEquals(mo, mgr)) { try { Marshal.ReleaseComObject(mo); } catch {} }
        }
    }
}

public static class AudioPolicy {
    [DllImport("combase.dll", CharSet=CharSet.Unicode)] static extern int WindowsCreateString(string src, int len, out IntPtr hstr);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr hstr);
    [DllImport("combase.dll", CharSet=CharSet.Unicode)] static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstr, out uint len);
    [DllImport("AudioSes.dll", CharSet=CharSet.Unicode)] static extern int DllGetActivationFactory(IntPtr classId, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetIIDsFn(IntPtr self, out uint count, out IntPtr iids);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QueryInterfaceFn(IntPtr self, IntPtr iid, out IntPtr obj);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetEndpointFn(IntPtr self, uint pid, int flow, int role, IntPtr deviceId);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetEndpointFn(IntPtr self, uint pid, int flow, int role, out IntPtr deviceId);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ClearAllFn(IntPtr self);

    static IntPtr factory = IntPtr.Zero;
    public static string InitError = null, Iid = null;

    public static bool Init() {
        try {
            IntPtr name;
            int hr = WindowsCreateString("Windows.Media.Internal.AudioPolicyConfig", 40, out name);
            if (hr != 0) { InitError = "CreateString 0x" + hr.ToString("X8"); return false; }
            IntPtr fac;
            hr = DllGetActivationFactory(name, out fac);
            WindowsDeleteString(name);
            if (hr != 0 || fac == IntPtr.Zero) { InitError = "DllGetActivationFactory 0x" + hr.ToString("X8"); return false; }
            IntPtr vt = Marshal.ReadIntPtr(fac);
            var getIids = (GetIIDsFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, IntPtr.Size*3), typeof(GetIIDsFn));
            uint count; IntPtr iids;
            hr = getIids(fac, out count, out iids);
            if (hr != 0 || count == 0) { InitError = "GetIIDs 0x" + hr.ToString("X8"); return false; }
            IntPtr lastIid = (IntPtr)((long)iids + 16 * (count - 1));
            byte[] b = new byte[16]; Marshal.Copy(lastIid, b, 0, 16);
            Iid = new Guid(b).ToString();
            var qi = (QueryInterfaceFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt), typeof(QueryInterfaceFn));
            IntPtr iface;
            hr = qi(fac, lastIid, out iface);
            if (hr != 0 || iface == IntPtr.Zero) { InitError = "QI 0x" + hr.ToString("X8"); return false; }
            factory = iface;
            return true;
        } catch (Exception ex) { InitError = ex.GetType().Name + ": " + ex.Message; return false; }
    }
    public static int Set(uint pid, int flow, int role, string deviceId) {
        string s = deviceId ?? "";
        IntPtr h; WindowsCreateString(s, s.Length, out h);
        IntPtr vt = Marshal.ReadIntPtr(factory);
        var fn = (SetEndpointFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, IntPtr.Size*25), typeof(SetEndpointFn));
        int hr = fn(factory, pid, flow, role, h);
        WindowsDeleteString(h);
        return hr;
    }
    public static string Get(uint pid, int flow, int role, out int hrOut) {
        IntPtr vt = Marshal.ReadIntPtr(factory);
        var fn = (GetEndpointFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, IntPtr.Size*26), typeof(GetEndpointFn));
        IntPtr h;
        int hr = fn(factory, pid, flow, role, out h);
        hrOut = hr;
        if (hr != 0 || h == IntPtr.Zero) return null;
        uint len; IntPtr buf = WindowsGetStringRawBuffer(h, out len);
        string s = Marshal.PtrToStringUni(buf, (int)len);
        WindowsDeleteString(h);
        return s;
    }
    public static int ClearAll() {
        IntPtr vt = Marshal.ReadIntPtr(factory);
        var fn = (ClearAllFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, IntPtr.Size*27), typeof(ClearAllFn));
        return fn(factory);
    }
}
"@

"===== 1) 活动输出设备（真实 WASAPI 端点ID）====="
$en = New-Object MMDeviceEnumeratorComObject
$col = $null
$hr = ([IMMDeviceEnumerator]$en).EnumAudioEndpoints(0, 1, [ref]$col)   # eRender, DEVICE_STATE_ACTIVE
$n = 0; $col.GetCount([ref]$n)
$devs = @()
for($i=0;$i -lt $n;$i++){
  $d = $null; $col.Item($i, [ref]$d)
  $id = $null; $d.GetId([ref]$id)
  $nm = [AudioProbe]::DeviceName($d)
  $devs += [pscustomobject]@{ Name=$nm; Id=$id; Dev=$d }
  "  [$i] $nm`n      $id"
}
$def = $null; ([IMMDeviceEnumerator]$en).GetDefaultAudioEndpoint(0,1,[ref]$def)
$defId = $null; $def.GetId([ref]$defId); $defName=[AudioProbe]::DeviceName($def)
"  默认设备(角色=多媒体) = $defName`n      $defId"

"===== 2) 取非公开接口 ====="
if(-not [AudioPolicy]::Init()){ "  ✗ $([AudioPolicy]::InitError)"; exit 1 }
"  ✅ IID = $([AudioPolicy]::Iid)"

"===== 3) 起游戏放 rep，等它真的出声（默认设备上出现 th123 会话）====="
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 2
$p=Start-Process -FilePath (Join-Path $root 'th123.exe') -ArgumentList ('"'+$rep+'"') -WorkingDirectory $root -PassThru
$found=$false
for($t=1;$t -le 20;$t++){
  Start-Sleep -Seconds 2
  $sess=[AudioProbe]::SessionsOn($def)
  if($sess -match 'th123'){ "  第 $($t*2) 秒：默认设备上有 th123 会话 → $sess"; $found=$true; break }
  if($t % 5 -eq 0){ "  第 $($t*2) 秒：默认设备会话 = $sess" }
}
if(-not $found){ "  ⚠ 默认设备上一直没看到 th123 会话（游戏可能在菜单/无声）" }
$p.Refresh(); "  游戏 PID=$($p.Id) 退出=$($p.HasExited)"

"===== 4) 给它换成另一个活动设备 ====="
$spare = $devs | Where-Object { $_.Id -ne $defId } | Select-Object -First 1
"  目标设备 = $($spare.Name)`n      $($spare.Id)"
for($r=0;$r -le 2;$r++){
  $hr=[AudioPolicy]::Set([uint32]$p.Id, 0, $r, $spare.Id)
  "  Set(role=$r) → 0x{0:X8}" -f $hr
}
$h=0; $got=[AudioPolicy]::Get([uint32]$p.Id, 0, 0, [ref]$h)
"  回读(role=0) → 0x{0:X8}  值={1}  与目标一致={2}" -f $h, $got, ($got -eq $spare.Id)

"===== 5) 看音频会话有没有真的搬家 ====="
"  默认设备上的会话 : $([AudioProbe]::SessionsOn($def))"
"  目标设备上的会话 : $([AudioProbe]::SessionsOn($spare.Dev))"

"===== 6) 还原（设回默认设备ID）====="
for($r=0;$r -le 2;$r++){ $hr=[AudioPolicy]::Set([uint32]$p.Id, 0, $r, $defId); "  Set(默认, role=$r) → 0x{0:X8}" -f $hr }
$h=0; $got=[AudioPolicy]::Get([uint32]$p.Id, 0, 0, [ref]$h)
"  回读 → 0x{0:X8}  值={1}  与默认一致={2}" -f $h, $got, ($got -eq $defId)
$p.Refresh(); if(-not $p.HasExited){ $p.Kill() }
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
"===== 探测结束 ====="
