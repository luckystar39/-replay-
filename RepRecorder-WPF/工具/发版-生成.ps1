# 发版-生成.ps1 —— 半自动发版的"备料"部分（2026-10-10）
#
# 用法（由 发版.bat 调用）：
#   发版-生成.ps1 <版本号> [--check]
#     不带 --check ：改源码里的版本号 → 编译 → 跑 验证WPF-00 → 同步到分发目录
#     --check      ：只做"不动源码"的那部分（验收脚本用的就是它）
#   两种模式最后都会：打更新包 → 算 sha256 → 写 version.json → 写照着做-发版.txt
#
# 环境变量（由 发版.bat 设好）：
#   GITEE   = 你的用户名/仓库名（留空 = 还没有 Gitee 仓库，那就跳过 Gitee 那条链接）
#   GITHUB  = 用户名/仓库名
#   NOTES   = 这一版的更新说明（一行）
#
# ⚠ 本文件必须存成 UTF-8【带 BOM】。PS 5.1 会把无 BOM 的 UTF-8 当 ANSI(CP936) 读，
#   中文注释和字符串会全乱 —— 仓库里 打包-发给别人.ps1 就是这个编码。
# ⚠ 只改"匹配到的那一行"，不做整文件重写；--check 模式下**一个字节都不写**。

$ErrorActionPreference = 'Stop'
$ver = $args[0]
$check = ($args -contains '--check')

if ([string]::IsNullOrEmpty($ver)) { Write-Host '用法：发版-生成.ps1 <版本号> [--check]'; exit 2 }
if ($ver -notmatch '^\d+\.\d+(\.\d+){0,2}$') { Write-Host ('版本号格式不对：' + $ver + '（应该像 1.0 或 1.1.2）'); exit 2 }
$four = (($ver.TrimEnd('.') + '.0.0.0') -split '\.')[0..3] -join '.'

$dir  = $PSScriptRoot
$wpf  = Split-Path $dir -Parent
$root = Split-Path $wpf -Parent
# 产物目录。验收脚本会用 RELEASE_OUT 指到临时目录 —— 否则它自己那一跑会把
  # 真正的发版材料覆盖成“用测试版本号生成的”（实测发生过：跑完测试，_发版 里
  # 躺着一份声称 1.1、内容却是 1.0 exe 的清单，照它发布会让用户永远收到“有新版”）。
  $rel  = if ($env:RELEASE_OUT) { $env:RELEASE_OUT } else { Join-Path $root '_发版' }
$src  = Join-Path $root 'RepRecorder-给别人'
New-Item -ItemType Directory -Force -Path $rel | Out-Null

$gitee  = $env:GITEE
$github = $env:GITHUB
$notes  = $env:NOTES
if ([string]::IsNullOrEmpty($notes)) { $notes = '（这一版没写更新说明）' }

# ---------------------------------------------------------------- 1) 版本号（两处源码）
function Run-Bat([string]$bat) {
  # ⚠ 不要用 `& cmd.exe /c ... | Out-String` 这种接法：那样 bat 里的
  #   powershell -File <中文名>.ps1 会把路径搞坏（实测：打包步骤直接失败，
  #   而且因为输出被接管道收走，失败还很难看出来）。
  #   让它自己开一个控制台（用系统默认代码页，GBK 文件才读得对）。
  $p = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', ('"' + $bat + '"'), '/nopause' -Wait -PassThru
  if ($p.ExitCode -ne 0) { Write-Host ('  ' + (Split-Path $bat -Leaf) + ' 返回了 ' + $p.ExitCode); exit 5 }
}
function Patch-One([string]$file, [string]$pattern, [string]$replacement, [string]$label) {
  if (-not (Test-Path $file)) { Write-Host ('找不到 ' + $file); exit 3 }
  $txt = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)
  # 判断“有没有匹配到”，而不是“文本有没有变”
  # —— 否则同一个版本号跑第二遍会被误报成
  # “没找到 AppInfo.Version”（其实找到了，只是值一样）。
  $m = [regex]::Match($txt, $pattern)
  if (-not $m.Success) { Write-Host ('在 ' + (Split-Path $file -Leaf) + ' 里没找到 ' + $label + ' —— 规则要更新了'); exit 3 }
  if ($m.Value -eq $replacement) { Write-Host ('  ' + $label + ' 已经是目标值，不用改'); return $txt }
  $new = [regex]::Replace($txt, $pattern, $replacement)
  Write-Host ('  ' + $label + ': ' + $m.Value + '  ->  ' + $replacement)
  if (-not $check) {
    [System.IO.File]::WriteAllText($file, $new, (New-Object System.Text.UTF8Encoding($false)))
  }
  return $new
}

Write-Host ('目标版本 ' + $ver + '（四段写法 ' + $four + '）')
Patch-One (Join-Path $wpf (Join-Path 'src' 'Core.cs')) 'const string Version = "[^"]*"' ('const string Version = "' + $ver + '"') 'AppInfo.Version' | Out-Null
Patch-One (Join-Path $wpf (Join-Path 'src' 'AssemblyInfo.cs')) 'AssemblyVersion\("[^"]*"\)' ('AssemblyVersion("' + $four + '")') 'AssemblyVersion' | Out-Null
Patch-One (Join-Path $wpf (Join-Path 'src' 'AssemblyInfo.cs')) 'AssemblyFileVersion\("[^"]*"\)' ('AssemblyFileVersion("' + $four + '")') 'AssemblyFileVersion' | Out-Null

# ---------------------------------------------------------------- 2) 不带 --check 才做的
if (-not $check) {
  # ⚠ 调那两个 .bat 之前必须把控制台代码页设成 936。它们是 GBK 编码的，
  #   而从这个环境继承来的代码页可能是 65001 —— 那样 cmd 会把整个 bat 按 UTF-8 解，
  #   中文路径全乱，bat 内部的 powershell -File 调用直接失败（实测踩过）。
  try { [Console]::OutputEncoding = [Text.Encoding]::GetEncoding(936) } catch { }
  try { & chcp.com 936 | Out-Null } catch { }
  Write-Host '编译…'
  Run-Bat (Join-Path $dir '编译.bat')
  Write-Host '跑 验证WPF-00（它会核对版本号三处一致）…'
  # ⚠ 就地调用（不要经 powershell.exe -File 子进程）：中文路径过进程边界会被
  #   编码搞坏，脚本根本跑不起来，而那时又容易被误判成"通过"。
  $v00 = (& (Join-Path $dir '验证WPF-00-编译与启动.ps1') *>&1 | Out-String)
  if ([string]::IsNullOrEmpty($v00) -or ($v00 -notmatch '(?m)^PASS'))
  { Write-Host '验证WPF-00 没过，停在这儿。输出：'; Write-Host $v00; exit 4 }
  Write-Host '同步到分发目录…'
  Run-Bat (Join-Path $dir '同步到所有文件夹.bat')
  # ⚠ 后置校验：确认同步真的把新 exe 送过去了。
  #   不加这一条，就会出现"编译成功、同步失败、更新包里装的还是旧 exe"这种
  #   半对的结果 —— 实测发生过一次。
  $hDev = (Get-FileHash (Join-Path $dir 'RepRecorderWPF.exe') -Algorithm SHA256).Hash
  $dstExe = Join-Path $src 'RepRecorder.exe'
  $hDst = if (Test-Path $dstExe) { (Get-FileHash $dstExe -Algorithm SHA256).Hash } else { '' }
  if ($hDev -ne $hDst)
  { Write-Host '同步没成功：分发目录里的 exe 和刚编译出来的不一致，停在这儿。'; exit 6 }
  Write-Host '  分发目录已更新（和刚编译的哈希一致）'
}

# ---------------------------------------------------------------- 3) 更新包
# 只装三样：exe + 两份说明。⚠ 绝不含 ffmpeg（217MB）—— 更新包必须小，
# 否则"一键更新"就变成下 85MB。验证WPF-18 有一条断言钉着"小于 20MB"。
# ⚠ 附件名一律用纯 ASCII，Gitee 那边也一起用（2026-10-10 实测的教训）：
#   往 GitHub 发行版传 RepRecorder-1.0-更新包.zip，落地后名字变成 RepRecorder-1.0-.zip
#   —— 中文被吃掉了。而下载地址是拿这个名字拼出来的，于是链接 404，用户更新失败。
#   两个仓库共用同一个纯 ASCII 名字，就不会再出现"传上去的名字和清单里的对不上"。
$updName  = 'RepRecorder-' + $ver + '-update.zip'
$fullName = 'RepRecorder-' + $ver + '-full.zip'
$updPath = Join-Path $rel $updName
if (Test-Path $updPath) { Remove-Item -LiteralPath $updPath -Force }
$exeIn = Join-Path $src 'RepRecorder.exe'
if (-not (Test-Path $exeIn)) { Write-Host ('分发目录里没有 RepRecorder.exe：' + $exeIn + '（先跑一次 同步到所有文件夹.bat）'); exit 5 }
$pack = @($exeIn)
$pack += @(Get-ChildItem -LiteralPath $src -Filter '*.md' | ForEach-Object { $_.FullName })
Compress-Archive -LiteralPath $pack -DestinationPath $updPath -Force
$sha = (Get-FileHash $updPath -Algorithm SHA256).Hash.ToLower()
$sizeKB = [int]((Get-Item $updPath).Length / 1024)
Write-Host ('  更新包：' + $updName + '（' + $sizeKB + ' KB）')

# 完整包也备一份"纯 ASCII 名字"的副本放进 _发版，这样"传附件"就只是把 _发版 里那两个文件
# 拖上去，不用临时改名、也不会因为名字对不上而 404。（原件 RepRecorder-发给别人.zip 不动，
# 网盘/直接发人那条路还在用它。）
$fullCopy = Join-Path $rel $fullName
$haveFull = $false
$fullSrc = Join-Path $root 'RepRecorder-发给别人.zip'
if (Test-Path -LiteralPath $fullSrc) {
  Copy-Item -LiteralPath $fullSrc -Destination $fullCopy -Force
  $haveFull = $true
  Write-Host ('  完整包副本：' + $fullName + '（' + [int]((Get-Item $fullCopy).Length / 1048576) + ' MB，传完可以删）')
} else {
  Write-Host ('  ⚠ 没找到 ' + $fullSrc + '：完整包那条链接不写进清单')
}

# ---------------------------------------------------------------- 4) version.json
$links = @()
if (-not [string]::IsNullOrEmpty($gitee)) {
  $links += [pscustomobject]@{ Type = 'Gitee'; Url = 'https://gitee.com/' + $gitee + '/releases/download/v' + $ver + '/' + $updName }
}
if (-not [string]::IsNullOrEmpty($github)) {
  $links += [pscustomobject]@{ Type = 'GitHub'; Url = 'https://github.com/' + $github + '/releases/download/v' + $ver + '/' + $updName }
}
$full = ''
if ((-not [string]::IsNullOrEmpty($github)) -and $haveFull) {
  $full = 'https://github.com/' + $github + '/releases/download/v' + $ver + '/' + $fullName
}
$manifest = [pscustomobject]@{
  Version      = $ver
  Notes        = $notes
  Sha256       = $sha
  DownloadLinks = $links
  FullPackage  = $full
}
$jsonPath = Join-Path $rel 'version.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
Write-Host ('  version.json：' + $jsonPath)

if ([string]::IsNullOrEmpty($gitee)) {
  Write-Host '  ⚠ GITEE 还没配（发版.bat 开头那一行留空）—— version.json 里没有 Gitee 链接，'
  Write-Host '    而程序默认就是去 Gitee 取清单。建好仓库、填上那一行、重跑本脚本。'
}

# ---------------------------------------------------------------- 5) 手把手清单
$steps = @()
$steps += '发版 ' + $ver + ' —— 还剩这几步（脚本已经备好料，放在 ' + $rel + '）'
$steps += ''
$steps += '1) 建 Release。⚠ 标签（tag）必须正好是 v' + $ver + '——清单里的下载地址是靠标签定位的，'
$steps += '   标签写成别的（比如中文名）链接就永远是 404。发行版的"名称"可以随便写，标签不行。'
$steps += '   附件就在 ' + $rel + ' 里（名字别改，都是纯 ASCII）：'
$steps += '     更新包：' + $updName + '（' + $sizeKB + ' KB）—— Gitee 和 GitHub 都传这个'
if ($haveFull) { $steps += '     完整包：' + $fullName + '（84.8MB）—— 只传 GitHub，Gitee 那 1GB 别占；传完可以删掉副本' }
if (-not [string]::IsNullOrEmpty($gitee))  { $steps += '     Gitee ：https://gitee.com/' + $gitee + '/releases/new' }
if (-not [string]::IsNullOrEmpty($github)) { $steps += '     GitHub：https://github.com/' + $github + '/releases/new' }
$steps += '   ⚠ 为什么必须是纯 ASCII：往 GitHub 传中文名的附件，落地后中文会被吃掉'
$steps += '     （2026-10-10 实测：RepRecorder-1.0-更新包.zip 变成了 RepRecorder-1.0-.zip），'
$steps += '     而下载地址是拿这个名字拼的 → 链接 404、用户更新失败。'
$steps += '     传完一定要去发行版页面照一眼名字和清单里的是否一字不差。'
$steps += ''
$steps += '2) 把 version.json 的内容整段复制到 Gitee 仓库的 version.json 里，提交'
$steps += '     （内容就在 ' + $jsonPath + '）'
$steps += ''
$steps += '3) 完整包按你原来的方式发（网盘 / 直接发人）—— Gitee 每仓库附件总共只有 1GB，'
$steps += '   完整包 84.8MB 传几次就满，所以它走 GitHub（GitHub 传的就是 _发版 里那份 ASCII 副本）。'
$steps += ''
$steps += '4) 发完自己验一次：跑 RepRecorder-WPF\_netcheck.ps1（要联网、要有已发布的发行版）。'
$steps += '   它会真的从线上把更新包下下来、校验 sha、替换自身再重启，还会逐条 HEAD 两个下载地址'
$steps += '   确认没有 404——全绿才算发好了。'
$steps += ''
if (-not [string]::IsNullOrEmpty($gitee)) {
  $steps += '⚠ 清单地址写在程序里（Update.cs 的 ManifestUrl），现在是：'
  $steps += '   https://gitee.com/' + $gitee + '/raw/master/version.json'
} else {
  $steps += '⚠ 清单地址写在程序里（Update.cs 的 ManifestUrl）——还没配 Gitee 仓库时它是空的。'
}
$steps += '   改这一处要重新编译；⚠ 分支名（master / main）必须和 Gitee 仓库的默认分支一致。'
$stepsTxt = Join-Path $rel '照着做-发版.txt'
$steps | Set-Content -LiteralPath $stepsTxt -Encoding UTF8
Write-Host ('  照着做-发版.txt：' + $stepsTxt)

if ($check) { Write-Host '（--check：没有改源码、没有编译、没有同步）' }
Write-Host '备料完成。'
