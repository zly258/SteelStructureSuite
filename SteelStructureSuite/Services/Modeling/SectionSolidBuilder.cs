using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Services.Modeling
{
    /// <summary>
    /// 型钢实体构建器：按截面外轮廓生成单个封闭面域，再沿杆件轴线一次挤出（CreateExtrudedSolid），
    /// 不再用腹板 + 翼缘多个箱体布尔拼接，实体面更干净，HLR 投影也更准确。
    /// </summary>
    public static class SectionSolidBuilder
    {
        public static Solid3d BuildVerticalMember(SectionProfile sec, double length)
        {
            double len = Math.Max(20.0, length);

            try
            {
                using (Region region = CreateProfileRegion(sec))
                {
                    if (region != null)
                    {
                        Solid3d solid = new Solid3d();
                        solid.SetDatabaseDefaults();
                        solid.CreateExtrudedSolid(region, new Vector3d(0, 0, len), new SweepOptions());
                        if (solid != null)
                        {
                            return solid;
                        }
                    }
                }
            }
            catch
            {
                // 面域挤出失败时退回箱体组合，保证任何截面都能生成实体
            }

            return BuildFallbackMember(sec, len);
        }

        public static Solid3d CreateMemberSolid(SectionProfile sec, Point3d p1, Point3d p2) => BuildMemberBetweenPoints(sec, p1, p2);

        public static Solid3d BuildMemberBetweenPoints(SectionProfile sec, Point3d p1, Point3d p2)
        {
            Vector3d vec = p2 - p1;
            double length = vec.Length;
            if (length < 10.0) length = 10.0;

            Solid3d member = BuildVerticalMember(sec, length);

            GetMemberAxes(vec, out Vector3d xDir, out Vector3d yDir, out Vector3d zDir);

            Matrix3d alignMatrix = Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                p1, xDir, yDir, zDir);

            member.TransformBy(alignMatrix);
            return member;
        }

        /// <summary>
        /// 计算杆件局部坐标系：截面高度方向 (X) 尽量保持竖直，截面宽度方向 (Y) 保持水平。
        /// 避免水平梁的 H/B 方向被旋转 90°。
        /// </summary>
        public static void GetMemberAxes(Vector3d axis, out Vector3d xDir, out Vector3d yDir, out Vector3d zDir)
        {
            zDir = axis.GetNormal();

            if (Math.Abs(zDir.Z) > 0.95)
            {
                // 竖向柱/立管：截面 X 沿世界 X，Y 由右手法则确定（支持向下绘制的柱）
                xDir = Vector3d.XAxis;
                yDir = zDir.CrossProduct(xDir).GetNormal();
                return;
            }

            // 斜/水平杆件：X 为世界 Z 在垂直于轴线平面内的投影（保持截面高度竖直）
            Vector3d vertical = Vector3d.ZAxis - zDir * zDir.DotProduct(Vector3d.ZAxis);
            if (vertical.Length < 1e-6)
            {
                xDir = Vector3d.XAxis;
                yDir = zDir.CrossProduct(xDir).GetNormal();
                return;
            }

            xDir = vertical.GetNormal();
            yDir = zDir.CrossProduct(xDir).GetNormal();
        }

        /// <summary>
        /// 创建截面封闭面域（XY 平面，Z=0）。型钢外轮廓一次围合，箱型/圆管用内轮廓挖孔。
        /// </summary>
        private static Region CreateProfileRegion(SectionProfile sec)
        {
            if (sec == null) return null;

            double h = Math.Max(30.0, sec.H);
            double b = Math.Max(30.0, sec.B);
            double tw = Math.Max(3.0, Math.Min(sec.Tw, Math.Min(h, b) * 0.35));
            double tf = Math.Max(4.0, Math.Min(sec.Tf, h * 0.35));

            switch (sec.Shape)
            {
                case SectionShapeType.HBeam:
                {
                    double hx = h * 0.5;
                    double by = b * 0.5;
                    double halfWeb = tw * 0.5;
                    var pts = new List<Point2d>
                    {
                        new Point2d(-hx, -by),
                        new Point2d(-hx, by),
                        new Point2d(-hx + tf, by),
                        new Point2d(-hx + tf, halfWeb),
                        new Point2d(hx - tf, halfWeb),
                        new Point2d(hx - tf, by),
                        new Point2d(hx, by),
                        new Point2d(hx, -by),
                        new Point2d(hx - tf, -by),
                        new Point2d(hx - tf, -halfWeb),
                        new Point2d(-hx + tf, -halfWeb),
                        new Point2d(-hx + tf, -by)
                    };
                    return CreateRegionFromPolygon(pts);
                }

                case SectionShapeType.Channel:
                {
                    double hx = h * 0.5;
                    double by = b * 0.5;
                    var pts = new List<Point2d>
                    {
                        new Point2d(-hx, -by),
                        new Point2d(hx, -by),
                        new Point2d(hx, by),
                        new Point2d(hx - tf, by),
                        new Point2d(hx - tf, -by + tw),
                        new Point2d(-hx + tf, -by + tw),
                        new Point2d(-hx + tf, by),
                        new Point2d(-hx, by)
                    };
                    return CreateRegionFromPolygon(pts);
                }

                case SectionShapeType.Angle:
                {
                    double hx = h * 0.5;
                    double by = b * 0.5;
                    var pts = new List<Point2d>
                    {
                        new Point2d(-hx, -by),
                        new Point2d(hx, -by),
                        new Point2d(hx, -by + tw),
                        new Point2d(-hx + tw, -by + tw),
                        new Point2d(-hx + tw, by),
                        new Point2d(-hx, by)
                    };
                    return CreateRegionFromPolygon(pts);
                }

                case SectionShapeType.RectTube:
                {
                    double hx = h * 0.5;
                    double by = b * 0.5;
                    double inH = Math.Max(6.0, h - 2.0 * tw);
                    double inB = Math.Max(6.0, b - 2.0 * tw);
                    var outer = new List<Point2d>
                    {
                        new Point2d(-hx, -by),
                        new Point2d(hx, -by),
                        new Point2d(hx, by),
                        new Point2d(-hx, by)
                    };
                    var inner = new List<Point2d>
                    {
                        new Point2d(-inH * 0.5, -inB * 0.5),
                        new Point2d(inH * 0.5, -inB * 0.5),
                        new Point2d(inH * 0.5, inB * 0.5),
                        new Point2d(-inH * 0.5, inB * 0.5)
                    };
                    return CreateRingRegion(outer, inner);
                }

                case SectionShapeType.PipeTube:
                {
                    double rOut = h * 0.5;
                    double rIn = Math.Max(3.0, rOut - tw);
                    return CreateCircleRingRegion(rOut, rIn);
                }

                default:
                {
                    double hx = h * 0.5;
                    double by = b * 0.5;
                    return CreateRegionFromPolygon(new List<Point2d>
                    {
                        new Point2d(-hx, -by),
                        new Point2d(hx, -by),
                        new Point2d(hx, by),
                        new Point2d(-hx, by)
                    });
                }
            }
        }

        /// <summary>
        /// 返回截面外轮廓点（局部 X=截面高度方向，Y=截面宽度方向），供出图轮廓投影使用。
        /// </summary>
        public static List<Point2d> GetProfileOuterPoints(SectionProfile sec)
        {
            if (sec == null) return new List<Point2d>();

            double h = Math.Max(30.0, sec.H);
            double b = Math.Max(30.0, sec.B);
            double tw = Math.Max(3.0, Math.Min(sec.Tw, Math.Min(h, b) * 0.35));
            double tf = Math.Max(4.0, Math.Min(sec.Tf, h * 0.35));
            double hx = h * 0.5, by = b * 0.5;

            switch (sec.Shape)
            {
                case SectionShapeType.HBeam:
                    return new List<Point2d>
                    {
                        new Point2d(-hx, -by), new Point2d(-hx, by),
                        new Point2d(-hx + tf, by), new Point2d(-hx + tf, tw * 0.5),
                        new Point2d(hx - tf, tw * 0.5), new Point2d(hx - tf, by),
                        new Point2d(hx, by), new Point2d(hx, -by),
                        new Point2d(hx - tf, -by), new Point2d(hx - tf, -tw * 0.5),
                        new Point2d(-hx + tf, -tw * 0.5), new Point2d(-hx + tf, -by)
                    };

                case SectionShapeType.Channel:
                    return new List<Point2d>
                    {
                        new Point2d(-hx, -by), new Point2d(hx, -by),
                        new Point2d(hx, by), new Point2d(hx - tf, by),
                        new Point2d(hx - tf, -by + tw), new Point2d(-hx + tf, -by + tw),
                        new Point2d(-hx + tf, by), new Point2d(-hx, by)
                    };

                case SectionShapeType.Angle:
                    return new List<Point2d>
                    {
                        new Point2d(-hx, -by), new Point2d(hx, -by),
                        new Point2d(hx, -by + tw), new Point2d(-hx + tw, -by + tw),
                        new Point2d(-hx + tw, by), new Point2d(-hx, by)
                    };

                case SectionShapeType.PipeTube:
                {
                    double r = h * 0.5;
                    var pts = new List<Point2d>();
                    for (int i = 0; i < 16; i++)
                    {
                        double a = 2.0 * Math.PI * i / 16.0;
                        pts.Add(new Point2d(r * Math.Cos(a), r * Math.Sin(a)));
                    }
                    return pts;
                }

                default:
                    return new List<Point2d>
                    {
                        new Point2d(-hx, -by), new Point2d(hx, -by),
                        new Point2d(hx, by), new Point2d(-hx, by)
                    };
            }
        }

        private static Region CreateRegionFromPolygon(IList<Point2d> pts)
        {
            if (pts == null || pts.Count < 3) return null;
            return CreateRegionFromCurves(new List<Curve> { CreateClosedPolyline(pts) });
        }

        private static Region CreateRingRegion(IList<Point2d> outerPts, IList<Point2d> innerPts)
        {
            Region outer = CreateRegionFromPolygon(outerPts);
            Region inner = CreateRegionFromPolygon(innerPts);
            if (outer == null) return null;
            if (inner == null) return outer;

            try
            {
                outer.BooleanOperation(BooleanOperationType.BoolSubtract, inner);
            }
            catch
            {
                // 内轮廓无效时保留外轮廓实体
            }
            finally
            {
                inner.Dispose();
            }
            return outer;
        }

        private static Region CreateCircleRingRegion(double rOuter, double rInner)
        {
            Region outer = CreateRegionFromCurves(new List<Curve> { CreateCircle(rOuter) });
            Region inner = CreateRegionFromCurves(new List<Curve> { CreateCircle(rInner) });
            if (outer == null) return null;
            if (inner == null) return outer;

            try
            {
                outer.BooleanOperation(BooleanOperationType.BoolSubtract, inner);
            }
            catch
            {
                // 壁厚过小时退化为实心圆管
            }
            finally
            {
                inner.Dispose();
            }
            return outer;
        }

        private static Polyline CreateClosedPolyline(IList<Point2d> pts)
        {
            Polyline pl = new Polyline(pts.Count);
            pl.SetDatabaseDefaults();
            pl.Normal = Vector3d.ZAxis;
            for (int i = 0; i < pts.Count; i++)
            {
                pl.AddVertexAt(i, pts[i], 0.0, 0.0, 0.0);
            }
            pl.Closed = true;
            return pl;
        }

        private static Circle CreateCircle(double radius)
        {
            Circle circle = new Circle(Point3d.Origin, Vector3d.ZAxis, Math.Max(1.0, radius));
            circle.SetDatabaseDefaults();
            return circle;
        }

        private static Region CreateRegionFromCurves(List<Curve> curves)
        {
            if (curves == null || curves.Count == 0) return null;

            DBObjectCollection input = new DBObjectCollection();
            foreach (Curve c in curves) input.Add(c);

            DBObjectCollection output = null;
            try
            {
                output = Region.CreateFromCurves(input);
                Region region = null;
                if (output != null && output.Count > 0)
                {
                    region = output[0] as Region;
                    for (int i = 1; i < output.Count; i++) output[i]?.Dispose();
                }
                return region;
            }
            catch
            {
                return null;
            }
            finally
            {
                foreach (Curve c in curves) c?.Dispose();
                if (output != null)
                {
                    foreach (DBObject obj in output)
                    {
                        if (!(obj is Region)) obj?.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// 面域挤出不可用时的兜底实现：腹板 + 翼缘箱体组合。
        /// </summary>
        private static Solid3d BuildFallbackMember(SectionProfile sec, double len)
        {
            double h = Math.Max(30.0, sec.H);
            double b = Math.Max(30.0, sec.B);
            double tw = Math.Max(3.0, Math.Min(sec.Tw, Math.Min(h, b) * 0.35));
            double tf = Math.Max(4.0, Math.Min(sec.Tf, h * 0.35));
            double halfZ = len * 0.5;

            switch (sec.Shape)
            {
                case SectionShapeType.HBeam:
                {
                    double webH = Math.Max(8.0, h - 2.0 * tf);
                    double flangeOffsetX = (h - tf) * 0.5;

                    Solid3d web = CreateOrientedBox(webH, tw, len, new Vector3d(0, 0, halfZ));
                    using (Solid3d f1 = CreateOrientedBox(tf, b, len, new Vector3d(-flangeOffsetX, 0, halfZ)))
                    using (Solid3d f2 = CreateOrientedBox(tf, b, len, new Vector3d(flangeOffsetX, 0, halfZ)))
                    {
                        web.BooleanOperation(BooleanOperationType.BoolUnite, f1);
                        web.BooleanOperation(BooleanOperationType.BoolUnite, f2);
                    }
                    return web;
                }

                case SectionShapeType.Channel:
                {
                    double webH = Math.Max(8.0, h - 2.0 * tf);
                    double flangeOffsetX = (h - tf) * 0.5;
                    double webOffsetY = -(b - tw) * 0.5;

                    Solid3d web = CreateOrientedBox(webH, tw, len, new Vector3d(0, webOffsetY, halfZ));
                    using (Solid3d f1 = CreateOrientedBox(tf, b, len, new Vector3d(-flangeOffsetX, 0, halfZ)))
                    using (Solid3d f2 = CreateOrientedBox(tf, b, len, new Vector3d(flangeOffsetX, 0, halfZ)))
                    {
                        web.BooleanOperation(BooleanOperationType.BoolUnite, f1);
                        web.BooleanOperation(BooleanOperationType.BoolUnite, f2);
                    }
                    return web;
                }

                case SectionShapeType.Angle:
                {
                    Solid3d leg1 = CreateOrientedBox(h, tw, len, new Vector3d(0, -(b - tw) * 0.5, halfZ));
                    double leg2Width = Math.Max(6.0, b - tw);
                    using (Solid3d leg2 = CreateOrientedBox(tw, leg2Width, len, new Vector3d(-(h - tw) * 0.5, tw * 0.5, halfZ)))
                    {
                        leg1.BooleanOperation(BooleanOperationType.BoolUnite, leg2);
                    }
                    return leg1;
                }

                case SectionShapeType.RectTube:
                {
                    Solid3d outerBox = CreateOrientedBox(h, b, len, new Vector3d(0, 0, halfZ));
                    double inH = h - 2.0 * tw;
                    double inB = b - 2.0 * tw;
                    if (inH > 6.0 && inB > 6.0)
                    {
                        using (Solid3d innerCore = CreateOrientedBox(inH, inB, len + 4.0, new Vector3d(0, 0, halfZ)))
                        {
                            outerBox.BooleanOperation(BooleanOperationType.BoolSubtract, innerCore);
                        }
                    }
                    return outerBox;
                }

                case SectionShapeType.PipeTube:
                {
                    double rOut = h * 0.5;
                    double rIn = rOut - tw;

                    Solid3d outerCyl = new Solid3d();
                    outerCyl.SetDatabaseDefaults();
                    outerCyl.CreateFrustum(len, rOut, rOut, rOut);
                    outerCyl.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, halfZ)));

                    if (rIn > 3.0)
                    {
                        using (Solid3d innerCyl = new Solid3d())
                        {
                            innerCyl.SetDatabaseDefaults();
                            innerCyl.CreateFrustum(len + 4.0, rIn, rIn, rIn);
                            innerCyl.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, halfZ)));
                            outerCyl.BooleanOperation(BooleanOperationType.BoolSubtract, innerCyl);
                        }
                    }
                    return outerCyl;
                }

                default:
                    return CreateOrientedBox(h, b, len, new Vector3d(0, 0, halfZ));
            }
        }

        private static Solid3d CreateOrientedBox(double sizeX, double sizeY, double sizeZ, Vector3d offset)
        {
            Solid3d box = new Solid3d();
            box.SetDatabaseDefaults();
            box.CreateBox(sizeX, sizeY, sizeZ);
            box.TransformBy(Matrix3d.Displacement(offset));
            return box;
        }

        /// <summary>
        /// 沿路径一次扫略截面。smooth=true 用样条（螺旋、弧形），否则用三维多段线（折点处自动斜接）。
        /// 扫略失败时返回 null，由调用方退回分段挤出。
        /// </summary>
        public static Solid3d SweepAlongPath(SectionProfile sec, IList<Point3d> path, bool smooth, bool closed)
        {
            if (sec == null || path == null || path.Count < 2) return null;

            if (path.Count == 2 && !closed)
            {
                return BuildMemberBetweenPoints(sec, path[0], path[1]);
            }

            Region region = null;
            Curve curve = null;
            Solid3d solid = null;
            try
            {
                region = CreateProfileRegion(sec);
                if (region == null) return null;

                Point3dCollection col = new Point3dCollection();
                foreach (Point3d p in path) col.Add(p);

                if (!smooth || closed)
                {
                    curve = new Polyline3d(Poly3dType.SimplePoly, col, closed);
                }
                else
                {
                    int order = Math.Min(4, path.Count);
                    curve = new Spline(col, order, 0.0);
                }

                solid = new Solid3d();
                solid.SetDatabaseDefaults();

                SweepOptionsBuilder sweep = new SweepOptionsBuilder
                {
                    Align = SweepOptionsAlignOption.AlignSweepEntityToPath,
                    Bank = smooth,
                    BasePoint = Point3d.Origin,
                    ScaleFactor = 1.0,
                    TwistAngle = 0.0,
                    CheckIntersections = false
                };
                solid.CreateSweptSolid(region, curve, sweep.ToSweepOptions());
                return solid;
            }
            catch
            {
                solid?.Dispose();
                return null;
            }
            finally
            {
                region?.Dispose();
                curve?.Dispose();
            }
        }
    }
}
