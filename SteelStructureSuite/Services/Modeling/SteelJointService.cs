using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace SteelStructureSuite.Services.Modeling
{
    /// <summary>
    /// 杆件连接处的几何处理：斜接（miter）。
    ///
    /// 设计约束（非常重要）：
    ///   本服务只修改 <see cref="SteelStructureBuilder.SolidMemberPart.Solid"/> 的几何，
    ///   <b>绝不修改 StartPt / EndPt</b>。
    ///   因此材料明细、单根下料长度、图纸标注始终是「切割前的真实长度」，
    ///   而三维显示与详图轮廓则是「切割后的真实接头形态」。
    ///
    /// 判定规则（依据杆件本身已是「轴线方向挤出、端面垂直于自身轴线」这一前提）：
    ///   · 近共线（|cos| &gt; 0.985）   → 不处理：本身已是连续对接面；
    ///   · 近垂直（|cos| &lt; 0.26）    → 不处理：端面本就垂直于轴线，天然平齐对接；
    ///   · 其余斜交                    → 沿角平分面斜接，两根杆件切口完全贴合。
    ///   中间交叉（如 X 型交叉支撑）交点不在端部区间，一律不处理，保持贯通。
    /// </summary>
    public static class SteelJointService
    {
        /// <summary>总开关：启用连接处真实斜接与主从切平处理。</summary>
        public static bool Enabled = true;

        /// <summary>近垂直阈值：|cos| 小于此值视为垂直对接，无需斜接。</summary>
        private const double PerpendicularCos = 0.26;      // 约 75°

        /// <summary>近共线阈值：|cos| 大于此值视为共线，无需斜接。</summary>
        private const double CollinearCos = 0.985;         // 约 10°

        /// <summary>交点必须落在两端点参数区间内，避免把中间交叉的杆件误判为端部连接。</summary>
        private const double EndZoneMin = 0.12;
        private const double EndZoneMax = 0.88;

        /// <summary>切割量小于该值（mm）时不动实体，避免无意义的布尔运算。</summary>
        private const double MinCutDepthMm = 1.0;

        private sealed class AxisInfo
        {
            public Point3d Start;
            public Point3d End;
            public Vector3d Dir;
            public double Length;
            public double HalfWidth;
        }

        /// <summary>一次端部斜接的计算结果（不含布尔运算，便于独立验证）。</summary>
        public sealed class JointCut
        {
            public int PartIndexA;
            public int PartIndexB;
            /// <summary>斜接平面通过点（两轴交点）。</summary>
            public Point3d PlanePoint;
            /// <summary>A 侧保留方向的法向（A 保留该法向正侧）。</summary>
            public Vector3d NormalA;
            /// <summary>B 侧保留方向的法向（B 保留该法向正侧），恒为 NormalA 的反向。</summary>
            public Vector3d NormalB;
        }

        /// <summary>
        /// 纯几何计算：判断两根杆件是否端部斜交、应沿哪个角平分面斜接。
        /// 不触碰任何 Solid3d，可脱离 AutoCAD 运行，便于单元验证。
        /// </summary>
        /// <returns>需要斜接的计划；无需处理时返回 null。</returns>
        public static JointCut PlanJoint(
            int indexA, Point3d startA, Point3d finishA, double halfWidthA,
            int indexB, Point3d startB, Point3d finishB, double halfWidthB)
        {
            var aa = MakeAxis(startA, finishA, halfWidthA);
            var ab = MakeAxis(startB, finishB, halfWidthB);
            if (aa == null || ab == null) return null;

            if (!TryClosestPoints(aa, ab, out Point3d xa, out Point3d xb, out double dist))
                return null;

            // 最近距离超过两者截面半宽之和，说明根本没接触
            double contactLimit = (aa.HalfWidth + ab.HalfWidth) * 0.85;
            if (dist > contactLimit) return null;

            Point3d joint = new Point3d(
                (xa.X + xb.X) * 0.5,
                (xa.Y + xb.Y) * 0.5,
                (xa.Z + xb.Z) * 0.5);

            // 交点必须落在两根杆件的端部区间，中间交叉不处理
            double ta = ParameterOnAxis(aa, xa);
            double tb = ParameterOnAxis(ab, xb);
            if (ta > EndZoneMin && ta < EndZoneMax) return null;
            if (tb > EndZoneMin && tb < EndZoneMax) return null;

            Vector3d dA = DirectionAwayFromJoint(aa, joint);
            Vector3d dB = DirectionAwayFromJoint(ab, joint);
            if (dA.Length < 0.5 || dB.Length < 0.5) return null;

            double cos = Math.Abs(dA.DotProduct(dB));

            // 近垂直：端面本就垂直，无需斜接
            if (cos < PerpendicularCos) return null;
            // 近共线：本身就是连续对接面，无需斜接
            if (cos > CollinearCos) return null;

            // 斜交：角平分面。法向沿 (dA - dB)，A 保留正侧、B 保留负侧。
            Vector3d normalA = (dA - dB);
            if (normalA.Length < 1e-6) return null;
            normalA = normalA.GetNormal();

            return new JointCut
            {
                PartIndexA = indexA,
                PartIndexB = indexB,
                PlanePoint = joint,
                NormalA = normalA,
                NormalB = -normalA
            };
        }

        /// <summary>
        /// 对一组构件执行连接处真实构造裁剪（角平分斜接 + 主从平切 + 落地平切）。
        /// 应在局部坐标系下、整体平移/旋转之前调用。
        /// </summary>
        /// <param name="parts">构件列表（会被就地修改实体几何，端点保持不变）。</param>
        /// <param name="warnings">可选：收集斜接失败的告警。</param>
        /// <returns>实际发生裁剪的构件数量。</returns>
        public static int ApplyJointTrims(
            List<SteelStructureBuilder.SolidMemberPart> parts,
            List<string> warnings = null)
        {
            if (!Enabled || parts == null || parts.Count < 2) return 0;

            // 1. 挑出可参与接头处理的杆件（排除板件、曲线杆件、退化杆件）
            var indices = new List<int>();
            var axes = new Dictionary<int, AxisInfo>();
            var boxes = new Dictionary<int, Extents3d>();

            for (int i = 0; i < parts.Count; i++)
            {
                SteelStructureBuilder.SolidMemberPart p = parts[i];
                if (p?.Solid == null) continue;
                if (p.IsPlate) continue;
                if (p.Section == null) continue;
                if (p.PathPoints != null && p.PathPoints.Count > 2) continue;   // 弧/螺旋杆件不切

                AxisInfo axis = MakeAxis(p.StartPt, p.EndPt, Math.Max(p.Section.H, p.Section.B) * 0.5);
                if (axis == null) continue;

                Extents3d ext;
                try { ext = p.Solid.GeometricExtents; }
                catch { continue; }

                indices.Add(i);
                axes[i] = axis;
                boxes[i] = ext;
            }
            if (indices.Count < 1) return 0;

            var trimmed = new HashSet<int>();

            // 2. 两两处理端部接头（先用包围盒粗筛）
            for (int a = 0; a < indices.Count; a++)
            {
                for (int b = a + 1; b < indices.Count; b++)
                {
                    int ia = indices[a];
                    int ib = indices[b];
                    if (!ExtentsOverlap(boxes[ia], boxes[ib], 1.0)) continue;

                    SteelStructureBuilder.SolidMemberPart pa = parts[ia];
                    SteelStructureBuilder.SolidMemberPart pb = parts[ib];

                    int tierA = GetMemberHierarchyTier(pa.RoleName);
                    int tierB = GetMemberHierarchyTier(pb.RoleName);

                    try
                    {
                        if (tierA != tierB)
                        {
                            // 主从节点：只切次杆端头使其贴合主杆外表面，不动主杆
                            int iPrimary = tierA < tierB ? ia : ib;
                            int iSecondary = tierA < tierB ? ib : ia;
                            if (TrimSecondaryToPrimaryInternal(parts[iSecondary], axes[iSecondary], parts[iPrimary], axes[iPrimary], 1.5, warnings))
                            {
                                trimmed.Add(iSecondary);
                            }
                        }
                        else
                        {
                            // 同级节点（如屋脊双坡斜梁、转角扶手/弦杆）：沿角平分面斜接
                            JointCut cut = PlanJoint(
                                ia, axes[ia].Start, axes[ia].End, axes[ia].HalfWidth,
                                ib, axes[ib].Start, axes[ib].End, axes[ib].HalfWidth);
                            if (cut == null) continue;

                            if (CutMember(pa, cut.PlanePoint, cut.NormalA, warnings))
                                trimmed.Add(ia);
                            if (CutMember(pb, cut.PlanePoint, cut.NormalB, warnings))
                                trimmed.Add(ib);
                        }
                    }
                    catch (Exception ex)
                    {
                        warnings?.Add($"“{pa.RoleName}”与“{pb.RoleName}”接头裁剪失败：{ex.Message}");
                    }
                }
            }

            // 3. 落地斜杆底端水平切平（防止斜撑/梯梁底部斜插到 Z=0 以下）
            foreach (int idx in indices)
            {
                SteelStructureBuilder.SolidMemberPart p = parts[idx];
                AxisInfo ax = axes[idx];
                double absDz = Math.Abs(ax.Dir.Z);
                if (absDz < 0.08 || absDz > 0.96) continue; // 跳过纯水平或纯竖直杆件

                double minZ = Math.Min(ax.Start.Z, ax.End.Z);
                double maxZ = Math.Max(ax.Start.Z, ax.End.Z);
                if (minZ <= 20.0 && maxZ > 150.0)
                {
                    if (CutMember(p, new Point3d(0, 0, Math.Max(0.0, minZ)), Vector3d.ZAxis, warnings))
                        trimmed.Add(idx);
                }
            }

            return trimmed.Count;
        }

        /// <summary>
        /// 判定杆件主从层级：数值越小越为主杆（0=柱/主梁/弦杆/梯梁，1=次梁/系杆/檩条，2=斜撑/隅撑/腹杆/牛腿斜撑）。
        /// </summary>
        private static int GetMemberHierarchyTier(string roleName)
        {
            if (string.IsNullOrEmpty(roleName)) return 1;
            if (roleName.Contains("斜撑") || roleName.Contains("隅撑") || roleName.Contains("腹杆") ||
                roleName.Contains("撑杆") || roleName.Contains("横隔") || roleName.Contains("托臂"))
                return 2;
            if (roleName.Contains("立柱") || roleName.Contains("钢柱") || roleName.Contains("主柱") ||
                roleName.Contains("支柱") || roleName.Contains("主梁") || roleName.Contains("斜梁") ||
                roleName.Contains("边梁") || roleName.Contains("纵梁") || roleName.Contains("横梁") ||
                roleName.Contains("弦杆") || roleName.Contains("梯梁") || roleName.Contains("牛腿主梁") ||
                roleName.Contains("柱牛腿"))
                return 0;
            return 1;
        }

        /// <summary>
        /// 公开的通用半空间裁剪：将杆件裁剪到指定平面，保留 <paramref name="keepNormal"/> 指向的一侧。
        /// </summary>
        public static bool TrimMemberToPlane(
            SteelStructureBuilder.SolidMemberPart part,
            Point3d planePoint,
            Vector3d keepNormal,
            List<string> warnings = null)
            => CutMember(part, planePoint, keepNormal, warnings);

        /// <summary>
        /// 主从切平：将次杆端头裁剪到主杆外轮廓平面（保留微嵌入量 <paramref name="embedMm"/> 以确保布尔合并零缝隙）。
        /// </summary>
        public static bool TrimSecondaryToPrimary(
            SteelStructureBuilder.SolidMemberPart secondary,
            SteelStructureBuilder.SolidMemberPart primary,
            double embedMm = 1.5,
            List<string> warnings = null)
        {
            if (secondary?.Section == null || primary?.Section == null) return false;
            AxisInfo secAxis = MakeAxis(secondary.StartPt, secondary.EndPt, Math.Max(secondary.Section.H, secondary.Section.B) * 0.5);
            AxisInfo priAxis = MakeAxis(primary.StartPt, primary.EndPt, Math.Max(primary.Section.H, primary.Section.B) * 0.5);
            if (secAxis == null || priAxis == null) return false;
            return TrimSecondaryToPrimaryInternal(secondary, secAxis, primary, priAxis, embedMm, warnings);
        }

        private static bool TrimSecondaryToPrimaryInternal(
            SteelStructureBuilder.SolidMemberPart secondary,
            AxisInfo secAxis,
            SteelStructureBuilder.SolidMemberPart primary,
            AxisInfo priAxis,
            double embedMm,
            List<string> warnings)
        {
            if (!TryClosestPoints(secAxis, priAxis, out Point3d xs, out Point3d xp, out double dist))
                return false;

            double contactLimit = (secAxis.HalfWidth + priAxis.HalfWidth) * 1.1;
            if (dist > contactLimit) return false;

            // 次杆必须是以端部连接到主杆（排除中间穿过主杆的连续杆）
            double ts = ParameterOnAxis(secAxis, xs);
            if (ts > EndZoneMin && ts < EndZoneMax) return false;

            Vector3d dSec = DirectionAwayFromJoint(secAxis, xs);
            double cos = Math.Abs(dSec.DotProduct(priAxis.Dir));
            if (cos > CollinearCos) return false; // 共线不切

            // 垂直于主杆、指向次杆远端的外法向
            Vector3d outNormal = dSec - priAxis.Dir * dSec.DotProduct(priAxis.Dir);
            if (outNormal.Length < 1e-4) return false;
            outNormal = outNormal.GetNormal();

            // 计算主杆截面在该外法向上的真实半宽（精确贴合主杆H/B投影）
            SectionSolidBuilder.GetMemberAxes(priAxis.Dir, out Vector3d px, out Vector3d py, out _);
            double priHalfExtent =
                Math.Abs(px.DotProduct(outNormal)) * (primary.Section.H * 0.5) +
                Math.Abs(py.DotProduct(outNormal)) * (primary.Section.B * 0.5);

            double offset = Math.Max(0.0, priHalfExtent - embedMm);
            Point3d cutPlanePt = xp + outNormal * offset;

            return CutMember(secondary, cutPlanePt, outNormal, warnings);
        }

        /// <summary>
        /// 把杆件切到指定平面：保留 <paramref name="keepNormal"/> 指向的一侧。
        ///
        /// 安全性：先在副本上做布尔运算，成功且结果有效才替换原实体。
        /// 这样即使 BooleanOperation 抛异常或切出空实体，原几何也完好无损，
        /// 绝不会因为接头处理而让构件在模型里消失。
        /// </summary>
        private static bool CutMember(
            SteelStructureBuilder.SolidMemberPart part,
            Point3d planePoint,
            Vector3d keepNormal,
            List<string> warnings)
        {
            if (part?.Solid == null) return false;

            Vector3d normal = keepNormal.GetNormal();
            if (normal.Length < 1e-9) return false;

            Extents3d before;
            try { before = part.Solid.GeometricExtents; }
            catch { return false; }

            double size = (before.MaxPoint - before.MinPoint).Length * 2.0 + 400.0;
            double cutDepth = EstimateDiscardDepth(before, planePoint, normal);
            if (cutDepth < MinCutDepthMm) return false;

            Solid3d work = null;
            Solid3d cutter = null;
            try
            {
                work = (Solid3d)part.Solid.Clone();
                cutter = BuildHalfSpaceCutter(planePoint, normal, size);
                if (cutter == null) return false;

                work.BooleanOperation(BooleanOperationType.BoolSubtract, cutter);

                // 结果必须仍然是一个有体积的实体
                Extents3d after = work.GeometricExtents;
                Vector3d diag = after.MaxPoint - after.MinPoint;
                if (diag.Length < 1.0) return false;

                // 通过校验，用副本替换原实体
                part.Solid.Dispose();
                part.Solid = work;
                work = null;
                return true;
            }
            catch (Exception ex)
            {
                warnings?.Add($"“{part.RoleName}”端部斜接已跳过（保留原几何）：{ex.Message}");
                return false;
            }
            finally
            {
                work?.Dispose();
                cutter?.Dispose();
            }
        }

        /// <summary>估算包围盒在切割平面「丢弃侧」的最大延伸量。</summary>
        private static double EstimateDiscardDepth(Extents3d box, Point3d planePoint, Vector3d keepNormal)
        {
            double maxDepth = 0.0;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Point3d(
                    (i & 1) == 0 ? box.MinPoint.X : box.MaxPoint.X,
                    (i & 2) == 0 ? box.MinPoint.Y : box.MaxPoint.Y,
                    (i & 4) == 0 ? box.MinPoint.Z : box.MaxPoint.Z);

                double d = (corner - planePoint).DotProduct(keepNormal);
                if (d < 0) maxDepth = Math.Max(maxDepth, -d);
            }
            return maxDepth;
        }

        /// <summary>构造半空间切割体：覆盖 <paramref name="keepNormal"/> 反方向那一侧的大盒子。</summary>
        private static Solid3d BuildHalfSpaceCutter(Point3d planePoint, Vector3d keepNormal, double size)
        {
            Vector3d nx = keepNormal.GetNormal();
            if (nx.Length < 1e-9) return null;

            Vector3d reference = Math.Abs(nx.Z) < 0.9 ? Vector3d.ZAxis : Vector3d.XAxis;
            Vector3d ny = nx.CrossProduct(reference);
            if (ny.Length < 1e-9) return null;
            ny = ny.GetNormal();
            Vector3d nz = nx.CrossProduct(ny).GetNormal();

            // 盒子中心落在丢弃侧（keepNormal 的反方向）
            Point3d center = planePoint - nx * (size * 0.5);

            var cutter = new Solid3d();
            cutter.SetDatabaseDefaults();
            cutter.CreateBox(size, size * 2.0, size * 2.0);
            cutter.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                center, nx, ny, nz));
            return cutter;
        }

        // ------------------------------------------------------------------
        // 几何辅助
        // ------------------------------------------------------------------

        private static AxisInfo MakeAxis(Point3d start, Point3d end, double halfWidth)
        {
            double len = start.DistanceTo(end);
            if (len < 20.0) return null;
            return new AxisInfo
            {
                Start = start,
                End = end,
                Dir = (end - start) / len,
                Length = len,
                HalfWidth = Math.Max(6.0, halfWidth)
            };
        }

        private static double ParameterOnAxis(AxisInfo axis, Point3d pt)
        {
            if (axis.Length < 1e-9) return 0.5;
            return (pt - axis.Start).DotProduct(axis.Dir) / axis.Length;
        }

        /// <summary>交点指向杆件远端的方向（单位向量）。</summary>
        private static Vector3d DirectionAwayFromJoint(AxisInfo axis, Point3d joint)
        {
            double t = ParameterOnAxis(axis, joint);
            Point3d far = t <= 0.5 ? axis.End : axis.Start;
            Vector3d v = far - joint;
            return v.Length < 1e-6 ? Vector3d.ZAxis : v.GetNormal();
        }

        private static bool ExtentsOverlap(Extents3d a, Extents3d b, double pad)
        {
            if (a.MaxPoint.X + pad < b.MinPoint.X || b.MaxPoint.X + pad < a.MinPoint.X) return false;
            if (a.MaxPoint.Y + pad < b.MinPoint.Y || b.MaxPoint.Y + pad < a.MinPoint.Y) return false;
            if (a.MaxPoint.Z + pad < b.MinPoint.Z || b.MaxPoint.Z + pad < a.MinPoint.Z) return false;
            return true;
        }

        /// <summary>求两条轴线的最近点对与距离（含平行/共线的稳健处理）。</summary>
        private static bool TryClosestPoints(
            AxisInfo a,
            AxisInfo b,
            out Point3d pa,
            out Point3d pb,
            out double dist)
        {
            pa = a.Start;
            pb = b.Start;
            dist = double.MaxValue;

            Vector3d u = a.End - a.Start;
            Vector3d v = b.End - b.Start;
            Vector3d w = a.Start - b.Start;

            double uu = u.DotProduct(u);
            double vv = v.DotProduct(v);
            double uv = u.DotProduct(v);
            double uw = u.DotProduct(w);
            double vw = v.DotProduct(w);

            double denom = uu * vv - uv * uv;

            if (Math.Abs(denom) > 1e-9 * uu * vv)
            {
                double s = (uv * vw - vv * uw) / denom;
                double t = (uu * vw - uv * uw) / denom;
                s = Math.Max(0.0, Math.Min(1.0, s));
                t = Math.Max(0.0, Math.Min(1.0, t));
                pa = a.Start + u * s;
                pb = b.Start + v * t;
                dist = pa.DistanceTo(pb);
                return true;
            }

            // 平行/共线：用「轴向区间重叠 + 横向垂直距离」判定是否真的相接。
            // 不能直接把端点投影到对方线段上，否则两条平行的远离杆件会因为
            // 投影点被钳制到端点而得到距离 0，被误判成相接。
            if (!SegmentsTouch(a, b))
                return false;

            double lateral = LateralDistance(a, b);
            double tolerance = (a.HalfWidth + b.HalfWidth) * 0.85;
            if (lateral > tolerance)
                return false;

            // 取轴向重叠区内、离对方最近的那个端点作为接触点
            Point3d[] candidates = { a.Start, a.End, b.Start, b.End };
            foreach (Point3d c in candidates)
            {
                Point3d projA = ProjectOntoSegment(c, a);
                double d = c.DistanceTo(projA);
                if (d < dist) { dist = d; pa = projA; pb = c; }

                Point3d projB = ProjectOntoSegment(c, b);
                d = c.DistanceTo(projB);
                if (d < dist) { dist = d; pa = c; pb = projB; }
            }
            return dist < double.MaxValue;
        }

        /// <summary>两条（近似）平行轴线的横向垂直距离。</summary>
        private static double LateralDistance(AxisInfo a, AxisInfo b)
        {
            Vector3d axis = a.Dir;
            Vector3d w = b.Start - a.Start;
            Vector3d perpendicular = w - axis * w.DotProduct(axis);
            return perpendicular.Length;
        }

        /// <summary>判断两条线段在轴向投影上是否重叠（即是否真的首尾相接或交叉）。</summary>
        private static bool SegmentsTouch(AxisInfo a, AxisInfo b)
        {
            // 用 a 的轴向做参数轴，比较两条线段在该轴上的投影区间
            Vector3d axis = a.Dir;
            double a0 = (a.Start - a.Start).DotProduct(axis);
            double a1 = (a.End - a.Start).DotProduct(axis);
            double b0 = (b.Start - a.Start).DotProduct(axis);
            double b1 = (b.End - a.Start).DotProduct(axis);

            double aMin = Math.Min(a0, a1), aMax = Math.Max(a0, a1);
            double bMin = Math.Min(b0, b1), bMax = Math.Max(b0, b1);

            // 允许半个截面宽的容差（端部相互插入的情况）
            double tolerance = (a.HalfWidth + b.HalfWidth) * 0.85;
            return bMin <= aMax + tolerance && bMax >= aMin - tolerance;
        }

        private static Point3d ProjectOntoSegment(Point3d pt, AxisInfo axis)
        {
            Vector3d v = axis.End - axis.Start;
            double vv = v.DotProduct(v);
            if (vv < 1e-12) return axis.Start;
            double t = (pt - axis.Start).DotProduct(v) / vv;
            t = Math.Max(0.0, Math.Min(1.0, t));
            return axis.Start + v * t;
        }
    }
}
