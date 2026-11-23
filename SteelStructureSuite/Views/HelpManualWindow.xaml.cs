using System.Windows;

namespace SteelStructureSuite.Views
{
    public partial class HelpManualWindow : Window
    {
        public HelpManualWindow()
        {
            InitializeComponent();
            TxtManual.Text =
@"【一、插件架构与概览】
本插件为 AutoCAD 2019 ~ 2024 独立三维参数化钢结构套件，采用标准分层架构（Core / Commands / Domain / Services / Ribbon / Views XAML），内置 5 类国标型钢分表库、26 种参数化钢结构（含化工、电力、冶金 8 类行业标准组件）、动态预览放置与线性绘制、正交夹点拉伸、全图树状模型管理器、MTO 材料统计及 A1~A4 标准图框详图出图。

【二、核心功能与命令】
1. 构建管理
   - 模型管理 (SS_MANAGER)：左侧按类型树状归类，右侧表格列出构件明细。支持双击或点击【定位高亮】自动缩放视口并高亮选中构件，支持直接编辑、出图与删除。
   - 截面管理 (SS_SECTIONS)：按 H型钢、槽钢、角钢、方矩管、圆钢管 5 个分表管理，支持多工作表 Excel (.xlsx) 或 CSV 导入导出。

2. 单构件专属参数化建模（26 种，含带尺寸动态示意图 + 独立材料清单 Tab）
   - 厂房框架：单跨门架 (SS_PORTAL)、多跨门架 (SS_MULTIPORTAL)、人字撑架 (SS_BRACED)、双层管廊 (SS_DOUBLE)、装置框架 (SS_SPACEFRAME)、屋面桁架 (SS_TRUSS)、管廊桁桥 (SS_PIPEBRIDGE)
   - 平台梯道：四柱平台 (SS_PLATFORM)、悬臂托架 (SS_BRACKET)、悬挑走道 (SS_WALKWAY)、直跑斜梯 (SS_STAIR_STRAIGHT)、护笼直梯 (SS_LADDER)、螺旋盘梯 (SS_STAIR)、安全栏杆 (SS_HANDRAIL)
   - 单根型钢：直线型钢 (SS_MEMBER)、弧形型钢 (SS_ARC)、螺旋型钢 (SS_SPIRAL)
   - 化工设备标准件：塔器环形检修平台 (SS_TOWER_PLAT)、立式塔器钢裙座 (SS_SKIRT)、化工管道加劲管托 (SS_PIPESHOE)、储罐拱顶环形栏杆 (SS_TANK_RAIL)、容器鞍式支座 (SS_SADDLE)
   - 电力与冶金标准件：变电站 A 字龙门构架 (SS_GANTRY)、电力电缆沟多层支架 (SS_CABLE_RACK)、冶金重型吊车梁 (SS_CRANE_BEAM)、冶金柱间交叉支撑 (SS_XBRACE)
   - 梯道栏杆（支持两点线性动态绘制）：连续绘制 (SS_LINEAR_CHAIN)、直跑斜梯 (SS_STAIR_STRAIGHT)、护笼直梯 (SS_LADDER)、螺旋盘梯 (SS_STAIR)、安全栏杆 (SS_HANDRAIL)、悬挑走道 (SS_WALKWAY)、管廊桁桥 (SS_PIPEBRIDGE)、单根型钢 (SS_MEMBER)

3. 正交夹点与复写控制
   - 选中构件后提供正交夹点：原点夹点仅平移、跨度夹点仅改跨度/长度、高度夹点仅改高度、宽度夹点仅改宽度，绝不重置其他参数。
   - 开启复写：SS_OVERRULE_ON  |  关闭复写：SS_OVERRULE_OFF
   - 新建时使用该构件自己的初始预设。截面按构件类型分别记住，不会串到其他构件。
   - 重置设置 (SS_RESETSETTINGS)：清除界面记忆，恢复各构件初始预设。不修改截面库。轴测件号分左右两列等距标注。

4. 材料统计与标准 A1~A4 图框详图
   - 材料统计 (SS_BOM)：导出双工作表 Excel (.xlsx) 或插入 CAD 表。
   - 施工详图 (SS_DRAWING)：仅支持单个构件。先预选一个构件或按提示单击拾取，校验为带参数的钢构件后弹出出图设置窗；当前图输出时还需拾取标准图框左下角插入点。
   - 出图设置 (SS_DRAWCFG)：设置图幅、比例、各视图、输出方式（当前图或新建 DWG）。
   - 批量转 DXF (SS_DWG2DXF)：多选 DWG，按当前 AutoCAD 支持的版本批量导出 DXF。
   - 合并图纸 (SS_DWGMERGE)：按模型空间范围把多张 DWG 或 DXF 横向或纵向排入一个新图纸，输出也可选 DWG 或 DXF。";
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
