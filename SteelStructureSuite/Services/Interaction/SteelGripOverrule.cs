using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Services.Modeling;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Services.Interaction
{
    /// <summary>
    /// 参数化夹点复写规则：
    /// - Grip 0: 基准点平移（只改 Origin，不改任何尺寸与角度）
    /// - Grip 1: 长度/跨度/外径正交拉伸夹点（沿构件 X 轴方向正交投影，只改 Span，绝不重置高度、宽度、旋转角或截面）
    /// - Grip 2: 高度正交拉伸夹点（沿 Z 轴正交投影，只改 Height，绝不改其他参数）
    /// - Grip 3: 宽度/进深正交拉伸夹点（沿构件 Y 轴正交投影，只改 Length，绝不改其他参数）
    /// AutoCAD 2019 实际调用的是 GripDataCollection 这一组重载，只写旧的 Point3dCollection 版本夹点不会出现。
    /// </summary>
    public class SteelGripOverrule : GripOverrule
    {
        /// <summary>
        /// 自绘夹点：比系统方块更大，并用黄/红/绿/品红与白色描边，避免被选中高亮的蓝色盖住。
        /// </summary>
        private sealed class ParamGrip : GripData
        {
            public int Kind;

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

            private void DrawGlyph(
                ViewportDraw draw,
                DrawType type,
                Point3d? imageGripPoint,
                int gripSizeInPixels)
            {
                Point3d anchor = type == DrawType.DragImageGrip && imageGripPoint.HasValue
                    ? imageGripPoint.Value
                    : GripPoint;

                Vector3d view = draw.Viewport.ViewDirection;
                if (view.Length < 1e-8) view = Vector3d.ZAxis;
                Vector3d into = view.GetNormal();
                Vector3d xAxis = into.CrossProduct(Vector3d.ZAxis);
                if (xAxis.Length < 1e-6) xAxis = Vector3d.XAxis;
                xAxis = xAxis.GetNormal();
                Vector3d yAxis = xAxis.CrossProduct(into).GetNormal();

                Point2d pixels = draw.Viewport.GetNumPixelsInUnitSquare(anchor);
                double pixelsPerUnit = Math.Max(Math.Abs(pixels.X), 1e-4);
                bool active = type == DrawType.HotGrip || type == DrawType.HoverGrip || type == DrawType.DragImageGrip;
                double half = Math.Max(gripSizeInPixels, 7) * (active ? 1.55 : 1.25) / pixelsPerUnit;
                Point3d center = anchor - into * (half * 1.6);

                short fill = Kind == 0 ? (short)2 : Kind == 1 ? (short)1 : Kind == 2 ? (short)3 : (short)6;
                if (type == DrawType.HotGrip || type == DrawType.DragImageGrip) fill = 30;
                else if (type == DrawType.HoverGrip) fill = 2;

                Fill(draw, center, xAxis, yAxis, half * 1.38, 7);
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
            if (!TryCollectGrips(entity, out Point3d origin, out Point3d spanPt, out bool hasHeight, out Point3d heightPt, out bool hasWidth, out Point3d widthPt))
            {
                base.GetGripPoints(entity, grips, curViewUnitSize, gripSize, curViewDir, bitFlags);
                return;
            }

            grips.Add(MakeGrip(0, origin));
            grips.Add(MakeGrip(1, spanPt));
            if (hasHeight) grips.Add(MakeGrip(2, heightPt));
            if (hasWidth) grips.Add(MakeGrip(3, widthPt));
        }

        public override void GetGripPoints(
            Entity entity,
            Point3dCollection gripPoints,
            IntegerCollection snapModes,
            IntegerCollection geometryIds)
        {
            if (!TryCollectGrips(entity, out Point3d origin, out Point3d spanPt, out bool hasHeight, out Point3d heightPt, out bool hasWidth, out Point3d widthPt))
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
            foreach (GripData grip in grips)
            {
                if (grip is ParamGrip paramGrip)
                    kinds.Add(paramGrip.Kind);
            }

            if (kinds.Count == 0)
            {
                base.MoveGripPointsAt(entity, grips, offset, bitFlags);
                return;
            }

            ApplyGripMove(entity, kinds, offset);
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

            bool hasHeight = p.StructureType != ParametricStructureType.LinearSteelMember;
            bool hasWidth = HasWidthGrip(p.StructureType);
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

            ApplyGripMove(entity, kinds, offset);
        }

        private static ParamGrip MakeGrip(int kind, Point3d point)
        {
            return new ParamGrip
            {
                Kind = kind,
                GripPoint = point
            };
        }

        private static bool TryCollectGrips(
            Entity entity,
            out Point3d origin,
            out Point3d spanPt,
            out bool hasHeight,
            out Point3d heightPt,
            out bool hasWidth,
            out Point3d widthPt)
        {
            origin = Point3d.Origin;
            spanPt = Point3d.Origin;
            heightPt = Point3d.Origin;
            widthPt = Point3d.Origin;
            hasHeight = false;
            hasWidth = false;

            if (!SteelDataStorage.TryReadParams(entity, out SteelStructureParams p))
                return false;

            SteelDataStorage.CacheParamsByHandle(entity, p);

            Vector3d dirX = new Vector3d(Math.Cos(p.RotationRad), Math.Sin(p.RotationRad), 0.0);
            Vector3d dirY = new Vector3d(-Math.Sin(p.RotationRad), Math.Cos(p.RotationRad), 0.0);

            origin = p.Origin;
            bool alongAxis = p.StructureType == ParametricStructureType.LinearSteelMember ||
                             p.StructureType == ParametricStructureType.SpiralSteelMember;
            if (p.StructureType == ParametricStructureType.SpiralStair ||
                p.StructureType == ParametricStructureType.VesselSupport)
            {
                spanPt = p.Origin + dirX * (p.Span * 0.5);
            }
            else if (alongAxis)
            {
                spanPt = p.Origin + dirX * p.Span + new Vector3d(0, 0, p.EndPointOffsetZ);
            }
            else if (p.StructureType == ParametricStructureType.ArcSteelMember)
            {
                spanPt = p.Origin + dirX * p.Span;
            }
            else
            {
                spanPt = p.Origin + dirX * p.Span + new Vector3d(0, 0, p.EndPointOffsetZ);
            }

            hasHeight = p.StructureType != ParametricStructureType.LinearSteelMember &&
                        p.StructureType != ParametricStructureType.ArcSteelMember &&
                        p.StructureType != ParametricStructureType.SpiralSteelMember;
            if (hasHeight)
            {
                double gripHeight = p.Height - SteelStructureBuilder.GetBaseOffsetZ(p);
                heightPt = p.Origin + new Vector3d(0, 0, gripHeight);
            }

            hasWidth = HasWidthGrip(p.StructureType);
            if (hasWidth)
                widthPt = p.Origin + dirY * p.Length;

            return true;
        }

        private static bool HasWidthGrip(ParametricStructureType t)
        {
            return t == ParametricStructureType.SpacePlatform ||
                   t == ParametricStructureType.CantileverWalkway ||
                   t == ParametricStructureType.MultiBaySpaceFrame ||
                   t == ParametricStructureType.PipeBridgeTruss ||
                   t == ParametricStructureType.StraightStair;
        }

        private static void ApplyGripMove(Entity entity, IntegerCollection kinds, Vector3d offset)
        {
            if (!(entity is Solid3d solid) ||
                !SteelDataStorage.TryReadParams(solid, out SteelStructureParams origParams) ||
                kinds == null || kinds.Count == 0)
            {
                return;
            }

            SteelStructureParams p = origParams.Clone();
            bool rebuildRequired = false;

            Vector3d dirX = new Vector3d(Math.Cos(p.RotationRad), Math.Sin(p.RotationRad), 0.0);
            Vector3d dirY = new Vector3d(-Math.Sin(p.RotationRad), Math.Cos(p.RotationRad), 0.0);

            foreach (int kind in kinds)
            {
                if (kind == 0)
                {
                    solid.TransformBy(Matrix3d.Displacement(offset));
                    p.Origin = p.Origin + offset;
                }
                else if (kind == 1)
                {
                    double dSpan = offset.DotProduct(dirX);
                    if (p.StructureType == ParametricStructureType.SpiralStair ||
                        p.StructureType == ParametricStructureType.VesselSupport)
                    {
                        p.Span = Math.Max(600.0, Math.Round(p.Span + dSpan * 2.0, 0));
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
                    p.Length = Math.Max(200.0, Math.Round(p.Length + dLen, 0));
                    rebuildRequired = true;
                }
            }

            SteelDataStorage.CacheParamsByHandle(solid, p);

            if (rebuildRequired)
            {
                using (Solid3d updated = SteelStructureBuilder.BuildCombinedSolid(p))
                {
                    if (updated != null)
                        SteelStructureBuilder.CopySolidKeepingLook(solid, updated);
                }
            }

            Database db = solid.Database;
            if (db != null)
            {
                try
                {
                    Transaction topTr = db.TransactionManager.TopTransaction;
                    if (topTr != null)
                        SteelDataStorage.WriteParams(topTr, solid, p);
                }
                catch { }
            }
        }
    }
}
