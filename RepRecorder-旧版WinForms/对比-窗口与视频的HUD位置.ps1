# 对比：游戏窗口画面 vs 成品视频画面，看 HUD（血条）的水平位置是否一致
$root='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123'
$rep='D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123\replay\【联机对战Rep】\[chocothi]\26-09-23_21-56-47_chocothi(文)_Ciel(红美铃)_(2︰0_负).rep'
$dst='D:\【完整包】非想天则整合包2026-06-15\RepRecorder\_看片'
$ffmpeg='C:\Users\江博文\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe'
Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition @"
using System; using System.Runtime.InteropServices; using System.Drawing;
public class Grab2 {
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
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
# 1) 抓游戏窗口（挪到左上角，避免被别的窗口挡住）
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
$p=Start-Process -FilePath (Join-Path $root 'th123.exe') -ArgumentList ('"'+$rep+'"') -WorkingDirectory $root -PassThru
Start-Sleep -Seconds 6
$h=$p.MainWindowHandle
for($i=0;$i -lt 20 -and $h -eq [IntPtr]::Zero;$i++){ Start-Sleep -Milliseconds 400; $p.Refresh(); $h=$p.MainWindowHandle }
[void][Grab2]::SetWindowPos($h,[IntPtr]::new(-1),20,20,0,0,0x0001 -bor 0x0040 -bor 0x0004)
Start-Sleep -Seconds 14
"窗口截图: " + [Grab2]::Shot($h, "$dst\对比-游戏窗口640x480.png")
Start-Sleep -Seconds 10
"窗口截图(第二次): " + [Grab2]::Shot($h, "$dst\对比-游戏窗口640x480-b.png")
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
# 2) 抽视频帧并缩到 640x480 便于对比
$f='C:\Users\江博文\Desktop\26-09-23_21-56-47_chocothi(文)_Ciel(红美铃)_(2︰0_负).mp4'
& $ffmpeg -hide_banner -loglevel error -y -ss 30 -i "$f" -frames:v 1 -vf "scale=640:480" "$dst\对比-视频帧640x480.png" 2>&1 | Out-Null
# 3) 在"血条那一行"找第一段亮橙色的起点
function FirstBright($path, $row){
  $bmp=[System.Drawing.Bitmap]::FromFile($path)
  try{
    $res=@()
    for($y=$row-6; $y -le $row+6; $y++){
      for($x=0; $x -lt $bmp.Width; $x++){
        $c=$bmp.GetPixel($x,$y)
        if($c.R -gt 200 -and $c.G -gt 120 -and $c.B -lt 120){ $res += $x; break }
      }
    }
    if($res.Count -gt 0){ return ($res | Measure-Object -Minimum).Minimum } else { return -1 }
  } finally { $bmp.Dispose() }
}
"--- 在 y≈25~40（血条带）找最左边第一段亮橙色像素的 x 坐标 ---"
foreach($n in @('对比-游戏窗口640x480.png','对比-游戏窗口640x480-b.png','对比-视频帧640x480.png')){
  $p2=Join-Path $dst $n
  if(Test-Path -LiteralPath $p2){
    "  {0,-34} 血条最左 x = {1}" -f $n, (FirstBright $p2 32)
  }
}