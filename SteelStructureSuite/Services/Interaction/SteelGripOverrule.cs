using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Modeling;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SteelStructureSuite.Services.Interaction
{
    /// <summary>
    /// 参数化夹点复写规则（彻底解决夹点与实体不同步问题）：
    /// - 每个 ParamGrip 实例直接携带源实体 SourceHandle 与拖拽起始快照 BaselineParams，
    ///   即使 AutoCAD 在拖拽预览帧传入 Handle=0 的瞬态克隆实体，也能精准计算并实时同步更新 GripPoint！
    /// - Kind 0: 基准原点平移夹点（正方形黄色，直接矩阵位移或同步 Origin，0 延迟）
    /// - Kind 1: 跨度/长度/外径正交拉伸夹点（红色箭头，沿构件 X 轴正交投影，只改 Span）
    /// - Kind 2: 高度正交拉伸夹点（绿色菱形，沿 Z 轴正交投影，只改 Height）
    /// - Kind 3: 宽度/进深正交拉伸夹点（品红圆形，沿构件 Y 轴正交投影，只改 Length）
    /// </summary>
    public class SteelGripOverrule : GripOverrule
    {
        private static readonly Dictionary<long, SteelStructureParams> GripBaseline =
            new Dictionary<long, SteelStructureParams>();
        private static readonly HashSet<Document> HookedDocs = new HashSet<Document>();

        private sealed class ParamGrip : GripData
        {
            public int Kind;
            public Vector3d Axis;
            public long SourceHandle;
            public SteelStructureParams BaselineSnapshot;

            public ParamGrip()
            {
                DrawAtDragImageGripPoint = true;
            }

            public override bool ViewportDraw(
                ViewportDraw worldDraw,
                ObjectId entityId,
                DrawType type,
                Point3d? imageGripPoint,
                int gripSizeInPixels)
            {
                try
                {
                    DrawGlyph(worldDraw, type, imageGripPoint, gripSizeInPixels);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            public override void OnGripStatusChanged(ObjectId entityId, Status newStatus)
            {
                if (newStatus == Status.GripStart && BaselineSnapshot != null)
                {
                    if (SourceHandle != 0)
                        GripBaseline[SourceHandle] = BaselineSnapshot.Clone();
                    SteelDataStorage.SetActiveTransientDragParams(BaselineSnapshot);
                }
                else if (newStatus == Status.GripEnd || newStatus == Status.GripAbort)
                {
                    Document doc = Application.DocumentManager.MdiActiveDocument;
                    if (newStatus == Status.GripEnd && doc != null)
                    {
                        SteelDataStorage.FlushPendingEdits(doc.Database);
                    }
                    else if (newStatus == Status.GripAbort)
                    {
                        SteelDataStorage.DiscardPendingEdits();
                    }
                    GripBaseline.Clear();
                }
                base.OnGripStatusChanged(entityId, newStatus);
            }

            private void DrawGlyph(
                ViewportDraw draw,
                DrawType type,
                Point3d? imageGripPoint,
                int gripSizeInPixels)
            {
                Point3d center = (type == DrawType.DragImageGrip && imageGripPoint.HasValue)
                    ? imageGripPoint.Value
                    : GripPoint;

                Vector3d view = draw.Viewport.ViewDirection;
                if (view.Length < 1e-8) view = Vector3d.ZAxis;
                Vector3d into = view.GetNormal();
                Vector3d xAxis = Axis.Length > 1e-6 ? Axis : Vector3d.XAxis;
                xAxis = xAxis - into * xAxis.DotProduct(into);
                if (xAxis.Length < 1e-6)
                {
                    xAxis = into.CrossProduct(Vector3d.ZAxis);
                    if (xAxis.Length < 1e-6) xAxis = Vector3d.XAxis;
                }
                xAxis = xAxis.GetNormal();
                Vector3d yAxis = xAxis.CrossProduct(into).GetNormal();

                Point2d pixels = draw.Viewport.GetNumPixelsInUnitSquare(center);
                double pixelsPerUnit = Math.Max(Math.Abs(pixels.X), 1e-6);
                bool active = type == DrawType.HotGrip || type == DrawType.HoverGrip || type == DrawType.DragImageGrip;
                int px = Math.Max(gripSizeInPixels, 6);
                double half = px * (active ? 0.65 : 0.50) / pixelsPerUnit;

                short fill = Kind == 0 ? (short)2 : Kind == 1 ? (short)1 : Kind == 2 ? (short)3 : (short)6;
                if (type == DrawType.HotGrip || type == DrawType.DragImageGrip) fill = 30;
                else if (type == DrawType.HoverGrip) fill = 2;

                Fill(draw, center, xAxis, yAxis, half, fill);
            }

            private void Fill(
                ViewportDraw draw,
                Point3d center,
                Vector3d xAxis,
                Vector3d yAxis,
                double half,
                short color)
            {
                draw.SubEntityTraits.Color = color;
                draw.SubEntityTraits.FillType = FillType.FillAlways;
                Point3dCollection pts = new Point3dCollection();
                foreach (Point2d local in Shape(Kind))
                    pts.Add(center + xAxis * (local.X * half) + yAxis * (local.Y * half));
                if (pts.Count >= 3)
                    draw.Geometry.Polygon(pts);
            }

            private static Point2d[] Shape(int kind)
            {
                switch (kind)
                {
                    case 1:
                        return new[]
                        {
                            new Point2d(-1.05, -0.72),
                            new Point2d(0.05, -0.72),
                            new Point2d(0.05, -0.28),
                            new Point2d(1.15, 0),
                            new Point2d(0.05, 0.28),
                            new Point2d(0.05, 0.72),
                            new Point2d(-1.05, 0.72)
                        };
                    case 2:
                        return new[]
                        {
                            new Point2d(0, -1.2),
                            new Point2d(0.85, 0),
                            new Point2d(0, 1.2),
                            new Point2d(-0.85, 0)
                        };
                    case 3:
                        return Circle(10);
                    default:
                        return new[]
                        {
                            new Point2d(-1, -1),
                            new Point2d(1, -1),
                            new Point2d(1, 1),
                            new Point2d(-1, 1)
                        };
                }
            }

            private static Point2d[] Circle(int segs)
            {
                var pts = new Point2d[segs];
                for (int i = 0; i < segs; i++)
                {
                    double a = Math.PI * 2.0 * i / segs;
                    pts[i] = new Point2d(Math.Cos(a), Math.Sin(a));
                }
                return pts;
            }
        }

        public override bool IsApplicable(RXObject overruledSubject)
        {
            return overruledSubject is Solid3d solid && SteelDataStorage.TryReadParams(solid, out _);
        }

        public override void GetGripPoints(
            Entity entity,
            GripDataCollection grips,
            double curViewUnitSize,
            int gripSize,
            Vector3d curViewDir,
            GetGripPointsFlags bitFlags)
        {
            if (!TryCollectGrips(entity, out SteelStructureParams p, out long handle, out Point3d origin, out Point3d spanPt, out bool hasHeight, out Point3d heightPt, out bool hasWidth, out Point3d widthPt))
            {
                base.GetGripPoints(entity, grips, curViewUnitSize, gripSize, curViewDir, bitFlags);
                return;
            }

            Vector3d dirX = new Vector3d(Math.Cos(p.RotationRad), Math.Sin(p.RotationRad), 0);
            Vector3d dirY = new Vector3d(-Math.Sin(p.RotationRad), Math.Cos(p.RotationRad), 0);

            grips.Add(MakeGrip(0, origin, dirX, handle, p));
            grips.Add(MakeGrip(1, spanPt, dirX, handle, p));
            if (hasHeight) grips.Add(MakeGrip(2, heightPt, Vector3d.ZAxis, handle, p));
            if (hasWidth) grips.Add(MakeGrip(3, widthPt, dirY, handle, p));
        }

        public override void GetGripPoints(
            Entity entity,
            Point3dCollection gripPoints,
            IntegerCollection snapModes,
            IntegerCollection geometryIds)
        {
            if (!TryCollectGrips(entity, out _, out _, out Point3d origin, out Point3d spanPt, out bool hasHeight, out Point3d heightPt, out bool hasWidth, out Point3d widthPt))
            {
                base.GetGripPoints(entity, gripPoints, snapModes, geometryIds);
                return;
            }

            gripPoints.Add(origin);
            gripPoints.Add(spanPt);
            if (hasHeight) gripPoints.Add(heightPt);
            if (hasWidth) gripPoints.Add(widthPt);
        }

        public override void MoveGripPointsAt(
            Entity entity,
            GripDataCollection grips,
            Vector3d offset,
            MoveGripPointsFlags bitFlags)
        {
            if (grips == null || grips.Count == 0)
            {
                base.MoveGripPointsAt(entity, grips, offset, bitFlags);
                return;
            }

            var kinds = new IntegerCollection();
            long fallbackHandle = 0;
            SteelStructureParams fallbackBaseline = null;

            foreach (GripData grip in grips)
            {
                if (grip is ParamGrip paramGrip)
                {
                    kinds.Add(paramGrip.Kind);
                    if (fallbackHandle == 0 && paramGrip.SourceHandle != 0)
                        fallbackHandle = paramGrip.SourceHandle;
                    if (fallbackBaseline == null && paramGrip.BaselineSnapshot != null)
                        fallbackBaseline = paramGrip.BaselineSnapshot;
                }
            }

            if (kinds.Count == 0)
            {
                base.MoveGripPointsAt(entity, grips, offset, bitFlags);
                return;
            }

            SteelStructureParams updatedParams = ApplyGripMove(entity, kinds, offset, fallbackHandle, fallbackBaseline);
            if (updatedParams != null)
            {
                ComputeGripPositionsForParams(updatedParams, out Point3d newOrigin, out Point3d newSpanPt, out _, out Point3d newHeightPt, out _, out Point3d newWidthPt);
                foreach (GripData grip in grips)
                {
                    if (grip is ParamGrip pg)
                    {
                        if (pg.Kind == 0) pg.GripPoint = newOrigin;
                        else if (pg.Kind == 1) pg.GripPoint = newSpanPt;
                        else if (pg.Kind == 2) pg.GripPoint = newHeightPt;
                        else if (pg.Kind == 3) pg.GripPoint = newWidthPt;
                    }
                }
            }
        }

        public override void MoveGripPointsAt(
            Entity entity,
            IntegerCollection indices,
            Vector3d offset)
        {
            if (indices == null || indices.Count == 0 ||
                !SteelDataStorage.TryReadParams(entity, out SteelStructureParams p))
            {
                base.MoveGripPointsAt(entity, indices, offset);
                return;
            }

            DescribeGrips(p, out bool hasHeight, out bool hasWidth);
            var kinds = new IntegerCollection();
            foreach (int idx in indices)
            {
                int kind = idx;
                if (!hasHeight && idx >= 2) kind++;
                if (!hasWidth && kind >= 3) continue;
                kinds.Add(kind);
            }

            if (kinds.Count == 0)
            {
                base.MoveGripPointsAt(entity, indices, offset);
                return;
            }

            long h = 0;
            try { h = entity.Handle.Value; } catch { }
            ApplyGripMove(entity, kinds, offset, h, p);
        }

        private static void HookDocument(Database db)
        {
            if (db == null) return;
            Document doc = null;
            try { doc = Application.DocumentManager.GetDocument(db); } catch { }
            if (doc == null || !HookedDocs.Add(doc)) return;
            doc.CommandEnded += OnGripCommandFinished;
            doc.CommandCancelled += OnGripCommandCancelled;
            doc.CommandFailed += OnGripCommandCancelled;
        }

        private static void OnGripCommandFinished(object sender, CommandEventArgs e)
        {
            if (sender is Document doc)
                SteelDataStorage.FlushPendingEdits(doc.Database);
            GripBaseline.Clear();
        }

        private static void OnGripCommandCancelled(object sender, CommandEventArgs e)
        {
            SteelDataStorage.DiscardPendingEdits();
            GripBaseline.Clear();
        }

        private static ParamGrip MakeGrip(int kind, Point3d point, Vector3d axis, long handle, SteelStructureParams snapshot)
        {
            if (axis.Length > 1e-8) axis = axis.GetNormal();
            return new ParamGrip
            {
                Kind = kind,
                GripPoint = point,
                Axis = axis,
                SourceHandle = handle,
                BaselineSnapshot = snapshot?.Clone()
            };
        }

        private static bool TryCollectGrips(
            Entity entity,
            out SteelStructureParams p,
            out long handle,
            out Point3d origin,
            out Point3d spanPt,
            out bool hasHeight,
            out Point3d heightPt,
            out bool hasWidth,
            out Point3d widthPt)
        {
            p = null;
            handle = 0;
            origin = Point3d.Origin;
            spanPt = Point3d.Origin;
            heightPt = Point3d.Origin;
            widthPt = Point3d.Origin;
            hasHeight = false;
            hasWidth = false;

            if (!SteelDataStorage.TryReadParams(entity, out p) || p == null)
                return false;

            try { handle = entity.Handle.Value; } catch { }
            SteelDataStorage.CacheParamsByHandle(entity, p);
            HookDocument(entity.Database);

            ComputeGripPositionsForParams(p, out origin, out spanPt, out hasHeight, out heightPt, out hasWidth, out widthPt);
            return true;
        }

        private static void ComputeGripPositionsForParams(
            SteelStructureParams p,
            out Point3d origin,
            out Point3d spanPt,
            out bool hasHeight,
            out Point3d heightPt,
            out bool hasWidth,
            out Point3d widthPt)
        {
            DescribeGrips(p, out hasHeight, out hasWidth);

            double span = Math.Max(p.Span, 200.0);
            double len = Math.Max(p.Length, 150.0);
            double h = Math.Max(p.Height, 200.0);
            double sub = Math.Max(p.SubHeight, 80.0);
            double dz = p.EndPointOffsetZ;

            origin = ToWorld(p, 0, 0, 0);
            spanPt = ToWorld(p, span, 0, 0);
            heightPt = ToWorld(p, 0, 0, h);
            widthPt = ToWorld(p, 0, len, 0);

            switch (p.StructureType)
            {
                case ParametricStructureType.LinearSteelMember:
                case ParametricStructureType.SpiralSteelMember:
                    spanPt = ToWorld(p, p.Span, 0, dz);
                    break;
                case ParametricStructureType.ArcSteelMember:
                    spanPt = ToWorld(p, Math.Max(p.Span, 80.0), 0, 0);
                    break;
                case ParametricStructureType.SpiralStair:
                case ParametricStructureType.VesselSupport:
                case ParametricStructureType.TowerCircularPlatform:
                case ParametricStructureType.VerticalVesselSkirt:
                case ParametricStructureType.PipeShoeSupport:
                case ParametricStructureType.TankRoofHandrail:
                    spanPt = ToWorld(p, span * 0.5, 0, 0);
                    heightPt = ToWorld(p, 0, 0, h);
                    if (p.StructureType == ParametricStructureType.TowerCircularPlatform)
                        widthPt = ToWorld(p, 0, span * 0.5 + len, h);
                    break;
                case ParametricStructureType.StraightStair:
                    spanPt = ToWorld(p, span, 0, h);
                    widthPt = ToWorld(p, 0, len, 0);
                    heightPt = ToWorld(p, 0, len * 0.5, h);
                    break;
                case ParametricStructureType.SafetyCagedLadder:
                    spanPt = ToWorld(p, span, 0, 0);
                    heightPt = ToWorld(p, 0, 0, h + 1100.0);
                    break;
                case ParametricStructureType.RoofTruss:
                    heightPt = ToWorld(p, span * 0.5, 0, h + sub);
                    break;
                case ParametricStructureType.CantileverWalkway:
                {
                    double knee = Math.Min(len * 0.65, Math.Max(h * 0.4, 450.0));
                    double rail = Math.Max(sub, 1050.0);
                    origin = ToWorld(p, 0, 0, h - knee);
                    spanPt = ToWorld(p, span, 0, h);
                    widthPt = ToWorld(p, 0, len, h);
                    heightPt = ToWorld(p, 0, len, h + rail);
                    break;
                }
            }
        }

        private static void DescribeGrips(SteelStructureParams p, out bool hasHeight, out bool hasWidth)
        {
            hasHeight = p.StructureType != ParametricStructureType.LinearSteelMember &&
                        p.StructureType != ParametricStructureType.ArcSteelMember &&
                        p.StructureType != ParametricStructureType.SpiralSteelMember;
            hasWidth = p.StructureType == ParametricStructureType.SpacePlatform ||
                       p.StructureType == ParametricStructureType.CantileverWalkway ||
                       p.StructureType == ParametricStructureType.MultiBaySpaceFrame ||
                       p.StructureType == ParametricStructureType.PipeBridgeTruss ||
                       p.StructureType == ParametricStructureType.StraightStair ||
                       p.StructureType == ParametricStructureType.TowerCircularPlatform ||
                       p.StructureType == ParametricStructureType.SubstationGantryFrame ||
                       p.StructureType == ParametricStructureType.CableTrenchSupport ||
                       p.StructureType == ParametricStructureType.CraneRunwayGirder;
        }

        private static Point3d ToWorld(SteelStructureParams p, double x, double y, double z)
        {
            Point3d local = new Point3d(x, y, z - SteelStructureBuilder.GetBaseOffsetZ(p));
            if (Math.Abs(p.RotationRad) > 1e-9)
                local = local.TransformBy(Matrix3d.Rotation(p.RotationRad, Vector3d.ZAxis, Point3d.Origin));
            return local + (p.Origin - Point3d.Origin);
        }

        private static SteelStructureParams ApplyGripMove(
            Entity entity,
            IntegerCollection kinds,
            Vector3d offset,
            long fallbackHandle,
            SteelStructureParams fallbackBaseline)
        {
            if (!(entity is Solid3d solid) || kinds == null || kinds.Count == 0)
                return null;

            long handle = 0;
            try { handle = solid.Handle.Value; } catch { }
            if (handle == 0) handle = fallbackHandle;

            SteelStructureParams origParams = null;
            if (handle != 0 && GripBaseline.TryGetValue(handle, out SteelStructureParams savedBase) && savedBase != null)
            {
                origParams = savedBase.Clone();
            }
            else if (fallbackBaseline != null)
            {
                origParams = fallbackBaseline.Clone();
                if (handle != 0) GripBaseline[handle] = origParams.Clone();
            }
            else if (SteelDataStorage.TryReadParams(solid, out SteelStructureParams readP) && readP != null)
            {
                origParams = readP.Clone();
                if (handle != 0) GripBaseline[handle] = origParams.Clone();
            }

            if (origParams == null) return null;

            HookDocument(solid.Database);
            SteelStructureParams p = origParams.Clone();
            bool rebuildRequired = false;

            Vector3d dirX = new Vector3d(Math.Cos(p.RotationRad), Math.Sin(p.RotationRad), 0.0);
            Vector3d dirY = new Vector3d(-Math.Sin(p.RotationRad), Math.Cos(p.RotationRad), 0.0);

            foreach (int kind in kinds)
            {
                if (kind == 0)
                {
                    p.Origin = p.Origin + offset;
                    rebuildRequired = true;
                }
                else if (kind == 1)
                {
                    double dSpan = offset.DotProduct(dirX);
                    if (p.StructureType == ParametricStructureType.SpiralStair ||
                        p.StructureType == ParametricStructureType.VesselSupport ||
                        p.StructureType == ParametricStructureType.TowerCircularPlatform ||
                        p.StructureType == ParametricStructureType.VerticalVesselSkirt ||
                        p.StructureType == ParametricStructureType.PipeShoeSupport ||
                        p.StructureType == ParametricStructureType.TankRoofHandrail)
                    {
                        p.Span = Math.Max(300.0, Math.Round(p.Span + dSpan * 2.0, 0));
                    }
                    else if (p.StructureType == ParametricStructureType.ArcSteelMember)
                    {
                        p.Span = Math.Max(80.0, Math.Round(p.Span + dSpan, 0));
                    }
                    else
                    {
                        p.Span = Math.Max(200.0, Math.Round(p.Span + dSpan, 0));
                        if (p.StructureType == ParametricStructureType.LinearSteelMember ||
                            p.StructureType == ParametricStructureType.SpiralSteelMember)
                            p.EndPointOffsetZ = Math.Round(p.EndPointOffsetZ + offset.Z, 0);
                    }
                    rebuildRequired = true;
                }
                else if (kind == 2)
                {
                    p.Height = Math.Max(200.0, Math.Round(p.Height + offset.Z, 0));
                    rebuildRequired = true;
                }
                else if (kind == 3)
                {
                    double dLen = offset.DotProduct(dirY);
                    p.Length = Math.Max(150.0, Math.Round(p.Length + dLen, 0));
                    rebuildRequired = true;
                }
            }

            if (rebuildRequired)
            {
                using (Solid3d updated = SteelStructureBuilder.BuildCombinedSolid(p))
                {
                    if (updated != null)
                        SteelStructureBuilder.CopySolidKeepingLook(solid, updated);
                }
            }

            SteelDataStorage.SetPendingEdit(solid, p, handle);

            Database db = solid.Database;
            if (db != null)
            {
                try
                {
                    Transaction topTr = db.TransactionManager.TopTransaction;
                    if (topTr != null && solid.IsWriteEnabled)
                        SteelDataStorage.WriteParams(topTr, solid, p);
                }
                catch { }
            }

            return p;
        }
    }
}
