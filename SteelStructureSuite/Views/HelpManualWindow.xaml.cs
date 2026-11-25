using System.Windows;
using SteelStructureSuite.Services.Settings;

namespace SteelStructureSuite.Views
{
    public partial class HelpManualWindow : Window
    {
        public HelpManualWindow()
        {
            InitializeComponent();
            TxtManual.Text =
@"【一、插件架构与概览】
本插件为 AutoCAD 2019 ~ 2026 独立三维参数化钢结构套件，采用标准分层架构（Core / Commands / Domain / Services / Ribbon / Views XAML），内置 5 大类 24 种型钢截面库（热轧标准型钢、焊接与组合型钢、冷弯薄壁型钢、空心钢管、实心型材）、30 种参数化钢结构（含化工、电力、冶金行业标准组件与区域组合构件）、动态预览放置与线性绘制、正交夹点拉伸、全图树状模型管理器、MTO 材料统计、A1~A4 标准图框详图与批量出图。

【二、核心功能与命令】
1. 构建管理
   - 模型管理 (SS_MANAGER)：左侧按类型树状归类，右侧表格列出构件明细。支持双击或点击【定位高亮】自动缩放视口并高亮选中构件，支持直接编辑、出图与删除。
   - 截面管理 (SS_SECTIONS)：左侧分类树 + 右侧表格 + 2D 实时轮廓预览 + 参数面板。热轧标准型钢内置国标手册截面积/理论米重/表面积，同时支持界面手工修正或一键按公式重算；支持导出/导入 Excel (.xlsx) 或 CSV。
   - 截面数据存储：UTF-8 (带 BOM) CSV，共 14 列（代号、类型、H、B、Tw、Tf、B2、Tf2、卷边C、间隙Gap、截面积、理论单重、表面积、数值来源 Manual/Auto），可直接用 Excel 或文本编辑器查看备份，启动 CAD 时自动加载。需要定位文件时点【打开目录】即可，界面不再显示完整路径。
   - 位号顺号 (SS_RENUMBER)：按类型前缀与编号位数批量重排，支持统一前缀与前后对比。
   - 编辑参数 (SS_EDIT)：修改选中构件的参数与截面。
   - 重置设置 (SS_RESETSETTINGS)：清除界面记忆，恢复各构件初始预设。不修改截面库。

2. 参数模板（取代写死的轻/中/重预设）
   - 模板管理 (SS_TEMPLATES)：重命名、删除、导入导出模板库。
   - 存为模板 (SS_SAVETEMPLATE)：把选中构件的全部参数存成具名模板。
   - 建模窗口内「参数模板」下拉可直接套用，旁边【存为模板】就地填写名称与备注，无需弹窗。
   - 模板库为单个可读 JSON 文件（%LocalAppData%\SteelStructureSuite\Templates.json），原子写入并自动保留 .bak 备份，无需数据库引擎。

3. 参数化构件（30 种，含尺寸动态示意图 + 材料清单 Tab）
   - 厂房框架：单跨门架 (SS_PORTAL)、多跨门架 (SS_MULTIPORTAL)、人字撑架 (SS_BRACED)、双层管廊 (SS_DOUBLE)、装置框架 (SS_SPACEFRAME)、屋面桁架 (SS_TRUSS)、管廊桁桥 (SS_PIPEBRIDGE)
   - 区域组合：钢平台成套 (SS_ZONE_PLATFORM)、厂房柱列 (SS_ZONE_COLROW)、管廊成套 (SS_ZONE_PIPERACK)、楼梯成套 (SS_ZONE_STAIR)
   - 平台梯道：四柱平台 (SS_PLATFORM)、悬臂托架 (SS_BRACKET)、悬挑走道 (SS_WALKWAY)、直跑斜梯 (SS_STAIR_STRAIGHT)、护笼直梯 (SS_LADDER)、螺旋盘梯 (SS_STAIR)、安全栏杆 (SS_HANDRAIL)
   - 单根型钢：直线型钢 (SS_MEMBER)、弧形型钢 (SS_ARC)、螺旋型钢 (SS_SPIRAL)
   - 化工设备标准件：塔器环形检修平台 (SS_TOWER_PLAT)、立式塔器钢裙座 (SS_SKIRT)、化工管道加劲管托 (SS_PIPESHOE)、储罐拱顶环形栏杆 (SS_TANK_RAIL)、容器鞍式支座 (SS_SADDLE)
   - 电力与冶金标准件：变电站 A 字龙门构架 (SS_GANTRY)、电力电缆沟多层支架 (SS_CABLE_RACK)、冶金重型吊车梁 (SS_CRANE_BEAM)、冶金柱间交叉支撑 (SS_XBRACE)
   - 连续绘制 (SS_LINEAR_CHAIN)：像画多段线一样连续拾取折点，生成首尾相连的多段栏杆、走道、钢梁等。

4. 功能区显示模式（解决分组过多显示不下）
   - 详细模式：每个构件一个按钮，一眼可见、一次点击。
   - 极简模式：每个大类折叠成一个下拉菜单，30 个构件按钮压缩为 6 个菜单。
   - 切换方式：命令 SS_RIBBONMODE，或点击功能区【帮助】面板里的【切换到极简/详细】。选择会记住。

5. 正交夹点与复写控制
   - 选中构件后提供正交夹点：原点夹点仅平移、跨度夹点仅改跨度/长度、高度夹点仅改高度、宽度夹点仅改宽度，绝不重置其他参数。
   - 开启复写：SS_OVERRULE_ON  |  关闭复写：SS_OVERRULE_OFF

6. 材料统计与出图
   - 材料统计 (SS_BOM)：导出双工作表 Excel (.xlsx) 或插入 CAD 表。
   - 施工详图 (SS_DRAWING)：仅支持单个构件。校验为带参数的钢构件后弹出出图设置窗；当前图输出时还需拾取标准图框左下角插入点。
   - 出图设置 (SS_DRAWCFG)：设置图幅、比例、各视图、接头局部放大图、输出方式（当前图或新建 DWG）。
   - 批量出详图 (SS_BATCHDRAW)：框选多个构件批量出图。几何、截面、材质完全相同的构件自动复用一张图；可附加图纸目录与汇总材料表；带进度条，可随时取消，已生成的图保留。
   - 批量转 DXF (SS_DWG2DXF)：多选 DWG，按当前 AutoCAD 支持的版本批量导出 DXF。
   - 合并图纸 (SS_DWGMERGE)：按模型空间范围把多张 DWG 或 DXF 横向或纵向排入一个新图纸。";

            UserSettings.AlignWindowToAutoCadScreen(this);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
