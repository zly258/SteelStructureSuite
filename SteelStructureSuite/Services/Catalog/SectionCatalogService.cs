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
    /// 按 5 大类、24 种截面类型独立管理（涵盖热轧型钢、焊接组合截面、冷弯薄壁型钢、空心钢管、实心型材），
    /// 支持持久化保存、多工作表 Excel (.xlsx) 与 CSV 批量导出与导入。
    /// </summary>
    public static class SectionCatalogService
    {
        public static readonly SectionCategory[] OrderedCategories =
        {
            SectionCategory.HotRolled,
            SectionCategory.WeldedComposite,
            SectionCategory.ColdFormed,
            SectionCategory.HollowTube,
            SectionCategory.SolidBar
        };

        public static readonly SectionShapeType[] OrderedShapeTypes =
        {
            // 1. 热轧标准型钢
            SectionShapeType.HBeam,
            SectionShapeType.IBeam,
            SectionShapeType.Channel,
            SectionShapeType.Angle,
            SectionShapeType.UnequalAngle,
            SectionShapeType.TBeam,

            // 2. 焊接与组合型钢
            SectionShapeType.WeldedHBeam,
            SectionShapeType.WeldedHBeamUnequal,
            SectionShapeType.WeldedBox,
            SectionShapeType.WeldedTBeam,
            SectionShapeType.WeldedCross,
            SectionShapeType.DoubleAngle,
            SectionShapeType.DoubleChannel,

            // 3. 冷弯薄壁型钢
            SectionShapeType.ColdFormedC,
            SectionShapeType.ColdFormedZ,
            SectionShapeType.ZBeam,
            SectionShapeType.ColdFormedU,
            SectionShapeType.HatSection,

            // 4. 空心钢管
            SectionShapeType.SquareTube,
            SectionShapeType.RectTube,
            SectionShapeType.PipeTube,

            // 5. 实心型材与板材
            SectionShapeType.FlatBar,
            SectionShapeType.RoundBar,
            SectionShapeType.SquareBar
        };

        private static Dictionary<SectionShapeType, List<SectionProfile>> _tables = InitializeTables();

        private static Dictionary<SectionShapeType, List<SectionProfile>> InitializeTables()
        {
            var tables = CreateDefaultTables();
            try
            {
                string savedPath = GetLocalCatalogCsvPath();
                if (File.Exists(savedPath))
                {
                    ImportFromCsvInto(tables, savedPath);
                }
                else
                {
                    // 首次初始化时自动在本地落盘生成 CSV 文件，方便用户查看、备份与编辑
                    Directory.CreateDirectory(Path.GetDirectoryName(savedPath));
                    ExportTablesToCsv(tables, savedPath);
                }
            }
            catch
            {
                // 读取/写入本地缓存失败时安全回退默认国标库
            }
            return tables;
        }

        /// <summary>截面库本地持久化文件完整路径（UTF-8 with BOM CSV 格式）</summary>
        public static string CatalogFilePath => GetLocalCatalogCsvPath();

        /// <summary>随插件 DLL 分发的默认截面库 CSV 路径（用于首次初始化与恢复默认库）</summary>
        public static string DefaultCatalogCsvPath => FindDefaultCatalogCsvPath();

        private static string GetLocalCatalogCsvPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteelStructureSuite");
            return Path.Combine(dir, "SectionCatalog_MultiTable.csv");
        }

        private static string FindDefaultCatalogCsvPath()
        {
            string asmDir = Path.GetDirectoryName(typeof(SectionCatalogService).Assembly.Location)
                            ?? AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(asmDir, "Data", "DefaultSectionCatalog.csv"),
                Path.GetFullPath(Path.Combine(asmDir, "..", "Data", "DefaultSectionCatalog.csv")),
                Path.Combine(asmDir, "DefaultSectionCatalog.csv")
            };
            foreach (string path in candidates)
            {
                if (File.Exists(path)) return path;
            }
            return candidates[0];
        }

        private static Dictionary<SectionShapeType, List<SectionProfile>> CreateDefaultTables()
        {
            var tables = new Dictionary<SectionShapeType, List<SectionProfile>>();
            foreach (SectionShapeType st in OrderedShapeTypes)
            {
                tables[st] = new List<SectionProfile>();
            }

            try
            {
                string defaultCsv = FindDefaultCatalogCsvPath();
                if (File.Exists(defaultCsv))
                {
                    ImportFromCsvInto(tables, defaultCsv);
                }
            }
            catch
            {
                // 外部默认 CSV 读取异常时进入最小兜底，避免插件启动失败
            }

            // 最小兜底：确保任何情况下至少保留基础规格，防止下拉框与建模空指针
            if (tables[SectionShapeType.HBeam].Count == 0)
                tables[SectionShapeType.HBeam].Add(new SectionProfile("HW200x200x8x12", SectionShapeType.HBeam, 200, 200, 8, 12).WithHandbookProps(63.53, 49.9, 1.158));
            if (tables[SectionShapeType.Channel].Count == 0)
                tables[SectionShapeType.Channel].Add(new SectionProfile("C16a", SectionShapeType.Channel, 160, 63, 6.5, 10.0).WithHandbookProps(21.96, 17.24, 0.548));
            if (tables[SectionShapeType.Angle].Count == 0)
                tables[SectionShapeType.Angle].Add(new SectionProfile("L75x75x6", SectionShapeType.Angle, 75, 75, 6, 6).WithHandbookProps(8.80, 6.91, 0.295));
            if (tables[SectionShapeType.RectTube].Count == 0)
                tables[SectionShapeType.RectTube].Add(new SectionProfile("F200x100x8", SectionShapeType.RectTube, 200, 100, 8, 8));
            if (tables[SectionShapeType.PipeTube].Count == 0)
                tables[SectionShapeType.PipeTube].Add(new SectionProfile("P114x6", SectionShapeType.PipeTube, 114, 114, 6, 6));

            return tables;
        }

        public static List<SectionShapeType> GetShapeTypesByCategory(SectionCategory category)
        {
            return OrderedShapeTypes.Where(t => SectionProfile.GetCategory(t) == category).ToList();
        }

        public static string GetShapeTabTitle(SectionShapeType shapeType)
        {
            return SectionProfile.GetShapeDisplayName(shapeType);
        }

        public static List<SectionProfile> GetByShapeType(SectionShapeType type)
        {
            if (_tables.TryGetValue(type, out var list))
                return list.ToList();
            return new List<SectionProfile>();
        }

        public static List<SectionProfile> GetByCategory(SectionCategory category)
        {
            var result = new List<SectionProfile>();
            foreach (SectionShapeType t in GetShapeTypesByCategory(category))
            {
                if (_tables.TryGetValue(t, out var list))
                    result.AddRange(list);
            }
            return result;
        }

        public static List<SectionProfile> GetSectionsByType(SectionShapeType type) => GetByShapeType(type);

        public static List<string> GetNamesByShapeType(SectionShapeType type)
        {
            if (type == SectionShapeType.RectTube)
            {
                // 兼容原“方矩管”筛选逻辑：同时包含方管与矩形管
                return GetByShapeType(SectionShapeType.SquareTube)
                    .Concat(GetByShapeType(SectionShapeType.RectTube))
                    .Select(x => x.Name)
                    .ToList();
            }
            return GetByShapeType(type).Select(x => x.Name).ToList();
        }

        public static List<SectionProfile> GetAllSections()
        {
            var all = new List<SectionProfile>();
            foreach (SectionShapeType t in OrderedShapeTypes)
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
                string key = name.Trim();
                foreach (var list in _tables.Values)
                {
                    var found = list.FirstOrDefault(s => string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase));
                    if (found != null) return found;
                }
            }
            var hList = _tables[SectionShapeType.HBeam];
            return hList.Count > 4 ? hList[4] : hList.FirstOrDefault() ?? new SectionProfile();
        }

        public static SectionProfile GetSectionOrDefault(string name) => GetByName(name);

        public static void AddOrUpdate(SectionProfile profile)
        {
            AddOrUpdateInto(_tables, profile);
            SaveCatalog();
        }

        private static void AddOrUpdateInto(Dictionary<SectionShapeType, List<SectionProfile>> tables, SectionProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.Name)) return;
            string name = profile.Name.Trim();

            // 如果同名截面原本在其它分类下，先移除旧记录避免重名冲突
            foreach (var kv in tables)
            {
                if (kv.Key == profile.Shape) continue;
                int oldIdx = kv.Value.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                if (oldIdx >= 0 && kv.Value.Count > 1)
                {
                    kv.Value.RemoveAt(oldIdx);
                }
            }

            if (!tables.TryGetValue(profile.Shape, out var list))
            {
                list = new List<SectionProfile>();
                tables[profile.Shape] = list;
            }

            SectionProfile copy = profile.Clone();
            copy.Name = name;
            int idx = list.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                list[idx] = copy;
            else
                list.Add(copy);
        }

        public static void AddOrUpdateSection(SectionProfile profile) => AddOrUpdate(profile);

        public static bool Remove(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string key = name.Trim();
            foreach (var kv in _tables)
            {
                var list = kv.Value;
                int idx = list.FindIndex(s => string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0 && list.Count > 1)
                {
                    list.RemoveAt(idx);
                    SaveCatalog();
                    return true;
                }
            }
            return false;
        }

        public static void ResetToDefaults()
        {
            _tables = CreateDefaultTables();
            SaveCatalog();
        }

        public static void SaveCatalog()
        {
            try
            {
                string path = GetLocalCatalogCsvPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                ExportTablesToCsv(_tables, path);
            }
            catch { }
        }

        public static void ExportCatalogToCsv(string filePath)
        {
            ExportTablesToCsv(_tables, filePath);
        }

        private static void ExportTablesToCsv(Dictionary<SectionShapeType, List<SectionProfile>> tables, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("截面代号(Name),截面类型(ShapeType),高度H(mm),宽度B(mm),腹板厚Tw(mm),翼缘厚Tf(mm),下翼缘宽B2(mm),下翼缘厚Tf2(mm),卷边C(mm),双拼间隙Gap(mm),截面积(cm2),理论单重(kg/m),表面积(m2/m),数值来源(PropSource)");
            foreach (SectionShapeType t in OrderedShapeTypes)
            {
                if (!tables.TryGetValue(t, out var list)) continue;
                foreach (var s in list)
                {
                    sb.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0},{1},{2:0.##},{3:0.##},{4:0.##},{5:0.##},{6:0.##},{7:0.##},{8:0.##},{9:0.##},{10:0.00},{11:0.00},{12:0.000},{13}\r\n",
                        s.Name, s.Shape, s.H, s.B, s.Tw, s.Tf, s.B2, s.Tf2, s.Lip, s.Gap,
                        s.AreaCm2, s.UnitWeightKgPerM, s.PerimeterMPerM,
                        s.HasManualProperties ? "Manual" : "Auto");
                }
            }
            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>
        /// 按 5 大类工作表导出多 Sheet Excel (.xlsx)，包含全部扩展参数列
        /// </summary>
        public static void ExportCatalogToXlsx(string filePath)
        {
            if (File.Exists(filePath)) File.Delete(filePath);

            string[] sheetNames =
            {
                "1_热轧标准型钢",
                "2_焊接与组合型钢",
                "3_冷弯薄壁型钢",
                "4_空心钢管",
                "5_实心型材与板材"
            };

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

                var wbSb = new StringBuilder();
                wbSb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
                wbSb.AppendLine(@"<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">");
                wbSb.AppendLine("  <sheets>");
                for (int i = 0; i < sheetNames.Length; i++)
                {
                    wbSb.AppendLine($@"    <sheet name=""{SecurityElement.Escape(sheetNames[i])}"" sheetId=""{i + 1}"" r:id=""rId{i + 1}""/>");
                }
                wbSb.AppendLine("  </sheets>");
                wbSb.AppendLine("</workbook>");
                WriteEntry(archive, "xl/workbook.xml", wbSb.ToString());

                WriteEntry(archive, "xl/_rels/workbook.xml.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
  <Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet2.xml""/>
  <Relationship Id=""rId3"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet3.xml""/>
  <Relationship Id=""rId4"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet4.xml""/>
  <Relationship Id=""rId5"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet5.xml""/>
</Relationships>");

                for (int i = 0; i < OrderedCategories.Length; i++)
                {
                    var list = GetByCategory(OrderedCategories[i]);
                    WriteEntry(archive, $"xl/worksheets/sheet{i + 1}.xml", BuildSectionSheetXml(list));
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
                return ImportFromCsvInto(_tables, filePath);
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

                // 兼容旧版 5 工作表默认类型映射
                SectionShapeType[] legacyTypes =
                {
                    SectionShapeType.HBeam,
                    SectionShapeType.Channel,
                    SectionShapeType.Angle,
                    SectionShapeType.RectTube,
                    SectionShapeType.PipeTube
                };

                var sheetEntries = archive.Entries
                    .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                             && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = 0; i < sheetEntries.Count; i++)
                {
                    SectionShapeType fallbackType = i < legacyTypes.Length ? legacyTypes[i] : SectionShapeType.HBeam;

                    using (Stream s = sheetEntries[i].Open())
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

                            if (cellVals.Count < 5 || string.IsNullOrWhiteSpace(cellVals[0])) continue;

                            // 新格式：[0]Name, [1]ShapeType, [2]H, [3]B, [4]Tw, [5]Tf, [6]B2, [7]Tf2, [8]Lip, [9]Gap
                            if (cellVals.Count >= 6 && Enum.TryParse(cellVals[1], true, out SectionShapeType parsedShape) &&
                                TryParsePositive(cellVals[2], out double nh) &&
                                TryParseNonNegative(cellVals[3], out double nb) &&
                                TryParseNonNegative(cellVals[4], out double ntw) &&
                                TryParseNonNegative(cellVals[5], out double ntf))
                            {
                                double b2 = cellVals.Count > 6 && TryParseNonNegative(cellVals[6], out double vB2) ? vB2 : 0.0;
                                double tf2 = cellVals.Count > 7 && TryParseNonNegative(cellVals[7], out double vTf2) ? vTf2 : 0.0;
                                double lip = cellVals.Count > 8 && TryParseNonNegative(cellVals[8], out double vLip) ? vLip : 0.0;
                                double gap = cellVals.Count > 9 && TryParseNonNegative(cellVals[9], out double vGap) ? vGap : 0.0;

                                var prof = new SectionProfile(cellVals[0], parsedShape, nh, nb, ntw, ntf, b2, tf2, lip, gap);
                                ApplyImportedPhysicalOverrides(
                                    prof,
                                    cellVals.Count > 10 ? cellVals[10] : null,
                                    cellVals.Count > 11 ? cellVals[11] : null,
                                    cellVals.Count > 12 ? cellVals[12] : null,
                                    cellVals.Count > 13 ? cellVals[13] : null);
                                AddOrUpdate(prof);
                                importedCount++;
                            }
                            // 兼容旧格式：[0]Name, [1]H, [2]B, [3]Tw, [4]Tf, [5]Area, [6]Weight
                            else if (TryParsePositive(cellVals[1], out double h) &&
                                     TryParsePositive(cellVals[2], out double b) &&
                                     TryParsePositive(cellVals[3], out double tw) &&
                                     TryParsePositive(cellVals[4], out double tf))
                            {
                                var prof = new SectionProfile(cellVals[0], fallbackType, h, b, tw, tf);
                                ApplyImportedPhysicalOverrides(
                                    prof,
                                    cellVals.Count > 5 ? cellVals[5] : null,
                                    cellVals.Count > 6 ? cellVals[6] : null,
                                    null, null);
                                AddOrUpdate(prof);
                                importedCount++;
                            }
                        }
                    }
                }
            }

            return importedCount;
        }

        private static int ImportFromCsvInto(Dictionary<SectionShapeType, List<SectionProfile>> tables, string filePath)
        {
            int count = 0;
            bool isNewFormat = false;
            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (i == 0 && (line.Contains("ShapeType") || line.Contains("截面代号")))
                {
                    isNewFormat = line.Contains("B2") || line.Contains("Lip") || line.Contains("Gap");
                    continue;
                }

                string[] parts = line.Split(',');
                if (parts.Length < 6) continue;
                if (Enum.TryParse(parts[1].Trim(), true, out SectionShapeType shape) &&
                    TryParsePositive(parts[2], out double h) &&
                    TryParseNonNegative(parts[3], out double b) &&
                    TryParseNonNegative(parts[4], out double tw) &&
                    TryParseNonNegative(parts[5], out double tf))
                {
                    double b2 = 0.0, tf2 = 0.0, lip = 0.0, gap = 0.0;
                    if (isNewFormat && parts.Length >= 10)
                    {
                        TryParseNonNegative(parts[6], out b2);
                        TryParseNonNegative(parts[7], out tf2);
                        TryParseNonNegative(parts[8], out lip);
                        TryParseNonNegative(parts[9], out gap);
                    }

                    var prof = new SectionProfile(parts[0].Trim(), shape, h, b, tw, tf, b2, tf2, lip, gap);
                    if (isNewFormat && parts.Length >= 13)
                    {
                        ApplyImportedPhysicalOverrides(
                            prof,
                            parts[10], parts[11], parts[12],
                            parts.Length > 13 ? parts[13] : null);
                    }
                    else if (!isNewFormat && parts.Length >= 9)
                    {
                        ApplyImportedPhysicalOverrides(prof, parts[6], parts[7], parts[8], null);
                    }

                    AddOrUpdateInto(tables, prof);
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 导入时保留手册/用户手工修改过的截面积、单重、表面积：
        /// 若显式标记为 Manual，或导入值与几何公式计算值存在差异（说明用户/手册已修正），则作为覆盖值保存。
        /// </summary>
        private static void ApplyImportedPhysicalOverrides(
            SectionProfile prof,
            string areaText,
            string weightText,
            string surfaceText,
            string sourceFlag)
        {
            bool explicitManual = string.Equals(sourceFlag?.Trim(), "Manual", StringComparison.OrdinalIgnoreCase);
            bool explicitAuto = string.Equals(sourceFlag?.Trim(), "Auto", StringComparison.OrdinalIgnoreCase);
            if (explicitAuto) return;

            if (TryParsePositive(areaText, out double a))
            {
                if (explicitManual || Math.Abs(a - prof.GetCalculatedAreaCm2()) > 0.02)
                    prof.ManualAreaCm2 = a;
            }
            if (TryParsePositive(weightText, out double w))
            {
                if (explicitManual || Math.Abs(w - prof.GetCalculatedUnitWeightKgPerM()) > 0.02)
                    prof.ManualUnitWeightKgPerM = w;
            }
            if (TryParsePositive(surfaceText, out double s))
            {
                if (explicitManual || Math.Abs(s - prof.GetCalculatedUnitSurfaceM2PerM()) > 0.003)
                    prof.ManualUnitSurfaceM2PerM = s;
            }
        }

        private static bool TryParsePositive(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value) && value > 0;
        }

        private static bool TryParseNonNegative(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value) && value >= 0;
        }

        private static string BuildSectionSheetXml(List<SectionProfile> list)
        {
            var sb = new StringBuilder();
            sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
            sb.Append(@"<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><sheetData>");
            sb.Append(@"<row r=""1"">");
            string[] cols =
            {
                "截面代号(Name)",
                "截面类型(ShapeType)",
                "高度/外径H(mm)",
                "宽度B(mm)",
                "腹板/壁厚Tw(mm)",
                "翼缘厚Tf(mm)",
                "下翼缘宽B2(mm)",
                "下翼缘厚Tf2(mm)",
                "卷边C(mm)",
                "双拼间隙Gap(mm)",
                "截面积(cm2)",
                "理论米重(kg/m)",
                "表面积(m2/m)",
                "数值来源(PropSource)"
            };
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
                sb.Append($@"<c r=""B{r}"" t=""inlineStr""><is><t>{s.Shape}</t></is></c>");
                sb.Append($@"<c r=""C{r}""><v>{s.H.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""D{r}""><v>{s.B.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""E{r}""><v>{s.Tw.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""F{r}""><v>{s.Tf.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""G{r}""><v>{s.B2.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""H{r}""><v>{s.Tf2.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""I{r}""><v>{s.Lip.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""J{r}""><v>{s.Gap.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""K{r}""><v>{s.AreaCm2.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""L{r}""><v>{s.UnitWeightKgPerM.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""M{r}""><v>{s.PerimeterMPerM.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""N{r}"" t=""inlineStr""><is><t>{(s.HasManualProperties ? "Manual" : "Auto")}</t></is></c>");
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
