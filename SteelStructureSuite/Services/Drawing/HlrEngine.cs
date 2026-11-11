using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SteelStructureSuite.Services.Modeling;
using SolidMemberPart = SteelStructureSuite.Services.Modeling.SteelStructureBuilder.SolidMemberPart;

namespace SteelStructureSuite.Services.Drawing
{
    /// <summary>
    /// 真实消隐（HLR）引擎。
    ///
    /// 与“投影凸包”的做法不同，本引擎按经典消隐算法工作：
    ///   1. 解析还原每个构件的真实几何——型钢截面沿轴线（或曲线路径）扫掠而成的棱柱，
    ///      得到真实的棱线（Edge）与面（Face）；板件/筒体按长方体或圆柱棱柱近似；
    ///   2. 沿视线方向（viewNormal 指向观察者）对每条棱线分段采样，
    ///      对每个采样点向观察者方向发射射线，与所有构件的面求交；
    ///   3. 命中前方构件的面 → 该处棱线为隐藏线（虚线），否则为可见线（实线）。
    ///
    /// 输出为视图平面坐标下的可见线段 / 隐藏线段，供出图分别绘制到
    /// LayerVisible（实线）与 LayerHidden（虚线）图层。
    /// </summary>
    internal static class HlrEngine
    {
        /// <summary>三维棱线（一段折线）。</summary>
        internal sealed class Edge3D
        {
            public List<Point3d> Points { get; set; } = new List<Point3d>();
            public bool Closed;
            /// <summary>相邻两个面在所属构件 Faces 列表中的下标。-1 表示没有。</summary>
            public int FaceA = -1;
            public int FaceB = -1;
        }

        /// <summary>视图平面上的可见 / 隐藏线段。</summary>
        internal sealed class Segment2D
        {
            public Point2d A;
            public Point2d B;
            public bool Visible;
            public int PartIndex;
        }

        /// <summary>一次消隐计算的结果。</summary>
        internal sealed class ViewResult
        {
            public readonly List<Segment2D> Segments = new List<Segment2D>();
            public double MinX = double.MaxValue;
            public double MaxX = double.MinValue;
            public double MinY = double.MaxValue;
            public double MaxY = double.MinValue;

            public bool HasGeometry => Segments.Count > 0;
        }

        /// <summary>解析得到的面（平面多边形），用于视线求交。</summary>
        private sealed class Face
        {
            public Point3d[] V;
            /// <summary>面法向（未归一化），满足 N·X = D。</summary>
            public Vector3d N;
            public double D;
            /// <summary>视图平面上的投影包围盒，用于快速排除。</summary>
            public double MinX, MaxX, MinY, MaxY;
            /// <summary>面顶点沿视线方向的最大深度。</summary>
            public double DepthMax;
            /// <summary>面内 2D 基底，用于点在多边形内的判定。</summary>
            public Point3d Origin;
            public Vector3d U;
            public Vector3d Vv;
        }

        private sealed class PartGeometry
        {
            public SolidMemberPart Part;
            public readonly List<Edge3D> Edges = new List<Edge3D>();
            public readonly List<Face> Faces = new List<Face>();
            public double MinX, MaxX, MinY, MaxY, DepthMax;
        }

        /// <summary>采样步长（mm）：棱线按此长度分段判定可见性。</summary>
        private const double SampleStep = 35.0;
        private const int MinSamplesPerEdge = 2;
        private const int MaxSamplesPerEdge = 80;
        /// <summary>射线起点容差（mm）：避免棱线自身所在面被判为遮挡。</summary>
        private const double RayEpsilon = 0.5;
        /// <summary>自身遮挡的最小厚度（mm）。小于此值视为贴着表面的擦边，不当成背面。</summary>
        private const double SelfOccludeMin = 3.0;
        /// <summary>投影重合判定（mm）。正投影后叠在一起的棱线合并成一条。</summary>
        private const double MergeTol = 2.5;
        /// <summary>二维包围盒容差（mm）。</summary>
        private const double BboxEpsilon = 0.5;
        /// <summary>短于该长度的线段不绘制。</summary>
        private const double MinSegmentLength = 0.05;
        /// <summary>
        /// 相邻面法向点积大于此值（夹角约 37° 以内）时，视为圆管或圆柱的光滑表面。
        /// 这类细分棱只保留轮廓线，避免 12 边形棱柱被画成一束平行线。
        /// </summary>
        private const double SmoothNormalDot = 0.80;
        /// <summary>扫掠路径转角余弦小于此值（约 23°）时，才画出中间截面环。</summary>
        private const double RingTurnDot = 0.92;

        /// <summary>
        /// 计算某视图下的可见线段与隐藏线段。
        /// </summary>
        /// <param name="parts">参与出图的构件（世界坐标）</param>
        /// <param name="proj">世界坐标 → 视图平面坐标</param>
        /// <param name="viewNormal">指向观察者的单位向量</param>
        /// <param name="simplifyTolerance">图面上应分开的最小距离（模型 mm，约 0.5mm×比例）。更近的平行线会合并。</param>
        public static ViewResult Compute(
            IList<SolidMemberPart> parts,
            Func<Point3d, Point2d> proj,
            Vector3d viewNormal,
            double simplifyTolerance = 0)
        {
            var result = new ViewResult();
            if (parts == null || parts.Count == 0 || proj == null) return result;

            Vector3d n = viewNormal.Length > 1e-9 ? viewNormal.GetNormal() : Vector3d.ZAxis;
            double simplify = simplifyTolerance > 0 ? simplifyTolerance : 0;

            var geoms = new List<PartGeometry>();
            foreach (SolidMemberPart part in parts)
            {
                if (part == null) continue;
                var geom = new PartGeometry { Part = part };
                try
                {
                    BuildGeometry(part, geom.Edges, geom.Faces);
                }
                catch
                {
                    continue;
                }
                if (geom.Edges.Count == 0) continue;
                geoms.Add(geom);
            }

            if (geoms.Count == 0) return result;

            // 视图平面包围盒（用于快速排除）
            foreach (PartGeometry geom in geoms)
            {
                geom.MinX = double.MaxValue;
                geom.MaxX = double.MinValue;
                geom.MinY = double.MaxValue;
                geom.MaxY = double.MinValue;
                geom.DepthMax = double.MinValue;
                foreach (Face face in geom.Faces)
                {
                    face.MinX = double.MaxValue;
                    face.MaxX = double.MinValue;
                    face.MinY = double.MaxValue;
                    face.MaxY = double.MinValue;
                    face.DepthMax = double.MinValue;
                    foreach (Point3d v in face.V)
                    {
                        Point2d p = proj(v);
                        if (p.X < face.MinX) face.MinX = p.X;
                        if (p.X > face.MaxX) face.MaxX = p.X;
                        if (p.Y < face.MinY) face.MinY = p.Y;
                        if (p.Y > face.MaxY) face.MaxY = p.Y;
                        double d = Dot(v, n);
                        if (d > face.DepthMax) face.DepthMax = d;
                        if (p.X < geom.MinX) geom.MinX = p.X;
                        if (p.X > geom.MaxX) geom.MaxX = p.X;
                        if (p.Y < geom.MinY) geom.MinY = p.Y;
                        if (p.Y > geom.MaxY) geom.MaxY = p.Y;
                        if (d > geom.DepthMax) geom.DepthMax = d;
                    }
                }
            }

            for (int gi = 0; gi < geoms.Count; gi++)
            {
                PartGeometry self = geoms[gi];
                foreach (Edge3D edge in self.Edges)
                {
                    // 两个相邻面都背对观察者：这是实体背面的棱，不画。
                    // 朝向观察者或处于轮廓上的棱才保留。不能靠“射线打到自身”判断，
                    // 那会把贴着表面的可见棱误判成被挡住，轴测图就会缺线、断线。
                    if (!ShouldDrawEdge(edge, self.Faces, n)) continue;

                    int count = edge.Points.Count;
                    if (count < 2) continue;
                    int segCount = edge.Closed ? count : count - 1;

                    for (int si = 0; si < segCount; si++)
                    {
                        Point3d p0 = edge.Points[si];
                        Point3d p1 = edge.Points[(si + 1) % count];
                        Vector3d d3 = p1 - p0;
                        double len3d = d3.Length;
                        if (len3d < MinSegmentLength) continue;

                        Point2d a = proj(p0);
                        Point2d b = proj(p1);
                        if ((a - b).Length < MinSegmentLength) continue;

                        EmitByOcclusion(result, p0, p1, gi, geoms, n, proj, 0);
                    }
                }
            }

            CleanupSegments(result, simplify);
            return result;
        }

        /// <summary>点与向量的点积（AutoCAD 的 Point3d 没有 DotProduct）。</summary>
        private static double Dot(Vector3d v, Point3d p) => v.X * p.X + v.Y * p.Y + v.Z * p.Z;
        private static double Dot(Point3d p, Vector3d v) => v.X * p.X + v.Y * p.Y + v.Z * p.Z;
        private static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>
        /// 按遮挡自适应切开棱线。端点和中点一致就整段输出；
        /// 只有穿过前面杆件时才继续二分。这样桁架腹杆被弦杆挡住的那一段会变成虚线，
        /// 而不会被“短遮挡忽略”抹掉。
        /// </summary>
        private static void EmitByOcclusion(
            ViewResult result,
            Point3d p0,
            Point3d p1,
            int selfIndex,
            List<PartGeometry> geoms,
            Vector3d n,
            Func<Point3d, Point2d> proj,
            int depth)
        {
            Point2d a = proj(p0);
            Point2d b = proj(p1);
            if ((a - b).Length < MinSegmentLength) return;

            bool h0 = OccludedByOther(p0, a, selfIndex, geoms, n);
            bool h1 = OccludedByOther(p1, b, selfIndex, geoms, n);
            Point3d mid3 = p0 + (p1 - p0) * 0.5;
            Point2d mid2 = proj(mid3);
            bool hm = OccludedByOther(mid3, mid2, selfIndex, geoms, n);
            double len3d = (p1 - p0).Length;

            if ((h0 == h1 && h1 == hm) || depth >= 8 || len3d < 16.0)
            {
                if (h0 == h1)
                {
                    AddSegment(result, a, b, !h0, selfIndex);
                }
                else
                {
                    AddSegment(result, a, mid2, !h0, selfIndex);
                    AddSegment(result, mid2, b, !h1, selfIndex);
                }
                return;
            }

            EmitByOcclusion(result, p0, mid3, selfIndex, geoms, n, proj, depth + 1);
            EmitByOcclusion(result, mid3, p1, selfIndex, geoms, n, proj, depth + 1);
        }

        private static void AddSegment(ViewResult result, Point2d a, Point2d b, bool visible, int partIndex)
        {
            if ((a - b).Length < MinSegmentLength) return;
            result.Segments.Add(new Segment2D { A = a, B = b, Visible = visible, PartIndex = partIndex });
            result.MinX = Math.Min(result.MinX, Math.Min(a.X, b.X));
            result.MaxX = Math.Max(result.MaxX, Math.Max(a.X, b.X));
            result.MinY = Math.Min(result.MinY, Math.Min(a.Y, b.Y));
            result.MaxY = Math.Max(result.MaxY, Math.Max(a.Y, b.Y));
        }

        /// <summary>
        /// 棱线是否要画。背面棱丢掉。圆管、圆柱这类光滑曲面上的细分棱只留轮廓
        /// （一面朝向观察者、另一面背向），其余实棱仍保留。
        /// </summary>
        private static bool ShouldDrawEdge(Edge3D edge, List<Face> faces, Vector3d view)
        {
            if (edge.FaceA < 0 && edge.FaceB < 0) return true;
            const double eps = 1e-4;
            double da = FaceViewDot(faces, edge.FaceA, view);
            double db = FaceViewDot(faces, edge.FaceB, view);
            bool frontA = da > eps;
            bool frontB = db > eps;
            if (!frontA && !frontB) return false;
            if (edge.FaceA < 0 || edge.FaceB < 0 || edge.FaceA >= faces.Count || edge.FaceB >= faces.Count)
                return true;

            double nd = Dot(faces[edge.FaceA].N, faces[edge.FaceB].N);
            if (nd <= SmoothNormalDot) return true;

            bool backA = da < -eps;
            bool backB = db < -eps;
            if ((frontA && backB) || (frontB && backA)) return true;
            bool grazA = Math.Abs(da) <= eps;
            bool grazB = Math.Abs(db) <= eps;
            return (grazA && (frontB || backB)) || (grazB && (frontA || backA));
        }

        private static double FaceViewDot(List<Face> faces, int index, Vector3d view)
        {
            if (index < 0 || index >= faces.Count) return -1;
            return view.DotProduct(faces[index].N);
        }

        /// <summary>把很短的可见/隐藏切换并回相邻状态，避免数值噪声切出碎线。</summary>
        private static void SmoothHiddenRuns(bool[] hidden, double length)
        {
            if (hidden.Length < 3) return;
            double step = length / Math.Max(1, hidden.Length - 1);
            int minRun = Math.Max(1, (int)Math.Ceiling(40.0 / Math.Max(step, 1.0)));
            int i = 0;
            while (i < hidden.Length)
            {
                int j = i;
                while (j < hidden.Length && hidden[j] == hidden[i]) j++;
                int run = j - i;
                if (run < minRun && i > 0 && j < hidden.Length)
                {
                    bool fill = hidden[i - 1];
                    for (int k = i; k < j; k++) hidden[k] = fill;
                }
                i = j;
            }
        }

        /// <summary>
        /// 是否被其它构件挡住。不检测自身：自身背面已经由面朝向剔除。
        /// </summary>
        private static bool OccludedByOther(
            Point3d sp,
            Point2d sp2,
            int selfIndex,
            List<PartGeometry> geoms,
            Vector3d n)
        {
            double myDepth = Dot(sp, n);
            for (int gi = 0; gi < geoms.Count; gi++)
            {
                if (gi == selfIndex) continue;
                PartGeometry other = geoms[gi];
                if (other.DepthMax <= myDepth - RayEpsilon) continue;
                if (sp2.X < other.MinX - BboxEpsilon || sp2.X > other.MaxX + BboxEpsilon ||
                    sp2.Y < other.MinY - BboxEpsilon || sp2.Y > other.MaxY + BboxEpsilon) continue;

                List<Face> faces = other.Faces;
                for (int fi = 0; fi < faces.Count; fi++)
                {
                    Face face = faces[fi];
                    // 不要求面朝向观察者。射线先打到的是杆件靠近自己的那一面，
                    // 它的法向是背对观察者的；只测朝向面就会整根桁架都“看穿”。
                    if (face.DepthMax <= myDepth - RayEpsilon) continue;
                    if (sp2.X < face.MinX - BboxEpsilon || sp2.X > face.MaxX + BboxEpsilon ||
                        sp2.Y < face.MinY - BboxEpsilon || sp2.Y > face.MaxY + BboxEpsilon) continue;

                    double denom = n.DotProduct(face.N);
                    if (Math.Abs(denom) < 1e-8) continue;
                    double t = (face.D - Dot(face.N, sp)) / denom;
                    if (t <= 1.0) continue;
                    if (PointInFace(face, sp + n * t)) return true;
                }
            }
            return false;
        }

        private static int ClassifyPoint(
            Point3d sp,
            Point2d sp2,
            int selfIndex,
            List<PartGeometry> geoms,
            Vector3d n)
        {
            double myDepth = Dot(sp, n);
            double bestOther = double.MaxValue;
            double bestSelf = double.MaxValue;

            for (int gi = 0; gi < geoms.Count; gi++)
            {
                bool same = gi == selfIndex;
                List<Face> faces = geoms[gi].Faces;
                for (int fi = 0; fi < faces.Count; fi++)
                {
                    Face face = faces[fi];
                    if (face.DepthMax <= myDepth - RayEpsilon) continue;
                    if (sp2.X < face.MinX - BboxEpsilon || sp2.X > face.MaxX + BboxEpsilon ||
                        sp2.Y < face.MinY - BboxEpsilon || sp2.Y > face.MaxY + BboxEpsilon) continue;

                    double denom = n.DotProduct(face.N);
                    if (Math.Abs(denom) < 1e-9) continue;

                    double t = (face.D - Dot(face.N, sp)) / denom;
                    if (t <= RayEpsilon) continue;
                    if (same && t < SelfOccludeMin) continue;

                    Point3d hit = sp + n * t;
                    if (!PointInFace(face, hit)) continue;

                    if (same)
                    {
                        if (t < bestSelf) bestSelf = t;
                    }
                    else if (t < bestOther)
                    {
                        bestOther = t;
                    }
                }
            }

            if (bestSelf < bestOther) return -1;
            if (bestOther < double.MaxValue) return 0;
            return 1;
        }

        /// <summary>
        /// 去掉过短碎线、重合线和同一直线上被拆开的小段。
        /// 截面每个顶点都会拉出一条纵向棱，正投影后大量重合；
        /// 圆管轮廓、翼缘厚度线靠得比图面分辨距离更近时也并成一条。
        /// 隐藏线用更大的合并距离，避免被板挡住的杆件画出一排虚线。
        /// </summary>
        private static void CleanupSegments(ViewResult result, double simplifyTolerance)
        {
            if (result.Segments.Count == 0) return;

            double visTol = Math.Max(MergeTol, simplifyTolerance);
            double hidTol = Math.Max(visTol, simplifyTolerance * 2.0);
            double minHidden = Math.Max(8.0, simplifyTolerance * 0.8);
            double collinearTol = Math.Max(1.5, simplifyTolerance * 0.08);

            var visible = new List<Segment2D>();
            var hidden = new List<Segment2D>();
            foreach (Segment2D seg in result.Segments)
            {
                if (SegLength(seg) < 2.0) continue;
                if (seg.Visible) visible.Add(seg);
                else hidden.Add(seg);
            }

            visible = WeldCollinear(visible, collinearTol);
            hidden = WeldCollinear(hidden, collinearTol);

            var kept = new List<Segment2D>();
            AbsorbNearDuplicates(visible, kept, visTol, 2.0);
            AbsorbNearDuplicates(hidden, kept, hidTol, minHidden);

            var vis2 = new List<Segment2D>();
            var hid2 = new List<Segment2D>();
            foreach (Segment2D seg in kept)
            {
                if (seg.Visible) vis2.Add(seg);
                else hid2.Add(seg);
            }

            result.Segments.Clear();
            result.MinX = double.MaxValue;
            result.MaxX = double.MinValue;
            result.MinY = double.MaxValue;
            result.MaxY = double.MinValue;
            AppendClean(result, WeldCollinear(vis2, collinearTol), 2.0);
            AppendClean(result, WeldCollinear(hid2, collinearTol), minHidden);
        }

        private static void AppendClean(ViewResult result, List<Segment2D> segs, double minLen)
        {
            foreach (Segment2D seg in segs)
            {
                if (SegLength(seg) < minLen) continue;
                result.Segments.Add(seg);
                result.MinX = Math.Min(result.MinX, Math.Min(seg.A.X, seg.B.X));
                result.MaxX = Math.Max(result.MaxX, Math.Max(seg.A.X, seg.B.X));
                result.MinY = Math.Min(result.MinY, Math.Min(seg.A.Y, seg.B.Y));
                result.MaxY = Math.Max(result.MaxY, Math.Max(seg.A.Y, seg.B.Y));
            }
        }

        private static void AbsorbNearDuplicates(List<Segment2D> source, List<Segment2D> kept, double tol, double minLen)
        {
            source.Sort((a, b) => SegLength(b).CompareTo(SegLength(a)));
            foreach (Segment2D seg in source)
            {
                if (SegLength(seg) < minLen) continue;
                bool covered = false;
                for (int i = 0; i < kept.Count; i++)
                {
                    if (!NearlyCovered(seg, kept[i], tol)) continue;
                    if (seg.Visible) kept[i].Visible = true;
                    covered = true;
                    break;
                }
                if (!covered) kept.Add(seg);
            }
        }

        private static List<Segment2D> WeldCollinear(List<Segment2D> segs, double tol)
        {
            var used = new bool[segs.Count];
            var result = new List<Segment2D>();
            for (int i = 0; i < segs.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                Point2d a = segs[i].A;
                Point2d b = segs[i].B;
                bool visible = segs[i].Visible;
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    for (int j = 0; j < segs.Count; j++)
                    {
                        if (used[j] || segs[j].Visible != visible) continue;
                        if (!TryMergeCollinear(ref a, ref b, segs[j].A, segs[j].B, tol)) continue;
                        used[j] = true;
                        grew = true;
                    }
                }
                result.Add(new Segment2D { A = a, B = b, Visible = visible, PartIndex = segs[i].PartIndex });
            }
            return result;
        }

        /// <summary>
        /// 共线、并且重叠或间隙不超过 tol 时并成一段。
        /// 垂直距离必须很小，相距一个翼缘厚度的平行线不会在这里被并掉。
        /// </summary>
        private static bool TryMergeCollinear(ref Point2d a, ref Point2d b, Point2d c, Point2d d, double tol)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) return false;
            double ux = dx / len;
            double uy = dy / len;
            if (Math.Abs(ux * (c.Y - a.Y) - uy * (c.X - a.X)) > tol) return false;
            if (Math.Abs(ux * (d.Y - a.Y) - uy * (d.X - a.X)) > tol) return false;

            double tc = (c.X - a.X) * ux + (c.Y - a.Y) * uy;
            double td = (d.X - a.X) * ux + (d.Y - a.Y) * uy;
            double c0 = Math.Min(tc, td);
            double c1 = Math.Max(tc, td);
            if (c1 < -tol || c0 > len + tol) return false;

            double n0 = Math.Min(0.0, c0);
            double n1 = Math.Max(len, c1);
            Point2d na = new Point2d(a.X + ux * n0, a.Y + uy * n0);
            Point2d nb = new Point2d(a.X + ux * n1, a.Y + uy * n1);
            a = na;
            b = nb;
            return true;
        }

        private static bool NearlyCovered(Segment2D seg, Segment2D cover, double tol)
        {
            return DistPointToSeg(seg.A, cover.A, cover.B) <= tol
                && DistPointToSeg(seg.B, cover.A, cover.B) <= tol;
        }

        private static double DistPointToSeg(Point2d p, Point2d a, Point2d b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-8) return Dist(p, a);
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            return Dist(p, new Point2d(a.X + t * dx, a.Y + t * dy));
        }

        private static double SegLength(Segment2D s) => Dist(s.A, s.B);

        private static double Dist(Point2d a, Point2d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>判断点是否落在面多边形内部（偶奇规则，面内 2D 基底投影）。</summary>
        private static bool PointInFace(Face face, Point3d pt)
        {
            Vector3d rel = pt - face.Origin;
            double a = rel.DotProduct(face.U);
            double b = rel.DotProduct(face.Vv);

            int count = face.V.Length;
            if (count < 3) return false;
            bool inside = false;
            Vector3d last = face.V[count - 1] - face.Origin;
            double prevA = last.DotProduct(face.U);
            double prevB = last.DotProduct(face.Vv);
            for (int i = 0; i < count; i++)
            {
                Vector3d r = face.V[i] - face.Origin;
                double curA = r.DotProduct(face.U);
                double curB = r.DotProduct(face.Vv);

                if ((prevB > b) != (curB > b))
                {
                    double xCross = (curA - prevA) * (b - prevB) / (curB - prevB) + prevA;
                    if (a <= xCross) inside = !inside;
                }
                prevA = curA;
                prevB = curB;
            }
            return inside;
        }

        // ==================== 几何解析 ====================

        private static void BuildGeometry(SolidMemberPart part, List<Edge3D> edges, List<Face> faces)
        {
            bool profileBased = part.Section != null && !part.IsPlate;
            if (profileBased)
            {
                List<Point2d> outline = SectionSolidBuilder.GetProfileOuterPoints(part.Section);
                if (outline != null && outline.Count >= 3)
                {
                    bool built = false;
                    if (part.PathPoints != null && part.PathPoints.Count >= 2)
                    {
                        BuildSweptPrism(part, outline, edges, faces);
                        built = true;
                    }
                    else
                    {
                        Vector3d axis = part.EndPt - part.StartPt;
                        if (axis.Length > 1e-6)
                        {
                            BuildStraightPrism(part.StartPt, part.EndPt, outline, edges, faces);
                            built = true;
                        }
                    }
                    if (built && edges.Count > 0) return;
                }
            }

            BuildBoxGeometry(part, edges, faces);
        }

        /// <summary>直杆棱柱：两端截面轮廓 + 纵向棱线 + 侧棱面 + 两端端面。</summary>
        private static void BuildStraightPrism(
            Point3d p0,
            Point3d p1,
            List<Point2d> outline,
            List<Edge3D> edges,
            List<Face> faces)
        {
            Vector3d axis = p1 - p0;
            SectionSolidBuilder.GetMemberAxes(axis, out Vector3d xDir, out Vector3d yDir, out _);

            int m = outline.Count;
            var ringA = new Point3d[m];
            var ringB = new Point3d[m];
            for (int i = 0; i < m; i++)
            {
                ringA[i] = p0 + xDir * outline[i].X + yDir * outline[i].Y;
                ringB[i] = p1 + xDir * outline[i].X + yDir * outline[i].Y;
            }

            EmitPrism(edges, faces, new[] { ringA, ringB });
        }

        /// <summary>曲线路径扫掠棱柱：逐段棱台 + 纵向棱线 + 端面。</summary>
        private static void BuildSweptPrism(
            SolidMemberPart part,
            List<Point2d> outline,
            List<Edge3D> edges,
            List<Face> faces)
        {
            IList<Point3d> path = part.PathPoints;
            int m = outline.Count;
            int rings = path.Count;

            var ringPts = new Point3d[rings][];
            for (int i = 0; i < rings; i++)
            {
                Vector3d tangent = i < rings - 1 ? path[i + 1] - path[i] : path[i] - path[i - 1];
                if (tangent.Length < 1e-6) tangent = part.EndPt - part.StartPt;
                SectionSolidBuilder.GetMemberAxes(tangent, out Vector3d xDir, out Vector3d yDir, out _);
                var ring = new Point3d[m];
                for (int k = 0; k < m; k++)
                {
                    ring[k] = path[i] + xDir * outline[k].X + yDir * outline[k].Y;
                }
                ringPts[i] = ring;
            }

            EmitPrism(edges, faces, ringPts);
        }

        private enum Axis { X, Y, Z }

        /// <summary>
        /// 板件 / 筒体等无截面轮廓的构件：按包围盒（长方体）或按轴向判定的圆柱棱柱近似。
        /// </summary>
        private static void BuildBoxGeometry(SolidMemberPart part, List<Edge3D> edges, List<Face> faces)
        {
            Extents3d ext;
            try
            {
                ext = part.Solid.GeometricExtents;
            }
            catch
            {
                return;
            }

            double dx = Math.Max(ext.MaxPoint.X - ext.MinPoint.X, 1.0);
            double dy = Math.Max(ext.MaxPoint.Y - ext.MinPoint.Y, 1.0);
            double dz = Math.Max(ext.MaxPoint.Z - ext.MinPoint.Z, 1.0);
            double maxDim = Math.Max(dx, Math.Max(dy, dz));

            bool cylZ = Math.Abs(dx - dy) <= 0.04 * maxDim && dz <= 1.05 * maxDim && dz >= 0.15 * maxDim;
            bool cylX = Math.Abs(dy - dz) <= 0.04 * maxDim && dx >= 1.05 * Math.Max(dy, dz);
            bool cylY = Math.Abs(dx - dz) <= 0.04 * maxDim && dy >= 1.05 * Math.Max(dx, dz);

            if (cylZ) BuildCylinderPrism(edges, faces, ext, Axis.Z);
            else if (cylX) BuildCylinderPrism(edges, faces, ext, Axis.X);
            else if (cylY) BuildCylinderPrism(edges, faces, ext, Axis.Y);
            else BuildBoxShape(part, edges, faces);
        }

        /// <summary>长方体近似（板件、连接板、加劲肋等）。</summary>
        private static void BuildBoxShape(SolidMemberPart part, List<Edge3D> edges, List<Face> faces)
        {
            Extents3d ext = part.Solid.GeometricExtents;
            Point3d a = ext.MinPoint;
            Point3d b = ext.MaxPoint;
            var c = new[]
            {
                new Point3d(a.X, a.Y, a.Z), new Point3d(b.X, a.Y, a.Z),
                new Point3d(b.X, b.Y, a.Z), new Point3d(a.X, b.Y, a.Z),
                new Point3d(a.X, a.Y, b.Z), new Point3d(b.X, a.Y, b.Z),
                new Point3d(b.X, b.Y, b.Z), new Point3d(a.X, b.Y, b.Z)
            };
            int f = faces.Count;
            // 法向一律朝外：底 -Z、顶 +Z、前 -Y、右 +X、后 +Y、左 -X
            AddFace(faces, new[] { c[0], c[3], c[2], c[1] });
            AddFace(faces, new[] { c[4], c[5], c[6], c[7] });
            AddFace(faces, new[] { c[0], c[1], c[5], c[4] });
            AddFace(faces, new[] { c[1], c[2], c[6], c[5] });
            AddFace(faces, new[] { c[2], c[3], c[7], c[6] });
            AddFace(faces, new[] { c[3], c[0], c[4], c[7] });

            int[,] boxEdges =
            {
                { 0, 1, 0, 2 }, { 1, 2, 0, 3 }, { 2, 3, 0, 4 }, { 3, 0, 0, 5 },
                { 4, 5, 1, 2 }, { 5, 6, 1, 3 }, { 6, 7, 1, 4 }, { 7, 4, 1, 5 },
                { 0, 4, 5, 2 }, { 1, 5, 2, 3 }, { 2, 6, 3, 4 }, { 3, 7, 4, 5 }
            };
            for (int i = 0; i < boxEdges.GetLength(0); i++)
            {
                edges.Add(new Edge3D
                {
                    Points = new List<Point3d> { c[boxEdges[i, 0]], c[boxEdges[i, 1]] },
                    FaceA = f + boxEdges[i, 2],
                    FaceB = f + boxEdges[i, 3]
                });
            }
        }

        /// <summary>圆柱棱柱近似（裙座、筒体等），16 边形逼近。</summary>
        private static void BuildCylinderPrism(List<Edge3D> edges, List<Face> faces, Extents3d ext, Axis axis)
        {
            const int seg = 16;
            Point3d lo = ext.MinPoint;
            Point3d hi = ext.MaxPoint;
            var bottom = new Point3d[seg];
            var top = new Point3d[seg];

            if (axis == Axis.Z)
            {
                double r = Math.Min(hi.X - lo.X, hi.Y - lo.Y) * 0.5;
                double cx = (lo.X + hi.X) * 0.5;
                double cy = (lo.Y + hi.Y) * 0.5;
                for (int i = 0; i < seg; i++)
                {
                    double ang = 2.0 * Math.PI * i / seg;
                    bottom[i] = new Point3d(cx + r * Math.Cos(ang), cy + r * Math.Sin(ang), lo.Z);
                    top[i] = new Point3d(cx + r * Math.Cos(ang), cy + r * Math.Sin(ang), hi.Z);
                }
            }
            else if (axis == Axis.X)
            {
                double r = Math.Min(hi.Y - lo.Y, hi.Z - lo.Z) * 0.5;
                double cy = (lo.Y + hi.Y) * 0.5;
                double cz = (lo.Z + hi.Z) * 0.5;
                for (int i = 0; i < seg; i++)
                {
                    double ang = 2.0 * Math.PI * i / seg;
                    bottom[i] = new Point3d(lo.X, cy + r * Math.Cos(ang), cz + r * Math.Sin(ang));
                    top[i] = new Point3d(hi.X, cy + r * Math.Cos(ang), cz + r * Math.Sin(ang));
                }
            }
            else
            {
                double r = Math.Min(hi.X - lo.X, hi.Z - lo.Z) * 0.5;
                double cx = (lo.X + hi.X) * 0.5;
                double cz = (lo.Z + hi.Z) * 0.5;
                for (int i = 0; i < seg; i++)
                {
                    double ang = 2.0 * Math.PI * i / seg;
                    bottom[i] = new Point3d(cx + r * Math.Cos(ang), lo.Y, cz + r * Math.Sin(ang));
                    top[i] = new Point3d(cx + r * Math.Cos(ang), hi.Y, cz + r * Math.Sin(ang));
                }
            }

            AppendPrism(edges, faces, bottom, top);
        }

        private static void AppendPrism(List<Edge3D> edges, List<Face> faces, Point3d[] bottom, Point3d[] top)
        {
            EmitPrism(edges, faces, new[] { bottom, top });
        }

        /// <summary>
        /// 由沿路径的截面环生成棱柱：侧面法向朝外，两端盖板法向朝外，并为每条棱记下相邻面。
        /// </summary>
        private static void EmitPrism(List<Edge3D> edges, List<Face> faces, Point3d[][] rings)
        {
            int nR = rings.Length;
            if (nR < 2) return;
            int m = rings[0].Length;
            if (m < 3) return;

            int sideBase = faces.Count;
            int segCount = nR - 1;
            for (int s = 0; s < segCount; s++)
            {
                for (int k = 0; k < m; k++)
                {
                    int k2 = (k + 1) % m;
                    AddFace(faces, new[] { rings[s][k], rings[s][k2], rings[s + 1][k2], rings[s + 1][k] });
                }
            }

            int cap0 = faces.Count;
            var startCap = new Point3d[m];
            for (int k = 0; k < m; k++) startCap[k] = rings[0][m - 1 - k];
            AddFace(faces, startCap);
            int cap1 = faces.Count;
            AddFace(faces, rings[nR - 1]);

            for (int s = 0; s < segCount; s++)
            {
                for (int k = 0; k < m; k++)
                {
                    edges.Add(new Edge3D
                    {
                        Points = new List<Point3d> { rings[s][k], rings[s + 1][k] },
                        FaceA = sideBase + s * m + (k - 1 + m) % m,
                        FaceB = sideBase + s * m + k
                    });
                }
            }

            for (int s = 0; s < nR; s++)
            {
                if (!ShouldEmitCrossSection(rings, s)) continue;
                for (int k = 0; k < m; k++)
                {
                    int k2 = (k + 1) % m;
                    int left;
                    int right;
                    if (s == 0)
                    {
                        left = sideBase + k;
                        right = cap0;
                    }
                    else if (s == nR - 1)
                    {
                        left = sideBase + (segCount - 1) * m + k;
                        right = cap1;
                    }
                    else
                    {
                        left = sideBase + (s - 1) * m + k;
                        right = sideBase + s * m + k;
                    }
                    edges.Add(new Edge3D
                    {
                        Points = new List<Point3d> { rings[s][k], rings[s][k2] },
                        FaceA = left,
                        FaceB = right
                    });
                }
            }
        }


        /// <summary>
        /// 直杆和缓弯扫掠只画两端截面。中间截面会把圆管和螺旋构件切成一串片子。
        /// 折角大约超过 23° 时仍保留该环，用来表达栏杆、折梁的转角。
        /// </summary>
        private static bool ShouldEmitCrossSection(Point3d[][] rings, int s)
        {
            int n = rings.Length;
            if (s <= 0 || s >= n - 1) return true;
            Point3d c0 = RingCentroid(rings[s - 1]);
            Point3d c1 = RingCentroid(rings[s]);
            Point3d c2 = RingCentroid(rings[s + 1]);
            Vector3d incoming = c1 - c0;
            Vector3d outgoing = c2 - c1;
            if (incoming.Length < 1e-6 || outgoing.Length < 1e-6) return false;
            return incoming.GetNormal().DotProduct(outgoing.GetNormal()) < RingTurnDot;
        }

        private static Point3d RingCentroid(Point3d[] ring)
        {
            double x = 0, y = 0, z = 0;
            int m = ring.Length;
            if (m == 0) return Point3d.Origin;
            for (int k = 0; k < m; k++)
            {
                x += ring[k].X;
                y += ring[k].Y;
                z += ring[k].Z;
            }
            double inv = 1.0 / m;
            return new Point3d(x * inv, y * inv, z * inv);
        }

        private static void AddFace(List<Face> faces, Point3d[] verts)
        {
            if (verts == null || verts.Length < 3) return;

            Vector3d nrm = new Vector3d(0, 0, 0);
            for (int i = 1; i + 1 < verts.Length; i++)
            {
                nrm = (verts[i] - verts[0]).CrossProduct(verts[i + 1] - verts[0]);
                if (nrm.Length > 1e-9) break;
            }
            if (nrm.Length < 1e-9) return;

            Vector3d unit = nrm.GetNormal();
            var face = new Face
            {
                V = verts,
                N = unit,
                D = Dot(unit, verts[0]),
                Origin = verts[0]
            };

            Vector3d u = verts[1] - verts[0];
            if (u.Length < 1e-9) u = verts[2] - verts[0];
            face.U = u.GetNormal();
            face.Vv = unit.CrossProduct(face.U).GetNormal();
            faces.Add(face);
        }
    }
}

