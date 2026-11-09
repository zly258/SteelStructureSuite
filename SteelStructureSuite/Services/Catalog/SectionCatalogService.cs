using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Services.Catalog
{
    /// <summary>
    /// 型钢截面分类库管理器
    /// 按 5 大截面类型分表独立管理（H型钢表、槽钢表、角钢表、方矩管表、圆钢管表），
    /// 支持多工作表 Excel (.xlsx) 与 CSV 批量导出与导入
    /// </summary>
    public static class SectionCatalogService
    {
        private static Dictionary<SectionShapeType, List<SectionProfile>> _tables = CreateDefaultTables();

        private static Dictionary<SectionShapeType, List<SectionProfile>> CreateDefaultTables()
        {
            return new Dictionary<SectionShapeType, List<SectionProfile>>
            {
                [SectionShapeType.HBeam] = new List<SectionProfile>
                {
                    new SectionProfile("HW150x150x7x10", SectionShapeType.HBeam, 150, 150, 7, 10),
                    new SectionProfile("HW200x200x8x12", SectionShapeType.HBeam, 200, 200, 8, 12),
                    new SectionProfile("HW250x250x9x14", SectionShapeType.HBeam, 250, 250, 9, 14),
                    new SectionProfile("HW300x300x10x15", SectionShapeType.HBeam, 300, 300, 10, 15),
                    new SectionProfile("HM194x150x6x9", SectionShapeType.HBeam, 194, 150, 6, 9),
                    new SectionProfile("HM244x175x7x11", SectionShapeType.HBeam, 244, 175, 7, 11),
                    new SectionProfile("HN200x100x5.5x8", SectionShapeType.HBeam, 200, 100, 5.5, 8),
                    new SectionProfile("HN250x125x6x9", SectionShapeType.HBeam, 250, 125, 6, 9),
                    new SectionProfile("HN300x150x6.5x9", SectionShapeType.HBeam, 300, 150, 6.5, 9)
                },
                [SectionShapeType.Channel] = new List<SectionProfile>
                {
                    new SectionProfile("C10", SectionShapeType.Channel, 100, 48, 5.3, 8.5),
                    new SectionProfile("C12.6", SectionShapeType.Channel, 126, 53, 5.5, 9.0),
                    new SectionProfile("C14a", SectionShapeType.Channel, 140, 58, 6.0, 9.5),
                    new SectionProfile("C16a", SectionShapeType.Channel, 160, 63, 6.5, 10.0),
                    new SectionProfile("C20a", SectionShapeType.Channel, 200, 73, 7.0, 11.0),
                    new SectionProfile("C25a", SectionShapeType.Channel, 250, 78, 8.0, 12.0)
                },
                [SectionShapeType.Angle] = new List<SectionProfile>
                {
                    new SectionProfile("L50x50x5", SectionShapeType.Angle, 50, 50, 5, 5),
                    new SectionProfile("L63x63x6", SectionShapeType.Angle, 63, 63, 6, 6),
                    new SectionProfile("L75x75x6", SectionShapeType.Angle, 75, 75, 6, 6),
                    new SectionProfile("L100x100x8", SectionShapeType.Angle, 100, 100, 8, 8),
                    new SectionProfile("L125x125x10", SectionShapeType.Angle, 125, 125, 10, 10)
                },
                [SectionShapeType.RectTube] = new List<SectionProfile>
                {
                    new SectionProfile("F100x100x6", SectionShapeType.RectTube, 100, 100, 6, 6),
                    new SectionProfile("F120x120x6", SectionShapeType.RectTube, 120, 120, 6, 6),
                    new SectionProfile("F150x150x8", SectionShapeType.RectTube, 150, 150, 8, 8),
                    new SectionProfile("F200x100x8", SectionShapeType.RectTube, 200, 100, 8, 8),
                    new SectionProfile("F200x200x10", SectionShapeType.RectTube, 200, 200, 10, 10)
                },
                [SectionShapeType.PipeTube] = new List<SectionProfile>
                {
                    new SectionProfile("P32x3.2", SectionShapeType.PipeTube, 32, 32, 3.2, 3.2),
                    new SectionProfile("P42x3.5", SectionShapeType.PipeTube, 42, 42, 3.5, 3.5),
                    new SectionProfile("P48x3.5", SectionShapeType.PipeTube, 48, 48, 3.5, 3.5),
                    new SectionProfile("P89x5", SectionShapeType.PipeTube, 89, 89, 5, 5),
                    new SectionProfile("P114x6", SectionShapeType.PipeTube, 114, 114, 6, 6),
                    new SectionProfile("P159x6", SectionShapeType.PipeTube, 159, 159, 6, 6),
                    new SectionProfile("P219x8", SectionShapeType.PipeTube, 219, 219, 8, 8)
                }
            };
        }

        public static string GetShapeTabTitle(SectionShapeType shapeType)
        {
            switch (shapeType)
            {
                case SectionShapeType.HBeam: return "1. H型钢表 (HW/HM/HN)";
                case SectionShapeType.Channel: return "2. 槽钢表 (C)";
                case SectionShapeType.Angle: return "3. 等边角钢表 (L)";
                case SectionShapeType.RectTube: return "4. 方矩管表 (RHS/F)";
                case SectionShapeType.PipeTube: return "5. 圆钢管表 (CHS/P)";
                default: return "型钢表";
            }
        }

        public static List<SectionProfile> GetByShapeType(SectionShapeType type)
        {
            if (_tables.TryGetValue(type, out var list))
                return list.ToList();
            return new List<SectionProfile>();
        }

        public static List<SectionProfile> GetSectionsByType(SectionShapeType type) => GetByShapeType(type);

        public static List<string> GetNamesByShapeType(SectionShapeType type)
        {
            return GetByShapeType(type).Select(x => x.Name).ToList();
        }

        public static List<SectionProfile> GetAllSections()
        {
            var all = new List<SectionProfile>();
            foreach (SectionShapeType t in Enum.GetValues(typeof(SectionShapeType)))
            {
                if (_tables.TryGetValue(t, out var list))
                    all.AddRange(list);
            }
            return all;
        }

        public static List<string> GetAllNames()
        {
            return GetAllSections().Select(x => x.Name).ToList();
        }

        public static SectionProfile GetByName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                foreach (var list in _tables.Values)
                {
                    var found = list.FirstOrDefault(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (found != null) return found;
                }
            }
            return _tables[SectionShapeType.HBeam][1];
        }

        public static SectionProfile GetSectionOrDefault(string name) => GetByName(name);

        public static void AddOrUpdate(SectionProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.Name)) return;
            var list = _tables[profile.Shape];
            int idx = list.FindIndex(s => string.Equals(s.Name, profile.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                list[idx] = profile.Clone();
            else
                list.Add(profile.Clone());
        }

        public static void AddOrUpdateSection(SectionProfile profile) => AddOrUpdate(profile);

        public static bool Remove(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            foreach (var kv in _tables)
            {
                var list = kv.Value;
                int idx = list.FindIndex(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (idx >= 0 && list.Count > 1)
                {
                    list.RemoveAt(idx);
                    return true;
                }
            }
            return false;
        }

        public static void ResetToDefaults()
        {
            _tables = CreateDefaultTables();
        }

        public static void SaveCatalog()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SteelStructureSuite");
                Directory.CreateDirectory(dir);
                ExportCatalogToCsv(Path.Combine(dir, "SectionCatalog_MultiTable.csv"));
            }
            catch { }
        }

        public static void ExportCatalogToCsv(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("截面代号(Name),截面类型(ShapeType),高度H(mm),宽度B(mm),腹板厚Tw(mm),翼缘厚Tf(mm),截面积(cm2),理论单重(kg/m),表面积(m2/m)");
            foreach (var s in GetAllSections())
            {
                sb.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2:0.##},{3:0.##},{4:0.##},{5:0.##},{6:0.00},{7:0.00},{8:0.000}\r\n",
                    s.Name, s.Shape, s.H, s.B, s.Tw, s.Tf, s.AreaCm2, s.UnitWeightKgPerM, s.PerimeterMPerM);
            }
            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
        }

        public static void ExportCatalogToXlsx(string filePath)
        {
            if (File.Exists(filePath)) File.Delete(filePath);

            using (FileStream fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.ReadWrite))
            using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "[Content_Types].xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/worksheets/sheet2.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/worksheets/sheet3.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/worksheets/sheet4.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/worksheets/sheet5.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
</Types>");

                WriteEntry(archive, "_rels/.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>");

                WriteEntry(archive, "xl/workbook.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheets>
    <sheet name=""1_H型钢表"" sheetId=""1"" r:id=""rId1""/>
    <sheet name=""2_槽钢表"" sheetId=""2"" r:id=""rId2""/>
    <sheet name=""3_角钢表"" sheetId=""3"" r:id=""rId3""/>
    <sheet name=""4_方矩管表"" sheetId=""4"" r:id=""rId4""/>
    <sheet name=""5_圆钢管表"" sheetId=""5"" r:id=""rId5""/>
  </sheets>
</workbook>");

                WriteEntry(archive, "xl/_rels/workbook.xml.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
  <Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet2.xml""/>
  <Relationship Id=""rId3"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet3.xml""/>
  <Relationship Id=""rId4"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet4.xml""/>
  <Relationship Id=""rId5"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet5.xml""/>
</Relationships>");

                SectionShapeType[] types =
                {
                    SectionShapeType.HBeam,
                    SectionShapeType.Channel,
                    SectionShapeType.Angle,
                    SectionShapeType.RectTube,
                    SectionShapeType.PipeTube
                };

                for (int i = 0; i < types.Length; i++)
                {
                    WriteEntry(archive, $"xl/worksheets/sheet{i + 1}.xml", BuildSectionSheetXml(_tables[types[i]]));
                }
            }
        }

        public static int ImportCatalogFromFile(string filePath, bool replaceExisting = false)
        {
            if (!File.Exists(filePath)) return 0;
            if (replaceExisting)
            {
                foreach (var k in _tables.Keys.ToList()) _tables[k].Clear();
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".csv")
            {
                return ImportFromCsv(filePath);
            }

            int importedCount = 0;
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                List<string> sharedStrings = new List<string>();
                ZipArchiveEntry ssEntry = archive.GetEntry("xl/sharedStrings.xml");
                if (ssEntry != null)
                {
                    using (Stream s = ssEntry.Open())
                    {
                        XmlDocument doc = new XmlDocument();
                        doc.Load(s);
                        XmlNodeList tNodes = doc.GetElementsByTagName("t");
                        foreach (XmlNode tn in tNodes)
                        {
                            sharedStrings.Add(tn.InnerText);
                        }
                    }
                }

                SectionShapeType[] types =
                {
                    SectionShapeType.HBeam,
                    SectionShapeType.Channel,
                    SectionShapeType.Angle,
                    SectionShapeType.RectTube,
                    SectionShapeType.PipeTube
                };

                for (int i = 0; i < types.Length; i++)
                {
                    ZipArchiveEntry sheetEntry = archive.GetEntry($"xl/worksheets/sheet{i + 1}.xml");
                    if (sheetEntry == null) continue;

                    using (Stream s = sheetEntry.Open())
                    {
                        XmlDocument doc = new XmlDocument();
                        doc.Load(s);
                        XmlNodeList rowNodes = doc.GetElementsByTagName("row");

                        foreach (XmlNode rowNode in rowNodes)
                        {
                            var rAttr = rowNode.Attributes?["r"];
                            if (rAttr != null && rAttr.Value == "1") continue;

                            List<string> cellVals = new List<string>();
                            foreach (XmlNode cNode in rowNode.ChildNodes)
                            {
                                if (cNode.LocalName != "c") continue;
                                string tAttr = cNode.Attributes?["t"]?.Value ?? "";
                                string val = "";
                                if (tAttr == "inlineStr")
                                {
                                    val = cNode.InnerText;
                                }
                                else if (tAttr == "s")
                                {
                                    if (int.TryParse(cNode.InnerText, out int ssIdx) && ssIdx >= 0 && ssIdx < sharedStrings.Count)
                                        val = sharedStrings[ssIdx];
                                }
                                else
                                {
                                    val = cNode.InnerText;
                                }
                                cellVals.Add(val.Trim());
                            }

                            if (cellVals.Count >= 5 &&
                                !string.IsNullOrWhiteSpace(cellVals[0]) &&
                                double.TryParse(cellVals[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double h) && h > 10 &&
                                double.TryParse(cellVals[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double b) && b > 10 &&
                                double.TryParse(cellVals[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double tw) && tw > 1 &&
                                double.TryParse(cellVals[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double tf) && tf > 1)
                            {
                                AddOrUpdate(new SectionProfile(cellVals[0], types[i], h, b, tw, tf));
                                importedCount++;
                            }
                        }
                    }
                }
            }

            return importedCount;
        }

        private static int ImportFromCsv(string filePath)
        {
            int count = 0;
            foreach (string line in File.ReadAllLines(filePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] parts = line.Split(',');
                if (parts.Length < 6) continue;
                if (Enum.TryParse(parts[1].Trim(), true, out SectionShapeType shape) &&
                    double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double h) &&
                    double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double b) &&
                    double.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double tw) &&
                    double.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out double tf))
                {
                    AddOrUpdate(new SectionProfile(parts[0].Trim(), shape, h, b, tw, tf));
                    count++;
                }
            }
            return count;
        }

        private static string BuildSectionSheetXml(List<SectionProfile> list)
        {
            var sb = new StringBuilder();
            sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
            sb.Append(@"<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><sheetData>");
            sb.Append(@"<row r=""1"">");
            string[] cols = { "截面代号(Name)", "高度/外径H(mm)", "宽度B(mm)", "腹板/壁厚Tw(mm)", "翼缘厚Tf(mm)", "截面积(cm2)", "理论米重(kg/m)" };
            for (int c = 0; c < cols.Length; c++)
            {
                char colChar = (char)('A' + c);
                sb.Append($@"<c r=""{colChar}1"" t=""inlineStr""><is><t>{SecurityElement.Escape(cols[c])}</t></is></c>");
            }
            sb.Append(@"</row>");

            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                int r = i + 2;
                sb.Append($@"<row r=""{r}"">");
                sb.Append($@"<c r=""A{r}"" t=""inlineStr""><is><t>{SecurityElement.Escape(s.Name)}</t></is></c>");
                sb.Append($@"<c r=""B{r}""><v>{s.H.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""C{r}""><v>{s.B.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""D{r}""><v>{s.Tw.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""E{r}""><v>{s.Tf.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""F{r}""><v>{s.AreaCm2.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""G{r}""><v>{s.UnitWeightKgPerM.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append(@"</row>");
            }

            sb.Append(@"</sheetData></worksheet>");
            return sb.ToString();
        }

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
            ZipArchiveEntry e = archive.CreateEntry(path, CompressionLevel.Optimal);
            using (Stream s = e.Open())
            using (StreamWriter sw = new StreamWriter(s, new UTF8Encoding(false)))
            {
                sw.Write(content);
            }
        }
    }
}
