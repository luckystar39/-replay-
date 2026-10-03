Add-Type @"
using System;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack=4)] public struct PROPERTYKEY { public Guid fmtid; public int pid; }
[StructLayout(LayoutKind.Explicit)] public struct PROPVARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; }

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] public class MMDeviceEnumeratorComObject { }
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceEnumerator {
    int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection devices);
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    int GetDevice(string id, out IMMDevice device);
    int RegisterEndpointNotificationCallback(IntPtr c);
    int UnregisterEndpointNotificationCallback(IntPtr c);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceCollection { int GetCount(out int count); int Item(int index, out IMMDevice device); }
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDevice {
    int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetState(out int state);
}
[ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyStore {
    int GetCount(out int c); int GetAt(int i, out PROPERTYKEY key);
    int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value); int Commit();
}
[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionManager2 {
    int GetAudioSessionControl(ref Guid g, int f, out IntPtr s);
    int GetSimpleAudioVolume(ref Guid g, int f, out IntPtr v);
    int GetSessionEnumerator(out IAudioSessionEnumerator e);
    int RegisterSessionNotification(IntPtr n); int UnregisterSessionNotification(IntPtr n);
    int RegisterDuckNotification(string s, IntPtr d); int UnregisterDuckNotification(IntPtr d);
}
[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionEnumerator { int GetCount(out int c); int GetSession(int i, out IAudioSessionControl s); }
[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionControl {
    int GetState(out int state);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string v, ref Guid c);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string p);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string v, ref Guid c);
    int GetGroupingParam(out Guid g); int SetGroupingParam(ref Guid g, ref Guid c);
    int RegisterAudioSessionNotification(IntPtr n); int UnregisterAudioSessionNotification(IntPtr n);
}
[ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionControl2 {
    int GetState(out int state);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string v, ref Guid c);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string p);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string v, ref Guid c);
    int GetGroupingParam(out Guid g); int SetGroupingParam(ref Guid g, ref Guid c);
    int RegisterAudioSessionNotification(IntPtr n); int UnregisterAudioSessionNotification(IntPtr n);
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string s);
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string s);
    int GetProcessId(out uint pid);
    int IsSystemSoundsSession();
    int SetDuckingPreference(bool optOut);
}

public static class RouteProbe {
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pv);
    [DllImport("combase.dll", CharSet=CharSet.Unicode)] static extern int WindowsCreateString(string src, int len, out IntPtr h);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr h);
    [DllImport("combase.dll", CharSet=CharSet.Unicode)] static extern IntPtr WindowsGetStringRawBuffer(IntPtr h, out uint len);
    [DllImport("AudioSes.dll", CharSet=CharSet.Unicode)] static extern int DllGetActivationFactory(IntPtr classId, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetIIDsFn(IntPtr s, out uint c, out IntPtr i);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QueryInterfaceFn(IntPtr s, IntPtr iid, out IntPtr o);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetFn(IntPtr s, uint pid, int flow, int role, IntPtr dev);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetFn(IntPtr s, uint pid, int flow, int role, out IntPtr dev);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ClearFn(IntPtr s);

    static IntPtr factory = IntPtr.Zero;
    public static string InitError = null, Iid = null;

    public static bool Init() {
        try {
            IntPtr name;
            int hr = WindowsCreateString("Windows.Media.Internal.AudioPolicyConfig", 40, out name);
            if (hr != 0) { InitError = "CreateString 0x" + hr.ToString("X8"); return false; }
            IntPtr fac; hr = DllGetActivationFactory(name, out fac); WindowsDeleteString(name);
            if (hr != 0 || fac == IntPtr.Zero) { InitError = "DllGetActivationFactory 0x" + hr.ToString("X8"); return false; }
            IntPtr vt = Marshal.ReadIntPtr(fac);
            GetIIDsFn gi = (GetIIDsFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, IntPtr.Size*3), typeof(GetIIDsFn));
            uint cnt; IntPtr iids; hr = gi(fac, out cnt, out iids);
            if (hr != 0 || cnt == 0) { InitError = "GetIIDs 0x" + hr.ToString("X8"); return false; }
            IntPtr last = (IntPtr)((long)iids + 16*(cnt-1));
            byte[] b = new byte[16]; Marshal.Copy(last, b, 0, 16); Iid = new Guid(b).ToString();
            QueryInterfaceFn qi = (QueryInterfaceFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt), typeof(QueryInterfaceFn));
            IntPtr iface; hr = qi(fac, last, out iface);
            if (hr != 0 || iface == IntPtr.Zero) { InitError = "QI 0x" + hr.ToString("X8"); return false; }
            factory = iface; return true;
        } catch (Exception ex) { InitError = ex.GetType().Name + ": " + ex.Message; return false; }
    }
    public static int Set(uint pid, int role, string devId) {
        string s = devId == null ? "" : devId;
        IntPtr h; WindowsCreateString(s, s.Length, out h);
        IntPtr vt = Marshal.ReadIntPtr(factory);
        SetFn fn = (SetFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, IntPtr.Size*25), typeof(SetFn));
        int hr = fn(factory, pid, 0, role, h);
        WindowsDeleteString(h); return hr;
    }
    public static string Get(uint pid, int role, out int hrOut) {
        IntPtr vt = Marshal.ReadIntPtr(factory);
        GetFn fn = (GetFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, IntPtr.Size*26), typeof(GetFn));
        IntPtr h; int hr = fn(factory, pid, 0, role, out h); hrOut = hr;
        if (hr != 0 || h == IntPtr.Zero) return null;
        uint len; IntPtr buf = WindowsGetStringRawBuffer(h, out len);
        string s = Marshal.PtrToStringUni(buf, (int)len); WindowsDeleteString(h); return s;
    }
    public static int ClearAll() {
        IntPtr vt = Marshal.ReadIntPtr(factory);
        ClearFn fn = (ClearFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, IntPtr.Size*27), typeof(ClearFn));
        return fn(factory);
    }

    public static string Name(IMMDevice d) {
        IPropertyStore ps = null;
        try {
            if (d.OpenPropertyStore(0, out ps) != 0 || ps == null) return "?";
            PROPERTYKEY k = new PROPERTYKEY();
            k.fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"); k.pid = 14;
            PROPVARIANT v; if (ps.GetValue(ref k, out v) != 0) return "?";
            string s = (v.vt == 31 && v.p != IntPtr.Zero) ? Marshal.PtrToStringUni(v.p) : "?";
            PropVariantClear(ref v); return s;
        } catch { return "?"; }
        finally { if (ps != null) { try { Marshal.ReleaseComObject(ps); } catch {} } }
    }
    public static string Sess(IMMDevice d) {
        object mo = null; IAudioSessionManager2 mgr = null; IAudioSessionEnumerator en = null;
        try {
            Guid iid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
            if (d.Activate(ref iid, 23, IntPtr.Zero, out mo) != 0) return "activate失败";
            mgr = (IAudioSessionManager2)mo;
            if (mgr.GetSessionEnumerator(out en) != 0) return "枚举失败";
            int n; en.GetCount(out n);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < n; i++) {
                IAudioSessionControl c = null;
                try {
                    en.GetSession(i, out c); if (c == null) continue;
                    IAudioSessionControl2 c2 = c as IAudioSessionControl2; if (c2 == null) continue;
                    uint pid; c2.GetProcessId(out pid);
                    string pn = "?";
                    try { pn = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch {}
                    int st; c2.GetState(out st);
                    sb.Append(pn + "(pid=" + pid + ",st" + st + ") ");
                } catch {}
                finally { if (c != null) { try { Marshal.ReleaseComObject(c); } catch {} } }
            }
            return sb.ToString().Trim();
        } catch (Exception ex) { return "异常:" + ex.Message; }
        finally {
            if (en != null) { try { Marshal.ReleaseComObject(en); } catch {} }
            if (mgr != null) { try { Marshal.ReleaseComObject(mgr); } catch {} }
            if (mo != null && !object.ReferenceEquals(mo, mgr)) { try { Marshal.ReleaseComObject(mo); } catch {} }
        }
    }

    // 返回设备清单文本；同时把活动设备 id 存进 ActiveIds
    public static string[] ActiveIds = new string[0];
    public static string[] ActiveNames = new string[0];
    public static string DefId = null, DefName = null;

    public static string Dump() {
        StringBuilder sb = new StringBuilder();
        IMMDeviceEnumerator en = null; IMMDeviceCollection col = null;
        System.Collections.Generic.List<string> ids = new System.Collections.Generic.List<string>();
        System.Collections.Generic.List<string> nms = new System.Collections.Generic.List<string>();
        try {
            en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
            int hr = en.EnumAudioEndpoints(0, 1, out col);
            if (hr != 0) return "EnumAudioEndpoints 失败 0x" + hr.ToString("X8");
            int n; col.GetCount(out n);
            for (int i = 0; i < n; i++) {
                IMMDevice d = null;
                try {
                    col.Item(i, out d); if (d == null) continue;
                    string id; d.GetId(out id); string nm = Name(d);
                    ids.Add(id); nms.Add(nm);
                    sb.AppendLine("  [" + i + "] " + nm);
                    sb.AppendLine("        id=" + id);
                    sb.AppendLine("        会话: " + Sess(d));
                } finally { if (d != null) { try { Marshal.ReleaseComObject(d); } catch {} } }
            }
            IMMDevice dd = null;
            if (en.GetDefaultAudioEndpoint(0, 1, out dd) == 0 && dd != null) {
                dd.GetId(out DefId); DefName = Name(dd);
                sb.AppendLine("  默认输出(多媒体) = " + DefName + "  id=" + DefId);
                Marshal.ReleaseComObject(dd);
            }
        } catch (Exception ex) { sb.AppendLine("  异常: " + ex.Message); }
        finally {
            if (col != null) { try { Marshal.ReleaseComObject(col); } catch {} }
            if (en != null) { try { Marshal.ReleaseComObject(en); } catch {} }
        }
        ActiveIds = ids.ToArray(); ActiveNames = nms.ToArray();
        return sb.ToString();
    }

    // 某个设备上的会话（按 id）
    public static string SessById(string deviceId) {
        IMMDeviceEnumerator en = null; IMMDevice d = null;
        try {
            en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
            if (en.GetDevice(deviceId, out d) != 0 || d == null) return "取设备失败";
            return Sess(d);
        } catch (Exception ex) { return "异常:" + ex.Message; }
        finally {
            if (d != null) { try { Marshal.ReleaseComObject(d); } catch {} }
            if (en != null) { try { Marshal.ReleaseComObject(en); } catch {} }
        }
    }
    public static string SessDefault() { return SessById(DefId); }

    // 等某个 pid 的音频会话出现（在任意活动设备上），返回命中的设备序号
    public static int WaitForSession(int pid, int seconds) {
        for (int t = 0; t < seconds; t++) {
            IMMDeviceEnumerator en = null; IMMDeviceCollection col = null;
            try {
                en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
                if (en.EnumAudioEndpoints(0, 1, out col) == 0) {
                    int n; col.GetCount(out n);
                    for (int i = 0; i < n; i++) {
                        IMMDevice d = null;
                        try {
                            col.Item(i, out d); if (d == null) continue;
                            string s = Sess(d);
                            if (s.IndexOf("pid=" + pid + ",") >= 0) return i;
                        } finally { if (d != null) { try { Marshal.ReleaseComObject(d); } catch {} } }
                    }
                }
            } catch {} finally {
                if (col != null) { try { Marshal.ReleaseComObject(col); } catch {} }
                if (en != null) { try { Marshal.ReleaseComObject(en); } catch {} }
            }
            Thread.Sleep(1000);
        }
        return -1;
    }
}
"@