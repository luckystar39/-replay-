# 打 RepRecorder-发给别人.zip
#
# 为什么要单独一个脚本：以前是「把 RepRecorder-给别人\ 整个目录塞进 zip」，
# 那个目录里混着**不该发出去**的东西：
#   · RepRecorder.settings.json —— 本机路径 + OBS 端口/密码（交接文档铁律 5：不要随包分发）
#   · RepRecorder.log / RepRecorder-上次.log —— 只是噪音
#   · 备份\ —— exe 备份，几百 MB 的冗余
# 所以改成「按白名单挑文件打包」，目录里多出什么都不会漏出去。
#
# 用法：双击 同步到所有文件夹.bat 会自动调它；也可以单独跑。
# 编码：本文件是 UTF-8 带 BOM（PowerShell 5.1 读它没问题；别改成无 BOM）。

param([string]$SourceDir, [string]$ZipPath)
$ErrorActionPreference = 'Stop'

if (-not $SourceDir) { $SourceDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'RepRecorder-给别人' }
if (-not $ZipPath)   { $ZipPath   = Join-Path (Split-Path -Parent $PSScriptRoot) 'RepRecorder-发给别人.zip' }

# 白名单：只有这些进包
$names = @(
  'RepRecorder.exe',
  'ffmpeg.exe',
  '说明-给使用者（简短版）.md',
  '说明-给使用者（详细版）.md'
)

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path -LiteralPath $SourceDir)) { Write-Host ("     [跳过] 找不到 " + $SourceDir); exit 1 }

Write-Host '    打包内容：'
$have = @()
foreach ($n in $names) {
  $p = Join-Path $SourceDir $n
  if (Test-Path -LiteralPath $p) {
    $have += $p
    Write-Host ('      + ' + $n + '  ' + [math]::Round((Get-Item -LiteralPath $p).Length / 1MB, 1) + ' MB')
  } else {
    Write-Host ('      ! 缺 ' + $n + '（跳过）')
  }
}
if ($have.Count -eq 0) { Write-Host '     [失败] 一个文件都没有，不打包'; exit 1 }

if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
$zip = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
  foreach ($p in $have) {
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
      $zip, $p, (Split-Path -Leaf $p), [System.IO.Compression.CompressionLevel]::Optimal)
  }
} finally { $zip.Dispose() }

Write-Host ('     [OK] ' + (Split-Path -Leaf $ZipPath) + '  ' +
            [math]::Round((Get-Item -LiteralPath $ZipPath).Length / 1MB, 1) + ' MB')
