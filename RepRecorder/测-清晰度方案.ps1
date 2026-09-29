# 测试：游戏窗口放大到 1440x1080 后，画面是否依然清晰（点采样）
# 对比四种：①窗口原生 640x480 ②游戏自己放大到 1440x1080 ③640x480 用 neighbor 放大 ④640x480 用 lanczos 放大
$root='D:\【完整包】非想天则整合包2026-06-15'
$game="$root\【完整包】非想天则整合包2026-06-15\th123"
$dst="$root\RepRecorder\_看片"
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
$rep=(Get-ChildItem -LiteralPath "$game\replay" -Recurse -Force -File -Filter '*.rep' | Where-Object { $_.Length -ge 40KB } | Sort-Object Length | Select-Object -First 1).FullName
function Say($m){ Write-Host $m }
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @"
using System; using System.Runtime.InteropServices; using System.Drawing;
public class RS {
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
  public static string ResizeClient(IntPtr h, int cw, int ch){
    RECT c, w;
    if(!GetClientRect(h, out c) || !GetWindowRect(h, out w)) return "fail";
    int frameW = (w.R-w.L) - c.R, frameH = (w.B-w.T) - c.B;
    if(!SetWindowPos(h, IntPtr.Zero, w.L, w.T, cw + frameW, ch + frameH, 0x0004)) return "SetWindowPos fail";
    return "已把客户区改成 " + cw + "x" + ch;
  }
  public static string Shot(IntPtr h, string path){
    RECT c; if(!GetClientRect(h, out c)) return "fail";
    POINT p=new POINT(); p.X=0; p.Y=0; if(!ClientToScreen(h, ref p)) return "fail";
    if(c.R<=0||c.B<=0) return "bad";
    using(Bitmap b=new Bitmap(c.R,c.B)) using(Graphics g=Graphics.FromImage(b)){
      g.CopyFromScreen(p.X,p.Y,0,0,new System.Drawing.Size(c.R,c.B)); b.Save(path); }
    return "OK " + c.R + "x" + c.B;
  }
}
"@
function Sharp($path){
  # 锐度指标：水平相邻像素的平均绝对差（越大越"硬"）
  $bmp=[System.Drawing.Bitmap]::FromFile($path)
  try{
    $sum=0.0; $n=0
    for($y=200; $y -lt [Math]::Min($bmp.Height,900); $y+=3){
      for($x=1; $x -lt $bmp.Width; $x+=2){
        $a=$bmp.GetPixel($x,$y); $b=$bmp.GetPixel($x-1,$y)
        $sum += [Math]::Abs(($a.R+$a.G+$a.B)/3.0 - ($b.R+$b.G+$b.B)/3.0); $n++
      }
    }
    return [Math]::Round($sum/$n,2)
  } finally { $bmp.Dispose() }
}
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$p=Start-Process -FilePath "$game\th123.exe" -ArgumentList ('"'+$rep+'"') -WorkingDirectory $game -PassThru
Start-Sleep -Seconds 6
$h=$p.MainWindowHandle
for($i=0;$i -lt 20 -and $h -eq [IntPtr]::Zero;$i++){ Start-Sleep -Milliseconds 400; $p.Refresh(); $h=$p.MainWindowHandle }
[void][RS]::SetWindowPos($h,[IntPtr]::new(-1),30,30,0,0,0x0001 -bor 0x0040)
Start-Sleep -Seconds 22
Say ("A 原生 640x480 截图：" + [RS]::Shot($h, "$dst\清晰度-A-原生640x480.png"))
Say ("   " + [RS]::ResizeClient($h, 1440, 1080))
Start-Sleep -Seconds 4
Say ("B 放大窗口 1440x1080 截图：" + [RS]::Shot($h, "$dst\清晰度-B-游戏自己放大1440x1080.png"))
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
# C/D：把 A 用两种滤波放大到 1440x1080
& $ffmpeg -hide_banner -loglevel error -y -i "$dst\清晰度-A-原生640x480.png" -vf "scale=1440:1080:flags=neighbor" "$dst\清晰度-C-neighbor放大.png" 2>&1 | Out-Null
& $ffmpeg -hide_banner -loglevel error -y -i "$dst\清晰度-A-原生640x480.png" -vf "scale=1440:1080:flags=lanczos" "$dst\清晰度-D-lanczos放大.png" 2>&1 | Out-Null
Say ""
Say ("锐度对比（水平相邻像素平均差，越大越清晰）：")
foreach($n in @('清晰度-A-原生640x480.png','清晰度-B-游戏自己放大1440x1080.png','清晰度-C-neighbor放大.png','清晰度-D-lanczos放大.png')){
  $f=Join-Path $dst $n
  if(Test-Path -LiteralPath $f){ Say ("  {0,-42} {1}" -f $n, (Sharp $f)) } else { Say ("  $n 缺") }
}