// RepRecorder —— 启动时检查更新（2026-10-10）
// 做法照抄整合包里的 SokuLauncher：Gitee 仓库放一份 version.json，安装包作为 Release 附件。
// 这个文件是【自成一体的】：版本比较、拉清单、下载、替换都在这儿，不往 Core.cs 里塞
// （那个文件已经 9856 行）。
// 设计文档：_更新功能-设计.md   实现计划：_更新功能-实现计划.md
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace RepRecorder
{
    public static class UpdateCheck
    {
        // ⚠ 留空 = 不检查更新（仓库还没建好时就是这个状态）。建好之后填：
        //   https://gitee.com/<你的用户名>/<仓库名>/raw/main/version.json
        // 填这一处、重新编译就生效，别的都不用改。
        public static string ManifestUrl = "https://gitee.com/xingya30/reprecorder-update/raw/master/version.json";

        // 按数字分段比。⚠ 绝不能用字符串比："1.10" 字典序小于 "1.9"，这是最经典的坑。
        // 返回 1 / 0 / -1；任何一段取不出数字 → int.MinValue，表示"这份清单无效"。
        public static int CompareVersions(string a, string b)
        {
            int[] pa = ParseVersion(a), pb = ParseVersion(b);
            if (pa == null || pb == null) return int.MinValue;
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int x = i < pa.Length ? pa[i] : 0;      // 短的补 0：1.1 ≡ 1.1.0.0
                int y = i < pb.Length ? pb[i] : 0;
                if (x != y) return x > y ? 1 : -1;
            }
            return 0;
        }

        static int[] ParseVersion(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            // 容忍手写清单里带个 v（"v1.1"）—— 不容忍的话会静默变成"没有更新"，最难查
            string[] parts = s.Trim().TrimStart('v', 'V').Split('.');
            List<int> v = new List<int>();
            foreach (string p in parts)
            {
                string t = p.Trim();
                int i = 0;
                while (i < t.Length && t[i] >= '0' && t[i] <= '9') i++;
                if (i == 0) return null;                // 这一段一个数字都没有 → 整份无效
                int n;
                if (!int.TryParse(t.Substring(0, i), out n)) return null;
                v.Add(n);
            }
            return v.Count == 0 ? null : v.ToArray();
        }

        static UpdateCheck()
        {
            // ⚠ 必须显式开 TLS 1.2：老机器上运行时默认可能是 TLS 1.0，Gitee / GitHub 会直接拒。
            //   别指望"运行时默认值" —— 这条踩过就知道有多难查（报的错和网络无关）。
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        // 命令行覆盖（验收脚本用；正式用户不会带这个参数）
        public static void ApplyUrlOverride(string[] args)
        {
            if (args == null) return;
            foreach (string a in args)
                if (a != null && a.StartsWith("--update-url="))
                    ManifestUrl = a.Substring("--update-url=".Length);
        }

        // 取清单并解析。失败一律返回 null + 把话说在 err 里，绝不抛 —— 调用方要能
        // 一律静默处理（没网、被网关拦成登录页、清单被手改坏，都是常态）。
        public static UpdateInfo FetchManifest(string url, out string err)
        {
            err = "";
            try
            {
                // ⚠ 用基类 WebRequest，不要硬转 HttpWebRequest：验收脚本用 file:// 喂清单
                //   （测试不能依赖网络），而 WebRequest.Create("file://…") 给的是
                //   FileWebRequest —— 硬转 HttpWebRequest 会抛 InvalidCastException，
                //   表现是"每一份清单都判成 invalid"，从错误信息里完全看不出原因。
                WebRequest req = WebRequest.Create(url);
                req.Timeout = 8000;
                HttpWebRequest hreq = req as HttpWebRequest;
                if (hreq != null)
                {
                    hreq.ReadWriteTimeout = 8000;
                    // ⚠ 必须带 UA：Gitee / GitHub 对空 User-Agent 的请求会 403
                    hreq.UserAgent = "RepRecorder/" + AppInfo.Version;
                }
                using (WebResponse resp = req.GetResponse())
                using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string txt = r.ReadToEnd();
                    // 去掉 BOM：手写的 version.json 存成"UTF-8 带 BOM"很常见
                    // （PowerShell 5.1 的 -Encoding UTF8 就带），而 JavaScriptSerializer
                    // 碰到开头的 BOM 会直接抛。
                    txt = txt.TrimStart('\uFEFF', '\u200B').Trim();
                    Dictionary<string, object> d =
                        new JavaScriptSerializer().DeserializeObject(txt) as Dictionary<string, object>;
                    if (d == null) { err = "清单不是 JSON 对象（是不是被网关拦成了登录页？）"; return null; }

                    UpdateInfo info = new UpdateInfo();
                    info.Version = Str(d, "Version");
                    info.Notes = Str(d, "Notes");
                    info.Sha256 = Str(d, "Sha256");
                    info.FullPackage = Str(d, "FullPackage");
                    if (d.ContainsKey("DownloadLinks"))
                    {
                        object[] links = d["DownloadLinks"] as object[];
                        if (links != null)
                            foreach (object o in links)
                            {
                                Dictionary<string, object> m = o as Dictionary<string, object>;
                                if (m == null) continue;
                                string u = Str(m, "Url");
                                if (u.Length > 0) info.Urls.Add(u);
                            }
                    }
                    if (info.Version.Length == 0) { err = "清单里没有 Version"; return null; }
                    if (info.Urls.Count == 0) { err = "清单里没有可用的下载地址"; return null; }
                    return info;
                }
            }
            catch (Exception ex) { err = ex.Message; return null; }
        }

        // 它是不是一个像样的 Windows 可执行文件（PE 镜像的 DOS 头是 "MZ"）。
        // 只看头两个字节，够挡住"这不是个程序"这一类打错包/下错文件的情况。
        static bool LooksLikeExe(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    int a = fs.ReadByte(), b = fs.ReadByte();
                    return a == 'M' && b == 'Z';
                }
            }
            catch { return false; }
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return "";
            return Convert.ToString(d[key]).Trim();
        }

        // 一次完整的"取清单 + 比版本"。不抛异常，全部化成返回值 —— 调用方要能
        // 一律静默处理。给调试命令用；启动时那条后台线程只用 FetchManifest 一次
        // （别在这里套着调，那会请求两遍）。
        public static string CheckOnce(string url, out string err)
        {
            err = "";
            if (string.IsNullOrEmpty(url)) return "off";        // 没配地址 = 不检查
            UpdateInfo info = FetchManifest(url, out err);
            if (info == null) return "invalid";
            int c = CompareVersions(info.Version, AppInfo.Version);
            if (c == int.MinValue) { err = "清单里的 Version 取不出数字：" + info.Version; return "invalid"; }
            if (c > 0) return "newer";
            if (c == 0) return "same";
            return "older";
        }

        public const int CheckIntervalHours = 6;

        static readonly object gate = new object();
        static UpdateInfo pending;      // 查到新版但还没弹过
        static bool dirty;              // 后台线程动过设置，等界面定时器落盘

        // 界面定时器每 2 秒来取一次。取走即清空 —— 弹窗是模态的，不清会重入、弹好几个。
        public static UpdateInfo TakePending()
        {
            lock (gate) { UpdateInfo p = pending; pending = null; return p; }
        }

        // 后台线程改过设置（时间戳）→ 让界面定时器去落盘。理由见 StartBackgroundCheck 里那段。
        public static bool TakeDirty()
        {
            lock (gate) { bool d = dirty; dirty = false; return d; }
        }

        // 启动时调一次。⚠ 只起线程、只写静态字段，绝不碰界面 —— 这就是它不需要
        // 任何跨线程代码的原因（回填交给那 2 秒的定时器，那条路径本来就在 UI 线程上）。
        public static void StartBackgroundCheck(Settings cfg)
        {
            if (string.IsNullOrEmpty(ManifestUrl)) { Log.Write("检查更新：未配置更新地址，跳过"); return; }
            if (cfg == null) return;
            try
            {
                DateTime last;
                if (!string.IsNullOrEmpty(cfg.LastUpdateCheckUtc) &&
                    DateTime.TryParse(cfg.LastUpdateCheckUtc, out last) &&
                    (DateTime.UtcNow - last.ToUniversalTime()).TotalHours < CheckIntervalHours)
                { Log.Write("检查更新：6 小时内查过，跳过"); return; }
            }
            catch { }

            Thread t = new Thread(delegate ()
            {
                string err = "";
                string r = "invalid";
                UpdateInfo info = null;
                try
                {
                    // ⚠ 只取一次清单。别写成"先 CheckOnce 再 FetchManifest"——那是两次 HTTP。
                    info = FetchManifest(ManifestUrl, out err);
                    if (info != null)
                    {
                        int c = CompareVersions(info.Version, AppInfo.Version);
                        if (c == int.MinValue)
                        { err = "清单里的 Version 取不出数字：" + info.Version; info = null; r = "invalid"; }
                        else r = (c > 0 ? "newer" : (c == 0 ? "same" : "older"));
                    }
                }
                catch (Exception ex) { r = "invalid"; err = ex.Message; }

                // ⚠ 只写内存，**这里绝对不 Save()**：Settings 的所有落盘都发生在 UI 线程上 ——
                //   这是这个项目定下的不变量。后台线程 Save 会和界面定时器同时写同一个
                //   文件，可能把设置写坏。落盘交给界面定时器，见 TakeDirty()。
                try
                {
                    // 时间戳不管成功失败都记 —— 失败还死缠着查会一直敲人家的服务器
                    cfg.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
                    lock (gate) dirty = true;
                }
                catch { }

                if (info == null || r != "newer")
                {
                    Log.Write("检查更新：" + r +
                              (string.IsNullOrEmpty(err) ? "" : "（" + err + "）"));
                    return;
                }
                if (info.Version == cfg.SkippedUpdateVersion)
                {
                    Log.Write("检查更新：有 " + info.Version + "，但用户点过「以后再说」");
                    return;
                }
                Log.Write("检查更新：发现新版本 " + info.Version + "（当前 " + AppInfo.Version + "）");
                lock (gate) pending = info;
            });
            t.IsBackground = true;
            t.Name = "检查更新";
            t.Start();
        }

        // 正在运行的那个 exe 的完整路径。⚠ 用它而不是硬编码文件名：开发目录里叫
        // RepRecorderWPF.exe、分发出去叫 RepRecorder.exe，替换时必须落在"自己"身上。
        public static string RunningExePath
        {
            get { try { return Process.GetCurrentProcess().MainModule.FileName; } catch { return null; } }
        }

        public static string InstallDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }

        public static string Sha256Of(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] h = sha.ComputeHash(fs);
                    StringBuilder sb = new StringBuilder();
                    foreach (byte b in h) sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            catch { return null; }
        }

        // 找一处【这个进程真的写得进去】的临时目录。
        // ⚠ 不能想当然地用 %TEMP%：2026-10-10 用户实测报"更新失败：对路径 …\Temp\repupdate-1.1.zip
        //   的访问被拒绝"。根因（A/B 实测，同一份 exe、同一个清单，只改一个属性）：
        //   exe 落在低完整性目录里（整合包根目录就是 Low Mandatory Level）→ 进程以低完整性运行；
        //   而这台机器的 %TEMP% 带"Medium + 不向上写"的标签 → 低完整性进程**新建文件就被拒**，
        //   同一个 exe 标成 Medium 则 2 秒装好。所以顺序探：%TEMP% → 程序自己那个目录
        //   （日志本来就写在那儿，一定写得进去）→ %LOCALAPPDATA%\RepRecorder。
        //   探法就是真建一个小文件再删掉 —— 猜权限不如试一下。
        internal static string PickWorkDir()
        {
            List<string> cands = new List<string>();
            try { cands.Add(Path.GetTempPath()); } catch { }
            try { cands.Add(InstallDir); } catch { }
            try
            {
                cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                       "RepRecorder"));
            }
            catch { }
            string fallback = "";
            foreach (string d in cands)
            {
                if (string.IsNullOrEmpty(d)) continue;
                if (fallback.Length == 0) fallback = d;
                try
                {
                    Directory.CreateDirectory(d);
                    string probe = Path.Combine(d, "repupdate-writeprobe.tmp");   // 探针文件，立刻删
                    using (FileStream fs = File.Create(probe)) fs.WriteByte(0);
                    File.Delete(probe);
                    if (!string.Equals(d, cands[0], StringComparison.OrdinalIgnoreCase))
                        Log.Write("更新：%TEMP% 写不进去（低完整性？），改用 " + d);
                    return d;
                }
                catch { }
            }
            return fallback;     // 都探不通就还用第一个，让原来的错误照原样冒出来
        }

        // 多个地址依次试（Gitee 拿不到就换 GitHub）。
        public static bool TryDownloadFirst(List<string> urls, string dest, out string err)
        {
            err = "";
            foreach (string u in urls)
            {
                try
                {
                    // ⚠ 基类 WebRequest，别硬转 HttpWebRequest —— file:// 给的是
                    //   FileWebRequest，硬转会抛 InvalidCastException（验收脚本用 file:// 喂包）
                    WebRequest req = WebRequest.Create(u);
                    req.Timeout = 30000;
                    HttpWebRequest hreq = req as HttpWebRequest;
                    if (hreq != null)
                    {
                        hreq.ReadWriteTimeout = 30000;
                        hreq.UserAgent = "RepRecorder/" + AppInfo.Version;
                    }
                    using (WebResponse resp = req.GetResponse())
                    using (Stream s = resp.GetResponseStream())
                    using (FileStream f = File.Create(dest))
                        s.CopyTo(f);
                    if (new FileInfo(dest).Length > 0) return true;
                    err = "下载到的文件是空的（" + u + "）";
                }
                catch (Exception ex) { err = ex.Message + "（" + u + "）"; }
            }
            return false;
        }

        // ⚠ 铁律：要么成功，要么一个字节都没动。最坏情况绝不能是"软件没了"。
        // 顺序刻意做成：先把新的完全准备好 → 再动旧的 → 最后一步只是改名（几乎不会失败）。
        public static bool DownloadAndApply(UpdateInfo info, string targetExePath, string targetDir,
                                           out string err, string expectSha = null)
        {
            err = "";
            string want = string.IsNullOrEmpty(expectSha) ? info.Sha256 : expectSha;
            // ⚠ 用"探得出来写得进去的那个目录"，不是想当然的 %TEMP%（见 PickWorkDir）
            string work = PickWorkDir();
            string tmpZip = Path.Combine(work, "repupdate-" + info.Version + ".zip");
            string tmpDir = Path.Combine(work, "repupdate-" + info.Version);
            // ⚠ 声明在 try 外面，好在 finally 里清掉。半路失败会在用户程序旁边留一个
            //   <exe>.new —— 无害（下次会被覆盖），但没人喜欢自己文件夹里多出个莫名其妙的
            //   东西。成功时它已经被改名走了，所以 finally 里那句是空操作。
            string staged = targetExePath + ".new";
            try
            {
                if (string.IsNullOrEmpty(want))
                { err = "清单里没有 Sha256，拒绝自动替换（请点「打开下载页」手动下）"; return false; }

                if (!TryDownloadFirst(info.Urls, tmpZip, out err)) return false;

                string got = Sha256Of(tmpZip);
                if (!string.Equals(got, want.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    err = "下载到的文件校验值不对（可能没下全）。期望 " + want + "，实际 " + got;
                    return false;
                }

                if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
                ZipFile.ExtractToDirectory(tmpZip, tmpDir);
                string from = Path.Combine(tmpDir, "RepRecorder.exe");    // 更新包里的固定名字
                if (!File.Exists(from) || new FileInfo(from).Length == 0)
                { err = "更新包里没有可用的 RepRecorder.exe"; return false; }
                // ⚠ 还得确认它真是个能跑的程序（PE 镜像，头两个字节 "MZ"）。
                //   校验值只能证明"下到的和发布的是同一份"，证明不了"发布的那份是好的"——
                //   万一打错包（传成占位文件、压错文件），换上去就是把用户的软件换成砖。
                if (!LooksLikeExe(from))
                { err = "更新包里的 RepRecorder.exe 不是个有效的程序（发布时可能打错包了）"; return false; }

                // 4) 先把新文件在目标旁边准备好
                string backup = targetExePath + ".old";
                File.Copy(from, staged, true);

                // 5) 旧的改名（⚠ 正在运行的 exe 也允许改名 —— 实测过），失败就什么都不会变
                if (File.Exists(backup)) { try { File.Delete(backup); } catch { } }
                File.Move(targetExePath, backup);
                try
                {
                    File.Move(staged, targetExePath);     // 只是改名
                }
                catch (Exception ex2)
                {   // 回滚：把旧的改回去，绝不留"软件没了"
                    // ⚠ 回滚本身也会失败（比如换新文件失败的原因就是杀软锁着这个目录，
                    //   那改名回去同样会被拒）。那种情况**必须留下线索**，绝不能再静默吞掉
                    //   —— 否则用户面对的是"程序没了、也没有任何提示"，最坏的一种结局。
                    string rollbackNote = "";
                    try { File.Move(backup, targetExePath); }
                    catch (Exception ex3)
                    {
                        rollbackNote = "；而且自动还原也失败了（" + ex3.Message + "）。旧程序还在旁边，" +
                                       "文件名是 " + Path.GetFileName(backup) + "，把它改回原名即可";
                        Log.Write("更新回滚失败：" + targetExePath + " " + rollbackNote);
                    }
                    err = (rollbackNote.Length == 0 ? "放新文件失败，已还原：" : "放新文件失败：")
                          + ex2.Message + rollbackNote;
                    return false;
                }

                // 6) 两份说明（包里有才覆盖）
                foreach (string md in Directory.GetFiles(tmpDir, "*.md"))
                {
                    try { File.Copy(md, Path.Combine(targetDir, Path.GetFileName(md)), true); }
                    catch (Exception ex3) { Log.Write("更新说明文件失败（不影响使用）：" + ex3.Message); }
                }
                Log.Write("更新完成：" + info.Version + "（旧文件留在 " + Path.GetFileName(backup) + "）");
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
            finally
            {
                try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { }
                try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); } catch { }
                try { if (File.Exists(staged)) File.Delete(staged); } catch { }   // 见上面的说明
            }
        }
    }

    // 解析后的清单。字段名沿用 SokuLauncher 那套（Version / Notes / DownloadLinks），
    // 这样两边的 version.json 看起来是同一个东西。
    public class UpdateInfo
    {
        public string Version = "";
        public string Notes = "";
        public string Sha256 = "";
        public string FullPackage = "";
        public List<string> Urls = new List<string>();
    }
}
