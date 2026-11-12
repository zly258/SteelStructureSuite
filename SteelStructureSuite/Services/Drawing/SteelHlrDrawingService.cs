using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Catalog;
using SteelStructureSuite.Services.Modeling;

namespace SteelStructureSuite.Services.Drawing
{
    public static class SteelHlrDrawingService
    {
        private const string LayerVisible = "SS_HLR_VISIBLE";
        private const string LayerHidden = "SS_HLR_HIDDEN";
        private const string LayerAxis = "SS_HLR_AXIS";
        private const string LayerDim = "SS_HLR_DIM";
        private const string LayerFrame = "SS_HLR_FRAME";
        private const string TextStyleName = "SS_SIMSUN_35_08";
        private const string DimStyleName = "SS_DIM_SIMSUN_35_08";

        private class ProjectedPart2D
        {
            public SteelStructureBuilder.SolidMemberPart SourcePart;
            public string RoleName;
            public string SectionName;
            public int PartNo;
            public bool IsPlate;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
            public double MinDepth;
            public double MaxDepth;
            public List<Point2d> Hull = new List<Point2d>();
        }

        private class IsoPart2D
        {
            public SteelStructureBuilder.SolidMemberPart SourcePart;
            public int PartNo;
            public List<Point2d> Hull;
            public Point2d Anchor2D;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
            public double Depth;
        }

        private class ViewBounds
        {
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
            public double Width => Math.Max(1.0, MaxX - MinX);
            public double Height => Math.Max(1.0, MaxY - MinY);
        }

        private static readonly double[] StandardScales =
        {
            1, 2, 5, 10, 15, 20, 25, 30, 40, 50, 60, 75, 100, 125, 150, 200, 250, 300, 400, 500
        };

        private class LeaderRequest
        {
            public Point3d AnchorPt;
            public string LabelText;
            public int Side; // 0 左水平引线, 1 右水平引线（严禁向图框上方发射竖向引线）
        }

        public static void GenerateDetailDrawingToNewDwgFile(
            SteelStructureParams p,
            string saveDwgPath,
            DrawingGenerationOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(saveDwgPath))
                throw new ArgumentException("新 DWG 文件路径不能为空。");

            string normalizedPath = NormalizeDwgPath(saveDwgPath);

            using (Database newDb = new Database(true, true))
            using (WorkingDatabaseScope.Use(newDb))
            {
                GenerateDetailDrawingSheet(newDb, p, Point3d.Origin, options);
                SaveDatabaseToDwg(newDb, normalizedPath);
            }
        }

        private static void GetStandardSheetPaperMm(string sheetName, out double paperW, out double paperH)
        {
            switch ((sheetName ?? "A2").ToUpperInvariant())
            {
                case "A1": paperW = 841.0; paperH = 594.0; break;
                case "A2": paperW = 594.0; paperH = 420.0; break;
                case "A3": paperW = 420.0; paperH = 297.0; break;
                case "A4": paperW = 297.0; paperH = 210.0; break;
                default: paperW = 594.0; paperH = 420.0; break;
            }
        }

        public static void GenerateDetailDrawingSheet(
            Database db,
            SteelStructureParams p,
            Point3d sheetOrigin,
            DrawingGenerationOptions options = null)
        {
            if (p == null) return;
            GenerateDetailDrawingSheet(db, new[] { p }, sheetOrigin, options);
        }

        public static void GenerateDetailDrawingToNewDwgFile(
            IList<SteelStructureParams> structures,
            string saveDwgPath,
            DrawingGenerationOptions options = null)
        {
            if (structures == null || structures.Count == 0)
                throw new ArgumentException("没有可出图的钢结构。");
            if (string.IsNullOrWhiteSpace(saveDwgPath))
                throw new ArgumentException("新 DWG 文件路径不能为空。");

            string normalizedPath = NormalizeDwgPath(saveDwgPath);

            using (Database newDb = new Database(true, true))
            using (WorkingDatabaseScope.Use(newDb))
            {
                GenerateDetailDrawingSheet(newDb, structures, Point3d.Origin, options);
                SaveDatabaseToDwg(newDb, normalizedPath);
            }
        }

        /// <summary>
        /// 规范化待保存的 DWG 路径：
        ///   1. 去掉非法文件名字符（Windows 保留字符 &lt; &gt; : " / \ | ? * 以及控制字符）；
        ///   2. 缺少扩展名时补 .dwg；
        ///   3. 返回绝对路径，避免因相对路径造成"另存为"落到错误目录。
        /// </summary>
        public static string NormalizeDwgPath(string saveDwgPath)
        {
            if (string.IsNullOrWhiteSpace(saveDwgPath))
                throw new ArgumentException("新 DWG 文件路径不能为空。");

            string raw = saveDwgPath.Trim().Trim('"');
            string dir = Path.GetDirectoryName(raw);
            string file = Path.GetFileName(raw);

            if (string.IsNullOrEmpty(file))
                throw new ArgumentException("新 DWG 文件路径缺少文件名：" + saveDwgPath);

            var sb = new System.Text.StringBuilder(file.Length);
            foreach (char ch in file)
            {
                if (char.IsControl(ch)) continue;
                if ("< > : \"/ \\ | ? *".IndexOf(ch) >= 0) continue;
                sb.Append(ch);
            }

            file = sb.ToString().Trim().TrimEnd('.');
            if (file.Length == 0)
                throw new ArgumentException("新 DWG 文件名全部由非法字符组成：" + saveDwgPath);
            if (!file.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                file += ".dwg";

            string full = string.IsNullOrEmpty(dir)
                ? file
                : Path.Combine(dir, file);
            try
            {
                full = Path.GetFullPath(full);
            }
            catch
            {
                // 路径含非法字符时退回拼接结果
            }
            return full;
        }

        /// <summary>
        /// 保存独立 DWG 文件。
        ///
        /// 之前的实现直接 newDb.SaveAs(path, DwgVersion.Current)，在以下情况会抛出
        /// eWrongDatabase：
        ///   · 文件名里含有 "/" 等 Windows 非法字符（例如把标准号 "HG/T 21543" 直接拼进文件名）；
        ///   · 目标文件已存在时 SaveAs 覆盖失败；
        ///   · 当前 CAD 的 DwgVersion.Current 与目标写入版本不匹配。
        /// 这里统一做净化 + 覆盖前删除 + 版本回退，并给出可读的错误信息。
        /// </summary>
        private static void SaveDatabaseToDwg(Database db, string saveDwgPath)
        {
            string dir = Path.GetDirectoryName(saveDwgPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Exception lastError = null;
            DwgVersion[] versions = { DwgVersion.Current, DwgVersion.AC1032, DwgVersion.AC1027, DwgVersion.AC1024 };

            foreach (DwgVersion version in versions)
            {
                try
                {
                    if (File.Exists(saveDwgPath))
                        File.Delete(saveDwgPath);

                    db.SaveAs(saveDwgPath, true, version, db.SecurityParameters);

                    if (File.Exists(saveDwgPath))
                    {
                        var info = new FileInfo(saveDwgPath);
                        if (info.Length > 0) return;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw new IOException(
                "保存 DWG 失败：" + saveDwgPath + "（" + (lastError == null ? "未知错误" : lastError.Message) + "）",
                lastError);
        }

        public static void GenerateDetailDrawingSheet(
            Database db,
            IList<SteelStructureParams> structures,
            Point3d sheetOrigin,
            DrawingGenerationOptions options = null)
        {
            if (structures == null || structures.Count == 0) return;
            if (options == null) options = new DrawingGenerationOptions();

            SteelStructureParams p = structures[0];
            bool groupSheet = structures.Count > 1;
            string sheetCaption = groupSheet
                ? $"图名: 组合详图 ({structures.Count}件)"
                : $"图名: {p.StructureTypeDisplayName} 详图 ({p.Tag})";

            GetStandardSheetPaperMm(options.SheetSizeName, out double paperWMm, out double paperHMm);

            EnsureDrawingLayers(db);
            List<SteelStructureBuilder.SolidMemberPart> parts3d = new List<SteelStructureBuilder.SolidMemberPart>();
            List<MemberPartBillItem> billItems = new List<MemberPartBillItem>();
            foreach (SteelStructureParams item in structures)
            {
                if (item == null) continue;
                parts3d.AddRange(groupSheet ? PlaceStructureInWorld(item) : SteelStructureBuilder.BuildDetailedParts(item));
                billItems.AddRange(SteelStructureBuilder.CalculatePartBillItems(item));
            }
            billItems = billItems
                .OrderBy(x => x.ParentTag ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.PartNo)
                .ThenBy(x => x.PartRole ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (groupSheet)
            {
                for (int i = 0; i < billItems.Count; i++)
                    billItems[i].PartNo = i + 1;
            }
            Dictionary<string, int> roleToPartNo = MapRoleToPartNo(parts3d, billItems);

            bool hasFront = options.IncludeFrontView;
            bool hasSide = options.IncludeSideView;
            bool hasPlan = options.IncludePlanView;
            bool hasIso = options.IncludeIsoView;
            bool hasBom = options.IncludeEmbeddedBomTable;

            Func<Point3d, Point2d> frontProj = pt => new Point2d(pt.X, pt.Z);
            Func<Point3d, Point2d> sideProj = pt => new Point2d(pt.Y, pt.Z);
            Func<Point3d, Point2d> planProj = pt => new Point2d(pt.X, pt.Y);
            Func<Point3d, Point2d> isoProj = IsoProject;

            ViewBounds frontBounds = hasFront ? ComputeProjectedBounds(parts3d, frontProj) : new ViewBounds();
            ViewBounds sideBounds = hasSide ? ComputeProjectedBounds(parts3d, sideProj) : new ViewBounds();
            ViewBounds planBounds = hasPlan ? ComputeProjectedBounds(parts3d, planProj) : new ViewBounds();
            ViewBounds isoBounds = hasIso ? ComputeProjectedBounds(parts3d, isoProj) : new ViewBounds();

            double scale = SelectBestScale(options, paperWMm, paperHMm, billItems.Count,
                hasFront, frontBounds, hasSide, sideBounds, hasPlan, planBounds, hasIso, isoBounds, hasBom);

            // 严格执行：字体宋体 3.5 x 0.8（字高 = 3.5 * scale，宽度因子 = 0.8）
            double textH = 3.5 * scale;
            double dimGapStep = 3.2 * textH;

            double frameW = paperWMm * scale;
            double frameH = paperHMm * scale;
            double borderLeft = 25.0 * scale;  // 国标装订边 25mm
            double borderOther = 10.0 * scale; // 国标其余留边 10mm

            ObjectId simSunTextStyleId = EnsureSimSunTextStyle(db);
            ObjectId simSunDimStyleId = EnsureSimSunDimStyle(db, simSunTextStyleId, textH);

            Dictionary<string, AutoCadSolprof.SolprofView> profiles = null;
            try
            {
                profiles = BuildSolprofViews(parts3d, hasFront, hasSide, hasPlan, hasIso);
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    double innerX0 = sheetOrigin.X + borderLeft;
                    double innerY0 = sheetOrigin.Y + borderOther;
                    double innerX1 = sheetOrigin.X + frameW - borderOther;
                    double innerY1 = sheetOrigin.Y + frameH - borderOther;
                    double innerW = innerX1 - innerX0;
                    double innerH = innerY1 - innerY0;

                    double margin = 9.0 * scale;
                    double gap = 14.0 * scale;
                    double dimPad = dimGapStep * 2.55;
                    double belowOrtho = dimGapStep * 2.15 + 4.2 * textH;
                    double colGap = Math.Max(gap, dimPad * 1.15);

                    double titleW = 180.0 * scale;
                    double titleH = 32.0 * scale;
                    double titleTop = innerY0 + titleH;
                    double bomRowH = textH * 2.05;
                    double bomHeaderH = textH * 2.45;
                    double bomH = hasBom ? bomHeaderH + (billItems.Count + 2) * bomRowH : 0.0;
                    double rightTableTop = titleTop + bomH;

                    double frontW = hasFront ? frontBounds.Width : 0.0;
                    double sideW = hasSide ? sideBounds.Width : 0.0;
                    double planW = hasPlan ? planBounds.Width : 0.0;
                    double isoW = hasIso ? isoBounds.Width : 0.0;
                    double frontH = hasFront ? frontBounds.Height : 0.0;
                    double sideH = hasSide ? sideBounds.Height : 0.0;
                    double planH = hasPlan ? planBounds.Height : 0.0;
                    double isoH = hasIso ? isoBounds.Height : 0.0;

                    double orthoH = Math.Max(frontH, sideH);
                    double leaderSideReserve = hasIso ? 12.5 * textH : 0.0;

                    // 左中区（正立面 + 侧立面 + 平面图）与右区（轴测图 + 材料表 + 标题栏）双区独立核算
                    double orthoRowW = (hasFront ? dimPad + frontW : 0.0)
                                     + (hasFront && hasSide ? colGap : 0.0)
                                     + (hasSide ? dimPad + sideW : 0.0);
                    double planRowW = hasPlan ? dimPad + planW : 0.0;
                    double leftZoneW = Math.Max(orthoRowW, planRowW);
                    double rightZoneW = Math.Max(
                        hasIso ? isoW + leaderSideReserve * 2.0 : 0.0,
                        titleW);

                    double freeW = Math.Max(0.0, innerW - margin * 2.0 - leftZoneW - rightZoneW - (leftZoneW > 0 && rightZoneW > 0 ? gap : 0.0));
                    double extraColGap = freeW * 0.35;
                    double leftStartX = innerX0 + margin + freeW * 0.18;

                    // 左中区竖向均衡排布（正/侧立面在上，平面图在下）
                    double leftStackH = (orthoH > 0 ? orthoH + belowOrtho : 0.0)
                                      + (hasPlan ? planH + belowOrtho : 0.0);
                    double leftAvailH = innerH - margin * 2.0;
                    double leftFreeH = Math.Max(0.0, leftAvailH - leftStackH);
                    double orthoTop = innerY1 - margin - leftFreeH * 0.30;
                    double orthoBottom = orthoTop - orthoH;

                    double cursorX = leftStartX;

                    if (hasFront)
                    {
                        double frontX = cursorX + dimPad;
                        double slotY = orthoBottom + (orthoH - frontH) * 0.5;
                        Point3d viewBase = new Point3d(frontX - frontBounds.MinX, slotY - frontBounds.MinY, 0);
                        DrawOrthographicHlrView(
                            tr, ms, p, parts3d, roleToPartNo, profiles[AutoCadSolprof.ViewSpec.Front.Key], viewBase,
                            frontProj, pt => pt.Y, "正立面图",
                            frontBounds.Width, frontBounds.Height,
                            isFrontView: true, isSideView: false, isPlanView: false,
                            options, simSunTextStyleId, simSunDimStyleId, textH, dimGapStep, groupSheet);
                        cursorX = frontX + frontW + colGap + extraColGap * 0.45;
                    }

                    if (hasSide)
                    {
                        double sideX = cursorX + dimPad;
                        double slotY = orthoBottom + (orthoH - sideH) * 0.5;
                        Point3d viewBase = new Point3d(sideX - sideBounds.MinX, slotY - sideBounds.MinY, 0);
                        DrawOrthographicHlrView(
                            tr, ms, p, parts3d, roleToPartNo, profiles[AutoCadSolprof.ViewSpec.Side.Key], viewBase,
                            sideProj, pt => pt.X, "侧立面图",
                            sideBounds.Width, sideBounds.Height,
                            isFrontView: false, isSideView: true, isPlanView: false,
                            options, simSunTextStyleId, simSunDimStyleId, textH, dimGapStep, groupSheet);
                        cursorX = sideX + sideW + gap;
                    }

                    if (hasPlan)
                    {
                        double planLeft = leftStartX + dimPad;
                        double planBottom = (orthoH > 0 ? orthoBottom - belowOrtho - leftFreeH * 0.25 : innerY1 - margin) - planH;
                        double minPlanBottom = innerY0 + margin + belowOrtho;
                        if (planBottom < minPlanBottom) planBottom = minPlanBottom;
                        Point3d planBase = new Point3d(planLeft - planBounds.MinX, planBottom - planBounds.MinY, 0);
                        DrawOrthographicHlrView(
                            tr, ms, p, parts3d, roleToPartNo, profiles[AutoCadSolprof.ViewSpec.Plan.Key], planBase,
                            planProj, pt => pt.Z, "平面图",
                            planBounds.Width, planBounds.Height,
                            isFrontView: false, isSideView: false, isPlanView: true,
                            options, simSunTextStyleId, simSunDimStyleId, textH, dimGapStep, groupSheet);
                    }

                    // 右上区：西南等轴测图（严格控制在 [rightTableTop + 6*textH, innerY1 - margin] 与左右引线安全边距内，绝不穿出图框！）
                    if (hasIso)
                    {
                        double rightZoneLeft = Math.Max(cursorX, innerX1 - margin - rightZoneW);
                        double rightZoneRight = innerX1 - margin;
                        double availIsoW = Math.Max(isoW, (rightZoneRight - rightZoneLeft) - leaderSideReserve * 2.0);
                        double isoLeft = rightZoneLeft + leaderSideReserve + Math.Max(0.0, (availIsoW - isoW) * 0.5);
                        if (isoLeft + isoW + leaderSideReserve > innerX1 - margin * 0.6)
                        {
                            isoLeft = (innerX1 - margin * 0.6) - leaderSideReserve - isoW;
                        }

                        double isoFloorY = rightTableTop + 6.6 * textH;
                        double isoCeilY = innerY1 - margin - 4.4 * textH;
                        double isoFreeY = Math.Max(0.0, (isoCeilY - isoFloorY) - isoH);
                        double isoBottom = isoFloorY + isoFreeY * 0.52;
                        if (isoBottom + isoH > isoCeilY)
                        {
                            isoBottom = isoCeilY - isoH;
                        }

                        Point3d isoBase = new Point3d(isoLeft - isoBounds.MinX, isoBottom - isoBounds.MinY, 0);
                        DrawIsometricHlrView(
                            tr, ms, p, parts3d, billItems, roleToPartNo, profiles[AutoCadSolprof.ViewSpec.Isometric.Key], isoBase,
                            options, simSunTextStyleId, textH);
                    }

                    // 国标 A1~A4 标准图框与右下角紧凑标题栏
                    DrawStandardSheetFrameAndTitleBlock(
                        tr, ms, p, sheetOrigin,
                        options.SheetSizeName ?? "A2",
                        frameW, frameH, borderLeft, borderOther,
                        simSunTextStyleId, textH, scale, sheetCaption);

                    // 标题栏正上方：零缝隙、左右边框 100% 共线对齐的国标构件材料明细表（件号 #1, #2... 与轴测图引线一一对应）
                    if (hasBom)
                    {
                        Point3d bomBottomLeft = new Point3d(innerX1 - titleW, titleTop, 0);
                        AppendDetailedPartBomTable(
                            tr, ms, p, billItems,
                            bomBottomLeft, titleW, bomRowH, bomHeaderH,
                            simSunTextStyleId, textH);
                    }

                    tr.Commit();
                }
            }
            finally
            {
                if (profiles != null)
                {
                    foreach (AutoCadSolprof.SolprofView view in profiles.Values)
                        view.Dispose();
                }
                foreach (var item in parts3d)
                {
                    item.Solid?.Dispose();
                }
            }
        }

        private static Point2d IsoProject(Point3d pt)
        {
            AutoCadSolprof.ViewSpec iso = AutoCadSolprof.ViewSpec.Isometric;
            return new Point2d(
                pt.X * iso.Right.X + pt.Y * iso.Right.Y + pt.Z * iso.Right.Z,
                pt.X * iso.Up.X + pt.Y * iso.Up.Y + pt.Z * iso.Up.Z);
        }

        private static Dictionary<string, AutoCadSolprof.SolprofView> BuildSolprofViews(
            List<SteelStructureBuilder.SolidMemberPart> parts,
            bool front,
            bool side,
            bool plan,
            bool iso)
        {
            var specs = new List<AutoCadSolprof.ViewSpec>();
            if (front) specs.Add(AutoCadSolprof.ViewSpec.Front);
            if (side) specs.Add(AutoCadSolprof.ViewSpec.Side);
            if (plan) specs.Add(AutoCadSolprof.ViewSpec.Plan);
            if (iso) specs.Add(AutoCadSolprof.ViewSpec.Isometric);
            if (specs.Count == 0)
                return new Dictionary<string, AutoCadSolprof.SolprofView>();

            var solids = new List<Solid3d>();
            foreach (SteelStructureBuilder.SolidMemberPart part in parts)
            {
                if (part?.Solid != null && !part.Solid.IsDisposed)
                    solids.Add(part.Solid);
            }
            return AutoCadSolprof.Project(solids, specs);
        }


        private static double SelectBestScale(
            DrawingGenerationOptions options,
            double paperW,
            double paperH,
            int billCount,
            bool hasFront, ViewBounds front,
            bool hasSide, ViewBounds side,
            bool hasPlan, ViewBounds plan,
            bool hasIso, ViewBounds iso,
            bool hasBom)
        {
            double start = options.AutoScale ? 1.0 : Math.Max(1.0, options.DrawingScale);
            foreach (double s in StandardScales)
            {
                if (s + 1e-6 < start) continue;
                if (FitsSheet(s, paperW, paperH, billCount, hasFront, front, hasSide, side, hasPlan, plan, hasIso, iso, hasBom))
                    return s;
            }
            return StandardScales[StandardScales.Length - 1];
        }

        private static bool FitsSheet(
            double scale, double paperW, double paperH, int billCount,
            bool hasFront, ViewBounds front,
            bool hasSide, ViewBounds side,
            bool hasPlan, ViewBounds plan,
            bool hasIso, ViewBounds iso,
            bool hasBom)
        {
            double textH = 3.5 * scale;
            double dimGapStep = 3.2 * textH;
            double margin = 9.0 * scale;
            double gap = 14.0 * scale;
            double dimPad = dimGapStep * 2.55;
            double belowOrtho = dimGapStep * 2.15 + 4.2 * textH;
            double colGap = Math.Max(gap, dimPad * 1.15);

            double orthoRowW = (hasFront ? dimPad + front.Width : 0.0)
                             + (hasFront && hasSide ? colGap : 0.0)
                             + (hasSide ? dimPad + side.Width : 0.0);
            double planRowW = hasPlan ? dimPad + plan.Width : 0.0;
            double leftZoneW = Math.Max(orthoRowW, planRowW);

            double leaderSideReserve = hasIso ? 12.5 * textH : 0.0;
            double titleW = 180.0 * scale;
            double rightZoneW = Math.Max(
                hasIso ? iso.Width + leaderSideReserve * 2.0 : 0.0,
                hasBom ? titleW : titleW);

            double orthoH = Math.Max(hasFront ? front.Height : 0.0, hasSide ? side.Height : 0.0);
            double leftZoneH = (orthoH > 0 ? orthoH + belowOrtho : 0.0)
                             + (hasPlan ? plan.Height + belowOrtho : 0.0);

            double titleBlockH = 32.0 * scale;
            double bomRowH = textH * 2.05;
            double bomHeaderH = textH * 2.45;
            double bomH = hasBom ? bomHeaderH + (billCount + 2) * bomRowH : 0.0;
            double rightZoneH = titleBlockH + bomH + (hasIso ? iso.Height + 11.2 * textH : 0.0);

            double requiredW = margin * 2.0 + leftZoneW + (leftZoneW > 0 && rightZoneW > 0 ? gap : 0.0) + rightZoneW;
            double requiredH = margin * 2.0 + Math.Max(leftZoneH, rightZoneH);

            double innerW = (paperW - 35.0) * scale;
            double innerH = (paperH - 20.0) * scale;

            return requiredW <= innerW && requiredH <= innerH;
        }

        private static ViewBounds ComputeProjectedBounds(
            List<SteelStructureBuilder.SolidMemberPart> parts,
            Func<Point3d, Point2d> proj)
        {
            ViewBounds bounds = new ViewBounds
            {
                MinX = double.MaxValue,
                MaxX = double.MinValue,
                MinY = double.MaxValue,
                MaxY = double.MinValue
            };

            foreach (var part in parts)
            {
                List<Point2d> pts = GetPartProjectedSamplePoints(part, proj);
                foreach (Point2d pt in pts)
                {
                    if (pt.X < bounds.MinX) bounds.MinX = pt.X;
                    if (pt.X > bounds.MaxX) bounds.MaxX = pt.X;
                    if (pt.Y < bounds.MinY) bounds.MinY = pt.Y;
                    if (pt.Y > bounds.MaxY) bounds.MaxY = pt.Y;
                }
            }

            if (bounds.MinX > bounds.MaxX || bounds.MinY > bounds.MaxY)
            {
                bounds.MinX = 0; bounds.MaxX = 1000;
                bounds.MinY = 0; bounds.MaxY = 1000;
            }

            return bounds;
        }

        private static List<Point2d> GetPartProjectedSamplePoints(
            SteelStructureBuilder.SolidMemberPart part,
            Func<Point3d, Point2d> proj)
        {
            try
            {
                return GetPartCornerPoints(part).ConvertAll(pt => proj(pt));
            }
            catch
            {
                return new List<Point2d>();
            }
        }

        private static List<Point2d> GetPartHull(
            SteelStructureBuilder.SolidMemberPart part,
            Func<Point3d, Point2d> proj)
        {
            try
            {
                return ConvexHull(GetPartCornerPoints(part).ConvertAll(pt => proj(pt)));
            }
            catch
            {
                return new List<Point2d>();
            }
        }

        private static List<Point3d> GetPartCornerPoints(SteelStructureBuilder.SolidMemberPart part)
        {
            if (part.Section != null && !part.IsPlate &&
                part.PathPoints != null && part.PathPoints.Count >= 2)
            {
                List<Point2d> outline = SectionSolidBuilder.GetProfileOuterPoints(part.Section);
                var curved = new List<Point3d>(part.PathPoints.Count * outline.Count);
                IList<Point3d> path = part.PathPoints;
                int step = Math.Max(1, path.Count / 32);
                for (int i = 0; i < path.Count; i += step)
                {
                    Vector3d tangent = i < path.Count - 1 ? path[i + 1] - path[i] : path[i] - path[i - 1];
                    if (tangent.Length < 1e-3) continue;
                    SectionSolidBuilder.GetMemberAxes(tangent, out Vector3d xDir, out Vector3d yDir, out _);
                    foreach (Point2d p2 in outline)
                        curved.Add(path[i] + xDir * p2.X + yDir * p2.Y);
                }
                if (curved.Count >= 4) return curved;
            }

            Vector3d axis = part.EndPt - part.StartPt;
            if (part.Section != null && !part.IsPlate && axis.Length > 1.0)
            {
                SectionSolidBuilder.GetMemberAxes(axis, out Vector3d xDir, out Vector3d yDir, out _);

                List<Point2d> outline = SectionSolidBuilder.GetProfileOuterPoints(part.Section);
                var pts = new List<Point3d>(outline.Count * 2);
                foreach (Point3d center in new[] { part.StartPt, part.EndPt })
                {
                    foreach (Point2d p2 in outline)
                    {
                        pts.Add(center + xDir * p2.X + yDir * p2.Y);
                    }
                }
                return pts;
            }

            Extents3d ext = part.Solid.GeometricExtents;
            Point3d min = ext.MinPoint;
            Point3d max = ext.MaxPoint;
            return new List<Point3d>
            {
                new Point3d(min.X, min.Y, min.Z),
                new Point3d(max.X, min.Y, min.Z),
                new Point3d(max.X, max.Y, min.Z),
                new Point3d(min.X, max.Y, min.Z),
                new Point3d(min.X, min.Y, max.Z),
                new Point3d(max.X, min.Y, max.Z),
                new Point3d(max.X, max.Y, max.Z),
                new Point3d(min.X, max.Y, max.Z)
            };
        }

        private static List<Point2d> ConvexHull(List<Point2d> points)
        {
            if (points == null || points.Count <= 2) return points ?? new List<Point2d>();

            var sorted = points
                .OrderBy(p => p.X)
                .ThenBy(p => p.Y)
                .ToList();

            var lower = new List<Point2d>();
            foreach (Point2d pt in sorted)
            {
                while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], pt) <= 0)
                    lower.RemoveAt(lower.Count - 1);
                lower.Add(pt);
            }

            var upper = new List<Point2d>();
            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                Point2d pt = sorted[i];
                while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], pt) <= 0)
                    upper.RemoveAt(upper.Count - 1);
                upper.Add(pt);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower;
        }

        private static double Cross(Point2d o, Point2d a, Point2d b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }

        private static string PartKey(string tag, string role)
        {
            return (tag ?? string.Empty) + "|" + (role ?? string.Empty);
        }

        private static List<SteelStructureBuilder.SolidMemberPart> PlaceStructureInWorld(SteelStructureParams p)
        {
            var parts = SteelStructureBuilder.BuildDetailedParts(p);
            Matrix3d xf = SteelStructureBuilder.GetPlacementMatrix(p);
            foreach (var part in parts)
            {
                part.Solid?.TransformBy(xf);
                part.StartPt = part.StartPt.TransformBy(xf);
                part.EndPt = part.EndPt.TransformBy(xf);
                if (part.PathPoints != null)
                {
                    for (int i = 0; i < part.PathPoints.Count; i++)
                        part.PathPoints[i] = part.PathPoints[i].TransformBy(xf);
                }
            }
            return parts;
        }

        private static Dictionary<string, int> MapRoleToPartNo(
            List<SteelStructureBuilder.SolidMemberPart> parts3d,
            List<MemberPartBillItem> billItems)
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (billItems == null || billItems.Count == 0) return dict;

            foreach (var part in parts3d)
            {
                string key = PartKey(part.ParentTag, part.RoleName);
                if (string.IsNullOrEmpty(part.RoleName) || dict.ContainsKey(key))
                    continue;

                var candidates = billItems
                    .Where(b => string.Equals(b.ParentTag ?? "", part.ParentTag ?? "", StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(b.SectionName, part.SectionName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (candidates.Count == 0)
                {
                    candidates = billItems
                        .Where(b => string.Equals(b.ParentTag ?? "", part.ParentTag ?? "", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                MemberPartBillItem best = null;
                int bestScore = int.MinValue;

                foreach (var b in candidates)
                {
                    int score = 0;
                    if (!string.IsNullOrEmpty(b.PartRole))
                    {
                        if (string.Equals(b.PartRole, part.RoleName, StringComparison.OrdinalIgnoreCase)) score += 12;
                        else if (part.RoleName.IndexOf(b.PartRole, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 b.PartRole.IndexOf(part.RoleName, StringComparison.OrdinalIgnoreCase) >= 0) score += 8;
                    }

                    score += CategoryScore(part.RoleName, b.PartRole);
                    if (string.Equals(b.SectionName, part.SectionName, StringComparison.OrdinalIgnoreCase)) score += 3;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = b;
                    }
                }

                if (best != null && bestScore >= 2)
                {
                    dict[key] = best.PartNo;
                }
            }

            return dict;
        }

        private static int CategoryScore(string partRole, string billRole)
        {
            if (string.IsNullOrEmpty(partRole) || string.IsNullOrEmpty(billRole)) return 0;

            string[] categories =
            {
                "柱", "梁", "撑", "弦", "腹杆", "次梁", "踏板", "踏步", "踏棍", "扶手", "栏杆", "横杆",
                "护笼", "底板", "腹板", "肋", "钢板", "锚固板", "裙座", "鞍", "托", "牛腿", "轨道", "钢轨", "系杆"
            };
            foreach (string cat in categories)
            {
                if (partRole.IndexOf(cat, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    billRole.IndexOf(cat, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return 3;
                }
            }
            return 0;
        }

        /// <summary>
        /// 正立面、侧立面、平面消隐图：绘制真实截面轮廓、翼缘线、中心轴线，并标注准确的设计轴网尺寸（绝不标引线）
        /// </summary>
        private static void DrawOrthographicHlrView(
            Transaction tr,
            BlockTableRecord ms,
            SteelStructureParams p,
            List<SteelStructureBuilder.SolidMemberPart> parts3d,
            Dictionary<string, int> roleToPartNo,
            AutoCadSolprof.SolprofView profile,
            Point3d viewBase,
            Func<Point3d, Point2d> proj2d,
            Func<Point3d, double> depthFunc,
            string viewTitle,
            double nominalW,
            double nominalH,
            bool isFrontView,
            bool isSideView,
            bool isPlanView,
            DrawingGenerationOptions options,
            ObjectId textStyleId,
            ObjectId dimStyleId,
            double textH,
            double dimGapStep,
            bool overallOnly = false)
        {
            var boxes = new List<ProjectedPart2D>();
            double minViewX = double.MaxValue, maxViewX = double.MinValue;
            double minViewY = double.MaxValue, maxViewY = double.MinValue;

            foreach (var part in parts3d)
            {
                if (part.Solid == null) continue;
                try
                {
                    List<Point2d> samplePts = GetPartProjectedSamplePoints(part, proj2d);
                    if (samplePts.Count < 3) continue;
                    List<Point2d> hull = ConvexHull(samplePts);
                    if (hull.Count < 3) continue;

                    double minX = samplePts.Min(pt => pt.X);
                    double maxX = samplePts.Max(pt => pt.X);
                    double minY = samplePts.Min(pt => pt.Y);
                    double maxY = samplePts.Max(pt => pt.Y);

                    Extents3d ext = part.Solid.GeometricExtents;
                    double d1 = depthFunc(ext.MinPoint);
                    double d2 = depthFunc(ext.MaxPoint);

                    minViewX = Math.Min(minViewX, minX);
                    maxViewX = Math.Max(maxViewX, maxX);
                    minViewY = Math.Min(minViewY, minY);
                    maxViewY = Math.Max(maxViewY, maxY);

                    int partNo = roleToPartNo.TryGetValue(PartKey(part.ParentTag, part.RoleName), out int no) ? no : 0;
                    boxes.Add(new ProjectedPart2D
                    {
                        SourcePart = part,
                        RoleName = part.RoleName,
                        SectionName = part.SectionName,
                        PartNo = partNo,
                        IsPlate = part.IsPlate,
                        MinX = minX,
                        MaxX = maxX,
                        MinY = minY,
                        MaxY = maxY,
                        MinDepth = Math.Min(d1, d2),
                        MaxDepth = Math.Max(d1, d2),
                        Hull = hull
                    });
                }
                catch { }
            }

            if (boxes.Count == 0)
            {
                minViewX = 0; maxViewX = nominalW;
                minViewY = 0; maxViewY = nominalH;
            }

            if (profile == null || !profile.HasGeometry)
                throw new InvalidOperationException(viewTitle + "：AutoCAD SOLPROF 没有生成轮廓线。");

            if (profile.TryGetBounds(out double profileMinX, out double profileMaxX, out double profileMinY, out double profileMaxY))
            {
                minViewX = profileMinX;
                maxViewX = profileMaxX;
                minViewY = profileMinY;
                maxViewY = profileMaxY;
            }

            double paperScale = textH > 1.0 ? textH / 3.5 : 1.0;
            profile.Draw(tr, ms, viewBase, LayerVisible, LayerHidden, Math.Max(8.0, paperScale * 6.0));

            GetDesignDimensionTicks(
                p, minViewX, maxViewX, minViewY, maxViewY,
                isFrontView, isSideView, isPlanView, overallOnly,
                out List<double> xTicks, out List<double> yTicks);

            // 结构中心轴线（红点划线，伸出构件边缘 1.6 * textH，不再画左下角 L 型包框）
            DrawStructuralAxisLines(
                tr, ms, viewBase,
                minViewX, maxViewX, minViewY, maxViewY,
                xTicks, yTicks, isPlanView, textH);

            // 标准工程尺寸标注（分跨轴线尺寸 + 总尺寸，严格对齐设计值如 3600 / 2600 / 5200）
            if (options.IncludeAutoDimensions)
            {
                AddAccurateDimensions(
                    tr, ms, viewBase,
                    minViewX, maxViewX, minViewY, maxViewY,
                    xTicks, yTicks,
                    dimStyleId, textH, dimGapStep);
            }

            // 视图名称居中放置于最外层尺寸线下方
            double titleY = minViewY - dimGapStep * 2.15 - 2.6 * textH;
            double titleHalfW = EstimateSimSunTextWidth(viewTitle, textH * 1.12) * 0.5;
            Point3d titlePt = new Point3d(viewBase.X + (minViewX + maxViewX) * 0.5 - titleHalfW, viewBase.Y + titleY, 0);
            AppendSimSunText(tr, ms, titlePt, viewTitle, textStyleId, textH * 1.12, LayerFrame);
        }

        /// <summary>
        /// 绘制正投影单个构件：支持曲线路径扫掠构件、端视真实型钢截面（H/C/L/Box/Pipe）以及侧视翼缘厚度线
        /// </summary>
        private static void DrawOrthographicPart2D(
            Transaction tr,
            BlockTableRecord ms,
            Point3d viewBase,
            ProjectedPart2D projPart,
            Func<Point3d, Point2d> proj2d,
            string layer)
        {
            var part = projPart.SourcePart;
            if (part == null)
            {
                DrawClosedPolyline2D(tr, ms, viewBase, projPart.Hull, layer);
                return;
            }

            // 1. 连续多段/曲线扫掠杆件（如螺旋盘梯、环形平台、弧形桁架、连续栏杆转角）：沿路径绘制双边轮廓线，绝不画全局凸包
            if (part.Section != null && !part.IsPlate &&
                part.PathPoints != null && part.PathPoints.Count >= 2)
            {
                DrawSweptPathMember2D(tr, ms, viewBase, part, proj2d, layer);
                return;
            }

            // 2. 直杆型钢构件：判断是否为端视截面或侧视带翼缘厚度线
            if (part.Section != null && !part.IsPlate)
            {
                Point2d s2 = proj2d(part.StartPt);
                Point2d e2 = proj2d(part.EndPt);
                Vector2d axis2d = e2 - s2;
                Vector3d axis3d = part.EndPt - part.StartPt;

                if (axis3d.Length > 1.0 && axis2d.Length < 4.0)
                {
                    // 端视截面（例如平面图中的立柱、立面图中的垂直横梁）：直接绘制真实型钢外轮廓（H型钢工字形、槽钢C形等）
                    SectionSolidBuilder.GetMemberAxes(axis3d, out Vector3d xDir, out Vector3d yDir, out _);
                    List<Point2d> outline = SectionSolidBuilder.GetProfileOuterPoints(part.Section);
                    var endProfile2d = new List<Point2d>(outline.Count);
                    foreach (Point2d p2 in outline)
                    {
                        Point3d worldPt = part.StartPt + xDir * p2.X + yDir * p2.Y;
                        endProfile2d.Add(proj2d(worldPt));
                    }
                    DrawClosedPolyline2D(tr, ms, viewBase, endProfile2d, layer);
                    return;
                }

                // 侧视/斜视直杆：绘制外轮廓 + 翼缘厚度特征线（展现真实 H 型钢 / 槽钢双翼缘线）
                DrawClosedPolyline2D(tr, ms, viewBase, projPart.Hull, layer);

                if (axis3d.Length > 20.0 && axis2d.Length > 20.0 &&
                    (part.Section.ShapeType == SectionShapeType.HBeam ||
                     part.Section.ShapeType == SectionShapeType.Channel))
                {
                    SectionSolidBuilder.GetMemberAxes(axis3d, out Vector3d xDir, out Vector3d yDir, out _);
                    Vector2d yDir2d = proj2d(Point3d.Origin + yDir) - proj2d(Point3d.Origin);
                    if (yDir2d.Length > 0.65)
                    {
                        double halfInner = Math.Max(4.0, part.Section.H * 0.5 - part.Section.Tf);
                        foreach (double sign in new[] { -1.0, 1.0 })
                        {
                            Point2d a = proj2d(part.StartPt + yDir * (sign * halfInner));
                            Point2d b = proj2d(part.EndPt + yDir * (sign * halfInner));
                            AppendLine2D(tr, ms, viewBase, a.X, a.Y, b.X, b.Y, layer);
                        }
                    }
                }
                return;
            }

            // 3. 钢板与其它实心件
            DrawClosedPolyline2D(tr, ms, viewBase, projPart.Hull, layer);
        }

        private static void DrawSweptPathMember2D(
            Transaction tr,
            BlockTableRecord ms,
            Point3d basePt,
            SteelStructureBuilder.SolidMemberPart part,
            Func<Point3d, Point2d> proj2d,
            string layer)
        {
            IList<Point3d> path = part.PathPoints;
            if (path == null || path.Count < 2) return;

            double halfR = Math.Max(12.0, Math.Max(part.Section.H, part.Section.B) * 0.5);
            var center2d = new List<Point2d>(path.Count);
            foreach (Point3d pt in path)
                center2d.Add(proj2d(pt));

            var leftEdge = new List<Point2d>(center2d.Count);
            var rightEdge = new List<Point2d>(center2d.Count);

            for (int i = 0; i < center2d.Count; i++)
            {
                Vector2d dir;
                if (i == 0) dir = center2d[1] - center2d[0];
                else if (i == center2d.Count - 1) dir = center2d[i] - center2d[i - 1];
                else dir = center2d[i + 1] - center2d[i - 1];

                Vector2d n = dir.Length > 1e-3
                    ? new Vector2d(-dir.Y / dir.Length, dir.X / dir.Length)
                    : new Vector2d(0, 1);

                leftEdge.Add(center2d[i] + n * halfR);
                rightEdge.Add(center2d[i] - n * halfR);
            }

            DrawOpenPolyline2D(tr, ms, basePt, leftEdge, layer);
            DrawOpenPolyline2D(tr, ms, basePt, rightEdge, layer);
            if (leftEdge.Count >= 2)
            {
                AppendLine2D(tr, ms, basePt, leftEdge[0].X, leftEdge[0].Y, rightEdge[0].X, rightEdge[0].Y, layer);
                int last = leftEdge.Count - 1;
                AppendLine2D(tr, ms, basePt, leftEdge[last].X, leftEdge[last].Y, rightEdge[last].X, rightEdge[last].Y, layer);
            }
        }

        /// <summary>
        /// 计算正立面、侧立面、平面图的设计轴线网格刻度（保证标出 3600 / 1800 / 2600 / 5200 等精确工程设计值）
        /// </summary>
        private static void GetDesignDimensionTicks(
            SteelStructureParams p,
            double minViewX, double maxViewX,
            double minViewY, double maxViewY,
            bool isFrontView, bool isSideView, bool isPlanView,
            bool overallOnly,
            out List<double> xTicks,
            out List<double> yTicks)
        {
            xTicks = new List<double>();
            yTicks = new List<double>();

            if (overallOnly || p == null)
            {
                xTicks.Add(Math.Round(minViewX));
                xTicks.Add(Math.Round(maxViewX));
                yTicks.Add(Math.Round(minViewY));
                yTicks.Add(Math.Round(maxViewY));
                return;
            }

            double span = Math.Max(p.Span, 250.0);
            double len = Math.Max(p.Length, 200.0);
            double h = Math.Max(p.Height, 250.0);
            double subH = Math.Max(p.SubHeight, 80.0);

            // 判断是否为以原点 (0,0) 为中心的回转/对称构件
            bool isCenteredXY =
                p.StructureType == ParametricStructureType.SpiralStair ||
                p.StructureType == ParametricStructureType.VesselSupport ||
                p.StructureType == ParametricStructureType.TowerCircularPlatform ||
                p.StructureType == ParametricStructureType.VerticalVesselSkirt ||
                p.StructureType == ParametricStructureType.PipeShoeSupport ||
                p.StructureType == ParametricStructureType.TankRoofHandrail;

            // 1. 水平方向设计轴网刻度 (xTicks)
            if (isFrontView || isPlanView)
            {
                if (isCenteredXY)
                {
                    double halfW = Math.Round((maxViewX - minViewX) * 0.5);
                    if (p.StructureType == ParametricStructureType.VerticalVesselSkirt ||
                        p.StructureType == ParametricStructureType.TankRoofHandrail ||
                        p.StructureType == ParametricStructureType.SpiralStair)
                    {
                        halfW = Math.Round(span * 0.5);
                    }
                    else if (p.StructureType == ParametricStructureType.TowerCircularPlatform)
                    {
                        halfW = Math.Round(span * 0.5 + len);
                    }
                    xTicks.Add(-halfW);
                    xTicks.Add(0.0);
                    xTicks.Add(halfW);
                }
                else
                {
                    int bays = 1;
                    if (p.StructureType == ParametricStructureType.MultiSpanPortalFrame)
                        bays = Math.Max(2, Math.Min(6, p.BayCount));
                    else if (p.StructureType == ParametricStructureType.PipeBridgeTruss)
                        bays = Math.Max(3, (int)Math.Round(span / Math.Max(h, 1500.0)));
                    else if (p.StructureType == ParametricStructureType.BracedFrame ||
                             p.StructureType == ParametricStructureType.RoofTruss)
                        bays = 2;

                    for (int i = 0; i <= bays; i++)
                        xTicks.Add(Math.Round(i * (span / bays)));
                }
            }
            else if (isSideView)
            {
                if (isCenteredXY)
                {
                    double halfD = Math.Round((maxViewX - minViewX) * 0.5);
                    if (p.StructureType == ParametricStructureType.VerticalVesselSkirt ||
                        p.StructureType == ParametricStructureType.TankRoofHandrail ||
                        p.StructureType == ParametricStructureType.SpiralStair)
                    {
                        halfD = Math.Round(span * 0.5);
                    }
                    else if (p.StructureType == ParametricStructureType.VesselSupport ||
                             p.StructureType == ParametricStructureType.PipeShoeSupport)
                    {
                        halfD = Math.Round(len * 0.5);
                    }
                    xTicks.Add(-halfD);
                    xTicks.Add(halfD);
                }
                else if (p.StructureType == ParametricStructureType.SubstationGantryFrame)
                {
                    double halfSpread = Math.Max(len * 0.5, 650.0);
                    xTicks.Add(-Math.Round(halfSpread));
                    xTicks.Add(0.0);
                    xTicks.Add(Math.Round(halfSpread));
                }
                else if (maxViewX - minViewX > Math.Max(len * 0.65, 450.0) && minViewX >= -400.0 && maxViewX >= len * 0.8)
                {
                    if (p.StructureType == ParametricStructureType.MultiBaySpaceFrame)
                    {
                        xTicks.Add(0.0);
                        xTicks.Add(Math.Round(len * 0.5));
                        xTicks.Add(Math.Round(len));
                    }
                    else if (p.StructureType == ParametricStructureType.SpacePlatform)
                    {
                        xTicks.Add(0.0);
                        xTicks.Add(Math.Round(len / 3.0));
                        xTicks.Add(Math.Round(len * 2.0 / 3.0));
                        xTicks.Add(Math.Round(len));
                    }
                    else
                    {
                        xTicks.Add(0.0);
                        xTicks.Add(Math.Round(len));
                    }
                }
                else
                {
                    xTicks.Add(Math.Round(minViewX));
                    xTicks.Add(Math.Round(maxViewX));
                }
            }

            // 2. 竖向设计轴网刻度 (yTicks)
            if (isPlanView)
            {
                if (isCenteredXY)
                {
                    double halfD = Math.Round((maxViewY - minViewY) * 0.5);
                    if (p.StructureType == ParametricStructureType.VerticalVesselSkirt ||
                        p.StructureType == ParametricStructureType.TankRoofHandrail ||
                        p.StructureType == ParametricStructureType.SpiralStair)
                    {
                        halfD = Math.Round(span * 0.5);
                    }
                    else if (p.StructureType == ParametricStructureType.TowerCircularPlatform)
                    {
                        halfD = Math.Round(span * 0.5 + len);
                    }
                    yTicks.Add(-halfD);
                    yTicks.Add(0.0);
                    yTicks.Add(halfD);
                }
                else if (p.StructureType == ParametricStructureType.SubstationGantryFrame)
                {
                    double halfSpread = Math.Max(len * 0.5, 650.0);
                    yTicks.Add(-Math.Round(halfSpread));
                    yTicks.Add(Math.Round(halfSpread));
                }
                else if (maxViewY - minViewY > Math.Max(len * 0.65, 450.0) && minViewY >= -400.0 && maxViewY >= len * 0.8)
                {
                    if (p.StructureType == ParametricStructureType.MultiBaySpaceFrame)
                    {
                        yTicks.Add(0.0);
                        yTicks.Add(Math.Round(len * 0.5));
                        yTicks.Add(Math.Round(len));
                    }
                    else if (p.StructureType == ParametricStructureType.SpacePlatform)
                    {
                        yTicks.Add(0.0);
                        yTicks.Add(Math.Round(len / 3.0));
                        yTicks.Add(Math.Round(len * 2.0 / 3.0));
                        yTicks.Add(Math.Round(len));
                    }
                    else
                    {
                        yTicks.Add(0.0);
                        yTicks.Add(Math.Round(len));
                    }
                }
                else
                {
                    yTicks.Add(Math.Round(minViewY));
                    yTicks.Add(Math.Round(maxViewY));
                }
            }
            else
            {
                // 立面高度刻度（Z 方向）
                switch (p.StructureType)
                {
                    case ParametricStructureType.DoubleTierFrame:
                    case ParametricStructureType.MultiBaySpaceFrame:
                    case ParametricStructureType.BracedFrame:
                    case ParametricStructureType.CrossBracingFrame:
                    {
                        double midZ = Math.Round(Math.Min(Math.Max(subH, 400.0), h - 300.0));
                        yTicks.Add(0.0);
                        if (midZ > 150.0 && midZ < h - 150.0) yTicks.Add(midZ);
                        yTicks.Add(Math.Round(h));
                        break;
                    }
                    case ParametricStructureType.RoofTruss:
                    {
                        double rise = Math.Max(subH, 600.0);
                        yTicks.Add(0.0);
                        yTicks.Add(Math.Round(h));
                        yTicks.Add(Math.Round(h + rise));
                        break;
                    }
                    case ParametricStructureType.SpacePlatform:
                    case ParametricStructureType.CantileverWalkway:
                    case ParametricStructureType.StraightStair:
                    {
                        double railH = Math.Max(subH, 1000.0);
                        yTicks.Add(0.0);
                        yTicks.Add(Math.Round(h));
                        yTicks.Add(Math.Round(h + railH));
                        break;
                    }
                    case ParametricStructureType.SafetyCagedLadder:
                    {
                        yTicks.Add(0.0);
                        yTicks.Add(Math.Round(h));
                        yTicks.Add(Math.Round(h + 1100.0));
                        break;
                    }
                    case ParametricStructureType.SpiralStair:
                    {
                        yTicks.Add(0.0);
                        yTicks.Add(Math.Round(h));
                        yTicks.Add(Math.Round(h + 1050.0));
                        break;
                    }
                    case ParametricStructureType.SubstationGantryFrame:
                    {
                        double rodH = Math.Max(subH, 1500.0);
                        yTicks.Add(0.0);
                        yTicks.Add(Math.Round(h));
                        yTicks.Add(Math.Round(h + rodH));
                        break;
                    }
                    default:
                    {
                        if (Math.Abs(minViewY) < 80.0 && Math.Abs(maxViewY - h) < 200.0)
                        {
                            yTicks.Add(0.0);
                            yTicks.Add(Math.Round(h));
                        }
                        else
                        {
                            yTicks.Add(Math.Round(minViewY));
                            yTicks.Add(Math.Round(maxViewY));
                        }
                        break;
                    }
                }
            }

            xTicks = CleanSortedTicks(xTicks);
            yTicks = CleanSortedTicks(yTicks);
        }

        private static List<double> CleanSortedTicks(List<double> raw)
        {
            var sorted = raw.OrderBy(v => v).ToList();
            var clean = new List<double>();
            foreach (double v in sorted)
            {
                if (clean.Count == 0 || Math.Abs(v - clean[clean.Count - 1]) >= 20.0)
                    clean.Add(v);
            }
            if (clean.Count < 2 && sorted.Count >= 2)
            {
                clean.Clear();
                clean.Add(sorted[0]);
                clean.Add(sorted[sorted.Count - 1]);
            }
            return clean;
        }

        private static void DrawStructuralAxisLines(
            Transaction tr,
            BlockTableRecord ms,
            Point3d viewBase,
            double minViewX, double maxViewX,
            double minViewY, double maxViewY,
            List<double> xTicks,
            List<double> yTicks,
            bool isPlanView,
            double textH)
        {
            double ext = 1.6 * textH;

            // 竖向结构柱中心轴线
            if (xTicks != null && xTicks.Count >= 2)
            {
                foreach (double x in xTicks)
                {
                    AppendLine2D(tr, ms, viewBase, x, minViewY - ext, x, maxViewY + ext, LayerAxis);
                }
            }

            // 水平标高轴线 / 平面横向轴线
            if (yTicks != null && yTicks.Count >= 2)
            {
                if (isPlanView)
                {
                    foreach (double y in yTicks)
                    {
                        AppendLine2D(tr, ms, viewBase, minViewX - ext, y, maxViewX + ext, y, LayerAxis);
                    }
                }
                else
                {
                    // 立面图绘制地面基准线与中间层标高中心线
                    AppendLine2D(tr, ms, viewBase, minViewX - ext, yTicks[0], maxViewX + ext, yTicks[0], LayerAxis);
                    for (int i = 1; i < yTicks.Count - 1; i++)
                    {
                        AppendLine2D(tr, ms, viewBase, minViewX - ext * 0.6, yTicks[i], maxViewX + ext * 0.6, yTicks[i], LayerAxis);
                    }
                }
            }
        }

        private static void AddAccurateDimensions(
            Transaction tr,
            BlockTableRecord ms,
            Point3d viewBase,
            double minViewX, double maxViewX,
            double minViewY, double maxViewY,
            List<double> xTicks,
            List<double> yTicks,
            ObjectId dimStyleId,
            double textH,
            double dimGapStep)
        {
            // 1. 底部水平尺寸标注（第一道：分跨轴线尺寸；第二道：总宽尺寸）
            if (xTicks != null && xTicks.Count >= 2 && (xTicks[xTicks.Count - 1] - xTicks[0]) >= 5.0)
            {
                double yAnchor = minViewY;
                double yDimTier1 = viewBase.Y + minViewY - dimGapStep * 1.0;
                double yDimTier2 = viewBase.Y + minViewY - dimGapStep * 1.85;
                bool hasSubBays = xTicks.Count > 2;

                if (hasSubBays)
                {
                    for (int i = 0; i < xTicks.Count - 1; i++)
                    {
                        double x0 = xTicks[i];
                        double x1 = xTicks[i + 1];
                        AppendLinearDim(tr, ms,
                            new Point3d(viewBase.X + x0, viewBase.Y + yAnchor, 0),
                            new Point3d(viewBase.X + x1, viewBase.Y + yAnchor, 0),
                            new Point3d(viewBase.X + (x0 + x1) * 0.5, yDimTier1, 0),
                            0.0, dimStyleId, textH);
                    }
                }

                double xStart = xTicks[0];
                double xEnd = xTicks[xTicks.Count - 1];
                AppendLinearDim(tr, ms,
                    new Point3d(viewBase.X + xStart, viewBase.Y + yAnchor, 0),
                    new Point3d(viewBase.X + xEnd, viewBase.Y + yAnchor, 0),
                    new Point3d(viewBase.X + (xStart + xEnd) * 0.5, hasSubBays ? yDimTier2 : yDimTier1, 0),
                    0.0, dimStyleId, textH);
            }

            // 2. 左侧竖向尺寸标注（第一道：分层高/分跨深；第二道：总高/总深）
            if (yTicks != null && yTicks.Count >= 2 && (yTicks[yTicks.Count - 1] - yTicks[0]) >= 5.0)
            {
                double xAnchor = minViewX;
                double xDimTier1 = viewBase.X + minViewX - dimGapStep * 1.0;
                double xDimTier2 = viewBase.X + minViewX - dimGapStep * 1.85;
                bool hasSubTiers = yTicks.Count > 2;

                if (hasSubTiers)
                {
                    for (int i = 0; i < yTicks.Count - 1; i++)
                    {
                        double y0 = yTicks[i];
                        double y1 = yTicks[i + 1];
                        AppendLinearDim(tr, ms,
                            new Point3d(viewBase.X + xAnchor, viewBase.Y + y0, 0),
                            new Point3d(viewBase.X + xAnchor, viewBase.Y + y1, 0),
                            new Point3d(xDimTier1, viewBase.Y + (y0 + y1) * 0.5, 0),
                            Math.PI * 0.5, dimStyleId, textH);
                    }
                }

                double yStart = yTicks[0];
                double yEnd = yTicks[yTicks.Count - 1];
                AppendLinearDim(tr, ms,
                    new Point3d(viewBase.X + xAnchor, viewBase.Y + yStart, 0),
                    new Point3d(viewBase.X + xAnchor, viewBase.Y + yEnd, 0),
                    new Point3d(hasSubTiers ? xDimTier2 : xDimTier1, viewBase.Y + (yStart + yEnd) * 0.5, 0),
                    Math.PI * 0.5, dimStyleId, textH);
            }
        }

        /// <summary>
        /// 西南等轴测三维特征图：全实线清晰展现三维棱边与曲线路径，且仅在左右两侧标注水平件号引线 (#1, #2...)，与材料表 100% 对应！
        /// </summary>
        private static void DrawIsometricHlrView(
            Transaction tr,
            BlockTableRecord ms,
            SteelStructureParams p,
            List<SteelStructureBuilder.SolidMemberPart> parts3d,
            List<MemberPartBillItem> billItems,
            Dictionary<string, int> roleToPartNo,
            AutoCadSolprof.SolprofView profile,
            Point3d isoBase,
            DrawingGenerationOptions options,
            ObjectId textStyleId,
            double textH)
        {
            Func<Point3d, Point2d> isoProj = IsoProject;

            double minIsoX = double.MaxValue, maxIsoX = double.MinValue;
            double minIsoY = double.MaxValue, maxIsoY = double.MinValue;

            var isoParts = new List<IsoPart2D>();

            foreach (var part in parts3d)
            {
                if (part.Solid == null) continue;
                try
                {
                    List<Point2d> samplePts = GetPartProjectedSamplePoints(part, isoProj);
                    if (samplePts.Count < 3) continue;
                    List<Point2d> hull = ConvexHull(samplePts);
                    if (hull.Count < 3) continue;

                    double minX = samplePts.Min(pt => pt.X);
                    double maxX = samplePts.Max(pt => pt.X);
                    double minY = samplePts.Min(pt => pt.Y);
                    double maxY = samplePts.Max(pt => pt.Y);

                    Extents3d ext = part.Solid.GeometricExtents;
                    double depth = (ext.MinPoint.X + ext.MaxPoint.X +
                                    ext.MinPoint.Y + ext.MaxPoint.Y +
                                    ext.MinPoint.Z + ext.MaxPoint.Z) * 0.5;

                    Point3d anchor3d;
                    if (part.PathPoints != null && part.PathPoints.Count >= 2)
                    {
                        anchor3d = part.PathPoints[part.PathPoints.Count / 2];
                    }
                    else
                    {
                        anchor3d = part.StartPt + (part.EndPt - part.StartPt) * 0.5;
                    }
                    Point2d anchor2d = isoProj(anchor3d);

                    int partNo = roleToPartNo.TryGetValue(PartKey(part.ParentTag, part.RoleName), out int no) ? no : 0;
                    isoParts.Add(new IsoPart2D
                    {
                        SourcePart = part,
                        PartNo = partNo,
                        Hull = hull,
                        Anchor2D = anchor2d,
                        MinX = minX,
                        MaxX = maxX,
                        MinY = minY,
                        MaxY = maxY,
                        Depth = depth
                    });

                    minIsoX = Math.Min(minIsoX, minX);
                    maxIsoX = Math.Max(maxIsoX, maxX);
                    minIsoY = Math.Min(minIsoY, minY);
                    maxIsoY = Math.Max(maxIsoY, maxY);
                }
                catch { }
            }

            if (isoParts.Count == 0)
            {
                minIsoX = 0; maxIsoX = p.Span;
                minIsoY = 0; maxIsoY = p.Height;
            }
            if (profile == null || !profile.HasGeometry)
                throw new InvalidOperationException("轴测图：AutoCAD SOLPROF 没有生成轮廓线。");

            double paperScale = textH > 1.0 ? textH / 3.5 : 1.0;
            profile.Draw(tr, ms, isoBase, LayerVisible, LayerHidden, Math.Max(8.0, paperScale * 6.0));
            if (profile.TryGetBounds(out double profileMinX, out double profileMaxX, out double profileMinY, out double profileMaxY))
            {
                minIsoX = profileMinX;
                maxIsoX = profileMaxX;
                minIsoY = profileMinY;
                maxIsoY = profileMaxY;
            }

            if (options.IncludeLeaderAnnotations && isoParts.Count > 0)
            {
                var leaderReqs = new List<LeaderRequest>();
                double cx = (minIsoX + maxIsoX) * 0.5;

                // 收集所有已映射 PartNo 的构件组，同时补齐未直接命中但存在于 billItems 的件号
                var groupedByNo = isoParts
                    .Where(x => x.PartNo > 0)
                    .GroupBy(x => x.PartNo)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var targetPartNos = new SortedSet<int>(groupedByNo.Keys);
                if (billItems != null)
                {
                    foreach (var bi in billItems)
                    {
                        if (bi.PartNo > 0) targetPartNos.Add(bi.PartNo);
                    }
                }

                foreach (int partNo in targetPartNos)
                {
                    IsoPart2D rep = null;
                    if (groupedByNo.TryGetValue(partNo, out List<IsoPart2D> candidates) && candidates.Count > 0)
                    {
                        // 优先选取离轴测图左右两侧边缘更近的代表性构件，使水平引线最短且最清晰
                        rep = candidates.OrderByDescending(x =>
                        {
                            double dx = x.Anchor2D.X - cx;
                            double dy = x.Anchor2D.Y - ((minIsoY + maxIsoY) * 0.5);
                            return dx * dx + dy * dy;
                        }).First();
                    }
                    else
                    {
                        rep = isoParts[(partNo - 1) % isoParts.Count];
                    }

                    int side = rep.Anchor2D.X >= cx ? 1 : 0;
                    leaderReqs.Add(new LeaderRequest
                    {
                        AnchorPt = new Point3d(isoBase.X + rep.Anchor2D.X, isoBase.Y + rep.Anchor2D.Y, 0),
                        LabelText = $"#{partNo}",
                        Side = side
                    });
                }

                PlaceCollisionFreeLeaders(
                    tr, ms, leaderReqs,
                    isoBase.X + minIsoX,
                    isoBase.X + maxIsoX,
                    isoBase.Y + minIsoY,
                    isoBase.Y + maxIsoY,
                    textStyleId, textH);
            }

            string isoTitle = "轴测图 (仅标件号)";
            double titleHalfW = EstimateSimSunTextWidth(isoTitle, textH * 1.12) * 0.5;
            Point3d titlePt = new Point3d(
                isoBase.X + (minIsoX + maxIsoX) * 0.5 - titleHalfW,
                isoBase.Y + minIsoY - 4.0 * textH,
                0);
            AppendSimSunText(
                tr, ms, titlePt,
                isoTitle,
                textStyleId, textH * 1.12, LayerFrame);
        }

        /// <summary>
        /// 绘制轴测图中单个三维构件的外轮廓、端部截面及纵向三维棱线
        /// </summary>
        private static void DrawIsoMember2D(
            Transaction tr,
            BlockTableRecord ms,
            Point3d isoBase,
            IsoPart2D isoPart,
            Func<Point3d, Point2d> isoProj,
            string layer)
        {
            var part = isoPart.SourcePart;
            if (part == null)
            {
                DrawClosedPolyline2D(tr, ms, isoBase, isoPart.Hull, layer);
                return;
            }

            // 1. 曲线/多段扫掠构件：沿路径绘制平滑双轨轮廓
            if (part.Section != null && !part.IsPlate &&
                part.PathPoints != null && part.PathPoints.Count >= 2)
            {
                DrawSweptPathMember2D(tr, ms, isoBase, part, isoProj, layer);
                return;
            }

            // 2. 直杆型钢构件：绘制外包轮廓 + 端部截面 + 内部三维纵向特征棱线
            Vector3d axis = part.EndPt - part.StartPt;
            if (part.Section != null && !part.IsPlate && axis.Length > 1.0)
            {
                DrawClosedPolyline2D(tr, ms, isoBase, isoPart.Hull, layer);

                SectionSolidBuilder.GetMemberAxes(axis, out Vector3d xDir, out Vector3d yDir, out _);
                double hx = part.Section.B * 0.5;
                double hy = part.Section.H * 0.5;

                Point3d[] boxCornersStart =
                {
                    part.StartPt - xDir * hx - yDir * hy,
                    part.StartPt + xDir * hx - yDir * hy,
                    part.StartPt + xDir * hx + yDir * hy,
                    part.StartPt - xDir * hx + yDir * hy
                };
                Point3d[] boxCornersEnd =
                {
                    part.EndPt - xDir * hx - yDir * hy,
                    part.EndPt + xDir * hx - yDir * hy,
                    part.EndPt + xDir * hx + yDir * hy,
                    part.EndPt - xDir * hx + yDir * hy
                };

                // 绘制最靠近观察者的前向纵向棱边，赋予轴测图真实的三维立体感
                int frontCornerIdx = 0;
                double minCornerDepth = double.MaxValue;
                for (int k = 0; k < 4; k++)
                {
                    Point3d midCorner = boxCornersStart[k] + (boxCornersEnd[k] - boxCornersStart[k]) * 0.5;
                    double d = -(midCorner.X + midCorner.Y) + midCorner.Z * 0.35;
                    if (d < minCornerDepth)
                    {
                        minCornerDepth = d;
                        frontCornerIdx = k;
                    }
                }

                Point2d cStart2d = isoProj(boxCornersStart[frontCornerIdx]);
                Point2d cEnd2d = isoProj(boxCornersEnd[frontCornerIdx]);
                AppendLine2D(tr, ms, isoBase, cStart2d.X, cStart2d.Y, cEnd2d.X, cEnd2d.Y, layer);

                // 绘制端盖轮廓
                var endCap = boxCornersEnd.Select(isoProj).ToList();
                DrawClosedPolyline2D(tr, ms, isoBase, endCap, layer);
                return;
            }

            // 3. 钢板与其它实心构件
            DrawClosedPolyline2D(tr, ms, isoBase, isoPart.Hull, layer);
        }

        /// <summary>
        /// Places isometric part-number balloons on a ring around the view.
        /// Slots are evenly spaced and matched in angular order so leaders do not cross.
        /// The downward arc stays open for the view title.
        /// </summary>
        private static void PlaceCollisionFreeLeaders(
            Transaction tr,
            BlockTableRecord ms,
            List<LeaderRequest> requests,
            double viewMinX,
            double viewMaxX,
            double viewMinY,
            double viewMaxY,
            ObjectId textStyleId,
            double textH)
        {
            if (requests == null || requests.Count == 0) return;

            int n = requests.Count;
            double cx = (viewMinX + viewMaxX) * 0.5;
            double cy = (viewMinY + viewMaxY) * 0.5;
            double hx = Math.Max((viewMaxX - viewMinX) * 0.5, textH);
            double hy = Math.Max((viewMaxY - viewMinY) * 0.5, textH);

            double textW = 2.2 * textH;
            foreach (LeaderRequest req in requests)
            {
                if (req == null || string.IsNullOrEmpty(req.LabelText)) continue;
                textW = Math.Max(textW, EstimateSimSunTextWidth(req.LabelText, textH));
            }

            double forbid = 0.90;
            double arc = Math.PI * 2.0 - forbid * 2.0;
            double startAng = -Math.PI / 2.0 + forbid;
            int rings = n > 14 ? 2 : 1;
            double step = arc / Math.Max(1, n);
            double sameStep = step * rings;
            double chordNeed = textW + 0.9 * textH;
            double rNeed = n <= 2
                ? hx + 2.8 * textH
                : chordNeed / (2.0 * Math.Sin(Math.Max(0.16, sameStep * 0.5)));
            double extra = rNeed - Math.Min(hx, hy);
            if (double.IsNaN(extra) || extra < 2.6 * textH) extra = 2.6 * textH;
            if (extra > 7.4 * textH) extra = 7.4 * textH;

            double rx0 = hx + extra;
            double ry0 = hy + Math.Min(2.6 * textH, extra);
            double rx1 = rx0 + 3.5 * textH;
            double ry1 = ry0 + 2.3 * textH;

            var ordered = new List<Tuple<LeaderRequest, double>>();
            foreach (LeaderRequest req in requests)
            {
                if (req == null) continue;
                double key = UnwrapLeaderAngle(
                    Math.Atan2(req.AnchorPt.Y - cy, req.AnchorPt.X - cx), startAng, arc);
                ordered.Add(Tuple.Create(req, key));
            }
            ordered = ordered.OrderBy(x => x.Item2).ThenBy(x => x.Item1.AnchorPt.X).ToList();
            n = ordered.Count;
            if (n == 0) return;

            for (int i = 0; i < n; i++)
            {
                double ang = n == 1 ? startAng + ordered[0].Item2 : startAng + arc * (i + 0.5) / n;
                int ring = (rings == 2 && (i % 2 == 1)) ? 1 : 0;
                DrawRingLeader(
                    tr, ms, ordered[i].Item1, cx, cy,
                    ring == 0 ? rx0 : rx1,
                    ring == 0 ? ry0 : ry1,
                    ang, textStyleId, textH);
            }
        }

        private static double UnwrapLeaderAngle(double angle, double startAng, double arc)
        {
            double d = angle - startAng;
            double twoPi = Math.PI * 2.0;
            while (d < 0) d += twoPi;
            while (d >= twoPi) d -= twoPi;
            if (d > arc)
            {
                double into = d - arc;
                double back = twoPi - d;
                d = into <= back ? arc : 0.0;
            }
            return d;
        }

        private static void DrawRingLeader(
            Transaction tr,
            BlockTableRecord ms,
            LeaderRequest req,
            double cx,
            double cy,
            double rx,
            double ry,
            double ang,
            ObjectId textStyleId,
            double textH)
        {
            if (req == null || string.IsNullOrEmpty(req.LabelText)) return;
            double cos = Math.Cos(ang);
            double sin = Math.Sin(ang);
            var elbow = new Point3d(cx + rx * cos, cy + ry * sin, 0);
            double dir = cos >= 0.0 ? 1.0 : -1.0;
            double tick = 0.95 * textH;
            var tip = new Point3d(elbow.X + dir * tick, elbow.Y, 0);
            double labelW = Math.Max(EstimateSimSunTextWidth(req.LabelText, textH), 2.1 * textH);
            Point3d textPos = dir > 0
                ? new Point3d(tip.X + 0.22 * textH, tip.Y - 0.35 * textH, 0)
                : new Point3d(tip.X - labelW - 0.22 * textH, tip.Y - 0.35 * textH, 0);

            Point3d anchor = req.AnchorPt;
            if (anchor.DistanceTo(elbow) < 0.45 * textH)
                elbow = new Point3d(anchor.X + cos * 1.2 * textH, anchor.Y + sin * 1.2 * textH, 0);

            using (Leader ld = new Leader())
            {
                ld.SetDatabaseDefaults();
                ld.Layer = LayerDim;
                ld.AppendVertex(anchor);
                ld.AppendVertex(elbow);
                if (elbow.DistanceTo(tip) > 0.2 * textH)
                    ld.AppendVertex(tip);
                ld.HasArrowHead = true;
                ld.Dimasz = textH * 0.70;
                ms.AppendEntity(ld);
                tr.AddNewlyCreatedDBObject(ld, true);
            }
            AppendSimSunText(tr, ms, textPos, req.LabelText, textStyleId, textH, LayerDim);
        }

        /// <summary>
        /// 在标题栏正上方绘制国标构件材料明细表（采用确定性网格与居中文字，与 180*scale 标题栏零缝隙、左右边框 100% 严格共线对齐）
        /// </summary>
        private static void AppendDetailedPartBomTable(
            Transaction tr,
            BlockTableRecord ms,
            SteelStructureParams p,
            List<MemberPartBillItem> billItems,
            Point3d bottomLeft,
            double totalWidth,
            double rowH,
            double titleRowH,
            ObjectId textStyleId,
            double textH)
        {
            if (billItems == null) billItems = new List<MemberPartBillItem>();

            int dataRows = billItems.Count;
            int gridRows = dataRows + 2; // 表头行 + 明细行 + 合计行
            double gridH = gridRows * rowH;
            double totalH = gridH + titleRowH;

            // 外框与右下角标题栏及内图框完全共线（宽度严格等于 totalWidth = 180 * scale，底边严格等于 titleTop）
            DrawRectangle2D(tr, ms, bottomLeft, 0, 0, totalWidth, totalH, LayerFrame);

            // 顶部表名通栏分隔线
            AppendLine2D(tr, ms, bottomLeft, 0, gridH, totalWidth, gridH, LayerFrame);
            AppendCenteredSimSunText(
                tr, ms,
                new Point3d(bottomLeft.X + totalWidth * 0.5, bottomLeft.Y + gridH + titleRowH * 0.5, 0),
                $"材料明细表 ({p.Tag})",
                textStyleId, textH * 1.05, LayerFrame);

            // 各列宽度比例（总和严格等于 1.00）
            double[] ratios = { 0.09, 0.21, 0.26, 0.11, 0.09, 0.11, 0.13 };
            double[] colX = new double[ratios.Length + 1];
            colX[0] = 0.0;
            for (int c = 0; c < ratios.Length; c++)
            {
                colX[c + 1] = colX[c] + totalWidth * ratios[c];
            }
            colX[ratios.Length] = totalWidth;

            // 内部竖向列分隔线
            for (int c = 1; c < ratios.Length; c++)
            {
                AppendLine2D(tr, ms, bottomLeft, colX[c], 0, colX[c], gridH, LayerFrame);
            }

            // 内部水平行分隔线
            for (int r = 1; r < gridRows; r++)
            {
                double y = r * rowH;
                AppendLine2D(tr, ms, bottomLeft, 0, y, totalWidth, y, LayerFrame);
            }

            // 表头行（位于 gridH - rowH .. gridH）
            string[] headers = { "件号", "部位", "截面规格", "材质", "数量", "单长", "重(kg)" };
            double headerCenterY = bottomLeft.Y + gridH - rowH * 0.5;
            for (int c = 0; c < headers.Length; c++)
            {
                double cx = bottomLeft.X + (colX[c] + colX[c + 1]) * 0.5;
                AppendCenteredSimSunText(tr, ms, new Point3d(cx, headerCenterY, 0), headers[c], textStyleId, textH, LayerFrame);
            }

            // 构件明细数据行（从上往下排）
            double sumKg = 0.0;
            for (int i = 0; i < dataRows; i++)
            {
                var item = billItems[i];
                double rowCenterY = bottomLeft.Y + (gridRows - 1 - i) * rowH - rowH * 0.5;
                string[] vals =
                {
                    $"#{item.PartNo}",
                    item.PartRole ?? string.Empty,
                    item.SectionName ?? string.Empty,
                    item.Material ?? string.Empty,
                    item.Count.ToString(CultureInfo.InvariantCulture),
                    item.SingleLengthMm.ToString("0", CultureInfo.InvariantCulture),
                    item.TotalWeightKg.ToString("0.00", CultureInfo.InvariantCulture)
                };

                for (int c = 0; c < vals.Length; c++)
                {
                    double cx = bottomLeft.X + (colX[c] + colX[c + 1]) * 0.5;
                    AppendCenteredSimSunText(tr, ms, new Point3d(cx, rowCenterY, 0), vals[c], textStyleId, textH * 0.96, LayerFrame);
                }
                sumKg += item.TotalWeightKg;
            }

            // 底部合计行（位于 0 .. rowH）
            double totalRowCenterY = bottomLeft.Y + rowH * 0.5;
            AppendCenteredSimSunText(
                tr, ms,
                new Point3d(bottomLeft.X + (colX[0] + colX[1]) * 0.5, totalRowCenterY, 0),
                "合计", textStyleId, textH, LayerFrame);
            AppendCenteredSimSunText(
                tr, ms,
                new Point3d(bottomLeft.X + (colX[2] + colX[3]) * 0.5, totalRowCenterY, 0),
                $"总重: {sumKg:0.00} kg", textStyleId, textH * 0.96, LayerFrame);
            AppendCenteredSimSunText(
                tr, ms,
                new Point3d(bottomLeft.X + (colX[6] + colX[7]) * 0.5, totalRowCenterY, 0),
                sumKg.ToString("0.00", CultureInfo.InvariantCulture), textStyleId, textH * 0.96, LayerFrame);
        }

        /// <summary>
        /// 绘制标准国家制图图框 (A1 / A2 / A3 / A4) 及右下角紧凑标题栏
        /// </summary>
        private static void DrawStandardSheetFrameAndTitleBlock(
            Transaction tr,
            BlockTableRecord ms,
            SteelStructureParams p,
            Point3d origin,
            string sheetSizeName,
            double frameW,
            double frameH,
            double borderLeft,
            double borderOther,
            ObjectId textStyleId,
            double textH,
            double scale,
            string sheetCaption = null)
        {
            // 外图框（细实线）
            DrawRectangle2D(tr, ms, origin, 0, 0, frameW, frameH, LayerFrame);
            // 内图框（装订边 25mm，其余 10mm）
            DrawRectangle2D(tr, ms, origin, borderLeft, borderOther, frameW - borderOther, frameH - borderOther, LayerFrame);

            // 国标右下角紧凑标题栏 (180mm x 32mm * scale)
            double tbW = 180.0 * scale;
            double tbH = 32.0 * scale;
            Point3d tbMin = origin + new Vector3d(frameW - borderOther - tbW, borderOther, 0);
            DrawRectangle2D(tr, ms, tbMin, 0, 0, tbW, tbH, LayerFrame);
            AppendLine2D(tr, ms, tbMin, 0, tbH * 0.5, tbW, tbH * 0.5, LayerFrame);
            AppendLine2D(tr, ms, tbMin, tbW * 0.64, 0, tbW * 0.64, tbH, LayerFrame);

            AppendSimSunText(tr, ms, tbMin + new Vector3d(1.5 * textH, tbH * 0.62, 0),
                sheetCaption ?? $"图名: {p.StructureTypeDisplayName} 详图 ({p.Tag})", textStyleId, textH * 1.02, LayerFrame);
            AppendSimSunText(tr, ms, tbMin + new Vector3d(1.5 * textH, tbH * 0.14, 0),
                $"主材: {p.ColumnSection} / {p.BeamSection} ({p.Material})", textStyleId, textH * 0.90, LayerFrame);
            AppendSimSunText(tr, ms, tbMin + new Vector3d(tbW * 0.66, tbH * 0.62, 0),
                $"图幅: {sheetSizeName}   比例: 1:{scale:0.#}", textStyleId, textH * 0.96, LayerFrame);
            AppendSimSunText(tr, ms, tbMin + new Vector3d(tbW * 0.66, tbH * 0.14, 0),
                $"日期: {DateTime.Now:yyyy-MM-dd}", textStyleId, textH * 0.96, LayerFrame);
        }

        private static void DrawRectangle2D(
            Transaction tr,
            BlockTableRecord ms,
            Point3d basePt,
            double minX,
            double minY,
            double maxX,
            double maxY,
            string layer)
        {
            Autodesk.AutoCAD.DatabaseServices.Polyline pl = new Autodesk.AutoCAD.DatabaseServices.Polyline(4);
            pl.SetDatabaseDefaults();
            pl.Layer = layer;
            pl.AddVertexAt(0, new Point2d(basePt.X + minX, basePt.Y + minY), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(basePt.X + maxX, basePt.Y + minY), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(basePt.X + maxX, basePt.Y + maxY), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(basePt.X + minX, basePt.Y + maxY), 0, 0, 0);
            pl.Closed = true;
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static void DrawClosedPolyline2D(
            Transaction tr,
            BlockTableRecord ms,
            Point3d basePt,
            List<Point2d> hull,
            string layer)
        {
            if (hull == null || hull.Count < 3) return;

            Autodesk.AutoCAD.DatabaseServices.Polyline pl =
                new Autodesk.AutoCAD.DatabaseServices.Polyline(hull.Count);
            pl.SetDatabaseDefaults();
            pl.Layer = layer;
            for (int i = 0; i < hull.Count; i++)
            {
                pl.AddVertexAt(i, new Point2d(basePt.X + hull[i].X, basePt.Y + hull[i].Y), 0, 0, 0);
            }
            pl.Closed = true;
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static void DrawOpenPolyline2D(
            Transaction tr,
            BlockTableRecord ms,
            Point3d basePt,
            List<Point2d> pts,
            string layer)
        {
            if (pts == null || pts.Count < 2) return;

            Autodesk.AutoCAD.DatabaseServices.Polyline pl =
                new Autodesk.AutoCAD.DatabaseServices.Polyline(pts.Count);
            pl.SetDatabaseDefaults();
            pl.Layer = layer;
            for (int i = 0; i < pts.Count; i++)
            {
                pl.AddVertexAt(i, new Point2d(basePt.X + pts[i].X, basePt.Y + pts[i].Y), 0, 0, 0);
            }
            pl.Closed = false;
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static void AppendLine2D(
            Transaction tr,
            BlockTableRecord ms,
            Point3d basePt,
            double x1,
            double y1,
            double x2,
            double y2,
            string layer,
            double linetypeScale = 0)
        {
            Line ln = new Line(
                new Point3d(basePt.X + x1, basePt.Y + y1, 0),
                new Point3d(basePt.X + x2, basePt.Y + y2, 0));
            ln.SetDatabaseDefaults(ms.Database);
            ln.Layer = layer;
            ln.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
            if (layer == LayerHidden)
            {
                // acad.lin 的 DASHED 周期只有 0.75 个图形单位，不放大的话在米级构件上会看成实线。
                double len = Math.Max(ln.Length, 80.0);
                ln.LinetypeScale = linetypeScale > 1.0
                    ? linetypeScale
                    : Math.Max(20.0, len / 14.0);
            }
            ms.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
        }

        private static void AppendLinearDim(
            Transaction tr,
            BlockTableRecord ms,
            Point3d pt1,
            Point3d pt2,
            Point3d dimLinePt,
            double rotRad,
            ObjectId dimStyleId,
            double textH)
        {
            if (pt1.DistanceTo(pt2) < 1.0) return;

            RotatedDimension dim = new RotatedDimension(
                rotRad, pt1, pt2, dimLinePt, string.Empty, dimStyleId);
            dim.SetDatabaseDefaults();
            dim.Layer = LayerDim;
            dim.Dimtxt = textH;
            dim.Dimasz = textH * 0.72;
            dim.Dimgap = textH * 0.30;
            dim.Dimexe = textH * 0.45;
            dim.Dimexo = textH * 0.35;
            dim.Dimdec = 0;
            dim.Dimtad = 1;
            dim.Dimtih = false;
            dim.Dimtoh = false;
            ms.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
        }

        private static void AppendSimSunText(
            Transaction tr,
            BlockTableRecord ms,
            Point3d pos,
            string content,
            ObjectId textStyleId,
            double height,
            string layer)
        {
            DBText txt = new DBText();
            txt.SetDatabaseDefaults();
            txt.Layer = layer;
            txt.TextStyleId = textStyleId;
            txt.Position = pos;
            txt.Height = height;
            txt.WidthFactor = 0.8;
            txt.TextString = content ?? string.Empty;
            ms.AppendEntity(txt);
            tr.AddNewlyCreatedDBObject(txt, true);
        }

        private static double EstimateSimSunTextWidth(string content, double height)
        {
            if (string.IsNullOrEmpty(content)) return 0.0;
            double width = 0.0;
            foreach (char c in content)
            {
                width += (c > 127 ? 0.80 : 0.50) * height;
            }
            return width;
        }

        private static void AppendCenteredSimSunText(
            Transaction tr,
            BlockTableRecord ms,
            Point3d centerPt,
            string content,
            ObjectId textStyleId,
            double height,
            string layer)
        {
            string s = content ?? string.Empty;
            double w = EstimateSimSunTextWidth(s, height);
            Point3d pos = new Point3d(centerPt.X - w * 0.5, centerPt.Y - height * 0.38, 0);
            AppendSimSunText(tr, ms, pos, s, textStyleId, height, layer);
        }

        public static ObjectId EnsureSimSunTextStyle(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                ObjectId styleId;
                if (tst.Has(TextStyleName))
                {
                    styleId = tst[TextStyleName];
                    TextStyleTableRecord existing = (TextStyleTableRecord)tr.GetObject(styleId, OpenMode.ForWrite);
                    existing.Font = new FontDescriptor("宋体", false, false, 134, 0);
                    existing.XScale = 0.8;
                }
                else
                {
                    tst.UpgradeOpen();
                    TextStyleTableRecord rec = new TextStyleTableRecord
                    {
                        Name = TextStyleName,
                        Font = new FontDescriptor("宋体", false, false, 134, 0),
                        XScale = 0.8
                    };
                    styleId = tst.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
                tr.Commit();
                return styleId;
            }
        }

        private static ObjectId EnsureSimSunDimStyle(Database db, ObjectId textStyleId, double textH)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DimStyleTable dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                ObjectId dimStyleId;
                if (dst.Has(DimStyleName))
                {
                    dimStyleId = dst[DimStyleName];
                    DimStyleTableRecord rec = (DimStyleTableRecord)tr.GetObject(dimStyleId, OpenMode.ForWrite);
                    rec.Dimtxsty = textStyleId;
                    rec.Dimtxt = textH;
                    rec.Dimasz = textH * 0.72;
                    rec.Dimdec = 0;
                    rec.Dimtad = 1;
                    rec.Dimtih = false;
                    rec.Dimtoh = false;
                }
                else
                {
                    dst.UpgradeOpen();
                    DimStyleTableRecord rec = new DimStyleTableRecord
                    {
                        Name = DimStyleName,
                        Dimtxsty = textStyleId,
                        Dimtxt = textH,
                        Dimasz = textH * 0.72,
                        Dimdec = 0,
                        Dimtad = 1,
                        Dimtih = false,
                        Dimtoh = false
                    };
                    dimStyleId = dst.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
                tr.Commit();
                return dimStyleId;
            }
        }

        private static void EnsureDrawingLayers(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                LinetypeTable ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);

                ObjectId dashedId = EnsureLinetype(db, tr, ltt, "DASHED");
                ObjectId centerId = EnsureLinetype(db, tr, ltt, "CENTER");

                CreateOrUpdateLayer(tr, lt, LayerVisible, 7, ObjectId.Null, LineWeight.LineWeight030);
                CreateOrUpdateLayer(tr, lt, LayerHidden, 8, dashedId, LineWeight.LineWeight015);
                CreateOrUpdateLayer(tr, lt, LayerAxis, 1, centerId, LineWeight.LineWeight013);
                CreateOrUpdateLayer(tr, lt, LayerDim, 3, ObjectId.Null, LineWeight.LineWeight018);
                CreateOrUpdateLayer(tr, lt, LayerFrame, 7, ObjectId.Null, LineWeight.LineWeight025);

                tr.Commit();
            }
        }

        private static ObjectId EnsureLinetype(Database db, Transaction tr, LinetypeTable ltt, string name)
        {
            if (ltt.Has(name)) return ltt[name];
            try
            {
                db.LoadLineTypeFile(name, "acad.lin");
                if (ltt.Has(name)) return ltt[name];
            }
            catch { }
            return db.ContinuousLinetype;
        }

        private static void CreateOrUpdateLayer(
            Transaction tr,
            LayerTable lt,
            string name,
            short colorIndex,
            ObjectId linetypeId,
            LineWeight lw)
        {
            if (lt.Has(name))
            {
                LayerTableRecord existing = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
                existing.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
                existing.LineWeight = lw;
                if (!linetypeId.IsNull) existing.LinetypeObjectId = linetypeId;
                return;
            }
            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex),
                LineWeight = lw
            };
            if (!linetypeId.IsNull) ltr.LinetypeObjectId = linetypeId;
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }
    }

    /// <summary>
    /// 把指定数据库临时设为当前工作数据库。
    /// 在 new Database 上建实体、装线型、SaveAs 都必须处于这个作用域内，否则抛 eWrongDatabase。
    /// </summary>
    internal sealed class WorkingDatabaseScope : System.IDisposable
    {
        private readonly Database _previous;
        private bool _disposed;

        private WorkingDatabaseScope(Database db)
        {
            _previous = HostApplicationServices.WorkingDatabase;
            HostApplicationServices.WorkingDatabase = db;
        }

        public static WorkingDatabaseScope Use(Database db)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            return new WorkingDatabaseScope(db);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            HostApplicationServices.WorkingDatabase = _previous;
        }
    }

}
