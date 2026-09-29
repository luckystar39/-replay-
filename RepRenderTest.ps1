# ReplayDnD "rep -> mp4" 渲染测试 / 诊断脚本
# 用法示例：
#   .\RepRenderTest.ps1 -Minimal                       # 只开 ReplayDnD + th123intl 试渲染
#   .\RepRenderTest.ps1 -Enable ReplayDnD,th123intl,SaveRep
#   .\RepRenderTest.ps1 -Rep "D:\...\xxx.rep" -Minimal
# 判定标准：输出 mp4 里必须存在 moov 索引盒，才算真正可播放的成功文件。
param(
  [string]$GameDir = "D:\【完整包】非想天则整合包2026-06-15\【完整包】非想天则整合包2026-06-15\th123",
  [string]$Rep,
  [string[]]$Enable,
  [switch]$Minimal,
  [int]$TimeoutSec = 120
)

$ErrorActionPreference = 'Stop'
$cfg = Join-Path $GameDir 'ModLoaderSettings.json'
$exe = Join-Path $GameDir 'th123.exe'

if (-not (Test-Path -LiteralPath $exe)) { throw "找不到 $exe" }

# 选一个测试用 rep（取最大的一场真实对局，排除路径中含 ! % & 的）
if (-not $Rep) {
  $cand = Get-ChildItem -LiteralPath (Join-Path $GameDir 'replay') -Recurse -Force -File -Filter *.rep |
          Where-Object { $_.FullName -notmatch '[!%&]' } | Sort-Object Length -Descending
  if (-not $cand) { throw 'replay 目录里没有可用的 rep' }
  $Rep = $cand[0].FullName
}

# 写入 mod 开关
$j = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
if ($Minimal) { $Enable = @('modules/ReplayDnD/ReplayDnD.dll','modules/th123intl/th123intl.dll') }
elseif (-not $Enable) { $Enable = @('modules/ReplayDnD/ReplayDnD.dll') }
else { $Enable = $Enable | ForEach-Object { if ($_ -match '/') { $_ } else { "modules/$_/$_.dll" } } }

foreach ($p in $j.modules.PSObject.Properties) { $p.Value.enabled = ($Enable -contains $p.Name) }
$j | ConvertTo-Json -Compress -Depth 6 | Set-Content -LiteralPath $cfg -Encoding UTF8

Write-Host "启用的 mod:" -ForegroundColor Cyan
$j.modules.PSObject.Properties | Where-Object { $_.Value.enabled } | ForEach-Object { "   " + $_.Name }
Write-Host "测试 rep: $Rep" -ForegroundColor Cyan

# 渲染
$out = [System.IO.Path]::ChangeExtension($Rep, '.mp4')
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }
Get-Process th123 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$p = Start-Process -FilePath $exe -ArgumentList "/render `"$Rep`"" -WorkingDirectory $GameDir -PassThru
$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
  Start-Sleep -Milliseconds 800
  if (-not (Get-Process -Id $p.Id -ErrorAction SilentlyContinue)) { break }
}
$alive = [bool](Get-Process -Id $p.Id -ErrorAction SilentlyContinue)
if ($alive) { Get-Process -Id $p.Id | Stop-Process -Force }
Start-Sleep -Milliseconds 500

# 判定
$size = -1; $hasMoov = $false
if (Test-Path -LiteralPath $out) {
  $size = (Get-Item -LiteralPath $out).Length
  $hasMoov = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($out)).Contains('moov')
}
$verdict = if ($hasMoov) { '成功（mp4 可播放）' } else { '失败（无 moov 索引，文件不可播放）' }
Write-Host ""
Write-Host "耗时      : $([math]::Round($sw.Elapsed.TotalSeconds,1)) 秒" -ForegroundColor Yellow
Write-Host "输出文件  : $out" -ForegroundColor Yellow
Write-Host "大小      : $size bytes" -ForegroundColor Yellow
Write-Host "moov 索引 : $hasMoov" -ForegroundColor Yellow
Write-Host "结论      : $verdict" -ForegroundColor $(if ($hasMoov) { 'Green' } else { 'Red' })
