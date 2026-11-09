# ============================================================================================
#  SteelStructureSuite 发布脚本（PowerShell 7 / pwsh）
#
#  背景：
#    AutoCAD 的托管程序集（AcDbMgd / AcCoreMgd / AcMgd / AdWindows）版本随 CAD 版本变化
#    （2019 = 23.x，2021~2024 = 24.x）。托管插件一旦引用了某个版本的程序集，就只能被
#    对应大版本的 AutoCAD 加载：把 2019 编译的 DLL 放进公共插件目录，AutoCAD 2021 会
#    直接加载失败，表现为"命令列表里什么都没有"。
#
#  方案：
#    为每个大版本分别编译，并发布成独立的 Bundle（SeriesMin / SeriesMax 互不重叠），
#    同时安装到公共插件目录，AutoCAD 只会加载与自己版本匹配的那一个。
#
#  用法：
#    pwsh tools/Publish.ps1 -Install            # 编译 + 发布 + 安装到本机
#    pwsh tools/Publish.ps1                     # 仅编译 + 生成 dist 产物
# ============================================================================================
[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'SteelStructureSuite.sln'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe'
if (-not (Test-Path $msbuild)) {
    $msbuild = (Get-ChildItem 'C:\Program Files\Microsoft Visual Studio' -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'MSBuild\\Current\\Bin' } | Select-Object -First 1).FullName
}
if (-not $msbuild -or -not (Test-Path $msbuild)) { throw '找不到 MSBuild.exe，请先安装 Visual Studio 2022。' }

# 大版本 → AutoCAD 安装目录
$targets = @(
    @{ Key = 'R23'; Series = 'R23.0 - R23.1 (AutoCAD 2019)'; AutoCADDir = 'C:\Program Files\Autodesk\AutoCAD 2019'; Bundle = 'SteelStructureSuite.bundle';         Manifest = 'installer\PackageContents.xml' },
    @{ Key = 'R24'; Series = 'R24.0 - R24.3 (AutoCAD 2021~2024)'; AutoCADDir = 'C:\Program Files\Autodesk\AutoCAD 2021'; Bundle = 'SteelStructureSuite2021.bundle'; Manifest = 'installer\PackageContentsR24.xml' }
)

function Stop-Acad {
    $procs = Get-Process -Name acad -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host '检测到 acad.exe 正在运行，正在结束进程...' -ForegroundColor Yellow
        $procs | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
}

foreach ($t in $targets) {
    $acadDir = $t.AutoCADDir
    if (-not (Test-Path (Join-Path $acadDir 'AcDbMgd.dll'))) {
        Write-Warning "跳过 $($t.Key)：未找到 $acadDir\AcDbMgd.dll"
        continue
    }

    $outDir = Join-Path $root ("SteelStructureSuite\bin\x64\Release_" + $t.Key)
    if (-not $SkipBuild) {
        Write-Host "编译 $($t.Key) → $acadDir" -ForegroundColor Cyan
        & $msbuild $solution /t:Rebuild /p:Configuration=Release /p:Platform=x64 "/p:AutoCADDir=$acadDir" "/p:OutputPath=$outDir" /v:minimal /nologo
        if ($LASTEXITCODE -ne 0) { throw "编译失败：$($t.Key)" }
    }

    $bundleDir = Join-Path $root ("dist\" + $t.Bundle)
    $contents = Join-Path $bundleDir 'Contents'
    New-Item -ItemType Directory -Force -Path (Join-Path $contents 'Help') | Out-Null

    Copy-Item (Join-Path $outDir 'SteelStructureSuite.dll') $contents -Force
    Copy-Item (Join-Path $outDir 'SteelStructureSuite.pdb') $contents -Force
    Copy-Item (Join-Path $root $t.Manifest) (Join-Path $bundleDir 'PackageContents.xml') -Force
    Copy-Item (Join-Path $root 'docs\UserManual.md') (Join-Path $contents 'Help') -Force
    Copy-Item (Join-Path $root 'LICENSE') (Join-Path $contents 'Help\UserManual-license.txt') -Force

    Write-Host "已生成 dist\$($t.Bundle)" -ForegroundColor Green

    if ($Install) {
        $dest = Join-Path 'C:\Program Files\Autodesk\ApplicationPlugins' $t.Bundle
        Stop-Acad
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
        if (Test-Path (Join-Path $dest 'Contents')) { Remove-Item (Join-Path $dest 'Contents') -Recurse -Force }
        Copy-Item $contents (Join-Path $dest 'Contents') -Recurse -Force
        Copy-Item (Join-Path $bundleDir 'PackageContents.xml') $dest -Force
        Write-Host "已安装到 $dest" -ForegroundColor Green
    }
}

# 打包 zip
$zip = Join-Path $root 'dist\SteelStructureSuite-bundle-1.0.0.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
$bundleDirs = Get-ChildItem (Join-Path $root 'dist') -Directory -Filter '*.bundle'
Compress-Archive -Path $bundleDirs.FullName -DestinationPath $zip -Force
Write-Host "已打包 $zip" -ForegroundColor Green
