using System;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Windows;

namespace SteelStructureSuite.Ribbon
{
    public static class SteelRibbonBuilder
    {
        private const string TabId = "STEEL_STRUCTURE_SUITE_TAB";

        /// <summary>检查当前功能区中是否已存在本插件选项卡。</summary>
        public static bool HasRibbonTab()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            return ribbon != null && ribbon.FindTab(TabId) != null;
        }

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
            mgrPanel.Items.Add(CreateLargeButton("模型管理", "SS_MANAGER ", "检索、定位、编辑、删除全图构件", "manager"));
            mgrPanel.Items.Add(CreateLargeButton("位号顺号", "SS_RENUMBER ", "批量统一前缀与顺号", "renumber"));
            mgrPanel.Items.Add(CreateLargeButton("截面管理", "SS_SECTIONS ", "管理截面库与国标特性", "section"));
            mgrPanel.Items.Add(CreateLargeButton("编辑参数", "SS_EDIT ", "修改选中构件的参数与截面", "edit"));
            mgrPanel.Items.Add(CreateLargeButton("重置设置", "SS_RESETSETTINGS ", "清除界面记忆，不修改截面库", "regen"));

            // Panel 2: 厂房框架
            // Panel 1.5: 构件模板（可保存/加载/删除参数模板，替代写死的轻中重预设）
            RibbonPanelSource templatePanel = CreatePanel(tab, "构件模板");
            templatePanel.Items.Add(CreateLargeButton("模板管理", "SS_TEMPLATES ", "重命名、删除、导入导出参数模板", "templ"));
            templatePanel.Items.Add(CreateLargeButton("存为模板", "SS_SAVETEMPLATE ", "把选中构件的参数存成模板", "tplsave"));

            RibbonPanelSource framePanel = CreatePanel(tab, "厂房框架");
            RibbonRowPanel fCol1 = new RibbonRowPanel();
            fCol1.Items.Add(CreateSmallButton("单跨门架", "SS_PORTAL ", "单跨门式刚架", "portal"));
            fCol1.Items.Add(CreateRowBreak());
            fCol1.Items.Add(CreateSmallButton("多跨门架", "SS_MULTIPORTAL ", "多跨连续门式刚架", "multiportal"));
            fCol1.Items.Add(CreateRowBreak());
            fCol1.Items.Add(CreateSmallButton("人字撑架", "SS_BRACED ", "带人字斜撑框架", "braced"));
            framePanel.Items.Add(fCol1);

            RibbonRowPanel fCol2 = new RibbonRowPanel();
            fCol2.Items.Add(CreateSmallButton("双层管廊", "SS_DOUBLE ", "双层管廊支架", "double"));
            fCol2.Items.Add(CreateRowBreak());
            fCol2.Items.Add(CreateSmallButton("装置框架", "SS_SPACEFRAME ", "多层空间装置钢框架", "space"));
            fCol2.Items.Add(CreateRowBreak());
            fCol2.Items.Add(CreateSmallButton("屋面桁架", "SS_TRUSS ", "双坡屋面钢桁架", "truss"));
            framePanel.Items.Add(fCol2);

            // Panel 3: 区域组合（一次布置一整片区域，内部复用单构件构建器）
            RibbonPanelSource zonePanel = CreatePanel(tab, "区域组合");
            RibbonRowPanel zCol1 = new RibbonRowPanel();
            zCol1.Items.Add(CreateSmallButton("钢平台成套", "SS_ZONE_PLATFORM ", "柱+梁+板+栏杆+斜梯", "zoneplat"));
            zCol1.Items.Add(CreateRowBreak());
            zCol1.Items.Add(CreateSmallButton("厂房柱列", "SS_ZONE_COLROW ", "柱+支撑+吊车梁+系杆", "zonecol"));
            zonePanel.Items.Add(zCol1);

            RibbonRowPanel zCol2 = new RibbonRowPanel();
            zCol2.Items.Add(CreateSmallButton("管廊成套", "SS_ZONE_PIPERACK ", "多层管廊按跨数生成", "zonerack"));
            zCol2.Items.Add(CreateRowBreak());
            zCol2.Items.Add(CreateSmallButton("楼梯成套", "SS_ZONE_STAIR ", "梯梁+踏步+扶手+平台", "zonestair"));
            zonePanel.Items.Add(zCol2);

            // Panel 3.5: 连续绘制（唯一入口，不再按构件类型重复放按钮）
            RibbonPanelSource chainPanel = CreatePanel(tab, "连续绘制");
            chainPanel.Items.Add(CreateLargeButton("连续绘制", "SS_LINEAR_CHAIN ", "连续折线绘制栏杆、走道、电缆架、钢梁等", "chain"));

            // Panel 4: 平台梯道
            RibbonPanelSource platformStairPanel = CreatePanel(tab, "平台梯道");
            RibbonRowPanel pCol1 = new RibbonRowPanel();
            pCol1.Items.Add(CreateSmallButton("四柱平台", "SS_PLATFORM ", "四柱空间检修平台", "platform"));
            pCol1.Items.Add(CreateRowBreak());
            pCol1.Items.Add(CreateSmallButton("悬臂托架", "SS_BRACKET ", "悬臂三角托架", "bracket"));
            pCol1.Items.Add(CreateRowBreak());
            pCol1.Items.Add(CreateSmallButton("悬挑走道", "SS_WALKWAY ", "悬挑走道平台（支持两点绘制）", "walkway"));
            platformStairPanel.Items.Add(pCol1);

            RibbonRowPanel pCol2 = new RibbonRowPanel();
            pCol2.Items.Add(CreateSmallButton("管廊桁桥", "SS_PIPEBRIDGE ", "空间管廊桁架桥（支持两点绘制）", "bridge"));
            pCol2.Items.Add(CreateRowBreak());
            pCol2.Items.Add(CreateSmallButton("直跑斜梯", "SS_STAIR_STRAIGHT ", "直跑钢斜梯", "stair"));
            pCol2.Items.Add(CreateRowBreak());
            pCol2.Items.Add(CreateSmallButton("护笼直梯", "SS_LADDER ", "GB 4053.1 带安全护笼钢直梯", "ladder"));
            platformStairPanel.Items.Add(pCol2);

            RibbonRowPanel pCol3 = new RibbonRowPanel();
            pCol3.Items.Add(CreateSmallButton("螺旋盘梯", "SS_STAIR ", "储罐连续螺旋盘梯", "spiral"));
            pCol3.Items.Add(CreateRowBreak());
            pCol3.Items.Add(CreateSmallButton("安全栏杆", "SS_HANDRAIL ", "GB 4053.3 平台栏杆", "rail"));
            platformStairPanel.Items.Add(pCol3);

            // Panel 4: 化工设备 (HG/T, NB/T, SH/T)
            RibbonPanelSource chemPanel = CreatePanel(tab, "化工设备");
            RibbonRowPanel cCol1 = new RibbonRowPanel();
            cCol1.Items.Add(CreateSmallButton("塔器平台", "SS_TOWER_PLAT ", "HG/T 21574 塔器环形平台", "tower"));
            cCol1.Items.Add(CreateRowBreak());
            cCol1.Items.Add(CreateSmallButton("塔器裙座", "SS_SKIRT ", "NB/T 47041 立式塔器裙座", "skirt"));
            cCol1.Items.Add(CreateRowBreak());
            cCol1.Items.Add(CreateSmallButton("管道管托", "SS_PIPESHOE ", "HG/T 21629 管道加劲管托", "shoe"));
            chemPanel.Items.Add(cCol1);

            RibbonRowPanel cCol2 = new RibbonRowPanel();
            cCol2.Items.Add(CreateSmallButton("罐顶栏杆", "SS_TANK_RAIL ", "SH/T 3070 罐顶环形栏杆", "tank"));
            cCol2.Items.Add(CreateRowBreak());
            cCol2.Items.Add(CreateSmallButton("容器鞍座", "SS_SADDLE ", "NB/T 47042 卧式容器鞍座", "saddle"));
            chemPanel.Items.Add(cCol2);

            // Panel 5: 电力冶金 (DL/T, 16G519, 19G518)
            RibbonPanelSource powerMetaPanel = CreatePanel(tab, "电力冶金");
            RibbonRowPanel pmCol1 = new RibbonRowPanel();
            pmCol1.Items.Add(CreateSmallButton("变电构架", "SS_GANTRY ", "DL/T 5218 变电站 A 字构架", "gantry"));
            pmCol1.Items.Add(CreateRowBreak());
            pmCol1.Items.Add(CreateSmallButton("电缆支架", "SS_CABLE_RACK ", "DL/T 5221 电缆沟多层支架", "cable"));
            powerMetaPanel.Items.Add(pmCol1);

            RibbonRowPanel pmCol2 = new RibbonRowPanel();
            pmCol2.Items.Add(CreateSmallButton("重吊车梁", "SS_CRANE_BEAM ", "16G519 重型吊车梁", "crane"));
            pmCol2.Items.Add(CreateRowBreak());
            pmCol2.Items.Add(CreateSmallButton("柱间支撑", "SS_XBRACE ", "19G518 柱间交叉支撑", "xbrace"));
            powerMetaPanel.Items.Add(pmCol2);

            // Panel 6: 单根型钢与参数编辑
            RibbonPanelSource memberPanel = CreatePanel(tab, "单根型钢");
            RibbonRowPanel mCol = new RibbonRowPanel();
            mCol.Items.Add(CreateSmallButton("直线型钢", "SS_MEMBER ", "两点绘制直线型钢梁、柱或斜撑", "member"));
            mCol.Items.Add(CreateRowBreak());
            mCol.Items.Add(CreateSmallButton("弧形型钢", "SS_ARC ", "选择已有圆弧，截面沿弧线扫掠", "arc"));
            mCol.Items.Add(CreateRowBreak());
            mCol.Items.Add(CreateSmallButton("螺旋型钢", "SS_SPIRAL ", "选择已有螺旋线，截面沿螺旋扫掠", "helix"));
            memberPanel.Items.Add(mCol);

            // Panel 7: 定位复写与统计出图
            RibbonPanelSource locatePanel = CreatePanel(tab, "定位复写");
            RibbonRowPanel alignCol = new RibbonRowPanel();
            alignCol.Items.Add(CreateSmallButton("三维对齐", "SS_ALIGN ", "多构件按 X/Y/Z 或中心对齐", "align"));
            alignCol.Items.Add(CreateRowBreak());
            alignCol.Items.Add(CreateSmallButton("阵列复制", "SS_ARRAY ", "定距阵列复制钢结构并自动递增位号", "array"));
            alignCol.Items.Add(CreateRowBreak());
            alignCol.Items.Add(CreateSmallButton("标高归零", "SS_DROPZ0 ", "构件底部一键归零至 Z=0", "drop"));
            locatePanel.Items.Add(alignCol);

            RibbonRowPanel overruleCol = new RibbonRowPanel();
            overruleCol.Items.Add(CreateSmallButton("开启复写", "SS_OVERRULE_ON ", "开启夹点复写", "gripon"));
            overruleCol.Items.Add(CreateRowBreak());
            overruleCol.Items.Add(CreateSmallButton("关闭复写", "SS_OVERRULE_OFF ", "关闭夹点复写", "gripoff"));
            locatePanel.Items.Add(overruleCol);

            RibbonPanelSource filePanel = CreatePanel(tab, "图纸工具");
            filePanel.Items.Add(CreateLargeButton("批量转DXF", "SS_DWG2DXF ", "批量 DWG 转 DXF", "dxf"));
            filePanel.Items.Add(CreateLargeButton("合并图纸", "SS_DWGMERGE ", "合并多张 DWG/DXF", "merge"));

            RibbonPanelSource outPanel = CreatePanel(tab, "统计出图");
            outPanel.Items.Add(CreateLargeButton("材料统计", "SS_BOM ", "材料统计并导出 Excel", "bom"));
            outPanel.Items.Add(CreateLargeButton("施工详图", "SS_DRAWING ", "生成 A1~A4 施工详图", "drawing"));
            outPanel.Items.Add(CreateLargeButton("批量出详图", "SS_BATCHDRAW ", "批量出详图，相同构件复用", "drawing"));
            outPanel.Items.Add(CreateLargeButton("使用说明", "SS_HELP ", "查看插件完整功能与命令使用说明", "help"));
            outPanel.Items.Add(CreateLargeButton("关于", "SS_ABOUT ", "查看版本、版权与 MIT 许可证信息", "about"));

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
            string glyph)
        {
            return new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = Orientation.Vertical,
                LargeImage = RibbonIconFactory.Create(32, glyph),
                Image = RibbonIconFactory.Create(16, glyph),
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
            string glyph,
            bool active = false)
        {
            return new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
                Orientation = Orientation.Horizontal,
                Image = RibbonIconFactory.Create(16, glyph, active),
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
