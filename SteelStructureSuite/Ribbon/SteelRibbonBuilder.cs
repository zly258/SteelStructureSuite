using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Windows;

namespace SteelStructureSuite.Ribbon
{
    public static class SteelRibbonBuilder
    {
        private const string TabId = "STEEL_STRUCTURE_SUITE_TAB";

        public static void BuildOrShowRibbon(bool activateTab = true)
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            RibbonTab existingTab = ribbon.FindTab(TabId);
            if (existingTab != null)
                ribbon.Tabs.Remove(existingTab);

            RibbonTab tab = new RibbonTab
            {
                Title = "参数化钢结构",
                Id = TabId,
                IsVisible = true
            };

            // Panel 1: 构建管理
            RibbonPanelSource mgrPanel = CreatePanel(tab, "构建管理");
            mgrPanel.Items.Add(CreateLargeButton(
                "模型管理",
                "SS_MANAGER ",
                "打开全图钢结构树状模型管理器（按行业大类与构件类型分组，支持视角定位、高亮、编辑与删除）",
                Color.FromRgb(16, 124, 65), "M"));
            mgrPanel.Items.Add(CreateLargeButton(
                "截面管理",
                "SS_SECTIONS ",
                "按 H型钢、槽钢、角钢、方矩管、圆钢管 5 个分表管理截面，支持 Excel 多表导入导出",
                Color.FromRgb(0, 103, 192), "H"));

            // Panel 2: 厂房框架
            RibbonPanelSource framePanel = CreatePanel(tab, "厂房框架");
            RibbonRowPanel fCol1 = new RibbonRowPanel();
            fCol1.Items.Add(CreateSmallButton("单跨门架", "SS_PORTAL ", "单跨门式刚架", Color.FromRgb(0, 103, 192)));
            fCol1.Items.Add(CreateRowBreak());
            fCol1.Items.Add(CreateSmallButton("多跨门架", "SS_MULTIPORTAL ", "多跨连续门式刚架", Color.FromRgb(0, 103, 192)));
            fCol1.Items.Add(CreateRowBreak());
            fCol1.Items.Add(CreateSmallButton("人字撑架", "SS_BRACED ", "带人字斜撑框架", Color.FromRgb(0, 103, 192)));
            framePanel.Items.Add(fCol1);

            RibbonRowPanel fCol2 = new RibbonRowPanel();
            fCol2.Items.Add(CreateSmallButton("双层管廊", "SS_DOUBLE ", "双层管廊支架", Color.FromRgb(0, 103, 192)));
            fCol2.Items.Add(CreateRowBreak());
            fCol2.Items.Add(CreateSmallButton("装置框架", "SS_SPACEFRAME ", "多层空间装置钢框架", Color.FromRgb(0, 103, 192)));
            fCol2.Items.Add(CreateRowBreak());
            fCol2.Items.Add(CreateSmallButton("屋面桁架", "SS_TRUSS ", "双坡屋面钢桁架", Color.FromRgb(0, 103, 192)));
            framePanel.Items.Add(fCol2);

            // Panel 3: 平台梯道
            RibbonPanelSource platformStairPanel = CreatePanel(tab, "平台梯道");
            platformStairPanel.Items.Add(CreateLargeButton(
                "连续绘制",
                "SS_LINEAR_CHAIN ",
                "连续多段线性动态绘制安全栏杆、走道、电缆支架或钢梁",
                Color.FromRgb(196, 89, 17), "L"));

            RibbonRowPanel pCol1 = new RibbonRowPanel();
            pCol1.Items.Add(CreateSmallButton("四柱平台", "SS_PLATFORM ", "四柱空间检修钢平台", Color.FromRgb(16, 124, 65)));
            pCol1.Items.Add(CreateRowBreak());
            pCol1.Items.Add(CreateSmallButton("悬臂托架", "SS_BRACKET ", "悬臂三角托架", Color.FromRgb(16, 124, 65)));
            pCol1.Items.Add(CreateRowBreak());
            pCol1.Items.Add(CreateSmallButton("悬挑走道", "SS_WALKWAY ", "悬挑走道平台（支持两点绘制）", Color.FromRgb(16, 124, 65)));
            platformStairPanel.Items.Add(pCol1);

            RibbonRowPanel pCol2 = new RibbonRowPanel();
            pCol2.Items.Add(CreateSmallButton("管廊桁桥", "SS_PIPEBRIDGE ", "空间管廊桁架桥（支持两点绘制）", Color.FromRgb(16, 124, 65)));
            pCol2.Items.Add(CreateRowBreak());
            pCol2.Items.Add(CreateSmallButton("直跑斜梯", "SS_STAIR_STRAIGHT ", "直跑钢斜梯（支持两点绘制）", Color.FromRgb(196, 89, 17)));
            pCol2.Items.Add(CreateRowBreak());
            pCol2.Items.Add(CreateSmallButton("护笼直梯", "SS_LADDER ", "GB 4053.1 带安全护笼钢直梯", Color.FromRgb(196, 89, 17)));
            platformStairPanel.Items.Add(pCol2);

            RibbonRowPanel pCol3 = new RibbonRowPanel();
            pCol3.Items.Add(CreateSmallButton("螺旋盘梯", "SS_STAIR ", "储罐连续螺旋盘梯", Color.FromRgb(196, 89, 17)));
            pCol3.Items.Add(CreateRowBreak());
            pCol3.Items.Add(CreateSmallButton("安全栏杆", "SS_HANDRAIL ", "GB 4053.3 平台安全栏杆（支持两点绘制）", Color.FromRgb(196, 89, 17)));
            platformStairPanel.Items.Add(pCol3);

            // Panel 4: 化工设备 (HG/T, NB/T, SH/T)
            RibbonPanelSource chemPanel = CreatePanel(tab, "化工设备");
            RibbonRowPanel cCol1 = new RibbonRowPanel();
            cCol1.Items.Add(CreateSmallButton("塔器平台", "SS_TOWER_PLAT ", "HG/T 21543《圆形塔平台通用图》塔器环形检修平台（带扇形牛腿与外环护栏）", Color.FromRgb(180, 70, 20)));
            cCol1.Items.Add(CreateRowBreak());
            cCol1.Items.Add(CreateSmallButton("塔器裙座", "SS_SKIRT ", "NB/T 47041《塔式容器》立式塔器钢裙座（含地脚螺栓底环板与检查孔）", Color.FromRgb(180, 70, 20)));
            cCol1.Items.Add(CreateRowBreak());
            cCol1.Items.Add(CreateSmallButton("管道管托", "SS_PIPESHOE ", "HG/T 21629《管架标准图》化工管道加劲管托（含双管卡与加劲肋）", Color.FromRgb(180, 70, 20)));
            chemPanel.Items.Add(cCol1);

            RibbonRowPanel cCol2 = new RibbonRowPanel();
            cCol2.Items.Add(CreateSmallButton("罐顶栏杆", "SS_TANK_RAIL ", "SH/T 3007 储罐拱顶环形安全栏杆（GB 4053.3 栏杆高度）", Color.FromRgb(180, 70, 20)));
            cCol2.Items.Add(CreateRowBreak());
            cCol2.Items.Add(CreateSmallButton("容器鞍座", "SS_SADDLE ", "NB/T 47042《卧式容器》卧式容器鞍式支座", Color.FromRgb(180, 70, 20)));
            chemPanel.Items.Add(cCol2);

            // Panel 5: 电力冶金 (DL/T, 20G520, 19G518-3)
            RibbonPanelSource powerMetaPanel = CreatePanel(tab, "电力冶金");
            RibbonRowPanel pmCol1 = new RibbonRowPanel();
            pmCol1.Items.Add(CreateSmallButton("变电构架", "SS_GANTRY ", "DL/T 5218《220kV~750kV变电站设计技术规程》A 字型钢管龙门构架与格构横梁", Color.FromRgb(110, 86, 207)));
            pmCol1.Items.Add(CreateRowBreak());
            pmCol1.Items.Add(CreateSmallButton("电缆支架", "SS_CABLE_RACK ", "DL/T 5221《城市电力电缆线路设计技术规定》电力电缆沟多层角钢支架（支持两点绘制）", Color.FromRgb(110, 86, 207)));
            powerMetaPanel.Items.Add(pmCol1);

            RibbonRowPanel pmCol2 = new RibbonRowPanel();
            pmCol2.Items.Add(CreateSmallButton("重吊车梁", "SS_CRANE_BEAM ", "20G520-1~2《钢吊车梁(6m~9m)》重型焊接吊车梁、制动走道花纹板与重轨（支持两点绘制）", Color.FromRgb(110, 86, 207)));
            pmCol2.Items.Add(CreateRowBreak());
            pmCol2.Items.Add(CreateSmallButton("柱间支撑", "SS_XBRACE ", "19G518-3《门式刚架轻型房屋钢结构(有吊车)》柱间上下柱双片十字交叉支撑（支持两点绘制）", Color.FromRgb(110, 86, 207)));
            powerMetaPanel.Items.Add(pmCol2);

            // Panel 6: 单根型钢与参数编辑
            RibbonPanelSource memberPanel = CreatePanel(tab, "单根型钢");
            RibbonRowPanel mCol = new RibbonRowPanel();
            mCol.Items.Add(CreateSmallButton("直线型钢", "SS_MEMBER ", "两点绘制直线型钢梁、柱或斜撑", Color.FromRgb(0, 120, 212)));
            mCol.Items.Add(CreateRowBreak());
            mCol.Items.Add(CreateSmallButton("弧形型钢", "SS_ARC ", "两点绘制圆弧扫掠型钢", Color.FromRgb(0, 120, 212)));
            mCol.Items.Add(CreateRowBreak());
            mCol.Items.Add(CreateSmallButton("螺旋型钢", "SS_SPIRAL ", "两点绘制螺旋扫掠型钢", Color.FromRgb(0, 120, 212)));
            memberPanel.Items.Add(mCol);
            memberPanel.Items.Add(CreateLargeButton(
                "编辑参数",
                "SS_EDIT ",
                "选中已有三维钢结构修改参数",
                Color.FromRgb(138, 43, 226), "E"));

            // Panel 7: 定位复写与统计出图
            RibbonPanelSource locatePanel = CreatePanel(tab, "定位复写");
            RibbonRowPanel alignCol = new RibbonRowPanel();
            alignCol.Items.Add(CreateSmallButton("三维对齐", "SS_ALIGN ", "多构件按 X/Y/Z 或中心对齐", Color.FromRgb(0, 120, 212)));
            alignCol.Items.Add(CreateRowBreak());
            alignCol.Items.Add(CreateSmallButton("阵列复制", "SS_ARRAY ", "定距阵列复制钢结构并自动递增位号", Color.FromRgb(0, 120, 212)));
            alignCol.Items.Add(CreateRowBreak());
            alignCol.Items.Add(CreateSmallButton("标高归零", "SS_DROPZ0 ", "构件底部一键归零至 Z=0", Color.FromRgb(0, 120, 212)));
            locatePanel.Items.Add(alignCol);

            RibbonRowPanel overruleCol = new RibbonRowPanel();
            overruleCol.Items.Add(CreateSmallButton("开启复写", "SS_OVERRULE_ON ", "开启参数化夹点复写 (SS_OVERRULE_ON)", Color.FromRgb(16, 124, 65)));
            overruleCol.Items.Add(CreateRowBreak());
            overruleCol.Items.Add(CreateSmallButton("关闭复写", "SS_OVERRULE_OFF ", "关闭参数化夹点复写 (SS_OVERRULE_OFF)", Color.FromRgb(180, 50, 50)));
            locatePanel.Items.Add(overruleCol);

            RibbonPanelSource outPanel = CreatePanel(tab, "统计出图");
            outPanel.Items.Add(CreateLargeButton(
                "材料统计",
                "SS_BOM ",
                "统计全图或框选钢结构材料，导出 Excel (.xlsx) 或插入 CAD 表",
                Color.FromRgb(196, 89, 17), "BOM"));
            outPanel.Items.Add(CreateLargeButton(
                "施工详图",
                "SS_DRAWING ",
                "生成标准 A1~A4 图框消隐详图（支持当前文件或新建独立 DWG）",
                Color.FromRgb(138, 43, 226), "HLR"));
            outPanel.Items.Add(CreateLargeButton(
                "使用说明",
                "SS_HELP ",
                "查看插件完整功能与命令使用说明",
                Color.FromRgb(0, 120, 212), "?"));
            outPanel.Items.Add(CreateLargeButton(
                "关于",
                "SS_ABOUT ",
                "查看版本、版权与 MIT 许可证信息",
                Color.FromRgb(95, 99, 104), "i"));

            ribbon.Tabs.Add(tab);
            if (activateTab)
            {
                tab.IsActive = true;
            }
        }

        private static RibbonPanelSource CreatePanel(RibbonTab tab, string title)
        {
            RibbonPanelSource source = new RibbonPanelSource { Title = title };
            RibbonPanel panel = new RibbonPanel { Source = source };
            tab.Panels.Add(panel);
            return source;
        }

        private static RibbonButton CreateLargeButton(
            string text,
            string command,
            string tooltip,
            Color badgeColor,
            string badgeLabel)
        {
            return new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = Orientation.Vertical,
                LargeImage = CreateBadgeIcon(32, badgeColor, badgeLabel),
                Image = CreateBadgeIcon(16, badgeColor, badgeLabel),
                CommandParameter = command,
                CommandHandler = new CadRibbonCommandHandler(),
                ToolTip = new RibbonToolTip
                {
                    Title = text,
                    Content = tooltip,
                    Command = command.Trim()
                }
            };
        }

        private static RibbonButton CreateSmallButton(
            string text,
            string command,
            string tooltip,
            Color dotColor)
        {
            return new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
                Orientation = Orientation.Horizontal,
                Image = CreateBadgeIcon(16, dotColor, ""),
                CommandParameter = command,
                CommandHandler = new CadRibbonCommandHandler(),
                ToolTip = new RibbonToolTip
                {
                    Title = text,
                    Content = tooltip,
                    Command = command.Trim()
                }
            };
        }

        private static RibbonRowBreak CreateRowBreak() => new RibbonRowBreak();

        private static BitmapSource CreateBadgeIcon(int size, Color bgColor, string text)
        {
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                SolidColorBrush brush = new SolidColorBrush(bgColor);
                Pen pen = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1.0);
                dc.DrawRoundedRectangle(brush, pen, new System.Windows.Rect(1, 1, size - 2, size - 2), 4, 4);

                if (size >= 28 && !string.IsNullOrEmpty(text))
                {
                    Pen whitePen = new Pen(Brushes.White, 2.2);
                    dc.DrawLine(whitePen, new System.Windows.Point(7, 8), new System.Windows.Point(size - 7, 8));
                    dc.DrawLine(whitePen, new System.Windows.Point(size / 2.0, 8), new System.Windows.Point(size / 2.0, size - 8));
                    dc.DrawLine(whitePen, new System.Windows.Point(7, size - 8), new System.Windows.Point(size - 7, size - 8));
                }
                else
                {
                    dc.DrawEllipse(Brushes.White, null, new System.Windows.Point(size / 2.0, size / 2.0), 3.0, 3.0);
                }
            }

            RenderTargetBitmap bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        private class CadRibbonCommandHandler : System.Windows.Input.ICommand
        {
            public event EventHandler CanExecuteChanged
            {
                add { }
                remove { }
            }

            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter)
            {
                string cmd = (parameter as RibbonCommandItem)?.CommandParameter as string ?? parameter as string;
                if (string.IsNullOrWhiteSpace(cmd)) return;

                Document doc = Application.DocumentManager.MdiActiveDocument;
                doc?.SendStringToExecute(cmd, true, false, true);
            }
        }
    }
}
