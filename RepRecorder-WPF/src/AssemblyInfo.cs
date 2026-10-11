// 程序集版本信息（2026-10-10 定为 1.0）。
//
// 为什么单独一个文件：csc 的 /version: 开关只设 AssemblyVersion，而资源管理器
// 「属性 → 详细信息」里给用户看的那一栏是 FileVersion —— 那个必须用属性显式声明。
// 所以这里不是凑数，是 1.0 这个版本号能被用户看见的必要条件。
//
// ⚠ 改版本号要同时改四处：这里、Core.cs 的 AppInfo.Version、编译.bat 的源文件列表
//   （已经含本文件）、以及分发那两份「说明」。别只改一处。
//
// 本文件必须保持纯 ASCII 之外的中文能被 csc 正确读取 —— 编译.bat 里带了
// /codepage:65001，所以 UTF-8 无 BOM 也读得对。
using System.Reflection;

[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]
[assembly: AssemblyProduct("RepRecorder")]
[assembly: AssemblyTitle("Rep to MP4 Recorder (OBS)")]
[assembly: AssemblyDescription("把非想天则 .rep 拖进来，自动用 OBS 依次录成 mp4")]
[assembly: AssemblyCopyright("")]
