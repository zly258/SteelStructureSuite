# 贡献指南

感谢你关注本项目。以下约定可帮助你的贡献被顺利合入。

## 一、开发环境

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 x64 |
| AutoCAD | 2019 ~ 2024（用于提供 `AcCoreMgd` / `AcDbMgd` / `AcMgd` / `AdWindows` 引用） |
| 运行时 | .NET Framework 4.8 |
| IDE / 编译器 | Visual Studio 2019 或 2022（MSBuild） |
| 打包 | Inno Setup 6.x（可选） |

## 二、分支与提交

- 从 `main` 切出特性分支，命名建议 `feat/<简述>`、`fix/<简述>`、`docs/<简述>`；
- 提交信息采用 [Conventional Commits](https://www.conventionalcommits.org/) 规范：

  ```text
  feat(components): 新增塔器环形检修平台构件
  fix(drawing): 修复出图服务 Vector2d/Point2d 类型误用
  docs(readme): 更新支持版本区间与 AutoCADDir 编译说明
  ```

- 一个提交只做一件事，提交前确认 `git diff` 中不含构建产物、临时文件与本机路径。

## 三、构建

```bash
# 必须使用 Rebuild，避免 MSBuild 依据时间戳跳过编译
msbuild SteelStructureSuite.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64

# 面向其它 AutoCAD 版本（托管程序集版本随产品版本变化，不可混用）
msbuild SteelStructureSuite.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64 ^
        /p:AutoCADDir="C:\Program Files\Autodesk\AutoCAD 2021"
```

校验产物是否包含最新命令：

```bash
rg -a -o "SS_[A-Z_]{3,}" SteelStructureSuite\bin\x64\Release\SteelStructureSuite.dll | sort -u
```

## 四、新增构件类型的检查清单

新增一种参数化构件时，至少需要同步以下位置：

1. `Domain/Enums/ParametricStructureType.cs` —— 枚举项与行业分类注释；
2. `Domain/Models/SteelStructureParams.cs` —— 显示名、`CategoryName` / `CategoryOrder`、工程预设；
3. `Services/Modeling/SteelStructureBuilder.cs` —— 三维几何构造（`Build*Parts`）与分发 `switch`；
4. `Services/Interaction/SteelGripOverrule.cs` —— 夹点复写行为；
5. `Views/Controls/StructureSchematicControl.cs` —— 参数窗口的二维示意图；
6. `Views/StructureEditWindow.xaml.cs` —— 参数编辑界面的字段绑定；
7. `Commands/ModelingCommands.cs` —— `SS_` 命令方法；
8. `Ribbon/SteelRibbonBuilder.cs` —— 功能区按钮；
9. `installer/PackageContents.xml` —— 命令登记；
10. `README.md` / `docs/UserManual.md` —— 构件表、命令表、参数说明。

> `EnableDefaultCompileItems` 已关闭，新增 `.cs` / `.xaml` 文件时必须同步写入 `.csproj` 的 `<Compile>` / `<Page>` 节点。

## 五、引用标准时的要求

- **务必核对标准号与标准全称**（例如 `NB/T 47041《塔式容器》`），错误的标准号是开源后最容易被指出的问题；
- 建议在构件注释与文档中同时给出标准号与全称，便于读者检索；
- 不得复制标准原文、图集原图或第三方图纸内容，本项目只做参数化几何实现；
- 涉及标准的几何应为工程近似模型，需在文档中说明不可直接用于施工图。

## 六、界面与代码风格

- WPF 界面改动请遵循仓库的 UI 规范（紧凑布局、防截断、状态必须同时设置前景与背景色）；
- 注释使用中文，说明「为什么」而非「做了什么」；
- 异常处理避免空 `catch { }` 静默吞掉问题，至少记录到调试输出。

## 七、提交 Pull Request

1. 从 `main` 创建分支并完成修改；
2. 确认本地可编译（`/t:Rebuild`）且功能区按钮、命令、参数界面均可用；
3. 在 PR 描述中写明：修改内容、影响范围、验证方式、关联 Issue；
4. 涉及界面截图请附上；
5. 保持一次 PR 聚焦单一主题，便于评审。

## 八、行为准则

请保持友善、专业、就事论事的交流风格。我们以「对代码本身」为唯一评判对象，尊重不同观点。
