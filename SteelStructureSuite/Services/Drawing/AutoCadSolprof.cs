using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SteelStructureSuite.Services.Drawing
{
    /// <summary>
    /// 调用 AutoCAD 自己的 SOLPROF 做消隐，不在插件里重写射线求交。
    /// 圆管是 ACIS 圆柱，相切棱由 SOLPROF 去掉，只留真实轮廓。
    /// 临时实体放在当前图模型空间，读完轮廓后删除。
    /// </summary>
    internal static class AutoCadSolprof
    {
        internal sealed class ViewSpec
        {
            public string Key { get; }
            public string Title { get; }
            public Vector3d Right { get; }
            public Vector3d Up { get; }

            public Vector3d TowardCamera
            {
                get { return Right.CrossProduct(Up).GetNormal(); }
            }

            private ViewSpec(string key, string title, Vector3d right, Vector3d up)
            {
                Key = key;
                Title = title;
                Right = right;
                Up = up;
            }

            public static ViewSpec Front { get; } = new ViewSpec(
                "front", "正立面", new Vector3d(1, 0, 0), new Vector3d(0, 0, 1));

            public static ViewSpec Side { get; } = new ViewSpec(
                "side", "侧立面", new Vector3d(0, 1, 0), new Vector3d(0, 0, 1));

            public static ViewSpec Plan { get; } = new ViewSpec(
                "plan", "平面", new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));

            public static ViewSpec Isometric { get; } = new ViewSpec(
                "iso",
                "轴测",
                new Vector3d(0.8660254037844386, -0.8660254037844386, 0),
                new Vector3d(0.5, 0.5, 1));
        }

        internal sealed class SolprofView : IDisposable
        {
            public List<Entity> Visible { get; } = new List<Entity>();
            public List<Entity> Hidden { get; } = new List<Entity>();
            private bool _disposed;

            public bool HasGeometry
            {
                get { return Visible.Count + Hidden.Count > 0; }
            }

            public bool TryGetBounds(out double minX, out double maxX, out double minY, out double maxY)
            {
                minX = double.MaxValue;
                maxX = double.MinValue;
                minY = double.MaxValue;
                maxY = double.MinValue;
                bool any = false;
                foreach (Entity ent in Visible)
                    any |= Accumulate(ent, ref minX, ref maxX, ref minY, ref maxY);
                foreach (Entity ent in Hidden)
                    any |= Accumulate(ent, ref minX, ref maxX, ref minY, ref maxY);
                if (!any)
                {
                    minX = maxX = minY = maxY = 0;
                }
                return any;
            }

            public void Draw(
                Transaction tr,
                BlockTableRecord space,
                Point3d basePoint,
                string visibleLayer,
                string hiddenLayer,
                double hiddenLinetypeScale)
            {
                Place(tr, space, basePoint, Visible, visibleLayer, 0);
                Place(tr, space, basePoint, Hidden, hiddenLayer, hiddenLinetypeScale);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                DisposeList(Visible);
                DisposeList(Hidden);
            }

            private static void Place(
                Transaction tr,
                BlockTableRecord space,
                Point3d basePoint,
                List<Entity> source,
                string layer,
                double linetypeScale)
            {
                Matrix3d move = Matrix3d.Displacement(new Vector3d(basePoint.X, basePoint.Y, basePoint.Z));
                foreach (Entity src in source)
                {
                    if (src == null) continue;
                    Entity copy = (Entity)src.Clone();
                    try
                    {
                        copy.TransformBy(move);
                        copy.SetDatabaseDefaults(space.Database);
                        copy.Layer = layer;
                        copy.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
                        copy.Linetype = "ByLayer";
                        copy.LineWeight = LineWeight.ByLayer;
                        if (linetypeScale > 0)
                        {
                            copy.LinetypeScale = linetypeScale;
                            if (copy is Polyline pline) pline.Plinegen = true;
                            if (copy is Polyline2d pline2d) pline2d.LinetypeGenerationOn = true;
                        }
                        space.AppendEntity(copy);
                        tr.AddNewlyCreatedDBObject(copy, true);
                    }
                    catch
                    {
                        if (!copy.IsDisposed) copy.Dispose();
                        throw;
                    }
                }
            }

            private static bool Accumulate(Entity ent, ref double minX, ref double maxX, ref double minY, ref double maxY)
            {
                if (ent == null) return false;
                try
                {
                    Extents3d ext = ent.GeometricExtents;
                    if (ext.MinPoint.X < minX) minX = ext.MinPoint.X;
                    if (ext.MaxPoint.X > maxX) maxX = ext.MaxPoint.X;
                    if (ext.MinPoint.Y < minY) minY = ext.MinPoint.Y;
                    if (ext.MaxPoint.Y > maxY) maxY = ext.MaxPoint.Y;
                    return true;
                }
                catch
                {
                    if (ent is Curve curve)
                    {
                        Consider(curve.StartPoint, ref minX, ref maxX, ref minY, ref maxY);
                        Consider(curve.EndPoint, ref minX, ref maxX, ref minY, ref maxY);
                        return true;
                    }
                    return false;
                }
            }

            private static void Consider(Point3d point, ref double minX, ref double maxX, ref double minY, ref double maxY)
            {
                if (point.X < minX) minX = point.X;
                if (point.X > maxX) maxX = point.X;
                if (point.Y < minY) minY = point.Y;
                if (point.Y > maxY) maxY = point.Y;
            }

            private static void DisposeList(List<Entity> entities)
            {
                foreach (Entity ent in entities)
                {
                    try
                    {
                        if (ent != null && !ent.IsDisposed) ent.Dispose();
                    }
                    catch
                    {
                    }
                }
                entities.Clear();
            }
        }

        public static Dictionary<string, SolprofView> Project(IList<Solid3d> solids, IList<ViewSpec> views)
        {
            if (views == null || views.Count == 0)
                return new Dictionary<string, SolprofView>();

            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                throw new InvalidOperationException("没有活动的 AutoCAD 文档，无法调用 SOLPROF。");

            var results = new Dictionary<string, SolprofView>();
            Database previous = HostApplicationServices.WorkingDatabase;
            try
            {
                HostApplicationServices.WorkingDatabase = doc.Database;
                ProjectCore(doc, solids, views, results);
                return results;
            }
            catch
            {
                foreach (SolprofView view in results.Values)
                    view.Dispose();
                results.Clear();
                throw;
            }
            finally
            {
                HostApplicationServices.WorkingDatabase = previous;
            }
        }

        private static void ProjectCore(
            Document doc,
            IList<Solid3d> solids,
            IList<ViewSpec> views,
            Dictionary<string, SolprofView> results)
        {
            Database db = doc.Database;
            Editor ed = doc.Editor;
            var state = CadState.Capture(ed);
            var solidIds = new List<ObjectId>();
            string layoutName = "SS_HLR_" + DateTime.Now.Ticks.ToString();
            ObjectId viewportId = ObjectId.Null;
            var protectedModelIds = new HashSet<ObjectId>();
            var protectedBlockIds = new HashSet<ObjectId>();
            var protectedLayerIds = new HashSet<ObjectId>();
            bool layoutCreated = false;

            try
            {
                try { AcApp.SetSystemVariable("CMDECHO", 0); } catch { }
                EnsureHiddenLinetype(db);
                Snapshot(db, protectedModelIds, protectedBlockIds, protectedLayerIds);

                Point3d viewTarget = Point3d.Origin;
                double viewHeight = 1000;
                MeasureSolids(solids, ref viewTarget, ref viewHeight);
                solidIds.AddRange(CloneSolids(doc, solids));
                if (solidIds.Count == 0)
                    throw new InvalidOperationException("没有可用于 SOLPROF 的三维实体。");

                viewportId = CreateTempViewport(db, layoutName, out layoutCreated);
                ActivateLayout(layoutName);
                int vpNumber = TurnViewportOn(db, viewportId);
                AimViewport(doc, viewportId, views[0], viewTarget, viewHeight);
                EnterViewport(doc, ref vpNumber, viewportId);
                ForceWorldUcs(db, ed, viewportId);

                foreach (ViewSpec spec in views)
                {
                    ed.WriteMessage("\n[SteelStructureSuite] SOLPROF " + spec.Title + "...\n");
                    AimViewport(doc, viewportId, spec, viewTarget, viewHeight);
                    EnterViewport(doc, ref vpNumber, viewportId);
                    ForceWorldUcs(db, ed, viewportId);

                    var known = SnapshotModelSpace(db);
                    try
                    {
                        var args = new List<object> { "_.SOLPROF" };
                        foreach (ObjectId id in solidIds) args.Add(id);
                        args.Add("");
                        args.Add("_Y");
                        args.Add("_Y");
                        args.Add("_Y");
                        ed.Command(args.ToArray());
                    }
                    catch (System.Exception ex)
                    {
                        throw new InvalidOperationException(
                            spec.Title + " 调用 AutoCAD SOLPROF 失败：" + ex.Message
                            + "。请从 SS_DRAWING 或功能区出图，并且不要包在 DocumentLock 里。",
                            ex);
                    }

                    SolprofView view = HarvestNewCurves(db, known, spec);
                    results[spec.Key] = view;
                    ed.WriteMessage(
                        "\n[SteelStructureSuite] " + spec.Title
                        + " 可见 " + view.Visible.Count
                        + " 条，隐藏 " + view.Hidden.Count + " 条。\n");
                    if (!view.HasGeometry)
                    {
                        throw new InvalidOperationException(spec.Title + "：AutoCAD SOLPROF 没有生成轮廓线。");
                    }
                }
            }
            finally
            {
                EraseExtras(db, protectedModelIds, protectedBlockIds, protectedLayerIds, solidIds);
                RestoreState(doc, state, layoutCreated ? layoutName : null);
            }
        }

        private static void MeasureSolids(IList<Solid3d> solids, ref Point3d center, ref double height)
        {
            bool any = false;
            Extents3d ext = new Extents3d();
            if (solids == null) return;
            foreach (Solid3d solid in solids)
            {
                if (solid == null || solid.IsDisposed) continue;
                try
                {
                    Extents3d one = solid.GeometricExtents;
                    if (!any)
                    {
                        ext = one;
                        any = true;
                    }
                    else
                    {
                        ext.AddExtents(one);
                    }
                }
                catch
                {
                }
            }
            if (!any) return;
            center = ext.MinPoint + (ext.MaxPoint - ext.MinPoint) * 0.5;
            double diagonal = ext.MinPoint.DistanceTo(ext.MaxPoint);
            height = Math.Max(100.0, diagonal * 1.25);
        }

        private static List<ObjectId> CloneSolids(Document doc, IList<Solid3d> solids)
        {
            var ids = new List<ObjectId>();
            if (solids == null || solids.Count == 0) return ids;
            Database db = doc.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord model = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                foreach (Solid3d solid in solids)
                {
                    if (solid == null || solid.IsDisposed) continue;
                    Solid3d clone = null;
                    try
                    {
                        clone = (Solid3d)solid.Clone();
                        clone.SetDatabaseDefaults(db);
                        model.AppendEntity(clone);
                        tr.AddNewlyCreatedDBObject(clone, true);
                        ids.Add(clone.ObjectId);
                        clone = null;
                    }
                    catch (System.Exception ex)
                    {
                        if (clone != null && !clone.IsDisposed) clone.Dispose();
                        throw new InvalidOperationException("把实体送入 SOLPROF 失败：" + ex.Message, ex);
                    }
                }
                tr.Commit();
            }
            return ids;
        }

        private static ObjectId CreateTempViewport(Database db, string layoutName, out bool created)
        {
            ObjectId layoutId = LayoutManager.Current.CreateLayout(layoutName);
            created = true;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Layout layout = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
                BlockTableRecord paper = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);
                Viewport viewport = new Viewport
                {
                    CenterPoint = new Point3d(160, 120, 0),
                    Width = 200,
                    Height = 150
                };
                paper.AppendEntity(viewport);
                tr.AddNewlyCreatedDBObject(viewport, true);
                tr.Commit();
                return viewport.ObjectId;
            }
        }

        private static void ActivateLayout(string layoutName)
        {
            try
            {
                LayoutManager.Current.CurrentLayout = layoutName;
            }
            catch
            {
                AcApp.SetSystemVariable("TILEMODE", (short)0);
                AcApp.SetSystemVariable("CTAB", layoutName);
            }
        }

        private static int TurnViewportOn(Database db, ObjectId viewportId)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Viewport viewport = (Viewport)tr.GetObject(viewportId, OpenMode.ForWrite);
                viewport.On = true;
                viewport.Locked = false;
                viewport.UcsFollowModeOn = false;
                viewport.PerspectiveOn = false;
                viewport.FrontClipOn = false;
                viewport.BackClipOn = false;
                tr.Commit();
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Viewport viewport = (Viewport)tr.GetObject(viewportId, OpenMode.ForRead);
                int number = viewport.Number;
                tr.Commit();
                return number;
            }
        }

        private static void AimViewport(
            Document doc,
            ObjectId viewportId,
            ViewSpec spec,
            Point3d target,
            double height)
        {
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                Viewport viewport = (Viewport)tr.GetObject(viewportId, OpenMode.ForWrite);
                if (!viewport.On) viewport.On = true;
                viewport.ViewDirection = spec.TowardCamera;
                viewport.ViewTarget = target;
                viewport.ViewCenter = Point2d.Origin;
                viewport.ViewHeight = height;
                viewport.TwistAngle = 0;
                viewport.PerspectiveOn = false;
                viewport.UcsFollowModeOn = false;
                viewport.SetUcsToWorld();
                try { viewport.UpdateDisplay(); } catch { }
                tr.Commit();
            }
            try { doc.Editor.Regen(); } catch { }
        }

        private static void EnterViewport(Document doc, ref int vpNumber, ObjectId viewportId)
        {
            try { doc.Editor.SwitchToModelSpace(); } catch { }
            if (vpNumber < 2)
            {
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    Viewport viewport = (Viewport)tr.GetObject(viewportId, OpenMode.ForRead);
                    vpNumber = viewport.Number;
                    tr.Commit();
                }
            }
            if (vpNumber >= 2)
            {
                try { AcApp.SetSystemVariable("CVPORT", (short)vpNumber); } catch { }
            }
            short current = 0;
            try { current = Convert.ToInt16(AcApp.GetSystemVariable("CVPORT")); } catch { }
            if (current < 2)
            {
                try { doc.Editor.SwitchToModelSpace(); } catch { }
                try { AcApp.SetSystemVariable("CVPORT", (short)vpNumber); } catch { }
                try { current = Convert.ToInt16(AcApp.GetSystemVariable("CVPORT")); } catch { }
            }
            if (current < 2)
                throw new InvalidOperationException("未能进入临时模型视口，SOLPROF 已中止。");
        }

        private static void ForceWorldUcs(Database db, Editor ed, ObjectId viewportId)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Viewport viewport = (Viewport)tr.GetObject(viewportId, OpenMode.ForWrite);
                viewport.UcsFollowModeOn = false;
                viewport.SetUcsToWorld();
                tr.Commit();
            }
            ed.CurrentUserCoordinateSystem = Matrix3d.Identity;
        }

        private static SolprofView HarvestNewCurves(Database db, HashSet<ObjectId> knownModelIds, ViewSpec spec)
        {
            var view = new SolprofView();
            Matrix3d toSheet = ViewMatrix(spec);
            var freshIds = new List<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord model = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in model)
                {
                    if (!knownModelIds.Contains(id)) freshIds.Add(id);
                }

                foreach (ObjectId id in freshIds)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead, false) is Entity ent) || ent.IsErased) continue;
                    if (ent is BlockReference blockRef)
                    {
                        CollectBlock(tr, blockRef, blockRef.BlockTransform, IsHiddenLayer(blockRef.Layer), toSheet, view, 0);
                    }
                    else if (IsProfileLayer(ent.Layer))
                    {
                        TakeEntity(ent, Matrix3d.Identity, IsHiddenLayer(ent.Layer), toSheet, view);
                    }
                }

                foreach (ObjectId id in freshIds)
                {
                    if (tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent && !ent.IsErased)
                        ent.Erase();
                }
                tr.Commit();
            }
            return view;
        }

        private static void CollectBlock(
            Transaction tr,
            BlockReference blockRef,
            Matrix3d transform,
            bool hidden,
            Matrix3d toSheet,
            SolprofView view,
            int depth)
        {
            if (blockRef == null || depth > 4) return;
            if (!(tr.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead, false) is BlockTableRecord definition)) return;
            foreach (ObjectId id in definition)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead, false) is Entity ent)) continue;
                if (ent is BlockReference nested)
                {
                    bool nestedHidden = hidden || IsHiddenLayer(nested.Layer);
                    CollectBlock(tr, nested, nested.BlockTransform.PreMultiplyBy(transform), nestedHidden, toSheet, view, depth + 1);
                    continue;
                }
                TakeEntity(ent, transform, hidden || IsHiddenLayer(ent.Layer), toSheet, view);
            }
        }

        private static void TakeEntity(Entity source, Matrix3d blockTransform, bool hidden, Matrix3d toSheet, SolprofView view)
        {
            if (source == null) return;
            Entity copy = null;
            try
            {
                copy = (Entity)source.Clone();
                if (!blockTransform.Equals(Matrix3d.Identity))
                    copy.TransformBy(blockTransform);
                copy.TransformBy(toSheet);
                foreach (Entity fresh in Detach(copy))
                {
                    if (fresh == null) continue;
                    if (hidden) view.Hidden.Add(fresh);
                    else view.Visible.Add(fresh);
                }
            }
            catch
            {
            }
            finally
            {
                if (copy != null && !copy.IsDisposed) copy.Dispose();
            }
        }

        private static Matrix3d ViewMatrix(ViewSpec spec)
        {
            double rightLen = spec.Right.Length;
            double upLen = spec.Up.Length;
            if (rightLen < 1e-9 || upLen < 1e-9)
                throw new InvalidOperationException(spec.Title + " 的投影坐标无效。");
            if (Math.Abs(rightLen - upLen) > 1e-6)
                throw new InvalidOperationException(spec.Title + " 的视图坐标轴缩放不一致。");

            Vector3d xAxis = spec.Right / rightLen;
            Vector3d yAxis = spec.Up / upLen;
            Vector3d zAxis = xAxis.CrossProduct(yAxis);
            if (zAxis.Length < 1e-9)
                throw new InvalidOperationException(spec.Title + " 的视线方向无效。");
            zAxis = zAxis.GetNormal();
            double scale = rightLen;
            return Matrix3d.AlignCoordinateSystem(
                Point3d.Origin,
                xAxis,
                yAxis,
                zAxis,
                Point3d.Origin,
                Vector3d.XAxis * scale,
                Vector3d.YAxis * scale,
                Vector3d.ZAxis * scale);
        }

        private static IEnumerable<Entity> Detach(Entity transformed)
        {
            switch (transformed)
            {
                case Line line:
                    if (line.Length >= 0.01)
                        yield return new Line(Flat(line.StartPoint), Flat(line.EndPoint));
                    yield break;
                case Circle circle:
                    if (circle.Radius >= 0.01)
                        yield return new Circle(Flat(circle.Center), Vector3d.ZAxis, circle.Radius);
                    yield break;
                case Arc arc:
                    foreach (Entity rebuilt in RebuildArc(arc))
                        yield return rebuilt;
                    yield break;
                case Ellipse ellipse:
                    foreach (Entity rebuilt in RebuildEllipse(ellipse))
                        yield return rebuilt;
                    yield break;
                case Polyline polyline:
                    foreach (Entity rebuilt in RebuildPolyline(polyline))
                        yield return rebuilt;
                    yield break;
                case Spline spline:
                    foreach (Entity rebuilt in RebuildSpline(spline))
                        yield return rebuilt;
                    yield break;
                case Polyline2d _:
                case Polyline3d _:
                    foreach (Entity piece in ExplodeAndDetach(transformed))
                        yield return piece;
                    yield break;
                case Curve curve:
                    Entity sampled = SampleCurve(curve);
                    if (sampled != null) yield return sampled;
                    yield break;
                default:
                    yield break;
            }
        }

        private static IEnumerable<Entity> RebuildArc(Arc arc)
        {
            if (arc.Radius < 0.01) yield break;
            Entity rebuilt = null;
            try
            {
                Point3d start = arc.StartPoint;
                Point3d end = arc.EndPoint;
                Point3d mid = arc.GetPointAtParameter((arc.StartParam + arc.EndParam) * 0.5);
                using (CircularArc3d ge = new CircularArc3d(start, mid, end))
                using (Curve created = Curve.CreateFromGeCurve(ge))
                {
                    if (created is Arc fresh)
                        rebuilt = new Arc(Flat(fresh.Center), fresh.Radius, fresh.StartAngle, fresh.EndAngle);
                }
            }
            catch
            {
                rebuilt = SampleCurve(arc);
            }
            if (rebuilt != null) yield return rebuilt;
        }

        private static IEnumerable<Entity> RebuildEllipse(Ellipse ellipse)
        {
            Vector3d major = new Vector3d(ellipse.MajorAxis.X, ellipse.MajorAxis.Y, 0);
            if (major.Length < 0.01 || ellipse.RadiusRatio < 1e-6) yield break;
            Entity rebuilt = null;
            try
            {
                rebuilt = new Ellipse(
                    Flat(ellipse.Center),
                    Vector3d.ZAxis,
                    major,
                    Math.Min(1.0, ellipse.RadiusRatio),
                    ellipse.StartAngle,
                    ellipse.EndAngle);
            }
            catch
            {
                rebuilt = SampleCurve(ellipse);
            }
            if (rebuilt != null) yield return rebuilt;
        }

        private static IEnumerable<Entity> RebuildPolyline(Polyline polyline)
        {
            if (polyline.NumberOfVertices < 2) yield break;
            if (polyline.Normal.GetNormal().DotProduct(Vector3d.ZAxis) < 0.99)
            {
                Entity sampled = SampleCurve(polyline);
                if (sampled != null) yield return sampled;
                yield break;
            }

            var copy = new Polyline();
            int added = 0;
            for (int index = 0; index < polyline.NumberOfVertices; index++)
            {
                Point2d point = polyline.GetPoint2dAt(index);
                if (added > 0)
                {
                    Point2d prev = copy.GetPoint2dAt(added - 1);
                    double dx = prev.X - point.X;
                    double dy = prev.Y - point.Y;
                    if (dx * dx + dy * dy < 1e-8) continue;
                }
                copy.AddVertexAt(added, point, polyline.GetBulgeAt(index), 0, 0);
                added++;
            }
            if (added < 2)
            {
                copy.Dispose();
                yield break;
            }
            copy.Closed = polyline.Closed && added >= 3;
            copy.Elevation = 0;
            yield return copy;
        }

        private static IEnumerable<Entity> RebuildSpline(Spline spline)
        {
            Entity rebuilt = null;
            try
            {
                NurbsData data = spline.NurbsData;
                rebuilt = new Spline(
                    data.Degree,
                    data.Rational,
                    data.Closed,
                    data.Periodic,
                    data.GetControlPoints(),
                    data.GetKnots(),
                    data.GetWeights(),
                    data.ControlPointTolerance,
                    data.KnotTolerance);
            }
            catch
            {
                rebuilt = SampleCurve(spline);
            }
            if (rebuilt != null) yield return rebuilt;
        }

        private static IEnumerable<Entity> ExplodeAndDetach(Entity source)
        {
            var produced = new List<Entity>();
            var pieces = new DBObjectCollection();
            bool exploded = true;
            try
            {
                source.Explode(pieces);
            }
            catch
            {
                exploded = false;
            }
            if (!exploded)
            {
                if (source is Curve curve)
                {
                    Entity sampled = SampleCurve(curve);
                    if (sampled != null) yield return sampled;
                }
                yield break;
            }

            foreach (DBObject obj in pieces)
            {
                if (obj is Entity ent)
                {
                    try
                    {
                        foreach (Entity fresh in Detach(ent))
                            produced.Add(fresh);
                    }
                    catch
                    {
                    }
                    if (!ent.IsDisposed) ent.Dispose();
                }
                else
                {
                    obj.Dispose();
                }
            }
            foreach (Entity fresh in produced)
                yield return fresh;
        }

        private static Entity SampleCurve(Curve curve)
        {
            double length;
            try
            {
                length = curve.GetDistanceAtParameter(curve.EndParam);
            }
            catch
            {
                length = 0;
            }
            if (length < 0.01) return null;
            int count = Math.Max(2, Math.Min(160, (int)(length / 15.0) + 1));
            var polyline = new Polyline();
            int added = 0;
            for (int index = 0; index <= count; index++)
            {
                Point3d point;
                try
                {
                    point = curve.GetPointAtDist(length * index / count);
                }
                catch
                {
                    continue;
                }
                var flat = new Point2d(point.X, point.Y);
                if (added > 0)
                {
                    Point2d prev = polyline.GetPoint2dAt(added - 1);
                    double dx = prev.X - flat.X;
                    double dy = prev.Y - flat.Y;
                    if (dx * dx + dy * dy < 1e-8) continue;
                }
                polyline.AddVertexAt(added, flat, 0, 0, 0);
                added++;
            }
            if (added < 2)
            {
                polyline.Dispose();
                return null;
            }
            return polyline;
        }

        private static Point3d Flat(Point3d point)
        {
            return new Point3d(point.X, point.Y, 0);
        }

        private static bool IsProfileLayer(string name)
        {
            return IsHiddenLayer(name) || (!string.IsNullOrEmpty(name) && name.StartsWith("PV", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsHiddenLayer(string name)
        {
            return !string.IsNullOrEmpty(name) && name.StartsWith("PH", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureHiddenLinetype(Database db)
        {
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    LinetypeTable table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                    if (table.Has("HIDDEN"))
                    {
                        tr.Commit();
                        return;
                    }
                    tr.Commit();
                }
                db.LoadLineTypeFile("HIDDEN", "acad.lin");
            }
            catch
            {
            }
        }

        private static void Snapshot(
            Database db,
            HashSet<ObjectId> modelIds,
            HashSet<ObjectId> blockIds,
            HashSet<ObjectId> layerIds)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in blockTable) blockIds.Add(id);
                BlockTableRecord model = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in model) modelIds.Add(id);
                LayerTable layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in layers) layerIds.Add(id);
                tr.Commit();
            }
        }

        private static HashSet<ObjectId> SnapshotModelSpace(Database db)
        {
            var ids = new HashSet<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord model = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in model) ids.Add(id);
                tr.Commit();
            }
            return ids;
        }

        private static void EraseExtras(
            Database db,
            HashSet<ObjectId> protectedModelIds,
            HashSet<ObjectId> protectedBlockIds,
            HashSet<ObjectId> protectedLayerIds,
            List<ObjectId> solidIds)
        {
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    LayerTable layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (layers.Has("0") && !protectedLayerIds.Contains(db.Clayer))
                        db.Clayer = layers["0"];

                    BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord model = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    var eraseModel = new List<ObjectId>();
                    foreach (ObjectId id in model)
                    {
                        if (!protectedModelIds.Contains(id)) eraseModel.Add(id);
                    }
                    foreach (ObjectId id in solidIds)
                    {
                        if (!eraseModel.Contains(id)) eraseModel.Add(id);
                    }
                    foreach (ObjectId id in eraseModel)
                    {
                        if (tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent && !ent.IsErased)
                            ent.Erase();
                    }

                    var eraseBlocks = new List<ObjectId>();
                    foreach (ObjectId id in blockTable)
                    {
                        if (!protectedBlockIds.Contains(id)) eraseBlocks.Add(id);
                    }
                    foreach (ObjectId id in eraseBlocks)
                    {
                        if (!(tr.GetObject(id, OpenMode.ForWrite, false) is BlockTableRecord record)) continue;
                        if (record.IsLayout || record.IsErased) continue;
                        try { record.Erase(); } catch { }
                    }

                    var eraseLayers = new List<ObjectId>();
                    foreach (ObjectId id in layers)
                    {
                        if (!protectedLayerIds.Contains(id)) eraseLayers.Add(id);
                    }
                    foreach (ObjectId id in eraseLayers)
                    {
                        if (id == db.Clayer) continue;
                        if (tr.GetObject(id, OpenMode.ForWrite, false) is LayerTableRecord layer && !layer.IsErased)
                        {
                            try { layer.Erase(); } catch { }
                        }
                    }
                    tr.Commit();
                }
            }
            catch
            {
            }
        }

        private static void RestoreState(Document doc, CadState state, string layoutName)
        {
            try
            {
                if (!string.IsNullOrEmpty(layoutName))
                {
                    try { LayoutManager.Current.CurrentLayout = "Model"; } catch { AcApp.SetSystemVariable("TILEMODE", (short)1); }
                    try { LayoutManager.Current.DeleteLayout(layoutName); } catch { }
                }
            }
            catch
            {
            }

            try { AcApp.SetSystemVariable("TILEMODE", state.TileMode); } catch { }
            try
            {
                short tile = Convert.ToInt16(state.TileMode);
                if (tile == 0 && state.Tab is string tab && !string.Equals(tab, layoutName, StringComparison.OrdinalIgnoreCase))
                    try { LayoutManager.Current.CurrentLayout = tab; } catch { AcApp.SetSystemVariable("CTAB", tab); }
            }
            catch { }
            try { AcApp.SetSystemVariable("CVPORT", state.CvPort); } catch { }
            try { doc.Editor.CurrentUserCoordinateSystem = state.Ucs; } catch { }
            try { AcApp.SetSystemVariable("CMDECHO", state.CmdEcho); } catch { }
        }

        private sealed class CadState
        {
            public object TileMode;
            public object Tab;
            public object CvPort;
            public object CmdEcho;
            public Matrix3d Ucs;

            public static CadState Capture(Editor ed)
            {
                return new CadState
                {
                    TileMode = AcApp.GetSystemVariable("TILEMODE"),
                    Tab = AcApp.GetSystemVariable("CTAB"),
                    CvPort = AcApp.GetSystemVariable("CVPORT"),
                    CmdEcho = AcApp.GetSystemVariable("CMDECHO"),
                    Ucs = ed.CurrentUserCoordinateSystem
                };
            }
        }
    }
}
