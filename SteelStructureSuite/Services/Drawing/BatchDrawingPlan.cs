using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Services.Drawing
{
    /// <summary>
    /// 批量出图计划：把一批构件按「图纸复用」规则归并成若干张图。
    ///
    /// 复用规则：几何参数、截面、材质、构件类型完全一致的构件视为同一规格，
    /// 只出一张详图，图名里注明「共 N 件」，材料表按单件列出并在合计里乘上数量，
    /// 避免 N 个一模一样的构件生成 N 张几乎相同的图纸。
    /// </summary>
    public sealed class BatchDrawingPlan
    {
        /// <summary>一组可共用一张详图的构件。</summary>
        public sealed class SheetGroup
        {
            /// <summary>代表性构件（用于出图）。</summary>
            public SteelStructureParams Representative { get; set; }

            /// <summary>该组包含的所有构件（含代表构件本身）。</summary>
            public List<SteelStructureParams> Members { get; } = new List<SteelStructureParams>();

            /// <summary>组内构件数量（用于图名与材料表数量）。</summary>
            public int Count => Members.Count;

            /// <summary>图名中使用的构件位号列表（超过 3 个时折叠）。</summary>
            public string TagSummary
            {
                get
                {
                    var tags = Members.Select(m => m.Tag ?? string.Empty)
                                      .Where(t => t.Length > 0)
                                      .ToList();
                    if (tags.Count == 0) return "-";
                    if (tags.Count <= 3) return string.Join("、", tags);
                    return string.Join("、", tags.Take(3)) + $" 等 {tags.Count} 件";
                }
            }
        }

        public List<SheetGroup> Groups { get; } = new List<SheetGroup>();

        /// <summary>参与批量出图的总构件数。</summary>
        public int TotalStructureCount { get; private set; }

        /// <summary>实际生成的图纸张数（复用后）。</summary>
        public int SheetCount => Groups.Count;

        /// <summary>因复用而省下的图纸张数。</summary>
        public int SavedSheetCount => Math.Max(0, TotalStructureCount - SheetCount);

        /// <summary>
        /// 由一批构件构建出图计划。
        /// </summary>
        /// <param name="structures">待出图的全部构件。</param>
        /// <param name="reuseIdentical">是否把完全相同的构件合并为一张图。</param>
        public static BatchDrawingPlan Build(IEnumerable<SteelStructureParams> structures, bool reuseIdentical = true)
        {
            var plan = new BatchDrawingPlan();
            if (structures == null) return plan;

            var list = structures.Where(s => s != null).ToList();
            plan.TotalStructureCount = list.Count;
            if (list.Count == 0) return plan;

            if (!reuseIdentical)
            {
                foreach (SteelStructureParams s in list)
                {
                    var group = new SheetGroup { Representative = s };
                    group.Members.Add(s);
                    plan.Groups.Add(group);
                }
                return plan;
            }

            // 按「签名」归并：签名只包含影响几何与材料表的字段，不含位号与插入点
            var byKey = new Dictionary<string, SheetGroup>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (SteelStructureParams s in list)
            {
                string key = SignatureOf(s);
                if (!byKey.TryGetValue(key, out SheetGroup group))
                {
                    group = new SheetGroup { Representative = s };
                    byKey[key] = group;
                    order.Add(key);
                }
                group.Members.Add(s);
            }

            foreach (string key in order)
                plan.Groups.Add(byKey[key]);

            return plan;
        }

        /// <summary>
        /// 构件签名：类型 + 全部几何参数 + 三个截面 + 材质 + 若干影响形状的开关。
        /// 位号、原点、旋转、曲线坐标系不参与签名（不影响单件详图形状）。
        /// </summary>
        public static string SignatureOf(SteelStructureParams p)
        {
            if (p == null) return string.Empty;

            var sb = new StringBuilder(160);
            sb.Append((int)p.StructureType).Append('|');
            sb.Append(N(p.Span)).Append('|');
            sb.Append(N(p.Length)).Append('|');
            sb.Append(N(p.Height)).Append('|');
            sb.Append(N(p.SubHeight)).Append('|');
            sb.Append(p.BayCount).Append('|');
            sb.Append(p.ColumnSection ?? string.Empty).Append('|');
            sb.Append(p.BeamSection ?? string.Empty).Append('|');
            sb.Append(p.BraceSection ?? string.Empty).Append('|');
            sb.Append(p.Material ?? string.Empty).Append('|');
            sb.Append(N(p.EndPointOffsetZ)).Append('|');
            sb.Append(N(p.TopRadius)).Append('|');
            sb.Append(p.HelixCounterClockwise ? '1' : '0');
            return sb.ToString();
        }

        private static string N(double v)
        {
            // 保留 3 位小数，避免浮点噪声导致本应相同的构件被判为不同
            return Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
