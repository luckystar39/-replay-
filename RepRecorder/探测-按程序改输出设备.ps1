# 探测：Windows 非公开 API「按程序设置输出设备」在这台机器上能不能用
$root='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123'
$rep='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123\replay\【联机对战Rep】\[Aquila(3) ]\26-08-30_15-55-29_Aquila(3) (衣玖)_Ciel(红美铃)_(0︰0_无).rep'
"===== 1) 枚举输出设备（读注册表：名字 / 端点ID / 状态）====="
$base='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render'
$devs=@()
Get-ChildItem -Path $base -ErrorAction SilentlyContinue | ForEach-Object {
  $p = Join-Path $_.PSPath 'Properties'
  $props = Get-ItemProperty -Path $p -ErrorAction SilentlyContinue
  $state = (Get-ItemProperty -Path $_.PSPath -ErrorAction SilentlyContinue).DeviceState
  $name = $props.'{a45c254e-df1c-4efd-8020-67d146a850e0},2'
  $id   = $props.'{b3f8fa53-0004-438e-9003-51a46e139bfc},6'
  $devs += [pscustomobject]@{ Name=$name; Id=$id; State=$state; Key=$_.PSChildName }
}
$devs | ForEach-Object { "  [{0}] {1}`n        id={2}`n        key={3}" -f $(if($_.State -eq 1){'活动'}else{"状态$($_.State)"}), $_.Name, $_.Id, $_.Key }

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class AudioPolicy {
    [DllImport("combase.dll", CharSet=CharSet.Unicode)] static extern int WindowsCreateString(string src, int len, out IntPtr hstr);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr hstr);
    [DllImport("combase.dll", CharSet=CharSet.Unicode)] static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstr, out uint len);
    [DllImport("AudioSes.dll", CharSet=CharSet.Unicode)] static extern int DllGetActivationFactory(IntPtr classId, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetIIDsFn(IntPtr self, out uint count, out IntPtr iids);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QueryInterfaceFn(IntPtr self, IntPtr iid, out IntPtr obj);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetEndpointFn(IntPtr self, uint pid, int flow, int role, IntPtr deviceId);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetEndpointFn(IntPtr self, uint pid, int flow, int role, out IntPtr deviceId);

    static IntPtr factory = IntPtr.Zero;
    static byte[] iidBytes = new byte[16];
    public static string InitError = null;
    public static string Iid = null;

    public static bool Init() {
        try {
            IntPtr name;
            int hr = WindowsCreateString("Windows.Media.Internal.AudioPolicyConfig", 40, out name);
            if (hr != 0) { InitError = "WindowsCreateString hr=0x" + hr.ToString("X8"); return false; }
            IntPtr fac;
            hr = DllGetActivationFactory(name, out fac);
            WindowsDeleteString(name);
            if (hr != 0 || fac == IntPtr.Zero) { InitError = "DllGetActivationFactory hr=0x" + hr.ToString("X8"); return false; }
            IntPtr vt = Marshal.ReadIntPtr(fac);
            IntPtr getIidsPtr = Marshal.ReadIntPtr(vt, IntPtr.Size * 3);
            var getIids = (GetIIDsFn)Marshal.GetDelegateForFunctionPointer(getIidsPtr, typeof(GetIIDsFn));
            uint count; IntPtr iids;
            hr = getIids(fac, out count, out iids);
            if (hr != 0) { InitError = "GetIIDs hr=0x" + hr.ToString("X8"); return false; }
            if (count == 0) { InitError = "GetIIDs count=0"; return false; }
            IntPtr lastIid = (IntPtr)((long)iids + 16 * (count - 1));
            Marshal.Copy(lastIid, iidBytes, 0, 16);
            Iid = new Guid(iidBytes).ToString();
            var qi = (QueryInterfaceFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt), typeof(QueryInterfaceFn));
            IntPtr iface;
            hr = qi(fac, lastIid, out iface);
            if (hr != 0 || iface == IntPtr.Zero) { InitError = "QueryInterface hr=0x" + hr.ToString("X8"); return false; }
            factory = iface;
            return true;
        } catch (Exception ex) { InitError = ex.GetType().Name + ": " + ex.Message; return false; }
    }
    public static int Set(uint pid, int flow, int role, string deviceId) {
        IntPtr h; WindowsCreateString(deviceId ?? "", (deviceId ?? "").Length, out h);
        IntPtr vt = Marshal.ReadIntPtr(factory);
        var fn = (SetEndpointFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, IntPtr.Size * 25), typeof(SetEndpointFn));
        int hr = fn(factory, pid, flow, role, h);
        WindowsDeleteString(h);
        return hr;
    }
    public static string Get(uint pid, int flow, int role, out int hrOut) {
        IntPtr vt = Marshal.ReadIntPtr(factory);
        var fn = (GetEndpointFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, IntPtr.Size * 26), typeof(GetEndpointFn));
        IntPtr h;
        int hr = fn(factory, pid, flow, role, out h);
        hrOut = hr;
        if (hr != 0 || h == IntPtr.Zero) return null;
        uint len; IntPtr buf = WindowsGetStringRawBuffer(h, out len);
        string s = Marshal.PtrToStringUni(buf, (int)len);
        WindowsDeleteString(h);
        return s;
    }
}
"@
"===== 2) 取非公开接口 ====="
if(-not [AudioPolicy]::Init()){ "  ✗ 初始化失败：$([AudioPolicy]::InitError)"; exit 1 }
"  ✅ 拿到接口，IID = $([AudioPolicy]::Iid)"

"===== 3) 起一个放 rep 的游戏，给它改输出设备 ====="
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 2
$p=Start-Process -FilePath (Join-Path $root 'th123.exe') -ArgumentList ('"'+$rep+'"') -WorkingDirectory $root -PassThru
Start-Sleep -Seconds 14
$p.Refresh()
"  游戏 PID = $($p.Id)  退出=$($p.HasExited)"
# 找当前默认设备（作为对照）与备用设备（非默认、活动的）
$active = $devs | Where-Object { $_.State -eq 1 -and $_.Id }
"  活动输出设备 $($active.Count) 个："
$active | ForEach-Object { "     - $($_.Name)" }
$spare = $active | Where-Object { $_.Name -match 'HDMI|Display|NVIDIA|AMD|Intel|数字输出|Digital' } | Select-Object -First 1
if(-not $spare){ $spare = $active | Select-Object -Last 1 }
"  选来做实验的『备用设备』= $($spare.Name)"

foreach($role in @(0,1,2)){
  $hr=[AudioPolicy]::Set([uint32]$p.Id, 0, $role, $spare.Id)
  "  Set(role=$role) → hr=0x{0:X8}" -f $hr
}
$h2=0; $got=[AudioPolicy]::Get([uint32]$p.Id, 0, 0, [ref]$h2)
"  回读 Get(role=0) → hr=0x{0:X8}  值={1}" -f $h2, $got
"  回读值是否等于备用设备ID: " + ($got -eq $spare.Id)

"===== 4) 杀掉游戏、重新起一个，看设置是否持久 ====="
$p.Kill(); Start-Sleep 3
$p2=Start-Process -FilePath (Join-Path $root 'th123.exe') -ArgumentList ('"'+$rep+'"') -WorkingDirectory $root -PassThru
Start-Sleep -Seconds 14
$h3=0; $got2=[AudioPolicy]::Get([uint32]$p2.Id, 0, 0, [ref]$h3)
"  新进程 Get(role=0) → hr=0x{0:X8}  值={1}" -f $h3, $got2
"  是否仍是备用设备ID: " + ($got2 -eq $spare.Id)

"===== 5) 试着把设置清掉（Set 空字符串）====="
$hr=[AudioPolicy]::Set([uint32]$p2.Id, 0, 0, "")
"  Set('') → hr=0x{0:X8}" -f $hr
$h4=0; $got3=[AudioPolicy]::Get([uint32]$p2.Id, 0, 0, [ref]$h4)
"  清完回读 → hr=0x{0:X8}  值=「{1}」" -f $h4, $got3
$p2.Kill()
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
"===== 探测结束 ====="
