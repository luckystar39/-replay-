# verify-WPF-07: the background settings dialog (phase 4's last piece)
#   opens from the top bar, edits live, writes through to cfg + the rendered layer,
#   and 取消 puts everything back.
# ASCII only. Run with: powershell -File <this>   (never pipe its stdout)
$dir = $PSScriptRoot
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$cfg = Join-Path $dir 'RepRecorder.settings.json'
$bak = Join-Path $dir '_settings-backup.json'
$runLog = Join-Path $dir '_run07.log'
$port = 7788
$fail = 0
Remove-Item $runLog -ErrorAction SilentlyContinue

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
$imgPath = Join-Path $dir 'bg-test.png'
$j = Get-Content $cfg -Raw -Encoding UTF8
$j = $j -replace '"BgImage":"[^"]*"', ('"BgImage":"' + $imgPath.Replace('\', '\\') + '"')
[System.IO.File]::WriteAllText($cfg, $j, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('bg image forced to: ' + $imgPath)

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

$bg = Dbg 'bgstate' | ConvertFrom-Json
Check 'bgstate answers' ($bg -ne $null) ''
Check 'background image loaded' ($bg.image -eq $true) ('pixels=' + $bg.pixels)
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
#    拉伸 给 0；自由/平铺 不限。
#  某根轴余量为 0 时，那一行在设置窗里【整行藏掉】-- 和 WE 的属性面板一样。
# =====================================================================================
function Lay([int]$i) { Dbg ("set:bgLayout=$i") | Out-Null; Start-Sleep -Milliseconds 700 }
function RowVis([string]$id) { return (Dbg "find:$id" | ConvertFrom-Json).vis }

$layName = @('cover', 'contain', 'stretch', 'center', 'tile', 'free')
foreach ($i in @(0, 1, 2, 3, 4, 5)) {
  Lay $i
  $lb = Dbg 'bgstate' | ConvertFrom-Json
  $rx = RowVis 'bgOffXRow'
  $ry = RowVis 'bgOffYRow'
  # The rule under test: limit exactly 0 ("nothing to move") <=> the row must be hidden.
  $wantX = $(if ([double]$lb.panx -eq 0) { 'Collapsed' } else { 'Visible' })
  $wantY = $(if ([double]$lb.pany -eq 0) { 'Collapsed' } else { 'Visible' })
  Check ('position row hides when its axis has no slack (' + $layName[$i] + ')') `
        (($rx -eq $wantX) -and ($ry -eq $wantY)) `
        ('pan=' + $lb.panx + ',' + $lb.pany + '  rows=' + $rx + ',' + $ry)
}

Lay 2
$lb = Dbg 'bgstate' | ConvertFrom-Json
Check 'stretch leaves no room to move at all' (([double]$lb.panx -eq 0) -and ([double]$lb.pany -eq 0)) ('pan=' + $lb.panx + ',' + $lb.pany)

Lay 5
$lb = Dbg 'bgstate' | ConvertFrom-Json
Check 'free is unlimited on both axes' (([double]$lb.panx -eq -1) -and ([double]$lb.pany -eq -1)) ('pan=' + $lb.panx + ',' + $lb.pany)

Lay 4
$lb = Dbg 'bgstate' | ConvertFrom-Json
Check 'tile is unlimited on both axes' (([double]$lb.panx -eq -1) -and ([double]$lb.pany -eq -1)) ('pan=' + $lb.panx + ',' + $lb.pany)

# cover must fit one axis EXACTLY -- that is what makes cover "no background ever shows".
Lay 0
$lb = Dbg 'bgstate' | ConvertFrom-Json
Check 'cover fits exactly one axis (so it can never show background)' `
      ((([double]$lb.panx -eq 0) -and ([double]$lb.pany -gt 0)) -or (([double]$lb.pany -eq 0) -and ([double]$lb.panx -gt 0))) `
      ('pan=' + $lb.panx + ',' + $lb.pany)

# Park the offsets wide open in free, then switch to stretch: ApplyBackground() must clamp
# them back, otherwise the stored value keeps pointing somewhere the image can no longer go.
Lay 5
Dbg 'set:bgOffX=99' | Out-Null
Dbg 'set:bgOffY=99' | Out-Null
Start-Sleep -Milliseconds 600
Lay 2
$lb = Dbg 'bgstate' | ConvertFrom-Json
Check 'switching to stretch clamps the stored offset back to 0' (([int]$lb.ox -eq 0) -and ([int]$lb.oy -eq 0)) ('ox=' + $lb.ox + ' oy=' + $lb.oy)

# ---- 翻转 / 缩放：和位移正交的一层变换，必须真的落到画刷上 ----
Lay 5
Dbg 'set:bgFlipH=true' | Out-Null
Start-Sleep -Milliseconds 700
$lb = Dbg 'bgstate' | ConvertFrom-Json
Check 'flip horizontal reaches the brush' ($lb.brushScale -match '^-1,') ('brushScale=' + $lb.brushScale)
Check 'flip horizontal reaches the setting' ($lb.flipH -eq $true) ('flipH=' + $lb.flipH)

Dbg 'set:bgFlipV=true' | Out-Null
Start-Sleep -Milliseconds 700
$lb = Dbg 'bgstate' | ConvertFrom-Json
Check 'flip vertical reaches the brush' ($lb.brushScale -eq '-1,-1') ('brushScale=' + $lb.brushScale)

Dbg 'set:bgZoom=200' | Out-Null
Start-Sleep -Milliseconds 700
$lb = Dbg 'bgstate' | ConvertFrom-Json
Check 'zoom reaches the brush on top of the flips' ($lb.brushScale -eq '-2,-2') ('brushScale=' + $lb.brushScale)

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

try { Stop-Process -Id $p.Id -Force } catch { }
Start-Sleep -Milliseconds 500
Copy-Item $bak $cfg -Force
Remove-Item $bak -Force
Write-Host 'settings restored'
if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
