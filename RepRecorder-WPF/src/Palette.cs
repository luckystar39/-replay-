// 调色板（2026-10-12 从 MainForm 里搬出来）。
//
// 为什么单独一个文件：这些颜色本来是旧 WinForms 界面 MainForm 的静态字段，WPF 界面一直
// 在直接引用它们（当时的理由是"同一个真源，免得两边颜色慢慢跑偏"）。脱胎那一轮把
// MainForm 整个删掉了，而颜色是唯一还需要留下的东西 —— 它跟界面框架没有关系，
// 只是 19 个 ARGB 常量，谁都可以引用。
//
// ⚠ 换配色只改这里。
using System.Drawing;

namespace RepRecorder
{
    internal static class Palette
    {
        // ============================================================ 设计图那套深蓝灰（2026-10 按设计图取色重做）
        // 设计图（图二）整体是一块**统一的深蓝灰**：窗口/卡片/按钮/队列/终端全是 #272E3A 上下，
        // 结构全靠细的蓝灰描边撑，亮色几乎不用。所以：
        //   · 底色 #272E3A；顶栏/状态栏亮一档 #2E3644；输入框再亮一档 #3A4557
        //   · 边框 #475979（设计图里那圈蓝灰线）；暗线 #38425A
        //   · 正文 #E6EBF4、次要 #93A0B4 —— 分区标题/表头/提示文字在设计图里都是浅色，不再是紫/蓝
        //   · 按钮（开始录制/开关）在设计图里是"暗底 + 描边 + 白字"，不再用亮紫整块填色
        //   · 强调蓝只留给勾选框/状态点；薄荷绿压暗，只用于"成功"状态字
        // ⚠ 底色一动，HudDim/HudLineDim 的取值就得跟着重挑，别只改 HudBg。
        internal static readonly Color HudBg = Color.FromArgb(39, 46, 58);       // #272E3A  底色（设计图主色：窗口/卡片/队列/终端统一）
        internal static readonly Color HudPanel = Color.FromArgb(46, 54, 68);    // #2E3644  顶栏（比底色亮一点）
        internal static readonly Color HudPanel2 = Color.FromArgb(58, 69, 87);   // #3A4557  下拉框/输入框底
        internal static readonly Color HudLine = Color.FromArgb(71, 89, 121);    // #475979  边框（设计图那圈蓝灰线）
        internal static readonly Color HudLineDim = Color.FromArgb(56, 66, 90);  // #38425A  暗线 / 刻度线 / 面板外框
        internal static readonly Color HudCorner = Color.FromArgb(90, 110, 140); // #5A6E8C  面板四个角的角标（比外框亮一档）
        internal static readonly Color HudText = Color.FromArgb(230, 235, 244);  // #E6EBF4  正文
        internal static readonly Color HudDim = Color.FromArgb(147, 160, 180);   // #93A0B4  次要文字
        // 强调色：勾选框 / 状态点 / 滚动条拖动中。设计图里亮色用得极少，别到处撒。
        internal static readonly Color HudAccent = Color.FromArgb(79, 116, 216);      // #4F74D8  强调蓝（勾选框那处）
        internal static readonly Color HudAccent2 = Color.FromArgb(55, 200, 160);     // #37C8A0  薄荷绿：成功状态字（压暗版）
        internal static readonly Color HudSel = Color.FromArgb(58, 74, 107);     // #3A4A6B  选中行
        internal static readonly Color HudOn = Color.FromArgb(46, 55, 70);       // #2E3746  按钮「开/主操作」底（设计图：暗底描边，不再亮紫）
        internal static readonly Color HudOff = Color.FromArgb(42, 49, 64);      // #2A3140  开关按钮「关」
        internal static readonly Color HudBatch = Color.FromArgb(46, 74, 62);    // #2E4A3E  列表里"本批"那几行的绿底
        internal static readonly Color HudBatchBar = Color.FromArgb(55, 200, 160);    // 和 HudAccent2 同一个薄荷绿
        internal static readonly Color HudHead = Color.FromArgb(51, 59, 74);     // #333B4A  列表表头底（设计图表头行）
        internal static readonly Color HudLog = Color.FromArgb(169, 194, 232);   // #A9C2E8  终端文字（设计图浅蓝灰）
        internal static readonly Color HudRed = Color.FromArgb(0xFF, 0x67, 0x67);     // #ff6767  失败
        internal static readonly Color HudOrange = Color.FromArgb(0xFF, 0xCA, 0x85);  // #ffca85  注意
    }
}
