# SteelStructureSuite · 参数化钢结构套件

面向 **AutoCAD（x64）** 的参数化钢结构建模插件。以 Autodesk Bundle 形式安装后随 AutoCAD 自动加载，在顶部生成「参数化钢结构」功能区，覆盖门式刚架、平台、管廊、桁架、梯道、栏杆、支座，以及**化工、电力、冶金三大行业的 8 类标准参数化组件**，提供快速建模、正交夹点编辑、材料统计与标准图框详图出图。

- 目标框架：.NET Framework 4.8 / WPF
- 适用平台：Windows 10 x64，AutoCAD 2019 (R23.0) ~ 2024 (R24.3)
- 已验证环境：AutoCAD 2019、AutoCAD Plant 3D 2019
- 程序集：`SteelStructureSuite.dll`（AutoCAD 托管程序集由 CAD 自身提供，不随包分发）

---

## 一、主要功能

| 模块 | 能力 |
| --- | --- |
| 参数化建模 | 厂房框架 6 种、平台梯道栏杆 7 种、单根型钢 3 种、化工设备标准件 5 种、电力与冶金标准件 4 种，共 **26 种**参数化构件 |
| 动态放置 | 单点放置（60FPS 实时预览）、两点线性绘制、`SS_LINEAR_CHAIN` 连续折线绘制 |
| 参数编辑 | 双击实体进入参数窗口；正交夹点复写（长度/高度/宽度/原点独立拉伸，互不干扰） |
| 截面管理 | H 型钢、槽钢、角钢、方矩管、圆钢管 5 类截面库，支持 Excel 导入导出 |
| 模型管理 | 全图构件树状分类 + 属性表，双击定位、高亮、编辑、出图、删除 |
| 材料统计 | 全图或框选统计零件明细，导出 `.xlsx`，或插入 CAD 汇总表 / 构件零件明细表 |
| 施工详图 | A1~A4 国标图框，自动匹配标准比例并按实测视图范围排版，构件轮廓消隐，轴测图 `#1/#2...` 件号引线，平面/立面标准尺寸；可生成到当前图纸或独立 DWG |

---

## 二、环境要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 x64（安装包最低要求 10.0） |
| AutoCAD | 2019 ~ 2024（64 位），.NET 加载；高版本需用对应版本的托管程序集重新编译（见「四、编译」） |
| 运行时 | .NET Framework 4.8 |
| 编译 | Visual Studio 2019/2022，MSBuild（`Debug| x64` 或 `Release| x64`） |
| 打包 | [Inno Setup 6.x](https://jrsoftware.org/isinfo.php)（含简体中文语言文件） |

> 编译时需要本机安装 AutoCAD，工程通过 `HintPath` 引用其 `AcCoreMgd.dll`、`AcDbMgd.dll`、`AcMgd.dll`、`AdWindows.dll`，这些引用均设置为 `Private=False`，不会复制到输出目录。
> 引用路径由 MSBuild 属性 `AutoCADDir` 控制，默认 `C:\Program Files\Autodesk\AutoCAD 2019`。
>
> **版本兼容性说明**：AutoCAD 托管程序集的版本号随产品版本变化（2019 为 `23.0.0.0`，2021 为 `24.0.0.0`，AdWindows 亦然），且 AutoCAD 未提供通用的绑定重定向。因此面向 2021 及以上版本使用时，请用对应版本的 `AutoCADDir` 重新编译一份 DLL，不要直接沿用 2019 版二进制。
>
> **切换 `AutoCADDir` 后请先 `/t:Clean` 再 `/t:Rebuild`**：切换引用版本后 `obj` 中的中间产物会与新引用不一致，可能导致 WPF 标记编译报错 `RG1000 已添加了具有相同键的项`。

---

## 三、目录结构

```text
SteelStructureSuite/
├─ SteelStructureSuite.sln
├─ README.md
├─ CHANGELOG.md
├─ CONTRIBUTING.md
├─ LICENSE                           # MIT 许可证
├─ docs/
│  └─ UserManual.md                  # 详细使用说明
├─ .github/
│  ├─ ISSUE_TEMPLATE/                # 问题与需求模板
│  └─ pull_request_template.md
├─ installer/
│  ├─ PackageContents.xml            # AutoCAD Bundle 自动加载清单
│  └─ SteelStructureSuite.iss        # Inno Setup 安装脚本
└─ SteelStructureSuite/
   ├─ SteelStructureSuite.csproj
   ├─ Core/                          # 插件入口、生命周期、复写注册
   ├─ Commands/                      # AutoCAD 命令层（SS_*）
   ├─ Domain/                        # 枚举与领域模型
   ├─ Ribbon/                        # 顶部功能区构建
   ├─ Services/
   │  ├─ Catalog/                    # 截面库
   │  ├─ Modeling/                   # 三维实体构建与参数持久化
   │  ├─ Interaction/                # Jig 动态放置、夹点复写、右键菜单
   │  ├─ Takeoff/                    # 算量与 Excel 导出
   │  └─ Drawing/                    # HLR 消隐详图
   └─ Views/                         # WPF 窗口（含「关于」窗口）与示意图控件
```

---

## 四、编译

1. 使用 Visual Studio 打开 `SteelStructureSuite.sln`。
2. 选择配置 **`Release | x64`**（或 `Debug | x64`）。
3. 生成解决方案，输出：

   ```text
   SteelStructureSuite\bin\x64\Release\SteelStructureSuite.dll
   ```

命令行方式（推荐，可指定 AutoCAD 版本）：

```bash
# 默认使用 C:\Program Files\Autodesk\AutoCAD 2019 的托管程序集
msbuild SteelStructureSuite.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64

# 面向 AutoCAD 2021 及以上版本重新编译
msbuild SteelStructureSuite.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64 ^
        /p:AutoCADDir="C:\Program Files\Autodesk\AutoCAD 2021"
```

> ⚠️ **请务必使用 `/t:Rebuild`（或先清理 `bin` / `obj`）**。
> 本工程输出路径为 `bin\x64\Release`，MSBuild 会依据时间戳做增量判断：当输出 DLL 比源文件新时会**跳过编译**并仍报告“生成成功”，导致部署了旧二进制却毫无察觉。
> 校验产物是否包含最新命令：`rg -a -o "SS_[A-Z_]{3,}" SteelStructureSuite\bin\x64\Release\SteelStructureSuite.dll`。

> `EnableDefaultCompileItems` 已关闭，新增 `.cs` / `.xaml` 文件时必须同步写入 `.csproj` 的 `<Compile>` / `<Page>` 节点。

---

## 五、安装与部署

### 方式 A：安装包（推荐）

1. 编译 `Release | x64`。
2. 用 Inno Setup 编译 `installer\SteelStructureSuite.iss`（或执行 `iscc.exe /DAppVersion=1.0.0 installer\SteelStructureSuite.iss`）。
3. 运行 `dist\SteelStructureSuite-1.0.0-setup`，安装包会：
   - 将 `PackageContents.xml` 写入 `C:\Program Files\Autodesk\ApplicationPlugins\SteelStructureSuite.bundle\`；
   - 将 `SteelStructureSuite.dll` 写入该 Bundle 的 `Contents\`；
   - 将使用说明写入 Bundle 的 `Contents\Help\` 及 `%ProgramData%\SteelStructureSuite\`。
4. 重启 AutoCAD，插件自动加载并激活「参数化钢结构」选项卡。

安装目录约定：

```text
C:\Program Files\Autodesk\ApplicationPlugins\SteelStructureSuite.bundle\
├─ PackageContents.xml
└─ Contents\
   ├─ SteelStructureSuite.dll
   └─ Help\
      └─ UserManual.md
```

> 安装需要管理员权限；升级安装会先清理用户级同名 Bundle 与目标 `Contents`，避免双份 DLL 或旧版残留。

### 方式 B：手工 NETLOAD（开发调试）

1. AutoCAD 命令行输入 `NETLOAD`，选择 `SteelStructureSuite.dll`。
2. 输入 `SS_RIBBON` 显示功能区，或直接执行 `SS_MANAGER` 等命令。

> 手工加载不会自动注册 Bundle，重启 CAD 后需重新 `NETLOAD`。

### 方式 C：手工放置 Bundle

将 `installer\PackageContents.xml` 与 `Release` 输出复制为上面的目录结构，重启 AutoCAD 即可自动加载。

---

## 六、命令一览

| 命令 | 说明 |
| --- | --- |
| `SS_RIBBON` | 显示/激活「参数化钢结构」功能区 |
| `SS_HELP` | 打开内置使用说明 |
| `SS_ABOUT` | 打开「关于」窗口（版本、版权、MIT 许可证，含完整许可证查看） |
| `SS_MANAGER` | 全图模型管理器（树状分类、定位高亮、编辑/出图/删除） |
| `SS_SECTIONS` | 截面库管理（5 类截面，Excel 导入导出） |
| `SS_BOM` | 材料统计，导出 Excel，或插入 CAD 汇总表 / 构件零件明细表 |
| `SS_DRAWING` | 生成 A1~A4 标准图框消隐详图 |
| `SS_PANEL` | 打开参数化构件建模工作台 |
| `SS_PORTAL` | 单跨门式刚架 |
| `SS_MULTIPORTAL` | 多跨门式刚架 |
| `SS_BRACED` | 人字斜撑框架 |
| `SS_DOUBLE` | 双层管廊支架 |
| `SS_SPACEFRAME` | 多层空间装置框架 |
| `SS_BRACKET` | 悬臂三角托架 |
| `SS_PLATFORM` | 四柱空间平台 |
| `SS_WALKWAY` | 悬挑检修走道 |
| `SS_TRUSS` | 双坡屋面桁架 |
| `SS_PIPEBRIDGE` | 空间管廊桁桥 |
| `SS_STAIR` | 螺旋盘梯 |
| `SS_STAIR_STRAIGHT` | 直跑钢斜梯 |
| `SS_LADDER` | 护笼钢直梯（GB 4053.1） |
| `SS_HANDRAIL` | 安全栏杆 |
| `SS_MEMBER` | 任意两点绘制单根型钢 |
| `SS_ARC` | 两点绘制弧形单根型钢（第一点为圆心，第二点定半径与起始方向） |
| `SS_SPIRAL` | 两点绘制螺旋单根型钢（第一点到第二点为轴线） |
| `SS_SADDLE` | 卧式容器鞍式支座（NB/T 47042） |
| `SS_LINEAR_CHAIN` | 连续折线绘制栏杆/走道/钢梁 |
| `SS_EDIT` | 编辑选中构件参数（双击实体亦可） |
| `SS_ALIGN` | 多构件 X/Y/Z/中心对齐 |
| `SS_ARRAY` | 定距阵列复制并递增位号 |
| `SS_DROPZ0` | 构件底部归零至 Z=0 |
| `SS_OVERRULE_ON` | 开启参数化夹点复写 |
| `SS_OVERRULE_OFF` | 关闭参数化夹点复写 |

### 行业标准组件命令（化工 / 电力 / 冶金）

| 命令 | 构件 | 参照标准 | 绘制方式 |
| --- | --- | --- | --- |
| `SS_TOWER_PLAT` | 塔器环形检修平台 | HG/T 21543《圆形塔平台通用图》、GB 4053.3 | 单点，60°~360° 包角 |
| `SS_SKIRT` | 立式塔器钢裙座 | NB/T 47041《塔式容器》 | 单点 |
| `SS_PIPESHOE` | 化工管道加劲管托 | HG/T 21629《管架标准图》 | 单点 / 两点定向 |
| `SS_TANK_RAIL` | 储罐拱顶环形安全栏杆 | SH/T 3007、GB 4053.3 | 单点，60°~360° 包角 |
| `SS_GANTRY` | 变电站 A 字型龙门构架 | DL/T 5218《220kV~750kV变电站设计技术规程》 | 单点 |
| `SS_CABLE_RACK` | 电力电缆沟多层支架 | DL/T 5221《城市电力电缆线路设计技术规定》 | 两点 / 连续多段线性 |
| `SS_CRANE_BEAM` | 冶金厂房重型吊车梁系统 | 20G520-1~2《钢吊车梁(6m~9m)》 | 两点线性 |
| `SS_XBRACE` | 冶金厂房柱间交叉支撑 | 19G518-3《门式刚架轻型房屋钢结构(有吊车)》 | 两点线性 |

各构件的参数含义、构造细节与材料清单组成详见 [docs/UserManual.md](docs/UserManual.md) 第六章。

---

## 七、打包发布

```bash
# 1. 生成 Release 产物（务必 Rebuild，见「四、编译」的增量陷阱说明）
msbuild SteelStructureSuite.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64

# 2. 生成安装包（Inno Setup 6）
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DAppVersion=1.0.0 installer\SteelStructureSuite.iss
```

输出：`dist\SteelStructureSuite-1.0.0-setup.exe`

脚本要点（`installer\SteelStructureSuite.iss`）：

- `RuntimeDir` 优先取 `bin\x64\Release`，不存在时回退 `Debug`；
- `AppId` / `ProductCode` / `UpgradeCode` 已固定，请勿随版本修改，否则无法覆盖升级；
- 安装到 `{commonpf}\Autodesk\ApplicationPlugins\SteelStructureSuite.bundle`（64 位 AutoCAD 只扫描该目录与用户级同名目录）；
- 卸载时由 Inno 记录删除文件，并清理空的 Bundle 目录。

---

## 八、常见问题

**Q1. 安装后 AutoCAD 没有自动加载？**
- 确认 Bundle 位于 `C:\Program Files\Autodesk\ApplicationPlugins\SteelStructureSuite.bundle\`，且 `PackageContents.xml` 与 `Contents\SteelStructureSuite.dll` 均在位；
- 检查 AutoCAD 的 `SECURELOAD` 是否为 0/1，或将 `Program Files\Autodesk\ApplicationPlugins` 加入受信任路径；
- 用 `NETLOAD` 手工加载一次，观察命令行报错（常见为 .NET 版本或 AutoCAD 版本不匹配）。

**Q2. 功能区没有出现？**
执行 `SS_RIBBON`；若仍无，查看命令行是否提示初始化异常（通常是 WPF 资源或权限问题）。

**Q3. 安装时提示文件被占用？**
安装前关闭 AutoCAD。DLL 被 `acad.exe` 加载后无法覆盖，安装包启用了 `CloseApplications`，但建议手动退出。

**Q4. 支持 AutoCAD 2021 及以上吗？**
Bundle 清单的版本区间已放宽为 `SeriesMin=R23.0`、`SeriesMax=R24.3`，因此 2021 ~ 2024 会正常扫描并加载本插件。
但**托管程序集需按版本分别编译**：AutoCAD 2021 提供 `AcCoreMgd 24.0.0.0`，与 2019 的 `23.0.0.0` 主版本不同，且 AutoCAD 未提供通用绑定重定向。面向高版本请执行：

```bash
msbuild SteelStructureSuite.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64 ^
        /p:AutoCADDir="C:\Program Files\Autodesk\AutoCAD 2021"
```

本仓库随附的二进制为 **AutoCAD 2019 / Plant 3D 2019** 编译版本。

**Q5. 双击实体没有弹出编辑窗口？**
确认 `SS_OVERRULE_ON` 已开启且对象是由本插件创建（参数保存在实体 XData/扩展字典中）。

---

## 九、开发约定

- 所有 AutoCAD 命令集中在 `Commands/`，命令名统一 `SS_` 前缀；
- 新增命令后需同步：
  1. 在 `PackageContents.xml` 的 `<Commands>` 中登记（Bundle 按需加载）；
  2. 在 `Ribbon/SteelRibbonBuilder.cs` 中添加按钮；
  3. 在本 README 命令表中补充说明。
- 新增构件类型后还需同步：`Domain/Enums/ParametricStructureType.cs`（枚举与分类）、`Domain/Models/SteelStructureParams.cs`（显示名、分类与预设）、`Services/Modeling/SteelStructureBuilder.cs`（几何构造）、`Services/Interaction/SteelGripOverrule.cs`（夹点复写）、`Views/Controls/StructureSchematicControl.cs`（示意图）、`Views/StructureEditWindow.xaml.cs`（参数界面）；
- 构件若引用国家/行业标准，**务必核对标准号与标准全称**（错误的标准号是开源后最容易被指出的问题），并在 `docs/UserManual.md` 同步登记；
- 提交信息采用 [Conventional Commits](https://www.conventionalcommits.org/) 规范，示例：
  `feat(components): 新增塔器环形检修平台等 8 类行业标准组件`、`fix(drawing): 修复出图服务 Vector2d/Point2d 类型误用`、`docs(readme): 更新支持版本区间`；
- 参数持久化由 `Services/Modeling/SteelDataStorage.cs` 负责，编辑与算量均依赖该数据；
- WPF 窗口统一通过 `Application.ShowModalWindow` 显示，保持 AutoCAD 模态消息循环兼容。

贡献流程详见 [CONTRIBUTING.md](CONTRIBUTING.md)，版本变更记录见 [CHANGELOG.md](CHANGELOG.md)。

---

## 十、标准与版权声明

- 本项目为**标准构件的参数化几何实现**：依据公开标准与标准图集所规定的构件形式、比例关系与构造要求进行参数化建模，**不包含标准原文、标准图集原图或任何第三方图纸内容**，与标准编制单位、标准出版单位无任何隶属或代理关系。
- 构件几何为工程近似模型（用于快速方案建模与算量），**不得直接用于施工图**；正式工程请依据现行标准原文与专业设计校核。
- 引用标准的名称与编号以官方发布版本为准；本项目在文档中标注的标准号供检索参考，如有勘误以官方原文为准。

---

## 十一、许可证

本项目以 **MIT License** 发布，完整条款见仓库根目录 [LICENSE](LICENSE)。

```text
Copyright (c) 2026 zhangly1403

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- 再分发时请保留 `LICENSE` 文件及源码/安装包中的版权声明；
- 安装包启动时会展示 MIT 许可协议页面（`LicenseFile`），同时将许可证释放到 `Contents\Help\LICENSE.txt` 与 `%ProgramData%\SteelStructureSuite\LICENSE.txt`；
- 运行命令 `SS_ABOUT` 或点击 Ribbon「统计出图 → 关于」可查看版本与完整 MIT 许可证条款。

Copyright © 2026 zhangly1403 · zhangly1403@qq.com
