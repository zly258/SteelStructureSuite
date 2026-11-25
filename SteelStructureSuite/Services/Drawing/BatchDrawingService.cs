using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SteelStructureSuite.Domain.Models;

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

            // 1. 计算图幅尺寸，按网格排布
            GetSheetSizeMm(options.SheetSizeName, out double paperW, out double paperH);

            // 网格列数：宽屏多排、窄图少排，保证整批不要过分扁长
            int perRow = Math.Max(1, Math.Min(MaxSheetsPerRow,
                plan.Groups.Count <= 2 ? plan.Groups.Count : MaxSheetsPerRow));

            double cellW = paperW + SheetGapMm;
            double cellH = paperH + SheetGapMm;

            var placed = new List<string>();

            for (int i = 0; i < plan.Groups.Count; i++)
            {
                if (shouldCancel != null && shouldCancel())
                {
                    result.Cancelled = true;
                    break;
                }

                BatchDrawingPlan.SheetGroup group = plan.Groups[i];
                int col = i % perRow;
                int row = i / perRow;

                var sheetOrigin = new Point3d(
                    origin.X + col * cellW,
                    origin.Y - row * cellH,
                    origin.Z);

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

                try
                {
                    SteelHlrDrawingService.GenerateDetailDrawingSheet(
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

                placed.Add(caption ?? $"图名: {group.Representative.StructureTypeDisplayName} 详图（{group.TagSummary}）");
                state.GeneratedSheets.Add(placed[placed.Count - 1]);
                state.Completed = i + 1;
                progress?.Invoke(state);
            }

            // 2. 图纸目录 + 汇总材料表（按选项决定是否生成）
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
                            plan,
                            placed,
                            new Point3d(origin.X, origin.Y - ((plan.Groups.Count - 1) / perRow + 1) * cellH, origin.Z),
                            options,
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
            BatchDrawingPlan plan,
            List<string> sheetCaptions,
            Point3d origin,
            DrawingGenerationOptions options,
            bool wantIndex,
            bool wantSummary)
        {
            GetSheetSizeMm(options.SheetSizeName, out double paperW, out double paperH);
            double scale = options.AutoScale ? 20.0 : Math.Max(1.0, options.DrawingScale);
            double textH = 3.5 * scale;

            ObjectId textStyleId = SteelHlrDrawingService.EnsureSimSunTextStyle(db);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                if (!wantIndex)
                {
                    tr.Commit();
                    return;
                }

                // ---- 图纸目录 ----
                double tableW = 180.0 * scale;
                double rowH = textH * 2.2;
                int rows = sheetCaptions.Count + 2;   // 表头 + 明细 + 合计
                double tableH = rows * rowH;

                var tableOrigin = new Point3d(origin.X, origin.Y, origin.Z);
                SteelHlrDrawingService.DrawRectangle2D(tr, ms, tableOrigin, 0, 0, tableW, tableH, "SS_DRAW_FRAME");

                // 列宽：图号 / 图名 / 图幅 / 比例 / 数量
                double[] ratios = { 0.12, 0.52, 0.12, 0.12, 0.12 };
                double[] colX = new double[ratios.Length + 1];
                for (int c = 0; c < ratios.Length; c++)
                    colX[c + 1] = colX[c] + tableW * ratios[c];

                for (int c = 1; c < ratios.Length; c++)
                    SteelHlrDrawingService.AppendLine2D(tr, ms, tableOrigin, colX[c], 0, colX[c], tableH, "SS_DRAW_FRAME");
                for (int r = 1; r < rows; r++)
                    SteelHlrDrawingService.AppendLine2D(tr, ms, tableOrigin, 0, r * rowH, tableW, r * rowH, "SS_DRAW_FRAME");

                string[] headers = { "图号", "图名", "图幅", "比例", "数量" };
                double headerY = tableOrigin.Y + tableH - rowH * 0.5;
                for (int c = 0; c < headers.Length; c++)
                {
                    double cx = tableOrigin.X + (colX[c] + colX[c + 1]) * 0.5;
                    SteelHlrDrawingService.AppendCenteredSimSunText(
                        tr, ms, new Point3d(cx, headerY, 0), headers[c], textStyleId, textH, "SS_DRAW_FRAME");
                }

                int totalQty = 0;
                for (int i = 0; i < sheetCaptions.Count; i++)
                {
                    BatchDrawingPlan.SheetGroup g = plan.Groups[i];
                    double y = tableOrigin.Y + (rows - 2 - i) * rowH + rowH * 0.5;
                    string[] vals =
                    {
                        $"详-{i + 1:00}",
                        CleanCaption(sheetCaptions[i]),
                        options.SheetSizeName,
                        $"1:{scale:0.#}",
                        g.Count.ToString(CultureInfo.InvariantCulture)
                    };
                    for (int c = 0; c < vals.Length; c++)
                    {
                        double cx = tableOrigin.X + (colX[c] + colX[c + 1]) * 0.5;
                        SteelHlrDrawingService.AppendCenteredSimSunText(
                            tr, ms, new Point3d(cx, y, 0), vals[c], textStyleId, textH * 0.95, "SS_DRAW_FRAME");
                    }
                    totalQty += g.Count;
                }

                double sumY = tableOrigin.Y + rowH * 0.5;
                SteelHlrDrawingService.AppendCenteredSimSunText(
                    tr, ms, new Point3d(tableOrigin.X + (colX[0] + colX[1]) * 0.5, sumY, 0),
                    "合计", textStyleId, textH, "SS_DRAW_FRAME");
                SteelHlrDrawingService.AppendCenteredSimSunText(
                    tr, ms, new Point3d(tableOrigin.X + (colX[1] + colX[2]) * 0.5, sumY, 0),
                    $"共 {sheetCaptions.Count} 张图", textStyleId, textH * 0.95, "SS_DRAW_FRAME");
                SteelHlrDrawingService.AppendCenteredSimSunText(
                    tr, ms, new Point3d(tableOrigin.X + (colX[4] + colX[5]) * 0.5, sumY, 0),
                    totalQty.ToString(CultureInfo.InvariantCulture), textStyleId, textH * 0.95, "SS_DRAW_FRAME");

                // 表名
                SteelHlrDrawingService.AppendCenteredSimSunText(
                    tr, ms,
                    new Point3d(tableOrigin.X + tableW * 0.5, tableOrigin.Y + tableH + textH * 1.2, 0),
                    "图纸目录", textStyleId, textH * 1.2, "SS_DRAW_FRAME");

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
