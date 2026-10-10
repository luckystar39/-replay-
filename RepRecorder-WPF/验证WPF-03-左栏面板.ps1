# verify-WPF-03: phase 1 -- the left control panel (cards, controls, collapse, width memory)
# ASCII ONLY. No Chinese literals anywhere: a BOM-less UTF-8 script is read as ANSI by
# PowerShell 5.1 and a trailing Chinese char eats the closing quote of the string.
# Chinese text is asserted with \uXXXX regex escapes instead.
# Text dumps go to _dump-utf8.json so a human can read the real characters.
$dir = $PSScriptRoot
$exe = Join-Path $dir 'RepRecorderWPF.exe'
$cfg = Join-Path $dir 'RepRecorder.settings.json'
$bak = Join-Path $dir '_settings-backup.json'
$dump = Join-Path $dir '_dump-utf8.json'
$port = 7798
$fail = 0
$report = @()

function Dbg([string]$cmd) {
  try {
    $c = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $port)
    $c.ReceiveTimeout = 3000
    $w = New-Object System.IO.StreamWriter($c.GetStream()); $w.AutoFlush = $true; $w.WriteLine($cmd)
    $r = New-Object System.IO.StreamReader($c.GetStream()); $out = $r.ReadLine(); $c.Close()
    return $out
  } catch { return $null }
}
function Check([string]$what, [bool]$ok, [string]$detail) {
  if ($ok) { Write-Host ("OK  {0}  {1}" -f $what, $detail) }
  else { Write-Host ("FAIL {0}  {1}" -f $what, $detail); $script:fail = 1 }
  $script:report += [pscustomobject]@{ what = $what; ok = $ok; detail = $detail }
  Add-Content -LiteralPath (Join-Path $dir '_run03.log') -Value (($(if ($ok) {'OK  '} else {'FAIL'})) + $what + '  ' + $detail) -Encoding UTF8
}
function CfgVal([string]$key) {
  try { return (Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json).$key } catch { return $null }
}
$CJK = '[\u4e00-\u9fff]'

Get-Process RepRecorderWPF -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Milliseconds 500
Copy-Item $cfg $bak -Force

# 2026-10-08: a stored panel width WIDER than the window used to be applied verbatim.
# Maximise (client ~2560), drag the splitter far right, quit, relaunch at the default 1180 ->
# that fixed pixel column is re-applied, the right side (queue/terminal) is laid out at 0 width
# and the 6px splitter ends up off-window and ungrabbable.  Inject a huge value: it must be
# clamped so the right side keeps MinRightPanel.
$j0 = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j0.PanelWidth = 3000
$j0.PanelCollapsed = $false
$j0 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8
$expectParallel = 6
try { $expectParallel = (Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json).ParallelCount } catch { }
Write-Host ('settings ParallelCount before test = ' + $expectParallel)

# 2026-10-10: several checks below measure the background layer's viewport, which only
# exists when a background image is configured -- and this test used to just assume the user
# had one (the same trap 验证WPF-07 hit on 2026-10-05, fixed there but not here).  It broke
# for real: the user's BgImage got wiped, so the test went red for a reason that had nothing
# to do with the panel.  Point it at the repo's own bg-test.png; $bak is restored at the end.
$j1 = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$j1.BgImage = (Join-Path $dir 'bg-test.png')
$j1 | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8

$p = Start-Process -FilePath $exe -ArgumentList @('--wpf', "--debug-port=$port") -PassThru
Start-Sleep -Seconds 5
$p.Refresh()
Check 'app alive' (-not $p.HasExited) ('pid=' + $p.Id)

# 2026-10-07: the app remembers whether the panel was collapsed last time, and every
# assertion below assumes it starts EXPANDED. Without this the whole file went red the
# moment the user left the panel collapsed, which says nothing about the code.
# Normalise the state first, the same way 验证WPF-12 does for the shortcut test.
function Dbg0([string]$c) {
  try {
    $cl = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $port)
    $cl.ReceiveTimeout = 4000
    $w = New-Object System.IO.StreamWriter($cl.GetStream()); $w.AutoFlush = $true; $w.WriteLine($c)
    $r = New-Object System.IO.StreamReader($cl.GetStream()); $out = $r.ReadLine(); $cl.Close()
    return $out
  } catch { return $null }
}
$s0 = $null
for ($i = 0; $i -lt 10; $i++) { $s0 = Dbg0 'status' | ConvertFrom-Json; if ($s0) { break }; Start-Sleep -Milliseconds 500 }
if ($s0 -and $s0.collapsed) {
  Dbg0 'press:btnExpand' | Out-Null
  Start-Sleep -Milliseconds 900
  Write-Host 'normalised: panel was collapsed at startup, expanded it'
  Check 'panel starts expanded' ($true) 'normalised'
} else {
  Check 'panel starts expanded' ($true) 'already expanded'
}

$ids = @('panelLeft','splitterV','btnPanel','btnBg','cardRecord','cardParallel','cardQueue','cardSound','cardEnv',
         'btnStart','btnStop','btnClear','btnRemove','btnMark','btnRefresh','btnPrepare','btnCheck','btnSet','btnOpenLog',
         # chkAuto / lblParHint removed 2026-10-05 at the user's request
         # (no more "drag in then auto start"; no more "(= the N you selected)" line).
         'btnSound','btnGameAudio','btnBgm','cbParallel','cbMic','chkPrepare','chkCloseObs','chkRound',
         'lblMemHint','hint','activityBar','btnExpand')
$missing = @()
foreach ($id in $ids) {
  $r = Dbg "find:$id" | ConvertFrom-Json
  if ($r.found -ne $true) { $missing += $id }
}
Check 'all left-panel controls present' ($missing.Count -eq 0) ('missing=' + ($missing -join ','))

# --- texts (assert with escapes; keep the real text for the human) ---
$tStart = Dbg 'text:btnStart' | ConvertFrom-Json
Check 'btnStart has a playlist glyph + CJK' (($tStart.text.IndexOf([char]0x25B6) -ge 0) -and ($tStart.text -match $CJK)) ('raw=' + $tStart.text)
$tMem = Dbg 'text:lblMemHint' | ConvertFrom-Json
Check 'mem hint looks like "<n.n> GB"' ($tMem.text -match '\d\.\d GB') ('raw=' + $tMem.text)
$tPar = Dbg 'text:cbParallel' | ConvertFrom-Json
Check 'cbParallel shows the saved value' ($tPar.text -eq "$expectParallel") ('raw=' + $tPar.text + ' expect=' + $expectParallel)
$tMic = Dbg 'text:cbMic' | ConvertFrom-Json
Check 'cbMic has a selection' ($tMic.text.Length -gt 0) ('raw=' + $tMic.text)

# --- parallel count -> settings ---
$r = Dbg 'set:cbParallel=2' | ConvertFrom-Json
Check 'set cbParallel=2' ($r.ok -eq $true) ('ok=' + $r.ok)
Start-Sleep -Milliseconds 400
Check 'settings ParallelCount==3' ((CfgVal 'ParallelCount') -eq 3) ('file=' + (CfgVal 'ParallelCount'))
$tMem = Dbg 'text:lblMemHint' | ConvertFrom-Json
Check 'mem hint follows (1.6 GB)' ($tMem.text -match '1\.6') ('raw=' + $tMem.text)

# --- collapse / expand ---
Dbg 'set:cbParallel=5' | Out-Null
Start-Sleep -Milliseconds 300
$s = Dbg 'status' | ConvertFrom-Json
Check 'panel width > 120' ($s.panel -gt 120) ('panel=' + $s.panel)
Check 'not collapsed' ($s.collapsed -eq $false) ('collapsed=' + $s.collapsed)

$r = Dbg 'press:btnPanel' | ConvertFrom-Json
Start-Sleep -Milliseconds 700
$s = Dbg 'status' | ConvertFrom-Json
Check 'collapsed after press' ($s.collapsed -eq $true) ('collapsed=' + $s.collapsed)
Check 'panel width 0 when collapsed' ($s.panel -eq 0) ('panel=' + $s.panel)
Check 'settings PanelCollapsed==true' ((CfgVal 'PanelCollapsed') -eq $true) ('file=' + (CfgVal 'PanelCollapsed'))
$tBtn = Dbg 'text:btnPanel' | ConvertFrom-Json
Check 'top button shows the expand arrow (U+25B6)' ($tBtn.text.IndexOf([char]0x25B6) -ge 0) ('raw=' + $tBtn.text)

$r = Dbg 'press:btnExpand' | ConvertFrom-Json
Start-Sleep -Milliseconds 700
$s = Dbg 'status' | ConvertFrom-Json
Check 'expanded again' ($s.collapsed -eq $false) ('collapsed=' + $s.collapsed)
Check 'panel width restored' ($s.panel -gt 120) ('panel=' + $s.panel)
$tBtn = Dbg 'text:btnPanel' | ConvertFrom-Json
Check 'top button back to the collapse arrow (U+25C0)' ($tBtn.text.IndexOf([char]0x25C0) -ge 0) ('raw=' + $tBtn.text)

# =====================================================================================
#  2026-10-07 回归护栏：折叠/展开左栏之后，背景的视口必须跟着图层尺寸重算。
#
#  背景的范围是「只铺内容区」时，图层大小 = 窗口减去左栏 —— 左栏一收一放，图层尺寸就变。
#  而背景的尺寸/位移是通过【画刷视口】做的，视口是拿 bgLayer.ActualWidth 算的。
#  SetPanelCollapsed 当时是改完列宽立刻调 ApplyBackground()，**那时布局还没跑**，
#  ActualWidth 还是旧的 → 视口停在旧尺寸上没人纠正。
#  实测症状：左栏收起再展开，视口留在 1685x1264 而图层已经是 1417x1003，
#  背景图被放大 1.19 倍、顶部被切（用户报的"有bug，如图"）。
#  判据：视口的宽高必须等于 rendered（同一时刻重算出来的正确值）。
# =====================================================================================
$b = Dbg 'bgstate' | ConvertFrom-Json
if ($b.viewport -ne '' -and $b.rendered -ne '') {
  $vp = $b.viewport.Split(',')[2].Split('x')
  $rd = $b.rendered.Split('x')
  Check 'background viewport matches the rendered size after panel toggling' `
        (([int]$vp[0] -eq [int]$rd[0]) -and ([int]$vp[1] -eq [int]$rd[1])) `
        ('viewport=' + $b.viewport + '  rendered=' + $b.rendered + '  layer=' + $b.layer)
} else {
  Check 'background viewport matches the rendered size after panel toggling' ($false) ('no image configured: viewport=[' + $b.viewport + '] rendered=[' + $b.rendered + ']')
}

# --- 过渡期间不许出现"视口尺寸和该有的尺寸对不上"的那一帧 -----------------------------
# 上面的检查是在静下来之后读的，抓不到过渡中的那一帧 —— 而用户看到的"闪一下再跑到右边"
# 恰好就在那一帧里（点击 → ApplyBackground 立刻跑，但布局还没跟上）。
# 所以这里压着点：点完按钮**不睡**，以最快速度连读 bgstate，看有没有哪一帧对不上。
# 2026-10-07 实测：修复前每轮会抓到 1~2 帧（视口算出 1144x858 这种"宽按旧、高按新"的混合值，
# 根因是 RenderedExtent 内部又读了一次过期的 bgLayer.ActualWidth，和外面推算的尺寸不是一套）；
# 修复后三轮全 0。
for ($round = 0; $round -lt 2; $round++) {
  Dbg 'press:btnPanel' | Out-Null      # 收起
  Start-Sleep -Milliseconds 1100
  Dbg 'press:btnPanel' | Out-Null      # 展开 —— 紧接着就开始连读
  $bad = 0
  for ($i = 0; $i -lt 12; $i++) {
    $b = Dbg 'bgstate' | ConvertFrom-Json
    if ($b -and $b.viewport -ne '' -and $b.rendered -ne '') {
      $vp = $b.viewport.Split(',')[2].Split('x')
      $rd = $b.rendered.Split('x')
      if ($vp[0] -ne $rd[0] -or $vp[1] -ne $rd[1]) { $bad++ }
    }
  }
  Start-Sleep -Milliseconds 800
  Check ('no wrong-sized background frame while expanding (round ' + $round + ')') ($bad -eq 0) `
        ($bad.ToString() + ' bad frames out of 12 samples')
}

Check 'no crash in log' ((Get-Content (Join-Path $dir 'RepRecorder.log') -Raw -Encoding UTF8) -notmatch 'Exception') ''

# --- a stored panel width must not squeeze the right panel out of existence -----------
$s = Dbg 'status' | ConvertFrom-Json
$winW = [int]($s.win.Split('x')[0])
$right = $winW - [int]$s.panel - 6
Check 'a stored 3000px panel width is clamped (right side survives)' ($right -ge 380) `
      ('win=' + $s.win + '  panel=' + $s.panel + '  right=' + $right)

# --- the settings dialog's "re-detect" must refresh the input boxes ---------------------
# Only the path-style Field used to get .Box set; the port (NumberRow) and password
# (PasswordRow) rows did not -- so re-detect updated cfg but not what the box showed, and OK
# then wrote the stale text back, silently discarding the detected port/password.
# Method: type 9999 into the port box (cfg untouched), press re-detect, the box must be
# refreshed back to cfg's real value.
Dbg 'press:btnSet' | Out-Null
Start-Sleep -Milliseconds 1600
Check 'settings dialog opened' ((Dbg 'find:setAuto' | ConvertFrom-Json).found -eq $true) ''
$before = (Dbg 'text:set_Port' | ConvertFrom-Json).text
Dbg 'set:set_Port=9999' | Out-Null
Start-Sleep -Milliseconds 500
$typed = (Dbg 'text:set_Port' | ConvertFrom-Json).text
Dbg 'press:setAuto' | Out-Null
Start-Sleep -Milliseconds 1400
$after = (Dbg 'text:set_Port' | ConvertFrom-Json).text
$cfgPort = (Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json).Port
Check 'the port box was actually typed over (guard is meaningful)' ($typed -eq '9999') ('typed=' + $typed)
Check 're-detect refreshes the port box (not silently discarded)' ($after -eq "$cfgPort") `
      ('was=' + $before + '  typed=9999  after=' + $after + '  cfg=' + $cfgPort)

try { Stop-Process -Id $p.Id -Force } catch { }
Start-Sleep -Milliseconds 500
Copy-Item $bak $cfg -Force
Remove-Item $bak -Force
Write-Host 'settings restored'
[System.IO.File]::WriteAllText($dump, ($report | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('dump -> ' + $dump)

if ($fail -eq 0) { Write-Host 'PASS' } else { Write-Host 'FAILED'; exit 1 }
