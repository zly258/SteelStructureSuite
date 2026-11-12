using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Drawing;
using SteelStructureSuite.Services.Modeling;

namespace SteelStructureSuite.Services.Takeoff
{
    public static class MaterialTakeoffService
    {
        public static List<SteelStructureParams> CollectStructures(Database db, IEnumerable<ObjectId> filterIds = null)
        {
            var result = new List<SteelStructureParams>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (filterIds != null)
                {
                    foreach (ObjectId id in filterIds)
                    {
                        if (!id.IsValid || id.IsErased) continue;
                        if (tr.GetObject(id, OpenMode.ForRead) is Entity ent &&
                            SteelDataStorage.TryReadParams(ent, out SteelStructureParams data))
                        {
                            result.Add(data);
                        }
                    }
                }
                else
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId id in ms)
                    {
                        if (!id.IsValid || id.IsErased) continue;
                        if (tr.GetObject(id, OpenMode.ForRead) is Entity ent &&
                            SteelDataStorage.TryReadParams(ent, out SteelStructureParams data))
                        {
                            result.Add(data);
                        }
                    }
                }

                tr.Commit();
            }

            return result.OrderBy(s => s.Tag).ToList();
        }

        public static List<MemberPartBillItem> BuildDetailedPartList(IEnumerable<SteelStructureParams> structures)
        {
            var allParts = new List<MemberPartBillItem>();
            foreach (var s in structures)
            {
                allParts.AddRange(SteelStructureBuilder.CalculatePartBillItems(s));
            }

            // 按位号、件号排序，保证表格与图纸引线编号顺序一致
            return allParts
                .OrderBy(x => x.ParentTag ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => RoleOrder(x.PartRole))
                .ThenBy(x => x.PartNo)
                .ThenBy(x => SectionSortKey(x.SectionName), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<SectionSummaryRow> BuildSectionSummary(IEnumerable<MemberPartBillItem> partItems)
        {
            var rows = partItems
                .GroupBy(i => new { i.SectionName, i.ShapeTypeName, i.Material, i.UnitWeightKgPerM })
                .Select(g => new SectionSummaryRow
                {
                    PartNoRange = string.Join(",", g.Select(x => x.PartNo.ToString(CultureInfo.InvariantCulture)).Distinct()),
                    SectionName = g.Key.SectionName,
                    ShapeTypeName = g.Key.ShapeTypeName,
                    Material = g.Key.Material,
                    TotalCount = g.Sum(x => x.Count),
                    TotalLengthM = Math.Round(g.Sum(x => x.Count * x.SingleLengthMm) * 0.001, 2),
                    UnitWeightKgPerM = g.Key.UnitWeightKgPerM,
                    TotalWeightKg = Math.Round(g.Sum(x => x.TotalWeightKg), 2),
                    TotalSurfaceAreaM2 = Math.Round(g.Sum(x => x.TotalSurfaceAreaM2), 2)
                })
                // 按型钢类别固定顺序（H型钢→槽钢→角钢→方矩管→圆钢管→钢板），再按规格排序
                .OrderBy(r => ShapeOrder(r.ShapeTypeName))
                .ThenBy(r => SectionSortKey(r.SectionName), StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Material, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int i = 0; i < rows.Count; i++) rows[i].Index = i + 1;
            return rows;
        }

        private static int ShapeOrder(string shapeTypeName)
        {
            switch (shapeTypeName)
            {
                case "H型钢": return 1;
                case "槽钢": return 2;
                case "等边角钢": return 3;
                case "方矩管": return 4;
                case "圆钢管": return 5;
                case "钢板": return 6;
                default: return 9;
            }
        }

        private static int RoleOrder(string role)
        {
            if (string.IsNullOrEmpty(role)) return 9;
            if (role.IndexOf("板", StringComparison.Ordinal) >= 0) return 5;
            if (role.IndexOf("扶手", StringComparison.Ordinal) >= 0 ||
                role.IndexOf("横杆", StringComparison.Ordinal) >= 0 ||
                role.IndexOf("护笼", StringComparison.Ordinal) >= 0 ||
                role.IndexOf("栏", StringComparison.Ordinal) >= 0) return 4;
            if (role.IndexOf("撑", StringComparison.Ordinal) >= 0 ||
                role.IndexOf("腹杆", StringComparison.Ordinal) >= 0) return 3;
            if (role.IndexOf("弦", StringComparison.Ordinal) >= 0 ||
                role.IndexOf("梁", StringComparison.Ordinal) >= 0) return 2;
            if (role.IndexOf("柱", StringComparison.Ordinal) >= 0 ||
                role.IndexOf("立管", StringComparison.Ordinal) >= 0) return 1;
            return 6;
        }

        private static string SectionSortKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            return System.Text.RegularExpressions.Regex.Replace(
                name,
                @"\d+",
                m => m.Value.PadLeft(6, '0'));
        }

        public static ObjectId CreateCadDetailedTableInModelSpace(
            Database db,
            Point3d insertPt,
            List<MemberPartBillItem> detailedItems,
            double scale = 20.0)
        {
            ObjectId simSunStyleId = SteelHlrDrawingService.EnsureSimSunTextStyle(db);
            double textH = 3.5 * (scale > 0.01 ? scale : 20.0);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                int colCount = 9;
                int rowCount = detailedItems.Count + 3;

                Table tb = new Table();
                tb.SetDatabaseDefaults(db);
                tb.TableStyle = db.Tablestyle;
                tb.Position = insertPt;
                tb.SetSize(rowCount, colCount);
                tb.SetRowHeight(textH * 2.1);

                double[] ratios = { 0.10, 0.07, 0.20, 0.20, 0.08, 0.07, 0.10, 0.10, 0.08 };
                double totalW = textH * 78.0;
                for (int c = 0; c < colCount; c++)
                    tb.Columns[c].Width = totalW * ratios[c];

                tb.Cells[0, 0].TextString = "钢结构构件零件明细表 (按位号 / 件号)";
                tb.Cells[0, 0].TextStyleId = simSunStyleId;
                tb.Cells[0, 0].TextHeight = textH * 1.1;
                tb.Cells[0, 0].Alignment = CellAlignment.MiddleCenter;

                string[] headers = { "位号", "件号", "部位", "截面规格", "材质", "件数", "单长(mm)", "总重(kg)", "面积(m²)" };
                for (int c = 0; c < colCount; c++)
                {
                    tb.Cells[1, c].TextString = headers[c];
                    tb.Cells[1, c].TextStyleId = simSunStyleId;
                    tb.Cells[1, c].TextHeight = textH;
                    tb.Cells[1, c].Alignment = CellAlignment.MiddleCenter;
                }

                double totalWeight = 0;
                double totalArea = 0;
                for (int i = 0; i < detailedItems.Count; i++)
                {
                    var item = detailedItems[i];
                    int r = i + 2;
                    string[] vals =
                    {
                        item.ParentTag ?? string.Empty,
                        item.PartNo.ToString(CultureInfo.InvariantCulture),
                        item.PartRole ?? string.Empty,
                        item.SectionName ?? string.Empty,
                        item.Material ?? string.Empty,
                        item.Count.ToString(),
                        item.SingleLengthMm.ToString("0", CultureInfo.InvariantCulture),
                        item.TotalWeightKg.ToString("0.00", CultureInfo.InvariantCulture),
                        item.TotalSurfaceAreaM2.ToString("0.00", CultureInfo.InvariantCulture)
                    };
                    for (int c = 0; c < colCount; c++)
                    {
                        tb.Cells[r, c].TextString = vals[c];
                        tb.Cells[r, c].TextStyleId = simSunStyleId;
                        tb.Cells[r, c].TextHeight = textH;
                        tb.Cells[r, c].Alignment = CellAlignment.MiddleCenter;
                    }
                    totalWeight += item.TotalWeightKg;
                    totalArea += item.TotalSurfaceAreaM2;
                }

                int lastRow = rowCount - 1;
                tb.Cells[lastRow, 0].TextString = "合计";
                tb.Cells[lastRow, 3].TextString = $"共 {detailedItems.Count} 条零件";
                tb.Cells[lastRow, 7].TextString = totalWeight.ToString("0.00", CultureInfo.InvariantCulture);
                tb.Cells[lastRow, 8].TextString = totalArea.ToString("0.00", CultureInfo.InvariantCulture);
                for (int c = 0; c < colCount; c++)
                {
                    tb.Cells[lastRow, c].TextStyleId = simSunStyleId;
                    tb.Cells[lastRow, c].TextHeight = textH;
                    tb.Cells[lastRow, c].Alignment = CellAlignment.MiddleCenter;
                }

                tb.GenerateLayout();
                ms.AppendEntity(tb);
                tr.AddNewlyCreatedDBObject(tb, true);
                tr.Commit();

                return tb.ObjectId;
            }
        }

        public static void ExportToExcelXlsx(
            string filePath,
            List<MemberPartBillItem> detailedItems,
            List<SectionSummaryRow> summaryRows)
        {
            ExcelOpenXmlExporter.ExportToXlsx(filePath, detailedItems, summaryRows);
        }

        public static ObjectId CreateCadBomTableInModelSpace(
            Database db,
            Point3d insertPt,
            List<SectionSummaryRow> summaryRows,
            double scale = 20.0)
        {
            ObjectId simSunStyleId = SteelHlrDrawingService.EnsureSimSunTextStyle(db);
            double textH = 3.5 * (scale > 0.01 ? scale : 20.0);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                int rowCount = summaryRows.Count + 3;
                int colCount = 9;

                Table tb = new Table();
                tb.SetDatabaseDefaults(db);
                tb.TableStyle = db.Tablestyle;
                tb.Position = insertPt;
                tb.SetSize(rowCount, colCount);
                tb.SetRowHeight(textH * 2.4);

                double[] colWidths =
                {
                    textH * 4.5,
                    textH * 7.5,
                    textH * 11.5,
                    textH * 6.5,
                    textH * 6.0,
                    textH * 5.0,
                    textH * 7.5,
                    textH * 7.5,
                    textH * 8.0
                };
                for (int c = 0; c < colCount; c++)
                {
                    tb.Columns[c].Width = colWidths[c];
                }

                tb.Cells[0, 0].TextString = "钢 结 构 材 料 统 计 汇 总 表 (MTO / BOM)";
                tb.Cells[0, 0].TextStyleId = simSunStyleId;
                tb.Cells[0, 0].TextHeight = textH * 1.1;
                tb.Cells[0, 0].Alignment = CellAlignment.MiddleCenter;

                string[] headers = { "序号", "对应件号", "截面规格", "类别", "材质", "件数", "总长(m)", "总重(kg)", "防腐面积(m²)" };
                for (int c = 0; c < colCount; c++)
                {
                    tb.Cells[1, c].TextString = headers[c];
                    tb.Cells[1, c].TextStyleId = simSunStyleId;
                    tb.Cells[1, c].TextHeight = textH;
                    tb.Cells[1, c].Alignment = CellAlignment.MiddleCenter;
                }

                double totalWeight = 0;
                double totalArea = 0;

                for (int i = 0; i < summaryRows.Count; i++)
                {
                    var r = summaryRows[i];
                    int rowIdx = i + 2;
                    string[] vals =
                    {
                        r.Index.ToString(),
                        r.PartNoRange ?? r.Index.ToString(CultureInfo.InvariantCulture),
                        r.SectionName,
                        r.ShapeTypeName,
                        r.Material,
                        r.TotalCount.ToString(),
                        r.TotalLengthM.ToString("0.00", CultureInfo.InvariantCulture),
                        r.TotalWeightKg.ToString("0.00", CultureInfo.InvariantCulture),
                        r.TotalSurfaceAreaM2.ToString("0.00", CultureInfo.InvariantCulture)
                    };

                    for (int c = 0; c < colCount; c++)
                    {
                        tb.Cells[rowIdx, c].TextString = vals[c];
                        tb.Cells[rowIdx, c].TextStyleId = simSunStyleId;
                        tb.Cells[rowIdx, c].TextHeight = textH;
                        tb.Cells[rowIdx, c].Alignment = CellAlignment.MiddleCenter;
                    }

                    totalWeight += r.TotalWeightKg;
                    totalArea += r.TotalSurfaceAreaM2;
                }

                int lastRow = rowCount - 1;
                tb.Cells[lastRow, 0].TextString = "合计";
                tb.Cells[lastRow, 2].TextString = $"共 {summaryRows.Count} 种规格";
                tb.Cells[lastRow, 7].TextString = totalWeight.ToString("0.00", CultureInfo.InvariantCulture);
                tb.Cells[lastRow, 8].TextString = totalArea.ToString("0.00", CultureInfo.InvariantCulture);
                for (int c = 0; c < colCount; c++)
                {
                    tb.Cells[lastRow, c].TextStyleId = simSunStyleId;
                    tb.Cells[lastRow, c].TextHeight = textH;
                    tb.Cells[lastRow, c].Alignment = CellAlignment.MiddleCenter;
                }

                tb.GenerateLayout();
                ms.AppendEntity(tb);
                tr.AddNewlyCreatedDBObject(tb, true);
                tr.Commit();

                return tb.ObjectId;
            }
        }
    }
}
