using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Services.Modeling
{
    public static class CurveSweepService
    {
        public static string LastSweepError { get; private set; }

        private sealed class PathFit
        {
            public Curve Curve;
            public Point3d Origin;
            public Vector3d XAxis;
            public Vector3d ZAxis;
            public double Radius;
            public double TopRadius;
            public double SweepDegrees;
            public double Height;
            public double Turns;
            public bool FullCircle;
            public bool Helix;
            public bool CounterClockwise = true;
        }

        public static Solid3d SweepAlongPath(SectionProfile sec, IList<Point3d> path, bool smooth, bool closed)
        {
            if (sec == null || path == null || path.Count < 2) return null;
            List<Point3d> pts = Clean(path, closed);
            if (pts.Count < 2) return null;
            if (!closed && pts.Count == 2)
                return SectionSolidBuilder.BuildMemberBetweenPoints(sec, pts[0], pts[1]);

            PathFit fit = TryFit(pts, closed);
            if (fit != null && fit.Curve != null)
            {
                Solid3d swept = SweepProfileAlongCurve(sec, fit.Curve);
                DisposeCurve(fit.Curve);
                if (swept != null) return swept;
            }

            if (smooth && !closed && pts.Count >= 4)
            {
                Curve spline = CreateOpenSpline(pts);
                if (spline != null)
                {
                    try
                    {
                        Solid3d swept = SweepProfileAlongCurve(sec, spline);
                        if (swept != null) return swept;
                    }
                    finally
                    {
                        DisposeCurve(spline);
                    }
                }
            }
            return null;
        }

        public static Solid3d SweepProfileAlongCurve(SectionProfile sec, Curve path)
        {
            LastSweepError = null;
            if (sec == null || path == null) return null;
            Curve pathClone = null;
            Region profile = null;
            Solid3d dbSolid = null;
            try
            {
                pathClone = path.Clone() as Curve;
                profile = SectionSolidBuilder.CreateProfileRegion(sec);
                if (pathClone == null || profile == null) return null;

                Database db = HostApplicationServices.WorkingDatabase;
                if (db == null) return null;
                pathClone.SetDatabaseDefaults();
                profile.SetDatabaseDefaults();
                AlignProfileToPathStart(profile, pathClone);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    ms.AppendEntity(profile);
                    tr.AddNewlyCreatedDBObject(profile, true);
                    ms.AppendEntity(pathClone);
                    tr.AddNewlyCreatedDBObject(pathClone, true);

                    dbSolid = new Solid3d();
                    dbSolid.SetDatabaseDefaults();
                    ms.AppendEntity(dbSolid);
                    tr.AddNewlyCreatedDBObject(dbSolid, true);

                    SweepOptionsBuilder options = new SweepOptionsBuilder();
                    options.Align = SweepOptionsAlignOption.AlignSweepEntityToPath;
                    options.AlignStart = true;
                    options.Bank = false;
                    options.BasePoint = Point3d.Origin;
                    options.CheckIntersections = false;
                    options.ScaleFactor = 1.0;
                    options.TwistAngle = 0.0;
                    options.DraftAngle = 0.0;
                    dbSolid.CreateSweptSolid(profile, pathClone, options.ToSweepOptions());
                    Solid3d result = dbSolid.Clone() as Solid3d;
                    dbSolid.Erase();
                    profile.Erase();
                    pathClone.Erase();
                    tr.Commit();
                    dbSolid.Dispose();
                    profile.Dispose();
                    pathClone.Dispose();
                    dbSolid = null;
                    profile = null;
                    pathClone = null;
                    return result;
                }
            }
            catch (System.Exception ex)
            {
                LastSweepError = ex.Message;
                return null;
            }
            finally
            {
                DisposeCurve(pathClone);
                try { if (profile != null && !profile.IsDisposed) profile.Dispose(); } catch { }
                try { if (dbSolid != null && !dbSolid.IsDisposed) dbSolid.Dispose(); } catch { }
            }
        }

        public static Curve CreateArcMemberPath(double radius, double sweepDeg, double endRise)
        {
            double sweep = Math.Min(360.0, Math.Max(1.0, sweepDeg)) * Math.PI / 180.0;
            double r = Math.Max(radius, 1.0);
            if (Math.Abs(endRise) >= 1.0)
            {
                double turns = Math.Max(sweep / (Math.PI * 2.0), 0.05);
                Helix helix = CreateVerticalHelix(r, r, Math.Abs(endRise), turns, true);
                if (helix != null && endRise < 0.0)
                    helix.TransformBy(Matrix3d.Mirroring(new Plane(Point3d.Origin, Vector3d.ZAxis)));
                return helix;
            }
            if (sweep >= Math.PI * 2.0 - 0.02)
            {
                Circle circle = new Circle(Point3d.Origin, Vector3d.ZAxis, r);
                circle.SetDatabaseDefaults();
                return circle;
            }
            Arc arc = new Arc(Point3d.Origin, r, 0.0, sweep);
            arc.SetDatabaseDefaults();
            return arc;
        }

        public static Helix CreateVerticalHelix(double baseRadius, double topRadius, double height, double turns, bool counterClockwise)
        {
            if (turns < 0.05 || height < 1.0) return null;
            Helix helix = TryBuildHelix(baseRadius, topRadius, height, turns, counterClockwise);
            if (helix == null) return null;
            if (GoesCounterClockwise(helix, turns) != counterClockwise)
            {
                DisposeCurve(helix);
                helix = TryBuildHelix(baseRadius, topRadius, height, turns, !counterClockwise);
            }
            return helix;
        }

        public static List<Point3d> SampleCurve(Curve curve, int count)
        {
            var pts = new List<Point3d>();
            if (curve == null) return pts;
            int n = Math.Max(2, count);
            double start;
            double end;
            try
            {
                start = curve.StartParam;
                end = curve.EndParam;
            }
            catch
            {
                return pts;
            }
            for (int i = 0; i <= n; i++)
            {
                double t = start + (end - start) * i / (double)n;
                try
                {
                    Point3d p = curve.GetPointAtParameter(t);
                    if (pts.Count == 0 || pts[pts.Count - 1].DistanceTo(p) > 0.5)
                        pts.Add(p);
                }
                catch { }
            }
            return pts;
        }

        public static bool TryBindPickedCurve(SteelStructureParams target, Curve curve, out string error)
        {
            error = null;
            if (target == null || curve == null)
            {
                error = "没有选中曲线。";
                return false;
            }

            if (target.StructureType == ParametricStructureType.SpiralSteelMember)
            {
                Helix helix = curve as Helix;
                if (helix != null) return BindHelix(target, helix, out error);
                error = "请选择螺旋线（HELIX）。";
                return false;
            }

            if (target.StructureType != ParametricStructureType.ArcSteelMember)
            {
                error = "当前构件不支持按曲线生成。";
                return false;
            }

            Arc arc = curve as Arc;
            if (arc != null) return BindArc(target, arc, out error);
            Circle circle = curve as Circle;
            if (circle != null) return BindCircle(target, circle, out error);
            Polyline polyline = curve as Polyline;
            if (polyline != null) return BindPolyline(target, polyline, out error);

            error = "请选择圆弧、圆，或单段圆弧多段线。";
            return false;
        }

        private static bool BindArc(SteelStructureParams target, Arc arc, out string error)
        {
            error = null;
            double sweep = arc.TotalAngle;
            if (sweep < 1e-3 || arc.Radius < 1.0)
            {
                error = "圆弧半径或包角无效。";
                return false;
            }
            Vector3d z = arc.Normal.GetNormal();
            Vector3d x = arc.StartPoint - arc.Center;
            x = x - z * x.DotProduct(z);
            if (x.Length < 1e-4)
            {
                error = "无法识别圆弧起点。";
                return false;
            }
            ApplyFrame(target, arc.Center, x.GetNormal(), z);
            target.Span = arc.Radius;
            target.SubHeight = sweep * 180.0 / Math.PI;
            target.EndPointOffsetZ = 0.0;
            target.TopRadius = arc.Radius;
            target.HelixCounterClockwise = true;
            return true;
        }

        private static bool BindCircle(SteelStructureParams target, Circle circle, out string error)
        {
            error = null;
            if (circle.Radius < 1.0)
            {
                error = "圆半径无效。";
                return false;
            }
            Vector3d z = circle.Normal.GetNormal();
            ApplyFrame(target, circle.Center, Perpendicular(z), z);
            target.Span = circle.Radius;
            target.SubHeight = 360.0;
            target.EndPointOffsetZ = 0.0;
            target.TopRadius = circle.Radius;
            target.HelixCounterClockwise = true;
            return true;
        }

        private static bool BindPolyline(SteelStructureParams target, Polyline polyline, out string error)
        {
            error = null;
            if (!polyline.Closed && polyline.NumberOfVertices == 2 && Math.Abs(polyline.GetBulgeAt(0)) > 1e-8)
            {
                CircularArc3d geo = polyline.GetArcSegmentAt(0);
                return BindCircularGeometry(target, geo, geo.StartPoint, out error);
            }

            List<Point3d> pts = SampleCurve(polyline, 32);
            if (polyline.Closed && pts.Count > 2 && pts[0].DistanceTo(pts[pts.Count - 1]) < 1.0)
                pts.RemoveAt(pts.Count - 1);
            PathFit fit = TryFit(pts, polyline.Closed);
            if (fit == null || fit.Helix || fit.Curve == null)
            {
                if (fit != null) DisposeCurve(fit.Curve);
                error = "这条多段线不是一段圆弧或整圆。";
                return false;
            }
            DisposeCurve(fit.Curve);
            ApplyFrame(target, fit.Origin, fit.XAxis, fit.ZAxis);
            target.Span = fit.Radius;
            target.SubHeight = fit.FullCircle ? 360.0 : fit.SweepDegrees;
            target.EndPointOffsetZ = 0.0;
            target.TopRadius = fit.Radius;
            target.HelixCounterClockwise = true;
            return true;
        }

        private static bool BindCircularGeometry(SteelStructureParams target, CircularArc3d geo, Point3d start, out string error)
        {
            error = null;
            if (geo == null || geo.Radius < 1.0)
            {
                error = "圆弧无效。";
                return false;
            }
            Vector3d z = geo.Normal.GetNormal();
            Vector3d x = start - geo.Center;
            x = x - z * x.DotProduct(z);
            if (x.Length < 1e-4)
            {
                error = "无法识别圆弧起点。";
                return false;
            }
            double sweep = geo.EndAngle - geo.StartAngle;
            if (sweep < 0.0) sweep += Math.PI * 2.0;
            if (sweep < 1e-3)
            {
                error = "圆弧包角过小。";
                return false;
            }
            ApplyFrame(target, geo.Center, x.GetNormal(), z);
            target.Span = geo.Radius;
            target.SubHeight = Math.Min(360.0, sweep * 180.0 / Math.PI);
            target.EndPointOffsetZ = 0.0;
            target.TopRadius = geo.Radius;
            target.HelixCounterClockwise = true;
            return true;
        }

        private static bool BindHelix(SteelStructureParams target, Helix helix, out string error)
        {
            error = null;
            Vector3d axis = helix.AxisVector;
            if (axis.Length < 1e-8) axis = Vector3d.ZAxis;
            axis = axis.GetNormal();
            Point3d axisPoint = helix.GetAxisPoint();
            Point3d start = helix.StartPoint;
            Point3d origin = axisPoint + axis * ((start - axisPoint).DotProduct(axis));
            Vector3d radial = start - origin;
            radial = radial - axis * radial.DotProduct(axis);
            if (radial.Length < 1.0 || Math.Abs(helix.Height) < 1.0 || Math.Abs(helix.Turns) < 0.05)
            {
                error = "螺旋线半径、高度或圈数无效。";
                return false;
            }
            Vector3d x = radial.GetNormal();
            Vector3d y = axis.CrossProduct(x).GetNormal();
            ApplyFrame(target, origin, x, axis);
            target.Length = Math.Max(helix.BaseRadius, 1.0);
            target.TopRadius = helix.TopRadius > 1.0 ? helix.TopRadius : target.Length;
            target.Span = Math.Abs(helix.Height);
            target.SubHeight = Math.Abs(helix.Turns);
            target.EndPointOffsetZ = 0.0;
            target.HelixCounterClockwise = LocalY(helix, origin, y) >= 0.0;
            return true;
        }

        private static double LocalY(Helix helix, Point3d origin, Vector3d yAxis)
        {
            try
            {
                double span = helix.EndParam - helix.StartParam;
                double frac = Math.Min(0.2, 1.0 / (18.0 * Math.Max(Math.Abs(helix.Turns), 0.25)));
                Point3d q = helix.GetPointAtParameter(helix.StartParam + span * frac);
                return (q - origin).DotProduct(yAxis);
            }
            catch
            {
                return 1.0;
            }
        }

        private static void ApplyFrame(SteelStructureParams target, Point3d origin, Vector3d xAxis, Vector3d zAxis)
        {
            Vector3d z = zAxis.GetNormal();
            Vector3d x = xAxis - z * xAxis.DotProduct(z);
            if (x.Length < 1e-8) x = Perpendicular(z);
            x = x.GetNormal();
            Vector3d y = z.CrossProduct(x).GetNormal();
            target.UseCurveFrame = true;
            target.Origin = origin;
            target.RotationRad = 0.0;
            target.FrameX = x;
            target.FrameY = y;
            target.FrameZ = z;
        }

        private static void AlignProfileToPathStart(Region profile, Curve path)
        {
            try
            {
                Vector3d tangent = path.GetFirstDerivative(path.StartParam).GetNormal();
                Vector3d height = Vector3d.ZAxis - tangent * tangent.DotProduct(Vector3d.ZAxis);
                if (height.Length < 1e-6)
                {
                    height = Vector3d.XAxis - tangent * tangent.DotProduct(Vector3d.XAxis);
                    if (height.Length < 1e-6)
                        height = Vector3d.YAxis - tangent * tangent.DotProduct(Vector3d.YAxis);
                }
                if (height.Length < 1e-6) return;
                height = height.GetNormal();
                Vector3d width = tangent.CrossProduct(height).GetNormal();
                Matrix3d frame = Matrix3d.AlignCoordinateSystem(
                    Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                    Point3d.Origin, height, width, tangent);
                profile.TransformBy(frame);
            }
            catch
            {
            }
        }

        private static Helix TryBuildHelix(double baseRadius, double topRadius, double height, double turns, bool twist)
        {
            try
            {
                Helix helix = new Helix();
                helix.SetDatabaseDefaults();
                helix.Constrain = ConstrainType.Height;
                helix.AxisVector = Vector3d.ZAxis;
                helix.SetAxisPoint(Point3d.Origin, true);
                helix.BaseRadius = Math.Max(1.0, baseRadius);
                helix.TopRadius = Math.Max(1.0, topRadius);
                helix.Height = Math.Max(1.0, height);
                helix.Turns = Math.Max(0.05, turns);
                helix.Twist = twist;
                helix.CreateHelix();
                AlignHelixStart(helix);
                return helix;
            }
            catch
            {
                return null;
            }
        }

        private static void AlignHelixStart(Helix helix)
        {
            Point3d start = helix.StartPoint;
            Vector3d radial = new Vector3d(start.X, start.Y, 0.0);
            if (radial.Length < 1e-4) return;
            double angle = Math.Atan2(radial.Y, radial.X);
            if (Math.Abs(angle) > 1e-4)
                helix.TransformBy(Matrix3d.Rotation(-angle, Vector3d.ZAxis, Point3d.Origin));
            Point3d axisPoint = helix.GetAxisPoint();
            Vector3d shift = Point3d.Origin - axisPoint;
            if (shift.Length > 1e-3)
                helix.TransformBy(Matrix3d.Displacement(shift));
        }

        private static bool GoesCounterClockwise(Helix helix, double turns)
        {
            try
            {
                double span = helix.EndParam - helix.StartParam;
                double frac = Math.Min(0.25, 1.0 / (18.0 * Math.Max(turns, 0.25)));
                Point3d q = helix.GetPointAtParameter(helix.StartParam + span * frac);
                return q.Y >= -1e-4;
            }
            catch
            {
                return true;
            }
        }

        private static PathFit TryFit(IList<Point3d> pts, bool closed)
        {
            if (pts == null || pts.Count < 3) return null;
            PathFit planar;
            if (TryFitPlanarArc(pts, closed, out planar)) return planar;
            PathFit helix;
            if (!closed && TryFitHelix(pts, out helix)) return helix;
            return null;
        }

        private static bool TryFitPlanarArc(IList<Point3d> pts, bool closed, out PathFit fit)
        {
            fit = null;
            Point3d origin;
            Vector3d normal;
            Vector3d axisU;
            Vector3d axisV;
            if (!TryPlane(pts, closed, out origin, out normal, out axisU, out axisV))
                return false;

            double thickness = 0.0;
            var xy = new List<Tuple<double, double>>(pts.Count);
            foreach (Point3d p in pts)
            {
                Vector3d d = p - origin;
                thickness = Math.Max(thickness, Math.Abs(d.DotProduct(normal)));
                xy.Add(Tuple.Create(d.DotProduct(axisU), d.DotProduct(axisV)));
            }
            double cx;
            double cy;
            double radius;
            if (!TryCircle2d(xy, out cx, out cy, out radius)) return false;
            double tol = Math.Max(3.0, radius * 0.012);
            if (thickness > tol) return false;
            double maxErr = 0.0;
            foreach (Tuple<double, double> p in xy)
            {
                double err = Math.Abs(Math.Sqrt((p.Item1 - cx) * (p.Item1 - cx) + (p.Item2 - cy) * (p.Item2 - cy)) - radius);
                if (err > maxErr) maxErr = err;
            }
            if (maxErr > tol) return false;

            Point3d center = origin + axisU * cx + axisV * cy;
            double measuredSweep = SweepAngleDegrees(xy, cx, cy);
            bool full = closed || measuredSweep >= 359.0;
            Point3d start = pts[0];
            Vector3d x = start - center;
            x = x - normal * x.DotProduct(normal);
            if (x.Length < 1e-4) x = axisU;
            x = x.GetNormal();

            Curve curve;
            double sweepDeg;
            if (full)
            {
                Circle circle = new Circle(center, normal, radius);
                circle.SetDatabaseDefaults();
                curve = circle;
                sweepDeg = 360.0;
            }
            else
            {
                Point3d mid = pts[pts.Count / 2];
                Point3d end = pts[pts.Count - 1];
                CircularArc3d geo = new CircularArc3d(start, mid, end);
                curve = ToDatabaseArc(geo, mid);
                if (curve == null) return false;
                sweepDeg = ((Arc)curve).TotalAngle * 180.0 / Math.PI;
            }

            fit = new PathFit();
            fit.Curve = curve;
            fit.Origin = center;
            fit.XAxis = x;
            fit.ZAxis = normal;
            fit.Radius = radius;
            fit.TopRadius = radius;
            fit.SweepDegrees = sweepDeg;
            fit.FullCircle = full;
            return true;
        }

        private static bool TryFitHelix(IList<Point3d> pts, out PathFit fit)
        {
            fit = null;
            Point3d centroid = Centroid(pts);
            Vector3d axis = MajorAxis(pts, centroid);
            if (axis.Length < 1e-6) return false;
            axis = axis.GetNormal();
            if ((pts[pts.Count - 1] - pts[0]).DotProduct(axis) < 0.0) axis = -axis;

            Vector3d u = Perpendicular(axis);
            Vector3d v = axis.CrossProduct(u).GetNormal();
            var xy = new List<Tuple<double, double>>(pts.Count);
            foreach (Point3d p in pts)
            {
                Vector3d d = p - centroid;
                xy.Add(Tuple.Create(d.DotProduct(u), d.DotProduct(v)));
            }
            double cx;
            double cy;
            double radius;
            if (!TryCircle2d(xy, out cx, out cy, out radius)) return false;
            Point3d axisAtCentroid = centroid + u * cx + v * cy;
            double height = (pts[pts.Count - 1] - pts[0]).DotProduct(axis);
            if (height < Math.Max(5.0, radius * 0.02)) return false;

            double tol = Math.Max(4.0, radius * 0.04);
            double[] radial = new double[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3d d = pts[i] - axisAtCentroid;
                double along = d.DotProduct(axis);
                Vector3d perp = d - axis * along;
                radial[i] = perp.Length;
            }
            int window = Math.Max(1, pts.Count / 5);
            double r0 = Average(radial, 0, window);
            double r1 = Average(radial, pts.Count - window, pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                double t = pts.Count == 1 ? 0.0 : (double)i / (pts.Count - 1);
                double expect = r0 + (r1 - r0) * t;
                if (Math.Abs(radial[i] - expect) > tol) return false;
            }

            Point3d origin = axisAtCentroid + axis * ((pts[0] - axisAtCentroid).DotProduct(axis));
            Vector3d x = pts[0] - origin;
            x = x - axis * x.DotProduct(axis);
            if (x.Length < 1e-4) return false;
            x = x.GetNormal();
            Vector3d y = axis.CrossProduct(x).GetNormal();

            double total = 0.0;
            double prev = 0.0;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector3d d = pts[i] - origin;
                double ang = Math.Atan2(d.DotProduct(y), d.DotProduct(x));
                double step = ang - prev;
                while (step > Math.PI) step -= Math.PI * 2.0;
                while (step < -Math.PI) step += Math.PI * 2.0;
                if (Math.Abs(step) > 150.0 * Math.PI / 180.0) return false;
                total += step;
                prev = ang;
            }
            double turns = Math.Abs(total) / (Math.PI * 2.0);
            if (turns < 0.05) return false;
            bool ccw = total >= 0.0;
            Helix helix = CreateVerticalHelix(r0, r1, height, turns, ccw);
            if (helix == null) return false;
            helix.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                origin, x, y, axis));
            List<Point3d> probe = SampleCurve(helix, 8);
            Point3d onHelix = probe.Count > 4 ? probe[4] : probe[probe.Count - 1];
            if (onHelix.DistanceTo(pts[pts.Count / 2]) > Math.Max(12.0, radius * 0.12))
            {
                DisposeCurve(helix);
                return false;
            }

            fit = new PathFit();
            fit.Curve = helix;
            fit.Origin = origin;
            fit.XAxis = x;
            fit.ZAxis = axis;
            fit.Radius = r0;
            fit.TopRadius = r1;
            fit.Turns = turns;
            fit.Height = height;
            fit.Helix = true;
            fit.CounterClockwise = ccw;
            return true;
        }

        private static Arc ToDatabaseArc(CircularArc3d geo, Point3d mid)
        {
            if (geo == null || geo.Radius < 1.0) return null;
            Arc first = BuildArc(geo.Center, geo.Normal, geo.Radius, geo.StartPoint, geo.EndPoint, mid);
            if (first != null && PointOnArc(first, mid)) return first;
            DisposeCurve(first);
            return BuildArc(geo.Center, -geo.Normal, geo.Radius, geo.StartPoint, geo.EndPoint, mid);
        }

        private static Arc BuildArc(Point3d center, Vector3d normal, double radius, Point3d start, Point3d end, Point3d mid)
        {
            try
            {
                Vector3d z = normal.GetNormal();
                Vector3d ecsX;
                Vector3d ecsY;
                GetEcs(z, out ecsX, out ecsY);
                double a0 = AngleAbout(start - center, ecsX, ecsY);
                double a1 = AngleAbout(end - center, ecsX, ecsY);
                if (a1 <= a0) a1 += Math.PI * 2.0;
                if (a1 - a0 >= Math.PI * 2.0 - 1e-4) return null;
                Arc arc = new Arc(center, z, radius, a0, a1);
                arc.SetDatabaseDefaults();
                if (!PointOnArc(arc, mid))
                {
                    DisposeCurve(arc);
                    return null;
                }
                return arc;
            }
            catch
            {
                return null;
            }
        }

        private static bool PointOnArc(Arc arc, Point3d point)
        {
            if (arc == null) return false;
            if (Math.Abs(point.DistanceTo(arc.Center) - arc.Radius) > Math.Max(3.0, arc.Radius * 0.02))
                return false;
            Vector3d ecsX;
            Vector3d ecsY;
            GetEcs(arc.Normal, out ecsX, out ecsY);
            double mid = AngleAbout(point - arc.Center, ecsX, ecsY);
            return AngleInside(arc.StartAngle, arc.EndAngle, mid);
        }

        private static Spline CreateOpenSpline(IList<Point3d> pts)
        {
            List<Point3d> used = pts.Count <= 48 ? new List<Point3d>(pts) : Downsample(pts, 48);
            if (used.Count < 4) return null;
            try
            {
                Point3dCollection col = new Point3dCollection();
                foreach (Point3d p in used) col.Add(p);
                Spline spline = new Spline(col, KnotParameterizationEnum.Chord, 3, 0.0);
                spline.SetDatabaseDefaults();
                return spline;
            }
            catch
            {
                return null;
            }
        }

        private static List<Point3d> Clean(IList<Point3d> pts, bool closed)
        {
            var clean = new List<Point3d>();
            if (pts == null) return clean;
            foreach (Point3d p in pts)
            {
                if (clean.Count == 0 || clean[clean.Count - 1].DistanceTo(p) > 1.0)
                    clean.Add(p);
            }
            if (closed && clean.Count > 2 && clean[0].DistanceTo(clean[clean.Count - 1]) < 1.0)
                clean.RemoveAt(clean.Count - 1);
            return clean;
        }

        private static List<Point3d> Downsample(IList<Point3d> pts, int maxSegs)
        {
            var res = new List<Point3d>();
            if (pts == null || pts.Count == 0) return res;
            int n = pts.Count - 1;
            int segs = Math.Max(1, maxSegs);
            for (int i = 0; i <= segs; i++)
            {
                int idx = (int)Math.Round((double)i * n / segs);
                if (idx < 0) idx = 0;
                if (idx > n) idx = n;
                if (res.Count == 0 || res[res.Count - 1].DistanceTo(pts[idx]) > 1.0)
                    res.Add(pts[idx]);
            }
            return res;
        }

        private static bool TryPlane(
            IList<Point3d> pts,
            bool closed,
            out Point3d origin,
            out Vector3d normal,
            out Vector3d axisU,
            out Vector3d axisV)
        {
            origin = Centroid(pts);
            double nx = 0, ny = 0, nz = 0;
            int segmentCount = closed ? pts.Count : pts.Count - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                Vector3d a = pts[i] - origin;
                Vector3d b = pts[(i + 1) % pts.Count] - origin;
                nx += (a.Y - b.Y) * (a.Z + b.Z);
                ny += (a.Z - b.Z) * (a.X + b.X);
                nz += (a.X - b.X) * (a.Y + b.Y);
            }
            normal = new Vector3d(nx, ny, nz);
            if (normal.Length < 1e-6)
            {
                axisU = Vector3d.XAxis;
                axisV = Vector3d.YAxis;
                return false;
            }
            normal = normal.GetNormal();
            axisU = Perpendicular(normal);
            axisV = normal.CrossProduct(axisU).GetNormal();
            return true;
        }

        private static double SweepAngleDegrees(
            List<Tuple<double, double>> xy,
            double cx,
            double cy)
        {
            if (xy == null || xy.Count < 2) return 0.0;
            double total = 0.0;
            double previous = Math.Atan2(xy[0].Item2 - cy, xy[0].Item1 - cx);
            for (int i = 1; i < xy.Count; i++)
            {
                double current = Math.Atan2(xy[i].Item2 - cy, xy[i].Item1 - cx);
                double delta = current - previous;
                while (delta > Math.PI) delta -= Math.PI * 2.0;
                while (delta <= -Math.PI) delta += Math.PI * 2.0;
                total += delta;
                previous = current;
            }
            return Math.Abs(total) * 180.0 / Math.PI;
        }

        private static bool TryCircle2d(List<Tuple<double, double>> xy, out double cx, out double cy, out double radius)
        {
            cx = 0.0;
            cy = 0.0;
            radius = 0.0;
            if (xy == null || xy.Count < 3) return false;
            double sxx = 0, syy = 0, sxy = 0, sx = 0, sy = 0;
            double sx3 = 0, sy3 = 0, sx2y = 0, sxy2 = 0;
            foreach (Tuple<double, double> p in xy)
            {
                double x = p.Item1;
                double y = p.Item2;
                double x2 = x * x;
                double y2 = y * y;
                sxx += x2;
                syy += y2;
                sxy += x * y;
                sx += x;
                sy += y;
                sx3 += x2 * x;
                sy3 += y2 * y;
                sx2y += x2 * y;
                sxy2 += x * y2;
            }
            double[,] a = new double[3, 3];
            a[0, 0] = sxx; a[0, 1] = sxy; a[0, 2] = sx;
            a[1, 0] = sxy; a[1, 1] = syy; a[1, 2] = sy;
            a[2, 0] = sx; a[2, 1] = sy; a[2, 2] = xy.Count;
            double[] b = new double[] { -(sx3 + sxy2), -(sy3 + sx2y), -(sxx + syy) };
            double[] sol = new double[3];
            if (!Solve3(a, b, sol)) return false;
            cx = -sol[0] * 0.5;
            cy = -sol[1] * 0.5;
            double r2 = cx * cx + cy * cy - sol[2];
            if (r2 <= 1.0) return false;
            radius = Math.Sqrt(r2);
            return radius < 1e8;
        }

        private static Vector3d MajorAxis(IList<Point3d> pts, Point3d centroid)
        {
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (Point3d p in pts)
            {
                double x = p.X - centroid.X;
                double y = p.Y - centroid.Y;
                double z = p.Z - centroid.Z;
                xx += x * x;
                xy += x * y;
                xz += x * z;
                yy += y * y;
                yz += y * z;
                zz += z * z;
            }
            Vector3d v = Vector3d.ZAxis;
            for (int i = 0; i < 24; i++)
            {
                Vector3d w = new Vector3d(
                    xx * v.X + xy * v.Y + xz * v.Z,
                    xy * v.X + yy * v.Y + yz * v.Z,
                    xz * v.X + yz * v.Y + zz * v.Z);
                if (w.Length < 1e-8) break;
                v = w.GetNormal();
            }
            return v;
        }

        private static Point3d Centroid(IList<Point3d> pts)
        {
            double x = 0, y = 0, z = 0;
            foreach (Point3d p in pts)
            {
                x += p.X;
                y += p.Y;
                z += p.Z;
            }
            double n = Math.Max(1, pts.Count);
            return new Point3d(x / n, y / n, z / n);
        }

        private static double Average(double[] values, int start, int end)
        {
            double sum = 0.0;
            int count = 0;
            for (int i = start; i < end && i < values.Length; i++)
            {
                sum += values[i];
                count++;
            }
            return count == 0 ? 0.0 : sum / count;
        }

        private static bool Solve3(double[,] a, double[] b, double[] x)
        {
            double[,] m = new double[3, 4];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++) m[r, c] = a[r, c];
                m[r, 3] = b[r];
            }
            for (int col = 0; col < 3; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < 3; r++)
                {
                    if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
                }
                if (Math.Abs(m[pivot, col]) < 1e-8) return false;
                if (pivot != col)
                {
                    for (int c = col; c < 4; c++)
                    {
                        double tmp = m[col, c];
                        m[col, c] = m[pivot, c];
                        m[pivot, c] = tmp;
                    }
                }
                double div = m[col, col];
                for (int c = col; c < 4; c++) m[col, c] /= div;
                for (int r = 0; r < 3; r++)
                {
                    if (r == col) continue;
                    double factor = m[r, col];
                    for (int c = col; c < 4; c++) m[r, c] -= factor * m[col, c];
                }
            }
            x[0] = m[0, 3];
            x[1] = m[1, 3];
            x[2] = m[2, 3];
            return true;
        }

        private static void GetEcs(Vector3d normal, out Vector3d xAxis, out Vector3d yAxis)
        {
            Vector3d z = normal.GetNormal();
            Vector3d seed = Math.Abs(z.X) < 1.0 / 64.0 && Math.Abs(z.Y) < 1.0 / 64.0
                ? Vector3d.YAxis
                : Vector3d.ZAxis;
            xAxis = seed.CrossProduct(z).GetNormal();
            yAxis = z.CrossProduct(xAxis).GetNormal();
        }

        private static double AngleAbout(Vector3d vector, Vector3d xAxis, Vector3d yAxis)
        {
            return Math.Atan2(vector.DotProduct(yAxis), vector.DotProduct(xAxis));
        }

        private static bool AngleInside(double start, double end, double value)
        {
            double s = NormAngle(start);
            double e = NormAngle(end);
            double m = NormAngle(value);
            double sweep = e - s;
            if (sweep < 0.0) sweep += Math.PI * 2.0;
            double delta = m - s;
            if (delta < 0.0) delta += Math.PI * 2.0;
            return delta <= sweep + 1e-3;
        }

        private static double NormAngle(double angle)
        {
            double twoPi = Math.PI * 2.0;
            double v = angle % twoPi;
            if (v < 0.0) v += twoPi;
            return v;
        }

        private static Vector3d Perpendicular(Vector3d axis)
        {
            Vector3d z = axis.GetNormal();
            Vector3d seed = Math.Abs(z.Z) > 0.9 ? Vector3d.XAxis : Vector3d.ZAxis;
            return z.CrossProduct(seed).GetNormal();
        }

        private static void DisposeCurve(Curve curve)
        {
            if (curve == null) return;
            try
            {
                if (!curve.IsDisposed) curve.Dispose();
            }
            catch { }
        }
    }
}
