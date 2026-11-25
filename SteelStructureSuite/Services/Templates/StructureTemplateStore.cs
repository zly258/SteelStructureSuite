using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Catalog;

namespace SteelStructureSuite.Services.Templates
{
    /// <summary>
    /// 参数模板库：以单个 JSON 文件保存在
    /// %LocalAppData%\SteelStructureSuite\Templates.json。
    ///
    /// 为什么不用 SQLite：
    ///   · 本插件同时要跑 net48 与 net8，SQLite 需要随每个 CAD 版本目录分发对应位数的
    ///     原生 e_sqlite3.dll，缺一个就是插件加载即崩；
    ///   · 模板量级只有几十到几百条，单文件 JSON 足够；
    ///   · 零第三方依赖，与现有 UserSettings / 截面库 CSV 的存储习惯一致。
    ///
    /// 写入策略：先写临时文件 → 校验可反序列化 → 备份旧文件 → 原子替换，
    /// 任何一步失败都不会破坏已有模板库。
    /// </summary>
    public static class StructureTemplateStore
    {
        private static List<StructureTemplate> _cache;
        private static readonly object Sync = new object();

        /// <summary>模板库文件完整路径。</summary>
        public static string StoreFilePath => Path.Combine(FolderPath, "Templates.json");

        /// <summary>备份文件路径。</summary>
        public static string BackupFilePath => StoreFilePath + ".bak";

        private static string FolderPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SteelStructureSuite");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        // ------------------------------------------------------------------
        // 查询
        // ------------------------------------------------------------------

        /// <summary>取全部模板（按构件类型、名称排序）。</summary>
        public static List<StructureTemplate> GetAll()
        {
            lock (Sync)
            {
                EnsureLoaded();
                return _cache
                    .OrderBy(t => t.IsGeneric ? 1 : 0)
                    .ThenBy(t => t.StructureType?.ToString() ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
        }

        /// <summary>取指定构件类型自带的 3 种内置预设模板。</summary>
        public static List<StructureTemplate> GetBuiltInForType(ParametricStructureType type)
        {
            var presets = SteelStructureParams.GetPresetsForType(type);
            var list = new List<StructureTemplate>();
            if (presets == null) return list;

            for (int i = 0; i < presets.Count; i++)
            {
                StructurePresetItem item = presets[i];
                var p = new SteelStructureParams
                {
                    Tag = string.Empty,
                    StructureType = type,
                    Span = item.Span,
                    Length = item.Length,
                    Height = item.Height,
                    SubHeight = item.SubHeight,
                    BayCount = item.BayCount <= 0 ? 2 : item.BayCount,
                    ColumnSection = string.IsNullOrWhiteSpace(item.ColSec) ? "HW200x200x8x12" : item.ColSec,
                    BeamSection = string.IsNullOrWhiteSpace(item.BeamSec) ? "HW200x200x8x12" : item.BeamSec,
                    BraceSection = string.IsNullOrWhiteSpace(item.BraceSec) ? "L100x100x8" : item.BraceSec,
                    Material = "Q355B"
                };
                list.Add(new StructureTemplate
                {
                    Id = $"BUILTIN_{(int)type}_{i}",
                    Name = item.Name,
                    Note = "内置预设",
                    StructureType = type,
                    IsBuiltIn = true,
                    ColumnSectionType = SectionCatalogService.TryFindByName(p.ColumnSection)?.Shape,
                    BeamSectionType = SectionCatalogService.TryFindByName(p.BeamSection)?.Shape,
                    BraceSectionType = SectionCatalogService.TryFindByName(p.BraceSection)?.Shape,
                    Parameters = p
                });
            }
            return list;
        }

        /// <summary>取指定构件类型可用的全部模板：自带 3 种内置预设 + 真实用户模板（该类型专属 + 通用模板）。</summary>
        public static List<StructureTemplate> GetForTypeWithBuiltIn(ParametricStructureType type)
        {
            var result = GetBuiltInForType(type);
            var userTemplates = GetForType(type);
            result.AddRange(userTemplates);
            return result;
        }

        /// <summary>取指定构件类型可用的真实用户模板：该类型专属 + 通用模板。</summary>
        public static List<StructureTemplate> GetForType(ParametricStructureType type)
        {
            lock (Sync)
            {
                EnsureLoaded();
                return _cache
                    .Where(t => t.IsGeneric || (t.StructureType.HasValue && t.StructureType.Value == type))
                    .OrderBy(t => t.IsGeneric ? 1 : 0)
                    .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
        }

        public static StructureTemplate GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (Sync)
            {
                EnsureLoaded();
                return _cache.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
            }
        }

        // ------------------------------------------------------------------
        // 增删改
        // ------------------------------------------------------------------

        /// <summary>
        /// 保存模板。若同名（同类型）模板已存在则覆盖其内容并保留 Id。
        /// </summary>
        /// <returns>实际保存后的模板。</returns>
        public static StructureTemplate Save(StructureTemplate template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (string.IsNullOrWhiteSpace(template.Name))
                throw new ArgumentException("模板名称不能为空。");

            lock (Sync)
            {
                EnsureLoaded();

                StructureTemplate existing = _cache.FirstOrDefault(t =>
                    string.Equals(t.Name?.Trim(), template.Name.Trim(), StringComparison.CurrentCultureIgnoreCase) &&
                    t.StructureType == template.StructureType);

                if (existing != null && !string.Equals(existing.Id, template.Id, StringComparison.Ordinal))
                {
                    // 同名覆盖：保留原 Id 与创建时间
                    existing.Parameters = template.Parameters?.Clone() ?? new SteelStructureParams();
                    existing.Note = template.Note ?? string.Empty;
                    existing.UpdatedAt = DateTime.Now;
                    Persist();
                    return existing;
                }

                StructureTemplate sameId = _cache.FirstOrDefault(t =>
                    string.Equals(t.Id, template.Id, StringComparison.Ordinal));

                if (sameId != null)
                {
                    sameId.Name = template.Name.Trim();
                    sameId.Note = template.Note ?? string.Empty;
                    sameId.StructureType = template.StructureType;
                    sameId.Parameters = template.Parameters?.Clone() ?? new SteelStructureParams();
                    sameId.UpdatedAt = DateTime.Now;
                }
                else
                {
                    template.Name = template.Name.Trim();
                    template.UpdatedAt = DateTime.Now;
                    if (string.IsNullOrEmpty(template.Id)) template.Id = Guid.NewGuid().ToString("N");
                    _cache.Add(template);
                }

                Persist();
                return template;
            }
        }

        public static bool Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (Sync)
            {
                EnsureLoaded();
                int removed = _cache.RemoveAll(t => string.Equals(t.Id, id, StringComparison.Ordinal));
                if (removed > 0)
                {
                    Persist();
                    return true;
                }
                return false;
            }
        }

        public static bool Rename(string id, string newName)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(newName)) return false;
            lock (Sync)
            {
                EnsureLoaded();
                StructureTemplate t = _cache.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
                if (t == null) return false;

                bool duplicate = _cache.Any(x =>
                    !string.Equals(x.Id, id, StringComparison.Ordinal) &&
                    x.StructureType == t.StructureType &&
                    string.Equals(x.Name?.Trim(), newName.Trim(), StringComparison.CurrentCultureIgnoreCase));
                if (duplicate) return false;

                t.Name = newName.Trim();
                t.UpdatedAt = DateTime.Now;
                Persist();
                return true;
            }
        }

        // ------------------------------------------------------------------
        // 导入导出
        // ------------------------------------------------------------------

        /// <summary>把全部模板导出到指定 JSON 文件（用于备份或同事间共享）。</summary>
        public static void ExportTo(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("导出路径不能为空。");

            lock (Sync)
            {
                EnsureLoaded();
                var dto = new TemplateFileDto
                {
                    Version = 1,
                    SavedAt = DateTime.Now,
                    Templates = _cache.Select(ToDto).ToList()
                };
                WriteJson(filePath, dto);
            }
        }

        /// <summary>
        /// 从 JSON 文件导入模板。同名同类型时跳过，返回新增数量。
        /// </summary>
        public static int ImportFrom(string filePath)
        {
            return ImportFrom(filePath, out _);
        }

        /// <summary>
        /// 从 JSON 文件导入模板。同名同类型时跳过，返回新增数量并输出跳过数量。
        /// </summary>
        public static int ImportFrom(string filePath, out int skippedCount)
        {
            skippedCount = 0;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("模板文件不存在。", filePath);

            TemplateFileDto dto = ReadJson<TemplateFileDto>(filePath);
            if (dto?.Templates == null || dto.Templates.Count == 0) return 0;

            int added = 0;
            lock (Sync)
            {
                EnsureLoaded();
                foreach (TemplateDto d in dto.Templates)
                {
                    StructureTemplate t = FromDto(d);
                    if (t == null || string.IsNullOrWhiteSpace(t.Name)) continue;

                    bool exists = _cache.Any(x =>
                        x.StructureType == t.StructureType &&
                        string.Equals(x.Name?.Trim(), t.Name.Trim(), StringComparison.CurrentCultureIgnoreCase));
                    if (exists)
                    {
                        skippedCount++;
                        continue;
                    }

                    t.Id = Guid.NewGuid().ToString("N");
                    t.IsBuiltIn = false;
                    _cache.Add(t);
                    added++;
                }

                if (added > 0) Persist();
            }
            return added;
        }

        // ------------------------------------------------------------------
        // 持久化
        // ------------------------------------------------------------------

        private static void EnsureLoaded()
        {
            if (_cache != null) return;

            _cache = new List<StructureTemplate>();
            string path = StoreFilePath;
            if (!File.Exists(path))
            {
                // 首次运行：自带 3 种预设已由 GetBuiltInForType 独立提供，真实用户模板库初始为空并落盘
                try { Persist(); } catch { }
                return;
            }

            try
            {
                TemplateFileDto dto = ReadJson<TemplateFileDto>(path);
                if (dto?.Templates != null)
                {
                    foreach (TemplateDto d in dto.Templates)
                    {
                        StructureTemplate t = FromDto(d);
                        if (t != null) _cache.Add(t);
                    }
                }
            }
            catch
            {
                // 主文件损坏时尝试备份
                try
                {
                    if (File.Exists(BackupFilePath))
                    {
                        TemplateFileDto dto = ReadJson<TemplateFileDto>(BackupFilePath);
                        if (dto?.Templates != null)
                        {
                            foreach (TemplateDto d in dto.Templates)
                            {
                                StructureTemplate t = FromDto(d);
                                if (t != null) _cache.Add(t);
                            }
                        }
                    }
                }
                catch
                {
                    _cache.Clear();
                }
            }
        }

        private static void Persist()
        {
            string path = StoreFilePath;
            string temp = path + ".tmp";

            var dto = new TemplateFileDto
            {
                Version = 1,
                SavedAt = DateTime.Now,
                Templates = _cache.Select(ToDto).ToList()
            };

            WriteJson(temp, dto);

            // 校验临时文件确实能读回来，再替换正式文件
            TemplateFileDto verify = ReadJson<TemplateFileDto>(temp);
            if (verify == null) throw new IOException("模板库写入校验失败。");

            try
            {
                if (File.Exists(path))
                {
                    if (File.Exists(BackupFilePath)) File.Delete(BackupFilePath);
                    File.Copy(path, BackupFilePath, true);
                }
            }
            catch
            {
                // 备份失败不影响主流程
            }

            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        /// <summary>强制重新从磁盘加载（测试与外部修改后使用）。</summary>
        public static void Reload()
        {
            lock (Sync)
            {
                _cache = null;
                EnsureLoaded();
            }
        }

        // ------------------------------------------------------------------
        // JSON 序列化（DataContractJsonSerializer，net48 与 net8 都自带，零依赖）
        // ------------------------------------------------------------------

        [DataContract]
        private sealed class TemplateFileDto
        {
            [DataMember(Name = "version", Order = 0)]
            public int Version { get; set; }

            [DataMember(Name = "savedAt", Order = 1)]
            public DateTime SavedAt { get; set; }

            [DataMember(Name = "templates", Order = 2)]
            public List<TemplateDto> Templates { get; set; }
        }

        [DataContract]
        private sealed class TemplateDto
        {
            [DataMember(Name = "id", Order = 0)] public string Id { get; set; }
            [DataMember(Name = "name", Order = 1)] public string Name { get; set; }
            [DataMember(Name = "note", Order = 2)] public string Note { get; set; }
            [DataMember(Name = "structureType", Order = 3)] public int StructureType { get; set; }
            [DataMember(Name = "isGeneric", Order = 4)] public bool IsGeneric { get; set; }
            [DataMember(Name = "createdAt", Order = 5)] public DateTime CreatedAt { get; set; }
            [DataMember(Name = "updatedAt", Order = 6)] public DateTime UpdatedAt { get; set; }

            [DataMember(Name = "span", Order = 10)] public double Span { get; set; }
            [DataMember(Name = "length", Order = 11)] public double Length { get; set; }
            [DataMember(Name = "height", Order = 12)] public double Height { get; set; }
            [DataMember(Name = "subHeight", Order = 13)] public double SubHeight { get; set; }
            [DataMember(Name = "bayCount", Order = 14)] public int BayCount { get; set; }
            [DataMember(Name = "columnSection", Order = 15)] public string ColumnSection { get; set; }
            [DataMember(Name = "beamSection", Order = 16)] public string BeamSection { get; set; }
            [DataMember(Name = "braceSection", Order = 17)] public string BraceSection { get; set; }
            [DataMember(Name = "material", Order = 18)] public string Material { get; set; }
            [DataMember(Name = "endPointOffsetZ", Order = 19)] public double EndPointOffsetZ { get; set; }
            [DataMember(Name = "topRadius", Order = 20)] public double TopRadius { get; set; }
            [DataMember(Name = "helixCounterClockwise", Order = 21)] public bool HelixCounterClockwise { get; set; }
            [DataMember(Name = "columnSectionType", Order = 22, EmitDefaultValue = false)] public string ColumnSectionType { get; set; }
            [DataMember(Name = "beamSectionType", Order = 23, EmitDefaultValue = false)] public string BeamSectionType { get; set; }
            [DataMember(Name = "braceSectionType", Order = 24, EmitDefaultValue = false)] public string BraceSectionType { get; set; }
        }

        private static TemplateDto ToDto(StructureTemplate t)
        {
            SteelStructureParams p = t.Parameters ?? new SteelStructureParams();
            SectionShapeType? colType = t.ColumnSectionType ?? SectionCatalogService.TryFindByName(p.ColumnSection)?.Shape;
            SectionShapeType? beamType = t.BeamSectionType ?? SectionCatalogService.TryFindByName(p.BeamSection)?.Shape;
            SectionShapeType? braceType = t.BraceSectionType ?? SectionCatalogService.TryFindByName(p.BraceSection)?.Shape;
            return new TemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                Note = t.Note,
                StructureType = t.StructureType.HasValue ? (int)t.StructureType.Value : -1,
                IsGeneric = t.IsGeneric,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
                Span = p.Span,
                Length = p.Length,
                Height = p.Height,
                SubHeight = p.SubHeight,
                BayCount = p.BayCount,
                ColumnSection = p.ColumnSection,
                BeamSection = p.BeamSection,
                BraceSection = p.BraceSection,
                Material = p.Material,
                EndPointOffsetZ = p.EndPointOffsetZ,
                TopRadius = p.TopRadius,
                HelixCounterClockwise = p.HelixCounterClockwise,
                ColumnSectionType = colType?.ToString(),
                BeamSectionType = beamType?.ToString(),
                BraceSectionType = braceType?.ToString()
            };
        }

        private static SectionShapeType? ParseShapeType(string text, string fallbackSectionName)
        {
            if (!string.IsNullOrWhiteSpace(text) && Enum.TryParse(text.Trim(), true, out SectionShapeType parsed))
                return parsed;
            return SectionCatalogService.TryFindByName(fallbackSectionName)?.Shape;
        }

        private static StructureTemplate FromDto(TemplateDto d)
        {
            if (d == null || string.IsNullOrWhiteSpace(d.Name)) return null;

            ParametricStructureType? type = null;
            if (!d.IsGeneric && d.StructureType >= 0 && Enum.IsDefined(typeof(ParametricStructureType), d.StructureType))
                type = (ParametricStructureType)d.StructureType;

            var p = new SteelStructureParams
            {
                Tag = string.Empty,
                StructureType = type ?? ParametricStructureType.PortalFrame,
                Span = d.Span,
                Length = d.Length,
                Height = d.Height,
                SubHeight = d.SubHeight,
                BayCount = d.BayCount <= 0 ? 2 : d.BayCount,
                ColumnSection = d.ColumnSection ?? "HW200x200x8x12",
                BeamSection = d.BeamSection ?? "HW200x200x8x12",
                BraceSection = d.BraceSection ?? "L100x100x8",
                Material = d.Material ?? "Q355B",
                EndPointOffsetZ = d.EndPointOffsetZ,
                TopRadius = d.TopRadius,
                HelixCounterClockwise = d.HelixCounterClockwise
            };

            return new StructureTemplate
            {
                Id = string.IsNullOrEmpty(d.Id) ? Guid.NewGuid().ToString("N") : d.Id,
                Name = d.Name.Trim(),
                Note = d.Note ?? string.Empty,
                StructureType = type,
                CreatedAt = d.CreatedAt == default(DateTime) ? DateTime.Now : d.CreatedAt,
                UpdatedAt = d.UpdatedAt == default(DateTime) ? DateTime.Now : d.UpdatedAt,
                ColumnSectionType = ParseShapeType(d.ColumnSectionType, p.ColumnSection),
                BeamSectionType = ParseShapeType(d.BeamSectionType, p.BeamSection),
                BraceSectionType = ParseShapeType(d.BraceSectionType, p.BraceSection),
                Parameters = p
            };
        }

        private static void WriteJson<T>(string path, T value)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, value);
                string json = Encoding.UTF8.GetString(ms.ToArray());
                // 缩进一下便于人工查看与 diff
                File.WriteAllText(path, PrettyPrint(json), new UTF8Encoding(true));
            }
        }

        private static T ReadJson<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return null;

            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(ms) as T;
            }
        }

        /// <summary>简单缩进，便于人工查看（不影响反序列化）。</summary>
        private static string PrettyPrint(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;

            var sb = new StringBuilder(json.Length + 256);
            int indent = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\')) inString = !inString;

                if (!inString && (c == '{' || c == '['))
                {
                    sb.Append(c).Append('\n');
                    indent++;
                    sb.Append(' ', indent * 2);
                }
                else if (!inString && (c == '}' || c == ']'))
                {
                    sb.Append('\n');
                    indent = Math.Max(0, indent - 1);
                    sb.Append(' ', indent * 2).Append(c);
                }
                else if (!inString && c == ',')
                {
                    sb.Append(c).Append('\n').Append(' ', indent * 2);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 首次运行的内置模板（替代原先写死的轻/中/重）
        // ------------------------------------------------------------------

        private static IEnumerable<StructureTemplate> BuildSeedTemplates()
        {
            var list = new List<StructureTemplate>();

            void Seed(string name, string note, ParametricStructureType type, double span, double height, double subH, string col, string beam, string brace)
            {
                var p = new SteelStructureParams
                {
                    Tag = string.Empty,
                    StructureType = type,
                    Span = span,
                    Height = height,
                    SubHeight = subH,
                    Length = 1500,
                    BayCount = 2,
                    ColumnSection = col,
                    BeamSection = beam,
                    BraceSection = brace,
                    Material = "Q355B"
                };
                list.Add(new StructureTemplate
                {
                    Name = name,
                    Note = note,
                    StructureType = type,
                    Parameters = p
                });
            }

            Seed("门架-轻型 3m", "小跨度轻型门式刚架，HW200 系列", ParametricStructureType.PortalFrame, 3000, 3600, 1800, "HW200x200x8x12", "HW200x200x8x12", "L100x100x8");
            Seed("门架-中型 6m", "常规厂房跨度，HN 系列主梁", ParametricStructureType.PortalFrame, 6000, 6000, 3000, "HW300x300x10x15", "HN400x200x8x13", "L125x125x10");
            Seed("门架-重型 12m", "大跨度重载门架，焊接 H 型钢", ParametricStructureType.PortalFrame, 12000, 9000, 4500, "HW400x400x13x21", "BH800x300x14x26", "L160x160x12");

            Seed("桁架-轻型 12m", "双坡屋面桁架", ParametricStructureType.RoofTruss, 12000, 5000, 1500, "HW300x300x10x15", "HW250x250x9x14", "L90x90x8");
            Seed("桁架-重型 24m", "大跨屋面桁架", ParametricStructureType.RoofTruss, 24000, 8000, 3000, "HW400x400x13x21", "HW350x350x12x19", "L125x125x10");

            Seed("平台-标准 6x4", "四柱检修平台含护栏", ParametricStructureType.SpacePlatform, 6000, 3000, 1050, "HW200x200x8x12", "HW250x125x6x9", "L75x75x6");
            Seed("栏杆-标准 1.2m", "GB 4053.3 平台栏杆", ParametricStructureType.HandrailGuardrail, 6000, 1200, 1500, "P42x3.5", "P42x3.5", "P42x3.5");

            Seed("斜梯-标准 3m", "直跑钢斜梯含双侧扶手", ParametricStructureType.StraightStair, 3000, 3000, 900, "C160x60x20x2.5", "P42x3.5", "PL4x250");

            return list;
        }
    }
}
