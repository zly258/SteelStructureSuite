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
            public string RoleName;
            public string SectionName;
            public int PartNo;
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
            public int PartNo;
            public List<Point2d> Hull;
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
            1, 2, 5, 10, 15, 20, 25, 30, 40, 50, 75, 100, 150, 200, 250, 300, 400, 500
        };

        private class LeaderRequest
        {
            public Point3d AnchorPt;
            public string LabelText;
            public int Side; // 0 左, 1 右, 2 上, 3 下
        }

        public static void GenerateDetailDrawingToNewDwgFile(
            SteelStructureParams p,
            string saveDwgPath,
            DrawingGenerationOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(saveDwgPath))
                throw new ArgumentException("新 DWG 文件路径不能为空。");

            string dir = Path.GetDirectoryName(saveDwgPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (Database newDb = new Database(true, true))
            {
                GenerateDetailDrawingSheet(newDb, p, Point3d.Origin, options);
                newDb.SaveAs(saveDwgPath, DwgVersion.Current);
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

            string dir = Path.GetDirectoryName(saveDwgPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (Database newDb = new Database(true, true))
            {
                GenerateDetailDrawingSheet(newDb, structures, Point3d.Origin, options);
                newDb.SaveAs(saveDwgPath, DwgVersion.Current);
            }
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
            double borderLeft = 25.0 * scale; // 国标装订边 25mm
            double borderOther = 10.0 * scale; // 国标其余留边 10mm

            ObjectId simSunTextStyleId = EnsureSimSunTextStyle(db);
            ObjectId simSunDimStyleId = EnsureSimSunDimStyle(db, simSunTextStyleId, textH);

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    double innerX0 = sheetOrigin.X + borderLeft;
                    double innerY0 = sheetOrigin.Y + borderOther;
                    double innerX1 = sheetOrigin.X + frameW - borderOther;
                    double innerY1 = sheetOrigin.Y + frameH - borderOther;
                    double margin = 10.0 * scale;
                    double gap = 14.0 * scale;
                    double labelSpace = 9.5 * textH;

                    // 顶排视图在本带内垂直居中。轴测图四周留出件号引线，平面图放在正立面正下方。
                    double leaderPad = hasIso ? 8.0 * textH : 0.0;
                    double topContentH = Math.Max(
                        Math.Max(hasFront ? frontBounds.Height : 0.0, hasSide ? sideBounds.Height : 0.0),
                        hasIso ? isoBounds.Height : 0.0);
                    double belowViews = (hasFront || hasSide) ? labelSpace : textH * 3.5;
                    double topRowH = topContentH + belowViews;
                    double topRowBottom = innerY1 - margin - leaderPad - topRowH;
                    double topContentBottom = topRowBottom + belowViews;
                    double cursorX = innerX0 + margin;

                    if (hasFront)
                    {
                        double slotY = topContentBottom + (topContentH - frontBounds.Height) * 0.5;
                        Point3d viewBase = new Point3d(
                            cursorX - frontBounds.MinX,
                            slotY - frontBounds.MinY,
                            0);
                        DrawOrthographicHlrView(
                            tr, ms, p, parts3d, roleToPartNo, viewBase,
                            frontProj,
                            pt => pt.Y,
                            "正立面图",
                            frontBounds.Width, frontBounds.Height,
                            isFrontView: true, isSideView: false, isPlanView: false,
                            options, simSunTextStyleId, simSunDimStyleId, textH, dimGapStep, groupSheet);
                        cursorX += frontBounds.Width + gap;
                    }

                    if (hasSide)
                    {
                        double slotY = topContentBottom + (topContentH - sideBounds.Height) * 0.5;
                        Point3d viewBase = new Point3d(
                            cursorX - sideBounds.MinX,
                            slotY - sideBounds.MinY,
                            0);
                        DrawOrthographicHlrView(
                            tr, ms, p, parts3d, roleToPartNo, viewBase,
                            sideProj,
                            pt => pt.X,
                            "侧立面图",
                            sideBounds.Width, sideBounds.Height,
                            isFrontView: false, isSideView: true, isPlanView: false,
                            options, simSunTextStyleId, simSunDimStyleId, textH, dimGapStep, groupSheet);
                        cursorX += sideBounds.Width + gap;
                    }

                    if (hasIso)
                    {
                        cursorX += leaderPad;
                        double slotY = topContentBottom + (topContentH - isoBounds.Height) * 0.5;
                        Point3d isoBase = new Point3d(
                            cursorX - isoBounds.MinX,
                            slotY - isoBounds.MinY,
                            0);
                        DrawIsometricHlrView(
                            tr, ms, p, parts3d, roleToPartNo, isoBase,
                            options, simSunTextStyleId, textH);
                    }

                    if (hasPlan)
                    {
                        double planBottom = topRowBottom - gap - planBounds.Height;
                        double minPlanBottom = innerY0 + margin;
                        if (planBottom < minPlanBottom) planBottom = minPlanBottom;
                        Point3d planBase = new Point3d(
                            innerX0 + margin - planBounds.MinX,
                            planBottom - planBounds.MinY,
                            0);
                        DrawOrthographicHlrView(
                            tr, ms, p, parts3d, roleToPartNo, planBase,
                            planProj,
                            pt => pt.Z,
                            "平面图",
                            planBounds.Width, planBounds.Height,
                            isFrontView: false, isSideView: false, isPlanView: true,
                            options, simSunTextStyleId, simSunDimStyleId, textH, dimGapStep, groupSheet);
                    }

                    // 国标 A1~A4 标准图框与右下角紧凑标题栏
                    DrawStandardSheetFrameAndTitleBlock(
                        tr, ms, p, sheetOrigin,
                        options.SheetSizeName ?? "A2",
                        frameW, frameH, borderLeft, borderOther,
                        simSunTextStyleId, textH, scale, sheetCaption);

                    // 标题栏上方：内嵌构件材料明细表（件号 #1, #2... 与轴测图序号一一对应）
                    if (hasBom)
                    {
                        // 与标题栏同宽，底边贴在标题栏上沿。表格向上生长，避免压进标题栏或飘到图面中部。
                        double tbWidth = 180.0 * scale;
                        double titleTop = innerY0 + 32.0 * scale;
                        Point3d bomPt = new Point3d(innerX1 - tbWidth, titleTop + 1.0 * scale, 0);
                        AppendDetailedPartBomTable(tr, db, ms, p, billItems, bomPt, tbWidth, simSunTextStyleId, textH);
                    }

                    tr.Commit();
                }
            }
            finally
            {
                foreach (var item in parts3d)
                {
                    item.Solid?.Dispose();
                }
            }
        }

        private static Point2d IsoProject(Point3d pt)
        {
            const double cos30 = 0.8660254037844386;
            return new Point2d((pt.X - pt.Y) * cos30, (pt.X + pt.Y) * 0.5 + pt.Z);
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
            double margin = 10.0 * scale;
            double gap = 14.0 * scale;
            double labelSpace = 9.5 * textH;

            int topCount = (hasFront ? 1 : 0) + (hasSide ? 1 : 0) + (hasIso ? 1 : 0);
            double topW = (hasFront ? front.Width : 0.0) +
                          (hasSide ? side.Width : 0.0) +
                          (hasIso ? iso.Width : 0.0) +
                          gap * Math.Max(0, topCount - 1);
            if (hasIso) topW += 16.0 * textH; // 轴测图左右件号引线
            double topH = Math.Max(
                Math.Max(hasFront ? front.Height : 0.0, hasSide ? side.Height : 0.0),
                hasIso ? iso.Height : 0.0);
            if (hasIso) topH += 8.0 * textH; // 轴测图上方引线
            if (hasFront || hasSide) topH += labelSpace;
            else if (hasIso) topH += textH * 4.0;

            double bomW = hasBom ? 180.0 * scale : 0.0;
            double bomH = hasBom ? (billCount + 3) * (textH * 2.1) : 0.0;
            double titleBlockH = 32.0 * scale;

            double bottomW = (hasPlan ? plan.Width : 0.0) + (hasBom ? gap + bomW : 0.0);
            double bottomH = Math.Max(
                hasPlan ? plan.Height + labelSpace : 0.0,
                hasBom ? bomH + titleBlockH + margin : 0.0);

            double requiredW = margin * 2.0 + Math.Max(topW, bottomW);
            double requiredH = margin * 2.0 + topH + (topH > 0 && bottomH > 0 ? gap : 0.0) + bottomH;

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
                List<Point2d> hull = GetPartHull(part, proj);
                foreach (Point2d pt in hull)
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
                int step = Math.Max(1, path.Count / 24);
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
                SectionSolidBuilder.GetMemberAxes(axis, out Vector3d xDir, out Vector3d yDir, out Vector3d zDir);

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
            Matrix3d xf = Matrix3d.Displacement(p.Origin - Point3d.Origin) *
                          Matrix3d.Rotation(p.RotationRad, Vector3d.ZAxis, Point3d.Origin);
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

        private static void AddOverallDimensions(
            Transaction tr,
            BlockTableRecord ms,
            Point3d viewBase,
            double minX,
            double minY,
            double maxX,
            double maxY,
            bool isPlanView,
            ObjectId dimStyleId,
            double textH,
            double dimGapStep)
        {
            double yDim = viewBase.Y + minY - dimGapStep;
            AppendLinearDim(tr, ms,
                new Point3d(viewBase.X + minX, viewBase.Y + minY, 0),
                new Point3d(viewBase.X + maxX, viewBase.Y + minY, 0),
                new Point3d(viewBase.X + (minX + maxX) * 0.5, yDim, 0),
                0.0, dimStyleId, textH);

            if (isPlanView || maxY - minY < 1.0) return;
            double xDim = viewBase.X + minX - dimGapStep;
            AppendLinearDim(tr, ms,
                new Point3d(viewBase.X + minX, viewBase.Y + minY, 0),
                new Point3d(viewBase.X + minX, viewBase.Y + maxY, 0),
                new Point3d(xDim, viewBase.Y + (minY + maxY) * 0.5, 0),
                Math.PI * 0.5, dimStyleId, textH);
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
                        if (string.Equals(b.PartRole, part.RoleName, StringComparison.OrdinalIgnoreCase)) score += 10;
                        else if (part.RoleName.IndexOf(b.PartRole, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 b.PartRole.IndexOf(part.RoleName, StringComparison.OrdinalIgnoreCase) >= 0) score += 6;
                    }

                    score += CategoryScore(part.RoleName, b.PartRole);
                    if (string.Equals(b.SectionName, part.SectionName, StringComparison.OrdinalIgnoreCase)) score += 3;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = b;
                    }
                }

                // 低于 3 分说明只有截面相同、部位完全不匹配，宁可不标引线也不能标错件号
                if (best != null && bestScore >= 3)
                {
                    dict[key] = best.PartNo;
                }
            }

            return dict;
        }

        private static int CategoryScore(string partRole, string billRole)
        {
            if (string.IsNullOrEmpty(partRole) || string.IsNullOrEmpty(billRole)) return 0;

            string[] categories = { "柱", "梁", "撑", "弦", "腹杆", "次梁", "踏板", "踏棍", "扶手", "栏杆", "横杆", "护笼", "底板", "腹板", "肋", "钢板", "锚固板" };
            foreach (string cat in categories)
            {
                if (partRole.IndexOf(cat, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    billRole.IndexOf(cat, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return 2;
                }
            }
            return 0;
        }

        /// <summary>
        /// 正立面、侧立面、平面消隐图：只标注干净的标准尺寸，绝不标注任何引线！
        /// </summary>
        private static void DrawOrthographicHlrView(
            Transaction tr,
            BlockTableRecord ms,
            SteelStructureParams p,
            List<SteelStructureBuilder.SolidMemberPart> parts3d,
            Dictionary<string, int> roleToPartNo,
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
                    List<Point2d> hull = GetPartHull(part, proj2d);
                    if (hull.Count < 3) continue;

                    double minX = hull.Min(pt => pt.X);
                    double maxX = hull.Max(pt => pt.X);
                    double minY = hull.Min(pt => pt.Y);
                    double maxY = hull.Max(pt => pt.Y);

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
                        RoleName = part.RoleName,
                        SectionName = part.SectionName,
                        PartNo = partNo,
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

            var sorted = boxes.OrderBy(b => b.MinDepth).ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                var cur = sorted[i];
                bool isOccluded = false;
                for (int j = 0; j < i; j++)
                {
                    var front = sorted[j];
                    if (front.MaxDepth < cur.MinDepth - 1.0 && IsOverlapping2D(front, cur))
                    {
                        isOccluded = true;
                        break;
                    }
                }

                string targetLayer = isOccluded ? LayerHidden : LayerVisible;
                // 只绘制构件投影轮廓，不再绘制箱体四边与翼缘内部线，避免线框式干扰线条
                DrawClosedPolyline2D(tr, ms, viewBase, cur.Hull, targetLayer);
            }

            // 基准轴线。组合出图的构件不在原点，轴线改到视图范围角上。
            double extMargin = 3.0 * textH;
            double axisX = overallOnly ? minViewX : 0;
            double axisY = overallOnly ? minViewY : 0;
            AppendLine2D(tr, ms, viewBase, minViewX - extMargin, axisY, maxViewX + extMargin, axisY, LayerAxis);
            AppendLine2D(tr, ms, viewBase, axisX, minViewY - extMargin, axisX, maxViewY + extMargin, LayerAxis);

            // 平面与立面只标干净的标准尺寸（分跨/分层尺寸 + 外包总尺寸）
            if (options.IncludeAutoDimensions && overallOnly)
            {
                AddOverallDimensions(tr, ms, viewBase, minViewX, minViewY, maxViewX, maxViewY, isPlanView, dimStyleId, textH, dimGapStep);
            }
            else if (options.IncludeAutoDimensions)
            {
                AddStandardCleanDimensions(
                    tr, ms, p, viewBase, nominalW, nominalH,
                    isFrontView, isSideView, isPlanView,
                    dimStyleId, textH, dimGapStep);
            }

            Point3d titlePt = viewBase + new Vector3d(minViewX + (maxViewX - minViewX) * 0.22,
                minViewY - dimGapStep * 1.85 - 2.6 * textH, 0);
            AppendSimSunText(tr, ms, titlePt, viewTitle, textStyleId, textH * 1.15, LayerFrame);
        }

        /// <summary>
        /// 平面与立面标准尺寸标注（内层分跨/分层标准尺寸 + 外层总包尺寸，清晰简洁不杂乱）
        /// </summary>
        private static void AddStandardCleanDimensions(
            Transaction tr,
            BlockTableRecord ms,
            SteelStructureParams p,
            Point3d viewBase,
            double nominalW,
            double nominalH,
            bool isFrontView,
            bool isSideView,
            bool isPlanView,
            ObjectId dimStyleId,
            double textH,
            double dimGapStep)
        {
            double yTier1 = viewBase.Y - dimGapStep * 1.0; // 内层分跨尺寸
            double yTier2 = viewBase.Y - dimGapStep * 1.85; // 外层总宽尺寸

            if (p.StructureType == ParametricStructureType.SpiralStair ||
                p.StructureType == ParametricStructureType.VesselSupport)
            {
                double rOuter = nominalW * 0.5;
                AppendLinearDim(tr, ms,
                    viewBase + new Vector3d(-rOuter, 0, 0),
                    viewBase,
                    new Point3d(viewBase.X - rOuter * 0.5, yTier1, 0),
                    0.0, dimStyleId, textH);
                AppendLinearDim(tr, ms,
                    viewBase,
                    viewBase + new Vector3d(rOuter, 0, 0),
                    new Point3d(viewBase.X + rOuter * 0.5, yTier1, 0),
                    0.0, dimStyleId, textH);
                AppendLinearDim(tr, ms,
                    viewBase + new Vector3d(-rOuter, 0, 0),
                    viewBase + new Vector3d(rOuter, 0, 0),
                    new Point3d(viewBase.X, yTier2, 0),
                    0.0, dimStyleId, textH);
            }
            else
            {
                int bays = p.StructureType == ParametricStructureType.MultiSpanPortalFrame
                    ? Math.Max(2, p.BayCount)
                    : 2;
                double bayW = nominalW / bays;
                for (int i = 0; i < bays; i++)
                {
                    AppendLinearDim(tr, ms,
                        viewBase + new Vector3d(i * bayW, 0, 0),
                        viewBase + new Vector3d((i + 1) * bayW, 0, 0),
                        new Point3d(viewBase.X + (i + 0.5) * bayW, yTier1, 0),
                        0.0, dimStyleId, textH);
                }
                AppendLinearDim(tr, ms,
                    viewBase,
                    viewBase + new Vector3d(nominalW, 0, 0),
                    new Point3d(viewBase.X + nominalW * 0.5, yTier2, 0),
                    0.0, dimStyleId, textH);
            }

            double xBaseLeft = (p.StructureType == ParametricStructureType.SpiralStair ||
                                p.StructureType == ParametricStructureType.VesselSupport)
                ? viewBase.X - nominalW * 0.5
                : viewBase.X;

            double xTier1 = xBaseLeft - dimGapStep * 1.0;  // 内层分层标高
            double xTier2 = xBaseLeft - dimGapStep * 1.85; // 外层总高

            if (!isPlanView)
            {
                double midH = (p.SubHeight > 150.0 && p.SubHeight < nominalH - 150.0)
                    ? p.SubHeight
                    : Math.Round(nominalH * 0.5, 0);
                AppendLinearDim(tr, ms,
                    new Point3d(xBaseLeft, viewBase.Y, 0),
                    new Point3d(xBaseLeft, viewBase.Y + midH, 0),
                    new Point3d(xTier1, viewBase.Y + midH * 0.5, 0),
                    Math.PI * 0.5, dimStyleId, textH);
                AppendLinearDim(tr, ms,
                    new Point3d(xBaseLeft, viewBase.Y + midH, 0),
                    new Point3d(xBaseLeft, viewBase.Y + nominalH, 0),
                    new Point3d(xTier1, viewBase.Y + (nominalH + midH) * 0.5, 0),
                    Math.PI * 0.5, dimStyleId, textH);

                double totalTopH = p.StructureType == ParametricStructureType.RoofTruss
                    ? nominalH + p.SubHeight
                    : nominalH;
                AppendLinearDim(tr, ms,
                    new Point3d(xBaseLeft, viewBase.Y, 0),
                    new Point3d(xBaseLeft, viewBase.Y + totalTopH, 0),
                    new Point3d(xTier2, viewBase.Y + totalTopH * 0.5, 0),
                    Math.PI * 0.5, dimStyleId, textH);
            }
            else
            {
                AppendLinearDim(tr, ms,
                    new Point3d(xBaseLeft, viewBase.Y, 0),
                    new Point3d(xBaseLeft, viewBase.Y + nominalH * 0.5, 0),
                    new Point3d(xTier1, viewBase.Y + nominalH * 0.25, 0),
                    Math.PI * 0.5, dimStyleId, textH);
                AppendLinearDim(tr, ms,
                    new Point3d(xBaseLeft, viewBase.Y + nominalH * 0.5, 0),
                    new Point3d(xBaseLeft, viewBase.Y + nominalH, 0),
                    new Point3d(xTier1, viewBase.Y + nominalH * 0.75, 0),
                    Math.PI * 0.5, dimStyleId, textH);
                AppendLinearDim(tr, ms,
                    new Point3d(xBaseLeft, viewBase.Y, 0),
                    new Point3d(xBaseLeft, viewBase.Y + nominalH, 0),
                    new Point3d(xTier2, viewBase.Y + nominalH * 0.5, 0),
                    Math.PI * 0.5, dimStyleId, textH);
            }
        }

        /// <summary>
        /// 西南等轴测消隐图：只在轴测图标注序号引线 (#1, #2...)，不标任何尺寸，与材料表严格对应！
        /// </summary>
        private static void DrawIsometricHlrView(
            Transaction tr,
            BlockTableRecord ms,
            SteelStructureParams p,
            List<SteelStructureBuilder.SolidMemberPart> parts3d,
            Dictionary<string, int> roleToPartNo,
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
                    List<Point2d> hull = GetPartHull(part, isoProj);
                    if (hull.Count < 3) continue;

                    double minX = hull.Min(pt => pt.X);
                    double maxX = hull.Max(pt => pt.X);
                    double minY = hull.Min(pt => pt.Y);
                    double maxY = hull.Max(pt => pt.Y);

                    Extents3d ext = part.Solid.GeometricExtents;
                    double depth = (ext.MinPoint.X + ext.MaxPoint.X +
                                    ext.MinPoint.Y + ext.MaxPoint.Y +
                                    ext.MinPoint.Z + ext.MaxPoint.Z) * 0.5;

                    int partNo = roleToPartNo.TryGetValue(PartKey(part.ParentTag, part.RoleName), out int no) ? no : 0;
                    isoParts.Add(new IsoPart2D
                    {
                        PartNo = partNo,
                        Hull = hull,
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
            else
            {
                // 只绘制构件投影轮廓，后侧构件被前侧构件遮挡时改用虚线，避免 12 条箱体线框干扰
                var sortedIso = isoParts.OrderBy(x => x.Depth).ToList();
                for (int i = 0; i < sortedIso.Count; i++)
                {
                    IsoPart2D cur = sortedIso[i];
                    bool hidden = false;
                    for (int j = 0; j < i; j++)
                    {
                        IsoPart2D front = sortedIso[j];
                        if (front.Depth < cur.Depth - 1.0 &&
                            front.MaxX > cur.MinX + 8.0 && front.MinX < cur.MaxX - 8.0 &&
                            front.MaxY > cur.MinY + 8.0 && front.MinY < cur.MaxY - 8.0)
                        {
                            hidden = true;
                            break;
                        }
                    }
                    DrawClosedPolyline2D(tr, ms, isoBase, cur.Hull, hidden ? LayerHidden : LayerVisible);
                }
            }

            var partCenters = isoParts
                .Where(x => x.PartNo > 0)
                .Select(x => new
                {
                    x.PartNo,
                    X = (x.MinX + x.MaxX) * 0.5,
                    Y = (x.MinY + x.MaxY) * 0.5
                })
                .ToList();

            if (options.IncludeLeaderAnnotations && partCenters.Count > 0)
            {
                var leaderReqs = new List<LeaderRequest>();
                double cx = (minIsoX + maxIsoX) * 0.5;
                double cy = (minIsoY + maxIsoY) * 0.5;
                double halfW = Math.Max(1.0, (maxIsoX - minIsoX) * 0.5);
                double halfH = Math.Max(1.0, (maxIsoY - minIsoY) * 0.5);

                foreach (var grp in partCenters.GroupBy(x => x.PartNo).OrderBy(g => g.Key))
                {
                    var rep = grp.OrderByDescending(x =>
                    {
                        double dx = x.X - cx;
                        double dy = x.Y - cy;
                        return dx * dx + dy * dy;
                    }).First();

                    double nx = (rep.X - cx) / halfW;
                    double ny = (rep.Y - cy) / halfH;
                    int side;
                    if (Math.Abs(nx) >= Math.Abs(ny))
                        side = nx >= 0 ? 1 : 0;
                    else
                        side = ny >= 0 ? 2 : 3;

                    leaderReqs.Add(new LeaderRequest
                    {
                        AnchorPt = new Point3d(isoBase.X + rep.X, isoBase.Y + rep.Y, 0),
                        LabelText = $"#{rep.PartNo}",
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

            Point3d titlePt = isoBase + new Vector3d(minIsoX, maxIsoY + 6.4 * textH, 0);
            AppendSimSunText(
                tr, ms, titlePt,
                "轴测图 (仅标件号)",
                textStyleId, textH * 1.15, LayerFrame);
        }

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

            double minGap = 2.6 * textH;
            double sideMargin = 3.2 * textH;
            double shelfLen = 2.4 * textH;

            for (int side = 0; side < 4; side++)
            {
                var group = requests.Where(r => r.Side == side).ToList();
                PlaceLeaderSideGroup(
                    tr, ms, group, side,
                    viewMinX, viewMaxX, viewMinY, viewMaxY,
                    minGap, sideMargin, shelfLen, textStyleId, textH);
            }
        }

        private static void PlaceLeaderSideGroup(
            Transaction tr,
            BlockTableRecord ms,
            List<LeaderRequest> sideGroup,
            int side,
            double viewMinX,
            double viewMaxX,
            double viewMinY,
            double viewMaxY,
            double minGap,
            double sideMargin,
            double shelfLen,
            ObjectId textStyleId,
            double textH)
        {
            if (sideGroup == null || sideGroup.Count == 0) return;

            bool alongY = side == 0 || side == 1;
            sideGroup.Sort((a, b) => alongY
                ? a.AnchorPt.Y.CompareTo(b.AnchorPt.Y)
                : a.AnchorPt.X.CompareTo(b.AnchorPt.X));

            double minC = alongY ? viewMinY : viewMinX;
            double maxC = alongY ? viewMaxY : viewMaxX;
            double available = Math.Max(textH, maxC - minC);
            if (sideGroup.Count > 1)
                minGap = Math.Min(minGap, available / (sideGroup.Count - 1));

            double[] slot = new double[sideGroup.Count];
            for (int i = 0; i < sideGroup.Count; i++)
            {
                double desired = alongY ? sideGroup[i].AnchorPt.Y : sideGroup[i].AnchorPt.X;
                double minAllowed = minC + i * minGap;
                if (desired < minAllowed) desired = minAllowed;
                slot[i] = Math.Min(maxC, desired);
            }
            for (int i = sideGroup.Count - 2; i >= 0; i--)
            {
                if (slot[i] > slot[i + 1] - minGap)
                    slot[i] = slot[i + 1] - minGap;
            }
            if (slot[0] < minC)
            {
                double shift = minC - slot[0];
                for (int i = 0; i < slot.Length; i++) slot[i] += shift;
            }

            for (int i = 0; i < sideGroup.Count; i++)
            {
                LeaderRequest req = sideGroup[i];
                Point3d p0 = req.AnchorPt;
                Point3d p1;
                Point3d p2;
                Point3d textPos;
                double textWidth = Math.Max(req.LabelText.Length * textH * 0.55, 2.0 * textH);

                if (side == 1)
                {
                    double x = viewMaxX + sideMargin;
                    p1 = new Point3d(x, slot[i], 0);
                    p2 = new Point3d(x + shelfLen, slot[i], 0);
                    textPos = new Point3d(p2.X + 0.35 * textH, slot[i] + 0.25 * textH, 0);
                }
                else if (side == 0)
                {
                    double x = viewMinX - sideMargin;
                    p1 = new Point3d(x, slot[i], 0);
                    p2 = new Point3d(x - shelfLen, slot[i], 0);
                    textPos = new Point3d(p2.X - textWidth, slot[i] + 0.25 * textH, 0);
                }
                else if (side == 2)
                {
                    double y = viewMaxY + sideMargin;
                    p1 = new Point3d(slot[i], y, 0);
                    p2 = new Point3d(slot[i], y + shelfLen, 0);
                    textPos = new Point3d(slot[i] + 0.35 * textH, p2.Y + 0.25 * textH, 0);
                }
                else
                {
                    double y = viewMinY - sideMargin;
                    p1 = new Point3d(slot[i], y, 0);
                    p2 = new Point3d(slot[i], y - shelfLen, 0);
                    textPos = new Point3d(slot[i] + 0.35 * textH, p2.Y - 1.15 * textH, 0);
                }

                using (Leader ld = new Leader())
                {
                    ld.SetDatabaseDefaults();
                    ld.Layer = LayerDim;
                    ld.AppendVertex(p0);
                    ld.AppendVertex(p1);
                    ld.AppendVertex(p2);
                    ld.HasArrowHead = true;
                    ld.Dimasz = textH * 0.72;
                    ms.AppendEntity(ld);
                    tr.AddNewlyCreatedDBObject(ld, true);
                }

                AppendSimSunText(tr, ms, textPos, req.LabelText, textStyleId, textH, LayerDim);
            }
        }

        private static void DrawIsoBoxWireframe(
            Transaction tr,
            BlockTableRecord ms,
            Point3d isoBase,
            Point3d min,
            Point3d max,
            Func<Point3d, Point2d> isoProj)
        {
            Point3d[] pts3d =
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

            Point2d[] pts2d = pts3d.Select(isoProj).ToArray();
            int[,] edges =
            {
                {0,1}, {1,2}, {2,3}, {3,0},
                {4,5}, {5,6}, {6,7}, {7,4},
                {0,4}, {1,5}, {2,6}, {3,7}
            };

            for (int e = 0; e < edges.GetLength(0); e++)
            {
                int a = edges[e, 0];
                int b = edges[e, 1];
                bool backHidden = (a == 3 || b == 3);
                AppendLine2D(
                    tr, ms, isoBase,
                    pts2d[a].X, pts2d[a].Y,
                    pts2d[b].X, pts2d[b].Y,
                    backHidden ? LayerHidden : LayerVisible);
            }
        }

        private static void AppendDetailedPartBomTable(
            Transaction tr,
            Database db,
            BlockTableRecord ms,
            SteelStructureParams p,
            List<MemberPartBillItem> billItems,
            Point3d tablePos,
            double totalTableWidth,
            ObjectId textStyleId,
            double textH)
        {
            int rowCount = billItems.Count + 3;
            int colCount = 7;

            Table tb = new Table();
            tb.SetDatabaseDefaults(db);
            tb.Layer = LayerFrame;
            tb.FlowDirection = FlowDirection.BottomToTop;
            tb.Position = tablePos;
            tb.SetSize(rowCount, colCount);
            tb.SetRowHeight(textH * 2.1);

            double[] ratios = { 0.09, 0.21, 0.26, 0.11, 0.09, 0.12, 0.12 };
            for (int c = 0; c < colCount; c++)
                tb.Columns[c].Width = totalTableWidth * ratios[c];

            tb.Cells[0, 0].TextString = $"材料明细表 ({p.Tag})";
            tb.Cells[0, 0].TextStyleId = textStyleId;
            tb.Cells[0, 0].TextHeight = textH;
            tb.Cells[0, 0].Alignment = CellAlignment.MiddleCenter;

            string[] headers = { "件号", "部位", "截面规格", "材质", "数量", "单长", "重(kg)" };
            for (int c = 0; c < colCount; c++)
            {
                tb.Cells[1, c].TextString = headers[c];
                tb.Cells[1, c].TextStyleId = textStyleId;
                tb.Cells[1, c].TextHeight = textH;
                tb.Cells[1, c].Alignment = CellAlignment.MiddleCenter;
            }

            double sumKg = 0;
            for (int i = 0; i < billItems.Count; i++)
            {
                var item = billItems[i];
                int r = i + 2;
                string[] vals =
                {
                    $"#{item.PartNo}",
                    item.PartRole,
                    item.SectionName,
                    item.Material,
                    item.Count.ToString(),
                    item.SingleLengthMm.ToString("0", CultureInfo.InvariantCulture),
                    item.TotalWeightKg.ToString("0.00", CultureInfo.InvariantCulture)
                };
                for (int c = 0; c < colCount; c++)
                {
                    tb.Cells[r, c].TextString = vals[c];
                    tb.Cells[r, c].TextStyleId = textStyleId;
                    tb.Cells[r, c].TextHeight = textH;
                    tb.Cells[r, c].Alignment = CellAlignment.MiddleCenter;
                }
                sumKg += item.TotalWeightKg;
            }

            int lastRow = rowCount - 1;
            tb.Cells[lastRow, 0].TextString = "合计";
            tb.Cells[lastRow, 2].TextString = $"总重: {sumKg:0.00} kg";
            tb.Cells[lastRow, 6].TextString = sumKg.ToString("0.00", CultureInfo.InvariantCulture);
            for (int c = 0; c < colCount; c++)
            {
                tb.Cells[lastRow, c].TextStyleId = textStyleId;
                tb.Cells[lastRow, c].TextHeight = textH;
                tb.Cells[lastRow, c].Alignment = CellAlignment.MiddleCenter;
            }

            tb.GenerateLayout();
            ms.AppendEntity(tb);
            tr.AddNewlyCreatedDBObject(tb, true);
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
            AppendLine2D(tr, ms, tbMin, tbW * 0.58, 0, tbW * 0.58, tbH, LayerFrame);

            AppendSimSunText(tr, ms, tbMin + new Vector3d(2.0 * textH, tbH * 0.62, 0),
                sheetCaption ?? $"图名: {p.StructureTypeDisplayName} 详图 ({p.Tag})", textStyleId, textH * 1.1, LayerFrame);
            AppendSimSunText(tr, ms, tbMin + new Vector3d(2.0 * textH, tbH * 0.14, 0),
                $"主材: {p.ColumnSection} / {p.BeamSection} ({p.Material})", textStyleId, textH, LayerFrame);
            AppendSimSunText(tr, ms, tbMin + new Vector3d(tbW * 0.60, tbH * 0.62, 0),
                $"图幅: {sheetSizeName}   比例: 1:{scale:0.#}", textStyleId, textH, LayerFrame);
            AppendSimSunText(tr, ms, tbMin + new Vector3d(tbW * 0.60, tbH * 0.14, 0),
                $"日期: {DateTime.Now:yyyy-MM-dd}", textStyleId, textH, LayerFrame);
        }

        private static bool IsOverlapping2D(ProjectedPart2D a, ProjectedPart2D b)
        {
            double ox = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
            double oy = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY);
            return ox > 8.0 && oy > 8.0;
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

        private static void AppendLine2D(
            Transaction tr,
            BlockTableRecord ms,
            Point3d basePt,
            double x1,
            double y1,
            double x2,
            double y2,
            string layer)
        {
            Line ln = new Line(
                new Point3d(basePt.X + x1, basePt.Y + y1, 0),
                new Point3d(basePt.X + x2, basePt.Y + y2, 0));
            ln.SetDatabaseDefaults();
            ln.Layer = layer;
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
            txt.TextString = content;
            ms.AppendEntity(txt);
            tr.AddNewlyCreatedDBObject(txt, true);
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

                CreateOrUpdateLayer(tr, lt, LayerVisible, 2, ObjectId.Null, LineWeight.LineWeight030);
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
            if (lt.Has(name)) return;
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
}
