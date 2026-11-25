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
using SteelStructureSuite.Services.Settings;

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
            public bool ThinPlate;
            public List<Point2d> Hull = new List<Point2d>();
        }

        private class IsoPart2D
        {
            public SteelStructureBuilder.SolidMemberPart SourcePart;
            public int PartNo;
            public List<Point2d> Hull;
            public Point2d Anchor2D;
            public bool NearEndExposed;
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
            1, 2, 5, 10, 15, 20, 25, 30, 40, 50, 60, 75, 80, 100, 125, 150, 200, 250, 300, 400, 500
        };

        private struct SheetLayoutMetrics
        {
            public double TextH;
            public double DimGapStep;
            public double Margin;
            public double ZoneGap;
            public double DimPad;
            public double BelowOrtho;
            public double OrthoColGap;
            public double LeaderSideReserve;
            public double IsoFloorReserve;
            public double IsoCeilReserve;
            public double TitleW;
            public double TitleH;
            public double BomRowH;
            public double BomHeaderH;
            public double BomH;

            public static SheetLayoutMetrics Create(double scale, bool hasIso, bool hasBom, int billCount)
            {
                // 严格执行：字体宋体 3.5 x 0.8（字高 = 3.5 * scale，宽度因子 = 0.8）
                double textH = 3.5 * scale;
                // 国标尺寸线间距 7~10mm（2.45 * 3.5 = 8.575mm），紧凑清晰不空旷
                double dimGapStep = 2.45 * textH;
                double margin = 7.0 * scale;
                double zoneGap = 10.0 * scale;
                // 左侧三道尺寸最外层位于 2.90 * dimGapStep，加上字高留白共 3.05 * dimGapStep + 0.8 * textH
                double dimPad = dimGapStep * 3.05 + 0.8 * textH;
                // 底部三道尺寸 + 视图标题行
                double belowOrtho = dimGapStep * 3.05 + 3.6 * textH;
                // 正立面与侧立面之间仅需清晰净距（侧立面自身已包含左侧 dimPad，避免重复叠加）
                double orthoColGap = zoneGap;
                // 轴测图单段短引线 + 圆圈件号安全留白
                double leaderSideReserve = hasIso ? 5.2 * textH : 0.0;
                double isoFloorReserve = hasIso ? 4.6 * textH : 0.0;
                double isoCeilReserve = hasIso ? 3.0 * textH : 0.0;

                double titleW = 180.0 * scale;
                double titleH = 32.0 * scale;
                double bomRowH = textH * 2.05;
                double bomHeaderH = textH * 2.45;
                double bomH = hasBom ? bomHeaderH + (billCount + 2) * bomRowH : 0.0;

                return new SheetLayoutMetrics
                {
                    TextH = textH,
                    DimGapStep = dimGapStep,
                    Margin = margin,
                    ZoneGap = zoneGap,
                    DimPad = dimPad,
                    BelowOrtho = belowOrtho,
                    OrthoColGap = orthoColGap,
                    LeaderSideReserve = leaderSideReserve,
                    IsoFloorReserve = isoFloorReserve,
                    IsoCeilReserve = isoCeilReserve,
                    TitleW = titleW,
                    TitleH = titleH,
                    BomRowH = bomRowH,
                    BomHeaderH = bomHeaderH,
                    BomH = bomH
                };
            }
        }

        private class LeaderRequest
        {
            public Point3d AnchorPt;
            public string LabelText;
            public int Side; // 0 左列, 1 右列
        }

        /// <summary>
        /// 视图线段收集器：先收集一个视图的全部线段，统一处理后一次性落笔。
        /// 作用：
        /// 1. 合并共线重叠线段，消除同一位置反复叠线；
        /// 2. 按“前方构件轮廓”逐段判断遮挡，被挡住的段落直接画成虚线；
        /// 3. 去掉被可见线完全覆盖的重复隐藏线。
        /// </summary>
        private class LineBatch
        {
            private struct Seg
            {
                public Point2d A;
                public Point2d B;
                public bool Hidden;
            }

            private readonly List<Seg> _segments = new List<Seg>();

            /// <summary>当前构件的遮挡轮廓（由近到远，只包含严格位于其前方的构件）。</summary>
            public List<List<Point2d>> Occluders { get; } = new List<List<Point2d>>();

            public void Add(Point2d a, Point2d b)
            {
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6) return;

                bool hidden = Occluders.Count > 0 && IsMostlyOccluded(a, b, len);
                _segments.Add(new Seg { A = a, B = b, Hidden = hidden });
            }

            public void AddPolyline(IList<Point2d> pts, bool closed)
            {
                if (pts == null || pts.Count < 2) return;
                for (int i = 0; i + 1 < pts.Count; i++)
                    Add(pts[i], pts[i + 1]);
                if (closed && pts.Count >= 3)
                    Add(pts[pts.Count - 1], pts[0]);
            }

            private bool IsMostlyOccluded(Point2d a, Point2d b, double len)
            {
                int samples = Math.Max(4, Math.Min(24, (int)(len / 40.0) + 1));
                int hits = 0;
                for (int i = 0; i < samples; i++)
                {
                    double t = (i + 0.5) / samples;
                    var pt = new Point2d(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                    foreach (List<Point2d> hull in Occluders)
                    {
                        if (PointInPolygon(pt, hull)) { hits++; break; }
                    }
                }
                return hits >= samples * 0.6;
            }

            /// <summary>合并共线重叠线段后落笔（高精度容差，避免圆弧/斜线产生锯齿与断口）。</summary>
            public void Emit(Transaction tr, BlockTableRecord ms, Point3d basePt, double textH)
            {
                if (_segments.Count == 0) return;

                double tol = 0.15;
                List<Seg> visible = Merge(_segments.Where(s => !s.Hidden), tol);
                List<Seg> hidden = Merge(_segments.Where(s => s.Hidden), tol);
                hidden = RemoveCovered(hidden, visible, tol);

                foreach (Seg seg in visible)
                    EmitSegment(tr, ms, basePt, seg, LayerVisible);
                foreach (Seg seg in hidden)
                    EmitSegment(tr, ms, basePt, seg, LayerHidden);
            }

            private static List<Seg> Merge(IEnumerable<Seg> src, double tol)
            {
                var groups = new Dictionary<string, List<Tuple<double, double, Seg>>>(StringComparer.Ordinal);
                foreach (Seg seg in src)
                {
                    double t0, t1;
                    string key = LineKey(seg.A, seg.B, tol, out t0, out t1);
                    if (!groups.TryGetValue(key, out List<Tuple<double, double, Seg>> list))
                    {
                        list = new List<Tuple<double, double, Seg>>();
                        groups[key] = list;
                    }
                    list.Add(Tuple.Create(Math.Min(t0, t1), Math.Max(t0, t1), seg));
                }

                var result = new List<Seg>();
                foreach (var kv in groups)
                {
                    List<Tuple<double, double, Seg>> list = kv.Value
                        .OrderBy(x => x.Item1)
                        .ToList();

                    // 未发生合并的单条线段直接保留精确原始端点，杜绝圆弧/斜线端点漂移产生锯齿和缝隙
                    if (list.Count == 1)
                    {
                        result.Add(list[0].Item3);
                        continue;
                    }

                    Seg rep = list[0].Item3;
                    double start = list[0].Item1;
                    double end = list[0].Item2;
                    bool mergedAny = false;
                    for (int i = 1; i < list.Count; i++)
                    {
                        if (list[i].Item1 <= end + tol)
                        {
                            end = Math.Max(end, list[i].Item2);
                            mergedAny = true;
                            continue;
                        }
                        result.Add(mergedAny ? Rebuild(rep, start, end, kv.Key) : rep);
                        rep = list[i].Item3;
                        start = list[i].Item1;
                        end = list[i].Item2;
                        mergedAny = false;
                    }
                    result.Add(mergedAny ? Rebuild(rep, start, end, kv.Key) : rep);
                }
                return result;
            }

            private static Seg Rebuild(Seg source, double t0, double t1, string key)
            {
                string[] parts = key.Split('|');
                Point2d a, b;

                if (parts[0] == "H")
                {
                    double y = (source.A.Y + source.B.Y) * 0.5;
                    a = new Point2d(t0, y);
                    b = new Point2d(t1, y);
                }
                else if (parts[0] == "V")
                {
                    double x = (source.A.X + source.B.X) * 0.5;
                    a = new Point2d(x, t0);
                    b = new Point2d(x, t1);
                }
                else
                {
                    double dx = source.B.X - source.A.X;
                    double dy = source.B.Y - source.A.Y;
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    double nx = len > 1e-9 ? dx / len : double.Parse(parts[1], CultureInfo.InvariantCulture);
                    double ny = len > 1e-9 ? dy / len : double.Parse(parts[2], CultureInfo.InvariantCulture);
                    if (nx < -1e-9 || (Math.Abs(nx) <= 1e-9 && ny < 0)) { nx = -nx; ny = -ny; }
                    double off = nx * source.A.Y - ny * source.A.X;
                    a = new Point2d(t0 * nx - off * ny, t0 * ny + off * nx);
                    b = new Point2d(t1 * nx - off * ny, t1 * ny + off * nx);
                }
                return new Seg { A = a, B = b, Hidden = source.Hidden };
            }

            private static string LineKey(Point2d a, Point2d b, double tol, out double t0, out double t1)
            {
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                double bucket = tol * 2.0;

                // 必须同时满足角度水平/竖直，防止短圆弧弦被误判为水平/竖直线造成阶梯锯齿
                if (len > 1e-6 && Math.Abs(dy) <= len * 1e-3 && Math.Abs(dy) <= 0.05)
                {
                    t0 = Math.Min(a.X, b.X);
                    t1 = Math.Max(a.X, b.X);
                    double y = (a.Y + b.Y) * 0.5;
                    return "H|" + Math.Round(y / bucket).ToString(CultureInfo.InvariantCulture);
                }
                if (len > 1e-6 && Math.Abs(dx) <= len * 1e-3 && Math.Abs(dx) <= 0.05)
                {
                    t0 = Math.Min(a.Y, b.Y);
                    t1 = Math.Max(a.Y, b.Y);
                    double x = (a.X + b.X) * 0.5;
                    return "V|" + Math.Round(x / bucket).ToString(CultureInfo.InvariantCulture);
                }

                double nx = dx / len, ny = dy / len;
                if (nx < -1e-9 || (Math.Abs(nx) <= 1e-9 && ny < 0))
                {
                    nx = -nx; ny = -ny;
                    Point2d tmp = a; a = b; b = tmp;
                }
                double off = nx * a.Y - ny * a.X;
                t0 = a.X * nx + a.Y * ny;
                t1 = b.X * nx + b.Y * ny;
                if (t0 > t1) { double tmp = t0; t0 = t1; t1 = tmp; }
                return "D|" + Math.Round(nx, 5).ToString(CultureInfo.InvariantCulture) +
                       "|" + Math.Round(ny, 5).ToString(CultureInfo.InvariantCulture) +
                       "|" + Math.Round(off / bucket).ToString(CultureInfo.InvariantCulture);
            }

            private static List<Seg> RemoveCovered(List<Seg> hidden, List<Seg> visible, double tol)
            {
                if (hidden.Count == 0 || visible.Count == 0) return hidden;

                var visGroups = new Dictionary<string, List<Tuple<double, double>>>(StringComparer.Ordinal);
                foreach (Seg seg in visible)
                {
                    double t0, t1;
                    string key = LineKey(seg.A, seg.B, tol, out t0, out t1);
                    if (!visGroups.TryGetValue(key, out List<Tuple<double, double>> list))
                    {
                        list = new List<Tuple<double, double>>();
                        visGroups[key] = list;
                    }
                    list.Add(Tuple.Create(Math.Min(t0, t1), Math.Max(t0, t1)));
                }

                var kept = new List<Seg>();
                foreach (Seg seg in hidden)
                {
                    double t0, t1;
                    string key = LineKey(seg.A, seg.B, tol, out t0, out t1);
                    if (!visGroups.TryGetValue(key, out List<Tuple<double, double>> list))
                    {
                        kept.Add(seg);
                        continue;
                    }
                    double s = Math.Min(t0, t1), e = Math.Max(t0, t1);
                    bool covered = list.Any(v => v.Item1 - tol <= s && v.Item2 + tol >= e);
                    if (!covered) kept.Add(seg);
                }
                return kept;
            }

            private static void EmitSegment(
                Transaction tr,
                BlockTableRecord ms,
                Point3d basePt,
                Seg seg,
                string layer)
            {
                Line ln = new Line(
                    new Point3d(basePt.X + seg.A.X, basePt.Y + seg.A.Y, 0),
                    new Point3d(basePt.X + seg.B.X, basePt.Y + seg.B.Y, 0));
                ln.SetDatabaseDefaults(ms.Database);
                ln.Layer = layer;
                ln.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                if (layer == LayerHidden)
                {
                    double len = Math.Max(ln.Length, 80.0);
                    ln.LinetypeScale = Math.Max(20.0, len / 14.0);
                }
                ms.AppendEntity(ln);
                tr.AddNewlyCreatedDBObject(ln, true);
            }
        }

        private static bool PointInPolygon(Point2d pt, IList<Point2d> poly)
        {
            if (poly == null || poly.Count < 3) return false;
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if (((poly[i].Y > pt.Y) != (poly[j].Y > pt.Y)) &&
                    (pt.X < (poly[j].X - poly[i].X) * (pt.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>估算一个轮廓有多大比例被遮挡：取顶点、边中点和形心作为采样点。</summary>
        private static double HullCoveredRatio(IList<Point2d> hull, IList<List<Point2d>> occluders)
        {
            if (hull == null || hull.Count < 3 || occluders == null || occluders.Count == 0) return 0.0;

            var samples = new List<Point2d>();
            double cx = 0.0, cy = 0.0;
            foreach (Point2d pt in hull) { samples.Add(pt); cx += pt.X; cy += pt.Y; }
            cx /= hull.Count;
            cy /= hull.Count;
            samples.Add(new Point2d(cx, cy));
            for (int i = 0; i < hull.Count; i++)
            {
                Point2d a = hull[i];
                Point2d b = hull[(i + 1) % hull.Count];
                samples.Add(new Point2d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5));
            }

            int hit = 0;
            foreach (Point2d pt in samples)
            {
                foreach (List<Point2d> hull2 in occluders)
                {
                    if (PointInPolygon(pt, hull2)) { hit++; break; }
                }
            }
            return (double)hit / samples.Count;
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

        internal static void GetStandardSheetPaperMm(string sheetName, out double paperW, out double paperH)
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

        /// <summary>
        /// 生成单件详图，并指定标题栏图名（批量出图用于标注「共 N 件」等信息）。
        /// </summary>
        public static void GenerateDetailDrawingSheet(
            Database db,
            IList<SteelStructureParams> structures,
            Point3d sheetOrigin,
            DrawingGenerationOptions options,
            string sheetCaption)
        {
            if (structures == null || structures.Count == 0) return;
            GenerateDetailDrawingSheetCore(db, structures, sheetOrigin, options, sheetCaption);
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
        internal static void SaveDatabaseToDwg(Database db, string saveDwgPath)
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
            GenerateDetailDrawingSheetCore(db, structures, sheetOrigin, options, null);
        }

        private static void GenerateDetailDrawingSheetCore(
            Database db,
            IList<SteelStructureParams> structures,
            Point3d sheetOrigin,
            DrawingGenerationOptions options,
            string explicitCaption)
        {
            if (structures == null || structures.Count == 0) return;
            if (options == null) options = new DrawingGenerationOptions();

            SteelStructureParams p = structures[0];
            bool groupSheet = structures.Count > 1;
            string sheetCaption = !string.IsNullOrWhiteSpace(explicitCaption)
                ? explicitCaption
                : (groupSheet
                    ? $"图名: 组合详图 ({structures.Count}件)"
                    : $"图名: {p.StructureTypeDisplayName} 详图 ({p.Tag})");

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

            // 相同“截面规格 + 材质 + 单长”的零件合并成一行并重新连续编号（不再显示部位）。
            // 合并后的件号会回写到源明细，轴测图引线据此一一对应。
            List<MemberPartBillItem> bomRows = MergeBillItems(billItems);

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

            double scale = SelectBestScale(options, paperWMm, paperHMm, bomRows.Count,
                hasFront, frontBounds, hasSide, sideBounds, hasPlan, planBounds, hasIso, isoBounds, hasBom);

            SheetLayoutMetrics m = SheetLayoutMetrics.Create(scale, hasIso, hasBom, bomRows.Count);
            double textH = m.TextH;
            double dimGapStep = m.DimGapStep;

            double frameW = paperWMm * scale;
            double frameH = paperHMm * scale;
            double borderLeft = 25.0 * scale;  // 国标装订边 25mm
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
                    double innerW = innerX1 - innerX0;
                    double innerH = innerY1 - innerY0;

                    double margin = m.Margin;
                    double gap = m.ZoneGap;
                    double dimPad = m.DimPad;
                    double belowOrtho = m.BelowOrtho;
                    double colGap = m.OrthoColGap;

                    double titleW = m.TitleW;
                    double titleH = m.TitleH;
                    double titleTop = innerY0 + titleH;
                    double bomRowH = m.BomRowH;
                    double bomHeaderH = m.BomHeaderH;
                    double bomH = m.BomH;
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
                    double leaderSideReserve = m.LeaderSideReserve;

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
                    double extraColGap = freeW * 0.30;
                    double leftStartX = innerX0 + margin + freeW * 0.16;

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
                            tr, ms, p, parts3d, roleToPartNo, viewBase,
                            frontProj, pt => pt.Y, "正立面图",
                            new Vector3d(0, -1, 0),
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
                            tr, ms, p, parts3d, roleToPartNo, viewBase,
                            sideProj, pt => pt.X, "侧立面图",
                            new Vector3d(1, 0, 0),
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
                            tr, ms, p, parts3d, roleToPartNo, planBase,
                            planProj, pt => pt.Z, "平面图",
                            new Vector3d(0, 0, 1),
                            planBounds.Width, planBounds.Height,
                            isFrontView: false, isSideView: false, isPlanView: true,
                            options, simSunTextStyleId, simSunDimStyleId, textH, dimGapStep, groupSheet);
                    }

                    // 右上区：西南等轴测图（严格控制在 [rightTableTop + IsoFloorReserve, innerY1 - margin - IsoCeilReserve] 与左右引线安全边距内，绝不穿出图框！）
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

                        double isoFloorY = rightTableTop + m.IsoFloorReserve;
                        double isoCeilY = innerY1 - margin - m.IsoCeilReserve;
                        double isoFreeY = Math.Max(0.0, (isoCeilY - isoFloorY) - isoH);
                        double isoBottom = isoFloorY + isoFreeY * 0.50;
                        if (isoBottom + isoH > isoCeilY)
                        {
                            isoBottom = isoCeilY - isoH;
                        }

                        Point3d isoBase = new Point3d(isoLeft - isoBounds.MinX, isoBottom - isoBounds.MinY, 0);
                        DrawIsometricHlrView(
                            tr, ms, p, parts3d, bomRows, roleToPartNo, isoBase,
                            options, simSunTextStyleId, textH);
                    }

                    // 国标 A1~A4 标准图框与右下角紧凑标题栏
                    DrawStandardSheetFrameAndTitleBlock(
                        tr, ms, p, sheetOrigin,
                        options.SheetSizeName ?? "A2",
                        frameW, frameH, borderLeft, borderOther,
                        simSunTextStyleId, textH, scale, sheetCaption);

                    // 标题栏正上方：零缝隙、左右边框 100% 共线对齐的国标构件材料明细表（件号 1, 2... 与轴测图引线一一对应）
                    if (hasBom)
                    {
                        Point3d bomBottomLeft = new Point3d(innerX1 - titleW, titleTop, 0);
                        AppendDetailedPartBomTable(
                            tr, ms, p, bomRows,
                            bomBottomLeft, titleW, bomRowH, bomHeaderH,
                            simSunTextStyleId, textH);
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
            SheetLayoutMetrics m = SheetLayoutMetrics.Create(scale, hasIso, hasBom, billCount);

            double orthoRowW = (hasFront ? m.DimPad + front.Width : 0.0)
                             + (hasFront && hasSide ? m.OrthoColGap : 0.0)
                             + (hasSide ? m.DimPad + side.Width : 0.0);
            double planRowW = hasPlan ? m.DimPad + plan.Width : 0.0;
            double leftZoneW = Math.Max(orthoRowW, planRowW);

            double rightZoneW = Math.Max(
                hasIso ? iso.Width + m.LeaderSideReserve * 2.0 : 0.0,
                m.TitleW);

            double orthoH = Math.Max(hasFront ? front.Height : 0.0, hasSide ? side.Height : 0.0);
            double leftZoneH = (orthoH > 0 ? orthoH + m.BelowOrtho : 0.0)
                             + (hasPlan ? plan.Height + m.BelowOrtho : 0.0);

            double rightZoneH = m.TitleH + m.BomH + (hasIso ? iso.Height + m.IsoFloorReserve + m.IsoCeilReserve : 0.0);

            double requiredW = m.Margin * 2.0 + leftZoneW + (leftZoneW > 0 && rightZoneW > 0 ? m.ZoneGap : 0.0) + rightZoneW;
            double requiredH = m.Margin * 2.0 + Math.Max(leftZoneH, rightZoneH);

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
                List<Point2d> outline = GetDrawingOutlinePoints(part.Section);
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

                List<Point2d> outline = GetDrawingOutlinePoints(part.Section);
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

        /// <summary>
        /// 出图用截面外轮廓：圆管/圆钢用 64 边形高精度逼近，保证投影端面圆滑无锯齿。
        /// </summary>
        private static List<Point2d> GetDrawingOutlinePoints(SectionProfile sec)
        {
            if (sec == null) return new List<Point2d>();

            if (sec.Shape == SectionShapeType.PipeTube || sec.Shape == SectionShapeType.RoundBar)
            {
                double r = Math.Max(6.0, sec.H * 0.5);
                const int segments = 64;
                var pts = new List<Point2d>(segments);
                for (int i = 0; i < segments; i++)
                {
                    double a = 2.0 * Math.PI * i / segments;
                    pts.Add(new Point2d(r * Math.Cos(a), r * Math.Sin(a)));
                }
                return pts;
            }

            return SectionSolidBuilder.GetProfileOuterPoints(sec);
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

        /// <summary>
        /// 把“截面规格 + 材质 + 单长”完全相同的零件合并为一行：数量、重量累加，
        /// 并重新连续编号。合并后的件号会同步回写到源明细，供轴测图引线映射。
        /// </summary>
        private static List<MemberPartBillItem> MergeBillItems(List<MemberPartBillItem> source)
        {
            var rows = new List<MemberPartBillItem>();
            if (source == null || source.Count == 0) return rows;

            Func<MemberPartBillItem, string> keyOf = x =>
                (x.SectionName ?? string.Empty).Trim().ToUpperInvariant() + "|" +
                (x.Material ?? string.Empty).Trim().ToUpperInvariant() + "|" +
                Math.Round(x.SingleLengthMm, 0).ToString("0", CultureInfo.InvariantCulture);

            var groups = source.GroupBy(keyOf, StringComparer.Ordinal).ToList();
            for (int i = 0; i < groups.Count; i++)
            {
                List<MemberPartBillItem> group = groups[i].ToList();
                MemberPartBillItem first = group[0];
                int partNo = i + 1;

                rows.Add(new MemberPartBillItem
                {
                    ParentTag = first.ParentTag,
                    PartNo = partNo,
                    PartRole = null,
                    SectionName = first.SectionName,
                    ShapeTypeName = first.ShapeTypeName,
                    Material = first.Material,
                    Count = group.Sum(x => x.Count),
                    SingleLengthMm = Math.Round(first.SingleLengthMm, 0),
                    UnitWeightKgPerM = first.UnitWeightKgPerM,
                    TotalWeightKg = Math.Round(group.Sum(x => x.TotalWeightKg), 2),
                    TotalSurfaceAreaM2 = Math.Round(group.Sum(x => x.TotalSurfaceAreaM2), 2)
                });

                foreach (MemberPartBillItem item in group)
                {
                    item.PartNo = partNo;
                }
            }

            return rows;
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

                    // 同一截面可能有多种下料长度，按实测长度再校正一次，避免合并后件号串行。
                    if (!part.IsPlate && b.SingleLengthMm > 1.0)
                    {
                        double partLen = EstimatePartLengthMm(part);
                        if (partLen > 1.0 &&
                            Math.Abs(partLen - b.SingleLengthMm) <= Math.Max(5.0, partLen * 0.02))
                        {
                            score += 6;
                        }
                    }

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

        private static double EstimatePartLengthMm(SteelStructureBuilder.SolidMemberPart part)
        {
            if (part == null) return 0.0;
            if (part.PathPoints != null && part.PathPoints.Count >= 2)
            {
                double sum = 0.0;
                for (int i = 1; i < part.PathPoints.Count; i++)
                    sum += part.PathPoints[i].DistanceTo(part.PathPoints[i - 1]);
                return sum;
            }
            return part.StartPt.DistanceTo(part.EndPt);
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
            Point3d viewBase,
            Func<Point3d, Point2d> proj2d,
            Func<Point3d, double> depthFunc,
            string viewTitle,
            Vector3d towardCamera,
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

                    // 板厚在图面上不足 0.22 字高时按单线符号表达，避免两条几乎重合的板边糊成一片。
                    bool thinPlate = Math.Min(maxX - minX, maxY - minY) < 0.22 * textH;

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
                        ThinPlate = part.IsPlate && thinPlate,
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

            // 二维工程符号出图：构件本体用真实截面/双线符号绘制。
            // 每个构件绘制前先登记"严格位于其前方的构件轮廓"，逐段判断遮挡并自动转虚线。
            var sorted = isPlanView
                ? boxes.OrderByDescending(b => b.MaxDepth).ToList()
                : boxes.OrderBy(b => b.MinDepth).ToList();

            var batch = new LineBatch();
            for (int i = 0; i < sorted.Count; i++)
            {
                var cur = sorted[i];

                batch.Occluders.Clear();
                for (int j = 0; j < i; j++)
                {
                    var front = sorted[j];
                    bool strictlyBehind = isPlanView
                        ? (front.MinDepth > cur.MaxDepth + 5.0)
                        : (front.MaxDepth < cur.MinDepth - 5.0);
                    if (strictlyBehind && front.Hull != null && front.Hull.Count >= 3)
                        batch.Occluders.Add(front.Hull);
                }

                // 完全被前方构件遮住的内部钢板不再出线，避免接头处堆叠无意义的矩形。
                if (cur.IsPlate && batch.Occluders.Count > 0 && HullCoveredRatio(cur.Hull, batch.Occluders) >= 0.92)
                    continue;

                DrawOrthographicPart2D(batch, cur, proj2d, towardCamera);
            }

            batch.Emit(tr, ms, viewBase, textH);

            // 非轴测图（正立面/侧立面/平面图）不标注件号，件号仅统一标注在轴测图上。

            GetDesignDimensionTicks(
                p, parts3d, proj2d, minViewX, maxViewX, minViewY, maxViewY,
                isFrontView, isSideView, isPlanView, overallOnly, textH,
                out List<double> xTicks, out List<double> yTicks);

            // 结构中心轴线（红点划线，伸出构件边缘 1.6 * textH，不再画左下角 L 型包框）
            DrawStructuralAxisLines(
                tr, ms, viewBase,
                minViewX, maxViewX, minViewY, maxViewY,
                xTicks, yTicks, isPlanView, textH);

            // 标准工程尺寸标注（详细分跨/分层轴线尺寸 + 总轴距 + 总外包尺寸，排列整齐）
            if (options.IncludeAutoDimensions)
            {
                AddAccurateDimensions(
                    tr, ms, viewBase,
                    minViewX, maxViewX, minViewY, maxViewY,
                    xTicks, yTicks,
                    dimStyleId, textH, dimGapStep);
            }

            // 视图名称居中放置于最外层尺寸线下方
            double titleY = minViewY - dimGapStep * 3.05 - 2.2 * textH;
            double titleHalfW = EstimateSimSunTextWidth(viewTitle, textH * 1.12) * 0.5;
            Point3d titlePt = new Point3d(viewBase.X + (minViewX + maxViewX) * 0.5 - titleHalfW, viewBase.Y + titleY, 0);
            AppendSimSunText(tr, ms, titlePt, viewTitle, textStyleId, textH * 1.12, LayerFrame);
        }

        /// <summary>
        /// 绘制正投影单个构件：支持曲线路径扫掠构件、端视真实型钢截面（H/C/L/Box/Pipe）以及侧视翼缘厚度线
        /// </summary>
        private static void DrawOrthographicPart2D(
            LineBatch batch,
            ProjectedPart2D projPart,
            Func<Point3d, Point2d> proj2d,
            Vector3d towardCamera)
        {
            var part = projPart.SourcePart;
            if (part == null)
            {
                batch.AddPolyline(projPart.Hull, true);
                return;
            }

            // 薄板在视图里按单线符号表达（制图惯例），比画出两条几乎重合的板边更清晰。
            if (projPart.ThinPlate)
            {
                double midX = (projPart.MinX + projPart.MaxX) * 0.5;
                double midY = (projPart.MinY + projPart.MaxY) * 0.5;
                if (projPart.MaxX - projPart.MinX >= projPart.MaxY - projPart.MinY)
                    batch.Add(new Point2d(projPart.MinX, midY), new Point2d(projPart.MaxX, midY));
                else
                    batch.Add(new Point2d(midX, projPart.MinY), new Point2d(midX, projPart.MaxY));
                return;
            }

            bool isPipe = part.Section != null && part.Section.Shape == SectionShapeType.PipeTube;
            if (isPipe && part.PathPoints != null && part.PathPoints.Count >= 2)
            {
                DrawSweptPathMember2D(batch, part, proj2d);
                return;
            }


            // 1. 连续多段/曲线扫掠杆件（如螺旋盘梯、环形平台、弧形桁架、连续栏杆转角）：沿路径绘制双边轮廓线，绝不画全局凸包
            if (part.Section != null && !part.IsPlate &&
                part.PathPoints != null && part.PathPoints.Count >= 2)
            {
                DrawSweptPathMember2D(batch, part, proj2d);
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
                    List<Point2d> outline = GetDrawingOutlinePoints(part.Section);
                    var endProfile2d = new List<Point2d>(outline.Count);
                    foreach (Point2d p2 in outline)
                    {
                        Point3d worldPt = part.StartPt + xDir * p2.X + yDir * p2.Y;
                        endProfile2d.Add(proj2d(worldPt));
                    }
                    batch.AddPolyline(endProfile2d, true);
                    return;
                }

                // 侧视/斜视直杆：绘制外轮廓 + 翼缘厚度特征线（展现真实 H 型钢 / 槽钢双翼缘线）
                batch.AddPolyline(projPart.Hull, true);

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
                            batch.Add(a, b);
                        }
                    }
                }
                return;
            }

            // 3. 钢板与其它实心件
            batch.AddPolyline(projPart.Hull, true);
        }

        private static void DrawSweptPathMember2D(
            LineBatch batch,
            SteelStructureBuilder.SolidMemberPart part,
            Func<Point3d, Point2d> proj2d)
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

            batch.AddPolyline(leftEdge, false);
            batch.AddPolyline(rightEdge, false);
            if (leftEdge.Count >= 2)
            {
                batch.Add(leftEdge[0], rightEdge[0]);
                int last = leftEdge.Count - 1;
                batch.Add(leftEdge[last], rightEdge[last]);
            }
        }

        /// <summary>
        /// 计算正立面、侧立面、平面图的设计轴线与构件详细刻度（涵盖柱距、分层标高、护栏、斜梯与区域组合，排列整齐不省略）。
        /// </summary>
        private static void GetDesignDimensionTicks(
            SteelStructureParams p,
            List<SteelStructureBuilder.SolidMemberPart> parts3d,
            Func<Point3d, Point2d> proj2d,
            double minViewX, double maxViewX,
            double minViewY, double maxViewY,
            bool isFrontView, bool isSideView, bool isPlanView,
            bool overallOnly,
            double textH,
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

            // 1. 从三维主构件中自动提取真实的柱轴线与梁/平台标高刻度（保证区域组合及各类复杂构件尺寸详尽无遗漏）
            if (parts3d != null && parts3d.Count > 0 && proj2d != null)
            {
                foreach (var part in parts3d)
                {
                    if (part == null) continue;
                    string role = part.RoleName ?? string.Empty;
                    bool isRailOrStep = role.Contains("踏步") || role.Contains("梯扶手") || role.Contains("梯中横杆") || role.Contains("护栏立柱");
                    if (isRailOrStep) continue;

                    Vector3d axis = part.EndPt - part.StartPt;
                    double axisLen = axis.Length;
                    if (!part.IsPlate && axisLen > 120.0)
                    {
                        Point2d p0 = proj2d(part.StartPt);
                        Point2d p1 = proj2d(part.EndPt);
                        // 视图内竖直杆件（如立柱）提供精确 X 轴刻度
                        if (Math.Abs(p1.X - p0.X) < 15.0 && Math.Abs(p1.Y - p0.Y) > 150.0)
                        {
                            xTicks.Add(Math.Round((p0.X + p1.X) * 0.5));
                            if (!isPlanView)
                            {
                                yTicks.Add(Math.Round(Math.Min(p0.Y, p1.Y)));
                                yTicks.Add(Math.Round(Math.Max(p0.Y, p1.Y)));
                            }
                        }
                        // 视图内水平杆件（如主梁、横梁、吊车梁、扶手顶杆）提供精确 Y 标高/轴线刻度
                        if (Math.Abs(p1.Y - p0.Y) < 15.0 && Math.Abs(p1.X - p0.X) > 150.0)
                        {
                            yTicks.Add(Math.Round((p0.Y + p1.Y) * 0.5));
                            xTicks.Add(Math.Round(Math.Min(p0.X, p1.X)));
                            xTicks.Add(Math.Round(Math.Max(p0.X, p1.X)));
                        }
                    }
                    else if (part.IsPlate && (role.Contains("平台") || role.Contains("花纹")))
                    {
                        Point2d pc = proj2d(part.EndPt);
                        if (!isPlanView) yTicks.Add(Math.Round(pc.Y));
                    }
                }
            }

            // 2. 补充精确设计轴网刻度 (xTicks)
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

                    if (p.StructureType != ParametricStructureType.ColumnRowAssembly &&
                        p.StructureType != ParametricStructureType.PipeRackAssembly &&
                        p.StructureType != ParametricStructureType.StairAssembly)
                    {
                        for (int i = 0; i <= bays; i++)
                            xTicks.Add(Math.Round(i * (span / bays)));
                    }
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
                else if (xTicks.Count < 2)
                {
                    xTicks.Add(Math.Round(minViewX));
                    xTicks.Add(Math.Round(maxViewX));
                }
            }

            // 3. 补充竖向设计轴网刻度 (yTicks)
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
                    else if (yTicks.Count < 2)
                    {
                        yTicks.Add(0.0);
                        yTicks.Add(Math.Round(len));
                    }
                }
                else if (yTicks.Count < 2)
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
                    case ParametricStructureType.ColumnRowAssembly:
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
                    case ParametricStructureType.PlatformAssembly:
                    case ParametricStructureType.CantileverWalkway:
                    case ParametricStructureType.StraightStair:
                    case ParametricStructureType.StairAssembly:
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
                        else if (yTicks.Count < 2)
                        {
                            yTicks.Add(Math.Round(minViewY));
                            yTicks.Add(Math.Round(maxViewY));
                        }
                        break;
                    }
                }
            }

            // 过滤过近刻度，确保相邻尺寸数字排列整齐、互不重叠
            double minTickGap = Math.Max(80.0, textH * 1.45);
            xTicks = CleanSortedTicks(xTicks, minViewX, maxViewX, minTickGap);
            yTicks = CleanSortedTicks(yTicks, minViewY, maxViewY, minTickGap);
        }

        private static List<double> CleanSortedTicks(List<double> raw, double minBound, double maxBound, double minGap)
        {
            double pad = 25.0;
            var sorted = raw
                .Where(v => v >= minBound - pad && v <= maxBound + pad)
                .OrderBy(v => v)
                .ToList();
            if (sorted.Count < 2)
            {
                return new List<double> { Math.Round(minBound), Math.Round(maxBound) };
            }

            var clean = new List<double> { sorted[0] };
            double lastVal = sorted[sorted.Count - 1];
            for (int i = 1; i < sorted.Count - 1; i++)
            {
                double v = sorted[i];
                if (v - clean[clean.Count - 1] >= minGap && lastVal - v >= minGap)
                    clean.Add(v);
            }
            if (lastVal - clean[clean.Count - 1] >= 20.0)
                clean.Add(lastVal);

            if (clean.Count < 2)
            {
                clean.Clear();
                clean.Add(Math.Round(minBound));
                clean.Add(Math.Round(maxBound));
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
            // 1. 底部水平尺寸标注（三道等距整齐排列：第一道分跨链式尺寸；第二道轴线总跨；第三道外包总宽）
            if (xTicks != null && xTicks.Count >= 2 && (xTicks[xTicks.Count - 1] - xTicks[0]) >= 5.0)
            {
                double yAnchor = minViewY;
                double yTier1 = viewBase.Y + minViewY - dimGapStep * 1.0;
                double yTier2 = viewBase.Y + minViewY - dimGapStep * 1.95;
                double yTier3 = viewBase.Y + minViewY - dimGapStep * 2.90;
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
                            new Point3d(viewBase.X + (x0 + x1) * 0.5, yTier1, 0),
                            0.0, dimStyleId, textH);
                    }
                }

                double xStart = xTicks[0];
                double xEnd = xTicks[xTicks.Count - 1];
                double totalY = hasSubBays ? yTier2 : yTier1;
                AppendLinearDim(tr, ms,
                    new Point3d(viewBase.X + xStart, viewBase.Y + yAnchor, 0),
                    new Point3d(viewBase.X + xEnd, viewBase.Y + yAnchor, 0),
                    new Point3d(viewBase.X + (xStart + xEnd) * 0.5, totalY, 0),
                    0.0, dimStyleId, textH);

                // 若外包总宽明显大于轴线总宽（如悬挑梁、底板、斜梯），增标最外层总外包尺寸
                if (Math.Abs((maxViewX - minViewX) - (xEnd - xStart)) >= 35.0)
                {
                    double outerY = hasSubBays ? yTier3 : yTier2;
                    AppendLinearDim(tr, ms,
                        new Point3d(viewBase.X + minViewX, viewBase.Y + yAnchor, 0),
                        new Point3d(viewBase.X + maxViewX, viewBase.Y + yAnchor, 0),
                        new Point3d(viewBase.X + (minViewX + maxViewX) * 0.5, outerY, 0),
                        0.0, dimStyleId, textH);
                }
            }

            // 2. 左侧竖向尺寸标注（三道等距整齐排列：第一道分层/分跨链式尺寸；第二道轴线总高/深；第三道外包总高/深）
            if (yTicks != null && yTicks.Count >= 2 && (yTicks[yTicks.Count - 1] - yTicks[0]) >= 5.0)
            {
                double xAnchor = minViewX;
                double xTier1 = viewBase.X + minViewX - dimGapStep * 1.0;
                double xTier2 = viewBase.X + minViewX - dimGapStep * 1.95;
                double xTier3 = viewBase.X + minViewX - dimGapStep * 2.90;
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
                            new Point3d(xTier1, viewBase.Y + (y0 + y1) * 0.5, 0),
                            Math.PI * 0.5, dimStyleId, textH);
                    }
                }

                double yStart = yTicks[0];
                double yEnd = yTicks[yTicks.Count - 1];
                double totalX = hasSubTiers ? xTier2 : xTier1;
                AppendLinearDim(tr, ms,
                    new Point3d(viewBase.X + xAnchor, viewBase.Y + yStart, 0),
                    new Point3d(viewBase.X + xAnchor, viewBase.Y + yEnd, 0),
                    new Point3d(totalX, viewBase.Y + (yStart + yEnd) * 0.5, 0),
                    Math.PI * 0.5, dimStyleId, textH);

                if (Math.Abs((maxViewY - minViewY) - (yEnd - yStart)) >= 35.0)
                {
                    double outerX = hasSubTiers ? xTier3 : xTier2;
                    AppendLinearDim(tr, ms,
                        new Point3d(viewBase.X + xAnchor, viewBase.Y + minViewY, 0),
                        new Point3d(viewBase.X + xAnchor, viewBase.Y + maxViewY, 0),
                        new Point3d(outerX, viewBase.Y + (minViewY + maxViewY) * 0.5, 0),
                        Math.PI * 0.5, dimStyleId, textH);
                }
            }
        }

        /// <summary>
        /// 西南等轴测三维特征图：全实线清晰展现三维棱边与曲线路径，且仅在左右两侧标注水平件号引线 (1, 2...)，与材料表 100% 对应！
        /// </summary>
        private static void DrawIsometricHlrView(
            Transaction tr,
            BlockTableRecord ms,
            SteelStructureParams p,
            List<SteelStructureBuilder.SolidMemberPart> parts3d,
            List<MemberPartBillItem> billItems,
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
            var partExtents = new List<KeyValuePair<SteelStructureBuilder.SolidMemberPart, Extents3d>>();
            foreach (var item in parts3d)
            {
                if (item?.Solid == null || item.Solid.IsDisposed) continue;
                try { partExtents.Add(new KeyValuePair<SteelStructureBuilder.SolidMemberPart, Extents3d>(item, item.Solid.GeometricExtents)); }
                catch { }
            }

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
                    Point3d center = ext.MinPoint + (ext.MaxPoint - ext.MinPoint) * 0.5;
                    // 观察者在 (-X,-Y,+Z) 方向：数值越小越远，按由远到近绘制。
                    double depth = TowardCameraDepth(center);

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
                        NearEndExposed = part.Section != null && !part.IsPlate &&
                            IsEndExposed(partExtents, part,
                                TowardCameraDepth(part.StartPt) >= TowardCameraDepth(part.EndPt)
                                    ? part.StartPt
                                    : part.EndPt),
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

            // 三维模型 → 轴测投影 → 平面压平：由远到近逐件入列，最后统一合并落笔。
            var isoBatch = new LineBatch();
            Vector3d isoCamera = new Vector3d(-1, -1, 1).GetNormal();
            foreach (IsoPart2D cur in isoParts.OrderBy(x => x.Depth))
            {
                DrawIsoMember2D(isoBatch, cur, isoProj, isoCamera);
            }
            isoBatch.Emit(tr, ms, isoBase, textH);

            if (options.IncludeLeaderAnnotations && isoParts.Count > 0)
            {
                var allLocalPts = new List<Point2d>();
                foreach (var ip in isoParts)
                {
                    if (ip.Hull != null) allLocalPts.AddRange(ip.Hull);
                }
                List<Point2d> viewHullLocal = ConvexHull(allLocalPts);

                var leaderReqs = new List<LeaderRequest>();
                double cx = (minIsoX + maxIsoX) * 0.5;
                double diag = Math.Max(1.0, Math.Sqrt(
                    (maxIsoX - minIsoX) * (maxIsoX - minIsoX) +
                    (maxIsoY - minIsoY) * (maxIsoY - minIsoY)));

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

                // 候选数少的件号优先选点，避免多根同类构件（如立柱/斜撑/主梁）全部扎堆到同一端跨
                var orderedPartNos = targetPartNos
                    .OrderBy(no => groupedByNo.TryGetValue(no, out var list) ? list.Count : int.MaxValue)
                    .ThenBy(no => no)
                    .ToList();

                double minDepth = isoParts.Min(x => x.Depth);
                double maxDepth = isoParts.Max(x => x.Depth);
                double depthSpan = Math.Max(1.0, maxDepth - minDepth);
                var chosenAnchors = new List<Point2d>();

                foreach (int partNo in orderedPartNos)
                {
                    IsoPart2D rep = null;
                    if (groupedByNo.TryGetValue(partNo, out List<IsoPart2D> candidates) && candidates.Count > 0)
                    {
                        double bestScore = double.NegativeInfinity;
                        foreach (var cand in candidates)
                        {
                            // 1) 离外包轮廓越近，引线越短且越不易穿越其它杆件
                            double distToEdge = DistanceToHullBoundary(cand.Anchor2D, viewHullLocal);
                            // 2) 与已选锚点保持分散，避免多条引线挤在同一区域
                            double minSep = chosenAnchors.Count == 0
                                ? diag * 0.25
                                : Math.Min(diag * 0.28, chosenAnchors.Min(a => a.GetDistanceTo(cand.Anchor2D)));
                            // 3) 优先选靠近观察者、外露清晰的构件
                            double depthNorm = (cand.Depth - minDepth) / depthSpan;

                            double score = minSep * 1.15 - distToEdge * 1.35 + depthNorm * (diag * 0.08);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                rep = cand;
                            }
                        }
                    }
                    else
                    {
                        rep = isoParts[(partNo - 1) % isoParts.Count];
                    }

                    chosenAnchors.Add(rep.Anchor2D);
                    int side = rep.Anchor2D.X >= cx ? 1 : 0;
                    leaderReqs.Add(new LeaderRequest
                    {
                        AnchorPt = new Point3d(isoBase.X + rep.Anchor2D.X, isoBase.Y + rep.Anchor2D.Y, 0),
                        LabelText = partNo.ToString(CultureInfo.InvariantCulture),
                        Side = side
                    });
                }

                var viewHullWorld = viewHullLocal
                    .Select(pt => new Point2d(isoBase.X + pt.X, isoBase.Y + pt.Y))
                    .ToList();

                PlaceCollisionFreeLeaders(
                    tr, ms, leaderReqs, viewHullWorld,
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
                isoBase.Y + minIsoY - 3.2 * textH,
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
            LineBatch batch,
            IsoPart2D isoPart,
            Func<Point3d, Point2d> isoProj,
            Vector3d towardCamera)
        {
            var part = isoPart.SourcePart;
            if (part == null)
            {
                batch.AddPolyline(isoPart.Hull, true);
                return;
            }

            bool isPipe = part.Section != null && part.Section.Shape == SectionShapeType.PipeTube;
            if (isPipe && part.PathPoints != null && part.PathPoints.Count >= 2)
            {
                DrawSweptPathMember2D(batch, part, isoProj);
                return;
            }


            // 1. 曲线/多段扫掠构件：沿路径绘制平滑双轨轮廓
            if (part.Section != null && !part.IsPlate &&
                part.PathPoints != null && part.PathPoints.Count >= 2)
            {
                DrawSweptPathMember2D(batch, part, isoProj);
                return;
            }

            // 2. 直杆型钢构件：绘制外包轮廓 + 端部截面
            Vector3d axis = part.EndPt - part.StartPt;
            if (part.Section != null && !part.IsPlate && axis.Length > 1.0)
            {
                batch.AddPolyline(isoPart.Hull, true);

                // 圆钢管：轴测投影就是“两条外切母线 + 两端平滑圆弧”，只保留这一条纯净外轮廓。
                // 不再产生任何棱柱母线、端面切片圆环或相切接缝。
                if (part.Section.Shape == SectionShapeType.PipeTube) return;

                // 端部埋进其他构件（焊接接头）时不画端面，避免接头处堆叠出不存在的截面线。
                if (!isoPart.NearEndExposed) return;

                SectionSolidBuilder.GetMemberAxes(axis, out Vector3d xDir, out Vector3d yDir, out _);

                // 只画靠近观察者的那个端面，并且用真实型钢截面（H 形 / C 形 / 角钢 / 方矩管）而不是包围盒四边形
                Point3d nearEnd = TowardCameraDepth(part.StartPt) >= TowardCameraDepth(part.EndPt)
                    ? part.StartPt
                    : part.EndPt;

                List<Point2d> outline = GetDrawingOutlinePoints(part.Section);
                var endProfile = new List<Point2d>(outline.Count);
                foreach (Point2d p2 in outline)
                    endProfile.Add(isoProj(nearEnd + xDir * p2.X + yDir * p2.Y));
                batch.AddPolyline(endProfile, true);
                return;
            }

            // 3. 钢板与其它实心构件
            batch.AddPolyline(isoPart.Hull, true);
        }

        private static double TowardCameraDepth(Point3d pt) => -pt.X - pt.Y + pt.Z;

        /// <summary>
        /// 判断杆件端部是否真正外露：若端点落在其他构件的包围盒内，说明是焊接接头，不画端面轮廓。
        /// </summary>
        private static bool IsEndExposed(
            List<KeyValuePair<SteelStructureBuilder.SolidMemberPart, Extents3d>> partExtents,
            SteelStructureBuilder.SolidMemberPart self,
            Point3d endPt)
        {
            const double eps = 6.0;
            foreach (var kv in partExtents)
            {
                if (ReferenceEquals(kv.Key, self)) continue;
                Extents3d ext = kv.Value;

                if (endPt.X >= ext.MinPoint.X - eps && endPt.X <= ext.MaxPoint.X + eps &&
                    endPt.Y >= ext.MinPoint.Y - eps && endPt.Y <= ext.MaxPoint.Y + eps &&
                    endPt.Z >= ext.MinPoint.Z - eps && endPt.Z <= ext.MaxPoint.Z + eps)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 单段直线箭头件号引线：沿轴测图实际外包凸包就近向外引出（不拉到包围盒远角），
        /// 经圆圈防重叠排斥与 2-opt 去交叉后，直接绘制「单段箭头直线 + 圆圈件号文字」。
        /// </summary>
        private static void PlaceCollisionFreeLeaders(
            Transaction tr,
            BlockTableRecord ms,
            List<LeaderRequest> requests,
            List<Point2d> viewHull,
            double viewMinX,
            double viewMaxX,
            double viewMinY,
            double viewMaxY,
            ObjectId textStyleId,
            double textH)
        {
            if (requests == null || requests.Count == 0) return;

            List<LeaderRequest> valid = requests
                .Where(x => x != null && !string.IsNullOrEmpty(x.LabelText))
                .ToList();
            if (valid.Count == 0) return;

            int n = valid.Count;
            double[] radii = new double[n];
            Point2d[] anchors = new Point2d[n];
            Point2d[] centers = new Point2d[n];

            double viewCx = (viewMinX + viewMaxX) * 0.5;
            double maxSideOut = 4.6 * textH;
            double maxTopOut = 2.6 * textH;
            double titleSafeY = viewMinY - 0.6 * textH;
            double minXBound = viewMinX - maxSideOut;
            double maxXBound = viewMaxX + maxSideOut;
            double maxYBound = viewMaxY + maxTopOut;

            double baseClearance = 2.4 * textH;
            double minHullGap = 1.6 * textH;

            // 1. 逐件按就近外法向在凸包外侧生成紧凑圆心位置
            for (int i = 0; i < n; i++)
            {
                string label = valid[i].LabelText ?? string.Empty;
                double textW = EstimateSimSunTextWidth(label, textH * 0.92);
                double r = Math.Max(0.90 * textH, textW * 0.58 + 0.28 * textH);
                radii[i] = r;
                anchors[i] = new Point2d(valid[i].AnchorPt.X, valid[i].AnchorPt.Y);

                double desiredClearance = baseClearance + r;
                FindBestHullExit(
                    anchors[i], viewHull, viewCx, titleSafeY, desiredClearance,
                    out Point2d exitPt, out Vector2d outDir);

                Point2d c = exitPt + outDir * desiredClearance;
                // 保证箭头直线长度足够清晰（至少 2.0 * textH + r）
                Vector2d fromAnchor = c - anchors[i];
                double minLen = 2.0 * textH + r;
                if (fromAnchor.Length < minLen)
                {
                    Vector2d dir = fromAnchor.Length > 1e-6 ? fromAnchor.GetNormal() : outDir;
                    c = anchors[i] + dir * minLen;
                }

                centers[i] = ClampBalloonCenter(c, minXBound, maxXBound, titleSafeY, maxYBound);
            }

            // 2. 迭代松弛：圆圈间防重叠 + 保持严格位于视图凸包外侧
            for (int iter = 0; iter < 32; iter++)
            {
                bool moved = false;

                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        double minDist = radii[i] + radii[j] + 0.95 * textH;
                        Vector2d delta = centers[j] - centers[i];
                        double d = delta.Length;
                        if (d < minDist)
                        {
                            Vector2d pushDir = d > 1e-6 ? delta / d : new Vector2d(0, 1);
                            double shift = (minDist - d) * 0.52;
                            centers[i] -= pushDir * shift;
                            centers[j] += pushDir * shift;
                            moved = true;
                        }
                    }
                }

                for (int i = 0; i < n; i++)
                {
                    Point2d pushed = EnsureOutsideHull(centers[i], viewHull, viewCx, minHullGap + radii[i]);
                    pushed = ClampBalloonCenter(pushed, minXBound, maxXBound, titleSafeY, maxYBound);
                    if (pushed.GetDistanceTo(centers[i]) > 1e-3)
                    {
                        centers[i] = pushed;
                        moved = true;
                    }
                }

                if (!moved) break;
            }

            // 3. 2-opt 去交叉：若两条引线线段相交，交换目标圆心（三角不等式保证总长严格缩短且必收敛）
            int maxPasses = n * n + 4;
            for (int pass = 0; pass < maxPasses; pass++)
            {
                bool swapped = false;
                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        if (SegmentsProperlyIntersect(anchors[i], centers[i], anchors[j], centers[j]))
                        {
                            Point2d tmp = centers[i];
                            centers[i] = centers[j];
                            centers[j] = tmp;
                            swapped = true;
                        }
                    }
                }
                if (!swapped) break;
            }

            // 4. 落笔绘制：单段直线 + 标准扁平箭头 + 圆圈 + 居中件号文字（无两段折线、无缺口）
            for (int i = 0; i < n; i++)
            {
                Point3d anchor = new Point3d(anchors[i].X, anchors[i].Y, 0);
                Point3d center = new Point3d(centers[i].X, centers[i].Y, 0);
                double balloonR = radii[i];

                Vector3d toCenter = center - anchor;
                double dist = toCenter.Length;
                if (dist > balloonR + 1e-3)
                {
                    Point3d edgePt = center - toCenter.GetNormal() * balloonR;
                    AddLeaderLine(tr, ms, anchor, edgePt, LayerDim);
                    AddLeaderArrow(tr, ms, anchor, edgePt, textH, LayerDim);
                }

                var circle = new Circle(center, Vector3d.ZAxis, balloonR);
                circle.SetDatabaseDefaults(ms.Database);
                circle.Layer = LayerDim;
                ms.AppendEntity(circle);
                tr.AddNewlyCreatedDBObject(circle, true);

                AppendCenteredSimSunText(tr, ms, center, valid[i].LabelText, textStyleId, textH * 0.92, LayerDim);
            }
        }

        private static double DistanceToHullBoundary(Point2d pt, List<Point2d> hull)
        {
            if (hull == null || hull.Count < 2) return 0.0;
            double best = double.MaxValue;
            for (int i = 0; i < hull.Count; i++)
            {
                Point2d a = hull[i];
                Point2d b = hull[(i + 1) % hull.Count];
                Point2d proj = ProjectPointToSegment2d(pt, a, b);
                double d = pt.GetDistanceTo(proj);
                if (d < best) best = d;
            }
            return best;
        }

        private static Point2d ProjectPointToSegment2d(Point2d p, Point2d a, Point2d b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-9) return a;
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;
            return new Point2d(a.X + t * dx, a.Y + t * dy);
        }

        private static bool IsInsideCcwHull(Point2d p, List<Point2d> hull)
        {
            if (hull == null || hull.Count < 3) return false;
            for (int i = 0; i < hull.Count; i++)
            {
                Point2d a = hull[i];
                Point2d b = hull[(i + 1) % hull.Count];
                if (Cross(a, b, p) < -1e-6) return false;
            }
            return true;
        }

        private static void FindBestHullExit(
            Point2d anchor,
            List<Point2d> hull,
            double viewCx,
            double titleSafeY,
            double clearance,
            out Point2d exitPt,
            out Vector2d outDir)
        {
            exitPt = anchor;
            outDir = new Vector2d(anchor.X < viewCx ? -1.0 : 1.0, 0.25).GetNormal();
            if (hull == null || hull.Count < 3) return;

            double bestSafeDist = double.MaxValue;
            Point2d bestSafeExit = exitPt;
            Vector2d bestSafeDir = outDir;

            double bestAnyDist = double.MaxValue;
            Point2d bestAnyExit = exitPt;
            Vector2d bestAnyDir = outDir;

            for (int i = 0; i < hull.Count; i++)
            {
                Point2d a = hull[i];
                Point2d b = hull[(i + 1) % hull.Count];
                double ex = b.X - a.X;
                double ey = b.Y - a.Y;
                double len = Math.Sqrt(ex * ex + ey * ey);
                if (len < 1e-6) continue;

                // CCW 凸包边的右手外法向
                Vector2d normal = new Vector2d(ey / len, -ex / len);
                Point2d proj = ProjectPointToSegment2d(anchor, a, b);
                double d = anchor.GetDistanceTo(proj);

                if (d < bestAnyDist)
                {
                    bestAnyDist = d;
                    bestAnyExit = proj;
                    bestAnyDir = normal;
                }

                // 避开视图正下方标题区域（轴测图 (仅标件号)）
                Point2d testCenter = proj + normal * clearance;
                if (testCenter.Y >= titleSafeY && d < bestSafeDist)
                {
                    bestSafeDist = d;
                    bestSafeExit = proj;
                    bestSafeDir = normal;
                }
            }

            if (bestSafeDist < double.MaxValue)
            {
                exitPt = bestSafeExit;
                outDir = bestSafeDir;
            }
            else
            {
                exitPt = bestAnyExit;
                outDir = new Vector2d(anchor.X < viewCx ? -1.0 : 1.0, Math.Max(0.0, bestAnyDir.Y)).GetNormal();
            }
        }

        private static Point2d EnsureOutsideHull(Point2d pt, List<Point2d> hull, double viewCx, double minMargin)
        {
            if (hull == null || hull.Count < 3) return pt;

            bool inside = IsInsideCcwHull(pt, hull);
            double bestDist = double.MaxValue;
            Point2d bestProj = pt;
            Vector2d bestNormal = new Vector2d(pt.X < viewCx ? -1.0 : 1.0, 0.0);

            for (int i = 0; i < hull.Count; i++)
            {
                Point2d a = hull[i];
                Point2d b = hull[(i + 1) % hull.Count];
                double ex = b.X - a.X;
                double ey = b.Y - a.Y;
                double len = Math.Sqrt(ex * ex + ey * ey);
                if (len < 1e-6) continue;

                Point2d proj = ProjectPointToSegment2d(pt, a, b);
                double d = pt.GetDistanceTo(proj);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestProj = proj;
                    bestNormal = new Vector2d(ey / len, -ex / len);
                }
            }

            if (inside)
            {
                return bestProj + bestNormal * minMargin;
            }

            if (bestDist < minMargin)
            {
                Vector2d away = pt - bestProj;
                Vector2d dir = away.Length > 1e-6 ? away.GetNormal() : bestNormal;
                return bestProj + dir * minMargin;
            }

            return pt;
        }

        private static Point2d ClampBalloonCenter(Point2d pt, double minX, double maxX, double minY, double maxY)
        {
            double x = Math.Max(minX, Math.Min(maxX, pt.X));
            double y = Math.Max(minY, Math.Min(maxY, pt.Y));
            return new Point2d(x, y);
        }

        private static bool SegmentsProperlyIntersect(Point2d a1, Point2d a2, Point2d b1, Point2d b2)
        {
            double d1 = Cross(a1, a2, b1);
            double d2 = Cross(a1, a2, b2);
            double d3 = Cross(b1, b2, a1);
            double d4 = Cross(b1, b2, a2);
            return ((d1 > 1e-6 && d2 < -1e-6) || (d1 < -1e-6 && d2 > 1e-6)) &&
                   ((d3 > 1e-6 && d4 < -1e-6) || (d3 < -1e-6 && d4 > 1e-6));
        }

        private static void AddLeaderLine(Transaction tr, BlockTableRecord ms, Point3d a, Point3d b, string layer)
        {
            var ln = new Line(a, b);
            ln.SetDatabaseDefaults(ms.Database);
            ln.Layer = layer;
            ms.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
        }

        /// <summary>扁平标准箭头（长宽比约 3.2:1，符合国标机械/钢结构制图标准）。</summary>
        private static void AddLeaderArrow(Transaction tr, BlockTableRecord ms, Point3d tip, Point3d toward, double textH, string layer)
        {
            double length = Math.Min(1.25 * textH, tip.DistanceTo(toward) * 0.45);
            if (length < 1e-6) return;

            Vector3d dir = toward - tip;
            if (dir.Length < 1e-9) return;
            dir = dir.GetNormal();

            Vector3d perp = dir.CrossProduct(Vector3d.ZAxis);
            if (perp.Length < 1e-9) perp = new Vector3d(0, 1, 0);
            // 扁平标准箭头：半宽 = 0.16 * length（全宽约 1/3 箭长）
            perp = perp.GetNormal() * (length * 0.16);

            Point3d b1 = tip + dir * length + perp;
            Point3d b2 = tip + dir * length - perp;
            var arrow = new Solid(tip, b1, b2, b2);
            arrow.SetDatabaseDefaults(ms.Database);
            arrow.Layer = layer;
            ms.AppendEntity(arrow);
            tr.AddNewlyCreatedDBObject(arrow, true);
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

            // 各列宽度比例（总和严格等于 1.00）：件号 / 截面规格 / 材质 / 数量 / 单长 / 重(kg)
            double[] ratios = { 0.07, 0.35, 0.13, 0.10, 0.15, 0.20 };
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
            string[] headers = { "件号", "截面规格", "材质", "数量", "单长", "重(kg)" };
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
                    item.PartNo.ToString(CultureInfo.InvariantCulture),
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
                new Point3d(bottomLeft.X + (colX[1] + colX[2]) * 0.5, totalRowCenterY, 0),
                $"总重: {sumKg:0.00} kg", textStyleId, textH * 0.96, LayerFrame);
            AppendCenteredSimSunText(
                tr, ms,
                new Point3d(bottomLeft.X + (colX[5] + colX[6]) * 0.5, totalRowCenterY, 0),
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

        internal static void DrawRectangle2D(
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

        internal static void AppendLine2D(
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
            dim.Dimasz = textH * 0.65;
            dim.Dimgap = textH * 0.28;
            dim.Dimexe = textH * 0.42;
            dim.Dimexo = textH * 0.35;
            dim.Dimdec = 0;
            dim.Dimtad = 1;
            dim.Dimtih = false;
            dim.Dimtoh = false;
            dim.Dimtmove = 2;
            ms.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
        }

        internal static void AppendSimSunText(
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

        internal static void AppendCenteredSimSunText(
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
