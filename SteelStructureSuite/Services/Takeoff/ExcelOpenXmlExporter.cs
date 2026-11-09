using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Services.Takeoff
{
    /// <summary>
    /// 纯 C# 原生 Microsoft Excel (.xlsx) 导出器（基于 OpenXML 标准与 System.IO.Compression，零外部第三方依赖）
    /// 生成包含【1_型钢材料汇总表】与【2_构件分项下料明细表】双工作表的带样式 .xlsx 工作簿
    /// </summary>
    public static class ExcelOpenXmlExporter
    {
        public static void ExportToXlsx(
            string filePath,
            List<MemberPartBillItem> detailedItems,
            List<SectionSummaryRow> summaryRows)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            using (FileStream fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.ReadWrite))
            using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                AddZipEntry(archive, "[Content_Types].xml", BuildContentTypesXml());
                AddZipEntry(archive, "_rels/.rels", BuildRootRelsXml());
                AddZipEntry(archive, "xl/workbook.xml", BuildWorkbookXml());
                AddZipEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelsXml());
                AddZipEntry(archive, "xl/styles.xml", BuildStylesXml());
                AddZipEntry(archive, "xl/worksheets/sheet1.xml", BuildSummarySheetXml(summaryRows));
                AddZipEntry(archive, "xl/worksheets/sheet2.xml", BuildDetailedSheetXml(detailedItems));
            }
        }

        private static void AddZipEntry(ZipArchive archive, string entryPath, string xmlContent)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
            using (Stream s = entry.Open())
            using (StreamWriter sw = new StreamWriter(s, new UTF8Encoding(false)))
            {
                sw.Write(xmlContent);
            }
        }

        private static string BuildContentTypesXml()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/worksheets/sheet2.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
</Types>";
        }

        private static string BuildRootRelsXml()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>";
        }

        private static string BuildWorkbookXml()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheets>
    <sheet name=""1_型钢材料汇总表"" sheetId=""1"" r:id=""rId1""/>
    <sheet name=""2_构件分项下料明细表"" sheetId=""2"" r:id=""rId2""/>
  </sheets>
</workbook>";
        }

        private static string BuildWorkbookRelsXml()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
  <Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet2.xml""/>
  <Relationship Id=""rId3"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/>
</Relationships>";
        }

        private static string BuildStylesXml()
        {
            // Style 0: Default
            // Style 1: Title Banner (14pt Bold, #0F172A)
            // Style 2: Table Header (11pt Bold White on #0F6CBD Fluent Blue, thin border)
            // Style 3: Data Text Cell (10pt, thin border, center)
            // Style 4: Data Number Cell (10pt, 0.00 format, thin border, right)
            // Style 5: Total Summary Row (11pt Bold on #EFF6FF soft blue, thin border)
            return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <numFmts count=""1"">
    <numFmt numFmtId=""164"" formatCode=""#,##0.00""/>
  </numFmts>
  <fonts count=""4"">
    <font><sz val=""10""/><name val=""Microsoft YaHei UI""/></font>
    <font><b/><sz val=""13""/><color rgb=""FF0F172A""/><name val=""Microsoft YaHei UI""/></font>
    <font><b/><sz val=""10""/><color rgb=""FFFFFFFF""/><name val=""Microsoft YaHei UI""/></font>
    <font><b/><sz val=""10""/><color rgb=""FF0F6CBD""/><name val=""Microsoft YaHei UI""/></font>
  </fonts>
  <fills count=""4"">
    <fill><patternFill patternType=""none""/></fill>
    <fill><patternFill patternType=""gray125""/></fill>
    <fill><patternFill patternType=""solid""><fgColor rgb=""FF0F6CBD""/></patternFill></fill>
    <fill><patternFill patternType=""solid""><fgColor rgb=""FFEFF6FF""/></patternFill></fill>
  </fills>
  <borders count=""2"">
    <border><left/><right/><top/><bottom/><diagonal/></border>
    <border>
      <left style=""thin""><color rgb=""FFCBD5E1""/></left>
      <right style=""thin""><color rgb=""FFCBD5E1""/></right>
      <top style=""thin""><color rgb=""FFCBD5E1""/></top>
      <bottom style=""thin""><color rgb=""FFCBD5E1""/></bottom>
      <diagonal/>
    </border>
  </borders>
  <cellStyleXfs count=""1"">
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/>
  </cellStyleXfs>
  <cellXfs count=""6"">
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/>
    <xf numFmtId=""0"" fontId=""1"" fillId=""0"" borderId=""0"" xfId=""0"" applyFont=""1""><alignment vertical=""center""/></xf>
    <xf numFmtId=""0"" fontId=""2"" fillId=""2"" borderId=""1"" xfId=""0"" applyFont=""1"" applyFill=""1"" applyBorder=""1""><alignment horizontal=""center"" vertical=""center""/></xf>
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""1"" xfId=""0"" applyBorder=""1""><alignment horizontal=""center"" vertical=""center""/></xf>
    <xf numFmtId=""164"" fontId=""0"" fillId=""0"" borderId=""1"" xfId=""0"" applyNumberFormat=""1"" applyBorder=""1""><alignment horizontal=""right"" vertical=""center""/></xf>
    <xf numFmtId=""164"" fontId=""3"" fillId=""3"" borderId=""1"" xfId=""0"" applyNumberFormat=""1"" applyFont=""1"" applyFill=""1"" applyBorder=""1""><alignment horizontal=""right"" vertical=""center""/></xf>
  </cellXfs>
</styleSheet>";
        }

        private static string BuildSummarySheetXml(List<SectionSummaryRow> rows)
        {
            var sb = new StringBuilder();
            sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
            sb.Append(@"<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">");
            sb.Append(@"<cols>");
            sb.Append(@"<col min=""1"" max=""1"" width=""10"" customWidth=""1""/>");
            sb.Append(@"<col min=""2"" max=""2"" width=""24"" customWidth=""1""/>");
            sb.Append(@"<col min=""3"" max=""4"" width=""14"" customWidth=""1""/>");
            sb.Append(@"<col min=""5"" max=""5"" width=""12"" customWidth=""1""/>");
            sb.Append(@"<col min=""6"" max=""9"" width=""18"" customWidth=""1""/>");
            sb.Append(@"</cols>");
            sb.Append(@"<sheetData>");

            // Row 1: Title
            sb.Append(@"<row r=""1"" ht=""28"" customHeight=""1"">");
            sb.Append(MakeStringCell("A1", "钢结构型钢材料分类汇总表 (Material Takeoff Summary)", 1));
            sb.Append(@"</row>");

            // Row 2: Headers
            string[] headers = { "序号", "截面规格", "型钢类别", "钢材材质", "总件数", "合计总长 (m)", "理论单重 (kg/m)", "合计总重 (kg)", "防腐面积 (m²)" };
            sb.Append(@"<row r=""2"" ht=""24"" customHeight=""1"">");
            for (int c = 0; c < headers.Length; c++)
            {
                sb.Append(MakeStringCell(ColLetter(c) + "2", headers[c], 2));
            }
            sb.Append(@"</row>");

            double grandWeight = 0;
            double grandArea = 0;
            int rowIdx = 3;

            foreach (var r in rows)
            {
                sb.AppendFormat(@"<row r=""{0}"" ht=""20"" customHeight=""1"">", rowIdx);
                sb.Append(MakeNumberCell("A" + rowIdx, r.Index, 3));
                sb.Append(MakeStringCell("B" + rowIdx, r.SectionName, 3));
                sb.Append(MakeStringCell("C" + rowIdx, r.ShapeTypeName, 3));
                sb.Append(MakeStringCell("D" + rowIdx, r.Material, 3));
                sb.Append(MakeNumberCell("E" + rowIdx, r.TotalCount, 3));
                sb.Append(MakeNumberCell("F" + rowIdx, r.TotalLengthM, 4));
                sb.Append(MakeNumberCell("G" + rowIdx, r.UnitWeightKgPerM, 4));
                sb.Append(MakeNumberCell("H" + rowIdx, r.TotalWeightKg, 4));
                sb.Append(MakeNumberCell("I" + rowIdx, r.TotalSurfaceAreaM2, 4));
                sb.Append(@"</row>");

                grandWeight += r.TotalWeightKg;
                grandArea += r.TotalSurfaceAreaM2;
                rowIdx++;
            }

            // Total Row
            sb.AppendFormat(@"<row r=""{0}"" ht=""22"" customHeight=""1"">", rowIdx);
            sb.Append(MakeStringCell("A" + rowIdx, "合计", 5));
            sb.Append(MakeStringCell("B" + rowIdx, $"共 {rows.Count} 种规格", 5));
            sb.Append(MakeStringCell("C" + rowIdx, "-", 5));
            sb.Append(MakeStringCell("D" + rowIdx, "-", 5));
            sb.Append(MakeStringCell("E" + rowIdx, "-", 5));
            sb.Append(MakeStringCell("F" + rowIdx, "-", 5));
            sb.Append(MakeStringCell("G" + rowIdx, "-", 5));
            sb.Append(MakeNumberCell("H" + rowIdx, grandWeight, 5));
            sb.Append(MakeNumberCell("I" + rowIdx, grandArea, 5));
            sb.Append(@"</row>");

            sb.Append(@"</sheetData></worksheet>");
            return sb.ToString();
        }

        private static string BuildDetailedSheetXml(List<MemberPartBillItem> items)
        {
            var sb = new StringBuilder();
            sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
            sb.Append(@"<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">");
            sb.Append(@"<cols>");
            sb.Append(@"<col min=""1"" max=""1"" width=""14"" customWidth=""1""/>");
            sb.Append(@"<col min=""2"" max=""2"" width=""10"" customWidth=""1""/>");
            sb.Append(@"<col min=""3"" max=""3"" width=""18"" customWidth=""1""/>");
            sb.Append(@"<col min=""4"" max=""4"" width=""24"" customWidth=""1""/>");
            sb.Append(@"<col min=""5"" max=""6"" width=""14"" customWidth=""1""/>");
            sb.Append(@"<col min=""7"" max=""11"" width=""16"" customWidth=""1""/>");
            sb.Append(@"</cols>");
            sb.Append(@"<sheetData>");

            sb.Append(@"<row r=""1"" ht=""28"" customHeight=""1"">");
            sb.Append(MakeStringCell("A1", "钢结构构件分项下料明细表 (Detailed Part Cutting List)", 1));
            sb.Append(@"</row>");

            string[] headers = { "构件位号", "件号", "零件部位", "截面规格", "型钢类别", "钢材材质", "件数", "单件长 (mm)", "单重 (kg/m)", "小计重 (kg)", "防腐面积 (m²)" };
            sb.Append(@"<row r=""2"" ht=""24"" customHeight=""1"">");
            for (int c = 0; c < headers.Length; c++)
            {
                sb.Append(MakeStringCell(ColLetter(c) + "2", headers[c], 2));
            }
            sb.Append(@"</row>");

            int rowIdx = 3;
            foreach (var item in items)
            {
                sb.AppendFormat(@"<row r=""{0}"" ht=""20"" customHeight=""1"">", rowIdx);
                sb.Append(MakeStringCell("A" + rowIdx, item.ParentTag, 3));
                sb.Append(MakeStringCell("B" + rowIdx, "#" + item.PartNo, 3));
                sb.Append(MakeStringCell("C" + rowIdx, item.PartRole, 3));
                sb.Append(MakeStringCell("D" + rowIdx, item.SectionName, 3));
                sb.Append(MakeStringCell("E" + rowIdx, item.ShapeTypeName, 3));
                sb.Append(MakeStringCell("F" + rowIdx, item.Material, 3));
                sb.Append(MakeNumberCell("G" + rowIdx, item.Count, 3));
                sb.Append(MakeNumberCell("H" + rowIdx, item.SingleLengthMm, 4));
                sb.Append(MakeNumberCell("I" + rowIdx, item.UnitWeightKgPerM, 4));
                sb.Append(MakeNumberCell("J" + rowIdx, item.TotalWeightKg, 4));
                sb.Append(MakeNumberCell("K" + rowIdx, item.TotalSurfaceAreaM2, 4));
                sb.Append(@"</row>");
                rowIdx++;
            }

            sb.Append(@"</sheetData></worksheet>");
            return sb.ToString();
        }

        private static string MakeStringCell(string cellRef, string text, int styleIndex)
        {
            string safe = SecurityElement.Escape(text ?? string.Empty);
            return $@"<c r=""{cellRef}"" s=""{styleIndex}"" t=""inlineStr""><is><t>{safe}</t></is></c>";
        }

        private static string MakeNumberCell(string cellRef, double val, int styleIndex)
        {
            string numStr = val.ToString("0.##", CultureInfo.InvariantCulture);
            return $@"<c r=""{cellRef}"" s=""{styleIndex}""><v>{numStr}</v></c>";
        }

        private static string ColLetter(int zeroBasedCol)
        {
            return ((char)('A' + zeroBasedCol)).ToString();
        }
    }
}
