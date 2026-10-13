// 选文件夹对话框（2026-10-12）。
//
// 为什么自己写：脱胎那一轮把 System.Windows.Forms 整个断掉了，而 .NET Framework 的 WPF 里
// 【没有】文件夹选择器（.NET Core 的 OpenFolderDialog 是后来才有的）。
// 以前用的是 WinForms 的 FolderBrowserDialog —— 它本身也只是 Win32 SHBrowseForFolder 的皮，
// 长得还是 Windows XP 那套树。这里直接调 Vista 起的 IFileOpenDialog + FOS_PICKFOLDERS，
// 长得跟"另存为"是一套，跟系统里别的选择器一致。
//
// 出错就返回 null（= 用户取消）：调用方本来就只判 null，不该让互操作的问题变成一个
// 用户看不懂的异常弹窗。
//
// ⚠ 这份 COM 声明的 vtable 顺序必须与 shobjidl.h 一致 —— IFileDialog 的方法顺序不是
//   "按拼音排"的，中间插一个方法，后面所有调用的槽位就全错位（表现为莫名其妙地弹出、
//   或者崩在 shell32 里）。要加方法只能加在末尾（SetFilter/GetResults/GetSelectedItems 之后）。
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace RepRecorder
{
    internal static class FolderPicker
    {
        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            // ---- IModalWindow ----
            [PreserveSig] int Show(IntPtr parent);
            // ---- IFileDialog ----
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        const uint FOS_PICKFOLDERS     = 0x00000020;
        const uint FOS_FORCEFILESYSTEM = 0x00000040;
        const uint FOS_PATHMUSTEXIST   = 0x00000800;
        const uint FOS_NOCHANGEDIR     = 0x00000008;
        const uint SIGDN_FILESYSPATH   = 0x80058000;
        const int  HRESULT_CANCELLED   = unchecked((int)0x800704C7);   // ERROR_CANCELLED
        const uint CLSCTX_INPROC_SERVER = 0x1;

        static readonly Guid CLSID_FileOpenDialog = new Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");
        static readonly Guid IID_IFileDialog     = new Guid("42f85136-db7e-439c-85f1-e4075d135fc8");
        static readonly Guid IID_IShellItem      = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");

        [DllImport("ole32.dll")]
        static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext,
                                           ref Guid riid,
                                           [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        // 把对话框挂在当前活动窗口上 —— 不挂的话它会跑到主窗后面去（用户以为没弹出来）。
        // 这和 WinForms 的 ShowDialog()（不传 owner 时用活动窗口）行为一致。
        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();

        /// <summary>
        /// 弹系统文件夹选择器。选中返回绝对路径；取消、或 COM 出任何错，一律返回 null。
        /// </summary>
        /// <param name="title">对话框标题（空则用系统默认）。</param>
        /// <param name="initialDir">初始目录（不存在就忽略，不因此弹不出来）。</param>
        internal static string Pick(string title, string initialDir)
        {
            object com = null;
            try
            {
                Guid clsid = CLSID_FileOpenDialog, iid = IID_IFileDialog;
                if (CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out com) != 0)
                    return null;

                IFileDialog d = (IFileDialog)com;

                uint opts;
                d.GetOptions(out opts);
                d.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR);

                if (!string.IsNullOrEmpty(title)) d.SetTitle(title);

                if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
                {
                    try
                    {
                        IShellItem start;
                        Guid iidItem = IID_IShellItem;
                        if (SHCreateItemFromParsingName(initialDir, IntPtr.Zero, iidItem, out start) == 0)
                            d.SetFolder(start);
                    }
                    catch { }   // 初始目录设不上不算错，接着弹
                }

                int hr = d.Show(GetActiveWindow());
                if (hr == HRESULT_CANCELLED || hr != 0) return null;

                IShellItem res;
                d.GetResult(out res);
                string path;
                res.GetDisplayName(SIGDN_FILESYSPATH, out path);
                return string.IsNullOrEmpty(path) ? null : path;
            }
            catch (Exception ex)
            {
                Log.Write("选文件夹对话框出错（已当作取消）：" + ex.Message);
                return null;
            }
            finally
            {
                if (com != null) { try { Marshal.ReleaseComObject(com); } catch { } }
            }
        }
    }
}
