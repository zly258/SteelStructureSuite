using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Services.Modeling
{
    /// <summary>
    /// 极速型钢三维实体构建器：
    /// 1. 圆管、圆钢直接调用 ACIS 原生 CreateFrustum 原语。
    /// 2. 方矩管、焊接箱型、扁钢、方钢直接调用 ACIS 原生 CreateBox 原语。
    /// 3. H/I/T/C/Z/L/十字/卷边/帽型/双拼等截面采用精确轮廓面域挤出 + 线程级模板实体克隆缓存。
    /// </summary>
    public static class SectionSolidBuilder
    {
        [ThreadStatic]
        private static Dictionary<string, Solid3d> _templateCache;

        public static void ClearTemplateCache()
        {
            if (_templateCache == null) return;
            foreach (var kv in _templateCache)
            {
                try { kv.Value?.Dispose(); } catch { }
            }
            _templateCache.Clear();
        }

        public static Solid3d BuildVerticalMember(SectionProfile sec, double length)
        {
            double len = Math.Max(20.0, length);
            if (sec == null)
            {
                return CreateOrientedBox(100.0, 100.0, len, new Vector3d(0, 0, len * 0.5));
            }

            // 1. 圆钢管 / 实心圆钢：直接使用 ACIS 原生圆柱原语
            if (sec.Shape == SectionShapeType.PipeTube || sec.Shape == SectionShapeType.RoundBar)
            {
                double rOut = Math.Max(3.0, sec.H * 0.5);
                Solid3d cyl = new Solid3d();
                cyl.SetDatabaseDefaults();
                cyl.CreateFrustum(len, rOut, rOut, rOut);
                cyl.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, len * 0.5)));
                return cyl;
            }

            // 2. 方管 / 矩形管 / 焊接箱型 / 扁钢 / 方钢：直接使用 ACIS 原生箱体原语
            if (sec.Shape == SectionShapeType.RectTube ||
                sec.Shape == SectionShapeType.SquareTube ||
                sec.Shape == SectionShapeType.WeldedBox ||
                sec.Shape == SectionShapeType.FlatBar ||
                sec.Shape == SectionShapeType.SquareBar)
            {
                double h = Math.Max(4.0, sec.H);
                double b = (sec.Shape == SectionShapeType.SquareTube || sec.Shape == SectionShapeType.SquareBar)
                    ? h
                    : Math.Max(4.0, sec.B);
                return CreateOrientedBox(h, b, len, new Vector3d(0, 0, len * 0.5));
            }

            // 3. 其余异形/开口/双拼型钢：检查线程级模板缓存，命中时直接 Clone()
            if (_templateCache == null)
                _templateCache = new Dictionary<string, Solid3d>(StringComparer.Ordinal);

            double qLen = Math.Round(len, 1);
            string cacheKey = $"{(int)sec.Shape}_{sec.H:0.#}_{sec.B:0.#}_{sec.Tw:0.#}_{sec.Tf:0.#}_{sec.B2:0.#}_{sec.Tf2:0.#}_{sec.Lip:0.#}_{sec.Gap:0.#}_{qLen:0.0}";
            if (_templateCache.TryGetValue(cacheKey, out Solid3d cached) && cached != null && !cached.IsDisposed)
            {
                try
                {
                    if (cached.Clone() is Solid3d cloned)
                        return cloned;
                }
                catch { }
            }

            Solid3d built = BuildExtrudedOrFallbackMember(sec, qLen);
            if (built != null && _templateCache.Count < 128)
            {
                try
                {
                    if (built.Clone() is Solid3d cachedCopy)
                        _templateCache[cacheKey] = cachedCopy;
                }
                catch { }
            }

            return built;
        }

        private static Solid3d BuildExtrudedOrFallbackMember(SectionProfile sec, double len)
        {
            try
            {
                var polygons = GetProfilePolygons(sec);
                if (polygons != null && polygons.Count > 0)
                {
                    Solid3d combined = null;
                    foreach (var pts in polygons)
                    {
                        using (Region region = CreateRegionFromPolygon(pts))
                        {
                            if (region == null) continue;
                            Solid3d part = new Solid3d();
                            part.SetDatabaseDefaults();
                            part.CreateExtrudedSolid(region, new Vector3d(0, 0, len), new SweepOptions());
                            if (combined == null)
                            {
                                combined = part;
                            }
                            else
                            {
                                combined.BooleanOperation(BooleanOperationType.BoolUnite, part);
                                part.Dispose();
                            }
                        }
                    }

                    if (combined != null) return combined;
                }

                using (Region region = CreateProfileRegion(sec))
                {
                    if (region != null)
                    {
                        Solid3d solid = new Solid3d();
                        solid.SetDatabaseDefaults();
                        solid.CreateExtrudedSolid(region, new Vector3d(0, 0, len), new SweepOptions());
                        return solid;
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
                xDir = Vector3d.XAxis;
                yDir = zDir.CrossProduct(xDir).GetNormal();
                return;
            }

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
        /// 创建截面封闭面域（XY 平面，Z=0），供直杆挤出与曲线扫掠使用。
        /// </summary>
        internal static Region CreateProfileRegion(SectionProfile sec)
        {
            if (sec == null) return null;

            if (sec.Shape == SectionShapeType.PipeTube || sec.Shape == SectionShapeType.RoundBar)
            {
                double rOut = Math.Max(3.0, sec.H * 0.5);
                return CreateRegionFromCurves(new List<Curve> { CreateCircle(rOut) });
            }

            var polygons = GetProfilePolygons(sec);
            if (polygons == null || polygons.Count == 0) return null;

            Region combined = null;
            foreach (var pts in polygons)
            {
                Region reg = CreateRegionFromPolygon(pts);
                if (reg == null) continue;
                if (combined == null)
                {
                    combined = reg;
                }
                else
                {
                    try
                    {
                        combined.BooleanOperation(BooleanOperationType.BoolUnite, reg);
                    }
                    catch { }
                    reg.Dispose();
                }
            }
            return combined;
        }

        /// <summary>
        /// 返回截面外轮廓点（局部 X=截面高度方向，Y=截面宽度方向），供出图轮廓投影使用。
        /// </summary>
        public static List<Point2d> GetProfileOuterPoints(SectionProfile sec)
        {
            if (sec == null) return new List<Point2d>();

            if (sec.Shape == SectionShapeType.PipeTube || sec.Shape == SectionShapeType.RoundBar)
            {
                double r = Math.Max(3.0, sec.H * 0.5);
                var pts = new List<Point2d>(12);
                for (int i = 0; i < 12; i++)
                {
                    double a = 2.0 * Math.PI * i / 12.0;
                    pts.Add(new Point2d(r * Math.Cos(a), r * Math.Sin(a)));
                }
                return pts;
            }

            var polygons = GetProfilePolygons(sec);
            if (polygons != null && polygons.Count == 1)
                return polygons[0];

            // 双拼截面取外包络矩形以简化出图投影
            double h = Math.Max(10.0, sec.H);
            double b = Math.Max(10.0, sec.B);
            double gap = Math.Max(0.0, sec.Gap);
            double totalB = (sec.Shape == SectionShapeType.DoubleAngle || sec.Shape == SectionShapeType.DoubleChannel)
                ? 2.0 * b + gap
                : b;
            double hx = h * 0.5, by = totalB * 0.5;
            return new List<Point2d>
            {
                new Point2d(-hx, -by), new Point2d(hx, -by),
                new Point2d(hx, by), new Point2d(-hx, by)
            };
        }

        /// <summary>
        /// 获取截面的一个或多个封闭多边形轮廓（局部坐标：X 为高度方向，Y 为宽度方向）
        /// </summary>
        internal static List<List<Point2d>> GetProfilePolygons(SectionProfile sec)
        {
            if (sec == null) return new List<List<Point2d>>();

            double h = Math.Max(6.0, sec.H);
            double b = (sec.Shape == SectionShapeType.SquareTube || sec.Shape == SectionShapeType.SquareBar)
                ? h
                : Math.Max(4.0, sec.B);
            double tw = Math.Max(1.0, Math.Min(sec.Tw > 0 ? sec.Tw : 4.0, Math.Min(h, b) * 0.42));
            double tf = Math.Max(1.0, Math.Min(sec.Tf > 0 ? sec.Tf : tw, h * 0.42));
            double hx = h * 0.5;
            double by = b * 0.5;

            switch (sec.Shape)
            {
                case SectionShapeType.HBeam:
                case SectionShapeType.IBeam:
                case SectionShapeType.WeldedHBeam:
                {
                    double halfWeb = tw * 0.5;
                    return SinglePoly(new List<Point2d>
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
                    });
                }

                case SectionShapeType.WeldedHBeamUnequal:
                {
                    double b2 = sec.B2 > 0 ? Math.Max(10.0, sec.B2) : b;
                    double tf2 = Math.Max(1.0, Math.Min(sec.Tf2 > 0 ? sec.Tf2 : tf, h * 0.42));
                    double by1 = b * 0.5;   // 上翼缘 (+hx)
                    double by2 = b2 * 0.5;  // 下翼缘 (-hx)
                    double halfWeb = Math.Min(tw * 0.5, Math.Min(by1, by2) * 0.8);
                    return SinglePoly(new List<Point2d>
                    {
                        new Point2d(-hx, -by2),
                        new Point2d(-hx, by2),
                        new Point2d(-hx + tf2, by2),
                        new Point2d(-hx + tf2, halfWeb),
                        new Point2d(hx - tf, halfWeb),
                        new Point2d(hx - tf, by1),
                        new Point2d(hx, by1),
                        new Point2d(hx, -by1),
                        new Point2d(hx - tf, -by1),
                        new Point2d(hx - tf, -halfWeb),
                        new Point2d(-hx + tf2, -halfWeb),
                        new Point2d(-hx + tf2, -by2)
                    });
                }

                case SectionShapeType.TBeam:
                case SectionShapeType.WeldedTBeam:
                {
                    double halfWeb = tw * 0.5;
                    return SinglePoly(new List<Point2d>
                    {
                        new Point2d(hx, -by),
                        new Point2d(hx, by),
                        new Point2d(hx - tf, by),
                        new Point2d(hx - tf, halfWeb),
                        new Point2d(-hx, halfWeb),
                        new Point2d(-hx, -halfWeb),
                        new Point2d(hx - tf, -halfWeb),
                        new Point2d(hx - tf, -by)
                    });
                }

                case SectionShapeType.WeldedCross:
                {
                    // 十字截面：两根正交对称 H 型钢（高度 H、翼缘宽 B、腹板厚 Tw、翼缘厚 Tf）
                    double halfWeb = tw * 0.5;
                    double bf = Math.Min(b * 0.5, hx - tf - 1.0);
                    if (bf <= halfWeb) bf = halfWeb + 2.0;
                    return SinglePoly(new List<Point2d>
                    {
                        // 顶翼缘 (+X)
                        new Point2d(hx, -bf),
                        new Point2d(hx, bf),
                        new Point2d(hx - tf, bf),
                        new Point2d(hx - tf, halfWeb),
                        // 右腹板到右翼缘 (+Y)
                        new Point2d(halfWeb, halfWeb),
                        new Point2d(halfWeb, hx - tf),
                        new Point2d(bf, hx - tf),
                        new Point2d(bf, hx),
                        new Point2d(-bf, hx),
                        new Point2d(-bf, hx - tf),
                        new Point2d(-halfWeb, hx - tf),
                        // 下腹板到底翼缘 (-X)
                        new Point2d(-halfWeb, halfWeb),
                        new Point2d(-hx + tf, halfWeb),
                        new Point2d(-hx + tf, bf),
                        new Point2d(-hx, bf),
                        new Point2d(-hx, -bf),
                        new Point2d(-hx + tf, -bf),
                        new Point2d(-hx + tf, -halfWeb),
                        // 左腹板到左翼缘 (-Y)
                        new Point2d(-halfWeb, -halfWeb),
                        new Point2d(-halfWeb, -hx + tf),
                        new Point2d(-bf, -hx + tf),
                        new Point2d(-bf, -hx),
                        new Point2d(bf, -hx),
                        new Point2d(bf, -hx + tf),
                        new Point2d(halfWeb, -hx + tf),
                        new Point2d(halfWeb, -halfWeb),
                        new Point2d(hx - tf, -halfWeb),
                        new Point2d(hx - tf, -bf)
                    });
                }

                case SectionShapeType.Channel:
                case SectionShapeType.ColdFormedU:
                {
                    return SinglePoly(new List<Point2d>
                    {
                        new Point2d(-hx, -by),
                        new Point2d(hx, -by),
                        new Point2d(hx, by),
                        new Point2d(hx - tf, by),
                        new Point2d(hx - tf, -by + tw),
                        new Point2d(-hx + tf, -by + tw),
                        new Point2d(-hx + tf, by),
                        new Point2d(-hx, by)
                    });
                }

                case SectionShapeType.Angle:
                case SectionShapeType.UnequalAngle:
                {
                    return SinglePoly(new List<Point2d>
                    {
                        new Point2d(-hx, -by),
                        new Point2d(hx, -by),
                        new Point2d(hx, -by + tw),
                        new Point2d(-hx + tw, -by + tw),
                        new Point2d(-hx + tw, by),
                        new Point2d(-hx, by)
                    });
                }

                case SectionShapeType.DoubleAngle:
                {
                    // 背靠背双角钢（T形拼装，中间留 Gap 节点板间隙）
                    double halfGap = Math.Max(0.0, sec.Gap) * 0.5;
                    var rightAngle = new List<Point2d>
                    {
                        new Point2d(-hx, halfGap),
                        new Point2d(hx, halfGap),
                        new Point2d(hx, halfGap + tw),
                        new Point2d(-hx + tw, halfGap + tw),
                        new Point2d(-hx + tw, halfGap + b),
                        new Point2d(-hx, halfGap + b)
                    };
                    var leftAngle = new List<Point2d>
                    {
                        new Point2d(-hx, -halfGap - b),
                        new Point2d(-hx + tw, -halfGap - b),
                        new Point2d(-hx + tw, -halfGap - tw),
                        new Point2d(hx, -halfGap - tw),
                        new Point2d(hx, -halfGap),
                        new Point2d(-hx, -halfGap)
                    };
                    return new List<List<Point2d>> { leftAngle, rightAngle };
                }

                case SectionShapeType.DoubleChannel:
                {
                    // 背靠背双槽钢（中间留 Gap 间隙）
                    double halfGap = Math.Max(0.0, sec.Gap) * 0.5;
                    var rightChan = new List<Point2d>
                    {
                        new Point2d(-hx, halfGap),
                        new Point2d(hx, halfGap),
                        new Point2d(hx, halfGap + b),
                        new Point2d(hx - tf, halfGap + b),
                        new Point2d(hx - tf, halfGap + tw),
                        new Point2d(-hx + tf, halfGap + tw),
                        new Point2d(-hx + tf, halfGap + b),
                        new Point2d(-hx, halfGap + b)
                    };
                    var leftChan = new List<Point2d>
                    {
                        new Point2d(-hx, -halfGap - b),
                        new Point2d(-hx + tf, -halfGap - b),
                        new Point2d(-hx + tf, -halfGap - tw),
                        new Point2d(hx - tf, -halfGap - tw),
                        new Point2d(hx - tf, -halfGap - b),
                        new Point2d(hx, -halfGap - b),
                        new Point2d(hx, -halfGap),
                        new Point2d(-hx, -halfGap)
                    };
                    return new List<List<Point2d>> { leftChan, rightChan };
                }

                case SectionShapeType.ColdFormedC:
                {
                    double t = Math.Max(1.0, Math.Min(tw, Math.Min(h, b) * 0.25));
                    double lip = Math.Max(t * 2.0, Math.Min(sec.Lip > 0 ? sec.Lip : 15.0, hx - t * 1.5));
                    return SinglePoly(new List<Point2d>
                    {
                        new Point2d(-hx, -by),
                        new Point2d(hx, -by),
                        new Point2d(hx, by),
                        new Point2d(hx - lip, by),
                        new Point2d(hx - lip, by - t),
                        new Point2d(hx - t, by - t),
                        new Point2d(hx - t, -by + t),
                        new Point2d(-hx + t, -by + t),
                        new Point2d(-hx + t, by - t),
                        new Point2d(-hx + lip, by - t),
                        new Point2d(-hx + lip, by),
                        new Point2d(-hx, by)
                    });
                }

                case SectionShapeType.ZBeam:
                {
                    double halfWeb = tw * 0.5;
                    double flangeOut = Math.Max(tw + 2.0, b);
                    return SinglePoly(new List<Point2d>
                    {
                        new Point2d(-hx, -flangeOut + halfWeb),
                        new Point2d(-hx, halfWeb),
                        new Point2d(hx - tf, halfWeb),
                        new Point2d(hx - tf, flangeOut - halfWeb),
                        new Point2d(hx, flangeOut - halfWeb),
                        new Point2d(hx, -halfWeb),
                        new Point2d(-hx + tf, -halfWeb),
                        new Point2d(-hx + tf, -flangeOut + halfWeb)
                    });
                }

                case SectionShapeType.ColdFormedZ:
                {
                    double t = Math.Max(1.0, Math.Min(tw, Math.Min(h, b) * 0.25));
                    double halfT = t * 0.5;
                    double lip = Math.Max(t * 2.0, Math.Min(sec.Lip > 0 ? sec.Lip : 15.0, hx - t * 1.5));
                    double flangeOut = Math.Max(t * 3.0, b);
                    return SinglePoly(new List<Point2d>
                    {
                        new Point2d(-hx + lip, -flangeOut + halfT),
                        new Point2d(-hx, -flangeOut + halfT),
                        new Point2d(-hx, halfT),
                        new Point2d(hx - t, halfT),
                        new Point2d(hx - t, flangeOut - halfT - t),
                        new Point2d(hx - lip, flangeOut - halfT - t),
                        new Point2d(hx - lip, flangeOut - halfT),
                        new Point2d(hx, flangeOut - halfT),
                        new Point2d(hx, -halfT),
                        new Point2d(-hx + t, -halfT),
                        new Point2d(-hx + t, -flangeOut + halfT + t),
                        new Point2d(-hx + lip, -flangeOut + halfT + t)
                    });
                }

                case SectionShapeType.HatSection:
                {
                    // 帽型钢：顶宽 B (+hx)，两腹板向下高 H (-hx)，底部两侧外翻边 Lip
                    double t = Math.Max(1.0, Math.Min(tw, Math.Min(h, b) * 0.25));
                    double lip = Math.Max(t * 2.0, sec.Lip > 0 ? sec.Lip : 20.0);
                    double innerBy = Math.Max(t, by - t);
                    return SinglePoly(new List<Point2d>
                    {
                        new Point2d(-hx, -by - lip),
                        new Point2d(-hx + t, -by - lip),
                        new Point2d(-hx + t, -by),
                        new Point2d(hx, -by),
                        new Point2d(hx, by),
                        new Point2d(-hx + t, by),
                        new Point2d(-hx + t, by + lip),
                        new Point2d(-hx, by + lip),
                        new Point2d(-hx, innerBy),
                        new Point2d(hx - t, innerBy),
                        new Point2d(hx - t, -innerBy),
                        new Point2d(-hx, -innerBy)
                    });
                }

                default:
                {
                    return SinglePoly(new List<Point2d>
                    {
                        new Point2d(-hx, -by),
                        new Point2d(hx, -by),
                        new Point2d(hx, by),
                        new Point2d(-hx, by)
                    });
                }
            }
        }

        private static List<List<Point2d>> SinglePoly(List<Point2d> pts)
        {
            return new List<List<Point2d>> { pts };
        }

        private static Region CreateRegionFromPolygon(IList<Point2d> pts)
        {
            if (pts == null || pts.Count < 3) return null;
            return CreateRegionFromCurves(new List<Curve> { CreateClosedPolyline(pts) });
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

        private static Solid3d BuildFallbackMember(SectionProfile sec, double len)
        {
            double h = Math.Max(10.0, sec.H);
            double b = Math.Max(10.0, sec.B);
            double tw = Math.Max(2.0, Math.Min(sec.Tw > 0 ? sec.Tw : 4.0, Math.Min(h, b) * 0.35));
            double tf = Math.Max(2.0, Math.Min(sec.Tf > 0 ? sec.Tf : tw, h * 0.35));
            double halfZ = len * 0.5;

            switch (sec.Shape)
            {
                case SectionShapeType.HBeam:
                case SectionShapeType.IBeam:
                case SectionShapeType.WeldedHBeam:
                {
                    double webH = Math.Max(6.0, h - 2.0 * tf);
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

                case SectionShapeType.TBeam:
                case SectionShapeType.WeldedTBeam:
                {
                    double webH = Math.Max(6.0, h - tf);
                    Solid3d flange = CreateOrientedBox(tf, b, len, new Vector3d((h - tf) * 0.5, 0, halfZ));
                    using (Solid3d web = CreateOrientedBox(webH, tw, len, new Vector3d(-tf * 0.5, 0, halfZ)))
                    {
                        flange.BooleanOperation(BooleanOperationType.BoolUnite, web);
                    }
                    return flange;
                }

                case SectionShapeType.Channel:
                case SectionShapeType.ColdFormedU:
                {
                    double webH = Math.Max(6.0, h - 2.0 * tf);
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
                case SectionShapeType.UnequalAngle:
                {
                    Solid3d leg1 = CreateOrientedBox(h, tw, len, new Vector3d(0, -(b - tw) * 0.5, halfZ));
                    double leg2Width = Math.Max(4.0, b - tw);
                    using (Solid3d leg2 = CreateOrientedBox(tw, leg2Width, len, new Vector3d(-(h - tw) * 0.5, tw * 0.5, halfZ)))
                    {
                        leg1.BooleanOperation(BooleanOperationType.BoolUnite, leg2);
                    }
                    return leg1;
                }

                case SectionShapeType.PipeTube:
                case SectionShapeType.RoundBar:
                {
                    double rOut = h * 0.5;
                    Solid3d outerCyl = new Solid3d();
                    outerCyl.SetDatabaseDefaults();
                    outerCyl.CreateFrustum(len, rOut, rOut, rOut);
                    outerCyl.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, halfZ)));
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

        public static Solid3d SweepAlongPath(SectionProfile sec, IList<Point3d> path, bool smooth, bool closed)
        {
            if (sec == null || path == null || path.Count < 2) return null;
            if (!closed && path.Count == 2)
                return BuildMemberBetweenPoints(sec, path[0], path[1]);
            return CurveSweepService.SweepAlongPath(sec, path, smooth, closed);
        }
    }
}
