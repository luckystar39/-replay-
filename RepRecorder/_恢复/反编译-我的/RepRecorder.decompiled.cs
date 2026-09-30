using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: AssemblyVersion("0.0.0.0")]
namespace RepRecorder;

public class Settings
{
	[Category("路径")]
	[Description("obs64.exe 的完整路径。留空自动搜索。")]
	public string ObsExe { get; set; }

	[Category("路径")]
	[Description("th123 文件夹（里面有 th123.exe）。留空自动搜索。")]
	public string GameDir { get; set; }

	[Description("obs-websocket 端口，默认 4455。")]
	[Category("OBS 连接")]
	public int Port { get; set; }

	[Category("OBS 连接")]
	[Description("obs-websocket 密码。留空则自动从 OBS 配置文件读取。")]
	public string Password { get; set; }

	[Category("并行录制")]
	[Description("一次同时开几个游戏实例来录（1~6，默认 3）。1 = 相当于普通单录。\r\n上限锁在 6 是因为 OBS 最多只有 6 条音轨 —— 超过 6 格每格的声音就没法分开，会录成没声音的视频。\r\n内存参考：每实例约 550 MB，6 个约 3.3 GB。")]
	public int ParallelCount { get; set; }

	[Category("并行录制")]
	[Description("切分并行视频时，同时跑几个 ffmpeg（默认 3）。\r\n并行录完是一整张合并视频，要按格裁成 N 个文件，这一步是重编码，最花时间。\r\n同时跑多个能快好几倍（切 6 格从 3 分钟降到 1 分钟左右）；\r\n但 N 卡的 NVENC 同时编码路数有限（家用卡一般 3~8 路），设太大反而会回退到很慢的软件编码。")]
	public int SplitParallel { get; set; }

	[Category("并行录制")]
	[Description("切分并行视频用的编码器：h264_nvenc（默认，兼容性最好）/ hevc_nvenc / av1_nvenc / libx264。\r\n没有 N 卡会自动改用 libx264，不用手动改。")]
	public string ParallelSplitEncoder { get; set; }

	[Category("并行录制")]
	[Description("切分时的质量 CQ（越小越清晰、文件越大），默认 19。")]
	public int ParallelSplitCQ { get; set; }

	[Category("录制")]
	[Description("录制中如果检测到游戏窗口被最小化，自动还原并挪到屏幕外（最小化会导致采集不到画面），录制继续。")]
	public bool KeepGameCapturable { get; set; }

	[Description("单个 rep 的录制上限（分钟），超时强制停止。")]
	[Category("录制")]
	public int MaxMinutes { get; set; }

	[Category("录制")]
	[Description("游戏窗口出现后、开始录像前等待的秒数。")]
	public int PreRollSeconds { get; set; }

	[Description("游戏退出后、停止录像前等待的秒数。")]
	[Category("录制")]
	public int PostRollSeconds { get; set; }

	[Description("把 rep 拖进窗口后立刻自动开始录制。")]
	[Category("行为")]
	public bool AutoStartOnDrop { get; set; }

	[Description("自动检查/创建 OBS 采集源，并把游戏窗口绑定给它。")]
	[Category("行为")]
	public bool AutoPrepareObs { get; set; }

	[Category("画布")]
	[Description("「准备 OBS」和每次开录时，是否把 OBS 的画布/输出分辨率与帧率设成下面三个值。\r\n非想天则原生是 4:3 的 640×480：用 1440×1080 这种 4:3 画布时画面正好铺满、左右没有黑边；\r\n关掉则不动 OBS 设置（若 OBS 画布是 1920×1080 的 16:9，4:3 画面会居中、左右各留黑边）。\r\n（原来主界面上的那个复选框就是这个开关，已合并到这里）")]
	public bool AutoCanvas { get; set; }

	[Description("画布/输出宽度。")]
	[Category("画布")]
	public int CanvasWidth { get; set; }

	[Category("画布")]
	[Description("画布/输出高度。")]
	public int CanvasHeight { get; set; }

	[Description("帧率。")]
	[Category("画布")]
	public int CanvasFps { get; set; }

	[Description("全部录完后关闭 OBS。")]
	[Category("行为")]
	public bool CloseObsAfter { get; set; }

	[Description("使用专用的「RepRecorder 录制场景」：只录游戏窗口，不会把整个桌面录进去。关掉则使用你当前场景里已有的采集源。")]
	[Category("行为")]
	public bool UseDedicatedScene { get; set; }

	[Description("录进视频的音频里是否包含游戏声音。关掉 → 录出来的视频是无声的（适合你之后自己配 BGM）。")]
	[Category("录音")]
	public bool RecordGameAudio { get; set; }

	[Category("录音")]
	[Description("gameOnly = 只录游戏声音（录制时自动静音麦克风）；gameAndMic = 游戏声音 + 麦克风（边录边解说）。")]
	public string MicMode { get; set; }

	[Description("只把游戏声音录进视频：新增「应用程序音频采集(th123.exe)」，并临时静音 OBS 的桌面音频，避免录进其他系统声音或重复。")]
	[Category("录音")]
	public bool GameAudioOnly { get; set; }

	[Description("录进视频的音量倍数（增益），默认 1.0。\r\n0.5 = 一半音量；2.0 = 两倍（+6dB）；上限 20。\r\n为什么需要：任务栏那个「电脑音量」调的是输出设备，OBS 抓的是进程音频流，改它【不会】影响录像音量；\r\n所以想改录像的响度就用这里，或者用 Windows「音量合成器」里 th123 那一条（那条会影响录像）。")]
	[Category("录音")]
	public double RecordVolume { get; set; }

	[Description("轮录：点一次「开始录制」就把队列里所有待录的 rep 一批接一批全部录完。\r\n每批 = 「同时录制数量」（上限 6）；最后不足一批的按实际剩余数量录；每批 1 个时自动走单录、依次录。\r\n不勾 = 和以前一样，点一次只录一批。")]
	[Category("行为")]
	public bool RoundRecord { get; set; }

	[Description("是否把游戏背景音乐（BGM）也录进视频。\r\n开 = 会自动把 ReplayDnD.ini 里的 MuteMusic 改成 0（播放 rep 时放 BGM）；关 = 改回 1。\r\n界面上有「录BGM」按钮可以直接切。注意 BGM 有版权风险。")]
	[Category("录音")]
	public bool RecordBgm { get; set; }

	[Description("单录时把游戏画面最外圈裁掉几像素（源坐标，默认 4）。\r\n实测 WGC 抓 640×480 游戏窗口时，最左/最右 2~3 像素会带一条亮边\r\n（整段视频列平均：最左列 82.6，第 3 列起 45.6）。等比裁掉这一圈后再缩放铺满，画面不变形，只损失约 0.6% 边缘。\r\n设成 0 = 不裁。")]
	[Category("录音")]
	public int SingleEdgeInset { get; set; }

	[Description("并行切分时，每一格往里裁掉多少像素（按成品视频的像素算，默认 6）。\r\n为什么需要：并行时相邻格的采集源是亚像素缩放摆的，格边会漏 1~3 像素的邻居画面或黑边进来，\r\n成品最外一圈就会出现一条亮线/暗线（用户实测：右边缘最外 2~3 像素亮度从 43 跳到 77）。\r\n往里裁一圈就干净了；6 像素 ≈ 0.4%，肉眼看不出来。设成 0 = 不裁（最外圈可能带线）。")]
	[Category("录音")]
	public int ParallelEdgeInset { get; set; }

	[Category("行为")]
	[Description("已经有同名 mp4 的 rep 直接跳过（批量重拖时省时间）。")]
	public bool SkipExisting { get; set; }

	[Category("行为")]
	[Description("每录完一个视频就弹窗问你：要放在哪里。")]
	public bool AskSaveLocation { get; set; }

	[Description("“另存到文件夹…”上次用过的文件夹。")]
	[Category("行为")]
	public string LastSaveFolder { get; set; }

	[Browsable(false)]
	public int SettingsVersion { get; set; }

	public static string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RepRecorder.settings.json");

	public static string ObsWebSocketConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio\\plugin_config\\obs-websocket\\config.json");

	public Settings()
	{
		ObsExe = "";
		GameDir = "";
		Port = 4455;
		Password = "";
		MaxMinutes = 12;
		PreRollSeconds = 3;
		PostRollSeconds = 2;
		AutoStartOnDrop = false;
		AutoPrepareObs = true;
		AutoCanvas = true;
		CanvasWidth = 1440;
		CanvasHeight = 1080;
		CanvasFps = 60;
		CloseObsAfter = false;
		UseDedicatedScene = true;
		MicMode = "gameOnly";
		RecordGameAudio = true;
		GameAudioOnly = true;
		RoundRecord = false;
		RecordBgm = false;
		ParallelEdgeInset = 6;
		SingleEdgeInset = 4;
		RecordVolume = 1.0;
		SettingsVersion = 4;
		KeepGameCapturable = true;
		ParallelCount = 3;
		ParallelSplitEncoder = "h264_nvenc";
		SplitParallel = 3;
		ParallelSplitCQ = 23;
		SkipExisting = true;
		AskSaveLocation = true;
		LastSaveFolder = "";
		SettingsVersion = 2;
	}

	public static Settings Load()
	{
		Settings settings = new Settings();
		try
		{
			if (File.Exists(SettingsPath))
			{
				string input = File.ReadAllText(SettingsPath, Encoding.UTF8);
				Settings settings2 = new JavaScriptSerializer().Deserialize<Settings>(input);
				if (settings2 != null)
				{
					settings = settings2;
				}
			}
		}
		catch (Exception ex)
		{
			Log.Write("读取设置失败：" + ex.Message);
		}
		if (settings.ParallelCount < 1)
		{
			settings.ParallelCount = 1;
		}
		if (settings.ParallelCount > 6)
		{
			settings.ParallelCount = 6;
		}
		if (settings.SettingsVersion < 2)
		{
			settings.SettingsVersion = 2;
		}
		if (settings.SettingsVersion < 3)
		{
			if (settings.SingleEdgeInset <= 0)
			{
				settings.SingleEdgeInset = 4;
			}
			if (settings.ParallelEdgeInset <= 0)
			{
				settings.ParallelEdgeInset = 6;
			}
			settings.SettingsVersion = 3;
		}
		if (settings.SettingsVersion < 4)
		{
			if (settings.RecordVolume <= 0.0)
			{
				settings.RecordVolume = 1.0;
			}
			if (settings.SplitParallel <= 0)
			{
				settings.SplitParallel = 3;
			}
			settings.SettingsVersion = 4;
		}
		settings.FillDefaults();
		return settings;
	}

	public void Save()
	{
		try
		{
			string contents = new JavaScriptSerializer().Serialize(this);
			File.WriteAllText(SettingsPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}
		catch (Exception ex)
		{
			Log.Write("保存设置失败：" + ex.Message);
		}
	}

	public void FillDefaults()
	{
		if (string.IsNullOrEmpty(GameDir) || !File.Exists(Path.Combine(GameDir, "th123.exe")))
		{
			string text = FindGameDir();
			if (text != null)
			{
				GameDir = text;
			}
		}
		if (string.IsNullOrEmpty(ObsExe) || !File.Exists(ObsExe))
		{
			string text = FindObsExe();
			if (text != null)
			{
				ObsExe = text;
			}
		}
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio\\plugin_config\\obs-websocket\\config.json");
			if (File.Exists(path))
			{
				Dictionary<string, object> dictionary = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path, Encoding.UTF8));
				if (dictionary != null)
				{
					if (dictionary.ContainsKey("server_port") && dictionary["server_port"] != null)
					{
						Port = Convert.ToInt32(dictionary["server_port"]);
					}
					if (string.IsNullOrEmpty(Password) && dictionary.ContainsKey("server_password") && dictionary["server_password"] != null)
					{
						Password = Convert.ToString(dictionary["server_password"]);
					}
				}
			}
		}
		catch
		{
		}
		if (Port <= 0)
		{
			Port = 4455;
		}
	}

	public static string FindGameDir()
	{
		string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
		List<string> list = new List<string>();
		list.Add(baseDirectory);
		try
		{
			DirectoryInfo parent = Directory.GetParent(baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			if (parent != null)
			{
				list.Add(parent.FullName);
			}
		}
		catch
		{
		}
		foreach (string item in list)
		{
			if (File.Exists(Path.Combine(Path.Combine(item, "th123"), "th123.exe")))
			{
				return Path.Combine(item, "th123");
			}
			try
			{
				string[] directories = Directory.GetDirectories(item);
				foreach (string path in directories)
				{
					if (File.Exists(Path.Combine(Path.Combine(path, "th123"), "th123.exe")))
					{
						return Path.Combine(path, "th123");
					}
				}
			}
			catch
			{
			}
		}
		return null;
	}

	public static string FindObsExe()
	{
		List<string> list = new List<string>();
		try
		{
			string[] array = new string[2] { "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall", "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall" };
			string[] array2 = array;
			foreach (string name in array2)
			{
				using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(name);
				if (registryKey == null)
				{
					continue;
				}
				string[] subKeyNames = registryKey.GetSubKeyNames();
				foreach (string name2 in subKeyNames)
				{
					using RegistryKey registryKey2 = registryKey.OpenSubKey(name2);
					if (registryKey2 == null)
					{
						continue;
					}
					string text = Convert.ToString(registryKey2.GetValue("DisplayName"));
					if (text == null || text.IndexOf("OBS", StringComparison.OrdinalIgnoreCase) < 0)
					{
						continue;
					}
					string text2 = Convert.ToString(registryKey2.GetValue("DisplayIcon"));
					if (!string.IsNullOrEmpty(text2))
					{
						int num = text2.IndexOf(',');
						if (num > 0)
						{
							text2 = text2.Substring(0, num);
						}
						list.Add(text2.Trim().Trim('"'));
					}
					string text3 = Convert.ToString(registryKey2.GetValue("InstallLocation"));
					if (!string.IsNullOrEmpty(text3))
					{
						list.Add(Path.Combine(text3.Trim().Trim('"'), "bin\\64bit\\obs64.exe"));
					}
				}
			}
		}
		catch
		{
		}
		list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "obs-studio\\bin\\64bit\\obs64.exe"));
		list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "obs-studio\\bin\\64bit\\obs64.exe"));
		list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\\obs-studio\\bin\\64bit\\obs64.exe"));
		try
		{
			DriveInfo[] drives = DriveInfo.GetDrives();
			foreach (DriveInfo driveInfo in drives)
			{
				if (driveInfo.DriveType != DriveType.Fixed || !driveInfo.IsReady)
				{
					continue;
				}
				string[] array2 = Directory.GetDirectories(driveInfo.RootDirectory.FullName);
				foreach (string text4 in array2)
				{
					if (Path.GetFileName(text4).ToLower().IndexOf("obs") >= 0)
					{
						list.Add(Path.Combine(text4, "bin\\64bit\\obs64.exe"));
						list.Add(Path.Combine(text4, "obs-studio\\bin\\64bit\\obs64.exe"));
					}
				}
			}
		}
		catch
		{
		}
		foreach (string item in list)
		{
			try
			{
				if (File.Exists(item))
				{
					return item;
				}
			}
			catch
			{
			}
		}
		return null;
	}

	public static bool EnsureWebSocketEnabled(out string message)
	{
		message = "";
		try
		{
			string obsWebSocketConfigPath = ObsWebSocketConfigPath;
			if (!File.Exists(obsWebSocketConfigPath))
			{
				message = "找不到 OBS 的 WebSocket 配置文件（先启动一次 OBS）";
				return false;
			}
			string input = File.ReadAllText(obsWebSocketConfigPath, Encoding.UTF8);
			Dictionary<string, object> dictionary = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(input);
			if (dictionary == null)
			{
				message = "WebSocket 配置文件读不出来";
				return false;
			}
			if (dictionary.ContainsKey("server_enabled") && dictionary["server_enabled"] != null && Convert.ToBoolean(dictionary["server_enabled"]))
			{
				message = "WebSocket 服务器已启用";
				return true;
			}
			if (Process.GetProcessesByName("obs64").Length > 0)
			{
				message = "OBS 正在运行，但 WebSocket 服务器是关闭的：请关掉 OBS 后重试本程序，或手动在 工具 → WebSocket 服务器设置 里启用";
				return false;
			}
			dictionary["server_enabled"] = true;
			File.WriteAllText(obsWebSocketConfigPath, new JavaScriptSerializer().Serialize(dictionary), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			message = "已自动帮你启用 OBS 的 WebSocket 服务器";
			return true;
		}
		catch (Exception ex)
		{
			message = "自动启用 WebSocket 失败：" + ex.Message;
			return false;
		}
	}
}
public static class Log
{
	private static readonly object gate = new object();

	public static string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RepRecorder.log");

	public static event Action<string> OnLine;

	public static void RotateOnStart()
	{
		try
		{
			if (!File.Exists(FilePath))
			{
				return;
			}
			string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RepRecorder-上次.log");
			try
			{
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch
			{
			}
			File.Move(FilePath, text);
		}
		catch
		{
		}
	}

	public static void Write(string msg)
	{
		string text = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
		lock (gate)
		{
			try
			{
				File.AppendAllText(FilePath, text + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
			catch
			{
			}
		}
		Action<string> onLine = OnLine;
		if (onLine != null)
		{
			try
			{
				onLine(text);
			}
			catch
			{
			}
		}
	}
}
public class Job
{
	public string Rep;

	public string Status;

	public string Detail;

	public string Output;

	public long Size;

	public DateTime Started;

	public DateTime Ended;

	public double VideoSeconds;

	public bool Force;

	public ListViewItem Item;

	public Job(string rep)
	{
		Rep = rep;
		Status = "等待";
		Detail = "";
		Output = "";
	}
}
public class ObsClient : IDisposable
{
	private ClientWebSocket ws;

	private readonly JavaScriptSerializer json = new JavaScriptSerializer();

	private int reqId;

	private Thread reader;

	private volatile bool running;

	private readonly object sendLock = new object();

	private readonly object gate = new object();

	private readonly Dictionary<string, Dictionary<string, object>> pending = new Dictionary<string, Dictionary<string, object>>();

	private readonly Dictionary<string, ManualResetEvent> signals = new Dictionary<string, ManualResetEvent>();

	private readonly Dictionary<string, double> levels = new Dictionary<string, double>();

	private DateTime lastMeterAt = DateTime.MinValue;

	public bool MetersEnabled = false;

	public bool MetersFresh => (DateTime.Now - lastMeterAt).TotalSeconds < 3.0;

	public bool IsConnected => ws != null && ws.State == WebSocketState.Open;

	public double GetLevel(string inputName)
	{
		lock (levels)
		{
			double value;
			return levels.TryGetValue(inputName, out value) ? value : 0.0;
		}
	}

	public List<string> LevelNames()
	{
		lock (levels)
		{
			return new List<string>(levels.Keys);
		}
	}

	private static string B64Sha256(string s)
	{
		using SHA256 sHA = SHA256.Create();
		return Convert.ToBase64String(sHA.ComputeHash(Encoding.UTF8.GetBytes(s)));
	}

	private void Send(object o)
	{
		string s = json.Serialize(o);
		byte[] bytes = Encoding.UTF8.GetBytes(s);
		lock (sendLock)
		{
			ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None).GetAwaiter().GetResult();
		}
	}

	private Dictionary<string, object> Receive()
	{
		MemoryStream memoryStream = new MemoryStream();
		byte[] array = new byte[65536];
		WebSocketReceiveResult result;
		do
		{
			bool flag = true;
			ArraySegment<byte> buffer = new ArraySegment<byte>(array);
			result = ws.ReceiveAsync(buffer, CancellationToken.None).GetAwaiter().GetResult();
			if (result.MessageType == WebSocketMessageType.Close)
			{
				throw new Exception("OBS 关闭了 WebSocket 连接");
			}
			if (result.Count > 0)
			{
				memoryStream.Write(array, 0, result.Count);
			}
		}
		while (!result.EndOfMessage);
		string text = Encoding.UTF8.GetString(memoryStream.ToArray());
		if (text.Length == 0)
		{
			return null;
		}
		return (Dictionary<string, object>)json.DeserializeObject(text);
	}

	public void Connect(int port, string password)
	{
		Dispose();
		ws = new ClientWebSocket();
		ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(5.0);
		ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port), CancellationToken.None).GetAwaiter().GetResult();
		Dictionary<string, object> dictionary = Receive();
		if (dictionary == null || Convert.ToInt32(dictionary["op"]) != 0)
		{
			throw new Exception("OBS 未返回 Hello 握手包");
		}
		Dictionary<string, object> dictionary2 = (Dictionary<string, object>)dictionary["d"];
		Dictionary<string, object> dictionary3 = new Dictionary<string, object>();
		dictionary3["rpcVersion"] = 1;
		dictionary3["eventSubscriptions"] = (MetersEnabled ? 65536 : 0);
		if (dictionary2.ContainsKey("authentication") && dictionary2["authentication"] != null)
		{
			Dictionary<string, object> dictionary4 = (Dictionary<string, object>)dictionary2["authentication"];
			if (string.IsNullOrEmpty(password))
			{
				throw new Exception("OBS 要求 WebSocket 密码，但设置里是空的");
			}
			string text = B64Sha256(password + Convert.ToString(dictionary4["salt"]));
			dictionary3["authentication"] = B64Sha256(text + Convert.ToString(dictionary4["challenge"]));
		}
		Send(new Dictionary<string, object>
		{
			{ "op", 1 },
			{ "d", dictionary3 }
		});
		Dictionary<string, object> dictionary5 = Receive();
		if (dictionary5 == null || Convert.ToInt32(dictionary5["op"]) != 2)
		{
			throw new Exception("obs-websocket 认证失败（密码不对？）");
		}
		Log.Write("已连接 OBS（obs-websocket " + Convert.ToString(dictionary2["obsWebSocketVersion"]) + (MetersEnabled ? "，已订阅音频电平" : "") + "）");
		running = true;
		reader = new Thread(ReaderLoop);
		reader.IsBackground = true;
		reader.Start();
	}

	private void ReaderLoop()
	{
		while (running)
		{
			Dictionary<string, object> dictionary;
			try
			{
				dictionary = Receive();
			}
			catch
			{
				break;
			}
			if (dictionary == null)
			{
				continue;
			}
			int num;
			try
			{
				num = Convert.ToInt32(dictionary["op"]);
			}
			catch
			{
				continue;
			}
			switch (num)
			{
			case 7:
			{
				if (!(dictionary["d"] is Dictionary<string, object> dictionary2) || !dictionary2.ContainsKey("requestId"))
				{
					break;
				}
				string key = Convert.ToString(dictionary2["requestId"]);
				ManualResetEvent manualResetEvent = null;
				lock (gate)
				{
					if (signals.ContainsKey(key))
					{
						pending[key] = dictionary2;
						manualResetEvent = signals[key];
					}
				}
				if (manualResetEvent != null)
				{
					try
					{
						manualResetEvent.Set();
					}
					catch
					{
					}
				}
				break;
			}
			case 5:
				if (dictionary["d"] is Dictionary<string, object> d)
				{
					HandleEvent(d);
				}
				break;
			}
		}
	}

	private void HandleEvent(Dictionary<string, object> d)
	{
		string text = Convert.ToString(d.ContainsKey("eventType") ? d["eventType"] : "");
		if (text != "InputVolumeMeters")
		{
			return;
		}
		Dictionary<string, object> dictionary = (d.ContainsKey("eventData") ? (d["eventData"] as Dictionary<string, object>) : null);
		if (dictionary == null)
		{
			return;
		}
		List<object> list = ToList(dictionary.ContainsKey("inputs") ? dictionary["inputs"] : null);
		lock (levels)
		{
			levels.Clear();
			foreach (object item in list)
			{
				if (!(item is Dictionary<string, object> dictionary2))
				{
					continue;
				}
				string key = Convert.ToString(dictionary2["inputName"]);
				double num = 0.0;
				if (dictionary2.ContainsKey("inputLevelsMul") && dictionary2["inputLevelsMul"] != null)
				{
					foreach (object item2 in ToList(dictionary2["inputLevelsMul"]))
					{
						foreach (object item3 in ToList(item2))
						{
							if (item3 != null)
							{
								double num2 = Convert.ToDouble(item3);
								if (num2 > num)
								{
									num = num2;
								}
							}
						}
					}
				}
				levels[key] = num;
			}
		}
		lastMeterAt = DateTime.Now;
	}

	public Dictionary<string, object> Request(string type, Dictionary<string, object> data)
	{
		if (!IsConnected)
		{
			throw new Exception("OBS 未连接");
		}
		bool lockTaken = false;
		object obj = default;
		string text;
		try
		{
			Monitor.Enter(obj = gate, ref lockTaken);
			reqId++;
			text = "r" + reqId;
		}
		finally
		{
			if (lockTaken)
			{
				Monitor.Exit(obj);
			}
		}
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		dictionary["requestType"] = type;
		dictionary["requestId"] = text;
		if (data != null)
		{
			dictionary["requestData"] = data;
		}
		ManualResetEvent manualResetEvent = new ManualResetEvent(initialState: false);
		bool lockTaken2 = false;
		try
		{
			Monitor.Enter(obj = gate, ref lockTaken2);
			signals[text] = manualResetEvent;
		}
		finally
		{
			if (lockTaken2)
			{
				Monitor.Exit(obj);
			}
		}
		try
		{
			Send(new Dictionary<string, object>
			{
				{ "op", 6 },
				{ "d", dictionary }
			});
			if (!manualResetEvent.WaitOne(20000))
			{
				throw new Exception("OBS 请求 " + type + " 超时（20 秒）");
			}
			bool lockTaken3 = false;
			Dictionary<string, object> dictionary2;
			try
			{
				Monitor.Enter(obj = gate, ref lockTaken3);
				dictionary2 = (pending.ContainsKey(text) ? pending[text] : null);
			}
			finally
			{
				if (lockTaken3)
				{
					Monitor.Exit(obj);
				}
			}
			if (dictionary2 == null)
			{
				throw new Exception("OBS 请求 " + type + " 没有拿到响应");
			}
			Dictionary<string, object> dictionary3 = (Dictionary<string, object>)dictionary2["requestStatus"];
			if (!Convert.ToBoolean(dictionary3["result"]))
			{
				throw new Exception("OBS 请求 " + type + " 失败：" + Convert.ToString(dictionary3["code"]) + " " + (dictionary3.ContainsKey("comment") ? Convert.ToString(dictionary3["comment"]) : ""));
			}
			if (dictionary2.ContainsKey("responseData") && dictionary2["responseData"] != null)
			{
				return (Dictionary<string, object>)dictionary2["responseData"];
			}
			return new Dictionary<string, object>();
		}
		finally
		{
			bool lockTaken4 = false;
			try
			{
				Monitor.Enter(obj = gate, ref lockTaken4);
				signals.Remove(text);
				pending.Remove(text);
			}
			finally
			{
				if (lockTaken4)
				{
					Monitor.Exit(obj);
				}
			}
			try
			{
				manualResetEvent.Close();
			}
			catch
			{
			}
		}
	}

	public static List<object> ToList(object o)
	{
		List<object> list = new List<object>();
		if (o is IEnumerable enumerable)
		{
			foreach (object item in enumerable)
			{
				list.Add(item);
			}
		}
		return list;
	}

	public void Dispose()
	{
		if (ws == null)
		{
			return;
		}
		running = false;
		try
		{
			if (ws.State == WebSocketState.Open)
			{
				ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).GetAwaiter().GetResult();
			}
		}
		catch
		{
		}
		try
		{
			ws.Dispose();
		}
		catch
		{
		}
		ws = null;
		try
		{
			if (reader != null && reader.IsAlive)
			{
				reader.Join(1500);
			}
		}
		catch
		{
		}
		reader = null;
		lock (gate)
		{
			foreach (KeyValuePair<string, ManualResetEvent> signal in signals)
			{
				try
				{
					signal.Value.Set();
				}
				catch
				{
				}
			}
		}
		lock (levels)
		{
			levels.Clear();
		}
	}
}
public class MainForm : Form
{
	private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

	private struct DROPFILES
	{
		public uint pFiles;

		public int x;

		public int y;

		public int fNC;

		public int fWide;
	}

	private struct POINT
	{
		public int X;

		public int Y;
	}

	private struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private const uint WM_DROPFILES = 563u;

	private const int SW_RESTORE = 9;

	private const uint SWP_NOZORDER = 4u;

	private const uint SWP_NOACTIVATE = 16u;

	private const string RecSceneName = "RepRecorder 录制场景";

	private const string RecSourceName = "RepRecorder 游戏窗口";

	private const string AudioSourceName = "RepRecorder 游戏声音";

	private const string SingleWinTitle = "th123 录制中";

	private readonly Settings cfg;

	private readonly List<Job> jobs = new List<Job>();

	private readonly object jobsLock = new object();

	private Thread worker;

	private volatile bool cancel;

	private volatile bool titleWatch = false;

	private ObsClient obs = new ObsClient();

	private string captureSource = null;

	private string captureScene = null;

	private bool captureSourceIsOurs = false;

	private string originalScene = null;

	private string originalRecordDir = null;

	private bool closing = false;

	private int currentMethod = -1;

	private int blackStreak = 0;

	private bool gameMutedByUs = false;

	private bool weMutedSystem = false;

	private double jobMaxGameLevel = 0.0;

	private string queueSaveFolder = null;

	private bool queueSaveDecided = false;

	private readonly List<string> mutedInputs = new List<string>();

	private readonly Dictionary<string, bool> micOriginalMute = new Dictionary<string, bool>();

	private readonly Dictionary<string, bool> deskOriginalMute = new Dictionary<string, bool>();

	private ListView list;

	private TextBox logBox;

	private Label hint;

	private Label obsState;

	private Panel hudTop;

	private Label hudLeftTitle;

	private Label lblParHint;

	private Button btnStart;

	private Button btnStop;

	private Button btnClear;

	private Button btnRemove;

	private Button btnCheck;

	private Button btnPrepare;

	private Button btnSet;

	private Button btnOpenLog;

	private Label lblSoundNote;

	private Button btnSound;

	private Button btnGameAudio;

	private Button btnBgm;

	private CheckBox chkAuto;

	private CheckBox chkPrepare;

	private CheckBox chkCloseObs;

	private CheckBox chkRound;

	private ComboBox cbMic;

	private Label lblMic;

	private Button btnRefresh;

	private ComboBox cbParallel;

	private ToolTip tip;

	private System.Windows.Forms.Timer soundTimer;

	private StatusStrip status;

	private ToolStripStatusLabel statusLabel;

	private static readonly Color HudBg = Color.FromArgb(9, 13, 26);

	private static readonly Color HudPanel = Color.FromArgb(14, 20, 38);

	private static readonly Color HudLine = Color.FromArgb(38, 62, 110);

	private static readonly Color HudText = Color.FromArgb(198, 216, 255);

	private static readonly Color HudDim = Color.FromArgb(120, 158, 214);

	private static readonly Color HudAccent = Color.FromArgb(90, 170, 255);

	private static readonly Color HudGreen = Color.FromArgb(24, 120, 74);

	private static readonly Color HudRed = Color.FromArgb(140, 40, 52);

	private static readonly Color HudGrey = Color.FromArgb(40, 52, 80);

	private Font batchFont = null;

	private int minimizedFixCount = 0;

	private static readonly string LastFramePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RepRecorder.lastframe.jpg");

	private List<ListViewItem> emptyClickKeep = new List<ListViewItem>();

	private System.Windows.Forms.Timer emptyClickTimer = null;

	private static readonly string[] BgmIniSubPaths = new string[2] { "modules\\ReplayDnD\\ReplayDnD.ini", "modules\\ReplayDnD-old\\ReplayDnD.ini" };

	private bool parallelRequested = false;

	private List<Job> selectedBatch = null;

	private volatile bool forceRecord = false;

	private bool obsStuckRetried = false;

	private List<Job> pinnedBatch = new List<Job>();

	private bool suppressSelSync = false;

	private static string MuteFlagPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RepRecorder.已静音待恢复");

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int n);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(IntPtr hWnd, StringBuilder s, int n);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("shell32.dll")]
	private static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);

	private static void DropFileOnWindow(IntPtr hwnd, string file)
	{
		int num = Marshal.SizeOf(typeof(DROPFILES));
		IntPtr intPtr = Marshal.AllocHGlobal(num + (file.Length + 2) * 2);
		try
		{
			Marshal.StructureToPtr(new DROPFILES
			{
				pFiles = (uint)num,
				fWide = 1
			}, intPtr, fDeleteOld: false);
			byte[] bytes = Encoding.Unicode.GetBytes(file + "\0\0");
			Marshal.Copy(bytes, 0, (IntPtr)((long)intPtr + num), bytes.Length);
			SendMessage(hwnd, 563u, intPtr, IntPtr.Zero);
		}
		finally
		{
			Marshal.FreeHGlobal(intPtr);
		}
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool SetWindowText(IntPtr hWnd, string text);

	[DllImport("user32.dll")]
	private static extern bool GetClientRect(IntPtr hWnd, out RECT r);

	[DllImport("user32.dll")]
	private static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(IntPtr hWnd, int cmd);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);

	public MainForm(string[] args)
	{
		MainForm mainForm = this;
		cfg = Settings.Load();
		Text = "Rep → MP4 录制器（OBS）";
		Font = new Font("Microsoft YaHei UI", 9f);
		ClientSize = new Size(1180, 780);
		MinimumSize = new Size(760, 520);
		BackColor = Color.FromArgb(9, 13, 26);
		ForeColor = Color.FromArgb(198, 216, 255);
		AllowDrop = true;
		DragEnter += OnDragEnter;
		DragDrop += OnDragDrop;
		StartPosition = FormStartPosition.CenterScreen;
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Left,
			Width = 312,
			FlowDirection = FlowDirection.TopDown,
			WrapContents = false,
			AutoScroll = true,
			Padding = new Padding(10, 8, 8, 8)
		};
		FlowLayoutPanel flowLayoutPanel2 = new FlowLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.TopDown,
			WrapContents = false,
			Margin = new Padding(0, 0, 0, 6)
		};
		btnStart = MakeButton("开始录制", 116, OnStart, Color.FromArgb(0, 120, 60));
		btnStop = MakeButton("停止", 72, OnStop, Color.FromArgb(160, 60, 60));
		btnClear = MakeButton("清空队列", 88, OnClear, Color.Empty);
		btnRemove = MakeButton("移除选中", 88, OnRemove, Color.Empty);
		btnRefresh = MakeButton("刷新", 60, OnRefreshQueue, Color.Empty);
		btnCheck = MakeButton("检查环境", 88, OnCheck, Color.Empty);
		btnPrepare = MakeButton("准备 OBS（自动建源+绑定窗口）", 208, OnPrepare, Color.Empty);
		btnSet = MakeButton("设置…", 72, OnSettings, Color.Empty);
		btnOpenLog = MakeButton("日志", 60, OnOpenLog, Color.Empty);
		btnSound = MakeButton("声音：开 ✓", 124, OnSoundToggle, Color.Empty);
		btnSound.FlatStyle = FlatStyle.Flat;
		lblSoundNote = new Label();
		lblSoundNote.AutoSize = true;
		lblSoundNote.ForeColor = Color.DimGray;
		lblSoundNote.Margin = new Padding(4, 9, 12, 0);
		lblSoundNote.Text = "（整个电脑静音，录出来的视频有声音）";
		btnGameAudio = MakeButton("录打击音效：开 ✓", 168, OnGameAudioToggle, Color.Empty);
		btnGameAudio.FlatStyle = FlatStyle.Flat;
		btnBgm = MakeButton("录BGM：关 ✕", 116, OnBgmToggle, Color.Empty);
		btnBgm.FlatStyle = FlatStyle.Flat;
		tip = new ToolTip();
		tip.SetToolTip(btnSound, "点一下切换【整个电脑静音 / 恢复声音】：静音时你自己（以及整个电脑）都听不到声音，\r\n但录出来的视频仍然有完整的游戏原声（OBS 抓的是游戏进程的音频流，端点静音掐不断它）。录完会自动恢复。");
		tip.SetToolTip(lblSoundNote, "说明：这个按钮管的是【你自己听不听得到】——它静音的是整台电脑的默认输出设备；\r\n录像里照样有游戏声音（录不录声音由右边的「录打击音效」决定）。");
		tip.SetToolTip(btnGameAudio, "这个按钮只管一件事：【游戏声音（打击音效 / BGM）要不要录进视频】。\r\n  开 = 游戏声音照实录进视频；\r\n  关 = 录出来的视频完全没有声音，并且会自动把「录BGM」锁成关（连游戏里的 BGM 也不放）。\r\n你自己耳朵听不听得到，由左边的「声音」按钮决定。");
		tip.SetToolTip(btnBgm, "这个按钮只管一件事：【播放 rep 时游戏要不要放背景音乐】。\r\n游戏是靠 ReplayDnD.ini 里的 MuteMusic 决定的，这个按钮只改那一行：\r\n  开 = MuteMusic 改成 0（有 BGM）；关 = 改回 1（只有音效）。\r\nBGM 有/没有都会照实录进视频（除非你把「录打击音效」关掉，那样整段都没声音；那样 BGM 也会被锁成关）。\r\n改完从下一个文件开始生效（游戏是启动时读这个 ini 的）。注意 BGM 有版权风险。");
		tip.SetToolTip(btnPrepare, "启动并连接 OBS：自动创建/检查采集源、把游戏窗口绑上去（勾了画布选项还会设分辨率）");
		tip.SetToolTip(btnRefresh, "重新检查队列里每个 rep 有没有已录好的视频；视频被你删掉的会变回「等待」，可以重新录");
		tip.SetToolTip(btnRefresh, "重新检查队列里每个 rep 有没有已录好的视频；视频被你删掉的会变回「等待」，可以重新录");
		chkAuto = new CheckBox();
		chkAuto.Text = "拖入后自动开始";
		chkAuto.AutoSize = true;
		chkAuto.Margin = new Padding(6, 8, 12, 0);
		chkAuto.Checked = cfg.AutoStartOnDrop;
		CheckBox checkBox = chkAuto;
		EventHandler value = (object param0, EventArgs param1) =>
		{
			cfg.AutoStartOnDrop = chkAuto.Checked;
			cfg.Save();
		};
		checkBox.CheckedChanged += value;
		chkPrepare = new CheckBox();
		chkPrepare.Text = "自动准备采集源并绑定游戏窗口";
		chkPrepare.AutoSize = true;
		chkPrepare.Margin = new Padding(6, 8, 12, 0);
		chkPrepare.Checked = cfg.AutoPrepareObs;
		chkPrepare.CheckedChanged += (object param0, EventArgs param1) =>
		{
			cfg.AutoPrepareObs = chkPrepare.Checked;
			cfg.Save();
		};
		chkCloseObs = new CheckBox();
		chkCloseObs.Text = "录完关闭 OBS";
		chkCloseObs.AutoSize = true;
		chkCloseObs.Margin = new Padding(6, 8, 12, 0);
		chkCloseObs.Checked = cfg.CloseObsAfter;
		chkCloseObs.CheckedChanged += (object param0, EventArgs param1) =>
		{
			cfg.CloseObsAfter = chkCloseObs.Checked;
			cfg.Save();
		};
		chkRound = new CheckBox();
		chkRound.Text = "轮录（录完一批自动接着录下一批）";
		chkRound.AutoSize = true;
		chkRound.Margin = new Padding(6, 8, 12, 0);
		chkRound.Checked = cfg.RoundRecord;
		chkRound.CheckedChanged += (object param0, EventArgs param1) =>
		{
			cfg.RoundRecord = chkRound.Checked;
			cfg.Save();
			Log.Write(chkRound.Checked ? "【轮录：开】点「开始录制」会把队列里所有待录的 rep 一批接一批全部录完（每批 = 上面「同时录制数量」个，最多 6 个；最后不足一批的按实际数量录；每批 1 个时走单录依次录）" : "【轮录：关】点「开始录制」只录一批（和以前一样）");
		};
		obsState = new Label();
		obsState.AutoSize = true;
		obsState.ForeColor = Color.DimGray;
		obsState.Margin = new Padding(12, 9, 0, 0);
		lblMic = new Label();
		lblMic.Text = "录音模式：";
		lblMic.AutoSize = true;
		lblMic.Margin = new Padding(6, 10, 2, 0);
		cbMic = new ComboBox();
		cbMic.DropDownStyle = ComboBoxStyle.DropDownList;
		cbMic.Width = 180;
		cbMic.Margin = new Padding(0, 5, 0, 0);
		cbMic.Items.Add("只录游戏声音");
		cbMic.Items.Add("游戏声音 + 麦克风");
		cbMic.SelectedIndex = ((cfg.MicMode == "gameAndMic") ? 1 : 0);
		cbMic.SelectedIndexChanged += (object param0, EventArgs param1) =>
		{
			cfg.MicMode = ((cbMic.SelectedIndex == 1) ? "gameAndMic" : "gameOnly");
			cfg.Save();
			Log.Write("录音模式已切换为：" + cbMic.Text + ((cfg.MicMode == "gameAndMic") ? "（录制时会保持/开启麦克风）" : "（录制时会静音麦克风）"));
			if (worker != null && worker.IsAlive && obs.IsConnected)
			{
				Thread thread = new Thread(() =>
				{
					try
					{
						ApplyMicMode(beforeRecording: true);
					}
					catch
					{
					}
				});
				thread.IsBackground = true;
				thread.Start();
			}
		};
		hint = new Label();
		hint.AutoSize = true;
		hint.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
		hint.ForeColor = Color.FromArgb(30, 90, 170);
		hint.Text = "把 .rep 文件（可多选，也可以整个文件夹）拖到这里  ↓";
		hint.Margin = new Padding(6, 8, 16, 0);
		flowLayoutPanel2.Controls.Add(btnStart);
		flowLayoutPanel2.Controls.Add(btnStop);
		flowLayoutPanel2.Controls.Add(btnClear);
		flowLayoutPanel2.Controls.Add(btnRemove);
		flowLayoutPanel2.Controls.Add(btnRefresh);
		flowLayoutPanel2.Controls.Add(btnCheck);
		flowLayoutPanel2.Controls.Add(btnPrepare);
		flowLayoutPanel2.Controls.Add(btnSet);
		flowLayoutPanel2.Controls.Add(btnOpenLog);
		flowLayoutPanel2.Controls.Add(btnSound);
		flowLayoutPanel2.Controls.Add(lblSoundNote);
		flowLayoutPanel2.Controls.Add(btnGameAudio);
		flowLayoutPanel2.Controls.Add(btnBgm);
		FlowLayoutPanel flowLayoutPanel3 = new FlowLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.TopDown,
			WrapContents = false,
			Margin = new Padding(0, 0, 0, 6)
		};
		Label value2 = new Label
		{
			Text = "同时录制数量：",
			AutoSize = true,
			Margin = new Padding(14, 10, 2, 0),
			ForeColor = Color.FromArgb(178, 92, 0),
			Font = new Font(Font, FontStyle.Bold)
		};
		cbParallel = new ComboBox();
		cbParallel.DropDownStyle = ComboBoxStyle.DropDownList;
		cbParallel.Width = 96;
		cbParallel.Margin = new Padding(0, 5, 0, 0);
		cbParallel.BackColor = Color.FromArgb(255, 236, 179);
		cbParallel.Font = new Font(Font, FontStyle.Bold);
		for (int num = 1; num <= 6; num++)
		{
			cbParallel.Items.Add(num.ToString());
		}
		int num2 = cfg.ParallelCount;
		if (num2 < 1)
		{
			num2 = 1;
		}
		if (num2 > 6)
		{
			num2 = 6;
		}
		cbParallel.SelectedIndex = num2 - 1;
		cbParallel.SelectedIndexChanged += (object param0, EventArgs param1) =>
		{
			cfg.ParallelCount = cbParallel.SelectedIndex + 1;
			cfg.Save();
			Log.Write("同时录制数量改为 " + cfg.ParallelCount + " 个（内存约 " + ((double)(cfg.ParallelCount * 550) / 1024.0).ToString("0.0") + " GB）");
		};
		flowLayoutPanel3.Controls.Add(chkAuto);
		flowLayoutPanel3.Controls.Add(chkPrepare);
		flowLayoutPanel3.Controls.Add(chkCloseObs);
		flowLayoutPanel3.Controls.Add(chkRound);
		flowLayoutPanel3.Controls.Add(value2);
		flowLayoutPanel3.Controls.Add(cbParallel);
		lblParHint = new Label();
		lblParHint.AutoSize = true;
		lblParHint.ForeColor = Color.FromArgb(178, 92, 0);
		lblParHint.Margin = new Padding(4, 10, 0, 0);
		flowLayoutPanel3.Controls.Add(lblParHint);
		flowLayoutPanel3.Controls.Add(lblMic);
		flowLayoutPanel3.Controls.Add(cbMic);
		tip.SetToolTip(cbParallel, "一次同时开几个游戏实例（1~6）。\r\n1 = 相当于普通单录；3 个约 1.6 GB 内存；6 个约 3.3 GB。\r\n上限锁在 6：OBS 只有 6 条音轨，再多就没法给每格单独放声音，会录出没声音的视频。");
		tip.SetToolTip(btnStart, "点一下就开始录：\r\n· 列表里选中了 rep → 只录选中的那些（不管之前有没有录过，一律重录）\r\n· 没选中 → 录队列里最靠前的待录 rep\r\n· 要录多个 → 按右边的「同时录制数量」并行，超过就自动分批；只有 1 个就按普通单录走\r\n（原来的「并行录制」「录制选中的」两个按钮已经并进这个按钮了）");
		FlowLayoutPanel value3 = new FlowLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.TopDown,
			WrapContents = false,
			Margin = new Padding(0, 0, 0, 6),
			Controls = 
			{
				(Control)hint,
				(Control)obsState
			}
		};
		flowLayoutPanel.Controls.Add(flowLayoutPanel2);
		flowLayoutPanel.Controls.Add(flowLayoutPanel3);
		flowLayoutPanel.Controls.Add(value3);
		list = new ListView();
		list.Dock = DockStyle.Fill;
		list.View = View.Details;
		list.FullRowSelect = true;
		list.GridLines = false;
		list.HideSelection = false;
		list.BorderStyle = BorderStyle.None;
		list.BackColor = Color.FromArgb(9, 13, 26);
		list.ForeColor = Color.FromArgb(198, 216, 255);
		list.Font = new Font("Microsoft YaHei UI", 9f);
		list.OwnerDraw = true;
		list.DrawColumnHeader += OnHudDrawColumnHeader;
		list.DrawItem += OnHudDrawItem;
		list.DrawSubItem += OnHudDrawSubItem;
		list.Columns.Add("#", 44);
		list.Columns.Add("文件", 380);
		list.Columns.Add("状态", 90);
		list.Columns.Add("用时", 86);
		list.Columns.Add("大小", 90);
		list.Columns.Add("说明", 240);
		list.DoubleClick += OnListDoubleClick;
		logBox = new TextBox();
		logBox.Dock = DockStyle.Fill;
		logBox.Multiline = true;
		logBox.ReadOnly = true;
		logBox.ScrollBars = ScrollBars.Vertical;
		logBox.BorderStyle = BorderStyle.None;
		logBox.BackColor = Color.FromArgb(7, 10, 20);
		logBox.ForeColor = Color.FromArgb(150, 200, 255);
		logBox.Font = new Font("Consolas", 9f);
		SplitContainer split = new SplitContainer();
		split.Dock = DockStyle.Fill;
		split.Orientation = Orientation.Horizontal;
		split.Panel1.Controls.Add(list);
		split.Panel2.Controls.Add(logBox);
		split.BackColor = Color.FromArgb(38, 62, 110);
		split.Panel1.BackColor = Color.FromArgb(9, 13, 26);
		split.Panel2.BackColor = Color.FromArgb(7, 10, 20);
		status = new StatusStrip();
		statusLabel = new ToolStripStatusLabel("就绪");
		status.Items.Add(statusLabel);
		status.BackColor = Color.FromArgb(11, 16, 32);
		status.ForeColor = Color.FromArgb(150, 200, 255);
		statusLabel.ForeColor = Color.FromArgb(150, 200, 255);
		status.SizingGrip = false;
		hudTop = new Panel();
		hudTop.Dock = DockStyle.Top;
		hudTop.Height = 30;
		hudTop.BackColor = Color.FromArgb(11, 16, 32);
		Label value4 = new Label
		{
			Text = "REP → MP4  录制器   (OBS)",
			AutoSize = true,
			ForeColor = Color.FromArgb(120, 190, 255),
			Font = new Font("Consolas", 10f, FontStyle.Bold),
			Location = new Point(12, 7)
		};
		hudTop.Controls.Add(value4);
		obsState.Location = new Point(280, 8);
		obsState.BackColor = Color.Transparent;
		hudTop.Controls.Add(obsState);
		hudTop.Paint += (object s, PaintEventArgs pe) =>
		{
			using Pen pen = new Pen(HudLine);
			pe.Graphics.DrawLine(pen, 0, hudTop.Height - 1, hudTop.Width, hudTop.Height - 1);
		};
		hudLeftTitle = MakeHudSectionTitle("控制面板 / CONTROL", 0);
		flowLayoutPanel.Controls.Add(hudLeftTitle);
		flowLayoutPanel.Controls.SetChildIndex(hudLeftTitle, 0);
		Controls.Add(split);
		Controls.Add(flowLayoutPanel);
		Controls.Add(status);
		Controls.Add(hudTop);
		ApplyHudTheme(this);
		EnableDarkTitleBar();
		try
		{
			cbParallel.Width = 96;
			cbParallel.BackColor = Color.FromArgb(52, 40, 12);
			cbParallel.ForeColor = Color.FromArgb(255, 214, 120);
			cbMic.Width = 210;
			lblParHint.ForeColor = Color.FromArgb(255, 196, 90);
			if (hint != null)
			{
				hint.ForeColor = Color.FromArgb(120, 190, 255);
			}
		}
		catch
		{
		}
		Log.OnLine += OnLogLine;
		FormClosing += OnFormClosing;
		list.ItemSelectionChanged += (object param0, ListViewItemSelectionChangedEventArgs param1) =>
		{
			if (!suppressSelSync)
			{
				if (list.SelectedItems.Count > 0)
				{
					pinnedBatch.Clear();
				}
				SyncParallelCountToSelection();
			}
		};
		list.MouseDown += (object s, MouseEventArgs me) =>
		{
			KeepSelectionOnEmptyClick(me);
		};
		list.MouseUp += (object s, MouseEventArgs me) =>
		{
			KeepSelectionOnEmptyClick(me);
		};
		cbParallel.MouseWheel += (object s, MouseEventArgs me) =>
		{
			if (me is HandledMouseEventArgs e)
			{
				e.Handled = true;
			}
		};
		soundTimer = new System.Windows.Forms.Timer();
		soundTimer.Interval = 2000;
		soundTimer.Tick += (object param0, EventArgs param1) =>
		{
			RefreshSoundButtons();
		};
		soundTimer.Start();
		Shown += (object param0, EventArgs param1) =>
		{
			try
			{
				split.SplitterDistance = 380;
			}
			catch
			{
			}
			mainForm.cfg.Save();
			mainForm.RefreshEnvLabel();
			mainForm.RefreshSoundButtons();
			mainForm.RefreshGameAudioButton();
			mainForm.btnStop.Enabled = false;
			mainForm.btnStart.Enabled = true;
			mainForm.SetStatusText("就绪");
			mainForm.RestoreSoundIfLeftMuted();
			Log.RotateOnStart();
			Log.Write("==== Rep → MP4 录制器启动 ====");
			Log.Write("游戏目录：" + (string.IsNullOrEmpty(mainForm.cfg.GameDir) ? "<未找到>" : mainForm.cfg.GameDir));
			Log.Write("OBS     ：" + (string.IsNullOrEmpty(mainForm.cfg.ObsExe) ? "<未找到>" : mainForm.cfg.ObsExe));
			mainForm.SyncBgmFromIni();
			if (args != null && args.Length > 0)
			{
				mainForm.AddPaths(args);
				if (mainForm.cfg.AutoStartOnDrop)
				{
					mainForm.StartRecording();
				}
			}
		};
	}

	private double AnalyzeCellHead(string grid, int x, int y, int cw, int ch)
	{
		string text = FindFfmpeg();
		if (text == null)
		{
			return 0.0;
		}
		string text2 = Path.Combine(Path.GetTempPath(), "RepRecorder_scan_" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(text2);
			string text3 = "crop=" + cw + ":" + ch + ":" + x + ":" + y + ",scale=160:120,fps=5";
			ProcessStartInfo processStartInfo = new ProcessStartInfo(text, "-hide_banner -loglevel error -y -t 45 -i \"" + grid + "\" -vf \"" + text3 + "\" -q:v 6 \"" + Path.Combine(text2, "h%04d.jpg") + "\"");
			processStartInfo.UseShellExecute = false;
			processStartInfo.CreateNoWindow = true;
			processStartInfo.WorkingDirectory = Path.GetDirectoryName(text);
			Process process = Process.Start(processStartInfo);
			process.WaitForExit();
			string[] files = Directory.GetFiles(text2, "h*.jpg");
			Array.Sort(files, StringComparer.Ordinal);
			for (int i = 0; i < files.Length; i++)
			{
				try
				{
					if (!IsBlank(File.ReadAllBytes(files[i])))
					{
						return (double)i / 5.0;
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		finally
		{
			try
			{
				if (Directory.Exists(text2))
				{
					Directory.Delete(text2, recursive: true);
				}
			}
			catch
			{
			}
		}
		return 0.0;
	}

	private static double Mp4Duration(string path)
	{
		try
		{
			using FileStream fileStream = File.OpenRead(path);
			long length = fileStream.Length;
			byte[] array = new byte[8];
			long num2;
			for (long num = 0L; num + 8 <= length; num += num2)
			{
				fileStream.Position = num;
				if (fileStream.Read(array, 0, 8) != 8)
				{
					break;
				}
				num2 = (long)(((ulong)array[0] << 24) | ((ulong)array[1] << 16) | ((ulong)array[2] << 8) | array[3]);
				string text = Encoding.ASCII.GetString(array, 4, 4);
				long num3 = 8L;
				switch (num2)
				{
				case 1L:
				{
					byte[] array2 = new byte[8];
					if (fileStream.Read(array2, 0, 8) != 8)
					{
						goto end_IL_037c;
					}
					num2 = 0L;
					for (int i = 0; i < 8; i++)
					{
						num2 = (num2 << 8) | array2[i];
					}
					num3 = 16L;
					break;
				}
				case 0L:
					num2 = length - num;
					break;
				}
				if (num2 < num3)
				{
					break;
				}
				if (text == "moov")
				{
					long num4 = Math.Min(num + num2, length);
					long num6;
					for (long num5 = num + num3; num5 + 8 <= num4; num5 += num6)
					{
						fileStream.Position = num5;
						if (fileStream.Read(array, 0, 8) != 8)
						{
							break;
						}
						num6 = (long)(((ulong)array[0] << 24) | ((ulong)array[1] << 16) | ((ulong)array[2] << 8) | array[3]);
						string text2 = Encoding.ASCII.GetString(array, 4, 4);
						long num7 = 8L;
						if (num6 == 1)
						{
							byte[] array2 = new byte[8];
							if (fileStream.Read(array2, 0, 8) != 8)
							{
								break;
							}
							num6 = 0L;
							for (int i = 0; i < 8; i++)
							{
								num6 = (num6 << 8) | array2[i];
							}
							num7 = 16L;
						}
						else if (num6 == 0)
						{
							num6 = num4 - num5;
						}
						if (text2 == "mvhd")
						{
							byte[] array3 = new byte[32];
							fileStream.Position = num5 + num7;
							int num8 = fileStream.Read(array3, 0, 32);
							if (num8 < 20)
							{
								return 0.0;
							}
							if (array3[0] == 1)
							{
								long num9 = (long)(((ulong)array3[20] << 24) | ((ulong)array3[21] << 16) | ((ulong)array3[22] << 8) | array3[23]);
								long num10 = 0L;
								for (int i = 24; i < 32; i++)
								{
									num10 = (num10 << 8) | array3[i];
								}
								return (num9 > 0) ? ((double)num10 / (double)num9) : 0.0;
							}
							long num11 = (long)(((ulong)array3[12] << 24) | ((ulong)array3[13] << 16) | ((ulong)array3[14] << 8) | array3[15]);
							long num12 = (long)(((ulong)array3[16] << 24) | ((ulong)array3[17] << 16) | ((ulong)array3[18] << 8) | array3[19]);
							return (num11 > 0) ? ((double)num12 / (double)num11) : 0.0;
						}
						if (num6 <= 0)
						{
							break;
						}
					}
					return 0.0;
				}
				if (num2 <= 0)
				{
					break;
				}
				continue;
				end_IL_037c:
				break;
			}
		}
		catch
		{
		}
		return 0.0;
	}

	private static string FmtDur(double seconds)
	{
		if (double.IsNaN(seconds) || seconds < 0.0)
		{
			seconds = 0.0;
		}
		long num = (long)Math.Round(seconds);
		long num2 = num / 3600;
		long num3 = num % 3600 / 60;
		long num4 = num % 60;
		if (num2 > 0)
		{
			return num2 + "小时" + num3.ToString("00") + "分" + num4.ToString("00") + "秒";
		}
		if (num3 > 0)
		{
			return num3 + "分" + num4.ToString("00") + "秒";
		}
		return num4 + "秒";
	}

	private Label MakeHudSectionTitle(string text, int width)
	{
		Label label = new Label();
		label.Text = "| " + text + " |";
		label.AutoSize = true;
		label.ForeColor = HudAccent;
		label.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
		label.Margin = new Padding(0, 2, 0, 4);
		if (width > 0)
		{
			label.Width = width;
		}
		return label;
	}

	private static Color HudMapBack(Color c)
	{
		if (c == Color.FromArgb(0, 120, 60))
		{
			return HudGreen;
		}
		if (c == Color.FromArgb(160, 60, 60))
		{
			return HudRed;
		}
		if (c == Color.FromArgb(190, 60, 60))
		{
			return HudRed;
		}
		if (c == Color.FromArgb(120, 120, 120))
		{
			return HudGrey;
		}
		if (c == Color.FromArgb(180, 180, 180))
		{
			return Color.FromArgb(28, 36, 58);
		}
		return HudPanel;
	}

	private void ApplyHudTheme(Control root)
	{
		try
		{
			foreach (Control control in root.Controls)
			{
				if (control is Button button)
				{
					bool flag = button.BackColor != SystemColors.Control && button.BackColor != Color.Empty && button.BackColor != HudPanel && button.BackColor != Color.Transparent;
					button.FlatStyle = FlatStyle.Flat;
					button.FlatAppearance.BorderColor = HudLine;
					button.FlatAppearance.BorderSize = 1;
					button.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 46, 84);
					button.FlatAppearance.MouseDownBackColor = Color.FromArgb(20, 32, 60);
					if (flag)
					{
						button.BackColor = HudMapBack(button.BackColor);
					}
					else
					{
						button.BackColor = HudPanel;
					}
					if (button.ForeColor != Color.White && button.ForeColor != Color.DimGray)
					{
						button.ForeColor = HudText;
					}
					button.Font = new Font("Microsoft YaHei UI", 9f);
				}
				if (control is CheckBox checkBox)
				{
					checkBox.ForeColor = HudText;
					checkBox.BackColor = Color.Transparent;
					checkBox.FlatStyle = FlatStyle.Flat;
					checkBox.Font = new Font("Microsoft YaHei UI", 9f);
				}
				if (control is ComboBox comboBox)
				{
					comboBox.FlatStyle = FlatStyle.Flat;
					comboBox.BackColor = Color.FromArgb(18, 26, 46);
					comboBox.ForeColor = HudText;
					comboBox.Font = new Font("Microsoft YaHei UI", 9f);
				}
				if (control is Label label)
				{
					if (label.BackColor != Color.Transparent)
					{
						label.BackColor = Color.Transparent;
					}
					if (label != hudLeftTitle && label.ForeColor != HudAccent)
					{
						bool flag2 = label.ForeColor == Color.DimGray;
						label.ForeColor = (flag2 ? HudDim : HudText);
					}
				}
				if (control is FlowLayoutPanel flowLayoutPanel)
				{
					flowLayoutPanel.BackColor = Color.Transparent;
				}
				if (control is Panel panel && panel != hudTop)
				{
					panel.BackColor = HudBg;
				}
				ApplyHudTheme(control);
			}
		}
		catch
		{
		}
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

	private void EnableDarkTitleBar()
	{
		try
		{
			int val = 1;
			DwmSetWindowAttribute(Handle, 20, ref val, 4);
			DwmSetWindowAttribute(Handle, 19, ref val, 4);
		}
		catch
		{
		}
	}

	private void OnHudDrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
	{
		using (SolidBrush brush = new SolidBrush(Color.FromArgb(16, 24, 44)))
		{
			e.Graphics.FillRectangle(brush, e.Bounds);
		}
		using (Pen pen = new Pen(HudLine))
		{
			e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
		}
		TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, e.Bounds, HudAccent, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
	}

	private void OnHudDrawItem(object sender, DrawListViewItemEventArgs e)
	{
		Color color;
		if (e.Item.Selected)
		{
			color = Color.FromArgb(30, 62, 124);
		}
		else if (e.Item.BackColor == Color.Empty || e.Item.BackColor == Color.White)
		{
			color = HudBg;
		}
		else
		{
			color = ((!(e.Item.BackColor == Color.FromArgb(214, 244, 214))) ? HudBg : Color.FromArgb(20, 68, 48));
		}
		using (SolidBrush brush = new SolidBrush(color))
		{
			e.Graphics.FillRectangle(brush, e.Bounds);
		}
		if (e.Item.Index % 2 == 1 && !e.Item.Selected && color == HudBg)
		{
			using (SolidBrush brush = new SolidBrush(Color.FromArgb(12, 18, 34)))
			{
				e.Graphics.FillRectangle(brush, e.Bounds);
			}
		}
	}

	private void OnHudDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
	{
		Color color = e.Item.ForeColor;
		if (color == Color.Black || color == Color.Empty)
		{
			color = HudText;
		}
		else if (color == Color.FromArgb(0, 120, 0))
		{
			color = Color.FromArgb(110, 230, 160);
		}
		else if (color == Color.FromArgb(180, 0, 0))
		{
			color = Color.FromArgb(255, 130, 130);
		}
		else if (color == Color.FromArgb(190, 120, 0))
		{
			color = Color.FromArgb(255, 200, 110);
		}
		Rectangle bounds = e.Bounds;
		if (e.ColumnIndex == 0)
		{
			bounds = new Rectangle(bounds.Left + 6, bounds.Top, bounds.Width - 6, bounds.Height);
		}
		TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, bounds, color, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
	}

	private Button MakeButton(string text, int w, EventHandler h, Color back)
	{
		Button button = new Button();
		button.Text = text;
		button.Size = new Size(w, 28);
		button.Margin = new Padding(2, 2, 2, 2);
		button.Click += h;
		if (back != Color.Empty)
		{
			button.BackColor = back;
			button.ForeColor = Color.White;
			button.FlatStyle = FlatStyle.Flat;
		}
		return button;
	}

	private void OnDragEnter(object sender, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			e.Effect = DragDropEffects.Copy;
			hint.Text = "松手即加入队列  ↓";
		}
	}

	private void OnDragDrop(object sender, DragEventArgs e)
	{
		hint.Text = "把 .rep 文件（可多选，也可以整个文件夹）拖到这里  ↓";
		string[] array = (string[])e.Data.GetData(DataFormats.FileDrop);
		if (array != null && array.Length != 0)
		{
			AddPaths(array);
			if (cfg.AutoStartOnDrop)
			{
				StartRecording();
			}
		}
	}

	private void AddPaths(string[] paths)
	{
		List<string> reps = new List<string>();
		foreach (string text in paths)
		{
			try
			{
				if (Directory.Exists(text))
				{
					reps.AddRange(Directory.GetFiles(text, "*.rep", SearchOption.AllDirectories));
				}
				else if (File.Exists(text) && text.ToLower().EndsWith(".rep"))
				{
					reps.Add(text);
				}
			}
			catch (Exception ex)
			{
				Log.Write("读取 " + text + " 失败：" + ex.Message);
			}
		}
		reps.Sort(StringComparer.OrdinalIgnoreCase);
		int added = 0;
		List<Job> already = new List<Job>();
		Ui(() =>
		{
			lock (jobsLock)
			{
				foreach (string item in reps)
				{
					bool flag = false;
					foreach (Job job2 in jobs)
					{
						if (string.Equals(job2.Rep, item, StringComparison.OrdinalIgnoreCase))
						{
							flag = true;
							break;
						}
					}
					if (!flag)
					{
						Job job = new Job(item);
						string text2 = FindExistingOutput(job);
						if (text2 != null)
						{
							job.Output = text2;
							try
							{
								job.Size = new FileInfo(text2).Length;
							}
							catch
							{
							}
							job.Status = "已录制";
							job.Detail = "已有视频，点开始会跳过";
							already.Add(job);
						}
						job.Item = new ListViewItem(new string[6]
						{
							(jobs.Count + 1).ToString(),
							Path.GetFileName(item),
							job.Status,
							"",
							(job.Size > 0) ? (((double)job.Size / 1048576.0).ToString("0.0") + " MB") : "",
							job.Detail
						});
						job.Item.Tag = job;
						if (job.Status == "已录制")
						{
							job.Item.ForeColor = Color.FromArgb(0, 120, 0);
						}
						jobs.Add(job);
						list.Items.Add(job.Item);
						added++;
					}
				}
			}
		});
		if (added > 0)
		{
			Log.Write("加入队列 " + added + " 个 rep");
		}
		else
		{
			Log.Write("没有可加入的 rep（重复或不是 .rep）");
		}
		if (already.Count > 0)
		{
			AskReRecord(already);
		}
		AfterQueueChanged(added);
	}

	private void AskReRecord(List<Job> already)
	{
		try
		{
			string text = ((already.Count == 1) ? Path.GetFileName(already[0].Rep) : (Path.GetFileName(already[0].Rep) + " 等 " + already.Count + " 个"));
			string text2 = "检测到 " + already.Count + " 个 rep 已经录制过了（旁边/上次的文件夹里已有同名视频）：\r\n\r\n" + text + "\r\n\r\n要重新录制吗？\r\n  是 = 这些文件重录（会覆盖原来的视频）\r\n  否 = 跳过它们，只录没有录过的";
			Log.Write("检测到 " + already.Count + " 个 rep 已经录制过，正在询问是否重录……");
			DialogResult dialogResult = MessageBox.Show(this, text2, "这些 rep 已经录制过了", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
			if (dialogResult == DialogResult.Yes)
			{
				foreach (Job item in already)
				{
					item.Force = true;
					SetJob(item, "等待", "你选了重录（会覆盖原视频）");
				}
				Log.Write("你选择重录：" + already.Count + " 个（状态已改成「等待」，会覆盖原来的视频）");
				return;
			}
			foreach (Job item2 in already)
			{
				SetJob(item2, "已录制", "已有视频（你选择跳过）");
			}
			Log.Write("你选择跳过：" + already.Count + " 个已录制过的 rep");
		}
		catch (Exception ex)
		{
			Log.Write("询问重录失败：" + ex.Message);
		}
	}

	private void AfterQueueChanged(int added)
	{
		try
		{
			if (added > 0)
			{
				int num = Math.Min(6, added);
				if (cfg.ParallelCount != num)
				{
					cfg.ParallelCount = num;
					cfg.Save();
					if (cbParallel != null && cbParallel.SelectedIndex != num - 1)
					{
						cbParallel.SelectedIndex = num - 1;
					}
					Log.Write("放进来 " + added + " 个 rep → 「同时录制数量」自动设为 " + num + ((added > 6) ? "（一次最多 6 个，剩下的会自动排到下一批）" : ""));
				}
			}
			HighlightBatchRows();
			try
			{
				SetStatusText("就绪 · 本批 " + Math.Max(1, Math.Min(6, cfg.ParallelCount)) + " 个（列表里浅绿高亮的就是）");
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void HighlightBatchRows()
	{
		try
		{
			if (this.list == null)
			{
				return;
			}
			if (batchFont == null)
			{
				batchFont = new Font(this.list.Font, FontStyle.Bold);
			}
			int num = Math.Max(1, Math.Min(6, cfg.ParallelCount));
			List<Job> list = new List<Job>();
			foreach (ListViewItem selectedItem in this.list.SelectedItems)
			{
				if (selectedItem.Tag is Job item && !list.Contains(item))
				{
					list.Add(item);
				}
			}
			int num2 = 0;
			lock (jobsLock)
			{
				foreach (Job job in jobs)
				{
					if (job.Item != null)
					{
						bool flag;
						if (list.Count > 0)
						{
							flag = list.Contains(job);
						}
						else if (pinnedBatch != null && pinnedBatch.Count > 0)
						{
							flag = pinnedBatch.Contains(job);
						}
						else if (job.Status == "等待" && num2 < num)
						{
							flag = true;
							num2++;
						}
						else
						{
							flag = false;
						}
						job.Item.BackColor = (flag ? Color.FromArgb(214, 244, 214) : Color.Empty);
						job.Item.Font = (flag ? batchFont : this.list.Font);
					}
				}
			}
		}
		catch
		{
		}
	}

	private void StartQueue()
	{
		if (worker != null && worker.IsAlive)
		{
			Log.Write("队列已在运行，新加入的会排在后面");
			return;
		}
		cancel = false;
		worker = new Thread(RunQueue);
		worker.IsBackground = true;
		worker.Start();
	}

	private void RunQueue()
	{
		try
		{
			SetStatusText("准备 OBS…");
			if (!EnsureObs())
			{
				MarkPendingFailed("无法使用 OBS");
				return;
			}
			if (parallelRequested)
			{
				parallelRequested = false;
				RunParallel();
				return;
			}
			if (cfg.AutoPrepareObs)
			{
				PrepareObs(verbose: false);
			}
			if (cfg.AutoPrepareObs && cfg.UseDedicatedScene)
			{
				SwitchScene(toOurs: true);
			}
			try
			{
				if (originalRecordDir == null)
				{
					CaptureOriginalRecordDir();
				}
			}
			catch
			{
			}
			ApplyMicMode(beforeRecording: true);
			ApplyAudioRouting(beforeRecording: true);
			while (!cancel)
			{
				Job job = null;
				lock (jobsLock)
				{
					foreach (Job job2 in jobs)
					{
						if (job2.Status == "等待")
						{
							job = job2;
							break;
						}
					}
				}
				if (job == null)
				{
					break;
				}
				ProcessJob(job);
			}
		}
		catch (Exception ex)
		{
			Log.Write("队列异常终止：" + ex.Message);
		}
		finally
		{
			if (weMutedSystem)
			{
				try
				{
					if (GameAudio.SetSystemMute(mute: false, out var _))
					{
						weMutedSystem = false;
						Log.Write("录制结束：已自动恢复系统声音（录制时你按过静音）");
					}
				}
				catch
				{
				}
			}
			try
			{
				ApplyMicMode(beforeRecording: false);
			}
			catch
			{
			}
			try
			{
				ApplyAudioRouting(beforeRecording: false);
			}
			catch
			{
			}
			try
			{
				if (cfg.AutoPrepareObs && cfg.UseDedicatedScene && obs.IsConnected)
				{
					SwitchScene(toOurs: false);
				}
			}
			catch
			{
			}
			try
			{
				RestoreRecordDirectory();
			}
			catch
			{
			}
			try
			{
				obs.Dispose();
			}
			catch
			{
			}
			Ui(() =>
			{
				btnStart.Enabled = true;
				btnStop.Enabled = false;
			});
			if (cancel)
			{
				SetStatusText("已停止");
			}
			else
			{
				SetStatusText("队列结束");
			}
			Log.Write("==== 队列结束 ====");
			if (!cancel && cfg.CloseObsAfter)
			{
				try
				{
					Process[] processesByName = Process.GetProcessesByName("obs64");
					foreach (Process process in processesByName)
					{
						process.CloseMainWindow();
					}
					Log.Write("已关闭 OBS（你勾选了「录完关闭 OBS」）");
				}
				catch
				{
				}
			}
		}
	}

	private void MarkPendingFailed(string reason)
	{
		Ui(() =>
		{
			lock (jobsLock)
			{
				foreach (Job job in jobs)
				{
					if (job.Status == "等待")
					{
						SetJob(job, "失败", reason);
					}
				}
			}
		});
	}

	private bool EnsureObs()
	{
		string text = Settings.FindObsExe();
		if (!string.IsNullOrEmpty(cfg.ObsExe) && File.Exists(cfg.ObsExe))
		{
			text = cfg.ObsExe;
		}
		if (string.IsNullOrEmpty(text))
		{
			Log.Write("找不到 obs64.exe —— 先安装 OBS：  winget install --id OBSProject.OBSStudio -e");
			return false;
		}
		cfg.ObsExe = text;
		if (Process.GetProcessesByName("obs64").Length == 0)
		{
			Settings.EnsureWebSocketEnabled(out var message);
			Log.Write(message);
			Log.Write("OBS 未运行，正在启动：" + text);
			ProcessStartInfo processStartInfo = new ProcessStartInfo(text, "--minimize-to-tray");
			processStartInfo.UseShellExecute = false;
			processStartInfo.WorkingDirectory = Path.GetDirectoryName(text);
			Process.Start(processStartInfo);
			Thread.Sleep(3000);
		}
		DateTime dateTime = DateTime.Now.AddSeconds(60.0);
		Exception ex = null;
		while (DateTime.Now < dateTime)
		{
			if (cancel)
			{
				return false;
			}
			try
			{
				obs.MetersEnabled = true;
				obs.Connect(cfg.Port, cfg.Password);
				return true;
			}
			catch (Exception ex2)
			{
				ex = ex2;
				Thread.Sleep(2000);
			}
		}
		Log.Write("连接 obs-websocket 失败：" + ((ex == null) ? "超时" : ex.Message));
		Log.Write("请检查：OBS → 工具 → WebSocket 服务器设置 → 启用（端口 " + cfg.Port + "）");
		return false;
	}

	private void PrepareObs(bool verbose)
	{
		try
		{
			StopObsRecordingIfAny();
			if (cfg.AutoCanvas)
			{
				Dictionary<string, object> dictionary = new Dictionary<string, object>();
				dictionary["baseWidth"] = cfg.CanvasWidth;
				dictionary["baseHeight"] = cfg.CanvasHeight;
				dictionary["outputWidth"] = cfg.CanvasWidth;
				dictionary["outputHeight"] = cfg.CanvasHeight;
				dictionary["fpsNumerator"] = cfg.CanvasFps;
				dictionary["fpsDenominator"] = 1;
				obs.Request("SetVideoSettings", dictionary);
				Log.Write("OBS 画布/输出已设为 " + cfg.CanvasWidth + "×" + cfg.CanvasHeight + "@" + cfg.CanvasFps);
			}
			try
			{
				originalScene = Convert.ToString(obs.Request("GetSceneList", null)["currentProgramSceneName"]);
			}
			catch
			{
				originalScene = null;
			}
			List<object> list = ObsClient.ToList(obs.Request("GetInputList", null)["inputs"]);
			if (!cfg.UseDedicatedScene)
			{
				string text = null;
				foreach (object item in list)
				{
					Dictionary<string, object> dictionary2 = (Dictionary<string, object>)item;
					string text2 = Convert.ToString(dictionary2["unversionedInputKind"]);
					switch (text2)
					{
					default:
						goto IL_020c;
					case "game_capture":
					case "window_capture":
					case "monitor_capture":
						break;
					}
					goto IL_0225;
					IL_0225:
					text = Convert.ToString(dictionary2["inputName"]);
					break;
					IL_020c:
					if (!(text2 == "screen_capture"))
					{
						continue;
					}
					goto IL_0225;
				}
				if (text != null)
				{
					captureSource = text;
					captureSourceIsOurs = false;
					try
					{
						captureScene = Convert.ToString(obs.Request("GetSceneList", null)["currentProgramSceneName"]);
					}
					catch
					{
						captureScene = null;
					}
					Log.Write("使用当前场景里已有的采集源：" + text);
					return;
				}
			}
			bool flag = false;
			try
			{
				List<object> list2 = ObsClient.ToList(obs.Request("GetSceneList", null)["scenes"]);
				foreach (object item2 in list2)
				{
					Dictionary<string, object> dictionary3 = (Dictionary<string, object>)item2;
					if (Convert.ToString(dictionary3["sceneName"]) == "RepRecorder 录制场景")
					{
						flag = true;
						break;
					}
				}
			}
			catch
			{
			}
			if (!flag)
			{
				Dictionary<string, object> dictionary4 = new Dictionary<string, object>();
				dictionary4["sceneName"] = "RepRecorder 录制场景";
				obs.Request("CreateScene", dictionary4);
				Log.Write("已创建录制专用场景：RepRecorder 录制场景");
			}
			bool flag2 = false;
			foreach (object item3 in list)
			{
				Dictionary<string, object> dictionary2 = (Dictionary<string, object>)item3;
				if (Convert.ToString(dictionary2["inputName"]) == "RepRecorder 游戏窗口")
				{
					flag2 = true;
					break;
				}
			}
			if (!flag2)
			{
				Dictionary<string, object> dictionary5 = new Dictionary<string, object>();
				dictionary5["method"] = 0;
				dictionary5["client_area"] = true;
				dictionary5["cursor"] = false;
				Dictionary<string, object> dictionary6 = new Dictionary<string, object>();
				dictionary6["sceneName"] = "RepRecorder 录制场景";
				dictionary6["inputName"] = "RepRecorder 游戏窗口";
				dictionary6["inputKind"] = "window_capture";
				dictionary6["inputSettings"] = dictionary5;
				dictionary6["sceneItemEnabled"] = true;
				obs.Request("CreateInput", dictionary6);
				Log.Write("已创建窗口采集源：RepRecorder 游戏窗口");
			}
			int num = -1;
			int num2 = 0;
			try
			{
				Dictionary<string, object> dictionary7 = new Dictionary<string, object>();
				dictionary7["sceneName"] = "RepRecorder 录制场景";
				List<object> list3 = ObsClient.ToList(obs.Request("GetSceneItemList", dictionary7)["sceneItems"]);
				foreach (object item4 in list3)
				{
					Dictionary<string, object> dictionary2 = (Dictionary<string, object>)item4;
					if (Convert.ToString(dictionary2["sourceName"]) != "RepRecorder 游戏窗口")
					{
						continue;
					}
					int num3 = Convert.ToInt32(dictionary2["sceneItemId"]);
					if (num < 0)
					{
						num = num3;
						continue;
					}
					try
					{
						Dictionary<string, object> dictionary8 = new Dictionary<string, object>();
						dictionary8["sceneName"] = "RepRecorder 录制场景";
						dictionary8["sceneItemId"] = num3;
						obs.Request("RemoveSceneItem", dictionary8);
						num2++;
					}
					catch
					{
					}
				}
			}
			catch (Exception ex)
			{
				Log.Write("读取场景元素失败：" + ex.Message);
			}
			if (num2 > 0)
			{
				Log.Write("清理了 " + num2 + " 个重复的采集源元素（之前重复添加导致的画面叠加）");
			}
			if (num >= 0)
			{
				try
				{
					Dictionary<string, object> dictionary9 = new Dictionary<string, object>();
					dictionary9["sceneName"] = "RepRecorder 录制场景";
					dictionary9["sceneItemId"] = num;
					if (!Convert.ToBoolean(obs.Request("GetSceneItemEnabled", dictionary9)["sceneItemEnabled"]))
					{
						SetSceneItemEnabled("RepRecorder 录制场景", num, enabled: true);
						Log.Write("画面采集源原本处于隐藏状态（多半是上次并行录制被强制结束留下的），已重新显示");
					}
				}
				catch
				{
				}
			}
			if (num < 0)
			{
				try
				{
					Dictionary<string, object> dictionary6 = new Dictionary<string, object>();
					dictionary6["sceneName"] = "RepRecorder 录制场景";
					dictionary6["sourceName"] = "RepRecorder 游戏窗口";
					dictionary6["sceneItemEnabled"] = true;
					Dictionary<string, object> dictionary10 = obs.Request("CreateSceneItem", dictionary6);
					if (dictionary10.ContainsKey("sceneItemId"))
					{
						num = Convert.ToInt32(dictionary10["sceneItemId"]);
					}
				}
				catch (Exception ex)
				{
					Log.Write("添加采集源到场景失败：" + ex.Message);
				}
			}
			if (cfg.GameAudioOnly)
			{
				bool flag3 = false;
				foreach (object item5 in list)
				{
					Dictionary<string, object> dictionary2 = (Dictionary<string, object>)item5;
					if (Convert.ToString(dictionary2["inputName"]) == "RepRecorder 游戏声音")
					{
						flag3 = true;
						break;
					}
				}
				if (!flag3)
				{
					Dictionary<string, object> dictionary11 = new Dictionary<string, object>();
					dictionary11["window"] = "";
					Dictionary<string, object> dictionary12 = new Dictionary<string, object>();
					dictionary12["sceneName"] = "RepRecorder 录制场景";
					dictionary12["inputName"] = "RepRecorder 游戏声音";
					dictionary12["inputKind"] = "wasapi_process_output_capture";
					dictionary12["inputSettings"] = dictionary11;
					dictionary12["sceneItemEnabled"] = true;
					obs.Request("CreateInput", dictionary12);
					Log.Write("已创建应用程序音频采集源（只录游戏声音）：RepRecorder 游戏声音");
				}
				else
				{
					try
					{
						Dictionary<string, object> dictionary6 = new Dictionary<string, object>();
						dictionary6["sceneName"] = "RepRecorder 录制场景";
						dictionary6["sourceName"] = "RepRecorder 游戏声音";
						dictionary6["sceneItemEnabled"] = true;
						obs.Request("CreateSceneItem", dictionary6);
					}
					catch
					{
					}
				}
				CleanupDuplicateItems("RepRecorder 录制场景", "RepRecorder 游戏声音");
				ApplyGameAudioSetting();
			}
			captureSource = "RepRecorder 游戏窗口";
			captureSourceIsOurs = true;
			captureScene = "RepRecorder 录制场景";
			FitSourceToCanvas("RepRecorder 录制场景", num);
		}
		catch (Exception ex)
		{
			Log.Write("准备 OBS 时出错：" + ex.Message);
		}
	}

	private void SwitchScene(bool toOurs)
	{
		try
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			Dictionary<string, object> dictionary2 = dictionary;
			string value;
			if (toOurs)
			{
				value = "RepRecorder 录制场景";
			}
			else
			{
				value = ((originalScene == null) ? "RepRecorder 录制场景" : originalScene);
			}
			dictionary2["sceneName"] = value;
			obs.Request("SetCurrentProgramScene", dictionary);
			if (!toOurs)
			{
				Log.Write("已切回原来的场景：" + originalScene);
			}
		}
		catch (Exception ex)
		{
			Log.Write("切换场景失败：" + ex.Message);
		}
	}

	private void RestoreRecordDirectory()
	{
		if (originalRecordDir != null && obs.IsConnected && !string.IsNullOrEmpty(originalRecordDir))
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary["recordDirectory"] = originalRecordDir;
			obs.Request("SetRecordDirectory", dictionary);
			Log.Write("已还原 OBS 录像目录：" + originalRecordDir);
		}
	}

	private void ApplyMicMode(bool beforeRecording)
	{
		try
		{
			if (beforeRecording)
			{
				bool flag = cfg.MicMode != "gameAndMic";
				List<string> list = Program.FindMicTargets(obs);
				if (list.Count == 0)
				{
					Log.Write("没找到麦克风设备，跳过");
					return;
				}
				{
					foreach (string item in list)
					{
						try
						{
							Dictionary<string, object> dictionary = new Dictionary<string, object>();
							dictionary["inputName"] = item;
							bool flag2 = Convert.ToBoolean(obs.Request("GetInputMute", dictionary)["inputMuted"]);
							if (!micOriginalMute.ContainsKey(item))
							{
								micOriginalMute[item] = flag2;
							}
							if (flag2 == flag)
							{
								Log.Write("麦克风 " + item + "：" + (flag2 ? "已静音" : "已开启") + "（符合当前录音模式，不用改）");
							}
							else
							{
								Dictionary<string, object> dictionary2 = new Dictionary<string, object>();
								dictionary2["inputName"] = item;
								dictionary2["inputMuted"] = flag;
								obs.Request("SetInputMute", dictionary2);
								Log.Write(flag ? ("已静音麦克风（录制期间只留游戏声音）：" + item) : ("已开启麦克风（录制期间会录进解说）：" + item));
							}
						}
						catch (Exception ex)
						{
							Log.Write("设置麦克风 " + item + " 失败：" + ex.Message);
						}
					}
					return;
				}
			}
			foreach (KeyValuePair<string, bool> item2 in micOriginalMute)
			{
				try
				{
					Dictionary<string, object> dictionary2 = new Dictionary<string, object>();
					dictionary2["inputName"] = item2.Key;
					dictionary2["inputMuted"] = item2.Value;
					obs.Request("SetInputMute", dictionary2);
				}
				catch
				{
				}
			}
			if (micOriginalMute.Count > 0)
			{
				Log.Write("已把麦克风恢复成录制前的状态");
			}
			micOriginalMute.Clear();
		}
		catch (Exception ex)
		{
			Log.Write("处理麦克风失败（可忽略）：" + ex.Message);
		}
	}

	private void ApplyAudioRouting(bool beforeRecording)
	{
		if (!cfg.GameAudioOnly && cfg.RecordGameAudio)
		{
			return;
		}
		try
		{
			if (beforeRecording)
			{
				List<string> list = Program.FindDesktopTargets(obs);
				{
					foreach (string item in list)
					{
						try
						{
							Dictionary<string, object> dictionary = new Dictionary<string, object>();
							dictionary["inputName"] = item;
							bool flag = Convert.ToBoolean(obs.Request("GetInputMute", dictionary)["inputMuted"]);
							if (!deskOriginalMute.ContainsKey(item))
							{
								deskOriginalMute[item] = flag;
							}
							if (!flag)
							{
								Dictionary<string, object> dictionary2 = new Dictionary<string, object>();
								dictionary2["inputName"] = item;
								dictionary2["inputMuted"] = true;
								obs.Request("SetInputMute", dictionary2);
								Log.Write("已静音 OBS 桌面音频（" + (cfg.RecordGameAudio ? "避免录进其他系统声音/重复" : "「录打击音效」已关，保证录像无声") + "）：" + item);
							}
						}
						catch (Exception ex)
						{
							Log.Write("静音桌面音频 " + item + " 失败：" + ex.Message);
						}
					}
					return;
				}
			}
			foreach (KeyValuePair<string, bool> item2 in deskOriginalMute)
			{
				try
				{
					Dictionary<string, object> dictionary2 = new Dictionary<string, object>();
					dictionary2["inputName"] = item2.Key;
					dictionary2["inputMuted"] = item2.Value;
					obs.Request("SetInputMute", dictionary2);
				}
				catch
				{
				}
			}
			if (deskOriginalMute.Count > 0)
			{
				Log.Write("已恢复 OBS 桌面音频");
			}
			deskOriginalMute.Clear();
		}
		catch (Exception ex)
		{
			Log.Write("处理音频路由失败（可忽略）：" + ex.Message);
		}
	}

	private void KeepGameWindowCapturable(Process game)
	{
		if (!cfg.KeepGameCapturable || game == null)
		{
			return;
		}
		try
		{
			game.Refresh();
			if (!game.HasExited)
			{
				IntPtr mainWindowHandle = game.MainWindowHandle;
				if (!(mainWindowHandle == IntPtr.Zero) && IsIconic(mainWindowHandle))
				{
					ShowWindow(mainWindowHandle, 9);
					minimizedFixCount++;
					Log.Write("检测到你把游戏窗口最小化了 —— 最小化后 Windows 不再为它合成画面，录出来会是黑屏；已自动把窗口还原（第 " + minimizedFixCount + " 次）。录制期间请用别的窗口【盖住】它，不要最小化。");
				}
			}
		}
		catch
		{
		}
	}

	private void LogAudioLevels(int round)
	{
		if (!obs.MetersFresh)
		{
			Log.Write("音频电平第" + round + "次：还没收到电平事件（可能 OBS 没在推，或没订阅）");
			return;
		}
		string text = "";
		foreach (string item in obs.LevelNames())
		{
			if (!(item != "RepRecorder 游戏声音") || item.IndexOf("桌面音频") >= 0 || item.IndexOf("麦克风") >= 0)
			{
				double level = obs.GetLevel(item);
				bool flag = false;
				try
				{
					Dictionary<string, object> dictionary = new Dictionary<string, object>();
					dictionary["inputName"] = item;
					flag = Convert.ToBoolean(obs.Request("GetInputMute", dictionary)["inputMuted"]);
				}
				catch
				{
				}
				string text2 = text;
				text = text2 + ((text.Length > 0) ? "   |   " : "") + item + " = " + level.ToString("0.000") + (flag ? "（源已静音→不进录像）" : ((level > 0.001) ? "（有声音，会进录像）" : "（无信号）"));
			}
		}
		Log.Write("音频电平第" + round + "次：" + ((text.Length == 0) ? "<没有相关的音频源>" : text));
	}

	private int CleanupDuplicateItems(string scene, string source)
	{
		int num = -1;
		int num2 = 0;
		try
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary["sceneName"] = scene;
			List<object> list = ObsClient.ToList(obs.Request("GetSceneItemList", dictionary)["sceneItems"]);
			foreach (object item in list)
			{
				Dictionary<string, object> dictionary2 = (Dictionary<string, object>)item;
				if (Convert.ToString(dictionary2["sourceName"]) != source)
				{
					continue;
				}
				int num3 = Convert.ToInt32(dictionary2["sceneItemId"]);
				if (num < 0)
				{
					num = num3;
					continue;
				}
				try
				{
					Dictionary<string, object> dictionary3 = new Dictionary<string, object>();
					dictionary3["sceneName"] = scene;
					dictionary3["sceneItemId"] = num3;
					obs.Request("RemoveSceneItem", dictionary3);
					num2++;
				}
				catch
				{
				}
			}
		}
		catch (Exception ex)
		{
			Log.Write("读取场景元素失败：" + ex.Message);
		}
		if (num2 > 0)
		{
			Log.Write("清理了 " + num2 + " 个重复的「" + source + "」元素");
		}
		return num;
	}

	private void FitSourceToCanvas(string scene, int itemId)
	{
		if (itemId < 0)
		{
			return;
		}
		try
		{
			int num = cfg.CanvasWidth;
			int num2 = cfg.CanvasHeight;
			try
			{
				Dictionary<string, object> dictionary = obs.Request("GetVideoSettings", null);
				if (dictionary.ContainsKey("baseWidth") && dictionary["baseWidth"] != null)
				{
					num = Convert.ToInt32(dictionary["baseWidth"]);
				}
				if (dictionary.ContainsKey("baseHeight") && dictionary["baseHeight"] != null)
				{
					num2 = Convert.ToInt32(dictionary["baseHeight"]);
				}
			}
			catch
			{
			}
			int num3 = cfg.SingleEdgeInset;
			if (num3 < 0)
			{
				num3 = 0;
			}
			if (num3 * 2 > 60)
			{
				num3 = 0;
			}
			Dictionary<string, object> dictionary2 = new Dictionary<string, object>();
			dictionary2["positionX"] = 0.0;
			dictionary2["positionY"] = 0.0;
			dictionary2["boundsType"] = "OBS_BOUNDS_SCALE_INNER";
			dictionary2["boundsAlignment"] = 0;
			dictionary2["boundsWidth"] = (double)num;
			dictionary2["boundsHeight"] = (double)num2;
			if (num3 > 0)
			{
				dictionary2["cropLeft"] = num3;
				dictionary2["cropRight"] = num3;
				dictionary2["cropTop"] = num3;
				dictionary2["cropBottom"] = num3;
			}
			Dictionary<string, object> dictionary3 = new Dictionary<string, object>();
			dictionary3["sceneName"] = scene;
			dictionary3["sceneItemId"] = itemId;
			dictionary3["sceneItemTransform"] = dictionary2;
			obs.Request("SetSceneItemTransform", dictionary3);
			Log.Write("采集源已按画布 " + num + "×" + num2 + " 居中适配" + ((num3 > 0) ? ("（并裁掉源最外圈 " + num3 + " 像素，去掉边缘那条亮边）") : ""));
		}
		catch (Exception ex)
		{
			Log.Write("缩放采集源失败（可忽略）：" + ex.Message);
		}
	}

	private bool BindGameWindow(int pid)
	{
		if (captureSource == null)
		{
			return false;
		}
		string title = null;
		string cls = null;
		EnumWindows((IntPtr h, IntPtr l) =>
		{
			GetWindowThreadProcessId(h, out var pid2);
			if (pid2 == (uint)pid && IsWindowVisible(h))
			{
				StringBuilder stringBuilder = new StringBuilder(512);
				GetWindowText(h, stringBuilder, 512);
				if (stringBuilder.Length > 0)
				{
					StringBuilder stringBuilder2 = new StringBuilder(256);
					GetClassName(h, stringBuilder2, 256);
					title = stringBuilder.ToString();
					cls = stringBuilder2.ToString();
					return false;
				}
			}
			return true;
		}, IntPtr.Zero);
		if (title == null)
		{
			return false;
		}
		string windowValue = MakeWindowString(title, cls);
		return ApplyWindowBinding(windowValue);
	}

	private bool ApplyWindowBinding(string windowValue)
	{
		if (captureSource == null)
		{
			return false;
		}
		try
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary["window"] = windowValue;
			dictionary["method"] = 0;
			dictionary["client_area"] = true;
			dictionary["cursor"] = false;
			Dictionary<string, object> dictionary2 = new Dictionary<string, object>();
			dictionary2["inputName"] = captureSource;
			dictionary2["inputSettings"] = dictionary;
			obs.Request("SetInputSettings", dictionary2);
			Log.Write("已把采集源绑定到窗口：" + windowValue);
			if (cfg.GameAudioOnly)
			{
				try
				{
					Dictionary<string, object> dictionary3 = new Dictionary<string, object>();
					dictionary3["window"] = windowValue;
					Dictionary<string, object> dictionary4 = new Dictionary<string, object>();
					dictionary4["inputName"] = "RepRecorder 游戏声音";
					dictionary4["inputSettings"] = dictionary3;
					obs.Request("SetInputSettings", dictionary4);
					Log.Write("已把音频采集源绑定到同一个窗口");
				}
				catch (Exception ex)
				{
					Log.Write("绑定音频采集源失败：" + ex.Message);
				}
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.Write("绑定窗口失败：" + ex.Message);
			return false;
		}
	}

	private bool BindSingleGameWindow(Process game, out IntPtr hwnd)
	{
		hwnd = WaitForMainWindow(game, 30);
		if (hwnd == IntPtr.Zero)
		{
			Log.Write("没等到游戏窗口，仍按进程标题绑定");
			return BindGameWindow(game.Id);
		}
		try
		{
			SetWindowText(hwnd, "th123 录制中");
		}
		catch
		{
		}
		StringBuilder stringBuilder = new StringBuilder(256);
		GetClassName(hwnd, stringBuilder, 256);
		string text = stringBuilder.ToString();
		if (text == "")
		{
			text = "th123_110a";
		}
		return ApplyWindowBinding(MakeWindowString("th123 录制中", text));
	}

	private void KeepSingleTitleFixed(IntPtr hwnd)
	{
		if (hwnd == IntPtr.Zero)
		{
			return;
		}
		try
		{
			StringBuilder stringBuilder = new StringBuilder(512);
			GetWindowText(hwnd, stringBuilder, 512);
			if (stringBuilder.ToString() != "th123 录制中")
			{
				SetWindowText(hwnd, "th123 录制中");
				Log.Write("游戏把窗口标题改掉了，已改回「th123 录制中」（否则采集源会抓不到窗口变全黑）");
			}
		}
		catch
		{
		}
	}

	private static string MethodName(int m)
	{
		return m switch
		{
			1 => "BitBlt", 
			2 => "Windows 图形捕获(WGC)", 
			_ => "自动", 
		};
	}

	private void ApplyMethod(int m)
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		dictionary["method"] = m;
		Dictionary<string, object> dictionary2 = new Dictionary<string, object>();
		dictionary2["inputName"] = captureSource;
		dictionary2["inputSettings"] = dictionary;
		obs.Request("SetInputSettings", dictionary2);
		Thread.Sleep((m == 2) ? 1200 : 800);
	}

	private bool EnsureNotBlank()
	{
		if (captureSource == null)
		{
			return true;
		}
		int[] array = new int[3] { 2, 0, 1 };
		int[] array2 = array;
		foreach (int m in array2)
		{
			try
			{
				ApplyMethod(m);
				byte[] array3 = Shot();
				try
				{
					File.WriteAllBytes(LastFramePath, array3);
				}
				catch
				{
				}
				if (!IsBlank(array3))
				{
					currentMethod = m;
					Log.Write("采集画面正常（捕获方式 " + MethodName(m) + "，源 " + captureSource + "）；已存一份截图：" + LastFramePath);
					return true;
				}
				Log.Write("采集画面全黑（捕获方式 " + MethodName(m) + "），换一种再试…");
			}
			catch (Exception ex)
			{
				Log.Write("检测采集画面失败（可忽略）：" + ex.Message);
				return true;
			}
		}
		Log.Write("警告：采集画面仍然是黑的，录像可能是黑屏 —— 请在 OBS 里手动换捕获方式/源；可看 " + LastFramePath);
		return false;
	}

	private void StopObsRecordingIfAny()
	{
		try
		{
			Dictionary<string, object> dictionary = obs.Request("GetRecordStatus", null);
			if (dictionary != null && dictionary.ContainsKey("outputActive") && Convert.ToBoolean(dictionary["outputActive"]))
			{
				obs.Request("StopRecord", null);
				Log.Write("发现 OBS 里还留着一段没停的录像（上次程序被强制结束留下的），已停止它");
				Thread.Sleep(2000);
			}
		}
		catch
		{
		}
	}

	private void EnsureStandardSourceEnabled()
	{
		try
		{
			int num = CleanupDuplicateItems("RepRecorder 录制场景", "RepRecorder 游戏窗口");
			if (num >= 0)
			{
				Dictionary<string, object> dictionary = new Dictionary<string, object>();
				dictionary["sceneName"] = "RepRecorder 录制场景";
				dictionary["sceneItemId"] = num;
				if (!Convert.ToBoolean(obs.Request("GetSceneItemEnabled", dictionary)["sceneItemEnabled"]))
				{
					SetSceneItemEnabled("RepRecorder 录制场景", num, enabled: true);
					Log.Write("画面采集源原本是隐藏的（上次并行录制被强制结束留下的），已重新显示 —— 否则这段录像会是全黑");
				}
			}
		}
		catch
		{
		}
	}

	private void CheckCaptureHealth()
	{
		try
		{
			byte[] jpg = Shot();
			if (!IsBlank(jpg))
			{
				blackStreak = 0;
				return;
			}
			blackStreak++;
			Log.Write("录制中检测到画面变黑（第 " + blackStreak + " 次，当前捕获方式 " + MethodName(currentMethod) + "）—— 通常是游戏窗口被别的窗口挡住了");
			if (blackStreak < 2)
			{
				return;
			}
			int[] array = new int[3] { 2, 0, 1 };
			int[] array2 = array;
			foreach (int num in array2)
			{
				if (num != currentMethod)
				{
					ApplyMethod(num);
					if (!IsBlank(Shot()))
					{
						currentMethod = num;
						blackStreak = 0;
						Log.Write("画面已恢复（捕获方式切换为 " + MethodName(num) + "）");
						return;
					}
				}
			}
			Log.Write("画面仍然是黑的：这段录像可能是黑屏。录制期间请不要让其他窗口挡住游戏窗口。");
			blackStreak = 0;
		}
		catch
		{
		}
	}

	private byte[] Shot()
	{
		if (captureScene != null && captureScene != captureSource)
		{
			try
			{
				return ShotOf(captureScene);
			}
			catch
			{
			}
		}
		return ShotOf(captureSource);
	}

	private byte[] ShotOf(string sourceName)
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		dictionary["sourceName"] = sourceName;
		dictionary["imageFormat"] = "jpg";
		dictionary["imageWidth"] = 320;
		dictionary["imageHeight"] = 240;
		dictionary["imageCompressionQuality"] = 60;
		string text = Convert.ToString(obs.Request("GetSourceScreenshot", dictionary)["imageData"]);
		int num = text.IndexOf("base64,");
		if (num >= 0)
		{
			text = text.Substring(num + 7);
		}
		return Convert.FromBase64String(text);
	}

	private bool IsBlank(byte[] jpg)
	{
		try
		{
			using MemoryStream stream = new MemoryStream(jpg);
			using Bitmap bitmap = new Bitmap(stream);
			double num = 0.0;
			for (int i = 0; i < bitmap.Height; i += 8)
			{
				for (int j = 0; j < bitmap.Width; j += 8)
				{
					Color pixel = bitmap.GetPixel(j, i);
					double num2 = (double)(pixel.R + pixel.G + pixel.B) / 3.0;
					if (num2 > num)
					{
						num = num2;
					}
				}
			}
			return num < 20.0;
		}
		catch
		{
			return false;
		}
	}

	private void ProcessJob(Job job)
	{
		job.Started = DateTime.Now;
		try
		{
			if (cfg.SkipExisting && !forceRecord && !job.Force)
			{
				string path = Path.ChangeExtension(job.Rep, ".mp4");
				string path2 = Path.ChangeExtension(job.Rep, ".mkv");
				if (File.Exists(path) || File.Exists(path2))
				{
					SetJob(job, "跳过", "已有录像文件（可在设置里关掉“跳过已录制”）");
					Log.Write("跳过（已有录像）：" + Path.GetFileName(job.Rep));
					return;
				}
			}
			if ((forceRecord || job.Force) && cfg.SkipExisting)
			{
				Log.Write("你明确选了重录它 → 即使已有录像也重录：" + Path.GetFileName(job.Rep));
			}
			SetJob(job, "准备", "");
			try
			{
				ApplyMicMode(beforeRecording: true);
			}
			catch
			{
			}
			KillGame();
			try
			{
				Dictionary<string, object> dictionary = new Dictionary<string, object>();
				dictionary["recordDirectory"] = Path.GetDirectoryName(job.Rep);
				obs.Request("SetRecordDirectory", dictionary);
				Log.Write("OBS 录像目录已设为：" + Path.GetDirectoryName(job.Rep));
			}
			catch (Exception ex)
			{
				Log.Write("设置 OBS 录像目录失败（会录到 OBS 默认目录再移动）：" + ex.Message);
			}
			SetJob(job, "启动游戏", "");
			ProcessStartInfo processStartInfo = new ProcessStartInfo(Path.Combine(cfg.GameDir, "th123.exe"), "\"" + job.Rep + "\"");
			processStartInfo.WorkingDirectory = cfg.GameDir;
			Process process = Process.Start(processStartInfo);
			bool flag = false;
			DateTime dateTime = DateTime.Now.AddSeconds(60.0);
			while (DateTime.Now < dateTime && !cancel)
			{
				Thread.Sleep(500);
				try
				{
					process.Refresh();
					if (!process.HasExited)
					{
						if (!(process.MainWindowHandle != IntPtr.Zero))
						{
							continue;
						}
						flag = true;
					}
				}
				catch
				{
				}
				break;
			}
			if (!flag && !cancel)
			{
				Log.Write("警告：没等到游戏窗口，仍继续尝试");
			}
			try
			{
				if (process.HasExited)
				{
					SetJob(job, "失败", "游戏启动后立刻退出");
					return;
				}
			}
			catch
			{
			}
			EnsureStandardSourceEnabled();
			IntPtr hwnd = IntPtr.Zero;
			if (captureSource != null && captureSourceIsOurs)
			{
				BindSingleGameWindow(process, out hwnd);
			}
			for (int i = 0; i < cfg.PreRollSeconds; i++)
			{
				if (cancel)
				{
					break;
				}
				Thread.Sleep(1000);
			}
			if (captureSource != null)
			{
				EnsureNotBlank();
			}
			obs.Request("StartRecord", null);
			SetJob(job, "录制中", "0:00");
			jobMaxGameLevel = 0.0;
			try
			{
				ApplyGameAudioSetting();
			}
			catch
			{
			}
			Log.Write("开始录像：" + Path.GetFileName(job.Rep));
			bool flag2 = false;
			DateTime now = DateTime.Now;
			int num = 0;
			blackStreak = 0;
			double lastVisibleSec = 0.0;
			while (!cancel)
			{
				Thread.Sleep(500);
				bool flag3 = false;
				try
				{
					flag3 = process.HasExited;
				}
				catch
				{
					flag3 = true;
				}
				if (flag3)
				{
					break;
				}
				if ((DateTime.Now - now).TotalMinutes > (double)cfg.MaxMinutes)
				{
					flag2 = true;
					break;
				}
				num++;
				if (captureSource != null && num % 10 == 0)
				{
					CheckCaptureHealth();
				}
				if (captureSource != null && num % 2 == 0)
				{
					try
					{
						if (!IsBlank(ShotOf(captureSource)))
						{
							lastVisibleSec = (DateTime.Now - now).TotalSeconds;
						}
					}
					catch
					{
					}
				}
				if (num % 4 == 0)
				{
					KeepGameWindowCapturable(process);
				}
				if (num % 4 == 0)
				{
					KeepSingleTitleFixed(hwnd);
				}
				if (num % 60 == 0)
				{
					LogAudioLevels(num / 60);
				}
				if (cfg.GameAudioOnly)
				{
					double level = obs.GetLevel("RepRecorder 游戏声音");
					if (level > jobMaxGameLevel)
					{
						jobMaxGameLevel = level;
					}
				}
				TimeSpan timeSpan = DateTime.Now - now;
				SetJob(job, "录制中", $"{(int)timeSpan.TotalMinutes}:{timeSpan.Seconds:00}");
			}
			if (flag2)
			{
				Log.Write("超过 " + cfg.MaxMinutes + " 分钟上限，强制结束");
				KillGame();
			}
			if (gameMutedByUs)
			{
				if (GameAudio.SetSystemMute(mute: false, out var message))
				{
					Log.Write("已恢复系统声音（" + message + "）");
					MarkSystemMuted(muted: false);
				}
				gameMutedByUs = false;
			}
			double num2 = 0.0;
			bool flag4 = false;
			try
			{
				flag4 = captureSource != null && IsBlank(ShotOf(captureSource));
			}
			catch
			{
			}
			if (flag4 && cfg.PostRollSeconds > 0)
			{
				Log.Write("游戏已退出、画面已经黑了：不再等 " + cfg.PostRollSeconds + " 秒收尾（免得结尾拖一段黑）");
			}
			for (int i = 0; i < cfg.PostRollSeconds; i++)
			{
				if (flag4)
				{
					break;
				}
				Thread.Sleep(1000);
			}
			num2 = (DateTime.Now - now).TotalSeconds;
			string text = null;
			try
			{
				Dictionary<string, object> dictionary2 = obs.Request("StopRecord", null);
				if (dictionary2.ContainsKey("outputPath"))
				{
					text = Convert.ToString(dictionary2["outputPath"]);
				}
			}
			catch (Exception ex)
			{
				Log.Write("停止录像出错：" + ex.Message);
			}
			if (string.IsNullOrEmpty(text) || !File.Exists(text))
			{
				try
				{
					string path3 = Convert.ToString(obs.Request("GetRecordDirectory", null)["recordDirectory"]);
					if (Directory.Exists(path3))
					{
						DateTime dateTime2 = DateTime.Now.AddMinutes(-3.0);
						string[] files = Directory.GetFiles(path3);
						foreach (string text2 in files)
						{
							switch (Path.GetExtension(text2).ToLower())
							{
							case ".mp4":
							case ".mkv":
							case ".flv":
							case ".mov":
								if (File.GetLastWriteTime(text2) > dateTime2)
								{
									text = text2;
								}
								break;
							}
						}
					}
				}
				catch
				{
				}
			}
			if (string.IsNullOrEmpty(text) || !File.Exists(text))
			{
				SetJob(job, "失败", "拿不到录像文件");
				Log.Write("失败：拿不到录像文件 —— " + Path.GetFileName(job.Rep));
				return;
			}
			string text3 = Path.ChangeExtension(job.Rep, Path.GetExtension(text));
			if (!string.Equals(Path.GetFullPath(text), Path.GetFullPath(text3), StringComparison.OrdinalIgnoreCase))
			{
				for (int i = 0; i < 10; i++)
				{
					try
					{
						if (File.Exists(text3))
						{
							File.Delete(text3);
						}
						File.Move(text, text3);
					}
					catch
					{
						Thread.Sleep(500);
						continue;
					}
					break;
				}
			}
			job.Output = text3;
			TrimTrailingBlack(job, num2, lastVisibleSec);
			if (File.Exists(text3))
			{
				job.Size = new FileInfo(text3).Length;
			}
			job.VideoSeconds = Mp4Duration(text3);
			job.Ended = DateTime.Now;
			string text4 = "";
			if (text3.ToLower().EndsWith(".mp4") && !HasMoov(text3))
			{
				text4 = "缺少 moov 索引，可能不可播";
			}
			else if (!text3.ToLower().EndsWith(".mp4"))
			{
				text4 = "格式 " + Path.GetExtension(text3) + "，需要转封装";
			}
			if (!cfg.RecordGameAudio)
			{
				Log.Write("音频自检：你关掉了「录打击音效」，这段视频按预期是无声的");
			}
			else if (cfg.GameAudioOnly)
			{
				bool flag5 = false;
				try
				{
					Dictionary<string, object> dictionary3 = new Dictionary<string, object>();
					dictionary3["inputName"] = "RepRecorder 游戏声音";
					flag5 = Convert.ToBoolean(obs.Request("GetInputMute", dictionary3)["inputMuted"]);
				}
				catch
				{
				}
				if (flag5)
				{
					Log.Write("⚠ 音频自检：「RepRecorder 游戏声音」源处于静音状态，这段录像里没有游戏声音");
					if (text4 == "")
					{
						text4 = "音频源被静音，录像无声";
					}
				}
				else if (!obs.MetersFresh)
				{
					Log.Write("音频自检：没有收到电平数据，无法判断（不影响录制）");
				}
				else if (jobMaxGameLevel < 0.002)
				{
					Log.Write("⚠ 音频自检：整段几乎没有游戏声音（最大电平 " + jobMaxGameLevel.ToString("0.000") + "）—— 录像可能是静音，检查 OBS 里「RepRecorder 游戏声音」源有没有绑上游戏窗口");
					if (text4 == "")
					{
						text4 = "录成了静音？(音频自检)";
					}
				}
				else
				{
					Log.Write("音频自检：游戏声音正常（最大电平 " + jobMaxGameLevel.ToString("0.000") + "）");
				}
			}
			SetJob(job, (text4 == "") ? "完成" : "注意", text4);
			Log.Write(((text4 == "") ? "完成：" : ("完成（" + text4 + "）：")) + Path.GetFileName(text3) + "  " + ((double)job.Size / 1048576.0).ToString("0.0") + " MB  视频时长 " + FmtDur(job.VideoSeconds) + "（任务耗时 " + FmtDur((job.Ended - job.Started).TotalSeconds) + "）");
			if (text4.StartsWith("格式"))
			{
				Log.Write("转封装：ffmpeg -i \"" + text3 + "\" -c copy \"" + Path.ChangeExtension(text3, ".mp4") + "\"");
			}
			string text5 = "";
			if (cfg.AskSaveLocation && !queueSaveDecided)
			{
				string text6 = AskSaveLocationDialog(job);
				if (!string.IsNullOrEmpty(text6) && text6 != "keep")
				{
					string text7 = MoveOutputTo(job, text6);
					if (text7 != null)
					{
						job.Output = text7;
						text5 = "已放到 " + Path.GetDirectoryName(text7);
					}
				}
			}
			else if (!string.IsNullOrEmpty(queueSaveFolder))
			{
				string text7 = MoveOutputTo(job, queueSaveFolder);
				if (text7 != null)
				{
					job.Output = text7;
					text5 = "已放到 " + queueSaveFolder;
				}
			}
			if (text5 != "")
			{
				if (File.Exists(job.Output))
				{
					job.Size = new FileInfo(job.Output).Length;
				}
				Log.Write("视频位置：" + job.Output);
				SetJob(job, (text4 == "") ? "完成" : "注意", text5);
			}
		}
		catch (Exception ex)
		{
			SetJob(job, "失败", ex.Message);
			Log.Write("任务失败：" + Path.GetFileName(job.Rep) + " —— " + ex.Message);
			try
			{
				if (obs.IsConnected)
				{
					obs.Request("StopRecord", null);
				}
			}
			catch
			{
			}
			KillGame();
		}
	}

	private void TrimTrailingBlack(Job job, double totalSec, double lastVisibleSec)
	{
		string text = null;
		try
		{
			string output = job.Output;
			if (string.IsNullOrEmpty(output) || !File.Exists(output) || !output.ToLower().EndsWith(".mp4") || lastVisibleSec <= 1.0 || totalSec <= lastVisibleSec)
			{
				return;
			}
			double num = lastVisibleSec + 0.25;
			double num2 = totalSec - num;
			if (num2 < 0.4)
			{
				return;
			}
			string text2 = FindFfmpeg();
			if (text2 == null)
			{
				return;
			}
			text = output + ".trim.mp4";
			if (File.Exists(text))
			{
				File.Delete(text);
			}
			string arguments = "-hide_banner -y -i \"" + output + "\" -c copy -t " + num.ToString("0.000", CultureInfo.InvariantCulture) + " -movflags +faststart \"" + text + "\"";
			ProcessStartInfo processStartInfo = new ProcessStartInfo(text2, arguments);
			processStartInfo.UseShellExecute = false;
			processStartInfo.CreateNoWindow = true;
			processStartInfo.WorkingDirectory = Path.GetDirectoryName(text2);
			Process process = Process.Start(processStartInfo);
			process.WaitForExit();
			if (process.ExitCode != 0 || !File.Exists(text) || new FileInfo(text).Length < 4096 || !HasMoov(text))
			{
				Log.Write("结尾黑屏修剪没成功（ffmpeg 退出码 " + process.ExitCode + "），保留原文件");
				try
				{
					if (File.Exists(text))
					{
						File.Delete(text);
					}
					return;
				}
				catch
				{
					return;
				}
			}
			try
			{
				File.Replace(text, output, null);
			}
			catch
			{
				File.Delete(output);
				File.Move(text, output);
			}
			Log.Write("已剪掉结尾 " + FmtDur(num2) + " 的黑屏（" + FmtDur(totalSec) + " → " + FmtDur(num) + "）");
		}
		catch (Exception ex)
		{
			Log.Write("结尾黑屏修剪出错（已忽略）：" + ex.Message);
			try
			{
				if (text != null && File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch
			{
			}
		}
	}

	private string AskSaveLocationDialog(Job job)
	{
		return AskSaveLocationDialog(job, 1);
	}

	private string AskSaveLocationDialog(Job job, int count)
	{
		string result = "keep";
		try
		{
			object obj = Invoke((Func<string>)(() =>
			{
				using (Form form = new Form())
				{
					form.Text = "录制完成 —— 视频要放在哪里？";
					form.Font = Font;
					form.ClientSize = new Size(520, 240);
					form.FormBorderStyle = FormBorderStyle.FixedDialog;
					form.MinimizeBox = false;
					form.MaximizeBox = false;
					form.StartPosition = FormStartPosition.CenterScreen;
					Label label = new Label();
					label.SetBounds(14, 12, 490, 70);
					if (count > 1)
					{
						label.Text = "这一批 " + count + " 个视频都录好了（例如：" + Path.GetFileName(job.Output) + "，" + ((double)job.Size / 1048576.0).ToString("0.0") + " MB，" + FmtDur(job.VideoSeconds) + "）\r\n现在位置：" + Path.GetDirectoryName(job.Output);
					}
					else
					{
						label.Text = "已录好：" + Path.GetFileName(job.Output) + "\r\n" + ((double)job.Size / 1048576.0).ToString("0.0") + " MB，时长 " + FmtDur(job.VideoSeconds) + "\r\n现在位置：" + Path.GetDirectoryName(job.Output);
					}
					Label label2 = new Label();
					label2.SetBounds(14, 88, 490, 20);
					label2.Text = ((count > 1) ? ("这 " + count + " 个视频要放到哪里？") : "你想把这个视频放到哪里？");
					Font font = new Font(Font, FontStyle.Bold);
					label2.Font = font;
					CheckBox checkBox = new CheckBox();
					checkBox.SetBounds(14, 116, 490, 22);
					checkBox.Text = "本队列后面录好的视频也都这样处理（不再询问）";
					Button button = new Button();
					button.Text = "留在 rep 旁边";
					button.SetBounds(14, 156, 130, 34);
					button.DialogResult = DialogResult.OK;
					Button button2 = new Button();
					button2.Text = "另存到文件夹…";
					button2.SetBounds(154, 156, 140, 34);
					button2.DialogResult = DialogResult.Yes;
					Button button3 = new Button();
					button3.Text = "以后不再询问";
					button3.SetBounds(304, 156, 130, 34);
					button3.DialogResult = DialogResult.No;
					form.Controls.Add(label);
					form.Controls.Add(label2);
					form.Controls.Add(checkBox);
					form.Controls.Add(button);
					form.Controls.Add(button2);
					form.Controls.Add(button3);
					form.AcceptButton = button;
					DialogResult dialogResult = form.ShowDialog(this);
					if (checkBox.Checked)
					{
						queueSaveDecided = true;
					}
					switch (dialogResult)
					{
					case DialogResult.Yes:
					{
						using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
						{
							folderBrowserDialog.Description = "选择保存视频的文件夹";
							folderBrowserDialog.ShowNewFolderButton = true;
							try
							{
								if (!string.IsNullOrEmpty(cfg.LastSaveFolder) && Directory.Exists(cfg.LastSaveFolder))
								{
									folderBrowserDialog.SelectedPath = cfg.LastSaveFolder;
								}
							}
							catch
							{
							}
							if (folderBrowserDialog.ShowDialog(form) == DialogResult.OK)
							{
								cfg.LastSaveFolder = folderBrowserDialog.SelectedPath;
								cfg.Save();
								if (checkBox.Checked)
								{
									queueSaveFolder = folderBrowserDialog.SelectedPath;
								}
								result = folderBrowserDialog.SelectedPath;
							}
						}
						break;
					}
					case DialogResult.No:
						cfg.AskSaveLocation = false;
						cfg.Save();
						Log.Write("已关闭“每次询问保存位置”，可在设置里重新打开");
						break;
					}
				}
				return result;
			}));
			if (obj != null)
			{
				result = Convert.ToString(obj);
			}
		}
		catch (Exception ex)
		{
			Log.Write("询问保存位置失败：" + ex.Message);
		}
		return result;
	}

	private string MoveOutputTo(Job job, string folder)
	{
		try
		{
			if (string.IsNullOrEmpty(job.Output) || !File.Exists(job.Output))
			{
				return null;
			}
			if (!Directory.Exists(folder))
			{
				Log.Write("目标文件夹不存在：" + folder);
				return null;
			}
			string text = Path.Combine(folder, Path.GetFileName(job.Output));
			if (string.Equals(Path.GetFullPath(text), Path.GetFullPath(job.Output), StringComparison.OrdinalIgnoreCase))
			{
				return job.Output;
			}
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(job.Output);
			string extension = Path.GetExtension(job.Output);
			int num = 2;
			while (File.Exists(text) && num < 500)
			{
				text = Path.Combine(folder, fileNameWithoutExtension + " (" + num + ")" + extension);
				num++;
			}
			File.Move(job.Output, text);
			return text;
		}
		catch (Exception ex)
		{
			Log.Write("移动视频失败：" + ex.Message);
			return null;
		}
	}

	private static bool HasMoov(string path)
	{
		try
		{
			using FileStream fileStream = File.OpenRead(path);
			int num = (int)Math.Min(1048576L, fileStream.Length);
			byte[] array = new byte[num];
			fileStream.Read(array, 0, num);
			fileStream.Seek(Math.Max(0L, fileStream.Length - 1048576), SeekOrigin.Begin);
			int num2 = (int)Math.Min(1048576L, fileStream.Length);
			byte[] array2 = new byte[num2];
			fileStream.Read(array2, 0, num2);
			string text = Encoding.ASCII.GetString(array) + Encoding.ASCII.GetString(array2);
			return text.Contains("moov");
		}
		catch
		{
			return false;
		}
	}

	private static void KillGame()
	{
		try
		{
			Process[] processesByName = Process.GetProcessesByName("th123");
			foreach (Process process in processesByName)
			{
				try
				{
					process.Kill();
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	private void Ui(Action a)
	{
		try
		{
			if (!IsDisposed)
			{
				if (InvokeRequired)
				{
					BeginInvoke(a);
				}
				else
				{
					a();
				}
			}
		}
		catch
		{
		}
	}

	private void SetJob(Job job, string status, string detail)
	{
		job.Status = status;
		job.Detail = detail;
		Ui(() =>
		{
			if (job.Item != null)
			{
				job.Item.SubItems[2].Text = status;
				bool flag = job.Started != DateTime.MinValue && job.Ended != DateTime.MinValue && job.Ended >= job.Started;
				ListViewItem.ListViewSubItem listViewSubItem = job.Item.SubItems[3];
				string text = ((job.VideoSeconds > 0.5) ? FmtDur(job.VideoSeconds) : (flag ? FmtDur((job.Ended - job.Started).TotalSeconds) : ((job.Started != DateTime.MinValue) ? "…" : "")));
				listViewSubItem.Text = text;
				job.Item.SubItems[4].Text = ((job.Size > 0) ? (((double)job.Size / 1048576.0).ToString("0.0") + " MB") : "");
				job.Item.SubItems[5].Text = detail;
				if (status == "完成" || status == "已录制")
				{
					job.Item.ForeColor = Color.FromArgb(0, 120, 0);
				}
				else if (status == "失败")
				{
					job.Item.ForeColor = Color.FromArgb(180, 0, 0);
				}
				else if (status == "注意")
				{
					job.Item.ForeColor = Color.FromArgb(190, 120, 0);
				}
				else
				{
					job.Item.ForeColor = Color.Black;
				}
				list.EnsureVisible(job.Item.Index);
				if (status == "完成" || status == "失败" || status == "注意" || status == "已录制")
				{
					HighlightBatchRows();
				}
			}
		});
	}

	private void SetStatusText(string s)
	{
		Ui(() =>
		{
			statusLabel.Text = s;
		});
	}

	private void OnLogLine(string line)
	{
		Ui(() =>
		{
			logBox.AppendText("> " + line + Environment.NewLine);
		});
	}

	private void RefreshEnvLabel()
	{
		Ui(() =>
		{
			string text = ((!string.IsNullOrEmpty(cfg.ObsExe) && File.Exists(cfg.ObsExe)) ? cfg.ObsExe : Settings.FindObsExe());
			string text2 = ((!string.IsNullOrEmpty(cfg.GameDir) && File.Exists(Path.Combine(cfg.GameDir, "th123.exe"))) ? cfg.GameDir : Settings.FindGameDir());
			string text3 = "";
			text3 = ((text != null) ? (text3 + "OBS：已就绪") : (text3 + "OBS：未安装（winget install --id OBSProject.OBSStudio -e）"));
			text3 = ((text2 != null) ? (text3 + "    游戏：已就绪") : (text3 + "    游戏：未找到 th123.exe"));
			obsState.Text = text3;
			obsState.ForeColor = ((text == null || text2 == null) ? Color.Firebrick : Color.SeaGreen);
		});
	}

	private void KeepSelectionOnEmptyClick(MouseEventArgs me)
	{
		try
		{
			if (me.Button != MouseButtons.Left || list.HitTest(me.Location).Item != null)
			{
				return;
			}
			if (list.SelectedItems.Count > 0)
			{
				emptyClickKeep.Clear();
				foreach (ListViewItem selectedItem in list.SelectedItems)
				{
					emptyClickKeep.Add(selectedItem);
				}
			}
			if (emptyClickKeep.Count == 0)
			{
				return;
			}
			suppressSelSync = true;
			if (emptyClickTimer == null)
			{
				emptyClickTimer = new System.Windows.Forms.Timer();
				emptyClickTimer.Interval = 120;
				emptyClickTimer.Tick += (object param0, EventArgs param1) =>
				{
					try
					{
						emptyClickTimer.Stop();
						pinnedBatch.Clear();
						foreach (ListViewItem item3 in emptyClickKeep)
						{
							if (item3.Tag is Job item2 && !pinnedBatch.Contains(item2))
							{
								pinnedBatch.Add(item2);
							}
						}
						try
						{
							suppressSelSync = true;
							foreach (ListViewItem item4 in list.Items)
							{
								item4.Selected = false;
							}
						}
						catch
						{
						}
						finally
						{
							suppressSelSync = false;
						}
						HighlightBatchRows();
						SyncParallelCountToSelection();
						Log.Write("你点在了列表空白处 —— 已把这 " + pinnedBatch.Count + " 个标记为「本批」（绿色），数量也跟着改成 " + Math.Min(6, pinnedBatch.Count) + " 个");
					}
					catch
					{
					}
				};
			}
			emptyClickTimer.Stop();
			emptyClickTimer.Start();
		}
		catch
		{
		}
	}

	private void SyncParallelCountToSelection()
	{
		try
		{
			int num = ((list != null) ? list.SelectedItems.Count : 0);
			bool flag = false;
			if (num <= 0 && pinnedBatch != null && pinnedBatch.Count > 0)
			{
				num = pinnedBatch.Count;
				flag = true;
			}
			if (num <= 0)
			{
				if (lblParHint != null)
				{
					lblParHint.Text = "";
				}
				return;
			}
			int num2 = Math.Min(6, num);
			if (cbParallel != null && cbParallel.SelectedIndex != num2 - 1)
			{
				cbParallel.SelectedIndex = num2 - 1;
			}
			if (lblParHint != null)
			{
				lblParHint.Text = ((num > 6) ? ("（你" + (flag ? "标记" : "选中") + "了 " + num + " 个，上限 6 → 会分 " + (num + 5) / 6 + " 批）") : ("（= 你" + (flag ? "标记" : "选中") + "的 " + num + " 个）"));
			}
			HighlightBatchRows();
		}
		catch
		{
		}
	}

	private bool AskSaveLocationUpFront(int count)
	{
		queueSaveDecided = false;
		queueSaveFolder = null;
		bool go = true;
		try
		{
			Invoke((Action)(() =>
			{
				using Form form = new Form();
				form.Text = "开始录制 —— 视频要放在哪里？";
				form.Font = Font;
				form.ClientSize = new Size(544, 214);
				form.FormBorderStyle = FormBorderStyle.FixedDialog;
				form.MinimizeBox = false;
				form.MaximizeBox = false;
				form.StartPosition = FormStartPosition.CenterScreen;
				Label label = new Label();
				label.SetBounds(14, 12, 516, 56);
				label.Text = "这次要录 " + count + " 个 rep" + ((count > 6) ? "（会按「同时录制数量」自动分批）" : "") + "。\r\n先选好视频放在哪里，选完就开始录。";
				Label label2 = new Label();
				label2.SetBounds(14, 74, 516, 20);
				label2.ForeColor = Color.FromArgb(120, 158, 214);
				label2.Text = "上次用的位置：" + (string.IsNullOrEmpty(cfg.LastSaveFolder) ? "（还没选过）" : cfg.LastSaveFolder);
				CheckBox checkBox = new CheckBox();
				checkBox.SetBounds(14, 100, 516, 22);
				checkBox.Text = "以后不再询问（都用这次的选择；可在「设置…」里重新打开）";
				Button button = new Button();
				button.Text = "和 rep 放在一起";
				button.SetBounds(14, 138, 160, 34);
				button.DialogResult = DialogResult.OK;
				Button button2 = new Button();
				button2.Text = "另存到文件夹…";
				button2.SetBounds(188, 138, 150, 34);
				button2.DialogResult = DialogResult.Yes;
				Button button3 = new Button();
				button3.Text = "取消（先不录）";
				button3.SetBounds(352, 138, 156, 34);
				button3.DialogResult = DialogResult.Cancel;
				form.Controls.Add(label);
				form.Controls.Add(label2);
				form.Controls.Add(checkBox);
				form.Controls.Add(button);
				form.Controls.Add(button2);
				form.Controls.Add(button3);
				form.AcceptButton = button;
				form.CancelButton = button3;
				DialogResult dialogResult = form.ShowDialog(this);
				if (dialogResult == DialogResult.Yes)
				{
					using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
					folderBrowserDialog.Description = "选择保存视频的文件夹";
					folderBrowserDialog.ShowNewFolderButton = true;
					try
					{
						if (!string.IsNullOrEmpty(cfg.LastSaveFolder) && Directory.Exists(cfg.LastSaveFolder))
						{
							folderBrowserDialog.SelectedPath = cfg.LastSaveFolder;
						}
					}
					catch
					{
					}
					if (folderBrowserDialog.ShowDialog(form) == DialogResult.OK)
					{
						queueSaveFolder = folderBrowserDialog.SelectedPath;
						cfg.LastSaveFolder = folderBrowserDialog.SelectedPath;
					}
					else
					{
						dialogResult = DialogResult.OK;
					}
				}
				if (dialogResult == DialogResult.Cancel)
				{
					go = false;
				}
				else
				{
					if (checkBox.Checked)
					{
						cfg.AskSaveLocation = false;
						Log.Write("已记住：以后不再询问保存位置（可在「设置…」里重新打开）");
					}
					queueSaveDecided = true;
					cfg.Save();
				}
			}));
		}
		catch (Exception ex)
		{
			Log.Write("询问保存位置失败（照常开始录制）：" + ex.Message);
			queueSaveDecided = true;
		}
		if (!go)
		{
			Log.Write("你在询问里点了「取消（先不录）」——这次不录制");
		}
		else if (!string.IsNullOrEmpty(queueSaveFolder))
		{
			Log.Write("本次录好的视频会放到：" + queueSaveFolder);
		}
		else
		{
			Log.Write("本次录好的视频就放在各自 rep 旁边");
		}
		return go;
	}

	private void OnStart(object sender, EventArgs e)
	{
		StartRecording();
	}

	private void StartRecording()
	{
		List<Job> list = new List<Job>();
		bool flag = false;
		bool roundRecord = cfg.RoundRecord;
		bool lockTaken = false;
		object obj = default;
		try
		{
			Monitor.Enter(obj = jobsLock, ref lockTaken);
			if (roundRecord)
			{
				foreach (Job job in jobs)
				{
					if (job.Status == "等待")
					{
						list.Add(job);
					}
				}
			}
			else
			{
				foreach (ListViewItem selectedItem in this.list.SelectedItems)
				{
					if (selectedItem.Tag is Job item && !list.Contains(item))
					{
						list.Add(item);
					}
				}
				if (list.Count == 0 && pinnedBatch != null && pinnedBatch.Count > 0)
				{
					foreach (Job item2 in pinnedBatch)
					{
						if (!list.Contains(item2))
						{
							list.Add(item2);
						}
					}
				}
			}
			flag = !roundRecord && list.Count > 0;
			if (roundRecord && list.Count == 0)
			{
				Log.Write("轮录：队列里没有待录的 rep（都录过了？点「刷新」重新检查，或先在列表里选中要重录的）");
				return;
			}
			if (!roundRecord && list.Count == 0 && jobs.Count == 0)
			{
				Log.Write("队列是空的，先把 rep 拖进来");
				return;
			}
			if (roundRecord)
			{
				pinnedBatch.Clear();
			}
		}
		finally
		{
			if (lockTaken)
			{
				Monitor.Exit(obj);
			}
		}
		cfg.FillDefaults();
		int num = Math.Max(1, Math.Min(6, cfg.ParallelCount));
		int num2 = 0;
		bool lockTaken2 = false;
		try
		{
			Monitor.Enter(obj = jobsLock, ref lockTaken2);
			foreach (Job job2 in jobs)
			{
				if (job2.Status == "等待")
				{
					num2++;
				}
			}
		}
		finally
		{
			if (lockTaken2)
			{
				Monitor.Exit(obj);
			}
		}
		if (roundRecord)
		{
			Log.Write("==== 轮录开始：从队列第一个 rep 开始数，共 " + list.Count + " 个待录，每批 " + num + " 个" + ((num == 1) ? "（每批 1 个 → 走单录，依次录）" : ("（最后不足 " + num + " 个的按实际数量录）")) + "  ※ 轮录模式下不看你的选中/绿色标记，一律从队列最前面开始 ====");
		}
		if (cfg.AskSaveLocation)
		{
			int count;
			if (flag)
			{
				count = list.Count;
			}
			else
			{
				count = (roundRecord ? list.Count : num2);
			}
			if (!AskSaveLocationUpFront(count))
			{
				return;
			}
		}
		if (list.Count > 0)
		{
			selectedBatch = list;
			if (flag)
			{
				forceRecord = true;
			}
			if (flag)
			{
				Log.Write("开始录制：你选中了 " + list.Count + " 个，同时录 " + num + " 个" + ((list.Count > num) ? ("，分 " + (list.Count + num - 1) / num + " 批") : "") + "（选中的一律重录）");
			}
		}
		else
		{
			selectedBatch = null;
			if (num2 == 0)
			{
				Log.Write("没有待录制的 rep（都录过了？点「刷新」重新检查，或先在列表里选中要重录的）");
				return;
			}
			Log.Write("开始录制：待录 " + num2 + " 个，同时录 " + num + " 个" + ((num2 > num) ? ("（只录这一批 " + num + " 个，想全录完请勾「轮录」）") : "") + ((num2 == 1) ? "（只有 1 个，按普通单录走）" : ""));
		}
		parallelRequested = true;
		Ui(() =>
		{
			btnStart.Enabled = false;
			btnStop.Enabled = true;
		});
		StartQueue();
	}

	private void OnStop(object sender, EventArgs e)
	{
		if (worker == null || !worker.IsAlive)
		{
			Log.Write("现在没有在录制，不需要停止" + ((list != null && list.Items.Count == 0) ? "（队列是空的）" : "") + "。要点开始录制请按「开始录制」");
			cancel = false;
			SetStatusText("就绪");
			Ui(() =>
			{
				btnStop.Enabled = false;
				btnStart.Enabled = true;
			});
		}
		else
		{
			cancel = true;
			Log.Write("已请求停止……");
			SetStatusText("正在停止…");
			Ui(() =>
			{
				btnStop.Enabled = false;
			});
		}
	}

	private void OnClear(object sender, EventArgs e)
	{
		if (worker != null && worker.IsAlive)
		{
			Log.Write("队列运行中，先停止再清空");
			return;
		}
		lock (jobsLock)
		{
			jobs.Clear();
		}
		list.Items.Clear();
		Log.Write("队列已清空");
	}

	private string FindExistingOutput(Job job)
	{
		try
		{
			if (!string.IsNullOrEmpty(job.Output) && File.Exists(job.Output))
			{
				return job.Output;
			}
			string[] array = new string[4] { ".mp4", ".mkv", ".flv", ".mov" };
			string[] array2 = array;
			foreach (string extension in array2)
			{
				string text = Path.ChangeExtension(job.Rep, extension);
				if (File.Exists(text))
				{
					return text;
				}
			}
			if (!string.IsNullOrEmpty(cfg.LastSaveFolder) && Directory.Exists(cfg.LastSaveFolder))
			{
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(job.Rep);
				array2 = array;
				foreach (string extension in array2)
				{
					string text = Path.Combine(cfg.LastSaveFolder, fileNameWithoutExtension + extension);
					if (File.Exists(text))
					{
						return text;
					}
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private void OnRefreshQueue(object sender, EventArgs e)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		lock (jobsLock)
		{
			foreach (Job job in jobs)
			{
				if (job.Status == "录制中" || job.Status == "准备" || job.Status == "启动游戏" || job.Status == "收尾")
				{
					num3++;
					continue;
				}
				string text = FindExistingOutput(job);
				if (text != null)
				{
					job.Output = text;
					try
					{
						job.Size = new FileInfo(text).Length;
					}
					catch
					{
						job.Size = 0L;
					}
					SetJob(job, "已录制", "已有视频，点开始会跳过");
					num++;
				}
				else
				{
					job.Output = "";
					job.Size = 0L;
					SetJob(job, "等待", "还没有视频，会被录制");
					num2++;
				}
			}
		}
		Log.Write("已刷新队列：有视频 " + num + " 个，待录制 " + num2 + " 个" + ((num3 > 0) ? ("，正在处理中 " + num3 + " 个（未改动）") : ""));
	}

	private void OnRemove(object sender, EventArgs e)
	{
		if (worker != null && worker.IsAlive)
		{
			Log.Write("队列运行中，不能移除");
			return;
		}
		List<Job> list = new List<Job>();
		foreach (ListViewItem selectedItem in this.list.SelectedItems)
		{
			list.Add((Job)selectedItem.Tag);
		}
		lock (jobsLock)
		{
			foreach (Job item in list)
			{
				jobs.Remove(item);
				this.list.Items.Remove(item.Item);
			}
			for (int i = 0; i < this.list.Items.Count; i++)
			{
				this.list.Items[i].SubItems[0].Text = (i + 1).ToString();
			}
		}
		foreach (Job item2 in list)
		{
			pinnedBatch.Remove(item2);
		}
		HighlightBatchRows();
	}

	private void OnCheck(object sender, EventArgs e)
	{
		cfg.FillDefaults();
		cfg.Save();
		RefreshEnvLabel();
		Log.Write("---- 环境检查 ----");
		Log.Write("游戏目录：" + (string.IsNullOrEmpty(cfg.GameDir) ? "未找到！" : cfg.GameDir));
		Log.Write("OBS     ：" + (string.IsNullOrEmpty(cfg.ObsExe) ? "未安装！" : cfg.ObsExe));
		Log.Write("WebSocket：127.0.0.1:" + cfg.Port + "（密码" + (string.IsNullOrEmpty(cfg.Password) ? "为空" : "已获取") + "）");
		try
		{
			string text = FindFfmpeg();
			Log.Write("ffmpeg  ：" + ((text == null) ? "找不到！并行录制会失败 —— 把 ffmpeg.exe 放到本程序同一个文件夹里就行" : text));
		}
		catch
		{
		}
		try
		{
			if (!string.IsNullOrEmpty(cfg.GameDir))
			{
				string text2 = Path.Combine(cfg.GameDir, "ModLoaderSettings.json");
				if (File.Exists(text2))
				{
					Dictionary<string, object> dictionary = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(text2, Encoding.UTF8));
					bool flag = false;
					bool flag2 = false;
					object obj2 = ((dictionary != null && dictionary.ContainsKey("modules")) ? dictionary["modules"] : null);
					if (obj2 is Dictionary<string, object> dictionary2)
					{
						foreach (KeyValuePair<string, object> item in dictionary2)
						{
							if (item.Key.IndexOf("ReplayDnD", StringComparison.OrdinalIgnoreCase) >= 0)
							{
								flag2 = true;
								if (item.Value is Dictionary<string, object> dictionary3 && dictionary3.ContainsKey("enabled") && dictionary3["enabled"] != null && Convert.ToBoolean(dictionary3["enabled"]))
								{
									flag = true;
								}
							}
						}
					}
					string text3;
					if (flag)
					{
						text3 = "已启用 ✓";
					}
					else
					{
						text3 = (flag2 ? "没有启用 ⚠" : "整合包里没有这个 mod ⚠");
					}
					Log.Write("ReplayDnD mod（游戏能不能「带 rep 直接播放」靠它）：" + text3);
					if (!flag)
					{
						Log.Write("  ⚠ 请在整合包的 mod 配置菜单里启用 ReplayDnD（或 ReplayDnD-old），否则点开始录制后游戏只会停在标题画面、录出来的不是对局。本程序只读不改这个文件。");
					}
				}
				else
				{
					Log.Write("ReplayDnD mod：找不到 " + text2 + "（游戏目录可能不对）");
				}
			}
		}
		catch (Exception ex)
		{
			Log.Write("检查 ReplayDnD 失败（不影响使用）：" + ex.Message);
		}
		try
		{
			if (!obs.IsConnected)
			{
				obs.Connect(cfg.Port, cfg.Password);
			}
			Log.Write("OBS 连接：成功");
			Log.Write("当前场景：" + Convert.ToString(obs.Request("GetSceneList", null)["currentProgramSceneName"]));
			List<object> list = ObsClient.ToList(obs.Request("GetInputList", null)["inputs"]);
			if (list.Count == 0)
			{
				Log.Write("场景里还没有任何源（可用「准备 OBS」自动创建）");
			}
			foreach (object item2 in list)
			{
				Dictionary<string, object> dictionary4 = (Dictionary<string, object>)item2;
				Log.Write("  源：" + Convert.ToString(dictionary4["inputName"]) + "  (" + Convert.ToString(dictionary4["unversionedInputKind"]) + ")");
			}
		}
		catch (Exception ex)
		{
			Log.Write("OBS 连接：失败 —— " + ex.Message);
		}
	}

	private void OnPrepare(object sender, EventArgs e)
	{
		try
		{
			if (EnsureObs())
			{
				PrepareObs(verbose: true);
				Log.Write("OBS 准备完毕");
			}
		}
		catch (Exception ex)
		{
			Log.Write("准备 OBS 失败：" + ex.Message);
		}
	}

	private void OnSettings(object sender, EventArgs e)
	{
		Form form = new Form();
		form.Text = "设置";
		form.Font = Font;
		form.ClientSize = new Size(560, 480);
		form.StartPosition = FormStartPosition.CenterParent;
		PropertyGrid propertyGrid = new PropertyGrid();
		propertyGrid.Dock = DockStyle.Fill;
		propertyGrid.SelectedObject = cfg;
		Panel panel = new Panel();
		panel.Dock = DockStyle.Bottom;
		panel.Height = 44;
		Button button = new Button();
		button.Text = "确定";
		button.Location = new Point(360, 8);
		button.Size = new Size(80, 28);
		button.DialogResult = DialogResult.OK;
		Button button2 = new Button();
		button2.Text = "取消";
		button2.Location = new Point(450, 8);
		button2.Size = new Size(80, 28);
		button2.DialogResult = DialogResult.Cancel;
		panel.Controls.Add(button);
		panel.Controls.Add(button2);
		form.Controls.Add(propertyGrid);
		form.Controls.Add(panel);
		form.AcceptButton = button;
		form.CancelButton = button2;
		if (form.ShowDialog(this) == DialogResult.OK)
		{
			cfg.Save();
			RefreshEnvLabel();
			Log.Write("设置已保存");
		}
	}

	private void OnSoundToggle(object sender, EventArgs e)
	{
		if (!GameAudio.GetSystemMute(out var muted, out var message))
		{
			Log.Write("读取系统静音状态失败：" + message);
		}
		else
		{
			SetSystemMuteUi(!muted);
		}
	}

	private void SetSystemMuteUi(bool mute)
	{
		bool flag = GameAudio.SetSystemMute(mute, out var message);
		Log.Write((mute ? "【静音】" : "【恢复声音】") + "：" + message + (flag ? "" : "  失败"));
		if (flag)
		{
			weMutedSystem = mute;
			MarkSystemMuted(mute);
			if (mute)
			{
				Log.Write("现在你自己听不到任何系统声音；录像里仍然有游戏原声。录制结束后会【自动恢复】声音");
			}
			else
			{
				Log.Write("系统声音已恢复");
			}
		}
		RefreshSoundButtons();
	}

	private static void MarkSystemMuted(bool muted)
	{
		try
		{
			if (muted)
			{
				File.WriteAllText(MuteFlagPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Encoding.UTF8);
			}
			else if (File.Exists(MuteFlagPath))
			{
				File.Delete(MuteFlagPath);
			}
		}
		catch
		{
		}
	}

	private void RestoreSoundIfLeftMuted()
	{
		try
		{
			if (!File.Exists(MuteFlagPath) || !GameAudio.GetSystemMute(out var muted, out var _))
			{
				return;
			}
			if (muted)
			{
				if (GameAudio.SetSystemMute(mute: false, out var message2))
				{
					Log.Write("检测到上次录制期间静音了系统声音、程序没来得及恢复（可能是被强制结束）——已自动帮你把声音打开");
				}
				else
				{
					Log.Write("想恢复上次的静音但失败了：" + message2);
				}
			}
			File.Delete(MuteFlagPath);
			RefreshSoundButtons();
		}
		catch
		{
		}
	}

	private void RefreshSoundButtons()
	{
		if (btnSound != null)
		{
			if (!GameAudio.GetSystemMute(out var muted, out var _))
			{
				btnSound.Text = "声音：未知";
				btnSound.Width = 124;
				btnSound.BackColor = Color.Gainsboro;
				btnSound.ForeColor = Color.Black;
			}
			else if (muted)
			{
				btnSound.Text = "声音：静音 ✕（录完自动恢复）";
				btnSound.Width = 236;
				btnSound.BackColor = Color.FromArgb(190, 60, 60);
				btnSound.ForeColor = Color.White;
			}
			else
			{
				btnSound.Text = "声音：开 ✓";
				btnSound.Width = 124;
				btnSound.BackColor = Color.FromArgb(0, 120, 60);
				btnSound.ForeColor = Color.White;
			}
		}
	}

	private void OnGameAudioToggle(object sender, EventArgs e)
	{
		cfg.RecordGameAudio = !cfg.RecordGameAudio;
		if (!cfg.RecordGameAudio)
		{
			if (cfg.RecordBgm)
			{
				cfg.RecordBgm = false;
				ApplyBgmSetting(verbose: true);
				Log.Write("因为关掉了「录打击音效」，已自动把「录BGM」也关掉（连游戏里的 BGM 都不放），确保录出来完全无声");
			}
			else
			{
				ApplyBgmSetting(verbose: false);
			}
		}
		cfg.Save();
		Log.Write(cfg.RecordGameAudio ? "【录打击音效：开】录出来的视频会包含游戏声音（音效/BGM 都照实录；BGM 有没有由「录BGM」单独决定）" : "【录打击音效：关】录出来的视频将完全没有声音，「录BGM」同时被锁住（不可点）");
		if (obs != null && obs.IsConnected)
		{
			Thread thread = new Thread(() =>
			{
				try
				{
					ApplyGameAudioSetting();
				}
				catch
				{
				}
				try
				{
					for (int i = 1; i <= 4; i++)
					{
						SetInputMuteRaw("RepRecorder P" + i + " 声音", !cfg.RecordGameAudio);
					}
				}
				catch
				{
				}
			});
			thread.IsBackground = true;
			thread.Start();
		}
		RefreshGameAudioButton();
	}

	private void ApplyGameAudioSetting()
	{
		if (obs == null || !obs.IsConnected)
		{
			return;
		}
		try
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary["inputName"] = "RepRecorder 游戏声音";
			bool flag = false;
			try
			{
				flag = Convert.ToBoolean(obs.Request("GetInputMute", dictionary)["inputMuted"]);
			}
			catch
			{
			}
			bool flag2 = !cfg.RecordGameAudio;
			if (flag != flag2)
			{
				Dictionary<string, object> dictionary2 = new Dictionary<string, object>();
				dictionary2["inputName"] = "RepRecorder 游戏声音";
				dictionary2["inputMuted"] = flag2;
				obs.Request("SetInputMute", dictionary2);
			}
			Log.Write("游戏声音录制：" + (cfg.RecordGameAudio ? "开（会录进视频）" : "关（视频无声）") + "；OBS 音频源「RepRecorder 游戏声音」现在" + (flag2 ? "已静音" : "未静音"));
			ApplyRecordVolume("RepRecorder 游戏声音");
		}
		catch (Exception ex)
		{
			Log.Write("设置游戏声音录制失败：" + ex.Message);
		}
	}

	private void ApplyRecordVolume(string inputName)
	{
		if (obs == null || !obs.IsConnected)
		{
			return;
		}
		try
		{
			double num = cfg.RecordVolume;
			if (num <= 0.0)
			{
				num = 1.0;
			}
			if (num > 20.0)
			{
				num = 20.0;
			}
			if (!(Math.Abs(num - 1.0) < 0.001))
			{
				Dictionary<string, object> dictionary = new Dictionary<string, object>();
				dictionary["inputName"] = inputName;
				dictionary["inputVolumeMul"] = num;
				obs.Request("SetInputVolume", dictionary);
				Log.Write("录像音量：" + inputName + " 设为 " + num.ToString("0.##") + " 倍");
			}
		}
		catch (Exception ex)
		{
			Log.Write("设置录像音量失败（忽略）：" + ex.Message);
		}
	}

	private void RefreshGameAudioButton()
	{
		if (btnGameAudio == null)
		{
			return;
		}
		if (cfg.RecordGameAudio)
		{
			btnGameAudio.Text = "录打击音效：开 ✓";
			btnGameAudio.BackColor = Color.FromArgb(0, 120, 60);
			btnGameAudio.ForeColor = Color.White;
		}
		else
		{
			btnGameAudio.Text = "录打击音效：关 ✕";
			btnGameAudio.BackColor = Color.FromArgb(120, 120, 120);
			btnGameAudio.ForeColor = Color.White;
		}
		if (btnBgm != null)
		{
			btnBgm.Enabled = cfg.RecordGameAudio;
			if (!cfg.RecordGameAudio)
			{
				btnBgm.Text = "录BGM：关 ✕（已锁）";
				btnBgm.BackColor = Color.FromArgb(180, 180, 180);
				btnBgm.ForeColor = Color.DimGray;
			}
			else
			{
				RefreshBgmButton();
			}
		}
	}

	private void WarnIfBgmMuted()
	{
		try
		{
			if (string.IsNullOrEmpty(cfg.GameDir))
			{
				return;
			}
			string[] array = new string[2] { "modules\\ReplayDnD\\ReplayDnD.ini", "modules\\ReplayDnD-old\\ReplayDnD.ini" };
			foreach (string text in array)
			{
				string path = Path.Combine(cfg.GameDir, text);
				if (File.Exists(path))
				{
					string input = File.ReadAllText(path);
					if (Regex.IsMatch(input, "(?im)^\\s*MuteMusic\\s*=\\s*1\\s*$"))
					{
						Log.Write("提醒：" + text + " 里 MuteMusic=1 —— 游戏播放 rep 时不放背景音乐，所以录像里只有音效、没有 BGM。想连 BGM 一起录就点界面上的「录BGM」按钮。");
					}
				}
			}
		}
		catch
		{
		}
	}

	private List<string> BgmIniPaths()
	{
		List<string> list = new List<string>();
		if (string.IsNullOrEmpty(cfg.GameDir))
		{
			return list;
		}
		string[] bgmIniSubPaths = BgmIniSubPaths;
		foreach (string path in bgmIniSubPaths)
		{
			string text = Path.Combine(cfg.GameDir, path);
			if (File.Exists(text))
			{
				list.Add(text);
			}
		}
		return list;
	}

	private bool? ReadBgmMuted()
	{
		try
		{
			foreach (string item in BgmIniPaths())
			{
				string input = File.ReadAllText(item);
				Match match = Regex.Match(input, "(?im)^\\s*MuteMusic\\s*=\\s*(\\d+)\\s*$");
				if (match.Success)
				{
					return match.Groups[1].Value.Trim() != "0";
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private void ApplyBgmSetting(bool verbose)
	{
		int num = ((!cfg.RecordBgm) ? 1 : 0);
		List<string> list = BgmIniPaths();
		if (list.Count == 0)
		{
			if (verbose)
			{
				Log.Write("找不到 ReplayDnD.ini，BGM 开关没生效（检查「设置…」里的游戏目录）");
			}
			return;
		}
		foreach (string item in list)
		{
			try
			{
				string text = File.ReadAllText(item);
				string text2 = ((!Regex.IsMatch(text, "(?im)^\\s*MuteMusic\\s*=\\s*\\d+\\s*$")) ? (text.TrimEnd() + Environment.NewLine + "MuteMusic=" + num + Environment.NewLine) : Regex.Replace(text, "(?im)^\\s*MuteMusic\\s*=\\s*\\d+\\s*$", "MuteMusic=" + num));
				if (text2 != text)
				{
					File.WriteAllText(item, text2);
				}
				if (verbose)
				{
					Log.Write("BGM：" + Path.GetFileName(Path.GetDirectoryName(item)) + " → MuteMusic=" + num + ((num == 0) ? "（播放 rep 时会放 BGM）" : "（不放 BGM，只有音效）"));
				}
			}
			catch (Exception ex)
			{
				Log.Write("写 ReplayDnD.ini 失败（" + item + "）：" + ex.Message);
			}
		}
	}

	private void OnBgmToggle(object sender, EventArgs e)
	{
		cfg.RecordBgm = !cfg.RecordBgm;
		cfg.Save();
		ApplyBgmSetting(verbose: true);
		RefreshBgmButton();
		Log.Write(cfg.RecordBgm ? "【录BGM：开】录像里会带背景音乐（已把 ReplayDnD.ini 的 MuteMusic 改成 0）" : "【录BGM：关】录像里只有音效、没有背景音乐（已把 MuteMusic 改回 1）");
		if (cfg.RecordBgm)
		{
			Log.Write("提醒：BGM 有版权风险（B 站等平台会检测），用不用你自己判断。");
		}
	}

	private void RefreshBgmButton()
	{
		if (btnBgm != null)
		{
			if (cfg.RecordBgm)
			{
				btnBgm.Text = "录BGM：开 ✓";
				btnBgm.BackColor = Color.FromArgb(0, 120, 60);
			}
			else
			{
				btnBgm.Text = "录BGM：关 ✕";
				btnBgm.BackColor = Color.FromArgb(120, 120, 120);
			}
			btnBgm.ForeColor = Color.White;
		}
	}

	private void SyncBgmFromIni()
	{
		bool? flag = ReadBgmMuted();
		if (flag.HasValue)
		{
			cfg.RecordBgm = !flag.Value;
			if (!cfg.RecordGameAudio && cfg.RecordBgm)
			{
				cfg.RecordBgm = false;
				ApplyBgmSetting(verbose: true);
				Log.Write("因为「录打击音效」是关的，已把 BGM 一并关掉（MuteMusic 改回 1），保证录出来完全无声");
			}
			RefreshBgmButton();
			Log.Write("BGM 录制：" + (cfg.RecordBgm ? "开（ReplayDnD.ini 里 MuteMusic=0，录像会带背景音乐）" : "关（ReplayDnD.ini 里 MuteMusic=1，录像只有音效）") + "；想改就点界面上的「录BGM」按钮");
		}
		else
		{
			RefreshBgmButton();
			Log.Write("没找到 ReplayDnD.ini，读不到也改不了 BGM 开关（检查「设置…」里的游戏目录）");
		}
	}

	private void SetGameAudioMute(bool mute)
	{
		try
		{
			Process[] processesByName = Process.GetProcessesByName("th123");
			if (processesByName.Length == 0)
			{
				Log.Write("游戏没在运行，无法" + (mute ? "静音" : "打开") + "游戏声音");
				return;
			}
			Process[] array = processesByName;
			foreach (Process process in array)
			{
				bool flag = GameAudio.SetProcessMute(process.Id, mute, out var message);
				Log.Write("游戏声音（PID " + process.Id + "）：" + message + (flag ? "" : "（失败）"));
			}
			if (mute)
			{
				Log.Write("已静音游戏声音：你自己听不到，但录像里仍会录到游戏原声");
			}
			else
			{
				Log.Write("已恢复游戏声音");
			}
		}
		catch (Exception ex)
		{
			Log.Write("切换游戏声音失败：" + ex.Message);
		}
	}

	private void OnOpenLog(object sender, EventArgs e)
	{
		try
		{
			if (File.Exists(Log.FilePath))
			{
				Process.Start("notepad.exe", "\"" + Log.FilePath + "\"");
			}
			else
			{
				Log.Write("日志文件还没生成");
			}
		}
		catch
		{
		}
	}

	private void OnRecordSelected(object sender, EventArgs e)
	{
		OnStart(sender, e);
	}

	private IntPtr WaitForMainWindow(Process p, int seconds)
	{
		DateTime dateTime = DateTime.Now.AddSeconds(seconds);
		while (DateTime.Now < dateTime)
		{
			Thread.Sleep(400);
			try
			{
				p.Refresh();
				if (p.HasExited)
				{
					return IntPtr.Zero;
				}
				if (p.MainWindowHandle != IntPtr.Zero)
				{
					return p.MainWindowHandle;
				}
			}
			catch
			{
				return IntPtr.Zero;
			}
		}
		return IntPtr.Zero;
	}

	private void MoveWindowClientTo(IntPtr h, int x, int y)
	{
		if (GetWindowRect(h, out var r) && GetClientRect(h, out var _))
		{
			POINT p = default;
			p.X = 0;
			p.Y = 0;
			if (ClientToScreen(h, ref p))
			{
				int num = p.X - r.Left;
				int num2 = p.Y - r.Top;
				SetWindowPos(h, IntPtr.Zero, x - num, y - num2, r.Right - r.Left, r.Bottom - r.Top, 20u);
			}
		}
	}

	private bool ForceAllWindowTitles(List<IntPtr> handles, List<string> wants, int waitSeconds)
	{
		try
		{
			int count = handles.Count;
			string[] array = new string[count];
			int[] array2 = new int[count];
			DateTime dateTime = DateTime.Now.AddSeconds(waitSeconds);
			while (DateTime.Now < dateTime)
			{
				bool flag = true;
				for (int i = 0; i < count; i++)
				{
					if (!(handles[i] == IntPtr.Zero))
					{
						StringBuilder stringBuilder = new StringBuilder(512);
						GetWindowText(handles[i], stringBuilder, 512);
						string text = stringBuilder.ToString();
						if (text == array[i])
						{
							array2[i]++;
						}
						else
						{
							array2[i] = 0;
							array[i] = text;
						}
						if (array2[i] < 3)
						{
							flag = false;
						}
					}
				}
				if (flag)
				{
					break;
				}
				Thread.Sleep(400);
			}
			for (int j = 0; j < 15; j++)
			{
				for (int i = 0; i < count; i++)
				{
					if (handles[i] != IntPtr.Zero)
					{
						SetWindowText(handles[i], wants[i]);
					}
				}
				Thread.Sleep(300);
				bool flag2 = true;
				for (int i = 0; i < count; i++)
				{
					if (!(handles[i] == IntPtr.Zero))
					{
						StringBuilder stringBuilder = new StringBuilder(512);
						GetWindowText(handles[i], stringBuilder, 512);
						if (stringBuilder.ToString() != wants[i])
						{
							flag2 = false;
							break;
						}
					}
				}
				if (flag2 && j >= 2)
				{
					return true;
				}
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	private static string MakeWindowString(string title, string cls)
	{
		return ((title == null) ? "" : title) + ":" + ((cls == null) ? "" : cls) + ":th123.exe";
	}

	private string BuildWindowStringForProcess(Process p)
	{
		string title = null;
		string cls = null;
		int pid = 0;
		try
		{
			pid = p.Id;
		}
		catch
		{
			return null;
		}
		EnumWindows((IntPtr h, IntPtr l) =>
		{
			GetWindowThreadProcessId(h, out var pid2);
			if (pid2 == (uint)pid && IsWindowVisible(h))
			{
				StringBuilder stringBuilder = new StringBuilder(512);
				GetWindowText(h, stringBuilder, 512);
				if (stringBuilder.Length > 0)
				{
					StringBuilder stringBuilder2 = new StringBuilder(256);
					GetClassName(h, stringBuilder2, 256);
					title = stringBuilder.ToString();
					cls = stringBuilder2.ToString();
					return false;
				}
			}
			return true;
		}, IntPtr.Zero);
		if (title == null)
		{
			return null;
		}
		return MakeWindowString(title, cls);
	}

	private void EnsureInput(string name, string kind, Dictionary<string, object> settings)
	{
		List<object> list = ObsClient.ToList(obs.Request("GetInputList", null)["inputs"]);
		foreach (object item in list)
		{
			Dictionary<string, object> dictionary = (Dictionary<string, object>)item;
			if (Convert.ToString(dictionary["inputName"]) == name)
			{
				return;
			}
		}
		Dictionary<string, object> dictionary2 = new Dictionary<string, object>();
		dictionary2["sceneName"] = "RepRecorder 录制场景";
		dictionary2["inputName"] = name;
		dictionary2["inputKind"] = kind;
		dictionary2["inputSettings"] = settings;
		dictionary2["sceneItemEnabled"] = true;
		obs.Request("CreateInput", dictionary2);
		Log.Write("并行：已创建源 " + name);
	}

	private static Dictionary<string, object> AudioInputSettings(string window)
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		dictionary["window"] = window;
		dictionary["priority"] = 1;
		return dictionary;
	}

	private static Dictionary<string, object> VideoInputSettings(string window)
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		dictionary["window"] = window;
		dictionary["method"] = 2;
		dictionary["client_area"] = true;
		dictionary["cursor"] = false;
		dictionary["priority"] = 1;
		return dictionary;
	}

	private void ApplyCellWindow(Dictionary<string, string> applied, string vName, string aName, string window)
	{
		if (!applied.TryGetValue(vName, out var value) || !(value == window))
		{
			SetInputSettingsRaw(vName, VideoInputSettings(window));
			SetInputSettingsRaw(aName, AudioInputSettings(window));
			applied[vName] = window;
			applied[aName] = window;
			Log.Write("并行：重设采集源绑定 " + vName + " → " + window);
		}
	}

	private void SetInputSettingsRaw(string inputName, Dictionary<string, object> settings)
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		dictionary["inputName"] = inputName;
		dictionary["inputSettings"] = settings;
		obs.Request("SetInputSettings", dictionary);
	}

	private void SetTransformRaw(string scene, int itemId, int x, int y, int cw, int ch)
	{
		if (itemId >= 0)
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary["positionX"] = (double)x;
			dictionary["positionY"] = (double)y;
			dictionary["scaleX"] = 1.0;
			dictionary["scaleY"] = 1.0;
			dictionary["boundsType"] = ((cw > 0 && ch > 0) ? "OBS_BOUNDS_SCALE_INNER" : "OBS_BOUNDS_NONE");
			dictionary["boundsAlignment"] = 5;
			if (cw > 0)
			{
				dictionary["boundsWidth"] = (double)cw;
			}
			if (ch > 0)
			{
				dictionary["boundsHeight"] = (double)ch;
			}
			Dictionary<string, object> dictionary2 = new Dictionary<string, object>();
			dictionary2["sceneName"] = scene;
			dictionary2["sceneItemId"] = itemId;
			dictionary2["sceneItemTransform"] = dictionary;
			obs.Request("SetSceneItemTransform", dictionary2);
		}
	}

	private void SetSceneItemEnabled(string scene, int itemId, bool enabled)
	{
		if (itemId < 0)
		{
			return;
		}
		try
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary["sceneName"] = scene;
			dictionary["sceneItemId"] = itemId;
			dictionary["sceneItemEnabled"] = enabled;
			obs.Request("SetSceneItemEnabled", dictionary);
		}
		catch (Exception ex)
		{
			Log.Write("并行：切换元素显示状态失败：" + ex.Message);
		}
	}

	private void SetTransformFitCell(string scene, int itemId, int x, int y, int cw, int ch, int idx)
	{
		if (itemId < 0)
		{
			Log.Write("并行：第 " + idx + " 格元素没找到（itemId<0），位置没设上");
			return;
		}
		try
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary["positionX"] = (double)x;
			dictionary["positionY"] = (double)y;
			dictionary["scaleX"] = 1.0;
			dictionary["scaleY"] = 1.0;
			dictionary["boundsType"] = "OBS_BOUNDS_SCALE_INNER";
			dictionary["boundsAlignment"] = 5;
			dictionary["boundsWidth"] = (double)cw;
			dictionary["boundsHeight"] = (double)ch;
			Dictionary<string, object> dictionary2 = new Dictionary<string, object>();
			dictionary2["sceneName"] = scene;
			dictionary2["sceneItemId"] = itemId;
			dictionary2["sceneItemTransform"] = dictionary;
			obs.Request("SetSceneItemTransform", dictionary2);
			double num = 0.0;
			double num2 = 0.0;
			try
			{
				Dictionary<string, object> dictionary3 = new Dictionary<string, object>();
				dictionary3["sceneName"] = scene;
				dictionary3["sceneItemId"] = itemId;
				Dictionary<string, object> dictionary4 = obs.Request("GetSceneItemTransform", dictionary3);
				if (dictionary4.ContainsKey("sceneItemTransform"))
				{
					Dictionary<string, object> dictionary5 = (Dictionary<string, object>)dictionary4["sceneItemTransform"];
					if (dictionary5.ContainsKey("sourceWidth"))
					{
						num = Convert.ToDouble(dictionary5["sourceWidth"]);
					}
					if (dictionary5.ContainsKey("sourceHeight"))
					{
						num2 = Convert.ToDouble(dictionary5["sourceHeight"]);
					}
				}
			}
			catch
			{
			}
			Log.Write("并行：第 " + idx + " 格摆到 (" + x + "," + y + ") 并用边界框缩放到 " + cw + "×" + ch + "（OBS 报的源尺寸 " + num + "×" + num2 + "，只是参考值）");
		}
		catch (Exception ex)
		{
			Log.Write("并行：第 " + idx + " 格设置位置失败 " + ex.Message);
		}
	}

	private void SetAudioTracksRaw(string inputName, int trackNo)
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		for (int i = 1; i <= 6; i++)
		{
			dictionary[i.ToString()] = i == trackNo;
		}
		Dictionary<string, object> dictionary2 = new Dictionary<string, object>();
		dictionary2["inputName"] = inputName;
		dictionary2["inputAudioTracks"] = dictionary;
		obs.Request("SetInputAudioTracks", dictionary2);
	}

	private void SetInputMuteRaw(string inputName, bool muted)
	{
		try
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary["inputName"] = inputName;
			dictionary["inputMuted"] = muted;
			obs.Request("SetInputMute", dictionary);
		}
		catch
		{
		}
	}

	private void SetProfileParamRaw(string cat, string name, string val)
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		dictionary["parameterCategory"] = cat;
		dictionary["parameterName"] = name;
		dictionary["parameterValue"] = val;
		obs.Request("SetProfileParameter", dictionary);
	}

	private static string FindFfmpeg()
	{
		List<string> list = new List<string>();
		try
		{
			list.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"));
		}
		catch
		{
		}
		try
		{
			list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft\\WinGet\\Links\\ffmpeg.exe"));
		}
		catch
		{
		}
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft\\WinGet\\Packages");
			if (Directory.Exists(path))
			{
				string[] directories = Directory.GetDirectories(path, "Gyan.FFmpeg*");
				foreach (string path2 in directories)
				{
					string[] directories2 = Directory.GetDirectories(path2, "ffmpeg*");
					foreach (string path3 in directories2)
					{
						list.Add(Path.Combine(path3, "bin\\ffmpeg.exe"));
					}
				}
			}
		}
		catch
		{
		}
		foreach (string item in list)
		{
			try
			{
				if (File.Exists(item))
				{
					return item;
				}
			}
			catch
			{
			}
		}
		try
		{
			string environmentVariable = Environment.GetEnvironmentVariable("PATH");
			if (environmentVariable != null)
			{
				string[] directories = environmentVariable.Split(';');
				foreach (string path2 in directories)
				{
					if (path2.Trim().Length != 0)
					{
						string text = Path.Combine(path2.Trim(), "ffmpeg.exe");
						if (File.Exists(text))
						{
							return text;
						}
					}
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private bool SplitOne(string grid, Job job, int idx, int cw, int ch, int x, int y, double startSec, double maxSeconds, out string err)
	{
		err = "";
		string text = FindFfmpeg();
		if (text == null)
		{
			err = "找不到 ffmpeg";
			return false;
		}
		string text2 = Path.ChangeExtension(job.Rep, ".mp4");
		string text3 = (string.IsNullOrEmpty(cfg.ParallelSplitEncoder) ? "h264_nvenc" : cfg.ParallelSplitEncoder);
		List<string> list = new List<string>();
		if (text3 == "libx264")
		{
			list.Add(" -c:v libx264 -preset medium -crf " + cfg.ParallelSplitCQ + " -pix_fmt yuv420p");
		}
		else
		{
			list.Add(" -c:v " + text3 + " -preset p5 -tune hq -rc vbr -cq " + cfg.ParallelSplitCQ + " -b:v 0 -pix_fmt yuv420p");
			list.Add(" -c:v libx264 -preset medium -crf " + cfg.ParallelSplitCQ + " -pix_fmt yuv420p");
		}
		if (startSec < 0.0)
		{
			startSec = 0.0;
		}
		double num = maxSeconds - startSec;
		string text4 = ((startSec > 0.2) ? (" -ss " + startSec.ToString("0.00", CultureInfo.InvariantCulture)) : "");
		string text5 = "-hide_banner -y" + text4 + " -i \"" + grid + "\" -map 0:v:0";
		int num2 = cfg.ParallelEdgeInset;
		if (num2 < 0)
		{
			num2 = 0;
		}
		int num3 = ((cfg.CanvasWidth > 0) ? cfg.CanvasWidth : 1440);
		int num4 = ((num3 > 0) ? ((int)Math.Round((double)num2 * (double)cw / (double)num3)) : 0);
		int num5 = ((cw > 0) ? ((int)Math.Round((double)num4 * (double)ch / (double)cw)) : 0);
		if (num4 * 2 >= cw - 16)
		{
			num4 = 0;
		}
		if (num5 * 2 >= ch - 16)
		{
			num5 = 0;
		}
		int num6 = cw - num4 * 2;
		int num7 = ch - num5 * 2;
		if (num4 > 0)
		{
			Log.Write("并行切分：每格往里裁 " + num4 + " 像素（格子坐标）≈ 成品 " + num2 + " 像素，裁后 " + num6 + "×" + num7 + " 再缩放到 " + num3 + "×" + ((cfg.CanvasHeight > 0) ? cfg.CanvasHeight : 1080));
		}
		string text6 = "";
		try
		{
			if (cfg.CanvasWidth > cw || cfg.CanvasHeight > ch)
			{
				text6 = ",scale=" + cfg.CanvasWidth + ":" + cfg.CanvasHeight + ":flags=lanczos";
			}
		}
		catch
		{
		}
		string text7 = "";
		string text8 = "";
		if (num > 1.5)
		{
			string text9 = (num - 0.6).ToString("0.00", CultureInfo.InvariantCulture);
			text7 = ",fade=t=out:st=" + text9 + ":d=0.6";
			text8 = " -af afade=t=out:st=" + text9 + ":d=0.6";
		}
		string text10 = " -filter:v crop=" + num6 + ":" + num7 + ":" + (x + num4) + ":" + (y + num5) + text6 + text7;
		string text11 = ((num > 10.0) ? (" -t " + num.ToString("0.0", CultureInfo.InvariantCulture)) : "");
		string text12 = " -c:a aac -b:a 192k" + text8 + text11;
		List<string> list2 = new List<string>();
		foreach (string item in list)
		{
			list2.Add(text5 + " -map 0:a:" + idx + text10 + item + text12);
			list2.Add(text5 + text10 + item + text12);
		}
		list2.Add(text5 + text10 + list[0] + " -an" + text11);
		int num8 = 0;
		foreach (string item2 in list2)
		{
			num8++;
			try
			{
				ProcessStartInfo processStartInfo = new ProcessStartInfo(text, item2 + " \"" + text2 + "\"");
				processStartInfo.UseShellExecute = false;
				processStartInfo.CreateNoWindow = true;
				processStartInfo.WorkingDirectory = Path.GetDirectoryName(text);
				Process process = Process.Start(processStartInfo);
				process.WaitForExit();
				if (process.ExitCode == 0 && File.Exists(text2) && new FileInfo(text2).Length > 4096)
				{
					if (num8 > 1)
					{
						Log.Write("并行切分：第 " + num8 + " 种参数组合才成功（" + ((num8 > 2) ? ("这台机器多半没有 NVIDIA 显卡，" + text3 + " 用不了 → 已自动换成 libx264 软件编码") : "退化成不带音频") + "）");
					}
					return true;
				}
				err = "ffmpeg 退出码 " + process.ExitCode;
				if (process.ExitCode != 0 && num8 == 1 && list.Count > 1)
				{
					Log.Write("并行切分：" + text3 + " 失败（可能是这台机器没有对应显卡），正在自动改用 libx264 软件编码重试…");
				}
			}
			catch (Exception ex)
			{
				err = ex.Message;
			}
		}
		return false;
	}

	private bool SourceLooksStatic(string sourceName)
	{
		try
		{
			byte[] array = ShotOf(sourceName);
			Thread.Sleep(800);
			byte[] array2 = ShotOf(sourceName);
			if (array == null || array2 == null)
			{
				return false;
			}
			if (array.Length != array2.Length)
			{
				return false;
			}
			int num = array.Length / 100 + 16;
			int num2 = 0;
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i] != array2[i])
				{
					num2++;
					if (num2 > num)
					{
						return false;
					}
				}
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private void CaptureOriginalRecordDir()
	{
		if (originalRecordDir != null)
		{
			return;
		}
		try
		{
			originalRecordDir = Convert.ToString(obs.Request("GetRecordDirectory", null)["recordDirectory"]);
		}
		catch
		{
		}
	}

	private void RunParallel()
	{
		int num = cfg.ParallelCount;
		if (num < 1)
		{
			num = 1;
		}
		if (num > 6)
		{
			num = 6;
		}
		List<Job> list = selectedBatch;
		selectedBatch = null;
		if (list != null)
		{
			if (list.Count == 0)
			{
				return;
			}
			Log.Write("==== 选中批次开始：共 " + list.Count + " 个，每批 " + num + " 个 ====");
			forceRecord = true;
			try
			{
				for (int i = 0; i < list.Count; i += num)
				{
					if (cancel)
					{
						Log.Write("已停止：剩下的选中项不再录");
						break;
					}
					int num2 = Math.Min(num, list.Count - i);
					Log.Write("---- 第 " + (i / num + 1) + " 批（" + num2 + " 个）----");
					obsStuckRetried = false;
					RunParallelBatch(list.GetRange(i, num2));
				}
			}
			finally
			{
				forceRecord = false;
			}
			Log.Write("==== 选中批次全部结束 ====");
			return;
		}
		List<Job> list2 = new List<Job>();
		lock (jobsLock)
		{
			foreach (Job job in jobs)
			{
				if (job.Status == "等待")
				{
					list2.Add(job);
					if (list2.Count >= num)
					{
						break;
					}
				}
			}
		}
		if (list2.Count == 0)
		{
			Log.Write("并行录制：没有待录制的 rep（都录过了？可以点「刷新」看看；或者先选中几行再点「录制选中的」）");
			return;
		}
		obsStuckRetried = false;
		RunParallelBatch(list2);
	}

	private void RunParallelBatch(List<Job> batch)
	{
		if (batch.Count == 0)
		{
			return;
		}
		if (batch.Count == 1)
		{
			Log.Write("这一批只有 1 个，改成普通单录");
			if (cfg.AutoPrepareObs)
			{
				PrepareObs(verbose: false);
			}
			if (cfg.AutoPrepareObs && cfg.UseDedicatedScene)
			{
				SwitchScene(toOurs: true);
			}
			CaptureOriginalRecordDir();
			ProcessJob(batch[0]);
			return;
		}
		if (!cfg.AutoPrepareObs)
		{
			Log.Write("并行录制需要打开「自动准备采集源并绑定游戏窗口」，已中止");
			return;
		}
		DateTime now = DateTime.Now;
		for (int i = 0; i < batch.Count; i++)
		{
			batch[i].Started = now;
		}
		bool flag = cfg.RecordGameAudio && batch.Count <= 6;
		if (batch.Count > 6 && cfg.RecordGameAudio)
		{
			Log.Write("⚠ 同时录 " + batch.Count + " 个，但 OBS 最多只有 6 条音轨 —— 每格的声音没法分开，这一批会录成【无声】视频（想要每格各自的声音，请把「同时录制数量」降到 6 以内）");
		}
		Log.Write("==== 并行录制开始：同时录 " + batch.Count + " 个 ====");
		List<Process> list = new List<Process>();
		List<Process> list2 = new List<Process>();
		List<string> list3 = new List<string>();
		List<string> list4 = new List<string>();
		List<int> list5 = new List<int>();
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		int num = -1;
		bool enabled = true;
		DateTime minValue = DateTime.MinValue;
		double[] array = null;
		double[] array2 = null;
		double[] array3 = null;
		string gridFile = null;
		bool flag2 = false;
		int CW = 0;
		int CH = 0;
		int cols = 1;
		int num2 = 1;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		string text = null;
		try
		{
			PrepareObs(verbose: false);
			for (int j = 1; j <= 6; j++)
			{
				string[] array4 = new string[2]
				{
					"RepRecorder P" + j + " 画面",
					"RepRecorder P" + j + " 声音"
				};
				string[] array5 = array4;
				foreach (string text2 in array5)
				{
					try
					{
						SetAudioTracksRaw(text2, 0);
					}
					catch
					{
					}
					try
					{
						Dictionary<string, object> dictionary2 = new Dictionary<string, object>();
						dictionary2["inputName"] = text2;
						obs.Request("RemoveInput", dictionary2);
					}
					catch
					{
					}
				}
			}
			Log.Write("并行：已清理上一批可能残留的 P1~P6 采集源并把音轨归零");
			try
			{
				Dictionary<string, object> dictionary3 = obs.Request("GetVideoSettings", null);
				num3 = Convert.ToInt32(dictionary3["baseWidth"]);
				num4 = Convert.ToInt32(dictionary3["baseHeight"]);
				if (dictionary3.ContainsKey("fpsNumerator") && dictionary3["fpsNumerator"] != null)
				{
					num5 = Convert.ToInt32(dictionary3["fpsNumerator"]);
				}
			}
			catch
			{
			}
			try
			{
				Dictionary<string, object> dictionary4 = new Dictionary<string, object>();
				dictionary4["parameterCategory"] = "SimpleOutput";
				dictionary4["parameterName"] = "RecTracks";
				Dictionary<string, object> dictionary5 = obs.Request("GetProfileParameter", dictionary4);
				text = (dictionary5.ContainsKey("parameterValue") ? Convert.ToString(dictionary5["parameterValue"]) : "1");
			}
			catch
			{
			}
			try
			{
				Dictionary<string, object> dictionary6 = new Dictionary<string, object>();
				dictionary6["recordDirectory"] = Path.GetDirectoryName(batch[0].Rep);
				obs.Request("SetRecordDirectory", dictionary6);
			}
			catch
			{
			}
			ApplyAudioRouting(beforeRecording: true);
			CaptureOriginalRecordDir();
			try
			{
				num = CleanupDuplicateItems("RepRecorder 录制场景", "RepRecorder 游戏窗口");
				if (num >= 0)
				{
					try
					{
						Dictionary<string, object> dictionary7 = new Dictionary<string, object>();
						dictionary7["sceneName"] = "RepRecorder 录制场景";
						dictionary7["sceneItemId"] = num;
						enabled = Convert.ToBoolean(obs.Request("GetSceneItemEnabled", dictionary7)["sceneItemEnabled"]);
					}
					catch
					{
					}
					SetSceneItemEnabled("RepRecorder 录制场景", num, enabled: false);
				}
				SetInputMuteRaw("RepRecorder 游戏声音", muted: true);
				Log.Write("并行：已把单文件用的采集源「RepRecorder 游戏窗口」隐藏、并把「RepRecorder 游戏声音」静音（否则画面/声音会漏进格子）");
			}
			catch (Exception ex)
			{
				Log.Write("并行：隐藏单文件采集源失败（可忽略）：" + ex.Message);
			}
			array = new double[batch.Count];
			array2 = new double[batch.Count];
			array3 = new double[batch.Count];
			List<IntPtr> handles2 = new List<IntPtr>();
			List<string> list6 = new List<string>();
			for (int i = 0; i < batch.Count; i++)
			{
				SetJob(batch[i], "启动游戏", "并行 " + (i + 1) + "/" + batch.Count);
				Process item = null;
				try
				{
					ProcessStartInfo processStartInfo = new ProcessStartInfo(Path.Combine(cfg.GameDir, "th123.exe"), "\"" + batch[i].Rep + "\"");
					processStartInfo.WorkingDirectory = cfg.GameDir;
					item = Process.Start(processStartInfo);
					Log.Write("并行：第 " + (i + 1) + " 个实例已启动 → " + Path.GetFileName(batch[i].Rep));
				}
				catch (Exception ex)
				{
					Log.Write("并行：第 " + (i + 1) + " 个实例启动失败 " + ex.Message);
				}
				list2.Add(item);
				list6.Add("th123 #" + (i + 1));
				Thread.Sleep(400);
			}
			for (int i = 0; i < list2.Count; i++)
			{
				if (list2[i] == null)
				{
					handles2.Add(IntPtr.Zero);
					continue;
				}
				IntPtr intPtr = WaitForMainWindow(list2[i], 90);
				handles2.Add(intPtr);
				if (intPtr == IntPtr.Zero)
				{
					Log.Write("并行：第 " + (i + 1) + " 个实例的窗口没出现（这一格会是空的）");
				}
			}
			for (int i = 0; i < handles2.Count; i++)
			{
				if (!(handles2[i] == IntPtr.Zero) && GetClientRect(handles2[i], out var r) && r.Right > 0 && r.Bottom > 0 && CW == 0)
				{
					CW = r.Right;
					CH = r.Bottom;
					if (CW > 1920 || CH > 1440)
					{
						CW = 640;
						CH = 480;
					}
				}
			}
			if (CW <= 0 || CH <= 0)
			{
				CW = 640;
				CH = 480;
			}
			int num6 = 4096;
			cols = (int)Math.Ceiling(Math.Sqrt((double)batch.Count * (double)CH / (double)CW));
			if (cols < 1)
			{
				cols = 1;
			}
			num2 = (batch.Count + cols - 1) / cols;
			while (cols * CW > num6 && cols > 1)
			{
				cols--;
				num2 = (batch.Count + cols - 1) / cols;
			}
			while (num2 * CH > num6 && num2 > 1)
			{
				num2--;
				cols = (batch.Count + num2 - 1) / num2;
			}
			if (num2 * CH > num6 || cols * CW > num6)
			{
				Log.Write("⚠ 并行：画布 " + cols * CW + "×" + num2 * CH + " 超过 OBS 上限 " + num6 + "，可能会失败（把「同时录制数量」调小一点）");
			}
			Dictionary<string, object> dictionary8 = new Dictionary<string, object>();
			dictionary8["baseWidth"] = cols * CW;
			dictionary8["baseHeight"] = num2 * CH;
			dictionary8["outputWidth"] = cols * CW;
			dictionary8["outputHeight"] = num2 * CH;
			dictionary8["fpsNumerator"] = 60;
			dictionary8["fpsDenominator"] = 1;
			obs.Request("SetVideoSettings", dictionary8);
			Log.Write("并行：每格画面 " + CW + "×" + CH + "，" + cols + " 列 × " + num2 + " 行，中继画布 " + cols * CW + "×" + num2 * CH);
			bool flag3 = ForceAllWindowTitles(handles2, list6, 20);
			Log.Write("并行：窗口标题改成唯一值 " + (flag3 ? "成功" : "失败（可能有多格录到同一个游戏）"));
			for (int i = 0; i < handles2.Count; i++)
			{
				if (handles2[i] != IntPtr.Zero)
				{
					MoveWindowClientTo(handles2[i], i % 5 * 80, i / 5 * 60);
				}
			}
			for (int i = 0; i < batch.Count; i++)
			{
				string text3 = "RepRecorder P" + (i + 1) + " 画面";
				string text4 = "RepRecorder P" + (i + 1) + " 声音";
				try
				{
					EnsureInput(text3, "window_capture", VideoInputSettings(""));
					EnsureInput(text4, "wasapi_process_output_capture", AudioInputSettings(""));
					list3.Add(text3);
					list4.Add(text4);
					int num7 = CleanupDuplicateItems("RepRecorder 录制场景", text3);
					list5.Add(num7);
					SetTransformRaw("RepRecorder 录制场景", num7, i % cols * CW, i / cols * CH, CW, CH);
					CleanupDuplicateItems("RepRecorder 录制场景", text4);
					SetAudioTracksRaw(text4, (batch.Count <= 6) ? (i + 1) : 0);
					SetInputMuteRaw(text4, !flag);
				}
				catch (Exception ex)
				{
					Log.Write("并行：第 " + (i + 1) + " 格建源失败 " + ex.Message);
				}
			}
			SetProfileParamRaw("SimpleOutput", "RecTracks", ((1 << Math.Min(batch.Count, 6)) - 1).ToString());
			try
			{
				foreach (string item2 in Program.FindMicTargets(obs))
				{
					Dictionary<string, object> dictionary9 = new Dictionary<string, object>();
					dictionary9["inputName"] = item2;
					dictionary9["inputMuted"] = true;
					obs.Request("SetInputMute", dictionary9);
				}
			}
			catch
			{
			}
			StopObsRecordingIfAny();
			minValue = DateTime.Now;
			obs.Request("StartRecord", null);
			bool flag4 = false;
			for (int l = 0; l < 2; l++)
			{
				if (flag4)
				{
					break;
				}
				Thread.Sleep(1500);
				try
				{
					Dictionary<string, object> dictionary10 = obs.Request("GetRecordStatus", null);
					flag4 = dictionary10.ContainsKey("outputActive") && dictionary10["outputActive"] != null && Convert.ToBoolean(dictionary10["outputActive"]);
				}
				catch
				{
				}
				if (!flag4 && l == 0)
				{
					Log.Write("并行：StartRecord 之后 OBS 说没在录，重试一次…");
					try
					{
						obs.Request("StartRecord", null);
					}
					catch (Exception ex)
					{
						Log.Write("并行：重试 StartRecord 失败：" + ex.Message);
					}
				}
			}
			if (!flag4)
			{
				Log.Write("并行：❌ OBS 没有真正开始录像（StartRecord 返回成功但 outputActive=false）。常见原因：OBS 里还有一段没停干净的录像、OBS 卡住、录制目录不可写、或录制格式/音轨设置异常。");
				if (!obsStuckRetried)
				{
					obsStuckRetried = true;
					Log.Write("并行：正在自动重启 OBS 并把这批重跑一次（第 1 次也是唯一一次自动重试）…");
					try
					{
						foreach (Process item3 in list2)
						{
							try
							{
								if (item3 != null && !item3.HasExited)
								{
									item3.Kill();
								}
							}
							catch
							{
							}
						}
						KillGame();
						try
						{
							obs.Dispose();
						}
						catch
						{
						}
						Process[] processesByName = Process.GetProcessesByName("obs64");
						foreach (Process process in processesByName)
						{
							try
							{
								process.Kill();
							}
							catch
							{
							}
						}
						Thread.Sleep(4000);
						if (EnsureObs())
						{
							if (cfg.AutoPrepareObs)
							{
								PrepareObs(verbose: false);
							}
							if (cfg.AutoPrepareObs && cfg.UseDedicatedScene)
							{
								SwitchScene(toOurs: true);
							}
							Log.Write("并行：OBS 已重启并重新准备好，开始重跑这一批…");
							RunParallelBatch(batch);
							return;
						}
						Log.Write("并行：重启 OBS 失败");
					}
					catch (Exception ex)
					{
						Log.Write("并行：自动重启 OBS 出错：" + ex.Message);
					}
				}
				{
					foreach (Job item4 in batch)
					{
						SetJob(item4, "失败", "OBS 没有真正开始录像（已中止，避免白等）");
					}
					return;
				}
			}
			Log.Write("并行录制：已开始录像（" + batch.Count + " 格，已确认 OBS 真的在录）");
			List<string> list7 = new List<string>();
			for (int i = 0; i < batch.Count && i < list3.Count; i++)
			{
				try
				{
					string text5 = null;
					if (handles2[i] != IntPtr.Zero)
					{
						StringBuilder stringBuilder = new StringBuilder(512);
						GetWindowText(handles2[i], stringBuilder, 512);
						string text6 = stringBuilder.ToString();
						if (text6.Length == 0 || text6.IndexOf("th123 #", StringComparison.Ordinal) < 0)
						{
							text6 = "th123 #" + (i + 1);
							Log.Write("并行：第 " + (i + 1) + " 格窗口标题读不到/不对，改用强制标题「" + text6 + "」绑定");
						}
						text5 = MakeWindowString(text6, "th123_110a");
					}
					if (text5 != null)
					{
						list7.Add(text5);
						ApplyCellWindow(dictionary, list3[i], list4[i], text5);
					}
					SetTransformFitCell("RepRecorder 录制场景", (i < list5.Count) ? list5[i] : (-1), i % cols * CW, i / cols * CH, CW, CH, i + 1);
					if (batch.Count <= 6)
					{
						SetAudioTracksRaw(list4[i], i + 1);
					}
					SetInputMuteRaw(list4[i], !flag);
					Log.Write("并行：第 " + (i + 1) + " 格绑定窗口串 = " + ((text5 == null) ? "<窗口没出现>" : text5));
				}
				catch (Exception ex)
				{
					Log.Write("并行：第 " + (i + 1) + " 格绑定失败 " + ex.Message);
				}
			}
			int count = new HashSet<string>(list7).Count;
			if (count < list7.Count)
			{
				Log.Write("⚠ 并行：有 " + (list7.Count - count + 1) + " 个格子绑到了同一个窗口（" + string.Join(" | ", list7.ToArray()) + "）—— 录出来会是同一场对局");
			}
			Log.Write("并行：建源并绑定完成（" + list7.Count + " 格，" + count + " 个不同窗口）");
			for (int i = 0; i < list4.Count; i++)
			{
				try
				{
					Dictionary<string, object> dictionary11 = new Dictionary<string, object>();
					dictionary11["inputName"] = list4[i];
					bool flag5 = false;
					try
					{
						flag5 = Convert.ToBoolean(obs.Request("GetInputMute", dictionary11)["inputMuted"]);
					}
					catch
					{
					}
					string text7 = "";
					try
					{
						Dictionary<string, object> dictionary12 = obs.Request("GetInputAudioTracks", dictionary11);
						if (dictionary12.ContainsKey("inputAudioTracks") && dictionary12["inputAudioTracks"] is Dictionary<string, object> dictionary13)
						{
							for (int m = 1; m <= 6; m++)
							{
								text7 += ((dictionary13.ContainsKey(m.ToString()) && Convert.ToBoolean(dictionary13[m.ToString()])) ? "1" : "0");
							}
						}
					}
					catch
					{
					}
					Log.Write("并行：第 " + (i + 1) + " 格音源状态 —— 静音=" + (flag5 ? "是" : "否") + "  音轨=" + ((text7 == "") ? "?" : text7) + "（应只有第 " + (i + 1) + " 位是 1）");
				}
				catch
				{
				}
			}
			string text8;
			if (flag)
			{
				text8 = "开（每格各自一条音轨，切分后各带自己的原声）";
			}
			else
			{
				text8 = (cfg.RecordGameAudio ? "关（同时录的格数超过 6，OBS 音轨不够分，这一批无声）" : "关（各格音源已静音，切分出来的视频是无声的）");
			}
			Log.Write("并行：游戏声音进录像 = " + text8);
			if (!flag)
			{
				SetInputMuteRaw("RepRecorder 游戏声音", muted: true);
				Log.Write("并行：已把「RepRecorder 游戏声音」也静音，保证这批确实是无声的");
			}
			titleWatch = true;
			Thread thread = new Thread(() =>
			{
				while (titleWatch && !cancel)
				{
					for (int n = 0; n < handles2.Count; n++)
					{
						if (!(handles2[n] == IntPtr.Zero))
						{
							try
							{
								StringBuilder stringBuilder3 = new StringBuilder(512);
								GetWindowText(handles2[n], stringBuilder3, 512);
								string text16 = "th123 #" + (n + 1);
								if (stringBuilder3.ToString() != text16)
								{
									SetWindowText(handles2[n], text16);
								}
							}
							catch
							{
							}
						}
					}
					Thread.Sleep(1000);
				}
			});
			thread.IsBackground = true;
			thread.Start();
			Thread.Sleep(6000);
			for (int i = 0; i < list2.Count && i < list3.Count; i++)
			{
				if (list2[i] == null || handles2[i] == IntPtr.Zero)
				{
					continue;
				}
				try
				{
					StringBuilder stringBuilder2 = new StringBuilder(512);
					GetWindowText(handles2[i], stringBuilder2, 512);
					string text9 = stringBuilder2.ToString();
					if (text9.Length == 0 || text9.IndexOf("th123 #", StringComparison.Ordinal) < 0)
					{
						text9 = "th123 #" + (i + 1);
					}
					string text10 = MakeWindowString(text9, "th123_110a");
					ApplyCellWindow(dictionary, list3[i], list4[i], text10);
					if (batch.Count <= 6)
					{
						SetAudioTracksRaw(list4[i], i + 1);
					}
					SetInputMuteRaw(list4[i], !flag);
				}
				catch
				{
				}
			}
			Log.Write("并行：开播后已再刷新一次采集绑定（音频按各格自己的窗口重新解析）");
			for (int i = 0; i < list3.Count && i < list2.Count; i++)
			{
				try
				{
					if (list2[i] == null || list2[i].HasExited)
					{
						continue;
					}
					goto IL_18a4;
				}
				catch
				{
				}
				continue;
				IL_18a4:
				if (SourceLooksStatic(list3[i]))
				{
					Log.Write("并行：第 " + (i + 1) + " 格画面看起来还是静止的（可能在读盘/开场，不影响整场录制）");
				}
			}
			for (int num8 = 0; num8 < 6; num8++)
			{
				Thread.Sleep(3000);
				bool flag6 = false;
				for (int i = 0; i < list3.Count && i < list2.Count; i++)
				{
					try
					{
						if (list2[i] == null || list2[i].HasExited || !IsBlank(ShotOf(list3[i])))
						{
							continue;
						}
						flag6 = true;
						string title = "th123 #" + (i + 1);
						if (handles2[i] != IntPtr.Zero)
						{
							try
							{
								SetWindowText(handles2[i], title);
							}
							catch
							{
							}
						}
						dictionary.Remove(list3[i]);
						ApplyCellWindow(dictionary, list3[i], list4[i], MakeWindowString(title, "th123_110a"));
						if (batch.Count <= 6)
						{
							SetAudioTracksRaw(list4[i], i + 1);
						}
						Log.Write("并行：第 " + (i + 1) + " 格画面是黑的 → 已重新绑定窗口（第 " + (num8 + 1) + " 次检查）");
					}
					catch
					{
					}
				}
				if (!flag6)
				{
					break;
				}
			}
			for (int i = 0; i < list3.Count && i < list2.Count; i++)
			{
				try
				{
					if (list2[i] != null && !list2[i].HasExited && IsBlank(ShotOf(list3[i])))
					{
						Log.Write("并行：⚠ 第 " + (i + 1) + " 格开播后一直是黑屏（重新绑定也没救回来）——这一格很可能没录上，建议停止后单独重录这一条");
					}
				}
				catch
				{
				}
			}
			DateTime now2 = DateTime.Now;
			bool flag7 = false;
			while (!cancel)
			{
				Thread.Sleep(1000);
				bool flag8 = true;
				for (int i = 0; i < list2.Count; i++)
				{
					try
					{
						if (list2[i] == null || list2[i].HasExited)
						{
							if (array != null && i < array.Length && array[i] <= 0.0 && minValue != DateTime.MinValue)
							{
								array[i] = (DateTime.Now - minValue).TotalSeconds;
								if (array[i] > 10.0)
								{
									Log.Write("并行：第 " + (i + 1) + " 格对局结束（录了 " + array[i].ToString("0") + " 秒），切分时按这个长度裁掉后面多余的部分");
								}
							}
						}
						else
						{
							flag8 = false;
						}
					}
					catch
					{
					}
				}
				if (minValue != DateTime.MinValue && array2 != null && list3.Count > 0)
				{
					for (int i = 0; i < list2.Count && i < list3.Count; i++)
					{
						try
						{
							if (list2[i] == null || list2[i].HasExited)
							{
								continue;
							}
							double totalSeconds = (DateTime.Now - minValue).TotalSeconds;
							if (!IsBlank(ShotOf(list3[i])))
							{
								if (array2[i] <= 0.0)
								{
									array2[i] = totalSeconds;
								}
								array3[i] = totalSeconds;
							}
						}
						catch
						{
						}
					}
				}
				if (flag8)
				{
					break;
				}
				if (cfg.KeepGameCapturable)
				{
					for (int i = 0; i < list2.Count; i++)
					{
						try
						{
							if (list2[i] != null && !list2[i].HasExited)
							{
								list2[i].Refresh();
								IntPtr mainWindowHandle = list2[i].MainWindowHandle;
								if (!(mainWindowHandle == IntPtr.Zero) && IsIconic(mainWindowHandle))
								{
									ShowWindow(mainWindowHandle, 9);
									minimizedFixCount++;
									Log.Write("并行：检测到第 " + (i + 1) + " 格游戏窗口被最小化 —— 最小化后 Windows 不再合成画面、那一格会变黑；已自动把窗口还原（第 " + minimizedFixCount + " 次）。录制期间请用别的窗口【盖住】它，不要最小化。");
								}
							}
						}
						catch
						{
						}
					}
				}
				if ((DateTime.Now - now2).TotalMinutes > (double)cfg.MaxMinutes)
				{
					flag7 = true;
					break;
				}
				TimeSpan timeSpan = DateTime.Now - now2;
				string detail = "并行中 " + (int)timeSpan.TotalMinutes + ":" + timeSpan.Seconds.ToString("00");
				foreach (Job item5 in batch)
				{
					SetJob(item5, "录制中", detail);
				}
			}
			titleWatch = false;
			if (flag7)
			{
				Log.Write("并行录制超过 " + cfg.MaxMinutes + " 分钟上限，强制结束游戏");
				foreach (Process item6 in list2)
				{
					try
					{
						if (item6 != null && !item6.HasExited)
						{
							item6.Kill();
						}
					}
					catch
					{
					}
				}
			}
			Thread.Sleep(2000);
			try
			{
				Dictionary<string, object> dictionary14 = obs.Request("StopRecord", null);
				if (dictionary14.ContainsKey("outputPath"))
				{
					gridFile = Convert.ToString(dictionary14["outputPath"]);
				}
			}
			catch (Exception ex)
			{
				Log.Write("并行：停止录像失败 " + ex.Message);
			}
			if (string.IsNullOrEmpty(gridFile) || !File.Exists(gridFile))
			{
				try
				{
					string text11 = null;
					try
					{
						text11 = Convert.ToString(obs.Request("GetRecordDirectory", null)["recordDirectory"]);
					}
					catch
					{
					}
					if (string.IsNullOrEmpty(text11) || !Directory.Exists(text11))
					{
						text11 = Path.GetDirectoryName(batch[0].Rep);
					}
					if (!string.IsNullOrEmpty(text11) && Directory.Exists(text11))
					{
						FileInfo fileInfo = null;
						string[] array5 = Directory.GetFiles(text11);
						foreach (string text12 in array5)
						{
							string text13 = Path.GetExtension(text12).ToLower();
							if (!(text13 != ".mp4") || !(text13 != ".mkv") || !(text13 != ".flv") || !(text13 != ".mov") || !(text13 != ".ts"))
							{
								FileInfo fileInfo2 = new FileInfo(text12);
								if ((!(minValue != DateTime.MinValue) || !(fileInfo2.LastWriteTime < minValue.AddSeconds(-30.0))) && fileInfo2.Length >= 1048576 && (fileInfo == null || fileInfo2.LastWriteTime > fileInfo.LastWriteTime))
								{
									fileInfo = fileInfo2;
								}
							}
						}
						if (fileInfo != null)
						{
							gridFile = fileInfo.FullName;
							Log.Write("并行：StopRecord 没给出文件，但我在录像目录里找到了刚录的：" + fileInfo.Name + "（" + ((double)fileInfo.Length / 1048576.0).ToString("0.0") + " MB）");
						}
						else
						{
							Log.Write("并行：录像目录里也找不到刚录出来的文件：" + text11 + "（很可能 OBS 根本没在录 —— 请看上面有没有「OBS 没有真正开始录像」那行）");
						}
					}
				}
				catch (Exception ex2)
				{
					Log.Write("并行：找合并文件失败：" + ex2.Message);
				}
			}
			Log.Write("并行录制结束，合并文件：" + gridFile);
		}
		catch (Exception ex)
		{
			Log.Write("并行录制出错：" + ex.Message);
		}
		finally
		{
			try
			{
				if (num3 > 0)
				{
					Dictionary<string, object> dictionary15 = new Dictionary<string, object>();
					dictionary15["baseWidth"] = num3;
					dictionary15["baseHeight"] = num4;
					dictionary15["outputWidth"] = num3;
					dictionary15["outputHeight"] = num4;
					dictionary15["fpsNumerator"] = ((num5 > 0) ? num5 : 60);
					dictionary15["fpsDenominator"] = 1;
					obs.Request("SetVideoSettings", dictionary15);
					Log.Write("并行：已还原 OBS 画布 " + num3 + "×" + num4);
				}
			}
			catch
			{
			}
			try
			{
				SetProfileParamRaw("SimpleOutput", "RecTracks", string.IsNullOrEmpty(text) ? "1" : text);
			}
			catch
			{
			}
			foreach (string item7 in list3)
			{
				try
				{
					obs.Request("RemoveInput", new Dictionary<string, object> { { "inputName", item7 } });
				}
				catch
				{
				}
			}
			foreach (string item8 in list4)
			{
				try
				{
					obs.Request("RemoveInput", new Dictionary<string, object> { { "inputName", item8 } });
				}
				catch
				{
				}
			}
			try
			{
				ApplyAudioRouting(beforeRecording: false);
			}
			catch
			{
			}
			try
			{
				ApplyGameAudioSetting();
			}
			catch
			{
			}
			if (flag2 && GameAudio.SetSystemMute(mute: false, out var message))
			{
				flag2 = false;
				Log.Write("并行：已恢复系统声音（" + message + "）");
				MarkSystemMuted(muted: false);
			}
			try
			{
				if (num >= 0)
				{
					SetSceneItemEnabled("RepRecorder 录制场景", num, enabled);
				}
			}
			catch
			{
			}
			try
			{
				if (cfg.UseDedicatedScene && obs.IsConnected)
				{
					SwitchScene(toOurs: false);
				}
			}
			catch
			{
			}
			try
			{
				RestoreRecordDirectory();
			}
			catch
			{
			}
			foreach (Process item9 in list)
			{
				try
				{
					if (!item9.HasExited)
					{
						item9.Kill();
					}
				}
				catch
				{
				}
			}
			foreach (Process item10 in list2)
			{
				try
				{
					if (item10 != null && !item10.HasExited)
					{
						item10.Kill();
					}
				}
				catch
				{
				}
			}
		}
		if (gridFile != null && File.Exists(gridFile))
		{
			int okCount = 0;
			double[] heads = new double[batch.Count];
			double[] ends = new double[batch.Count];
			for (int i = 0; i < batch.Count; i++)
			{
				double num9 = 0.0;
				try
				{
					double num10 = AnalyzeCellHead(gridFile, i % cols * CW, i / cols * CH, CW, CH);
					if (num10 > 0.2)
					{
						num9 = num10 - 0.15;
					}
					Log.Write("并行切分（第 " + (i + 1) + " 格）：画面从第 " + num10.ToString("0.0") + " 秒开始 → 开头剪掉 " + num9.ToString("0.0") + " 秒");
				}
				catch
				{
				}
				double num11 = ((array != null && i < array.Length) ? array[i] : 0.0);
				if (array3 != null && i < array3.Length && array3[i] > 0.0)
				{
					double num12 = array3[i] + 0.25;
					if (num11 <= 0.0 || num12 < num11)
					{
						num11 = num12;
					}
				}
				if (num9 > 0.0 && num11 > 0.0)
				{
					Log.Write("并行切分（第 " + (i + 1) + " 格）：保留 " + num9.ToString("0.0") + " ~ " + num11.ToString("0.0") + " 秒（共 " + (num11 - num9).ToString("0.0") + " 秒）");
				}
				if (num11 > 0.0 && num11 - num9 < 5.0)
				{
					num11 = 0.0;
				}
				heads[i] = num9;
				ends[i] = num11;
			}
			int num13 = cfg.SplitParallel;
			if (num13 < 1)
			{
				num13 = 1;
			}
			if (num13 > 6)
			{
				num13 = 6;
			}
			if (num13 > batch.Count)
			{
				num13 = batch.Count;
			}
			if (batch.Count > 1)
			{
				Log.Write("并行切分：同时跑 " + num13 + " 个 ffmpeg（共 " + batch.Count + " 格）");
			}
			object splitGate = new object();
			int nextCell = 0;
			List<Thread> list8 = new List<Thread>();
			for (int num14 = 0; num14 < num13; num14++)
			{
				Thread thread2 = new Thread(() =>
				{
					object obj39 = default;
					while (true)
					{
						bool flag9 = true;
						bool lockTaken = false;
						int num15;
						try
						{
							Monitor.Enter(obj39 = splitGate, ref lockTaken);
							num15 = nextCell;
							nextCell++;
						}
						finally
						{
							if (lockTaken)
							{
								Monitor.Exit(obj39);
							}
						}
						if (num15 >= batch.Count)
						{
							break;
						}
						if (SplitOne(gridFile, batch[num15], num15, CW, CH, num15 % cols * CW, num15 / cols * CH, heads[num15], ends[num15], out var err))
						{
							batch[num15].Output = Path.ChangeExtension(batch[num15].Rep, ".mp4");
							long num16 = 0L;
							try
							{
								num16 = new FileInfo(batch[num15].Output).Length;
							}
							catch
							{
							}
							batch[num15].Size = num16;
							batch[num15].VideoSeconds = Mp4Duration(batch[num15].Output);
							batch[num15].Ended = DateTime.Now;
							SetJob(batch[num15], "完成", ((double)num16 / 1048576.0).ToString("0.0") + " MB（并行切分）");
							Log.Write("并行切分完成（第 " + (num15 + 1) + " 格）：" + Path.GetFileName(batch[num15].Output) + "  " + ((double)num16 / 1048576.0).ToString("0.0") + " MB  视频时长 " + FmtDur(batch[num15].VideoSeconds));
							bool lockTaken2 = false;
							try
							{
								Monitor.Enter(obj39 = splitGate, ref lockTaken2);
								okCount++;
							}
							finally
							{
								if (lockTaken2)
								{
									Monitor.Exit(obj39);
								}
							}
						}
						else
						{
							SetJob(batch[num15], "失败", "切分失败");
							Log.Write("并行切分失败（第 " + (num15 + 1) + " 格）：" + err + "；合并文件保留在 " + gridFile);
						}
					}
				});
				thread2.IsBackground = true;
				thread2.Start();
				list8.Add(thread2);
			}
			foreach (Thread item11 in list8)
			{
				try
				{
					item11.Join();
				}
				catch
				{
				}
			}
			if (okCount == batch.Count)
			{
				try
				{
					File.Delete(gridFile);
					Log.Write("并行：已删除中间合并文件（切分全部成功）");
				}
				catch
				{
				}
			}
			else
			{
				Log.Write("并行：有切分失败，中间合并文件保留在 " + gridFile + "（可用 ffmpeg 手动裁）");
			}
			if (okCount > 0)
			{
				if (cfg.AskSaveLocation && !queueSaveDecided)
				{
					string text14 = AskSaveLocationDialog(batch[0], okCount);
					if (!string.IsNullOrEmpty(text14) && text14 != "keep")
					{
						foreach (Job item12 in batch)
						{
							if (item12.Output != null && File.Exists(item12.Output))
							{
								string text15 = MoveOutputTo(item12, text14);
								if (text15 != null)
								{
									item12.Output = text15;
									item12.Size = new FileInfo(text15).Length;
									SetJob(item12, "完成", "已放到 " + Path.GetDirectoryName(text15));
								}
							}
						}
					}
				}
				else if (!string.IsNullOrEmpty(queueSaveFolder))
				{
					foreach (Job item13 in batch)
					{
						if (item13.Output != null && File.Exists(item13.Output))
						{
							string text15 = MoveOutputTo(item13, queueSaveFolder);
							if (text15 != null)
							{
								item13.Output = text15;
								item13.Size = new FileInfo(text15).Length;
								SetJob(item13, "完成", "已放到 " + queueSaveFolder);
							}
						}
					}
				}
			}
		}
		else
		{
			Log.Write("并行录制：没有拿到合并视频文件，无法切分");
			foreach (Job item14 in batch)
			{
				SetJob(item14, "失败", "并行录制没拿到文件");
			}
		}
		Log.Write("==== 并行录制结束 ====");
	}

	private void OnListDoubleClick(object sender, EventArgs e)
	{
		if (list.SelectedItems.Count == 0)
		{
			return;
		}
		Job job = (Job)list.SelectedItems[0].Tag;
		try
		{
			string text = (File.Exists(job.Output) ? job.Output : job.Rep);
			Process.Start("explorer.exe", "/select,\"" + text + "\"");
		}
		catch
		{
		}
	}

	private void OnFormClosing(object sender, FormClosingEventArgs e)
	{
		cancel = true;
		if (!closing && worker != null && worker.IsAlive)
		{
			e.Cancel = true;
			closing = true;
			SetStatusText("正在停止录制并收尾…");
			Log.Write("关闭窗口：先停止录制并收尾…");
			Thread thread = new Thread(() =>
			{
				try
				{
					worker.Join(20000);
				}
				catch
				{
				}
				Ui(() =>
				{
					Close();
				});
			});
			thread.IsBackground = true;
			thread.Start();
		}
		else
		{
			cfg.Save();
		}
	}
}
public static class Program
{
	[STAThread]
	public static void Main(string[] args)
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		if (args != null && args.Length >= 1 && args[0] == "--stop-record")
		{
			StopRecordingCli();
			return;
		}
		if (args != null && args.Length >= 2 && args[0] == "--set-record-dir")
		{
			SetRecordDirCli(args[1]);
			return;
		}
		if (args != null && args.Length >= 2 && args[0] == "--mic")
		{
			MicCli(args[1]);
			return;
		}
		if (args != null && args.Length >= 2 && args[0] == "--game-sound")
		{
			GameSoundCli(args[1]);
			return;
		}
		if (args != null && args.Length >= 2 && args[0] == "--audio-meter")
		{
			AudioMeterCli(args[1]);
			return;
		}
		if (args != null && args.Length >= 2 && args[0] == "--sys-mute")
		{
			SysMuteCli(args[1]);
			return;
		}
		if (args != null && args.Length >= 2 && args[0] == "--obs-req")
		{
			ObsReqCli(args[1]);
			return;
		}
		try
		{
			Application.Run(new MainForm(args));
		}
		catch (Exception ex)
		{
			Log.Write("程序崩溃：" + ex.ToString());
			MessageBox.Show(ex.Message, "RepRecorder 出错", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private static void StopRecordingCli()
	{
		try
		{
			Settings settings = Settings.Load();
			using ObsClient obsClient = new ObsClient();
			obsClient.Connect(settings.Port, settings.Password);
			Dictionary<string, object> dictionary = obsClient.Request("GetRecordStatus", null);
			if (!dictionary.ContainsKey("outputActive") || dictionary["outputActive"] == null || !Convert.ToBoolean(dictionary["outputActive"]))
			{
				Log.Write("--stop-record：OBS 当前没有在录像");
				return;
			}
			Dictionary<string, object> dictionary2 = obsClient.Request("StopRecord", null);
			string text = ((dictionary2.ContainsKey("outputPath") && dictionary2["outputPath"] != null) ? Convert.ToString(dictionary2["outputPath"]) : "");
			Log.Write("--stop-record：已停止录像" + ((text.Length > 0) ? ("，文件：" + text) : ""));
		}
		catch (Exception ex)
		{
			Log.Write("--stop-record 失败：" + ex.Message);
		}
	}

	private static void SetRecordDirCli(string dir)
	{
		try
		{
			Settings settings = Settings.Load();
			using ObsClient obsClient = new ObsClient();
			obsClient.Connect(settings.Port, settings.Password);
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			dictionary["recordDirectory"] = dir;
			obsClient.Request("SetRecordDirectory", dictionary);
			Log.Write("--set-record-dir：OBS 录像目录已设为 " + dir);
		}
		catch (Exception ex)
		{
			Log.Write("--set-record-dir 失败：" + ex.Message);
		}
	}

	public static List<string> FindGlobalAudioNames(bool micOnly)
	{
		List<string> list = new List<string>();
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio\\basic\\scenes");
			if (!Directory.Exists(path))
			{
				return list;
			}
			List<string> list2 = new List<string>(Directory.GetFiles(path, "*.json"));
			list2.Sort((string a, string b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
			foreach (string item in list2)
			{
				try
				{
					Dictionary<string, object> dictionary = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(item, Encoding.UTF8));
					if (dictionary == null)
					{
						continue;
					}
					foreach (KeyValuePair<string, object> item2 in dictionary)
					{
						bool flag = item2.Key.StartsWith("AuxAudioDevice");
						bool flag2 = item2.Key.StartsWith("DesktopAudioDevice");
						if ((flag || (flag2 && !micOnly)) && item2.Value is Dictionary<string, object> dictionary2 && dictionary2.ContainsKey("name") && dictionary2["name"] != null)
						{
							string text = Convert.ToString(dictionary2["name"]);
							if (text.Length > 0 && !list.Contains(text))
							{
								list.Add(text);
							}
						}
					}
					if (list.Count <= 0)
					{
						continue;
					}
					break;
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		return list;
	}

	public static List<string> FindMicTargets(ObsClient obs)
	{
		List<string> list = new List<string>();
		try
		{
			foreach (object item2 in ObsClient.ToList(obs.Request("GetInputList", null)["inputs"]))
			{
				Dictionary<string, object> dictionary = (Dictionary<string, object>)item2;
				string text = Convert.ToString(dictionary["unversionedInputKind"]);
				if (!(text != "wasapi_input_capture") || !(text != "coreaudio_input_capture"))
				{
					string item = Convert.ToString(dictionary["inputName"]);
					if (!list.Contains(item))
					{
						list.Add(item);
					}
				}
			}
		}
		catch
		{
		}
		foreach (string item3 in FindGlobalAudioNames(micOnly: true))
		{
			if (!list.Contains(item3))
			{
				list.Add(item3);
			}
		}
		return list;
	}

	public static List<string> FindDesktopTargets(ObsClient obs)
	{
		List<string> list = new List<string>();
		try
		{
			foreach (object item2 in ObsClient.ToList(obs.Request("GetInputList", null)["inputs"]))
			{
				Dictionary<string, object> dictionary = (Dictionary<string, object>)item2;
				string text = Convert.ToString(dictionary["unversionedInputKind"]);
				if (!(text != "wasapi_output_capture") || !(text != "coreaudio_output_capture"))
				{
					string item = Convert.ToString(dictionary["inputName"]);
					if (!list.Contains(item))
					{
						list.Add(item);
					}
				}
			}
		}
		catch
		{
		}
		List<string> list2 = FindGlobalAudioNames(micOnly: true);
		foreach (string item3 in FindGlobalAudioNames(micOnly: false))
		{
			if (!list2.Contains(item3) && !list.Contains(item3))
			{
				list.Add(item3);
			}
		}
		return list;
	}

	private static void AudioMeterCli(string secArg)
	{
		int result = 15;
		int.TryParse(secArg, out result);
		if (result <= 0)
		{
			result = 15;
		}
		try
		{
			Settings settings = Settings.Load();
			using ObsClient obsClient = new ObsClient();
			obsClient.MetersEnabled = true;
			obsClient.Connect(settings.Port, settings.Password);
			DateTime dateTime = DateTime.Now.AddSeconds(result);
			int num = 0;
			while (DateTime.Now < dateTime)
			{
				Thread.Sleep(1000);
				num++;
				if (!obsClient.MetersFresh)
				{
					Log.Write("--audio-meter #" + num + "：还没收到电平事件");
					continue;
				}
				string text = "";
				foreach (string item in obsClient.LevelNames())
				{
					string text2 = text;
					text = text2 + ((text.Length > 0) ? "   |   " : "") + item + " = " + obsClient.GetLevel(item).ToString("0.000");
				}
				Log.Write("--audio-meter #" + num + "：" + ((text.Length == 0) ? "<没有活动的音频源>" : text));
			}
		}
		catch (Exception ex)
		{
			Log.Write("--audio-meter 失败：" + ex.Message);
		}
	}

	private static void SysMuteCli(string mode)
	{
		try
		{
			string message;
			if (mode == "on")
			{
				Log.Write("--sys-mute on：" + (GameAudio.SetSystemMute(mute: true, out message) ? "OK " : "失败 ") + message);
				return;
			}
			if (mode == "off")
			{
				Log.Write("--sys-mute off：" + (GameAudio.SetSystemMute(mute: false, out message) ? "OK " : "失败 ") + message);
				return;
			}
			GameAudio.GetSystemMute(out var muted, out message);
			Log.Write("--sys-mute test：默认输出设备" + (muted ? "已静音" : "未静音") + "（" + message + "）");
		}
		catch (Exception ex)
		{
			Log.Write("--sys-mute 失败：" + ex.Message);
		}
	}

	private static void ObsReqCli(string type)
	{
		try
		{
			Settings settings = Settings.Load();
			using ObsClient obsClient = new ObsClient();
			obsClient.Connect(settings.Port, settings.Password);
			Dictionary<string, object> obj = obsClient.Request(type, null);
			Log.Write("--obs-req " + type + " → " + new JavaScriptSerializer().Serialize(obj));
		}
		catch (Exception ex)
		{
			Log.Write("--obs-req " + type + " 失败：" + ex.Message);
		}
	}

	private static void GameSoundCli(string mode)
	{
		try
		{
			bool flag = mode == "off" || mode == "mute" || mode == "静音";
			Process[] processesByName = Process.GetProcessesByName("th123");
			if (processesByName.Length == 0)
			{
				Log.Write("--game-sound：游戏没在运行");
				return;
			}
			Process[] array = processesByName;
			foreach (Process process in array)
			{
				bool flag2 = GameAudio.SetProcessMute(process.Id, flag, out var message);
				Log.Write("--game-sound：" + (flag ? "静音" : "打开") + " 游戏声音 PID " + process.Id + " → " + message + (flag2 ? "  OK" : "  失败"));
			}
		}
		catch (Exception ex)
		{
			Log.Write("--game-sound 失败：" + ex.Message);
		}
	}

	private static void MicCli(string mode)
	{
		try
		{
			Settings settings = Settings.Load();
			using ObsClient obsClient = new ObsClient();
			obsClient.Connect(settings.Port, settings.Password);
			List<string> list = FindGlobalAudioNames(micOnly: false);
			Log.Write("--mic " + mode + "：JSON 里的全局音频设备 = " + ((list.Count == 0) ? "<无>" : string.Join(" / ", list.ToArray())));
			List<string> list2 = FindMicTargets(obsClient);
			Log.Write("--mic " + mode + "：麦克风候选 = " + ((list2.Count == 0) ? "<无>" : string.Join(" / ", list2.ToArray())));
			if (list2.Count == 0)
			{
				return;
			}
			bool flag = mode == "on";
			foreach (string item in list2)
			{
				try
				{
					Dictionary<string, object> dictionary = new Dictionary<string, object>();
					dictionary["inputName"] = item;
					bool flag2 = Convert.ToBoolean(obsClient.Request("GetInputMute", dictionary)["inputMuted"]);
					Log.Write("  " + item + "：当前" + (flag2 ? "已静音" : "未静音"));
					if (!(mode == "test"))
					{
						if (flag2 != flag)
						{
							Dictionary<string, object> dictionary2 = new Dictionary<string, object>();
							dictionary2["inputName"] = item;
							dictionary2["inputMuted"] = flag;
							obsClient.Request("SetInputMute", dictionary2);
						}
						bool flag3 = Convert.ToBoolean(obsClient.Request("GetInputMute", dictionary)["inputMuted"]);
						Log.Write("  " + item + "：写回后" + (flag3 ? "已静音" : "未静音") + ((flag3 == flag) ? "  OK" : "  没生效"));
					}
				}
				catch (Exception ex)
				{
					Log.Write("  " + item + "：失败 —— " + ex.Message);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Write("--mic 失败：" + ex.Message);
		}
	}
}
public static class GameAudio
{
	public static bool SetSystemMute(bool mute, out string message)
	{
		message = "";
		IMMDeviceEnumerator iMMDeviceEnumerator = null;
		IMMDevice endpoint = null;
		object ppInterface = null;
		try
		{
			iMMDeviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
			int defaultAudioEndpoint = iMMDeviceEnumerator.GetDefaultAudioEndpoint(0, 1, out endpoint);
			if (defaultAudioEndpoint != 0)
			{
				message = "取默认输出设备失败 hr=" + defaultAudioEndpoint;
				return false;
			}
			Guid iid = typeof(IAudioEndpointVolume).GUID;
			defaultAudioEndpoint = endpoint.Activate(ref iid, 23, IntPtr.Zero, out ppInterface);
			if (defaultAudioEndpoint != 0)
			{
				message = "Activate 失败 hr=" + defaultAudioEndpoint;
				return false;
			}
			IAudioEndpointVolume audioEndpointVolume = (IAudioEndpointVolume)ppInterface;
			Guid eventContext = Guid.Empty;
			defaultAudioEndpoint = audioEndpointVolume.SetMute(mute ? 1 : 0, ref eventContext);
			if (defaultAudioEndpoint != 0)
			{
				message = "SetMute 失败 hr=" + defaultAudioEndpoint;
				return false;
			}
			audioEndpointVolume.GetMute(out var mute2);
			message = "默认输出设备现在" + ((mute2 != 0) ? "已静音" : "未静音");
			return mute2 != 0 == mute;
		}
		catch (Exception ex)
		{
			message = ex.Message;
			return false;
		}
		finally
		{
			if (ppInterface != null)
			{
				try
				{
					Marshal.ReleaseComObject(ppInterface);
				}
				catch
				{
				}
			}
			if (endpoint != null)
			{
				try
				{
					Marshal.ReleaseComObject(endpoint);
				}
				catch
				{
				}
			}
			if (iMMDeviceEnumerator != null)
			{
				try
				{
					Marshal.ReleaseComObject(iMMDeviceEnumerator);
				}
				catch
				{
				}
			}
		}
	}

	public static bool GetSystemMute(out bool muted, out string message)
	{
		muted = false;
		message = "";
		IMMDeviceEnumerator iMMDeviceEnumerator = null;
		IMMDevice endpoint = null;
		object ppInterface = null;
		try
		{
			iMMDeviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
			int defaultAudioEndpoint = iMMDeviceEnumerator.GetDefaultAudioEndpoint(0, 1, out endpoint);
			if (defaultAudioEndpoint != 0)
			{
				message = "取默认输出设备失败 hr=" + defaultAudioEndpoint;
				return false;
			}
			Guid iid = typeof(IAudioEndpointVolume).GUID;
			defaultAudioEndpoint = endpoint.Activate(ref iid, 23, IntPtr.Zero, out ppInterface);
			if (defaultAudioEndpoint != 0)
			{
				message = "Activate 失败 hr=" + defaultAudioEndpoint;
				return false;
			}
			IAudioEndpointVolume audioEndpointVolume = (IAudioEndpointVolume)ppInterface;
			defaultAudioEndpoint = audioEndpointVolume.GetMute(out var mute);
			if (defaultAudioEndpoint != 0)
			{
				message = "GetMute 失败 hr=" + defaultAudioEndpoint;
				return false;
			}
			muted = mute != 0;
			message = "读取成功";
			return true;
		}
		catch (Exception ex)
		{
			message = ex.Message;
			return false;
		}
		finally
		{
			if (ppInterface != null)
			{
				try
				{
					Marshal.ReleaseComObject(ppInterface);
				}
				catch
				{
				}
			}
			if (endpoint != null)
			{
				try
				{
					Marshal.ReleaseComObject(endpoint);
				}
				catch
				{
				}
			}
			if (iMMDeviceEnumerator != null)
			{
				try
				{
					Marshal.ReleaseComObject(iMMDeviceEnumerator);
				}
				catch
				{
				}
			}
		}
	}

	public static bool SetProcessMute(int pid, bool mute, out string message)
	{
		message = "";
		IMMDeviceEnumerator iMMDeviceEnumerator = null;
		IMMDevice endpoint = null;
		object ppInterface = null;
		IAudioSessionManager2 audioSessionManager = null;
		IAudioSessionEnumerator SessionEnum = null;
		try
		{
			iMMDeviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
			int defaultAudioEndpoint = iMMDeviceEnumerator.GetDefaultAudioEndpoint(0, 1, out endpoint);
			if (defaultAudioEndpoint != 0)
			{
				message = "取默认输出设备失败 hr=" + defaultAudioEndpoint;
				return false;
			}
			Guid iid = typeof(IAudioSessionManager2).GUID;
			defaultAudioEndpoint = endpoint.Activate(ref iid, 23, IntPtr.Zero, out ppInterface);
			if (defaultAudioEndpoint != 0)
			{
				message = "Activate 失败 hr=" + defaultAudioEndpoint;
				return false;
			}
			audioSessionManager = (IAudioSessionManager2)ppInterface;
			defaultAudioEndpoint = audioSessionManager.GetSessionEnumerator(out SessionEnum);
			if (defaultAudioEndpoint != 0)
			{
				message = "取音频会话列表失败 hr=" + defaultAudioEndpoint;
				return false;
			}
			int SessionCount = 0;
			SessionEnum.GetCount(out SessionCount);
			int num = 0;
			for (int i = 0; i < SessionCount; i++)
			{
				IAudioSessionControl Session = null;
				try
				{
					SessionEnum.GetSession(i, out Session);
					if (Session != null && Session is IAudioSessionControl2 audioSessionControl)
					{
						uint retVal = 0u;
						audioSessionControl.GetProcessId(out retVal);
						if (retVal == (uint)pid && Session is ISimpleAudioVolume simpleAudioVolume)
						{
							Guid eventContext = Guid.Empty;
							simpleAudioVolume.SetMute(mute, ref eventContext);
							num++;
						}
					}
				}
				catch
				{
				}
				finally
				{
					if (Session != null)
					{
						try
						{
							Marshal.ReleaseComObject(Session);
						}
						catch
						{
						}
					}
				}
			}
			message = ((num > 0) ? ("已" + (mute ? "静音" : "恢复") + " " + num + " 个音频会话") : "没有找到该进程的音频会话（游戏此时没有在发声？）");
			return num > 0;
		}
		catch (Exception ex)
		{
			message = ex.Message;
			return false;
		}
		finally
		{
			if (SessionEnum != null)
			{
				try
				{
					Marshal.ReleaseComObject(SessionEnum);
				}
				catch
				{
				}
			}
			if (audioSessionManager != null)
			{
				try
				{
					Marshal.ReleaseComObject(audioSessionManager);
				}
				catch
				{
				}
			}
			if (ppInterface != null && !object.ReferenceEquals(ppInterface, audioSessionManager))
			{
				try
				{
					Marshal.ReleaseComObject(ppInterface);
				}
				catch
				{
				}
			}
			if (endpoint != null)
			{
				try
				{
					Marshal.ReleaseComObject(endpoint);
				}
				catch
				{
				}
			}
			if (iMMDeviceEnumerator != null)
			{
				try
				{
					Marshal.ReleaseComObject(iMMDeviceEnumerator);
				}
				catch
				{
				}
			}
		}
	}
}
[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal interface IMMDeviceEnumerator
{
	int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr devices);

	int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);

	int GetDevice(string id, out IMMDevice device);

	int RegisterEndpointNotificationCallback(IntPtr client);

	int UnregisterEndpointNotificationCallback(IntPtr client);
}
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal interface IMMDevice
{
	int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

	int OpenPropertyStore(int stgmAccess, out IntPtr properties);

	int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

	int GetState(out int state);
}
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
internal interface IAudioSessionManager2
{
	int GetAudioSessionControl(ref Guid AudioSessionGuid, int StreamFlags, out IntPtr SessionControl);

	int GetSimpleAudioVolume(ref Guid AudioSessionGuid, int StreamFlags, out ISimpleAudioVolume AudioVolume);

	int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);

	int RegisterSessionNotification(IntPtr SessionNotification);

	int UnregisterSessionNotification(IntPtr SessionNotification);

	int RegisterDuckNotification(string sessionID, IntPtr duckNotification);

	int UnregisterDuckNotification(IntPtr duckNotification);
}
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
internal interface IAudioSessionEnumerator
{
	int GetCount(out int SessionCount);

	int GetSession(int SessionCount, out IAudioSessionControl Session);
}
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
internal interface IAudioSessionControl
{
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
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
internal interface IAudioSessionControl2
{
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
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
internal interface ISimpleAudioVolume
{
	int SetMasterVolume(float level, ref Guid eventContext);

	int GetMasterVolume(out float level);

	int SetMute(bool mute, ref Guid eventContext);

	int GetMute(out bool mute);
}
[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
	int RegisterControlChangeNotify(IntPtr notify);

	int UnregisterControlChangeNotify(IntPtr notify);

	int GetChannelCount(out int channelCount);

	int SetMasterVolumeLevel(float level, ref Guid eventContext);

	int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

	int GetMasterVolumeLevel(out float level);

	int GetMasterVolumeLevelScalar(out float level);

	int SetChannelVolumeLevel(uint channel, float level, ref Guid eventContext);

	int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

	int GetChannelVolumeLevel(uint channel, out float level);

	int GetChannelVolumeLevelScalar(uint channel, out float level);

	int SetMute(int mute, ref Guid eventContext);

	int GetMute(out int mute);

	int GetVolumeStepInfo(out uint step, out uint stepCount);

	int VolumeStepUp(ref Guid eventContext);

	int VolumeStepDown(ref Guid eventContext);

	int QueryHardwareSupport(out uint mask);

	int GetVolumeRange(out float min, out float max, out float increment);
}
