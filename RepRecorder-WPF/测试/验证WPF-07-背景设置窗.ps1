# verify-WPF-07: the background settings dialog (phase 4's last piece)
#   opens from the top bar, edits live, writes through to cfg + the rendered layer,
#   and 取消 puts everything back.
# ASCII only. Run with: powershell -File <this>   (never pipe its stdout)
$here = $PSScriptRoot                  # this script lives in the tests folder
$dir  = Split-Path $here -Parent       # the app folder: the exe and its log/settings live there
$src  = Join-Path $dir 'src'   # source code folder
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$cfg = Join-Path $dir 'RepRecorder.settings.json'
$bak = Join-Path $here '_settings-backup.json'
$runLog = Join-Path $here '_run07.log'
$port = 7788
$fail = 0
Remove-Item $runLog -ErrorAction SilentlyContinue

# Needed by the resize check (see the "resizing must not eat the offset" block).
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W7 {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
[W7]::SetProcessDPIAware() | Out-Null

function Dbg([string]$cmd) {
  try {
    $c = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $port)
    $c.ReceiveTimeout = 4000
    $w = New-Object System.IO.StreamWriter($c.GetStream()); $w.AutoFlush = $true; $w.WriteLine($cmd)
    $r = New-Object System.IO.StreamReader($c.GetStream()); $out = $r.ReadLine(); $c.Close()
    return $out
  } catch { return $null }
}
function Check([string]$what, [bool]$ok, [string]$detail) {
  $line = $(if ($ok) { 'OK   ' } else { 'FAIL ' }) + $what + '  ' + $detail
  Write-Host $line
  Add-Content -LiteralPath $runLog -Value $line -Encoding UTF8
  if (-not $ok) { $script:fail = 1 }
}
function CfgVal([string]$key) {
  try { return (Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json).$key } catch { return $null }
}

Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 600
Copy-Item $cfg $bak -Force

# The test needs a background image to exercise, so don't depend on the user having one
# configured -- 2026-10-05 the user cleared theirs and this test went red for no reason.
# Point BgImage at the repo's own bg-test.png; $bak is restored at the end either way.
$imgPath = Join-Path $here 'bg-test.png'
$j = Get-Content $cfg -Raw -Encoding UTF8
$j = $j -replace '"BgImage":"[^"]*"', ('"BgImage":"' + $imgPath.Replace('\', '\\') + '"')
# 2026-10-07: the "free" alignment was merged into "cover" and deleted. A settings file
# still holding "free" must come back as "fill" -- otherwise the dropdown would show
# "cover" while cfg still said "free", and ComputePanRange has no branch for it any more.
# Forced here so the check below has something to bite on; restored from $bak at the end.
$j = $j -replace '"BgLayout":"[^"]*"', '"BgLayout":"free"'
[System.IO.File]::WriteAllText($cfg, $j, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('bg image forced to: ' + $imgPath)
Write-Host 'bg layout forced to: free   (must come back as fill)'

$opBefore = CfgVal 'BgOpacity'
$scopeBefore = CfgVal 'BgScope'
Write-Host ("before: BgOpacity=$opBefore BgScope=$scopeBefore")

Start-Sleep -Seconds 3      # 新 exe 先让 Defender 扫完（不然 Start-Process 会卡）
$p = Start-Process -FilePath $exe -ArgumentList @('--wpf', "--debug-port=$port") -PassThru
# 等端口起来（新 exe 要先被 Defender 扫一遍，固定 sleep 不够稳）
$up = $false
for ($i = 0; $i -lt 30; $i++) {
  Start-Sleep -Milliseconds 1000
  $probe = Dbg 'status'
  if ($probe -ne $null) { $up = $true; break }
  $p.Refresh(); if ($p.HasExited) { break }
}
Check 'app alive' ($up -and -not $p.HasExited) ('pid=' + $p.Id + ' waited=' + $i + 's')
$p.Refresh()
$h7 = $p.MainWindowHandle

$bg = Dbg 'bgstate' | ConvertFrom-Json
Check 'bgstate answers' ($bg -ne $null) ''
Check 'background image loaded' ($bg.image -eq $true) ('pixels=' + $bg.pixels)
# The settings file on disk says "free" (forced above). The app must have migrated it away.
Check 'a stored "free" layout migrates to "fill"' ($bg.layout -eq 'fill') ('layout=' + $bg.layout + ' (file said free)')
Check 'layer opacity matches settings' ([int]$bg.opacity -eq [int]$opBefore) ('layer=' + $bg.opacity + ' cfg=' + $opBefore)
$scrimBefore = [int]$bg.scrim
# Read the starting layout instead of hardcoding it: the user may have switched it
# (e.g. fill -> fit), and the old hardcoded 'UniformToFill' then failed for no reason.
$stretchBefore = $bg.stretch
$flipHBefore = $bg.flipH
$flipVBefore = $bg.flipV
$zoomBefore = [int]$bg.zoom

# --- open the dialog from the top bar ---
$r = Dbg 'press:btnBg' | ConvertFrom-Json
Start-Sleep -Milliseconds 800
Check 'press:btnBg ok' ($r.ok -eq $true) ('ok=' + $r.ok)
Check 'dialog is open' ((Dbg 'find:bgDialog' | ConvertFrom-Json).found -eq $true) ''
foreach ($id in @('bgImage','bgBrowse','bgClear','bgOpacity','panelScrim','bgBlur','bgOffX','bgOffY','bgOffXRow','bgOffYRow','bgLayout','bgScope','bgFlipH','bgFlipV','bgZoom','bgOk','bgCancel')) {
  $f = Dbg "find:$id" | ConvertFrom-Json
  Check ("control " + $id) ($f.found -eq $true) ('type=' + $f.type)
}

# --- live edit: transparency ---
# 2026-10-04: the slider now shows 透明度 (0 = image fully clear) while the stored
# setting stays BgOpacity (opacity). So slider 60 MUST land on the layer as 40.
$r = Dbg 'set:bgOpacity=60' | ConvertFrom-Json
Start-Sleep -Milliseconds 600
$bg = Dbg 'bgstate' | ConvertFrom-Json
Check 'transparency 60 lands on the layer as opacity 40' ([int]$bg.opacity -eq 40) ('layer=' + $bg.opacity)

# --- live edit: scope ---
$r = Dbg 'set:bgScope=1' | ConvertFrom-Json
Start-Sleep -Milliseconds 600
$bg = Dbg 'bgstate' | ConvertFrom-Json
Check 'scope switches to global' ($bg.scope -eq 'global') ('scope=' + $bg.scope)

# 2026-10-07: switching 内容区 <-> 全局 changes the background LAYER size (content area vs
# the whole window), so the brush viewport must be recomputed with it. It was not: the
# viewport stayed on the content-area size and the picture only covered a vertical band in
# the middle of the window (the user's screenshot). The size is now re-applied whenever the
# layer's own SizeChanged fires (UiWindow.BuildBackgroundLayer), so the two must always agree.
function ViewportMatchesRendered([object]$b, [string]$tag) {
  if ($b.viewport -eq '' -or $b.rendered -eq '') { Check ('viewport follows the scope change (' + $tag + ')') $false 'no image configured'; return }
  $vp = $b.viewport.Split(',')[2].Split('x')
  $rd = $b.rendered.Split('x')
  Check ('viewport follows the scope change (' + $tag + ')') `
        (([int]$vp[0] -eq [int]$rd[0]) -and ([int]$vp[1] -eq [int]$rd[1])) `
        ('viewport=' + $b.viewport + '  rendered=' + $b.rendered + '  layer=' + $b.layer)
}
ViewportMatchesRendered $bg 'global'
$r = Dbg 'set:bgScope=0' | ConvertFrom-Json
Start-Sleep -Milliseconds 700
$bg = Dbg 'bgstate' | ConvertFrom-Json
Check 'scope switches back to content' ($bg.scope -eq 'content') ('scope=' + $bg.scope)
ViewportMatchesRendered $bg 'content'
$r = Dbg 'set:bgScope=1' | Out-Null
Start-Sleep -Milliseconds 700

# --- live edit: layout ---
$r = Dbg 'set:bgLayout=2' | ConvertFrom-Json
Start-Sleep -Milliseconds 600
$bg = Dbg 'bgstate' | ConvertFrom-Json
Check 'layout switches to stretch' ($bg.stretch -eq 'Fill') ('stretch=' + $bg.stretch)
# Regression guard: the config value changing is NOT enough -- the brush on the layer
# must actually carry the new Stretch. 2026-10-04 the layout dropdown did nothing
# because ApplyBackground only rebuilt the brush when the image PATH changed, and this
# test only ever looked at the config-derived value, so it stayed green.
Check 'the layer brush really switched too' ($bg.brushStretch -eq $bg.stretch) ('brush=' + $bg.brushStretch + ' cfg=' + $bg.stretch)

# =====================================================================================
#  2026-10-07: 对齐方式 / 位置 / 翻转 -- 照搬 Wallpaper Engine 的三件套
#  「位置」不再固定 +-100：每种对齐方式自己决定图能挪多远 --
#    覆盖/适应/居中 只给裁边（或留边）真正留下的那点余量，滑到头图边正好贴住框边；
#    拉伸 给 0；只有平铺不限（它没有边）。
#  某根轴余量为 0 时，那一行在设置窗里【整行藏掉】-- 和 WE 的属性面板一样。
#  2026-10-07：「自由」已合并进「覆盖」并删除，下拉只剩 5 项（0..4）。
# =====================================================================================
function Lay([int]$i) { Dbg ("set:bgLayout=$i") | Out-Null; Start-Sleep -Milliseconds 700 }
function RowVis([string]$id) { return (Dbg "find:$id" | ConvertFrom-Json).vis }

$layName = @('cover', 'contain', 'stretch', 'center', 'tile')
$layKey  = @('fill', 'fit', 'stretch', 'center', 'tile')
foreach ($i in @(0, 1, 2, 3, 4)) {
  Lay $i
  $lb = Dbg 'bgstate' | ConvertFrom-Json
  # Pin the mode set itself: these five, in this order, and nothing else.
  # (before 2026-10-07 there was a 6th, "free", at index 5.)
  Check ('alignment index ' + $i + ' is ' + $layKey[$i]) ($lb.layout -eq $layKey[$i]) ('layout=' + $lb.layout)
  $rx = RowVis 'bgOffXRow'
  $ry = RowVis 'bgOffYRow'
  # The rule under test: limit exactly 0 ("nothing to move") <=> that slider must be LOCKED.
  # 2026-10-07: the row used to be COLLAPSED instead. But this dialog is SizeToContent=Height,
  # so hiding a row resized the dialog and shifted everything below it -- the slider being
  # dragged slid out from under the pointer and its value jumped (the user's "摇杆乱跳").
  # Rows are therefore ALWAYS visible now, and merely disabled when locked.
  $wantX = -not ([double]$lb.panx -eq 0)
  $wantY = -not ([double]$lb.pany -eq 0)
  $enX = (Dbg 'find:bgOffX' | ConvertFrom-Json).enabled
  $enY = (Dbg 'find:bgOffY' | ConvertFrom-Json).enabled
  Check ('slider locks when its axis has no slack (' + $layName[$i] + ')') `
        (($enX -eq $wantX) -and ($enY -eq $wantY)) `
        ('pan=' + $lb.panx + ',' + $lb.pany + '  enabled=' + $enX + ',' + $enY)
  Check ('position row never disappears (' + $layName[$i] + ')') `
        (($rx -eq 'Visible') -and ($ry -eq 'Visible')) `
        ('vis=' + $rx + ',' + $ry + '  (hiding it is what made the dialog jump)')
}

Lay 2
$lb = Dbg 'bgstate' | ConvertFrom-Json
Check 'stretch leaves no room to move at all' (([double]$lb.panx -eq 0) -and ([double]$lb.pany -eq 0)) ('pan=' + $lb.panx + ',' + $lb.pany)

Lay 4
$lb = Dbg 'bgstate' | ConvertFrom-Json
Check 'tile is unlimited on both axes' (([double]$lb.panx -eq -1) -and ([double]$lb.pany -eq -1)) ('pan=' + $lb.panx + ',' + $lb.pany)

# cover must fit one axis EXACTLY -- that is what makes cover "no background ever shows".
Lay 0
$lb = Dbg 'bgstate' | ConvertFrom-Json
Check 'cover fits exactly one axis (so it can never show background)' `
      ((([double]$lb.panx -eq 0) -and ([double]$lb.pany -gt 0)) -or (([double]$lb.pany -eq 0) -and ([double]$lb.panx -gt 0))) `
      ('pan=' + $lb.panx + ',' + $lb.pany)

# =====================================================================================
#  2026-10-07: 切换对齐方式 / 改窗口大小，都【不许改动存下来的位移】。
#
#  这里原本断言的是反过来的行为（"切到拉伸会把位移夹回 0"），而正是那个行为构成了
#  "伸缩软件时背景图闪几下、之后就停了" 的根因：量程随窗口大小变，所以每拖一下窗口
#  就把用户的位移夹掉一点，而且缩回去也不恢复，直到夹成 0 才安静。
#  存下来的位移是用户的意图，只该由用户拖滑块来改。
#  夹取仍然发生 —— 但只在渲染那一层，所以"滑到头正好贴边、永不露底"的承诺不受影响。
# =====================================================================================
Lay 4
Dbg 'set:bgOffX=99' | Out-Null
Dbg 'set:bgOffY=99' | Out-Null
Start-Sleep -Milliseconds 600
Lay 2
$lb = Dbg 'bgstate' | ConvertFrom-Json
Check 'switching to stretch does NOT rewrite the stored offset' (([int]$lb.ox -eq 99) -and ([int]$lb.oy -eq 99)) ('ox=' + $lb.ox + ' oy=' + $lb.oy + ' (kept, not clamped)')
Check 'stretch still renders it clamped (no blank band)' (([double]$lb.bx -eq 0) -and ([double]$lb.by -eq 0)) ('bx=' + $lb.bx + ' by=' + $lb.by)

# --- resizing the window must not eat the offset either ---
# Grow the window until the cover range shrinks below the parked offset, then shrink back.
Lay 0
Start-Sleep -Milliseconds 600
$lb = Dbg 'bgstate' | ConvertFrom-Json
$lim = [int]$lb.pany
Check 'cover has a positive vertical range to park inside' ($lim -gt 0) ('pany=' + $lim)
# Park exactly AT the limit.
# Two subtleties, both of them consequences of the fix under test:
#   - the slider refuses values outside its range, so parking past the limit proves nothing;
#   - the row refresh clamps the slider's DISPLAYED value without writing it back, so if the
#     slider already shows the limit, setting the limit again is a no-op and never reaches cfg.
# Zero it first so the change is real and the write actually happens.
Dbg 'set:bgOffY=0' | Out-Null
Start-Sleep -Milliseconds 500
Dbg ('set:bgOffY=' + $lim) | Out-Null
Start-Sleep -Milliseconds 600
$lb = Dbg 'bgstate' | ConvertFrom-Json
$parked = [int]$lb.oy
Check 'the offset parked at the limit took effect' (($parked -eq $lim) -and ($parked -gt 0)) ('oy=' + $parked + ' pany=' + $lim)

$rc0 = New-Object W7+RECT
[W7]::GetWindowRect($h7, [ref]$rc0) | Out-Null
$w0 = $rc0.R - $rc0.L; $h0 = $rc0.B - $rc0.T
[W7]::SetWindowPos($h7, [IntPtr]::Zero, $rc0.L, $rc0.T, $w0 + 260, $h0 + 200, 0x0004) | Out-Null
Start-Sleep -Milliseconds 900
$big = Dbg 'bgstate' | ConvertFrom-Json
Check 'growing the window does not rewrite the stored offset' ([int]$big.oy -eq $parked) ('oy=' + $big.oy + ' parked=' + $parked + ' pany now=' + $big.pany)
Check 'the render is clamped into the new (smaller) range' ([Math]::Abs([double]$big.by) -le [double]$big.pany + 0.01) ('by=' + $big.by + ' pany=' + $big.pany)
Check 'the range really did shrink (otherwise this test proves nothing)' ([int]$big.pany -lt $parked) ('pany=' + $big.pany + ' was ' + $parked)

[W7]::SetWindowPos($h7, [IntPtr]::Zero, $rc0.L, $rc0.T, $w0, $h0, 0x0004) | Out-Null
Start-Sleep -Milliseconds 900
$back = Dbg 'bgstate' | ConvertFrom-Json
Check 'shrinking back restores the original position' ([int]$back.oy -eq $parked) ('oy=' + $back.oy + ' parked=' + $parked)
Check 'and the rendered offset is the original one again' ([Math]::Abs([double]$back.by - 0.18) -lt 0.02) ('by=' + $back.by)

Lay 4
Dbg 'set:bgOffX=0' | Out-Null
Dbg 'set:bgOffY=0' | Out-Null
Start-Sleep -Milliseconds 400

# ---- 翻转 / 缩放：和位移正交的一层变换，必须真的落到画刷上 ----
# NOTE: do NOT hardcode "-1,1". The brush scale carries the zoom as well, and the zoom is
# whatever the user last left in their settings (the first run of this test went red on
# "-1.01,1.01" purely because their BgZoom was 101). Derive the expectation from the
# reported zoom instead.
Lay 4
Dbg 'set:bgFlipH=true' | Out-Null
Start-Sleep -Milliseconds 700
$lb = Dbg 'bgstate' | ConvertFrom-Json
$z = [double]$lb.zoom / 100.0
$sc = $lb.brushScale -split ','
$sx = [double]$sc[0]; $sy = [double]$sc[1]
Check 'flip horizontal reaches the brush' (($sx -lt 0) -and ([Math]::Abs([Math]::Abs($sx) - $z) -lt 0.01) -and ($sy -gt 0)) `
      ('brushScale=' + $lb.brushScale + '  zoom=' + $lb.zoom)
Check 'flip horizontal reaches the setting' ($lb.flipH -eq $true) ('flipH=' + $lb.flipH)

Dbg 'set:bgFlipV=true' | Out-Null
Start-Sleep -Milliseconds 700
$lb = Dbg 'bgstate' | ConvertFrom-Json
$sc = $lb.brushScale -split ','
Check 'flip vertical reaches the brush' (([double]$sc[0] -lt 0) -and ([double]$sc[1] -lt 0)) ('brushScale=' + $lb.brushScale)

# Zoom used to show up as a ScaleTransform on the brush. Since 2026-10-07 the displacement
# is done by moving the brush VIEWPORT and the brush is always Stretch.Fill, so zoom now
# shows up as the rendered size instead -- assert it there.
$rzBefore = [int]((Dbg 'bgstate' | ConvertFrom-Json).rendered.Split('x')[0])
Dbg 'set:bgZoom=200' | Out-Null
Start-Sleep -Milliseconds 700
$lb = Dbg 'bgstate' | ConvertFrom-Json
$rzAfter = [int]$lb.rendered.Split('x')[0]
Check 'zoom doubles the rendered size' ([Math]::Abs($rzAfter - 2 * $rzBefore) -le 2) ('rendered ' + $rzBefore + ' -> ' + $rzAfter + ' (zoom=' + $lb.zoom + ')')

# =====================================================================================
#  2026-10-07 回归护栏：滑动位移必须【露出被裁掉的那部分】，而不是空出一条底色。
#  这里以前是把位移做成画刷的 RelativeTransform —— UniformToFill 先把图裁到视口，
#  再把裁好的结果平移，空出来的地方不会补上。实测全屏下 Y 拉到 +12%，
#  顶部会出现一条 129 DIP 的窗口底色（用户报的"移动 Y 看不到被裁掉的部分"就是这个）。
#  现在位移做在**视口**上：图和视口一起走，没有"空出来的地方"。
#  判据：拉到 +max 时视口顶边必须贴住图层顶边（<=1），而且还要盖住图层底边（不留空）。
# =====================================================================================
Dbg 'set:bgZoom=100' | Out-Null
Start-Sleep -Milliseconds 600
Lay 0
Start-Sleep -Milliseconds 700
$lb = Dbg 'bgstate' | ConvertFrom-Json
$lim = [int]$lb.pany
$layH = [int]$lb.layer.Split('x')[1]
$rendH = [int]$lb.rendered.Split('x')[1]
Dbg 'set:bgOffY=0' | Out-Null
Start-Sleep -Milliseconds 400
Dbg ('set:bgOffY=' + $lim) | Out-Null
Start-Sleep -Milliseconds 700
$lb = Dbg 'bgstate' | ConvertFrom-Json
$vp = $lb.viewport.Split(',')
$vpx = [int]$vp[0]; $vpy = [int]$vp[1]
$vpsz = $vp[2].Split('x'); $vpw = [int]$vpsz[0]; $vph = [int]$vpsz[1]
Check 'Y=+max puts the image TOP edge at the frame top (crop revealed)' ($vpy -le 1) `
      ('viewport y=' + $vpy + '  rendered height=' + $vph + '  layer height=' + $layH)
Check 'and it still covers the bottom edge (no blank band opened)' (($vpy + $vph) -ge ($layH - 1)) `
      ('viewport bottom=' + ($vpy + $vph) + '  layer height=' + $layH)
Check 'the rendered image is taller than the frame (so there IS something cropped)' ($vph -gt $layH) `
      ('rendered=' + $rendH + '  layer=' + $layH)

Dbg 'set:bgOffY=0' | Out-Null
Start-Sleep -Milliseconds 400
Dbg ('set:bgOffY=-' + $lim) | Out-Null
Start-Sleep -Milliseconds 700
$lb = Dbg 'bgstate' | ConvertFrom-Json
$vp = $lb.viewport.Split(',')
$vpy = [int]$vp[1]
$vpsz = $vp[2].Split('x'); $vph = [int]$vpsz[1]
Check 'Y=-max puts the image BOTTOM edge at the frame bottom' ([Math]::Abs(($vpy + $vph) - $layH) -le 1) `
      ('viewport bottom=' + ($vpy + $vph) + '  layer height=' + $layH)
Check 'and it still covers the top edge' ($vpy -le 1) ('viewport y=' + $vpy)
Dbg 'set:bgOffY=0' | Out-Null
Start-Sleep -Milliseconds 400

# --- live edit: panel scrim (0 = panels fully transparent) ---
$r = Dbg 'set:panelScrim=0' | ConvertFrom-Json
Start-Sleep -Milliseconds 600
$bg = Dbg 'bgstate' | ConvertFrom-Json
Check 'panel scrim 0 reaches the setting' ([int]$bg.scrim -eq 0) ('scrim=' + $bg.scrim)

# --- 取消 must put everything back ---
$r = Dbg 'press:bgCancel' | ConvertFrom-Json
Start-Sleep -Milliseconds 800
$bg = Dbg 'bgstate' | ConvertFrom-Json
Check 'cancel restores opacity' ([int]$bg.opacity -eq [int]$opBefore) ('layer=' + $bg.opacity + ' before=' + $opBefore)
Check 'cancel restores scope' ($bg.scope -eq $scopeBefore) ('scope=' + $bg.scope + ' before=' + $scopeBefore)
Check 'cancel restores layout' ($bg.stretch -eq $stretchBefore) ('stretch=' + $bg.stretch + ' before=' + $stretchBefore)
Check 'cancel restores panel scrim' ([int]$bg.scrim -eq $scrimBefore) ('scrim=' + $bg.scrim + ' before=' + $scrimBefore)
Check 'cancel restores flip and zoom' (($bg.flipH -eq $flipHBefore) -and ($bg.flipV -eq $flipVBefore) -and ([int]$bg.zoom -eq $zoomBefore)) ('file=' + $bg.flipH + ',' + $bg.flipV + ' ' + $bg.zoom + '  before=' + $flipHBefore + ',' + $flipVBefore + ' ' + $zoomBefore)
Check 'settings file untouched by cancel' ([int](CfgVal 'BgOpacity') -eq [int]$opBefore) ('file=' + (CfgVal 'BgOpacity'))

# --- 确定 must persist ---
Dbg 'press:btnBg' | Out-Null
Start-Sleep -Milliseconds 800
Dbg 'set:bgOpacity=42' | Out-Null          # slider = transparency 42% -> stored opacity 58%
Start-Sleep -Milliseconds 400
Dbg 'press:bgOk' | Out-Null
Start-Sleep -Milliseconds 800
Check 'OK writes the settings file' ([int](CfgVal 'BgOpacity') -eq 58) ('file=' + (CfgVal 'BgOpacity'))
# The log line prints the transparency (what the slider shows), not the stored opacity.
Check 'log records the background change' ((Get-Content (Join-Path $dir 'RepRecorder.log') -Raw -Encoding UTF8) -match '=42%') ''
Check 'no exception in log' ((Get-Content (Join-Path $dir 'RepRecorder.log') -Raw -Encoding UTF8) -notmatch 'Exception') ''

# =====================================================================================
#  2026-10-07 回归护栏：不透明度 0 时 UI 线程不许空转。
#
#  BgOpacity = 0 会把背景图层设成 Collapsed，宽度**永远是 0**；而 ApplyBackground 里那段
#  "尺寸还没出来就补算一次"当时写成"入队 → 回调里清闸门 → 再调一次" —— 于是无限自我重排，
#  UI 线程被灌满。消息队列还在回控制消息，所以 Responding 仍是 True、也测不出"死锁"，
#  但**输入被饿死**：鼠标变漏斗、整个软件点不动（用户报的就是这个）。
#  判据只能看 CPU：把透明度拉满（= 不透明度 0）之后，进程的 CPU 增量必须很小。
#  修复前实测 ~21% of a core，修复后 0%。
# =====================================================================================
Dbg 'press:btnBg' | Out-Null
Start-Sleep -Milliseconds 1000
Dbg 'set:bgOpacity=100' | Out-Null      # 滑块是"透明度"，100 -> 存下来的 BgOpacity = 0
Start-Sleep -Milliseconds 1500
$bgNow = Dbg 'bgstate' | ConvertFrom-Json
$proc = Get-Process -Id $p.Id
$proc.Refresh(); $t0 = $proc.TotalProcessorTime.TotalSeconds
Start-Sleep -Seconds 5
$proc.Refresh(); $t1 = $proc.TotalProcessorTime.TotalSeconds
$burn = ($t1 - $t0) / 5 * 100
Check 'transparency 100 collapses the layer' ([int]$bgNow.opacity -eq 0) ('layer opacity=' + $bgNow.opacity)
Check 'opacity 0 does NOT spin the UI thread' ($burn -lt 8) `
      ('cpu=' + [Math]::Round($burn, 1) + '% of a core over 5s   (the self-rescheduling loop measured ~21%)')
Dbg 'press:bgCancel' | Out-Null
Start-Sleep -Milliseconds 900

try { Stop-Process -Id $p.Id -Force } catch { }
Start-Sleep -Milliseconds 500
Copy-Item $bak $cfg -Force
Remove-Item $bak -Force
Write-Host 'settings restored'
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
