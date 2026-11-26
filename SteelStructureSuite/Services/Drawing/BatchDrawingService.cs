using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Modeling;
using SteelStructureSuite.Services.Takeoff;

namespace SteelStructureSuite.Services.Drawing
{
    /// <summary>批量出图的进度快照。</summary>
    public sealed class BatchDrawingProgress
    {
        /// <summary>当前阶段说明，例如「正在生成 3/12：门式刚架 ST-101」。</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>已完成数量（0 起）。</summary>
        public int Completed { get; set; }

        /// <summary>总数量。</summary>
        public int Total { get; set; }

        /// <summary>0~100。</summary>
        public int Percent => Total <= 0 ? 0 : (int)Math.Round(Completed * 100.0 / Total);

        /// <summary>已生成的图名（用于结束汇总）。</summary>
        public List<string> GeneratedSheets { get; } = new List<string>();
    }

    /// <summary>批量出图结果。</summary>
    public sealed class BatchDrawingResult
    {
        public bool Cancelled { get; set; }
        public int SheetCount { get; set; }
        public int StructureCount { get; set; }
        public int ReusedCount { get; set; }
        public string OutputPath { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// 批量出图服务：把一批构件按复用规则排成若干张标准图框，
    /// 附带图纸目录与汇总材料表，支持进度回报与随时取消。
    ///
    /// 与单件出图的区别：
    ///   · 多张图框按网格排布在同一张图纸空间里（可以是新 DWG，也可以是当前 DWG）；
    ///   · 自动生成「图纸目录」表格（图号、图名、图幅、比例、构件数）；
    ///   · 自动生成「汇总材料表」（跨所有图合并同规格零件）；
    ///   · 每张图都回报进度，用户可随时取消，已生成的图保留不丢弃。
    /// </summary>
    public static class BatchDrawingService
    {
        /// <summary>每行最多排几张图（按图幅宽度动态决定，这里是上限）。</summary>
        private const int MaxSheetsPerRow = 3;

        /// <summary>图框之间的间距（mm）。</summary>
        private const double SheetGapMm = 40.0;

        /// <summary>
        /// 执行批量出图。
        /// </summary>
        /// <param name="db">目标数据库（当前 DWG 的 Database，或新建的 Database）。</param>
        /// <param name="plan">出图计划。</param>
        /// <param name="origin">整批图纸的左下角起点。</param>
        /// <param name="options">出图选项（图幅、比例、视图开关等）。</param>
        /// <param name="progress">进度回调，可在此更新界面。</param>
        /// <param name="shouldCancel">取消判定，返回 true 则停止后续图纸。</param>
        public static BatchDrawingResult GenerateBatch(
            Database db,
            BatchDrawingPlan plan,
            Point3d origin,
            DrawingGenerationOptions options,
            Action<BatchDrawingProgress> progress = null,
            Func<bool> shouldCancel = null)
        {
            var result = new BatchDrawingResult();
            if (db == null || plan == null || plan.Groups.Count == 0) return result;
            if (options == null) options = new DrawingGenerationOptions();

            result.StructureCount = plan.TotalStructureCount;
            result.ReusedCount = plan.SavedSheetCount;

            var state = new BatchDrawingProgress { Total = plan.Groups.Count };
            progress?.Invoke(state);

            // 1. 计算图幅基准尺寸，严格按每张图的实际比例与外框宽度横向向右排列，互不重叠
            GetSheetSizeMm(options.SheetSizeName, out double paperW, out double paperH);

            var placed = new List<string>();
            var placedGroups = new List<BatchDrawingPlan.SheetGroup>();
            var placedScales = new List<double>();
            double curX = origin.X;
            double maxScale = options.AutoScale ? 20.0 : Math.Max(1.0, options.DrawingScale);

            for (int i = 0; i < plan.Groups.Count; i++)
            {
                if (shouldCancel != null && shouldCancel())
                {
                    result.Cancelled = true;
                    break;
                }

                BatchDrawingPlan.SheetGroup group = plan.Groups[i];
                var sheetOrigin = new Point3d(curX, origin.Y, origin.Z);

                state.Message = string.Format(
                    CultureInfo.CurrentCulture,
                    "正在生成 {0}/{1}：{2}（{3}）",
                    i + 1,
                    plan.Groups.Count,
                    group.Representative.StructureTypeDisplayName,
                    group.TagSummary);

                // 单张图复用现有单件出图能力，保证样式与单件出图完全一致
                var opts = options.Clone();
                // 复用组内构件数量写进图名
                string caption = group.Count > 1
                    ? $"图名: {group.Representative.StructureTypeDisplayName} 详图（{group.TagSummary}，共 {group.Count} 件）"
                    : null;

                SteelHlrDrawingService.SheetLayoutResult layout = null;
                try
                {
                    layout = SteelHlrDrawingService.GenerateDetailDrawingSheetWithBounds(
                        db,
                        new[] { group.Representative },
                        sheetOrigin,
                        opts,
                        caption);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"第 {i + 1} 张图（{group.TagSummary}）生成失败：{ex.Message}");
                    state.Completed = i + 1;
                    progress?.Invoke(state);
                    continue;
                }

                double usedScale = (layout != null && layout.UsedScale > 0.1) ? layout.UsedScale : maxScale;
                double usedW = (layout != null && layout.FrameWidth > 1.0) ? layout.FrameWidth : paperW * usedScale;
                double gapX = Math.Max(1500.0, usedW * 0.08);
                curX += usedW + gapX;
                maxScale = Math.Max(maxScale, usedScale);

                placed.Add(caption ?? $"图名: {group.Representative.StructureTypeDisplayName} 详图（{group.TagSummary}）");
                placedGroups.Add(group);
                placedScales.Add(usedScale);
                state.GeneratedSheets.Add(placed[placed.Count - 1]);
                state.Completed = i + 1;
                progress?.Invoke(state);
            }

            // 2. 图纸目录 + 汇总材料表（紧跟在所有详图最右侧横向排列，互不重叠）
            bool wantIndex = options.IncludeDrawingIndex;
            bool wantSummary = options.IncludeSummaryBom;

            if (placed.Count > 0 && (wantIndex || wantSummary))
            {
                if (shouldCancel == null || !shouldCancel())
                {
                    state.Message = wantIndex && wantSummary
                        ? "正在生成图纸目录与汇总材料表..."
                        : (wantIndex ? "正在生成图纸目录..." : "正在生成汇总材料表...");
                    progress?.Invoke(state);

                    try
                    {
                        AppendDrawingIndexAndSummary(
                            db,
                            placedGroups,
                            placed,
                            placedScales,
                            new Point3d(curX, origin.Y, origin.Z),
                            options,
                            maxScale,
                            wantIndex,
                            wantSummary);
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add("图纸目录/汇总材料表生成失败：" + ex.Message);
                    }
                }
            }

            result.SheetCount = placed.Count;
            state.Message = result.Cancelled
                ? $"已取消，已生成 {placed.Count} 张图。"
                : $"完成，共 {placed.Count} 张图。";
            progress?.Invoke(state);

            return result;
        }

        /// <summary>
        /// 批量出图到新的 DWG 文件。
        /// </summary>
        public static BatchDrawingResult GenerateBatchToNewDwgFile(
            BatchDrawingPlan plan,
            string saveDwgPath,
            DrawingGenerationOptions options,
            Action<BatchDrawingProgress> progress = null,
            Func<bool> shouldCancel = null)
        {
            if (string.IsNullOrWhiteSpace(saveDwgPath))
                throw new ArgumentException("新 DWG 文件路径不能为空。");

            string path = SteelHlrDrawingService.NormalizeDwgPath(saveDwgPath);
            BatchDrawingResult result;

            using (Database newDb = new Database(true, true))
            using (WorkingDatabaseScope.Use(newDb))
            {
                result = GenerateBatch(newDb, plan, Point3d.Origin, options, progress, shouldCancel);

                // 即使中途取消，也把已生成的部分保存下来
                if (result.SheetCount > 0)
                    SteelHlrDrawingService.SaveDatabaseToDwg(newDb, path);
            }

            result.OutputPath = path;
            return result;
        }

        // ------------------------------------------------------------------
        // 图纸目录 + 汇总材料表
        // ------------------------------------------------------------------

        private static void AppendDrawingIndexAndSummary(
            Database db,
            List<BatchDrawingPlan.SheetGroup> groups,
            List<string> sheetCaptions,
            List<double> usedScales,
            Point3d origin,
            DrawingGenerationOptions options,
            double referenceScale,
            bool wantIndex,
            bool wantSummary)
        {
            double scale = Math.Max(10.0, referenceScale);
            double baseTextH = SteelStructureSuite.Services.Settings.UserSettings.GetCadDisplaySettings().BaseTextHeightMm;
            double textH = baseTextH * scale;
            double rowH = textH * 2.2;
            double tableGap = 25.0 * scale;

            SteelHlrDrawingService.EnsureDrawingLayers(db);
            ObjectId textStyleId = SteelHlrDrawingService.EnsureSimSunTextStyle(db);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                Point3d curOrigin = origin;

                // ---- 1. 图纸目录 ----
                if (wantIndex && sheetCaptions.Count > 0)
                {
                    double tableW = 190.0 * scale;
                    int rows = sheetCaptions.Count + 2;   // 表头 + 明细 + 合计
                    double tableH = rows * rowH;

                    SteelHlrDrawingService.DrawRectangle2D(tr, ms, curOrigin, 0, 0, tableW, tableH, SteelHlrDrawingService.LayerFrame);

                    double[] ratios = { 0.12, 0.52, 0.12, 0.12, 0.12 };
                    double[] colX = new double[ratios.Length + 1];
                    for (int c = 0; c < ratios.Length; c++)
                        colX[c + 1] = colX[c] + tableW * ratios[c];

                    for (int c = 1; c < ratios.Length; c++)
                        SteelHlrDrawingService.AppendLine2D(tr, ms, curOrigin, colX[c], 0, colX[c], tableH, SteelHlrDrawingService.LayerFrame);
                    for (int r = 1; r < rows; r++)
                        SteelHlrDrawingService.AppendLine2D(tr, ms, curOrigin, 0, r * rowH, tableW, r * rowH, SteelHlrDrawingService.LayerFrame);

                    string[] headers = { "图号", "图名", "图幅", "比例", "数量" };
                    double headerY = curOrigin.Y + tableH - rowH * 0.5;
                    for (int c = 0; c < headers.Length; c++)
                    {
                        double cx = curOrigin.X + (colX[c] + colX[c + 1]) * 0.5;
                        SteelHlrDrawingService.AppendCenteredSimSunText(
                            tr, ms, new Point3d(cx, headerY, 0), headers[c], textStyleId, textH, SteelHlrDrawingService.LayerFrame);
                    }

                    int totalQty = 0;
                    for (int i = 0; i < sheetCaptions.Count; i++)
                    {
                        BatchDrawingPlan.SheetGroup g = groups[i];
                        double sc = i < usedScales.Count ? usedScales[i] : scale;
                        double y = curOrigin.Y + (rows - 2 - i) * rowH + rowH * 0.5;
                        string[] vals =
                        {
                            $"详-{i + 1:00}",
                            CleanCaption(sheetCaptions[i]),
                            options.SheetSizeName,
                            $"1:{sc:0.#}",
                            g.Count.ToString(CultureInfo.InvariantCulture)
                        };
                        for (int c = 0; c < vals.Length; c++)
                        {
                            double cx = curOrigin.X + (colX[c] + colX[c + 1]) * 0.5;
                            SteelHlrDrawingService.AppendCenteredSimSunText(
                                tr, ms, new Point3d(cx, y, 0), vals[c], textStyleId, textH * 0.90, SteelHlrDrawingService.LayerFrame);
                        }
                        totalQty += g.Count;
                    }

                    double sumY = curOrigin.Y + rowH * 0.5;
                    SteelHlrDrawingService.AppendCenteredSimSunText(
                        tr, ms, new Point3d(curOrigin.X + (colX[0] + colX[1]) * 0.5, sumY, 0),
                        "合计", textStyleId, textH, SteelHlrDrawingService.LayerFrame);
                    SteelHlrDrawingService.AppendCenteredSimSunText(
                        tr, ms, new Point3d(curOrigin.X + (colX[1] + colX[2]) * 0.5, sumY, 0),
                        $"共 {sheetCaptions.Count} 张图", textStyleId, textH * 0.90, SteelHlrDrawingService.LayerFrame);
                    SteelHlrDrawingService.AppendCenteredSimSunText(
                        tr, ms, new Point3d(curOrigin.X + (colX[4] + colX[5]) * 0.5, sumY, 0),
                        totalQty.ToString(CultureInfo.InvariantCulture), textStyleId, textH * 0.90, SteelHlrDrawingService.LayerFrame);

                    SteelHlrDrawingService.AppendCenteredSimSunText(
                        tr, ms,
                        new Point3d(curOrigin.X + tableW * 0.5, curOrigin.Y + tableH + textH * 1.2, 0),
                        "图纸目录", textStyleId, textH * 1.25, SteelHlrDrawingService.LayerFrame);

                    curOrigin = new Point3d(curOrigin.X + tableW + tableGap, origin.Y, origin.Z);
                }

                // ---- 2. 汇总材料表 ----
                if (wantSummary && groups.Count > 0)
                {
                    var allItems = new List<MemberPartBillItem>();
                    foreach (BatchDrawingPlan.SheetGroup g in groups)
                    {
                        List<MemberPartBillItem> singleBill = SteelStructureBuilder.CalculatePartBillItems(g.Representative);
                        foreach (MemberPartBillItem item in singleBill)
                        {
                            item.Count *= g.Count;
                            item.TotalWeightKg = Math.Round(item.TotalWeightKg * g.Count, 2);
                            item.TotalSurfaceAreaM2 = Math.Round(item.TotalSurfaceAreaM2 * g.Count, 2);
                            allItems.Add(item);
                        }
                    }

                    List<SectionSummaryRow> summaryRows = MaterialTakeoffService.BuildSectionSummary(allItems);
                    if (summaryRows.Count > 0)
                    {
                        double sumTableW = 260.0 * scale;
                        int sumRows = summaryRows.Count + 2; // 表头 + 明细 + 合计
                        double sumTableH = sumRows * rowH;

                        SteelHlrDrawingService.DrawRectangle2D(tr, ms, curOrigin, 0, 0, sumTableW, sumTableH, SteelHlrDrawingService.LayerFrame);

                        // 列宽：序号 / 截面规格 / 类别 / 材质 / 数量 / 总长度(m) / 单重(kg/m) / 总重量(kg) / 表面积(m²)
                        double[] ratios = { 0.06, 0.22, 0.11, 0.09, 0.08, 0.11, 0.11, 0.12, 0.10 };
                        double[] colX = new double[ratios.Length + 1];
                        for (int c = 0; c < ratios.Length; c++)
                            colX[c + 1] = colX[c] + sumTableW * ratios[c];

                        for (int c = 1; c < ratios.Length; c++)
                            SteelHlrDrawingService.AppendLine2D(tr, ms, curOrigin, colX[c], 0, colX[c], sumTableH, SteelHlrDrawingService.LayerFrame);
                        for (int r = 1; r < sumRows; r++)
                            SteelHlrDrawingService.AppendLine2D(tr, ms, curOrigin, 0, r * rowH, sumTableW, r * rowH, SteelHlrDrawingService.LayerFrame);

                        string[] headers = { "序号", "截面规格", "类别", "材质", "数量", "总长(m)", "单重", "总重(kg)", "面积(m2)" };
                        double headerY = curOrigin.Y + sumTableH - rowH * 0.5;
                        for (int c = 0; c < headers.Length; c++)
                        {
                            double cx = curOrigin.X + (colX[c] + colX[c + 1]) * 0.5;
                            SteelHlrDrawingService.AppendCenteredSimSunText(
                                tr, ms, new Point3d(cx, headerY, 0), headers[c], textStyleId, textH * 0.90, SteelHlrDrawingService.LayerFrame);
                        }

                        int totalCount = 0;
                        double totalLenM = 0.0;
                        double totalWeight = 0.0;
                        double totalArea = 0.0;

                        for (int i = 0; i < summaryRows.Count; i++)
                        {
                            SectionSummaryRow r = summaryRows[i];
                            double y = curOrigin.Y + (sumRows - 2 - i) * rowH + rowH * 0.5;
                            string[] vals =
                            {
                                r.Index.ToString(CultureInfo.InvariantCulture),
                                r.SectionName ?? string.Empty,
                                r.ShapeTypeName ?? string.Empty,
                                r.Material ?? string.Empty,
                                r.TotalCount.ToString(CultureInfo.InvariantCulture),
                                r.TotalLengthM.ToString("0.00", CultureInfo.InvariantCulture),
                                r.UnitWeightKgPerM > 0 ? r.UnitWeightKgPerM.ToString("0.00", CultureInfo.InvariantCulture) : "-",
                                r.TotalWeightKg.ToString("0.00", CultureInfo.InvariantCulture),
                                r.TotalSurfaceAreaM2.ToString("0.00", CultureInfo.InvariantCulture)
                            };
                            for (int c = 0; c < vals.Length; c++)
                            {
                                double cx = curOrigin.X + (colX[c] + colX[c + 1]) * 0.5;
                                SteelHlrDrawingService.AppendCenteredSimSunText(
                                    tr, ms, new Point3d(cx, y, 0), vals[c], textStyleId, textH * 0.85, SteelHlrDrawingService.LayerFrame);
                            }
                            totalCount += r.TotalCount;
                            totalLenM += r.TotalLengthM;
                            totalWeight += r.TotalWeightKg;
                            totalArea += r.TotalSurfaceAreaM2;
                        }

                        double sumY = curOrigin.Y + rowH * 0.5;
                        SteelHlrDrawingService.AppendCenteredSimSunText(
                            tr, ms, new Point3d(curOrigin.X + (colX[0] + colX[2]) * 0.5, sumY, 0),
                            "合计", textStyleId, textH * 0.95, SteelHlrDrawingService.LayerFrame);
                        SteelHlrDrawingService.AppendCenteredSimSunText(
                            tr, ms, new Point3d(curOrigin.X + (colX[4] + colX[5]) * 0.5, sumY, 0),
                            totalCount.ToString(CultureInfo.InvariantCulture), textStyleId, textH * 0.85, SteelHlrDrawingService.LayerFrame);
                        SteelHlrDrawingService.AppendCenteredSimSunText(
                            tr, ms, new Point3d(curOrigin.X + (colX[5] + colX[6]) * 0.5, sumY, 0),
                            totalLenM.ToString("0.00", CultureInfo.InvariantCulture), textStyleId, textH * 0.85, SteelHlrDrawingService.LayerFrame);
                        SteelHlrDrawingService.AppendCenteredSimSunText(
                            tr, ms, new Point3d(curOrigin.X + (colX[7] + colX[8]) * 0.5, sumY, 0),
                            totalWeight.ToString("0.00", CultureInfo.InvariantCulture), textStyleId, textH * 0.85, SteelHlrDrawingService.LayerFrame);
                        SteelHlrDrawingService.AppendCenteredSimSunText(
                            tr, ms, new Point3d(curOrigin.X + (colX[8] + colX[9]) * 0.5, sumY, 0),
                            totalArea.ToString("0.00", CultureInfo.InvariantCulture), textStyleId, textH * 0.85, SteelHlrDrawingService.LayerFrame);

                        SteelHlrDrawingService.AppendCenteredSimSunText(
                            tr, ms,
                            new Point3d(curOrigin.X + sumTableW * 0.5, curOrigin.Y + sumTableH + textH * 1.2, 0),
                            "汇总材料表", textStyleId, textH * 1.25, SteelHlrDrawingService.LayerFrame);
                    }
                }

                tr.Commit();
            }
        }

        private static string CleanCaption(string caption)
        {
            if (string.IsNullOrEmpty(caption)) return string.Empty;
            string s = caption.Replace("图名:", string.Empty).Trim();
            return s;
        }

        private static void GetSheetSizeMm(string sheetName, out double w, out double h)
        {
            switch ((sheetName ?? "A2").ToUpperInvariant())
            {
                case "A1": w = 841.0; h = 594.0; break;
                case "A2": w = 594.0; h = 420.0; break;
                case "A3": w = 420.0; h = 297.0; break;
                case "A4": w = 297.0; h = 210.0; break;
                default: w = 594.0; h = 420.0; break;
            }
        }
    }
}
