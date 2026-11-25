using System;
using System.Collections.Generic;
using System.Linq;

using System.Windows;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using SteelStructureSuite.Core;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Ribbon;
using SteelStructureSuite.Services.Drawing;
using SteelStructureSuite.Services.Interaction;
using SteelStructureSuite.Services.Modeling;

using SteelStructureSuite.Services.Settings;
using SteelStructureSuite.Services.Templates;
using SteelStructureSuite.Services.Takeoff;
using SteelStructureSuite.Views;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(SteelStructureSuite.Commands.ModelingCommands))]

namespace SteelStructureSuite.Commands
{
    public class ModelingCommands
    {
        private static int _tagSeq = 101;

        [CommandMethod("SS_RIBBON", CommandFlags.Modal)]
        public void ShowSteelRibbon()
        {
            SteelRibbonBuilder.BuildOrShowRibbon(activateTab: true);
            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                "\n[SteelStructureSuite] 顶部 [参数化钢结构] 选项卡已激活。\n");
        }

        /// <summary>在「详细」与「极简」功能区布局之间切换。</summary>
        [CommandMethod("SS_RIBBONMODE", CommandFlags.Modal)]
        public void ToggleRibbonDisplayMode()
        {
            SteelRibbonBuilder.ToggleDisplayMode();

            string mode = SteelRibbonBuilder.DisplayMode == SteelRibbonBuilder.RibbonDisplayMode.Compact
                ? "极简（每个大类一个下拉菜单）"
                : "详细（每个构件一个按钮）";

            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                $"\n[SteelStructureSuite] 功能区已切换为：{mode}\n");
        }

        [CommandMethod("SS_OVERRULE_ON", CommandFlags.Modal)]
        public void TurnOverruleOn()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            PluginApplication.EnableOverrules();
            doc.Editor.Regen();
            doc.Editor.WriteMessage("\n[SteelStructureSuite] 参数化三维夹点复写已【开启】(SS_OVERRULE_ON)。\n");
        }

        [CommandMethod("SS_OVERRULE_OFF", CommandFlags.Modal)]
        public void TurnOverruleOff()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            PluginApplication.DisableOverrules();
            doc.Editor.Regen();
            doc.Editor.WriteMessage("\n[SteelStructureSuite] 参数化三维夹点复写已【关闭】(SS_OVERRULE_OFF)。\n");
        }


        [CommandMethod("SS_RESETSETTINGS", CommandFlags.Modal)]
        public void ResetUserSettings()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            MessageBoxResult answer = MessageBox.Show(
                "将清除界面记忆，包括窗口位置、各构件上次使用的截面、出图选项和图纸工具选项。\n各构件恢复自己的初始预设。截面库不会改动。\n\n是否继续？",
                "重置设置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            UserSettings.Reset();
            doc?.Editor.WriteMessage("\n[SteelStructureSuite] 界面设置已初始化。下次打开对话框时使用各构件的初始预设。\n");
        }

        [CommandMethod("SS_SECTIONS", CommandFlags.Modal)]
        public void OpenSectionCatalogWindow()
        {
            var win = new SectionCatalogWindow();
            UserSettings.ShowModalWindowOnAutoCad(win);
        }

        [CommandMethod("SS_MANAGER", CommandFlags.Modal)]
        public void OpenStructureManagerWindow()
        {
            var win = new StructureManagerWindow();
            UserSettings.ShowModalWindowOnAutoCad(win);
        }

        [CommandMethod("SS_RENUMBER", CommandFlags.Modal)]
        public void OpenStructureRenumberWindow()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var records = new List<StructureRecordItem>();
            Database db = doc.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity ent &&
                        SteelDataStorage.TryReadParams(ent, out SteelStructureParams p))
                    {
                        records.Add(new StructureRecordItem
                        {
                            EntityId = id,
                            Params = p
                        });
                    }
                }
                tr.Commit();
            }

            records = records
                .OrderBy(r => r.Params.CategoryOrder)
                .ThenBy(r => r.Params.StructureType)
                .ThenBy(r => r.Tag)
                .ToList();

            var win = new StructureRenumberWindow(records);
            if (UserSettings.ShowModalWindowOnAutoCad(win) == true)
            {
                doc.Editor.WriteMessage($"\n[SteelStructureSuite] 位号顺号完成，共更新 {win.UpdatedCount} 座构件位号。\n");
            }
        }

        [CommandMethod("SS_HELP", CommandFlags.Modal)]
        public void OpenHelpManualWindow()
        {
            var win = new HelpManualWindow();
            UserSettings.ShowModalWindowOnAutoCad(win);
        }

        [CommandMethod("SS_ABOUT", CommandFlags.Modal)]
        public void OpenAboutWindow()
        {
            var win = new AboutWindow();
            UserSettings.ShowModalWindowOnAutoCad(win);
        }

        [CommandMethod("SS_PANEL", CommandFlags.Modal)]
        public void OpenParametricWorkbench()
        {
            CreateStructureInteractive(ParametricStructureType.PortalFrame, lockType: false);
        }

        [CommandMethod("SS_PORTAL", CommandFlags.Modal)]
        public void CreatePortalFrame() => CreateStructureInteractive(ParametricStructureType.PortalFrame);

        [CommandMethod("SS_MULTIPORTAL", CommandFlags.Modal)]
        public void CreateMultiSpanPortalFrame() => CreateStructureInteractive(ParametricStructureType.MultiSpanPortalFrame);

        [CommandMethod("SS_BRACED", CommandFlags.Modal)]
        public void CreateBracedFrame() => CreateStructureInteractive(ParametricStructureType.BracedFrame);

        [CommandMethod("SS_DOUBLE", CommandFlags.Modal)]
        public void CreateDoubleTierRack() => CreateStructureInteractive(ParametricStructureType.DoubleTierFrame);

        [CommandMethod("SS_SPACEFRAME", CommandFlags.Modal)]
        public void CreateMultiBaySpaceFrame() => CreateStructureInteractive(ParametricStructureType.MultiBaySpaceFrame);

        [CommandMethod("SS_BRACKET", CommandFlags.Modal)]
        public void CreateCantileverBracket() => CreateStructureInteractive(ParametricStructureType.CantileverBracket);

        [CommandMethod("SS_PLATFORM", CommandFlags.Modal)]
        public void CreateSpacePlatform() => CreateStructureInteractive(ParametricStructureType.SpacePlatform);

        [CommandMethod("SS_WALKWAY", CommandFlags.Modal)]
        public void CreateCantileverWalkway() => CreateStructureInteractive(ParametricStructureType.CantileverWalkway);

        [CommandMethod("SS_TRUSS", CommandFlags.Modal)]
        public void CreateRoofTruss() => CreateStructureInteractive(ParametricStructureType.RoofTruss);

        [CommandMethod("SS_PIPEBRIDGE", CommandFlags.Modal)]
        public void CreatePipeBridgeTruss() => CreateStructureInteractive(ParametricStructureType.PipeBridgeTruss);

        [CommandMethod("SS_STAIR", CommandFlags.Modal)]
        public void CreateSpiralStair() => CreateStructureInteractive(ParametricStructureType.SpiralStair);

        [CommandMethod("SS_STAIR_STRAIGHT", CommandFlags.Modal)]
        public void CreateStraightStair() => CreateStructureInteractive(ParametricStructureType.StraightStair);

        [CommandMethod("SS_LADDER", CommandFlags.Modal)]
        public void CreateSafetyCagedLadder() => CreateStructureInteractive(ParametricStructureType.SafetyCagedLadder);

        [CommandMethod("SS_HANDRAIL", CommandFlags.Modal)]
        public void CreateHandrailGuardrail() => CreateStructureInteractive(ParametricStructureType.HandrailGuardrail);

        [CommandMethod("SS_MEMBER", CommandFlags.Modal)]
        public void CreateLinearSteelMember() => CreateStructureInteractive(ParametricStructureType.LinearSteelMember);

        [CommandMethod("SS_ARC", CommandFlags.Modal)]
        public void CreateArcSteelMember() => CreateStructureInteractive(ParametricStructureType.ArcSteelMember);

        [CommandMethod("SS_SPIRAL", CommandFlags.Modal)]
        public void CreateSpiralSteelMember() => CreateStructureInteractive(ParametricStructureType.SpiralSteelMember);

        [CommandMethod("SS_SADDLE", CommandFlags.Modal)]
        public void CreateVesselSupport() => CreateStructureInteractive(ParametricStructureType.VesselSupport);

        // ==================== 化工、电力、冶金行业标准组件命令 ====================
        [CommandMethod("SS_TOWER_PLAT", CommandFlags.Modal)]
        public void CreateTowerCircularPlatform() => CreateStructureInteractive(ParametricStructureType.TowerCircularPlatform);

        [CommandMethod("SS_SKIRT", CommandFlags.Modal)]
        public void CreateVerticalVesselSkirt() => CreateStructureInteractive(ParametricStructureType.VerticalVesselSkirt);

        [CommandMethod("SS_PIPESHOE", CommandFlags.Modal)]
        public void CreatePipeShoeSupport() => CreateStructureInteractive(ParametricStructureType.PipeShoeSupport);

        [CommandMethod("SS_TANK_RAIL", CommandFlags.Modal)]
        public void CreateTankRoofHandrail() => CreateStructureInteractive(ParametricStructureType.TankRoofHandrail);

        [CommandMethod("SS_GANTRY", CommandFlags.Modal)]
        public void CreateSubstationGantryFrame() => CreateStructureInteractive(ParametricStructureType.SubstationGantryFrame);

        [CommandMethod("SS_CABLE_RACK", CommandFlags.Modal)]
        public void CreateCableTrenchSupport() => CreateStructureInteractive(ParametricStructureType.CableTrenchSupport);

        [CommandMethod("SS_CRANE_BEAM", CommandFlags.Modal)]
        public void CreateCraneRunwayGirder() => CreateStructureInteractive(ParametricStructureType.CraneRunwayGirder);

        [CommandMethod("SS_XBRACE", CommandFlags.Modal)]
        public void CreateCrossBracingFrame() => CreateStructureInteractive(ParametricStructureType.CrossBracingFrame);

        [CommandMethod("SS_LINEAR_CHAIN", CommandFlags.Modal)]
        public void DrawContinuousLinearChain()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var initParams = CreateDefaultParamsForType(ParametricStructureType.HandrailGuardrail);
            var win = new StructureEditWindow(initParams, isEditMode: false, lockStructureType: false, chainPlacementMode: true);
            if (UserSettings.ShowModalWindowOnAutoCad(win) != true) return;
            if (!win.Data.SupportsChainDraw)
            {
                ed.WriteMessage("\n当前构件类型不支持连续折线绘制，请改用两点线性绘制或单点放置。\n");
                return;
            }

            PromptPointResult pprStart = ed.GetPoint($"\n请拾取连续绘制 [{win.Data.StructureTypeDisplayName}] 的起点: ");
            if (pprStart.Status != PromptStatus.OK) return;

            Point3d currentStart = pprStart.Value;
            int segCount = 0;

            while (true)
            {
                SteelTwoPointLinearJig jig = new SteelTwoPointLinearJig(win.Data, currentStart);
                PromptResult jr = ed.Drag(jig);
                if (jr.Status != PromptStatus.OK) break;

                SteelStructureParams segParams = jig.UpdatedParams.Clone();
                segParams.Tag = GetNextAutoTag(segParams.StructureType);

                using (DocumentLock dl = doc.LockDocument())
                {
                    SteelStructureBuilder.AppendToCurrentDatabase(db, segParams);
                }

                ReportBuildWarnings(ed);

                segCount++;
                Vector3d dir = new Vector3d(System.Math.Cos(segParams.RotationRad), System.Math.Sin(segParams.RotationRad), 0);
                currentStart = segParams.Origin + dir * segParams.Span + new Vector3d(0, 0, segParams.EndPointOffsetZ);
            }

            if (segCount > 0)
            {
                ed.WriteMessage($"\n[SteelStructureSuite] 已连续动态绘制 {segCount} 段 [{win.Data.StructureTypeDisplayName}]。\n");
            }
        }

        [CommandMethod("SS_EDIT", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void EditExistingStructure()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            ObjectId targetId = PickParametricStructure(ed, db, "\n请选择要修改参数的三维钢结构实体: ");
            if (targetId.IsNull) return;

            SteelStructureParams currentParams = null;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(targetId, OpenMode.ForRead) is Entity ent)
                {
                    SteelDataStorage.TryReadParams(ent, out currentParams);
                }
                tr.Commit();
            }

            if (currentParams == null)
            {
                ed.WriteMessage("\n所选实体不包含参数化钢结构数据。\n");
                return;
            }

            var win = new StructureEditWindow(currentParams, isEditMode: true, lockStructureType: true);
            if (UserSettings.ShowModalWindowOnAutoCad(win) != true) return;

            using (DocumentLock dl = doc.LockDocument())
            {
                SteelStructureBuilder.UpdateStructureInDatabase(db, targetId, win.Data);
            }

            ed.Regen();
            ed.WriteMessage($"\n[SteelStructureSuite] 已更新钢结构 [{win.Data.Tag}] ({win.Data.BuildSpecSummary()})。\n");
        }

        [CommandMethod("SS_BOM", CommandFlags.Modal)]
        public void OpenMaterialTakeoffWindow()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var win = new MaterialTakeoffWindow(db);
            bool? res = UserSettings.ShowModalWindowOnAutoCad(win);

            if (res == true && win.RequestInsertCadTable &&
                (win.RequestInsertTableType == "detail" ? win.ResultDetailedItems.Count > 0 : win.ResultSummaryRows.Count > 0))
            {
                PromptPointResult ppr = ed.GetPoint("\n请在图面拾取钢结构材料统计表插入点: ");
                if (ppr.Status != PromptStatus.OK) return;

                using (DocumentLock dl = doc.LockDocument())
                {
                    if (win.RequestInsertTableType == "detail")
                    {
                        MaterialTakeoffService.CreateCadDetailedTableInModelSpace(db, ppr.Value, win.ResultDetailedItems);
                        ed.WriteMessage("\n[SteelStructureSuite] 已在模型空间生成构件零件明细表。\n");
                    }
                    else
                    {
                        MaterialTakeoffService.CreateCadBomTableInModelSpace(db, ppr.Value, win.ResultSummaryRows);
                        ed.WriteMessage("\n[SteelStructureSuite] 已在模型空间生成钢结构材料统计汇总表。\n");
                    }
                }
            }
        }

        [CommandMethod("SS_DRAWING", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void GenerateHlrDetailDrawing()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // 只允许单选：优先用预选集中的唯一构件，否则单击拾取，并校验确实是本插件的参数化钢结构。
            SteelStructureParams picked = null;
            List<SteelStructureParams> implied = CollectParametricStructures(ed, db, promptSelection: false);
            if (implied.Count == 1)
            {
                picked = implied[0];
            }
            else if (implied.Count > 1)
            {
                ed.WriteMessage("\n[SteelStructureSuite] 出图仅支持单个构件，已忽略多选。\n");
            }

            for (int attempt = 0; picked == null && attempt < 3; attempt++)
            {
                string prompt = attempt == 0
                    ? "\n请单击要出图的钢结构（仅一个）: "
                    : "\n该对象不是带参数的钢结构，请重新单击: ";
                PromptEntityResult per = ed.GetEntity(prompt);
                if (per.Status != PromptStatus.OK) return;
                picked = ReadStructureParams(db, per.ObjectId);
            }
            if (picked == null)
            {
                ed.WriteMessage("\n[SteelStructureSuite] 未选择到有效的参数化钢结构，出图已取消。\n");
                return;
            }

            var cfgWin = new DrawingConfigWindow(picked);
            if (UserSettings.ShowModalWindowOnAutoCad(cfgWin) != true) return;
            DrawingGenerationOptions options = cfgWin.Options;

            if (options.OutputToNewDwgFile)
            {
                string savePath = options.NewDwgFilePath;
                try
                {
                    SteelHlrDrawingService.GenerateDetailDrawingToNewDwgFile(picked, savePath, options);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[SteelStructureSuite] 创建新 DWG 文件失败: {ex.Message}\n");
                    return;
                }

                string savedPath = SteelHlrDrawingService.NormalizeDwgPath(savePath);
                ed.WriteMessage($"\n[SteelStructureSuite] 详图已写入：{savedPath}\n");

                if (options.OpenNewDwgAfterSave && File.Exists(savedPath))
                {
                    try
                    {
                        Application.DocumentManager.Open(savedPath, false);
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n[SteelStructureSuite] 详图已保存，但打开新 DWG 失败: {ex.Message}\n");
                    }
                }
            }
            else
            {
                PromptPointResult ppr = ed.GetPoint("\n请在当前图面空白区域拾取标准图框左下角插入点: ");
                if (ppr.Status != PromptStatus.OK) return;
                Point3d insertPt = ppr.Value;

                try
                {
                    using (DocumentLock dl = doc.LockDocument())
                    {
                        SteelHlrDrawingService.GenerateDetailDrawingSheet(db, new[] { picked }, insertPt, options);
                    }
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage("\n[SteelStructureSuite] 详图生成失败: " + ex.Message + "\n");
                    return;
                }

                ed.WriteMessage($"\n[SteelStructureSuite] 已生成 {options.SheetSizeName} 详图（图框左下角 {insertPt.X:0.##},{insertPt.Y:0.##}）。\n");
            }
        }

        [CommandMethod("SS_BATCHDRAW", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void BatchGenerateDetailDrawings()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            List<SteelStructureParams> picked = CollectParametricStructures(ed, doc.Database, promptSelection: true);
            if (picked == null || picked.Count == 0)
            {
                ed.WriteMessage("\n[SteelStructureSuite] 未选择到有效的参数化钢结构，批量出图已取消。\n");
                return;
            }

            var plan = BatchDrawingPlan.Build(picked, reuseIdentical: true);
            ed.WriteMessage($"\n[SteelStructureSuite] 已选 {plan.TotalStructureCount} 个构件，" +
                            $"{plan.SheetCount} 种规格" +
                            (plan.SavedSheetCount > 0 ? $"（复用省下 {plan.SavedSheetCount} 张图）" : string.Empty) + "\n");

            var win = new BatchDrawingWindow(picked);
            UserSettings.ShowModalWindowOnAutoCad(win);

            if (win.Result != null)
            {
                ed.WriteMessage($"\n[SteelStructureSuite] 批量出图结束：{win.Result.SheetCount} 张图" +
                                (win.Result.Cancelled ? "（已取消）" : string.Empty) + "\n");
                if (!string.IsNullOrEmpty(win.Result.OutputPath))
                    ed.WriteMessage($"  输出：{win.Result.OutputPath}\n");
                foreach (string w in win.Result.Warnings)
                    ed.WriteMessage("  警告：" + w + "\n");
            }
        }

        // ==================== 区域组合构件 ====================

        [CommandMethod("SS_ZONE_PLATFORM", CommandFlags.Modal)]
        public void CreateZonePlatformAssembly() =>
            CreateStructureInteractive(ParametricStructureType.PlatformAssembly, lockType: true);

        [CommandMethod("SS_ZONE_COLROW", CommandFlags.Modal)]
        public void CreateZoneColumnRowAssembly() =>
            CreateStructureInteractive(ParametricStructureType.ColumnRowAssembly, lockType: true);

        [CommandMethod("SS_ZONE_PIPERACK", CommandFlags.Modal)]
        public void CreateZonePipeRackAssembly() =>
            CreateStructureInteractive(ParametricStructureType.PipeRackAssembly, lockType: true);

        [CommandMethod("SS_ZONE_STAIR", CommandFlags.Modal)]
        public void CreateZoneStairAssembly() =>
            CreateStructureInteractive(ParametricStructureType.StairAssembly, lockType: true);

        [CommandMethod("SS_ZONE_HALL", CommandFlags.Modal)]
        public void CreateZonePortalHallAssembly() =>
            CreateStructureInteractive(ParametricStructureType.PortalHallAssembly, lockType: true);

        [CommandMethod("SS_ZONE_WALKWAY", CommandFlags.Modal)]
        public void CreateZoneWalkwayBridgeAssembly() =>
            CreateStructureInteractive(ParametricStructureType.WalkwayBridgeAssembly, lockType: true);

        [CommandMethod("SS_ZONE_EQFRAME", CommandFlags.Modal)]
        public void CreateZoneEquipmentFrameAssembly() =>
            CreateStructureInteractive(ParametricStructureType.EquipmentFrameAssembly, lockType: true);

        [CommandMethod("SS_ZONE_CANOPY", CommandFlags.Modal)]
        public void CreateZoneCanopyAssembly() =>
            CreateStructureInteractive(ParametricStructureType.CanopyAssembly, lockType: true);

        // ==================== 参数模板 ====================

        [CommandMethod("SS_TEMPLATES", CommandFlags.Modal)]
        public void OpenTemplateManager()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var win = new TemplateManagerWindow();
            UserSettings.ShowModalWindowOnAutoCad(win);
        }

        [CommandMethod("SS_SAVETEMPLATE", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void SaveSelectedAsTemplate()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            SteelStructureParams picked = PickSingleStructure(ed, doc.Database);
            if (picked == null)
            {
                ed.WriteMessage("\n[SteelStructureSuite] 未选择到有效的参数化钢结构，已取消。\n");
                return;
            }

            // 直接进编辑窗口：模板名称/备注在窗口内就地填，不再额外弹输入框
            var win = new StructureEditWindow(picked, isEditMode: true, lockStructureType: true)
            {
                StartInTemplateSaveMode = true
            };
            if (UserSettings.ShowModalWindowOnAutoCad(win) != true) return;

            ed.WriteMessage("\n[SteelStructureSuite] 模板操作完成。\n");
        }

        [CommandMethod("SS_APPLYTEMPLATE", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void ApplyTemplateToSelected()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            SteelStructureParams picked = PickSingleStructure(ed, doc.Database);
            if (picked == null)
            {
                ed.WriteMessage("\n[SteelStructureSuite] 未选择到有效的参数化钢结构，已取消。\n");
                return;
            }

            // 用建模窗口承载模板套用：类型锁定，模板下拉直接选
            var win = new StructureEditWindow(picked, isEditMode: true, lockStructureType: true);
            if (UserSettings.ShowModalWindowOnAutoCad(win) != true) return;

            ed.WriteMessage($"\n[SteelStructureSuite] 模板套用完成，请用 SS_EDIT 或夹点更新模型。\n");
        }

        /// <summary>拾取单个参数化钢结构（优先用预选集中的唯一构件）。</summary>
        private static SteelStructureParams PickSingleStructure(Editor ed, Database db)
        {
            List<SteelStructureParams> implied = CollectParametricStructures(ed, db, promptSelection: false);
            if (implied.Count == 1) return implied[0];
            if (implied.Count > 1)
                ed.WriteMessage("\n[SteelStructureSuite] 已忽略多选，请只选一个构件。\n");

            for (int attempt = 0; attempt < 3; attempt++)
            {
                string prompt = attempt == 0
                    ? "\n请单击一个参数化钢结构: "
                    : "\n该对象不是带参数的钢结构，请重新单击: ";
                PromptEntityResult per = ed.GetEntity(prompt);
                if (per.Status != PromptStatus.OK) return null;

                SteelStructureParams found = ReadStructureParams(db, per.ObjectId);
                if (found != null) return found;
            }
            return null;
        }

        [CommandMethod("SS_DRAWCFG", CommandFlags.Modal)]
        public void ConfigureDrawingSettings()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            List<SteelStructureParams> picked = CollectParametricStructures(doc.Editor, doc.Database, promptSelection: false);
            SteelStructureParams header = picked.Count > 0 ? picked[0].Clone() : new SteelStructureParams();
            var cfgWin = new DrawingConfigWindow(header);
            UserSettings.ShowModalWindowOnAutoCad(cfgWin);
        }

        private static SteelStructureParams ReadStructureParams(Database db, ObjectId id)
        {
            if (!id.IsValid || id.IsErased) return null;
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity ent &&
                        SteelDataStorage.TryReadParams(ent, out SteelStructureParams data))
                    {
                        return data;
                    }
                    tr.Commit();
                }
            }
            catch
            {
            }
            return null;
        }

        private static List<SteelStructureParams> CollectParametricStructures(Editor ed, Database db, bool promptSelection = true)
        {
            var ids = new List<ObjectId>();
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
                ids.AddRange(implied.Value.GetObjectIds());

            List<SteelStructureParams> Read(IEnumerable<ObjectId> source)
            {
                var found = new List<SteelStructureParams>();
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in source)
                    {
                        if (!id.IsValid || id.IsErased) continue;
                        if (tr.GetObject(id, OpenMode.ForRead) is Entity ent &&
                            SteelDataStorage.TryReadParams(ent, out SteelStructureParams data))
                        {
                            found.Add(data);
                        }
                    }
                    tr.Commit();
                }
                return found;
            }

            List<SteelStructureParams> picked = Read(ids);
            if (picked.Count > 0) return picked;
            if (!promptSelection) return picked;

            PromptSelectionOptions pso = new PromptSelectionOptions
            {
                MessageForAdding = "\n请框选要一起出图的钢结构（可多选）: "
            };
            PromptSelectionResult psr = ed.GetSelection(pso);
            if (psr.Status != PromptStatus.OK || psr.Value == null) return picked;
            return Read(psr.Value.GetObjectIds());
        }

        private static void CreateStructureInteractive(ParametricStructureType defaultType, bool lockType = true)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var initParams = CreateDefaultParamsForType(defaultType);
            var win = new StructureEditWindow(initParams, isEditMode: false, lockStructureType: lockType);
            if (UserSettings.ShowModalWindowOnAutoCad(win) != true) return;

            SteelStructureParams finalParams = win.Data;
            bool useLinearDraw = win.UseTwoPointLinearDraw && finalParams.SupportsTwoPointDraw;

            if (win.UseCurvePick || finalParams.RequiresPickedCurve)
            {
                if (!PromptAndBindCurve(ed, finalParams)) return;
            }
            else if (useLinearDraw)
            {
                PromptPointResult pprStart = ed.GetPoint(finalParams.TwoPointStartPrompt);
                if (pprStart.Status != PromptStatus.OK) return;

                SteelTwoPointLinearJig linearJig = new SteelTwoPointLinearJig(finalParams, pprStart.Value);
                PromptResult jigRes = ed.Drag(linearJig);
                if (jigRes.Status != PromptStatus.OK) return;

                finalParams = linearJig.UpdatedParams;
            }
            else
            {
                SteelSinglePointPlacementJig singleJig = new SteelSinglePointPlacementJig(finalParams);
                PromptResult jigRes = ed.Drag(singleJig);
                if (jigRes.Status != PromptStatus.OK) return;

                finalParams.Origin = singleJig.PickedPoint;
            }

            using (DocumentLock dl = doc.LockDocument())
            {
                SteelStructureBuilder.AppendToCurrentDatabase(db, finalParams);
                ReportBuildWarnings(ed);
            }

            _tagSeq++;
            ed.WriteMessage($"\n[SteelStructureSuite] 已生成三维钢结构 [{finalParams.Tag}] ({finalParams.BuildSpecSummary()})。\n");
        }

        private static readonly Dictionary<ParametricStructureType, int> _typeTagSeqMap =
            new Dictionary<ParametricStructureType, int>();

        private static string GetNextAutoTag(ParametricStructureType type)
        {
            int nextSeq = 1;
            if (_typeTagSeqMap.TryGetValue(type, out int last))
            {
                nextSeq = last + 1;
            }
            else
            {
                // 首次按类型编号时，尝试扫描当前图纸已有同类型构件的最大编号，避免重号
                try
                {
                    Document doc = Application.DocumentManager.MdiActiveDocument;
                    if (doc != null)
                    {
                        var existing = MaterialTakeoffService.CollectStructures(doc.Database);
                        int maxNum = 0;
                        foreach (var item in existing)
                        {
                            if (item.StructureType != type || string.IsNullOrEmpty(item.Tag)) continue;
                            int idx = item.Tag.Length;
                            while (idx > 0 && char.IsDigit(item.Tag[idx - 1])) idx--;
                            if (idx < item.Tag.Length && int.TryParse(item.Tag.Substring(idx), out int n) && n > maxNum)
                                maxNum = n;
                        }
                        nextSeq = maxNum + 1;
                    }
                }
                catch { }
            }

            _typeTagSeqMap[type] = nextSeq;
            return UserSettings.FormatTag(type, nextSeq);
        }

        public static SteelStructureParams CreateDefaultParamsForType(ParametricStructureType type)
        {
            var p = new SteelStructureParams
            {
                Tag = GetNextAutoTag(type),
                StructureType = type,
                Material = "Q355B"
            };

            switch (type)
            {
                case ParametricStructureType.PortalFrame:
                    p.Span = 3000; p.Length = 1500; p.Height = 3600; p.SubHeight = 1800;
                    p.ColumnSection = "HW200x200x8x12"; p.BeamSection = "HW200x200x8x12"; p.BraceSection = "L75x75x6";
                    break;
                case ParametricStructureType.MultiSpanPortalFrame:
                    p.Span = 7200; p.Length = 1500; p.Height = 4000; p.SubHeight = 2000; p.BayCount = 2;
                    p.ColumnSection = "HW250x250x9x14"; p.BeamSection = "HW200x200x8x12"; p.BraceSection = "L100x100x8";
                    break;
                case ParametricStructureType.BracedFrame:
                    p.Span = 3200; p.Length = 1500; p.Height = 4200; p.SubHeight = 2100;
                    p.ColumnSection = "HW250x250x9x14"; p.BeamSection = "HW200x200x8x12"; p.BraceSection = "L100x100x8";
                    break;
                case ParametricStructureType.DoubleTierFrame:
                    p.Span = 2800; p.Length = 1500; p.Height = 4800; p.SubHeight = 2600;
                    p.ColumnSection = "HW250x250x9x14"; p.BeamSection = "HW200x200x8x12"; p.BraceSection = "C16a";
                    break;
                case ParametricStructureType.MultiBaySpaceFrame:
                    p.Span = 3600; p.Length = 3600; p.Height = 5200; p.SubHeight = 2600;
                    p.ColumnSection = "HW250x250x9x14"; p.BeamSection = "HW200x200x8x12"; p.BraceSection = "C16a";
                    break;
                case ParametricStructureType.CantileverBracket:
                    p.Span = 1400; p.Length = 1000; p.Height = 2600; p.SubHeight = 900;
                    p.ColumnSection = "HW200x200x8x12"; p.BeamSection = "HM194x150x6x9"; p.BraceSection = "C14a";
                    break;
                case ParametricStructureType.SpacePlatform:
                    p.Span = 3200; p.Length = 4200; p.Height = 3400; p.SubHeight = 1050;
                    p.ColumnSection = "HW200x200x8x12"; p.BeamSection = "HN250x125x6x9"; p.BraceSection = "C16a";
                    break;
                case ParametricStructureType.CantileverWalkway:
                    p.Span = 4500; p.Length = 1000; p.Height = 3000; p.SubHeight = 1050;
                    p.ColumnSection = "C16a"; p.BeamSection = "C16a"; p.BraceSection = "L75x75x6";
                    break;
                case ParametricStructureType.RoofTruss:
                    p.Span = 6000; p.Length = 3000; p.Height = 3800; p.SubHeight = 1200;
                    p.ColumnSection = "HW250x250x9x14"; p.BeamSection = "F120x120x6"; p.BraceSection = "L75x75x6";
                    break;
                case ParametricStructureType.PipeBridgeTruss:
                    p.Span = 8000; p.Length = 2200; p.Height = 1800; p.SubHeight = 4200;
                    p.ColumnSection = "HW150x150x7x10"; p.BeamSection = "HM194x150x6x9"; p.BraceSection = "L75x75x6";
                    break;
                case ParametricStructureType.SpiralStair:
                    p.Span = 2600; p.Length = 850; p.Height = 4200; p.SubHeight = 270;
                    p.ColumnSection = "C20a"; p.BeamSection = "P48x3.5"; p.BraceSection = "C12.6";
                    break;
                case ParametricStructureType.StraightStair:
                    p.Span = 3200; p.Length = 900; p.Height = 2800; p.SubHeight = 1050;
                    p.ColumnSection = "C20a"; p.BeamSection = "P42x3.5"; p.BraceSection = "C14a";
                    break;
                case ParametricStructureType.SafetyCagedLadder:
                    p.Span = 600; p.Length = 750; p.Height = 4800; p.SubHeight = 2200;
                    p.ColumnSection = "P48x3.5"; p.BeamSection = "P32x3.2"; p.BraceSection = "P32x3.2";
                    break;
                case ParametricStructureType.HandrailGuardrail:
                    p.Span = 4000; p.Length = 120; p.Height = 1050; p.SubHeight = 1000;
                    p.ColumnSection = "P48x3.5"; p.BeamSection = "P42x3.5"; p.BraceSection = "P32x3.2";
                    break;
                case ParametricStructureType.LinearSteelMember:
                    p.Span = 3600; p.Length = 200; p.Height = 200; p.SubHeight = 100;
                    p.ColumnSection = "HW200x200x8x12"; p.BeamSection = "HW200x200x8x12"; p.BraceSection = "L100x100x8";
                    break;
                case ParametricStructureType.ArcSteelMember:
                    p.Span = 1500; p.Length = 200; p.Height = 200; p.SubHeight = 90;
                    p.ColumnSection = "P89x4"; p.BeamSection = "P89x4"; p.BraceSection = "P48x3.5";
                    break;
                case ParametricStructureType.SpiralSteelMember:
                    p.Span = 4000; p.Length = 280; p.Height = 200; p.SubHeight = 3;
                    p.ColumnSection = "P48x3.5"; p.BeamSection = "P48x3.5"; p.BraceSection = "P32x3.2";
                    break;
                case ParametricStructureType.VesselSupport:
                    p.Span = 2200; p.Length = 450; p.Height = 1350; p.SubHeight = 120;
                    p.ColumnSection = "HW200x200x8x12"; p.BeamSection = "C20a"; p.BraceSection = "L100x100x8";
                    break;
                case ParametricStructureType.TowerCircularPlatform:
                    p.Span = 2400; p.Length = 1050; p.Height = 950; p.SubHeight = 210;
                    p.ColumnSection = "C20a"; p.BeamSection = "C16a"; p.BraceSection = "L75x75x6";
                    break;
                case ParametricStructureType.VerticalVesselSkirt:
                    p.Span = 2200; p.Length = 300; p.Height = 3600; p.SubHeight = 550; p.BayCount = 12;
                    p.ColumnSection = "HW200x200x8x12"; p.BeamSection = "C20a"; p.BraceSection = "L75x75x6";
                    break;
                case ParametricStructureType.PipeShoeSupport:
                    p.Span = 630; p.Length = 500; p.Height = 480; p.SubHeight = 420;
                    p.ColumnSection = "HW200x200x8x12"; p.BeamSection = "C16a"; p.BraceSection = "L75x75x6";
                    break;
                case ParametricStructureType.TankRoofHandrail:
                    p.Span = 4800; p.Length = 120; p.Height = 1050; p.SubHeight = 270;
                    p.ColumnSection = "P48x3.5"; p.BeamSection = "P42x3.5"; p.BraceSection = "P32x3.2";
                    break;
                case ParametricStructureType.SubstationGantryFrame:
                    p.Span = 9000; p.Length = 2200; p.Height = 11000; p.SubHeight = 2500;
                    p.ColumnSection = "P219x8"; p.BeamSection = "L100x100x8"; p.BraceSection = "L63x63x6";
                    break;
                case ParametricStructureType.CableTrenchSupport:
                    p.Span = 4500; p.Length = 500; p.Height = 1500; p.SubHeight = 300; p.BayCount = 4;
                    p.ColumnSection = "C10"; p.BeamSection = "L50x50x5"; p.BraceSection = "L50x50x5";
                    break;
                case ParametricStructureType.CraneRunwayGirder:
                    p.Span = 6000; p.Length = 650; p.Height = 800; p.SubHeight = 1000;
                    p.ColumnSection = "HW300x300x10x15"; p.BeamSection = "C20a"; p.BraceSection = "L75x75x6";
                    break;
                case ParametricStructureType.CrossBracingFrame:
                    p.Span = 6000; p.Length = 250; p.Height = 7200; p.SubHeight = 4800;
                    p.ColumnSection = "HW250x250x9x14"; p.BeamSection = "F150x150x8"; p.BraceSection = "L100x100x8";
                    break;

                // ---- 区域组合构件 ----
                case ParametricStructureType.PlatformAssembly:
                    p.Span = 6000; p.Length = 4500; p.Height = 3000; p.SubHeight = 1050; p.BayCount = 3;
                    p.ColumnSection = "HW200x200x8x12"; p.BeamSection = "HN300x150x6.5x9"; p.BraceSection = "L75x75x6";
                    break;
                case ParametricStructureType.ColumnRowAssembly:
                    p.Span = 7500; p.Length = 30000; p.Height = 9000; p.SubHeight = 6000; p.BayCount = 5;
                    p.ColumnSection = "HW400x400x13x21"; p.BeamSection = "HN500x200x10x16"; p.BraceSection = "L125x125x10";
                    break;
                case ParametricStructureType.PipeRackAssembly:
                    p.Span = 6000; p.Length = 24000; p.Height = 8000; p.SubHeight = 3000; p.BayCount = 4;
                    p.ColumnSection = "HW300x300x10x15"; p.BeamSection = "HW250x250x9x14"; p.BraceSection = "L100x100x8";
                    break;
                case ParametricStructureType.StairAssembly:
                    p.Span = 3600; p.Length = 900; p.Height = 3000; p.SubHeight = 1050; p.BayCount = 0;
                    p.ColumnSection = "C200x70x20x2.5"; p.BeamSection = "C200x70x20x2.5"; p.BraceSection = "PL4x250";
                    break;
                case ParametricStructureType.PortalHallAssembly:
                    p.Span = 12000; p.Length = 24000; p.Height = 6000; p.SubHeight = 1200; p.BayCount = 4;
                    p.ColumnSection = "HW250x250x9x14"; p.BeamSection = "HN350x175x7x11"; p.BraceSection = "L90x90x8";
                    break;
                case ParametricStructureType.WalkwayBridgeAssembly:
                    p.Span = 18000; p.Length = 2000; p.Height = 5000; p.SubHeight = 1050; p.BayCount = 3;
                    p.ColumnSection = "HW250x250x9x14"; p.BeamSection = "HN300x150x6.5x9"; p.BraceSection = "L90x90x8";
                    break;
                case ParametricStructureType.EquipmentFrameAssembly:
                    p.Span = 9000; p.Length = 6000; p.Height = 9000; p.SubHeight = 3000; p.BayCount = 3;
                    p.ColumnSection = "HW250x250x9x14"; p.BeamSection = "HN300x150x6.5x9"; p.BraceSection = "L100x100x8";
                    break;
                case ParametricStructureType.CanopyAssembly:
                    p.Span = 15000; p.Length = 4500; p.Height = 4000; p.SubHeight = 600; p.BayCount = 4;
                    p.ColumnSection = "HW200x200x8x12"; p.BeamSection = "HN250x125x6x9"; p.BraceSection = "C160x60x20x2.5";
                    break;
            }

            return p;
        }

        private static bool PromptAndBindCurve(Editor ed, SteelStructureParams p)
        {
            bool helix = p.StructureType == ParametricStructureType.SpiralSteelMember;
            PromptEntityOptions peo = new PromptEntityOptions(
                helix
                    ? "\n选择作为路径的螺旋线: "
                    : "\n选择作为路径的圆弧、圆或圆弧多段线: ");
            peo.SetRejectMessage(helix ? "\n请选择螺旋线。" : "\n请选择圆弧、圆或圆弧多段线。");
            if (helix)
            {
                peo.AddAllowedClass(typeof(Helix), false);
            }
            else
            {
                peo.AddAllowedClass(typeof(Arc), false);
                peo.AddAllowedClass(typeof(Circle), false);
                peo.AddAllowedClass(typeof(Polyline), false);
            }
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return false;

            using (Transaction tr = ed.Document.Database.TransactionManager.StartTransaction())
            {
                Curve curve = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Curve;
                string error = null;
                bool ok = curve != null && CurveSweepService.TryBindPickedCurve(p, curve, out error);
                tr.Commit();
                if (!ok)
                {
                    ed.WriteMessage("\n[SteelStructureSuite] " + (string.IsNullOrEmpty(error) ? "无法使用这条曲线。" : error));
                    return false;
                }
            }
            return true;
        }

        /// <summary>把建模过程中的告警（例如布尔合并失败导致构件缺失）回显给用户。</summary>
        private static void ReportBuildWarnings(Editor ed)
        {
            if (ed == null) return;
            if (SteelStructureBuilder.LastBuildWarnings == null) return;
            foreach (string warning in SteelStructureBuilder.LastBuildWarnings)
            {
                ed.WriteMessage($"\n[SteelStructureSuite] 建模告警：{warning}");
            }
        }

        private static ObjectId PickParametricStructure(Editor ed, Database db, string prompt)
        {
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
            {
                foreach (ObjectId id in implied.Value.GetObjectIds())
                {
                    if (IsParametricSolid(db, id)) return id;
                }
            }

            PromptEntityOptions peo = new PromptEntityOptions(prompt);
            peo.SetRejectMessage("\n请选择参数化钢结构构件。");
            peo.AddAllowedClass(typeof(Solid3d), false);
            peo.AddAllowedClass(typeof(BlockReference), false);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status == PromptStatus.OK && IsParametricSolid(db, per.ObjectId))
            {
                return per.ObjectId;
            }
            return ObjectId.Null;
        }

        private static bool IsParametricSolid(Database db, ObjectId id)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(id, OpenMode.ForRead) is Entity ent &&
                    SteelDataStorage.TryReadParams(ent, out SteelStructureParams _))
                {
                    tr.Commit();
                    return true;
                }
                tr.Commit();
            }
            return false;
        }
    }
}
