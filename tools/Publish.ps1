# ============================================================================================
#  SteelStructureSuite 发布脚本（PowerShell 7 / pwsh）
#
#  背景：
#    AutoCAD 的托管程序集（AcDbMgd / AcCoreMgd / AcMgd / AdWindows）版本随 CAD 版本变化：
#      - 2019~2020: Series R23 (.NET Framework 4.8)
#      - 2021~2024: Series R24 (.NET Framework 4.8)
#      - 2025~2026: Series R25 (.NET 8.0 CoreCLR)
#
#  方案：
#    采用 Autodesk 官方推荐的单一多版本自适应 Bundle 架构（SteelStructureSuite.bundle）：
#    统一部署到 %ProgramData%\Autodesk\ApplicationPlugins\SteelStructureSuite.bundle\
#    Contents\ 下按年份分别存放对应程序集（2019、2021、2026），由 PackageContents.xml
#    根据当前运行的 CAD 版本自适应加载匹配模块。
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

# 各大版本编译目标
$targets = @(
    @{ Key = 'R23'; SubDir = '2019'; Series = 'AutoCAD 2019~2020 (R23)'; AutoCADDir = 'C:\Program Files\Autodesk\AutoCAD 2019'; TargetFramework = 'net48' },
    @{ Key = 'R24'; SubDir = '2021'; Series = 'AutoCAD 2021~2024 (R24)'; AutoCADDir = 'C:\Program Files\Autodesk\AutoCAD 2021'; TargetFramework = 'net48' },
    @{ Key = 'R25'; SubDir = '2026'; Series = 'AutoCAD 2025~2026 (R25)'; AutoCADDir = 'C:\Program Files\Autodesk\AutoCAD 2026'; TargetFramework = 'net8.0-windows' }
)

$InstallRoot = Join-Path $env:ProgramData 'Autodesk\ApplicationPlugins'
$bundleName = 'SteelStructureSuite.bundle'
$bundleDir = Join-Path $root ("dist\" + $bundleName)
$contents = Join-Path $bundleDir 'Contents'

function Remove-LegacyBundles {
    $legacyRoots = @(
        'C:\Program Files\Autodesk\ApplicationPlugins',
        'C:\Program Files (x86)\Autodesk\ApplicationPlugins',
        (Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins')
    )
    $allNames = @(
        'SteelStructureSuite.bundle',
        'SteelStructureSuite2021.bundle',
        'SteelStructureSuite2026.bundle'
    )
    foreach ($legacyRoot in $legacyRoots) {
        foreach ($b in $allNames) {
            $legacy = Join-Path $legacyRoot $b
            if (Test-Path -LiteralPath $legacy) {
                Write-Host ("清理旧路径残留包: " + $legacy) -ForegroundColor Yellow
                Remove-Item -LiteralPath $legacy -Recurse -Force
            }
        }
    }
    # 清理公共插件目录下带版本号的旧包
    foreach ($b in @('SteelStructureSuite2021.bundle', 'SteelStructureSuite2026.bundle')) {
        $old = Join-Path $InstallRoot $b
        if (Test-Path -LiteralPath $old) {
            Write-Host ("清理旧版本残留包: " + $old) -ForegroundColor Yellow
            Remove-Item -LiteralPath $old -Recurse -Force
        }
    }
}

function Stop-Acad {
    $procs = Get-Process -Name acad, accoreconsole -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host '检测到 AutoCAD / 控制台正在运行，正在结束进程...' -ForegroundColor Yellow
        $procs | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
}

# 0. 重建 dist 下的 Bundle 目录，彻底清除历史残留文件
foreach ($ob in @($bundleDir, (Join-Path $root 'dist\SteelStructureSuite2021.bundle'), (Join-Path $root 'dist\SteelStructureSuite2026.bundle'))) {
    if (Test-Path -LiteralPath $ob) {
        Remove-Item -LiteralPath $ob -Recurse -Force
    }
}
New-Item -ItemType Directory -Force -Path $contents | Out-Null

# 1. 编译各大版本
foreach ($t in $targets) {
    $acadDir = $t.AutoCADDir
    if (-not (Test-Path (Join-Path $acadDir 'AcDbMgd.dll'))) {
        Write-Warning "跳过 $($t.Key)：未找到 $acadDir\AcDbMgd.dll"
        continue
    }

    $outDir = Join-Path $root ("SteelStructureSuite\bin\x64\Release_" + $t.Key)
    $tfm = $t.TargetFramework
    if (-not $SkipBuild) {
        Write-Host "编译 $($t.Key) ($tfm) → $acadDir" -ForegroundColor Cyan
        & $msbuild $solution /t:Rebuild /p:Configuration=Release /p:Platform=x64 "/p:AutoCADDir=$acadDir" "/p:TargetFramework=$tfm" "/p:OutputPath=$outDir" /v:minimal /nologo
        if ($LASTEXITCODE -ne 0) { throw "编译失败：$($t.Key)" }
    }

    # 复制到统一 Bundle 下的对应年份子目录
    $targetContents = Join-Path $contents $t.SubDir
    New-Item -ItemType Directory -Force -Path $targetContents | Out-Null
    Copy-Item (Join-Path $outDir 'SteelStructureSuite.dll') $targetContents -Force
    Copy-Item (Join-Path $outDir 'SteelStructureSuite.pdb') $targetContents -Force
    if (Test-Path (Join-Path $outDir 'SteelStructureSuite.deps.json')) {
        Copy-Item (Join-Path $outDir 'SteelStructureSuite.deps.json') $targetContents -Force
    }
}

# 2. 组装通用资源与清单
$helpDir = Join-Path $contents 'Help'
New-Item -ItemType Directory -Force -Path $helpDir | Out-Null
Copy-Item (Join-Path $root 'installer\PackageContents.xml') (Join-Path $bundleDir 'PackageContents.xml') -Force
Copy-Item (Join-Path $root 'docs\UserManual.md') $helpDir -Force
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $helpDir 'UserManual-license.txt') -Force
Write-Host "已生成统一架构插件包 dist\$bundleName" -ForegroundColor Green

# 3. 安装到本机（如果指定 -Install）
if ($Install) {
    Stop-Acad
    Remove-LegacyBundles

    $dest = Join-Path $InstallRoot $bundleName
    if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item $contents (Join-Path $dest 'Contents') -Recurse -Force
    Copy-Item (Join-Path $bundleDir 'PackageContents.xml') $dest -Force
    Write-Host "已安装到 $dest (自适应支持 2019~2026)" -ForegroundColor Green
}

# 4. 打包绿色 zip 包（仅包含统一的 SteelStructureSuite.bundle）
$obsoleteBundles = @(Join-Path $root 'dist\SteelStructureSuite2021.bundle', Join-Path $root 'dist\SteelStructureSuite2026.bundle')
foreach ($ob in $obsoleteBundles) {
    if (Test-Path $ob) { Remove-Item $ob -Recurse -Force }
}

$zip = Join-Path $root 'dist\SteelStructureSuite-bundle-1.0.0.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $bundleDir -DestinationPath $zip -Force
Write-Host "已打包绿色分发包 $zip" -ForegroundColor Green

# 5. 打包 Inno Setup 单文件安装包
$iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
if (Test-Path $iscc) {
    Write-Host "检测到 Inno Setup 6，正在生成安装包..." -ForegroundColor Cyan
    $issFile = Join-Path $root 'installer\SteelStructureSuite.iss'
    & $iscc $issFile
    if ($LASTEXITCODE -eq 0) {
        Write-Host "已生成安装包 setup.exe" -ForegroundColor Green
    } else {
        Write-Warning "Inno Setup 打包失败，错误码: $LASTEXITCODE"
    }
}
