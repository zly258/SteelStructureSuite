using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Windows;
using SteelStructureSuite.Services.Settings;

namespace SteelStructureSuite.Ribbon
{
    /// <summary>
    /// 功能区构建器。
    ///
    /// 支持两种显示模式（由设置项 Ribbon.DisplayMode 控制，可随时切换）：
    ///   · Detailed（详细）：所有构件各占一个按钮，一眼可见、一次点击；
    ///   · Compact （极简）：每个大类折叠成一个下拉菜单，先点大类再选具体构件。
    /// 极简模式把 30 个构件按钮压缩成 6 个菜单，彻底解决功能区分组过多、显示不下的问题。
    /// </summary>
    public static class SteelRibbonBuilder
    {
        private const string TabId = "STEEL_STRUCTURE_SUITE_TAB";
        private const string DisplayModeKey = "Ribbon.DisplayMode";

        /// <summary>功能区显示模式。</summary>
        public enum RibbonDisplayMode
        {
            /// <summary>详细：每个构件一个按钮。</summary>
            Detailed = 0,
            /// <summary>极简：每个大类一个下拉菜单。</summary>
            Compact = 1
        }

        /// <summary>当前显示模式（持久化在本地设置，默认使用极简菜单）。</summary>
        public static RibbonDisplayMode DisplayMode
        {
            get
            {
                int v = UserSettings.GetInt(DisplayModeKey, (int)RibbonDisplayMode.Compact);
                return v == (int)RibbonDisplayMode.Detailed
                    ? RibbonDisplayMode.Detailed
                    : RibbonDisplayMode.Compact;
            }
            set
            {
                UserSettings.SetInt(DisplayModeKey, (int)value);
                UserSettings.Save();
            }
        }

        /// <summary>一个参数化构件条目。</summary>
        private sealed class MemberEntry
        {
            public string Text;
            public string Command;
            public string Tooltip;
            public string Glyph;
        }

        /// <summary>一个大类，含其下所有构件。</summary>
        private sealed class MemberGroup
        {
            public string Title;
            public string Glyph;
            public List<MemberEntry> Members = new List<MemberEntry>();
        }

        /// <summary>参数化构件大类清单（Ribbon 与极简菜单共用同一份数据：单根型钢放第一，区域组合放最后）。</summary>
        private static List<MemberGroup> GetMemberGroups()
        {
            return new List<MemberGroup>
            {
                new MemberGroup
                {
                    Title = "单根型钢", Glyph = "member",
                    Members =
                    {
                        new MemberEntry { Text = "直线型钢", Command = "SS_MEMBER ", Tooltip = "两点绘制直线型钢", Glyph = "member" },
                        new MemberEntry { Text = "弧形型钢", Command = "SS_ARC ", Tooltip = "截面沿圆弧扫掠", Glyph = "arc" },
                        new MemberEntry { Text = "螺旋型钢", Command = "SS_SPIRAL ", Tooltip = "截面沿螺旋线扫掠", Glyph = "helix" },
                    }
                },
                new MemberGroup
                {
                    Title = "厂房框架", Glyph = "portal",
                    Members =
                    {
                        new MemberEntry { Text = "单跨门架", Command = "SS_PORTAL ", Tooltip = "单跨门式刚架", Glyph = "portal" },
                        new MemberEntry { Text = "多跨门架", Command = "SS_MULTIPORTAL ", Tooltip = "多跨连续门式刚架", Glyph = "multiportal" },
                        new MemberEntry { Text = "人字撑架", Command = "SS_BRACED ", Tooltip = "带人字斜撑框架", Glyph = "braced" },
                        new MemberEntry { Text = "双层管廊", Command = "SS_DOUBLE ", Tooltip = "双层管廊支架", Glyph = "double" },
                        new MemberEntry { Text = "装置框架", Command = "SS_SPACEFRAME ", Tooltip = "多层空间装置钢框架", Glyph = "space" },
                        new MemberEntry { Text = "屋面桁架", Command = "SS_TRUSS ", Tooltip = "双坡屋面钢桁架", Glyph = "truss" },
                        new MemberEntry { Text = "管廊桁桥", Command = "SS_PIPEBRIDGE ", Tooltip = "空间管廊桁架桥", Glyph = "bridge" },
                    }
                },
                new MemberGroup
                {
                    Title = "平台梯道", Glyph = "platform",
                    Members =
                    {
                        new MemberEntry { Text = "四柱平台", Command = "SS_PLATFORM ", Tooltip = "四柱空间检修平台", Glyph = "platform" },
                        new MemberEntry { Text = "悬臂托架", Command = "SS_BRACKET ", Tooltip = "悬臂三角托架", Glyph = "bracket" },
                        new MemberEntry { Text = "悬挑走道", Command = "SS_WALKWAY ", Tooltip = "悬挑走道平台（两点绘制）", Glyph = "walkway" },
                        new MemberEntry { Text = "直跑斜梯", Command = "SS_STAIR_STRAIGHT ", Tooltip = "直跑钢斜梯", Glyph = "stair" },
                        new MemberEntry { Text = "螺旋盘梯", Command = "SS_STAIR ", Tooltip = "储罐连续螺旋盘梯", Glyph = "spiral" },
                        new MemberEntry { Text = "护笼直梯", Command = "SS_LADDER ", Tooltip = "GB 4053.1 带安全护笼钢直梯", Glyph = "ladder" },
                        new MemberEntry { Text = "安全栏杆", Command = "SS_HANDRAIL ", Tooltip = "GB 4053.3 平台栏杆", Glyph = "rail" },
                    }
                },
                new MemberGroup
                {
                    Title = "化工设备", Glyph = "tower",
                    Members =
                    {
                        new MemberEntry { Text = "塔器平台", Command = "SS_TOWER_PLAT ", Tooltip = "HG/T 21574 塔器环形平台", Glyph = "tower" },
                        new MemberEntry { Text = "塔器裙座", Command = "SS_SKIRT ", Tooltip = "NB/T 47041 立式塔器裙座", Glyph = "skirt" },
                        new MemberEntry { Text = "管道管托", Command = "SS_PIPESHOE ", Tooltip = "HG/T 21629 管道加劲管托", Glyph = "pipeshoe" },
                        new MemberEntry { Text = "罐顶栏杆", Command = "SS_TANK_RAIL ", Tooltip = "SH/T 3070 罐顶环形栏杆", Glyph = "tank" },
                        new MemberEntry { Text = "容器鞍座", Command = "SS_SADDLE ", Tooltip = "NB/T 47042 卧式容器鞍座", Glyph = "saddle" },
                    }
                },
                new MemberGroup
                {
                    Title = "电力冶金", Glyph = "gantry",
                    Members =
                    {
                        new MemberEntry { Text = "变电构架", Command = "SS_GANTRY ", Tooltip = "DL/T 5218 变电站 A 字构架", Glyph = "gantry" },
                        new MemberEntry { Text = "电缆支架", Command = "SS_CABLE_RACK ", Tooltip = "DL/T 5221 电缆沟多层支架", Glyph = "cable" },
                        new MemberEntry { Text = "重吊车梁", Command = "SS_CRANE_BEAM ", Tooltip = "16G519 重型吊车梁", Glyph = "crane" },
                        new MemberEntry { Text = "柱间支撑", Command = "SS_XBRACE ", Tooltip = "19G518 柱间交叉支撑", Glyph = "xbrace" },
                    }
                },
                new MemberGroup
                {
                    Title = "区域组合", Glyph = "zoneplat",
                    Members =
                    {
                        new MemberEntry { Text = "钢平台成套", Command = "SS_ZONE_PLATFORM ", Tooltip = "柱+梁+板+栏杆+斜梯", Glyph = "zoneplat" },
                        new MemberEntry { Text = "厂房柱列", Command = "SS_ZONE_COLROW ", Tooltip = "柱+支撑+吊车梁+系杆", Glyph = "zonecol" },
                        new MemberEntry { Text = "管廊成套", Command = "SS_ZONE_PIPERACK ", Tooltip = "多层管廊按跨数生成", Glyph = "zonerack" },
                        new MemberEntry { Text = "楼梯成套", Command = "SS_ZONE_STAIR ", Tooltip = "梯梁+踏步+扶手+平台", Glyph = "zonestair" },
                        new MemberEntry { Text = "轻钢厂房成套", Command = "SS_ZONE_HALL ", Tooltip = "多榀门架+屋面檩条+柱间支撑", Glyph = "zonehall" },
                        new MemberEntry { Text = "连廊通廊成套", Command = "SS_ZONE_WALKWAY ", Tooltip = "支墩柱+纵横梁+铺板+双侧栏杆", Glyph = "zonewalk" },
                        new MemberEntry { Text = "设备框架成套", Command = "SS_ZONE_EQFRAME ", Tooltip = "多层多跨设备框架+平台+护栏", Glyph = "zoneeq" },
                        new MemberEntry { Text = "钢雨棚成套", Command = "SS_ZONE_CANOPY ", Tooltip = "多榀立柱+悬挑斜梁+斜撑+檩条", Glyph = "zonecanopy" },
                    }
                },
            };
        }

        /// <summary>检查当前功能区中是否已存在本插件选项卡。</summary>
        public static bool HasRibbonTab()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            return ribbon != null && ribbon.FindTab(TabId) != null;
        }

        /// <summary>切换显示模式并立即重建功能区。</summary>
        public static void ToggleDisplayMode()
        {
            DisplayMode = DisplayMode == RibbonDisplayMode.Detailed
                ? RibbonDisplayMode.Compact
                : RibbonDisplayMode.Detailed;
            BuildOrShowRibbon(activateTab: true);
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

            bool compact = DisplayMode == RibbonDisplayMode.Compact;

            // ---------- 面板 1：构建管理 ----------
            RibbonPanelSource mgrPanel = CreatePanel(tab, "构建管理");
            mgrPanel.Items.Add(CreateLargeButton("模型管理", "SS_MANAGER ", "检索、定位、编辑、删除全图构件", "manager"));
            mgrPanel.Items.Add(CreateLargeButton("截面管理", "SS_SECTIONS ", "管理截面库与国标特性", "section"));

            RibbonRowPanel mgrCol = new RibbonRowPanel();
            mgrCol.Items.Add(CreateSmallButton("位号顺号", "SS_RENUMBER ", "批量统一前缀与顺号", "renumber"));
            mgrCol.Items.Add(CreateRowBreak());
            mgrCol.Items.Add(CreateSmallButton("编辑参数", "SS_EDIT ", "修改选中构件的参数", "edit"));
            mgrCol.Items.Add(CreateRowBreak());
            mgrCol.Items.Add(CreateSmallButton("重置设置", "SS_RESETSETTINGS ", "清除界面记忆", "regen"));
            mgrPanel.Items.Add(mgrCol);

            // ---------- 面板 2：构件模板 ----------
            RibbonPanelSource templatePanel = CreatePanel(tab, "构件模板");
            templatePanel.Items.Add(CreateLargeButton("模板管理", "SS_TEMPLATES ", "重命名、删除、导入导出参数模板", "templ"));
            templatePanel.Items.Add(CreateLargeButton("存为模板", "SS_SAVETEMPLATE ", "把选中构件的参数存成模板", "tplsave"));

            // ---------- 面板 3：参数化构件（按模式生成） ----------
            List<MemberGroup> groups = GetMemberGroups();

            if (compact)
            {
                // 极简：每个大类一个下拉菜单，6 个菜单取代 30 个按钮
                RibbonPanelSource memberPanel = CreatePanel(tab, "参数化构件");
                foreach (MemberGroup g in groups)
                {
                    memberPanel.Items.Add(CreateMemberMenu(g));
                }
            }
            else
            {
                // 详细：每个大类一个面板，每个构件一个按钮（每列最多 3 行，避免面板过高）
                foreach (MemberGroup g in groups)
                {
                    RibbonPanelSource panel = CreatePanel(tab, g.Title);
                    AddSmallButtonColumns(panel, g.Members);
                }
            }

            // ---------- 面板 4：定位复写（每列最多 3 行） ----------
            RibbonPanelSource locatePanel = CreatePanel(tab, "定位复写");
            var locateItems = new List<MemberEntry>
            {
                new MemberEntry { Text = "连续绘制", Command = "SS_LINEAR_CHAIN ", Tooltip = "连续折线绘制栏杆、走道、钢梁等", Glyph = "chain" },
                new MemberEntry { Text = "三维对齐", Command = "SS_ALIGN ", Tooltip = "按 X/Y/Z 或中心对齐", Glyph = "align" },
                new MemberEntry { Text = "阵列复制", Command = "SS_ARRAY ", Tooltip = "定距阵列并递增位号", Glyph = "array" },
                new MemberEntry { Text = "标高归零", Command = "SS_DROPZ0 ", Tooltip = "构件底部归零至 Z=0", Glyph = "drop" },
                new MemberEntry { Text = "开启复写", Command = "SS_OVERRULE_ON ", Tooltip = "开启夹点复写", Glyph = "gripon" },
                new MemberEntry { Text = "关闭复写", Command = "SS_OVERRULE_OFF ", Tooltip = "关闭夹点复写", Glyph = "gripoff" }
            };
            AddSmallButtonColumns(locatePanel, locateItems);

            // ---------- 面板 5：图纸与出图 ----------
            RibbonPanelSource outPanel = CreatePanel(tab, "统计出图");
            outPanel.Items.Add(CreateLargeButton("材料统计", "SS_BOM ", "材料统计并导出 Excel", "bom"));
            outPanel.Items.Add(CreateLargeButton("施工详图", "SS_DRAWING ", "生成 A1~A4 施工详图", "drawing"));
            outPanel.Items.Add(CreateLargeButton("批量出详图", "SS_BATCHDRAW ", "批量出详图，相同构件复用", "drawing"));

            RibbonRowPanel fileCol = new RibbonRowPanel();
            fileCol.Items.Add(CreateSmallButton("批量转DXF", "SS_DWG2DXF ", "批量 DWG 转 DXF", "dxf"));
            fileCol.Items.Add(CreateRowBreak());
            fileCol.Items.Add(CreateSmallButton("合并图纸", "SS_DWGMERGE ", "合并多张 DWG/DXF", "merge"));
            outPanel.Items.Add(fileCol);
            outPanel.Items.Add(CreateLargeButton("图层样式", "SS_SETTINGS ", "配置建模/出图图层、颜色、文字字高与标注样式", "settings"));

            // ---------- 面板 6：帮助（含模式切换） ----------
            RibbonPanelSource helpPanel = CreatePanel(tab, "帮助");
            RibbonRowPanel helpCol = new RibbonRowPanel();
            helpCol.Items.Add(CreateSmallButton("使用说明", "SS_HELP ", "查看完整功能说明", "help"));
            helpCol.Items.Add(CreateRowBreak());
            helpCol.Items.Add(CreateSmallButton(
                compact ? "详细菜单" : "极简菜单",
                "SS_RIBBONMODE ",
                compact ? "当前极简菜单：点击切换为详细菜单（显示全部构件按钮）" : "当前详细菜单：点击切换为极简菜单（按大类折叠构件）",
                "ribbonmode"));
            helpCol.Items.Add(CreateRowBreak());
            helpCol.Items.Add(CreateSmallButton("关于", "SS_ABOUT ", "版本与许可证", "about"));
            helpPanel.Items.Add(helpCol);

            ribbon.Tabs.Add(tab);
            if (activateTab)
            {
                tab.IsActive = true;
            }
        }

        /// <summary>把一个大类做成下拉菜单（极简模式用）。</summary>
        private static RibbonMenuButton CreateMemberMenu(MemberGroup group)
        {
            var menu = new RibbonMenuButton
            {
                Text = group.Title,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = Orientation.Vertical,
                LargeImage = RibbonIconFactory.Create(32, group.Glyph),
                Image = RibbonIconFactory.Create(16, group.Glyph),
                IsSplit = false,
                ToolTip = new RibbonToolTip
                {
                    Title = group.Title,
                    Content = $"展开选择具体构件（{group.Members.Count} 种）"
                }
            };

            foreach (MemberEntry m in group.Members)
            {
                menu.Items.Add(new RibbonMenuItem
                {
                    Text = m.Text,
                    Image = RibbonIconFactory.Create(16, m.Glyph),
                    ShowImage = true,
                    CommandParameter = m.Command,
                    CommandHandler = new CadRibbonCommandHandler(),
                    ToolTip = new RibbonToolTip { Title = m.Text, Content = m.Tooltip }
                });
            }

            return menu;
        }

        /// <summary>
        /// 把一组小按钮按「每列最多 maxRows 行」分列排布，保证面板高度不超过 maxRows 行。
        /// Ribbon 面板高度是按行计价的，超过 3 行会显得臃肿且在小屏上放不下。
        /// </summary>
        private static void AddSmallButtonColumns(RibbonPanelSource panel, IList<MemberEntry> items, int maxRows = 3)
        {
            if (items == null || items.Count == 0) return;

            for (int start = 0; start < items.Count; start += maxRows)
            {
                var col = new RibbonRowPanel();
                int end = Math.Min(start + maxRows, items.Count);
                for (int i = start; i < end; i++)
                {
                    if (i > start) col.Items.Add(CreateRowBreak());
                    MemberEntry m = items[i];
                    col.Items.Add(CreateSmallButton(m.Text, m.Command, m.Tooltip, m.Glyph));
                }
                panel.Items.Add(col);
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
