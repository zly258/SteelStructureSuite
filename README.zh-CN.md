<h1 align="center">SteelStructureSuite</h1>

<p align="center">面向 AutoCAD (x64) 的参数化钢结构三维建模、材料统计与施工详图套件。</p>

<p align="center">
  <a href="./README.md">English</a> | <a href="./README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  <a href="https://github.com/zly258/SteelStructureSuite/releases/latest"><img src="https://img.shields.io/github/v/release/zly258/SteelStructureSuite?style=flat-square&color=F59E0B&label=Release" alt="Latest Release"></a> <img src="https://img.shields.io/badge/AutoCAD-2019--2026%20x64-3776AB?style=flat-square" alt="AutoCAD 2019-2026 x64"> <img src="https://img.shields.io/badge/.NET-4.8%20%7C%208.0-3776AB?style=flat-square" alt=".NET 4.8 and 8.0"> <img src="https://img.shields.io/badge/Platform-Windows%2010%2F11%20x64-4B5563?style=flat-square" alt="Windows 10/11 x64"> <a href="./docs/UserManual.md"><img src="https://img.shields.io/badge/Docs-User%20Manual-4F46E5?style=flat-square" alt="User Manual"></a> <a href="./LICENSE"><img src="https://img.shields.io/badge/License-GPL--3.0-22C55E?style=flat-square" alt="License: GPL-3.0"></a>
</p>

**SteelStructureSuite（参数化钢结构套件）** 基于 Autodesk ApplicationPlugins Bundle 规范集成至 AutoCAD (x64)，随 AutoCAD 启动自动加载并在顶部生成专属「参数化钢结构」功能区。套件覆盖 26 类参数化钢结构构件，适用于厂房刚架、平台梯道、空间管廊以及化工、电力与冶金行业的标准支承结构。

## 核心亮点

| 亮点能力 | 工程价值 |
|---|---|
| 30 类行业参数化构件 | 涵盖门式刚架、平台、梯道、栏杆、单根型钢、化工/电力/冶金三大行业标准组件，以及钢平台成套、厂房柱列、管廊成套区段、楼梯成套等区域组合构件。 |
| 5 大类 24 种型钢截面库 | 内置 `DefaultSectionCatalog.csv` 共 176 条预置规格，覆盖热轧、焊接/组合、冷弯薄壁、空心钢管与实心型材，支持 2D 实时轮廓预览、手册单重修正及 CSV/Excel 导入导出。 |
| 动态预览与正交夹点编辑 | 支持单点动态落位、两点定向拉伸、`SS_LINEAR_CHAIN` 连续折线绘制，以及独立长宽高正交夹点拉伸与双击实体直接改参。 |
| 全图模型管理与位号顺号 | 通过 `SS_MANAGER` 提供左树右表检索与视口聚焦高亮；通过 `SS_RENUMBER` 支持前缀统一、自定义编号位数与前后对比预览。 |
| 材料统计与 AcBr 原生消隐出图 | 按截面规格、材质与单长合并累重，内置 OpenXML 导出 `.xlsx` 或插入 CAD 表格（`SS_BOM`）；基于 AcBr 实体边界消隐生成国标 A1–A4 施工详图（`SS_DRAWING`）。 |
| 多版本自适应统一 Bundle | 单个 `SteelStructureSuite.bundle` 内置 R23（`2019–2020`，.NET 4.8）、R24（`2021–2024`，.NET 4.8）与 R25（`2025–2026`，.NET 8.0）三个运行时模块，按宿主 AutoCAD 版本自动匹配加载。 |

## 系统架构

```text
┌────────────────────────────────────────────────────────────────────────────┐
│                   AutoCAD / Plant 3D (2019-2026, x64)                      │
│          PackageContents.xml  ──▶  R23 (.NET 4.8) / R24 / R25 (.NET 8)     │
└────────────────────────────────────┬───────────────────────────────────────┘
                                     │ Load on Startup / Command
                                     ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                      Core & Presentation Layer                             │
│  ┌──────────────────────┐   ┌──────────────────────┐   ┌────────────────┐  │
│  │ Core/                │   │ Ribbon/              │   │ Views/ + Theme │  │
│  │ PluginApplication    │──▶│ SteelRibbonBuilder   │   │ WPF Windows &  │  │
│  │ (Overrules, Events)  │   │ RibbonIconFactory    │   │ Preview Ctrls  │  │
│  └──────────┬───────────┘   └──────────┬───────────┘   └───────┬────────┘  │
└─────────────┼──────────────────────────┼───────────────────────┼───────────┘
              │                          ▼                       │
              │             ┌────────────────────────┐           │
              └────────────▶│   Commands/ (SS_*)     │◀──────────┘
                            │ Modeling / Layout /    │
                            │ DrawingExchange        │
                            └────────────┬───────────┘
                                         │
                                         ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                            Services Layer                                  │
│  ┌────────────┐  ┌────────────┐  ┌─────────────┐  ┌──────────┐  ┌───────┐  │
│  │ Catalog    │  │ Modeling   │  │ Interaction │  │ Takeoff  │  │Drawing│  │
│  │ (24 shapes)│  │ (3D Solid) │  │ (Jig/Grips) │  │ (BOM/XLS)│  │(HLR)  │  │
│  └──────┬─────┘  └──────┬─────┘  └──────┬──────┘  └────┬─────┘  └───┬───┘  │
└─────────┼───────────────┼───────────────┼──────────────┼────────────┼──────┘
          └───────────────┴───────┬───────┴──────────────┴────────────┘
                                  ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                         Domain & Data Layer                                │
│  Domain/ (Enums, Models)  ◀───▶  Data/DefaultSectionCatalog.csv (176 rows) │
│  Services/Settings/ (UserSettings & %LocalAppData%\SteelStructureSuite)    │
└────────────────────────────────────────────────────────────────────────────┘
```

## 快速安装

### 环境要求

| 配置项 | 支持版本 | 说明 |
|---|---|---|
| 操作系统 | Windows 10 / 11 x64 | 仅支持 64 位 Windows |
| AutoCAD 平台 | AutoCAD / AutoCAD Plant 3D 2019–2026 (x64) | 对应 `R23.0` 至 `R25.x` 系列 |
| 目标框架 | .NET Framework 4.8 / .NET 8.0 | 由宿主 AutoCAD 自带（`2019–2024` 为 .NET 4.8，`2025–2026` 为 .NET 8.0） |
| 编译工具链（仅源码构建） | Visual Studio 2022 (MSBuild) + PowerShell 7 (`pwsh`) | 如需打包 `.exe` 安装程序，另需 Inno Setup 6.x |

### 方式一：Windows 安装包（推荐）

1. 先关闭所有正在运行的 AutoCAD（`acad.exe`）进程，避免 DLL 被占用。
2. 前往 [GitHub Releases](https://github.com/zly258/SteelStructureSuite/releases/latest) 下载最新版本的 `SteelStructureSuite-1.1.0-setup.exe`。
3. 运行安装向导，插件将自动安装到受 `SECURELOAD=1` 默认信任的系统插件目录：

```text
C:\Program Files\Autodesk\ApplicationPlugins\SteelStructureSuite.bundle\
```

4. 需要卸载时，可通过 Windows「设置 > 应用」或控制面板一键卸载。

### 方式二：绿色免安装 Bundle

从 [GitHub Releases](https://github.com/zly258/SteelStructureSuite/releases/latest) 下载 `SteelStructureSuite-bundle-1.1.0.zip`，解压至 `C:\Program Files\Autodesk\ApplicationPlugins\`，保持如下目录结构：

```text
C:\Program Files\Autodesk\ApplicationPlugins\
└─ SteelStructureSuite.bundle\
   ├─ PackageContents.xml
   └─ Contents\
      ├─ 2019\SteelStructureSuite.dll   # AutoCAD 2019-2020 (R23, .NET 4.8)
      ├─ 2021\SteelStructureSuite.dll   # AutoCAD 2021-2024 (R24, .NET 4.8)
      ├─ 2026\SteelStructureSuite.dll   # AutoCAD 2025-2026 (R25, .NET 8.0)
      ├─ Data\DefaultSectionCatalog.csv
      └─ Help\UserManual.md
```

### 方式三：开发者手工加载（`NETLOAD`）

调试源码时，在 AutoCAD 命令行执行 `NETLOAD`，选中与当前 AutoCAD 版本匹配的编译产物 `SteelStructureSuite.dll`，若顶部选项卡未自动出现，执行 `SS_RIBBON` 激活。

## 快速上手

1. 启动 AutoCAD（2019–2026，64 位）。插件会自动加载、启用夹点与双击复写，并在顶部显示「参数化钢结构」选项卡。若切换工作空间后选项卡未显示，在命令行执行：

```text
SS_RIBBON
```

2. 在功能区点击构件按钮或直接输入命令创建模型，例如单跨门式刚架（`SS_PORTAL`）或四柱空间平台（`SS_PLATFORM`），在模型空间拾取插入点即可生成三维实体。
3. 选中已生成的构件，可直接拖动正交长宽高夹点调整尺寸，或双击实体（及执行 `SS_EDIT`）打开参数编辑窗口修改截面与几何参数。
4. 进行全图管理、材料算量与施工详图输出：

```text
SS_MANAGER    # 树状分类与关键字检索，双击行聚焦高亮构件
SS_RENUMBER   # 按构件类型统一前缀并批量顺号
SS_BOM        # 汇总钢材重量并导出 .xlsx 或插入 CAD 表格
SS_DRAWING    # 生成国标 A1-A4 消隐施工详图
```

## 命令速查

### 综合管理与出图

| 命令 | 说明 |
|---|---|
| `SS_RIBBON` | 重新生成并激活顶部「参数化钢结构」功能区选项卡 |
| `SS_RIBBONMODE` | 在详细（每构件一按钮）与极简（每大类一下拉菜单）两种功能区布局间切换 |
| `SS_MANAGER` | 打开全图模型管理器，支持分类树过滤、关键字搜索、视口居中高亮、参数修改与删除 |
| `SS_RENUMBER` | 按构件类型前缀与编号位数批量顺号重排，支持统一前缀与前后对比预览 |
| `SS_SECTIONS` | 打开 5 大类 24 种型钢截面库，支持 2D 轮廓预览、手册单重修正及 Excel/CSV 导入导出 |
| `SS_BOM` | 框选或全图提取构件材料明细，导出 `.xlsx` 报表或插入 CAD 原生表格 |
| `SS_DRAWING` | 拾取构件生成国标 A1–A4 消隐施工图（正/侧/俯视图、轴测图件号引线、明细表与接头放大图） |
| `SS_BATCHDRAW` | 批量出详图：相同构件自动复用图纸，附图纸目录与汇总材料表，带进度条可随时取消 |
| `SS_DRAWCFG` | 配置详图图幅、比例、投影视图与输出目标（当前图纸或独立新 DWG） |
| `SS_HELP` | 打开插件内置使用说明窗口 |
| `SS_ABOUT` | 查看版本、版权与 GNU GPL v3.0 许可证信息 |

### 参数模板

模板取代了原先写死的「轻 / 中 / 重」预设：任意构件配好参数后可存成具名模板，随时套用。
模板库是单个可读的 JSON 文件，位于 `%LocalAppData%\SteelStructureSuite\Templates.json`，
采用原子写入并自动保留 `.bak` 备份，可直接用文本编辑器查看、备份或同事间共享，无需数据库引擎。

| 命令 | 作用 |
|---|---|
| `SS_TEMPLATES` | 模板管理窗口：重命名、删除、导入导出整个模板库 |
| `SS_SAVETEMPLATE` | 把选中构件的全部参数保存为具名模板 |
| `SS_APPLYTEMPLATE` | 把模板参数套用到选中构件 |

### 图纸批处理工具

| 命令 | 说明 |
|---|---|
| `SS_DWGMERGE` | 批量合并多张 DWG/DXF 图纸，按实际边界自动计算间距并水平或垂直排布 |
| `SS_DWG2DXF` | 使用独立子进程将 DWG 批量转换为指定 AutoCAD 版本的 DXF 文件 |

### 定位、连续绘制与夹点辅助

| 命令 | 说明 |
|---|---|
| `SS_LINEAR_CHAIN` | 连续折线绘制安全栏杆、悬挑走道或连续直线型钢 |
| `SS_EDIT` | 修改选中构件的参数与截面（也可直接双击实体唤起） |
| `SS_ALIGN` | 将多个构件沿 X、Y、Z 轴或包围盒中心快速对齐 |
| `SS_ARRAY` | 沿指定轴向定距阵列并自动递增构件位号 |
| `SS_DROPZ0` | 将选中构件底标高整体归零对齐至 `Z = 0` 平面 |
| `SS_OVERRULE_ON` | 启用自主正交参数夹点复写（默认开启） |
| `SS_OVERRULE_OFF` | 关闭参数夹点复写，回退为原生夹点 |
| `SS_RESETSETTINGS` | 清除本地界面记忆，恢复默认预设值 |

### 30 种参数化构件清单

| 行业大类 | 构件名称 | 命令 | 参照标准 / 图集 | 绘制模式 |
|---|---|---|---|---|
| 厂房与装置框架 | 单跨门式刚架 | `SS_PORTAL` | GB 51022 | 单点放置 |
| | 多跨门式刚架 | `SS_MULTIPORTAL` | GB 51022 | 单点放置 |
| | 人字斜撑框架 | `SS_BRACED` | GB 50017 | 单点放置 |
| | 双层管廊支架 | `SS_DOUBLE` | HG/T 20670 | 单点放置 |
| | 多层空间装置框架 | `SS_SPACEFRAME` | GB 50017 | 单点放置 |
| | 双坡屋面桁架 | `SS_TRUSS` | 05G515 | 单点放置 |
| 平台梯道与栏杆 | 四柱空间平台 | `SS_PLATFORM` | GB 50017、GB 4053 | 单点放置 |
| | 悬臂三角托架 | `SS_BRACKET` | GB 50017 | 单点放置 |
| | 悬挑检修走道 | `SS_WALKWAY` | GB 4053.3 | 单点 / 两点 / 连续折线 |
| | 空间管廊桁桥 | `SS_PIPEBRIDGE` | HG/T 21629 | 单点 / 两点 / 连续折线 |
| | 螺旋盘梯 | `SS_STAIR` | GB 4053.2 | 单点放置 |
| | 直跑钢斜梯 | `SS_STAIR_STRAIGHT` | GB 4053.2 | 单点 / 两点定向 |
| | 护笼钢直梯 | `SS_LADDER` | GB 4053.1 | 单点放置 |
| | 安全栏杆 | `SS_HANDRAIL` | GB 4053.3 | 两点 / 连续折线 |
| 化工设备标准件 | 塔器环形检修平台 | `SS_TOWER_PLAT` | HG/T 21543、GB 4053.3 | 单点（60°–360° 包角） |
| | 立式塔器钢裙座 | `SS_SKIRT` | NB/T 47041 | 单点放置 |
| | 化工管道加劲管托 | `SS_PIPESHOE` | HG/T 21629 | 单点 / 两点定向 |
| | 储罐拱顶环形安全栏杆 | `SS_TANK_RAIL` | SH/T 3007、GB 4053.3 | 单点（60°–360° 包角） |
| | 卧式容器鞍式支座 | `SS_SADDLE` | NB/T 47042 | 单点放置 |
| 电力与冶金标准件 | 变电站 A 字型龙门构架 | `SS_GANTRY` | DL/T 5218 | 单点放置 |
| | 电力电缆沟多层支架 | `SS_CABLE_RACK` | DL/T 5221 | 两点 / 连续折线 |
| | 冶金厂房重型吊车梁系统 | `SS_CRANE_BEAM` | 20G520-1~2 | 两点线性 |
| | 冶金厂房柱间交叉支撑 | `SS_XBRACE` | 19G518-3 | 两点线性 |
| 单根型钢构件 | 单根直线型钢 | `SS_MEMBER` | GB/T 11263 | 三维两点拾取 |
| | 单根弧形型钢 | `SS_ARC` | 沿圆弧/圆/多段线扫掠 | 拾取路径图元 |
| | 单根螺旋型钢 | `SS_SPIRAL` | 沿 AutoCAD `HELIX` 扫掠 | 拾取螺旋线图元 |
| 区域组合构件 | 钢平台成套（柱+主次梁+花纹板+栏杆+斜梯） | `SS_ZONE_PLATFORM` | GB 50017 / GB 4053.3 | 单点放置 |
| | 厂房柱列（柱+柱间支撑+吊车梁+系杆） | `SS_ZONE_COLROW` | GB 50017 / 16G519 | 单点放置 |
| | 管廊成套区段（多层按跨数生成） | `SS_ZONE_PIPERACK` | HG/T 20670 | 单点放置 |
| | 楼梯成套（梯梁+踏步+扶手+休息平台） | `SS_ZONE_STAIR` | GB 4053.1 | 单点放置 |

## 源码编译与打包

克隆仓库：

```bash
git clone https://github.com/zly258/SteelStructureSuite.git
cd SteelStructureSuite
```

使用 MSBuild 编译指定 AutoCAD 版本（请始终使用 `/t:Rebuild` 避免增量编译缓存残留）：

```powershell
# AutoCAD 2019-2020 (R23, net48)
msbuild SteelStructureSuite.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64 `
        /p:AutoCADDir="C:\Program Files\Autodesk\AutoCAD 2019" /p:TargetFramework=net48

# AutoCAD 2021-2024 (R24, net48)
msbuild SteelStructureSuite.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64 `
        /p:AutoCADDir="C:\Program Files\Autodesk\AutoCAD 2021" /p:TargetFramework=net48

# AutoCAD 2025-2026 (R25, net8.0-windows)
msbuild SteelStructureSuite.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64 `
        /p:AutoCADDir="C:\Program Files\Autodesk\AutoCAD 2026" /p:TargetFramework=net8.0-windows
```

使用发布脚本一键编译本机已安装的各版本目标、组装统一 Bundle 并生成 `dist\SteelStructureSuite-bundle-1.1.0.zip`（检测到 Inno Setup 6 时同步生成 `dist\SteelStructureSuite-1.1.0-setup.exe`）：

```powershell
# 仅编译并在 dist\ 目录生成发布产物
pwsh tools\Publish.ps1

# 编译打包并直接安装到 C:\Program Files\Autodesk\ApplicationPlugins\
pwsh tools\Publish.ps1 -Install
```

单独调用 Inno Setup 6 构建安装包：

```powershell
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DAppVersion=1.1.0 installer\SteelStructureSuite.iss
```

## 文档导航

| 主题 | 内容说明 | 文档链接 |
|---|---|---|
| 详细使用说明 | 功能区 9 大面板说明、模型管理、位号顺号、截面库与出图操作指引 | [docs/UserManual.md](./docs/UserManual.md) |
| 版本更新日志 | 各版本新增功能与缺陷修复记录 | [CHANGELOG.md](./CHANGELOG.md) |
| Bundle 加载清单 | AutoCAD R23/R24/R25 运行时自适应配置与命令注册表 | [installer/PackageContents.xml](./installer/PackageContents.xml) |
| 开源贡献指南 | 开发环境、分支策略、Conventional Commits 提交规范与构建要求 | [CONTRIBUTING.md](./CONTRIBUTING.md) |

## 参与贡献

欢迎提交 Issue 与 Pull Request。请从 `main` 分支切出特性或修复分支，遵循 [Conventional Commits](https://www.conventionalcommits.org/zh-hans/) 提交规范，保持单次提交职责单一，且不提交构建产物或本机路径。详见 [CONTRIBUTING.md](./CONTRIBUTING.md)。

## 社区与支持

- **问题反馈与需求建议**：[GitHub Issues](https://github.com/zly258/SteelStructureSuite/issues)
- **版本下载**：[GitHub Releases](https://github.com/zly258/SteelStructureSuite/releases)
- **赞助作者**：[爱发电创作者主页](https://ifdian.net/a/zhangly1403) | [直达支持页](https://ifdian.net/order/create?user_id=3db8e630e6dc11f1b37452540025c377)

### 规范标准与工程免责声明

- 本项目构件模型依据公开的国家标准（GB）、化工/石化行业标准（HG/T、SH/T）、电力行业标准（DL/T）、容器标准（NB/T）及 G 系列标准图集的公开几何构造形式进行参数化实现，不包含任何受版权保护的标准原文、扫描件或第三方专有底图。
- 本项目生成的三维构件几何与材料统计结果仅供方案设计、空间布置、碰撞校核与初步算量参考，不可未经独立结构受力计算与专业工程校审直接用于施工制造。

## 开源许可证

本项目基于 [GNU General Public License v3.0 (GPL-3.0)](./LICENSE) 协议开源。

Copyright (c) 2026 zhangly1403 · [zhangly1403@qq.com](mailto:zhangly1403@qq.com)
