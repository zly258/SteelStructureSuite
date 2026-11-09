using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Catalog;

namespace SteelStructureSuite.Services.Modeling
{
    public static class SteelStructureBuilder
    {
        private const string LayerModel3D = "SS_STEEL_3D";
        private const double JointOverlapMm = 22.0;

        public class SolidMemberPart
        {
            public Solid3d Solid { get; set; }
            public string RoleName { get; set; }
            public string ParentTag { get; set; }
            public string SectionName { get; set; }
            public SectionProfile Section { get; set; }
            public bool IsPlate { get; set; }
            public Point3d StartPt { get; set; }
            public Point3d EndPt { get; set; }
            /// <summary>弯曲或折线杆件的扫略中心线。出图按整条路径取轮廓，而不是只取首尾。</summary>
            public List<Point3d> PathPoints { get; set; }
        }

        /// <summary>
        /// 构件局部最低点相对参数原点的偏移（用于让插入基点落在结构实际底面，而不是悬空的绝对标高）。
        /// 目前仅悬挑走道需要：模型中走道位于安装标高，基点上移到最下牛腿斜撑底部。
        /// </summary>
        public static double GetBaseOffsetZ(SteelStructureParams p)
        {
            if (p == null) return 0.0;

            if (p.StructureType == ParametricStructureType.CantileverWalkway)
            {
                double w = Math.Max(p.Length, 200.0);
                double h = Math.Max(p.Height, 250.0);
                double kneeDrop = Math.Min(w * 0.65, Math.Max(h * 0.4, 450.0));
                return Math.Max(0.0, h - kneeDrop);
            }

            return 0.0;
        }

        /// <summary>
        /// 生成轻量级三维预览线段集合（专供 DrawJig 动态移动鼠标或两点线性绘制时 60FPS 零卡顿实时渲染）
        /// </summary>
        public static List<Tuple<Point3d, Point3d>> BuildFastPreviewWireframeLines(SteelStructureParams p)
        {
            var lines = new List<Tuple<Point3d, Point3d>>();
            double span = Math.Max(p.Span, 200.0);
            double len = Math.Max(p.Length, 150.0);
            double h = Math.Max(p.Height, 200.0);
            double subH = Math.Max(p.SubHeight, 80.0);
            double baseOffsetZ = GetBaseOffsetZ(p);

            // 与 BuildCombinedSolid 完全一致的旋转与平移变换矩阵：先局部下移基点，再绕原点旋转，最后平移到 p.Origin
            Matrix3d worldMat = Matrix3d.Displacement(p.Origin - Point3d.Origin) *
                                Matrix3d.Rotation(p.RotationRad, Vector3d.ZAxis, Point3d.Origin) *
                                Matrix3d.Displacement(new Vector3d(0, 0, -baseOffsetZ));

            SectionProfile colSec = SectionCatalogService.GetByName(p.ColumnSection);
            SectionProfile beamSec = SectionCatalogService.GetByName(p.BeamSection);

            Action<Point3d, Point3d> addSeg = (a, b) =>
            {
                lines.Add(Tuple.Create(a.TransformBy(worldMat), b.TransformBy(worldMat)));
            };

            // 辅助函数：绘制带截面厚度的双线杆件轮廓，使预览与实际三维实体外形完全吻合
            Action<Point3d, Point3d, double> addThickMemberXZ = (p0, p1, halfW) =>
            {
                addSeg(p0, p1);
                Vector3d v = p1 - p0;
                if (v.Length > 10.0)
                {
                    Vector3d perp = new Vector3d(-v.Z, 0, v.X).GetNormal() * halfW;
                    addSeg(p0 + perp, p1 + perp);
                    addSeg(p0 - perp, p1 - perp);
                }
            };

            switch (p.StructureType)
            {
                case ParametricStructureType.PortalFrame:
                {
                    double beamZ = h - beamSec.H * 0.5;
                    addThickMemberXZ(new Point3d(0, 0, 0), new Point3d(0, 0, h), colSec.H * 0.5);
                    addThickMemberXZ(new Point3d(span, 0, 0), new Point3d(span, 0, h), colSec.H * 0.5);
                    addThickMemberXZ(new Point3d(-colSec.H * 0.5, 0, beamZ), new Point3d(span + colSec.H * 0.5, 0, beamZ), beamSec.H * 0.5);
                    break;
                }
                case ParametricStructureType.MultiSpanPortalFrame:
                {
                    int spans = Math.Max(2, Math.Min(6, p.BayCount));
                    double baySpan = span / spans;
                    double beamZ = h - beamSec.H * 0.5;
                    for (int i = 0; i <= spans; i++)
                    {
                        double x = i * baySpan;
                        addThickMemberXZ(new Point3d(x, 0, 0), new Point3d(x, 0, h), colSec.H * 0.5);
                    }
                    addThickMemberXZ(new Point3d(-colSec.H * 0.5, 0, beamZ), new Point3d(span + colSec.H * 0.5, 0, beamZ), beamSec.H * 0.5);
                    break;
                }
                case ParametricStructureType.BracedFrame:
                {
                    double beamZ = h - beamSec.H * 0.5;
                    double midZ = Math.Min(Math.Max(subH, h * 0.35), h - 300.0);
                    addThickMemberXZ(new Point3d(0, 0, 0), new Point3d(0, 0, h), colSec.H * 0.5);
                    addThickMemberXZ(new Point3d(span, 0, 0), new Point3d(span, 0, h), colSec.H * 0.5);
                    addThickMemberXZ(new Point3d(0, 0, beamZ), new Point3d(span, 0, beamZ), beamSec.H * 0.5);
                    addThickMemberXZ(new Point3d(0, 0, midZ), new Point3d(span, 0, midZ), beamSec.H * 0.5);
                    addSeg(new Point3d(0, 0, midZ), new Point3d(span * 0.5, 0, beamZ));
                    addSeg(new Point3d(span, 0, midZ), new Point3d(span * 0.5, 0, beamZ));
                    break;
                }
                case ParametricStructureType.DoubleTierFrame:
                {
                    double beamZ = h - beamSec.H * 0.5;
                    double tier1Z = Math.Min(Math.Max(subH, h * 0.45), h - 400.0);
                    double knee = Math.Min(span * 0.2, 450.0);
                    addThickMemberXZ(new Point3d(0, 0, 0), new Point3d(0, 0, h), colSec.H * 0.5);
                    addThickMemberXZ(new Point3d(span, 0, 0), new Point3d(span, 0, h), colSec.H * 0.5);
                    addThickMemberXZ(new Point3d(0, 0, beamZ), new Point3d(span, 0, beamZ), beamSec.H * 0.5);
                    addThickMemberXZ(new Point3d(0, 0, tier1Z), new Point3d(span, 0, tier1Z), beamSec.H * 0.5);
                    addSeg(new Point3d(0, 0, tier1Z - knee), new Point3d(knee, 0, tier1Z));
                    addSeg(new Point3d(span, 0, tier1Z - knee), new Point3d(span - knee, 0, tier1Z));
                    break;
                }
                case ParametricStructureType.MultiBaySpaceFrame:
                {
                    double tier1Z = Math.Min(Math.Max(subH, h * 0.5), h - 500.0);
                    double tier2Z = h - beamSec.H * 0.5;
                    Point3d[] cols = { new Point3d(0, 0, 0), new Point3d(span, 0, 0), new Point3d(0, len, 0), new Point3d(span, len, 0) };
                    foreach (var c in cols) addSeg(c, new Point3d(c.X, c.Y, h));
                    foreach (double z in new[] { tier1Z, tier2Z })
                    {
                        addSeg(new Point3d(0, 0, z), new Point3d(span, 0, z));
                        addSeg(new Point3d(0, len, z), new Point3d(span, len, z));
                        addSeg(new Point3d(0, 0, z), new Point3d(0, len, z));
                        addSeg(new Point3d(span, 0, z), new Point3d(span, len, z));
                        addSeg(new Point3d(0, len * 0.5, z), new Point3d(span, len * 0.5, z));
                    }
                    addSeg(new Point3d(0, 0, 0), new Point3d(0, len, tier1Z));
                    addSeg(new Point3d(span, 0, 0), new Point3d(span, len, tier1Z));
                    break;
                }
                case ParametricStructureType.CantileverBracket:
                {
                    double topZ = h - beamSec.H * 0.5;
                    double braceDrop = Math.Min(Math.Max(subH, 500.0), h - 250.0);
                    addThickMemberXZ(new Point3d(0, 0, 0), new Point3d(0, 0, h), colSec.H * 0.5);
                    addThickMemberXZ(new Point3d(0, 0, topZ), new Point3d(span, 0, topZ), beamSec.H * 0.5);
                    addSeg(new Point3d(0, 0, topZ - braceDrop), new Point3d(span * 0.88, 0, topZ));
                    break;
                }
                case ParametricStructureType.SpacePlatform:
                {
                    double railTopZ = h + Math.Max(subH, 1050.0);
                    Point3d[] cols = { new Point3d(0, 0, 0), new Point3d(span, 0, 0), new Point3d(span, len, 0), new Point3d(0, len, 0) };
                    for (int i = 0; i < 4; i++)
                    {
                        Point3d a = cols[i];
                        Point3d b = cols[(i + 1) % 4];
                        addSeg(a, new Point3d(a.X, a.Y, railTopZ));
                        addSeg(new Point3d(a.X, a.Y, h), new Point3d(b.X, b.Y, h));
                        addSeg(new Point3d(a.X, a.Y, railTopZ), new Point3d(b.X, b.Y, railTopZ));
                    }
                    addSeg(new Point3d(0, len / 3.0, h), new Point3d(span, len / 3.0, h));
                    addSeg(new Point3d(0, len * 2.0 / 3.0, h), new Point3d(span, len * 2.0 / 3.0, h));
                    break;
                }
                case ParametricStructureType.CantileverWalkway:
                {
                    int bCount = Math.Max(2, (int)Math.Ceiling(span / 1800.0) + 1);
                    double pitch = span / (bCount - 1);
                    double rH = Math.Max(subH, 1050.0);
                    double kneeDrop = Math.Min(len * 0.65, Math.Max(h * 0.4, 450.0));
                    for (int i = 0; i < bCount; i++)
                    {
                        double x = i * pitch;
                        addSeg(new Point3d(x, 0, h), new Point3d(x, len, h));
                        addSeg(new Point3d(x, 0, h - kneeDrop), new Point3d(x, len * 0.85, h));
                        addSeg(new Point3d(x, len, h), new Point3d(x, len, h + rH));
                    }
                    addSeg(new Point3d(0, 0, h), new Point3d(span, 0, h));
                    addSeg(new Point3d(0, len, h), new Point3d(span, len, h));
                    addSeg(new Point3d(0, len, h + rH), new Point3d(span, len, h + rH));
                    addSeg(new Point3d(0, len, h + rH * 0.52), new Point3d(span, len, h + rH * 0.52));
                    break;
                }
                case ParametricStructureType.RoofTruss:
                {
                    double ridgeZ = h + Math.Max(subH, 600.0);
                    double midX = span * 0.5;
                    Point3d ridgePt = new Point3d(midX, 0, ridgeZ);
                    addThickMemberXZ(new Point3d(0, 0, 0), new Point3d(0, 0, h), colSec.H * 0.5);
                    addThickMemberXZ(new Point3d(span, 0, 0), new Point3d(span, 0, h), colSec.H * 0.5);
                    addSeg(new Point3d(0, 0, h), new Point3d(span, 0, h));
                    addSeg(new Point3d(0, 0, h), ridgePt);
                    addSeg(new Point3d(span, 0, h), ridgePt);
                    addSeg(new Point3d(midX, 0, h), ridgePt);
                    double qZ = h + (ridgeZ - h) * 0.5;
                    addSeg(new Point3d(span * 0.25, 0, h), new Point3d(span * 0.25, 0, qZ));
                    addSeg(new Point3d(span * 0.75, 0, h), new Point3d(span * 0.75, 0, qZ));
                    addSeg(new Point3d(span * 0.25, 0, qZ), new Point3d(midX, 0, h));
                    addSeg(new Point3d(span * 0.75, 0, qZ), new Point3d(midX, 0, h));
                    break;
                }
                case ParametricStructureType.PipeBridgeTruss:
                {
                    // 基点位于桁架底弦，插入点即是结构底面
                    double zBot = 0.0;
                    double zTop = Math.Max(h, 1200.0);
                    int panels = Math.Max(3, (int)Math.Round(span / Math.Max(h, 1500.0)));
                    double panelLen = span / panels;
                    addSeg(new Point3d(0, 0, zBot), new Point3d(span, 0, zBot));
                    addSeg(new Point3d(0, len, zBot), new Point3d(span, len, zBot));
                    addSeg(new Point3d(0, 0, zTop), new Point3d(span, 0, zTop));
                    addSeg(new Point3d(0, len, zTop), new Point3d(span, len, zTop));
                    for (int i = 0; i <= panels; i++)
                    {
                        double x = i * panelLen;
                        addSeg(new Point3d(x, 0, zBot), new Point3d(x, 0, zTop));
                        addSeg(new Point3d(x, len, zBot), new Point3d(x, len, zTop));
                        addSeg(new Point3d(x, 0, zBot), new Point3d(x, len, zBot));
                        addSeg(new Point3d(x, 0, zTop), new Point3d(x, len, zTop));
                        if (i < panels)
                        {
                            addSeg(new Point3d(x, 0, zBot), new Point3d((i + 1) * panelLen, 0, zTop));
                            addSeg(new Point3d(x, len, zBot), new Point3d((i + 1) * panelLen, len, zTop));
                        }
                    }
                    break;
                }
                case ParametricStructureType.SpiralStair:
                {
                    double outerR = Math.Max(span * 0.5, 900.0);
                    double innerR = Math.Max(outerR - len, 350.0);
                    double totalRad = Math.Max(subH, 90.0) * Math.PI / 180.0;
                    int stepCount = Math.Max(8, (int)Math.Round(h / 220.0));
                    double handrailH = 1050.0;
                    for (int i = 0; i < stepCount; i++)
                    {
                        double t0 = (double)i / stepCount;
                        double t1 = (double)(i + 1) / stepCount;
                        double a0 = t0 * totalRad, a1 = t1 * totalRad;
                        double z0 = t0 * h, z1 = t1 * h;
                        addSeg(new Point3d(innerR * Math.Cos(a0), innerR * Math.Sin(a0), z0),
                               new Point3d(innerR * Math.Cos(a1), innerR * Math.Sin(a1), z1));
                        addSeg(new Point3d(outerR * Math.Cos(a0), outerR * Math.Sin(a0), z0),
                               new Point3d(outerR * Math.Cos(a1), outerR * Math.Sin(a1), z1));
                        addSeg(new Point3d(innerR * Math.Cos(a1), innerR * Math.Sin(a1), z1),
                               new Point3d(outerR * Math.Cos(a1), outerR * Math.Sin(a1), z1));
                        addSeg(new Point3d(outerR * Math.Cos(a1), outerR * Math.Sin(a1), z1),
                               new Point3d(outerR * Math.Cos(a1), outerR * Math.Sin(a1), z1 + handrailH));
                        addSeg(new Point3d(outerR * Math.Cos(a0), outerR * Math.Sin(a0), z0 + handrailH),
                               new Point3d(outerR * Math.Cos(a1), outerR * Math.Sin(a1), z1 + handrailH));
                    }
                    break;
                }
                case ParametricStructureType.StraightStair:
                {
                    double w = Math.Max(len, 650.0);
                    double rH = Math.Max(subH, 1000.0);
                    addSeg(new Point3d(0, 0, 0), new Point3d(span, 0, h));
                    addSeg(new Point3d(0, w, 0), new Point3d(span, w, h));
                    addSeg(new Point3d(0, 0, rH), new Point3d(span, 0, h + rH));
                    addSeg(new Point3d(0, w, rH), new Point3d(span, w, h + rH));
                    addSeg(new Point3d(0, 0, 0), new Point3d(0, 0, rH));
                    addSeg(new Point3d(0, w, 0), new Point3d(0, w, rH));
                    addSeg(new Point3d(span, 0, h), new Point3d(span, 0, h + rH));
                    addSeg(new Point3d(span, w, h), new Point3d(span, w, h + rH));
                    int steps = Math.Max(4, (int)Math.Round(h / 200.0));
                    for (int i = 1; i <= steps; i++)
                    {
                        double t = (double)i / steps;
                        addSeg(new Point3d(span * t, 0, h * t), new Point3d(span * t, w, h * t));
                    }
                    break;
                }
                case ParametricStructureType.SafetyCagedLadder:
                {
                    double w = Math.Max(span, 500.0);
                    double topExt = 1100.0;
                    addSeg(new Point3d(0, 0, 0), new Point3d(0, 0, h + topExt));
                    addSeg(new Point3d(w, 0, 0), new Point3d(w, 0, h + topExt));
                    int rungs = Math.Max(4, (int)(h / 300.0));
                    for (int i = 1; i <= rungs; i++)
                        addSeg(new Point3d(0, 0, i * 300.0), new Point3d(w, 0, i * 300.0));
                    double startZ = Math.Min(Math.Max(subH, 2000.0), h - 400.0);
                    double cageR = Math.Max(len * 0.5, 350.0);
                    addSeg(new Point3d(w * 0.5, -cageR * 1.7, startZ), new Point3d(w * 0.5, -cageR * 1.7, h + topExt));
                    break;
                }
                case ParametricStructureType.HandrailGuardrail:
                {
                    double spacing = Math.Max(subH, 900.0);
                    if (spacing > 1500.0) spacing = 1100.0;
                    int postCount = Math.Max(2, (int)Math.Ceiling(span / spacing) + 1);
                    double pitch = span / (postCount - 1);
                    addSeg(new Point3d(0, 0, h), new Point3d(span, 0, h));
                    addSeg(new Point3d(0, 0, h * 0.52), new Point3d(span, 0, h * 0.52));
                    addSeg(new Point3d(0, 0, 80), new Point3d(span, 0, 80));
                    for (int i = 0; i < postCount; i++)
                    {
                        double x = i * pitch;
                        addSeg(new Point3d(x, 0, 0), new Point3d(x, 0, h));
                    }
                    break;
                }
                case ParametricStructureType.ArcSteelMember:
                {
                    double radius = Math.Max(span, 80.0);
                    double sweep = Math.Min(330.0, Math.Max(15.0, subH)) * Math.PI / 180.0;
                    int n = Math.Max(8, (int)Math.Ceiling(Math.Min(330.0, Math.Max(15.0, subH)) / 8.0));
                    Point3d prev = new Point3d(radius, 0, 0);
                    for (int i = 1; i <= n; i++)
                    {
                        double a = sweep * i / n;
                        Point3d cur = new Point3d(radius * Math.Cos(a), radius * Math.Sin(a), 0);
                        addSeg(prev, cur);
                        prev = cur;
                    }
                    break;
                }
                case ParametricStructureType.SpiralSteelMember:
                {
                    double radius = Math.Max(len, 30.0);
                    double turns = SpiralTurns(subH);
                    double axisLen = Math.Sqrt(span * span + p.EndPointOffsetZ * p.EndPointOffsetZ);
                    if (axisLen < 50.0) axisLen = Math.Max(span, 200.0);
                    Vector3d axis = new Vector3d(span, 0, p.EndPointOffsetZ);
                    if (axis.Length < 10.0) axis = Vector3d.XAxis;
                    axis = axis.GetNormal();
                    Vector3d side = Math.Abs(axis.Z) > 0.9
                        ? Vector3d.YAxis
                        : Vector3d.ZAxis.CrossProduct(axis).GetNormal();
                    Vector3d binormal = axis.CrossProduct(side).GetNormal();
                    int n = Math.Max(16, (int)Math.Ceiling(turns * 16.0));
                    Point3d prev = Point3d.Origin + side * radius;
                    for (int i = 1; i <= n; i++)
                    {
                        double t = (double)i / n;
                        double a = t * turns * Math.PI * 2.0;
                        Point3d cur = Point3d.Origin + axis * (axisLen * t)
                            + side * (radius * Math.Cos(a))
                            + binormal * (radius * Math.Sin(a));
                        addSeg(prev, cur);
                        prev = cur;
                    }
                    break;
                }
                case ParametricStructureType.LinearSteelMember:
                {
                    Point3d p0 = Point3d.Origin;
                    Point3d p1 = new Point3d(span, 0, p.EndPointOffsetZ);
                    addSeg(p0, p1);
                    double halfH = colSec.H * 0.5;
                    double halfB = colSec.B * 0.5;
                    addSeg(p0 + new Vector3d(0, -halfB, -halfH), p1 + new Vector3d(0, -halfB, -halfH));
                    addSeg(p0 + new Vector3d(0, halfB, -halfH), p1 + new Vector3d(0, halfB, -halfH));
                    addSeg(p0 + new Vector3d(0, -halfB, halfH), p1 + new Vector3d(0, -halfB, halfH));
                    addSeg(p0 + new Vector3d(0, halfB, halfH), p1 + new Vector3d(0, halfB, halfH));
                    break;
                }
                case ParametricStructureType.VesselSupport:
                {
                    double r = Math.Max(span * 0.5, 500.0);
                    double w = Math.Max(len, 300.0);
                    double baseW = r * 1.55;
                    addSeg(new Point3d(-baseW * 0.5, -w * 0.5, 0), new Point3d(baseW * 0.5, -w * 0.5, 0));
                    addSeg(new Point3d(baseW * 0.5, -w * 0.5, 0), new Point3d(baseW * 0.5, w * 0.5, 0));
                    addSeg(new Point3d(baseW * 0.5, w * 0.5, 0), new Point3d(-baseW * 0.5, w * 0.5, 0));
                    addSeg(new Point3d(-baseW * 0.5, w * 0.5, 0), new Point3d(-baseW * 0.5, -w * 0.5, 0));
                    double halfAngle = Math.Min(Math.Max(subH, 90.0), 150.0) * 0.5 * Math.PI / 180.0;
                    int arcSegs = 6;
                    for (int i = 0; i < arcSegs; i++)
                    {
                        double a0 = -Math.PI * 0.5 - halfAngle + (2.0 * halfAngle * i / arcSegs);
                        double a1 = -Math.PI * 0.5 - halfAngle + (2.0 * halfAngle * (i + 1) / arcSegs);
                        addSeg(new Point3d(r * Math.Cos(a0), 0, h + r * Math.Sin(a0)),
                               new Point3d(r * Math.Cos(a1), 0, h + r * Math.Sin(a1)));
                    }
                    break;
                }
            }

            return lines;
        }

        /// <summary>
        /// 构建用于 HLR 详图投影的未旋转局部零件实体列表（局部原点为 (0,0,0)）
        /// </summary>
        public static List<SolidMemberPart> BuildDetailedParts(SteelStructureParams p)
        {
            var parts = new List<SolidMemberPart>();
            SectionProfile colProf = SectionCatalogService.GetByName(p.ColumnSection);
            SectionProfile beamProf = SectionCatalogService.GetByName(p.BeamSection);
            SectionProfile braceProf = SectionCatalogService.GetByName(p.BraceSection);

            double span = Math.Max(p.Span, 250.0);
            double len = Math.Max(p.Length, 200.0);
            double h = Math.Max(p.Height, 250.0);
            double subH = Math.Max(p.SubHeight, 80.0);

            switch (p.StructureType)
            {
                case ParametricStructureType.PortalFrame:
                    BuildPortalFrameParts(parts, colProf, beamProf, span, h);
                    break;
                case ParametricStructureType.MultiSpanPortalFrame:
                    BuildMultiSpanPortalFrameParts(parts, colProf, beamProf, braceProf, span, h, Math.Max(2, p.BayCount));
                    break;
                case ParametricStructureType.BracedFrame:
                    BuildBracedFrameParts(parts, colProf, beamProf, braceProf, span, h, subH);
                    break;
                case ParametricStructureType.DoubleTierFrame:
                    BuildDoubleTierFrameParts(parts, colProf, beamProf, braceProf, span, h, subH);
                    break;
                case ParametricStructureType.MultiBaySpaceFrame:
                    BuildMultiBaySpaceFrameParts(parts, colProf, beamProf, braceProf, span, len, h, subH);
                    break;
                case ParametricStructureType.CantileverBracket:
                    BuildCantileverBracketParts(parts, colProf, beamProf, braceProf, span, h, subH);
                    break;
                case ParametricStructureType.SpacePlatform:
                    BuildSpacePlatformParts(parts, colProf, beamProf, braceProf, span, len, h, subH);
                    break;
                case ParametricStructureType.CantileverWalkway:
                    BuildCantileverWalkwayParts(parts, colProf, beamProf, braceProf, span, len, h, subH);
                    break;
                case ParametricStructureType.RoofTruss:
                    BuildRoofTrussParts(parts, colProf, beamProf, braceProf, span, h, subH);
                    break;
                case ParametricStructureType.PipeBridgeTruss:
                    BuildPipeBridgeTrussParts(parts, colProf, beamProf, braceProf, span, len, h, subH);
                    break;
                case ParametricStructureType.SpiralStair:
                    BuildSpiralStairParts(parts, colProf, beamProf, braceProf, span, len, h, subH);
                    break;
                case ParametricStructureType.StraightStair:
                    BuildStraightStairParts(parts, colProf, beamProf, braceProf, span, len, h, subH);
                    break;
                case ParametricStructureType.SafetyCagedLadder:
                    BuildSafetyCagedLadderParts(parts, colProf, beamProf, braceProf, span, len, h, subH);
                    break;
                case ParametricStructureType.HandrailGuardrail:
                    BuildHandrailGuardrailParts(parts, colProf, beamProf, braceProf, span, h, subH);
                    break;
                case ParametricStructureType.LinearSteelMember:
                    BuildLinearSteelMemberParts(parts, colProf, span, p.EndPointOffsetZ);
                    break;
                case ParametricStructureType.ArcSteelMember:
                    BuildArcSteelMemberParts(parts, colProf, span, subH, p.EndPointOffsetZ);
                    break;
                case ParametricStructureType.SpiralSteelMember:
                    BuildSpiralSteelMemberParts(parts, colProf, span, len, subH, p.EndPointOffsetZ);
                    break;
                case ParametricStructureType.VesselSupport:
                    BuildVesselSupportParts(parts, colProf, beamProf, braceProf, span, len, h, subH);
                    break;
            }

            TrimIncomingEnds(parts);

            double baseOffsetZ = GetBaseOffsetZ(p);
            Vector3d shift = new Vector3d(0, 0, -baseOffsetZ);
            foreach (var part in parts)
            {
                part.ParentTag = p.Tag ?? "";
                if (baseOffsetZ > 1e-6)
                {
                    part.Solid?.TransformBy(Matrix3d.Displacement(shift));
                    part.StartPt += shift;
                    part.EndPt += shift;
                    if (part.PathPoints != null)
                    {
                        for (int i = 0; i < part.PathPoints.Count; i++)
                            part.PathPoints[i] += shift;
                    }
                }
            }

            return parts;
        }

        /// <summary>
        /// 构建连续贯通的三维组合实体并自动变换至 p.Origin 与 p.RotationRad（高速两阶段合并，杜绝卡顿）
        /// </summary>
        public static Solid3d BuildCombinedSolid(SteelStructureParams p)
        {
            List<SolidMemberPart> parts = BuildDetailedParts(p);
            Solid3d master = null;

            foreach (var part in parts)
            {
                if (part?.Solid == null) continue;
                if (master == null)
                {
                    master = part.Solid;
                }
                else
                {
                    try
                    {
                        master.BooleanOperation(BooleanOperationType.BoolUnite, part.Solid);
                    }
                    catch { }
                    part.Solid.Dispose();
                }
            }

            if (master != null)
            {
                if (Math.Abs(p.RotationRad) > 1e-6)
                {
                    master.TransformBy(Matrix3d.Rotation(p.RotationRad, Vector3d.ZAxis, Point3d.Origin));
                }
                if (p.Origin != Point3d.Origin)
                {
                    master.TransformBy(Matrix3d.Displacement(p.Origin - Point3d.Origin));
                }
            }

            return master;
        }

        // ==================== 1. 单跨门式刚架 ====================
        private static void BuildPortalFrameParts(
            List<SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            double span,
            double h)
        {
            double colTop = Math.Max(200.0, h - beamProf.H);
            AddMember(parts, "左立柱", colProf, new Point3d(0, 0, 0), new Point3d(0, 0, colTop));
            AddMember(parts, "右立柱", colProf, new Point3d(span, 0, 0), new Point3d(span, 0, colTop));

            double beamZ = h - beamProf.H * 0.5;
            double halfColH = colProf.H * 0.5;
            AddMember(parts, "顶部主梁", beamProf,
                new Point3d(-halfColH, 0, beamZ),
                new Point3d(span + halfColH, 0, beamZ));

            AddBasePlate(parts, "左柱脚底板", new Point3d(0, 0, 0), colProf.H + 100, colProf.B + 100, 20);
            AddBasePlate(parts, "右柱脚底板", new Point3d(span, 0, 0), colProf.H + 100, colProf.B + 100, 20);
        }

        // ==================== 2. 多跨连续门式刚架 (新增) ====================
        private static void BuildMultiSpanPortalFrameParts(
            List<SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            SectionProfile braceProf,
            double totalSpan,
            double h,
            int bayCount)
        {
            int spans = Math.Max(2, Math.Min(6, bayCount));
            double baySpan = totalSpan / spans;
            double beamZ = h - beamProf.H * 0.5;
            double halfColH = colProf.H * 0.5;

            for (int i = 0; i <= spans; i++)
            {
                double x = i * baySpan;
                string role = (i == 0 || i == spans) ? "边立柱" : "中立柱";
                double colTop = Math.Max(200.0, h - beamProf.H);
                AddMember(parts, role, colProf, new Point3d(x, 0, 0), new Point3d(x, 0, colTop));
                AddBasePlate(parts, "柱脚底板", new Point3d(x, 0, 0), colProf.H + 100, colProf.B + 100, 20);
            }

            // 连续贯通顶梁
            AddMember(parts, "连续主梁", beamProf,
                new Point3d(-halfColH, 0, beamZ),
                new Point3d(totalSpan + halfColH, 0, beamZ));

            // 隅撑 (Knee Braces)
            double kneeOffset = Math.Min(baySpan * 0.18, h * 0.22);
            for (int i = 0; i <= spans; i++)
            {
                double x = i * baySpan;
                if (i < spans)
                {
                    AddMemberExtended(parts, "梁柱隅撑", braceProf,
                        new Point3d(x, 0, h - kneeOffset),
                        new Point3d(x + kneeOffset, 0, beamZ), JointOverlapMm);
                }
                if (i > 0)
                {
                    AddMemberExtended(parts, "梁柱隅撑", braceProf,
                        new Point3d(x, 0, h - kneeOffset),
                        new Point3d(x - kneeOffset, 0, beamZ), JointOverlapMm);
                }
            }
        }

        // ==================== 3. 带人字斜撑框架 ====================
        private static void BuildBracedFrameParts(
            List<SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            SectionProfile braceProf,
            double span,
            double h,
            double subH)
        {
            BuildPortalFrameParts(parts, colProf, beamProf, span, h);
            double midZ = Math.Min(Math.Max(subH, h * 0.35), h - 300.0);
            double beamZ = h - beamProf.H * 0.5;

            AddMember(parts, "中部横梁", beamProf, new Point3d(0, 0, midZ), new Point3d(span, 0, midZ));
            Point3d midTop = new Point3d(span * 0.5, 0, beamZ);
            AddMemberExtended(parts, "左人字斜撑", braceProf, new Point3d(0, 0, midZ), midTop, JointOverlapMm);
            AddMemberExtended(parts, "右人字斜撑", braceProf, new Point3d(span, 0, midZ), midTop, JointOverlapMm);
        }

        // ==================== 4. 双层管廊支架 ====================
        private static void BuildDoubleTierFrameParts(
            List<SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            SectionProfile braceProf,
            double span,
            double h,
            double subH)
        {
            BuildPortalFrameParts(parts, colProf, beamProf, span, h);
            double tier1Z = Math.Min(Math.Max(subH, h * 0.45), h - 400.0);
            AddMember(parts, "一层管廊横梁", beamProf,
                new Point3d(-colProf.H * 0.5, 0, tier1Z),
                new Point3d(span + colProf.H * 0.5, 0, tier1Z));

            double knee = Math.Min(span * 0.2, 450.0);
            AddMemberExtended(parts, "左斜隅撑", braceProf,
                new Point3d(0, 0, tier1Z - knee), new Point3d(knee, 0, tier1Z), JointOverlapMm);
            AddMemberExtended(parts, "右斜隅撑", braceProf,
                new Point3d(span, 0, tier1Z - knee), new Point3d(span - knee, 0, tier1Z), JointOverlapMm);
        }

        // ==================== 5. 多层空间装置钢框架 (新增) ====================
        private static void BuildMultiBaySpaceFrameParts(
            List<SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            SectionProfile braceProf,
            double span,
            double len,
            double h,
            double subH)
        {
            double tier1Z = Math.Min(Math.Max(subH, h * 0.5), h - 500.0);
            double tier2Z = h - beamProf.H * 0.5;

            Point3d[] cols =
            {
                new Point3d(0, 0, 0),
                new Point3d(span, 0, 0),
                new Point3d(0, len, 0),
                new Point3d(span, len, 0)
            };

            foreach (var c in cols)
            {
                AddMember(parts, "框架立柱", colProf, c, new Point3d(c.X, c.Y, Math.Max(200.0, h - beamProf.H)));
                AddBasePlate(parts, "柱脚底板", c, colProf.H + 100, colProf.B + 100, 20);
            }

            foreach (double z in new[] { tier1Z, tier2Z })
            {
                string prefix = Math.Abs(z - tier1Z) < 1.0 ? "一层" : "顶层";
                // X 向主梁
                AddMember(parts, prefix + "横向主梁", beamProf, new Point3d(0, 0, z), new Point3d(span, 0, z));
                AddMember(parts, prefix + "横向主梁", beamProf, new Point3d(0, len, z), new Point3d(span, len, z));
                // Y 向纵向联系梁
                AddMember(parts, prefix + "纵向主梁", beamProf, new Point3d(0, 0, z), new Point3d(0, len, z));
                AddMember(parts, prefix + "纵向主梁", beamProf, new Point3d(span, 0, z), new Point3d(span, len, z));
                // 中间次梁
                AddMember(parts, prefix + "平台次梁", braceProf, new Point3d(0, len * 0.5, z), new Point3d(span, len * 0.5, z));
            }

            // 侧面纵向柱间交叉斜撑
            AddMemberExtended(parts, "柱间斜撑", braceProf, new Point3d(0, 0, 0), new Point3d(0, len, tier1Z), JointOverlapMm);
            AddMemberExtended(parts, "柱间斜撑", braceProf, new Point3d(span, 0, 0), new Point3d(span, len, tier1Z), JointOverlapMm);
        }

        // ==================== 6. 悬臂三角托架 ====================
        private static void BuildCantileverBracketParts(
            List<SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            SectionProfile braceProf,
            double span,
            double h,
            double subH)
        {
            AddMember(parts, "附墙立柱", colProf, new Point3d(0, 0, 0), new Point3d(0, 0, h));
            double topZ = h - beamProf.H * 0.5;
            double braceDrop = Math.Min(Math.Max(subH, 500.0), h - 250.0);

            AddMemberExtended(parts, "水平悬挑主梁", beamProf,
                new Point3d(-colProf.H * 0.4, 0, topZ),
                new Point3d(span, 0, topZ), JointOverlapMm);

            AddMemberExtended(parts, "斜撑杆", braceProf,
                new Point3d(0, 0, topZ - braceDrop),
                new Point3d(span * 0.88, 0, topZ), JointOverlapMm);

            AddBasePlate(parts, "柱脚锚固板", new Point3d(0, 0, 0), colProf.H + 100, colProf.B + 100, 20);
        }

        // ==================== 7. 四柱空间检修钢平台 (带双层护栏与踢脚板) ====================
        private static void BuildSpacePlatformParts(
            List<SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            SectionProfile braceProf,
            double span,
            double len,
            double h,
            double handrailH)
        {
            Point3d[] colBases =
            {
                new Point3d(0, 0, 0),
                new Point3d(span, 0, 0),
                new Point3d(span, len, 0),
                new Point3d(0, len, 0)
            };

            double beamZ = h - beamProf.H * 0.5;
            double railTopZ = h + Math.Max(handrailH, 1050.0);

            for (int i = 0; i < 4; i++)
            {
                AddMember(parts, "平台立柱", colProf, colBases[i], new Point3d(colBases[i].X, colBases[i].Y, Math.Max(200.0, h - beamProf.H)));
                AddBasePlate(parts, "柱脚底板", colBases[i], colProf.H + 100, colProf.B + 100, 20);
            }

            // 四周框架主梁
            AddMember(parts, "横向主梁", beamProf, new Point3d(0, 0, beamZ), new Point3d(span, 0, beamZ));
            AddMember(parts, "横向主梁", beamProf, new Point3d(0, len, beamZ), new Point3d(span, len, beamZ));
            AddMember(parts, "纵向边梁", beamProf, new Point3d(0, 0, beamZ), new Point3d(0, len, beamZ));
            AddMember(parts, "纵向边梁", beamProf, new Point3d(span, 0, beamZ), new Point3d(span, len, beamZ));

            // 平台次梁
            int subCount = 2;
            for (int i = 1; i <= subCount; i++)
            {
                double y = len * i / (subCount + 1);
                AddMember(parts, "平台次梁", braceProf,
                    new Point3d(0, y, h - braceProf.H * 0.5),
                    new Point3d(span, y, h - braceProf.H * 0.5));
            }

            // 平台铺板
            AddBasePlate(parts, "平台花纹钢板", new Point3d(span * 0.5, len * 0.5, h), span, len, 8.0);

            // 顶部连续安全栏杆扶手 (圆管 P42x3.5)
            SectionProfile pipeProf = SectionCatalogService.GetByName("P42x3.5");
            double midRailZ = h + (railTopZ - h) * 0.5;
            for (int i = 0; i < 4; i++)
            {
                Point3d a = colBases[i];
                Point3d b = colBases[(i + 1) % 4];
                AddMemberExtended(parts, "护栏立柱", pipeProf, new Point3d(a.X, a.Y, h), new Point3d(a.X, a.Y, railTopZ), 15.0);
                AddMemberExtended(parts, "护栏中柱", pipeProf, new Point3d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, h), new Point3d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, railTopZ), 15.0);
            }

            double elbowR = Math.Max(pipeProf.H * 2.2, 80.0);
            var topRail = new List<Point3d>();
            var midRail = new List<Point3d>();
            for (int i = 0; i < 4; i++)
            {
                topRail.Add(new Point3d(colBases[i].X, colBases[i].Y, railTopZ));
                midRail.Add(new Point3d(colBases[i].X, colBases[i].Y, midRailZ));
            }
            AddContinuousMember(parts, "顶部扶手管", pipeProf, topRail, elbowR, smooth: false, closed: true);
            AddContinuousMember(parts, "中部横杆", pipeProf, midRail, elbowR, smooth: false, closed: true);
        }

        // ==================== 8. 悬挑走道平台 (带外侧连续安全栏杆，支持线性绘制，新增) ====================
        private static void BuildCantileverWalkwayParts(
            List<SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            SectionProfile braceProf,
            double walkwayLength,
            double walkwayWidth,
            double elevationH,
            double railHeight)
        {
            int bracketCount = Math.Max(2, (int)Math.Ceiling(walkwayLength / 1800.0) + 1);
            double pitch = walkwayLength / (bracketCount - 1);
            double rH = Math.Max(railHeight, 1050.0);
            double kneeDrop = Math.Min(walkwayWidth * 0.65, Math.Max(elevationH * 0.4, 450.0));
            SectionProfile pipeProf = SectionCatalogService.GetByName("P42x3.5");

            for (int i = 0; i < bracketCount; i++)
            {
                double x = i * pitch;
                // 牛腿横向悬挑梁 (沿 Y 方向从 0 到 walkwayWidth)
                AddMemberExtended(parts, "悬挑牛腿主梁", colProf,
                    new Point3d(x, 0, elevationH - colProf.H * 0.5),
                    new Point3d(x, walkwayWidth, elevationH - colProf.H * 0.5), 15.0);
                // 三角斜撑
                AddMemberExtended(parts, "牛腿三角斜撑", braceProf,
                    new Point3d(x, 0, elevationH - kneeDrop),
                    new Point3d(x, walkwayWidth * 0.85, elevationH - colProf.H * 0.5), 20.0);
                // 附墙埋件竖板
                AddBasePlate(parts, "附墙锚固板", new Point3d(x, 0, elevationH - kneeDrop * 0.5), 180, 16, kneeDrop + 140);
                // 外侧护栏立柱
                AddMemberExtended(parts, "走道栏杆立柱", pipeProf,
                    new Point3d(x, walkwayWidth, elevationH),
                    new Point3d(x, walkwayWidth, elevationH + rH), 15.0);
            }

            // 内外纵向走道边梁 (沿 X 方向贯通)
            AddMemberExtended(parts, "走道内侧纵梁", beamProf,
                new Point3d(0, 0, elevationH - beamProf.H * 0.5),
                new Point3d(walkwayLength, 0, elevationH - beamProf.H * 0.5), 15.0);
            AddMemberExtended(parts, "走道外侧纵梁", beamProf,
                new Point3d(0, walkwayWidth, elevationH - beamProf.H * 0.5),
                new Point3d(walkwayLength, walkwayWidth, elevationH - beamProf.H * 0.5), 15.0);

            // 走道踏步铺板
            AddBasePlate(parts, "走道钢格栅板",
                new Point3d(walkwayLength * 0.5, walkwayWidth * 0.5, elevationH),
                walkwayLength, walkwayWidth, 8.0);

            // 外侧连续扶手、中横杆与踢脚板
            AddMemberExtended(parts, "外侧顶部扶手", pipeProf,
                new Point3d(0, walkwayWidth, elevationH + rH),
                new Point3d(walkwayLength, walkwayWidth, elevationH + rH), 20.0);
            AddMemberExtended(parts, "外侧中部横杆", pipeProf,
                new Point3d(0, walkwayWidth, elevationH + rH * 0.52),
                new Point3d(walkwayLength, walkwayWidth, elevationH + rH * 0.52), 20.0);
            AddBasePlate(parts, "外侧踢脚板",
                new Point3d(walkwayLength * 0.5, walkwayWidth, elevationH),
                walkwayLength, 6.0, 100.0);
        }

        // ==================== 9. 双坡屋面钢桁架 ====================
        private static void BuildRoofTrussParts(
            List<SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile chordProf,
            SectionProfile webProf,
            double span,
            double eaveH,
            double riseH)
        {
            double ridgeZ = eaveH + Math.Max(riseH, 600.0);
            double midX = span * 0.5;

            double colTop = Math.Max(200.0, eaveH - chordProf.H * 0.5);
            AddMember(parts, "左桁架柱", colProf, new Point3d(0, 0, 0), new Point3d(0, 0, colTop));
            AddMember(parts, "右桁架柱", colProf, new Point3d(span, 0, 0), new Point3d(span, 0, colTop));
            AddMemberExtended(parts, "桁架下弦杆", chordProf, new Point3d(0, 0, eaveH), new Point3d(span, 0, eaveH), JointOverlapMm);

            Point3d ridgePt = new Point3d(midX, 0, ridgeZ);
            AddContinuousMember(parts, "双坡上弦杆", chordProf, new List<Point3d>
            {
                new Point3d(0, 0, eaveH),
                ridgePt,
                new Point3d(span, 0, eaveH)
            }, bendRadius: 0.0, smooth: false, closed: false);
            AddMemberExtended(parts, "中竖腹杆", webProf, new Point3d(midX, 0, eaveH), ridgePt, JointOverlapMm);

            double q1X = span * 0.25;
            double q3X = span * 0.75;
            double qZ = eaveH + (ridgeZ - eaveH) * 0.5;
            AddMemberExtended(parts, "左竖腹杆", webProf, new Point3d(q1X, 0, eaveH), new Point3d(q1X, 0, qZ), JointOverlapMm);
            AddMemberExtended(parts, "右竖腹杆", webProf, new Point3d(q3X, 0, eaveH), new Point3d(q3X, 0, qZ), JointOverlapMm);
            AddMemberExtended(parts, "左斜腹杆", webProf, new Point3d(q1X, 0, qZ), new Point3d(midX, 0, eaveH), JointOverlapMm);
            AddMemberExtended(parts, "右斜腹杆", webProf, new Point3d(q3X, 0, qZ), new Point3d(midX, 0, eaveH), JointOverlapMm);
        }

        // ==================== 10. 空间管廊桁架桥 (支持两点线性绘制，新增) ====================
        private static void BuildPipeBridgeTrussParts(
            List<SolidMemberPart> parts,
            SectionProfile chordProf,
            SectionProfile beamProf,
            SectionProfile webProf,
            double bridgeSpan,
            double bridgeWidth,
            double trussHeight,
            double clearanceZ)
        {
            // 基点位于桁架底弦，插入点即结构底面；clearanceZ 仅保留参数兼容，不再抬高模型
            double zBot = 0.0;
            double zTop = zBot + Math.Max(trussHeight, 1200.0);
            int panels = Math.Max(3, (int)Math.Round(bridgeSpan / Math.Max(trussHeight, 1500.0)));
            double panelLen = bridgeSpan / panels;

            // 四根纵向贯通弦杆 (左下、右下、左上、右上)
            AddMemberExtended(parts, "下弦纵杆", chordProf, new Point3d(0, 0, zBot), new Point3d(bridgeSpan, 0, zBot), 20.0);
            AddMemberExtended(parts, "下弦纵杆", chordProf, new Point3d(0, bridgeWidth, zBot), new Point3d(bridgeSpan, bridgeWidth, zBot), 20.0);
            AddMemberExtended(parts, "上弦纵杆", chordProf, new Point3d(0, 0, zTop), new Point3d(bridgeSpan, 0, zTop), 20.0);
            AddMemberExtended(parts, "上弦纵杆", chordProf, new Point3d(0, bridgeWidth, zTop), new Point3d(bridgeSpan, bridgeWidth, zTop), 20.0);

            for (int i = 0; i <= panels; i++)
            {
                double x = i * panelLen;
                // 两侧竖杆
                AddMemberExtended(parts, "桁架竖杆", webProf, new Point3d(x, 0, zBot), new Point3d(x, 0, zTop), 18.0);
                AddMemberExtended(parts, "桁架竖杆", webProf, new Point3d(x, bridgeWidth, zBot), new Point3d(x, bridgeWidth, zTop), 18.0);
                // 上下横向承重横梁
                AddMemberExtended(parts, "底部承重横梁", beamProf, new Point3d(x, 0, zBot), new Point3d(x, bridgeWidth, zBot), 18.0);
                AddMemberExtended(parts, "顶部联系横梁", beamProf, new Point3d(x, 0, zTop), new Point3d(x, bridgeWidth, zTop), 18.0);

                if (i < panels)
                {
                    double nextX = (i + 1) * panelLen;
                    AddMemberExtended(parts, "侧面斜腹杆", webProf, new Point3d(x, 0, zBot), new Point3d(nextX, 0, zTop), 20.0);
                    AddMemberExtended(parts, "侧面斜腹杆", webProf, new Point3d(x, bridgeWidth, zBot), new Point3d(nextX, bridgeWidth, zTop), 20.0);
                }
            }
        }

        // ==================== 11. 储罐连续螺旋盘梯 (100% 连续贯通) ====================
        private static void BuildSpiralStairParts(
            List<SolidMemberPart> parts,
            SectionProfile stringerProf,
            SectionProfile handrailProf,
            SectionProfile stepProf,
            double outerDia,
            double stepWidth,
            double totalHeight,
            double sweepDeg)
        {
            double outerR = Math.Max(outerDia * 0.5, 900.0);
            double innerR = Math.Max(outerR - stepWidth, 350.0);
            double totalRad = Math.Max(sweepDeg, 90.0) * Math.PI / 180.0;
            int stepCount = Math.Max(8, (int)Math.Round(totalHeight / 220.0));
            double handrailH = 1050.0;

            AddContinuousMember(parts, "内螺旋斜梁", stringerProf, SampleHelix(innerR, totalHeight, totalRad, 0.0), 0.0, smooth: true, closed: false);
            AddContinuousMember(parts, "外螺旋斜梁", stringerProf, SampleHelix(outerR, totalHeight, totalRad, 0.0), 0.0, smooth: true, closed: false);
            AddContinuousMember(parts, "外侧螺旋扶手", handrailProf, SampleHelix(outerR, totalHeight, totalRad, handrailH), 0.0, smooth: true, closed: false);
            AddContinuousMember(parts, "螺旋中栏杆", handrailProf, SampleHelix(outerR, totalHeight, totalRad, handrailH * 0.5), 0.0, smooth: true, closed: false);

            for (int i = 0; i < stepCount; i++)
            {
                double t1 = (double)(i + 1) / stepCount;
                double aStep = t1 * totalRad;
                double zStep = t1 * totalHeight;

                Point3d stepIn = new Point3d((innerR - 25.0) * Math.Cos(aStep), (innerR - 25.0) * Math.Sin(aStep), zStep);
                Point3d stepOut = new Point3d((outerR + 25.0) * Math.Cos(aStep), (outerR + 25.0) * Math.Sin(aStep), zStep);
                AddMember(parts, "防滑踏步板", stepProf, stepIn, stepOut);

                Point3d postBot = new Point3d(outerR * Math.Cos(aStep), outerR * Math.Sin(aStep), zStep - 35.0);
                Point3d postTop = new Point3d(outerR * Math.Cos(aStep), outerR * Math.Sin(aStep), zStep + handrailH + 25.0);
                AddMember(parts, "栏杆立柱", handrailProf, postBot, postTop);
            }

            Point3d firstBot = new Point3d(outerR, 0, 0);
            Point3d firstTop = new Point3d(outerR, 0, handrailH + 25.0);
            AddMember(parts, "起始栏杆立柱", handrailProf, firstBot, firstTop);
        }

        // ==================== 12. 直跑钢斜梯 + 双侧连续栏杆扶手 (支持两点线性绘制，新增) ====================
        private static void BuildStraightStairParts(
            List<SolidMemberPart> parts,
            SectionProfile stringerProf,
            SectionProfile handrailProf,
            SectionProfile stepProf,
            double horizontalRun,
            double stairWidth,
            double riseHeight,
            double handrailHeight)
        {
            double run = Math.Max(horizontalRun, 800.0);
            double width = Math.Max(stairWidth, 650.0);
            double rise = Math.Max(riseHeight, 600.0);
            double rH = Math.Max(handrailHeight, 1000.0);

            SectionProfile pipeProf = handrailProf.ShapeType == SectionShapeType.PipeTube
                ? handrailProf
                : SectionCatalogService.GetByName("P42x3.5");

            // 左右两根连续槽钢斜梁 (延伸两端保证与平台及地面完整结合)
            AddMemberExtended(parts, "左梯梁(斜梁)", stringerProf,
                new Point3d(0, 0, 0), new Point3d(run, 0, rise), 35.0);
            AddMemberExtended(parts, "右梯梁(斜梁)", stringerProf,
                new Point3d(0, width, 0), new Point3d(run, width, rise), 35.0);

            // 踏步板等分布置
            int stepCount = Math.Max(4, (int)Math.Round(rise / 200.0));
            for (int i = 1; i <= stepCount; i++)
            {
                double t = (double)i / stepCount;
                double x = run * t;
                double z = rise * t;
                AddMemberExtended(parts, "斜梯踏步板", stepProf,
                    new Point3d(x, 0, z), new Point3d(x, width, z), 20.0);
            }

            // 双侧栏杆立柱 (每隔 2~3 级踏步设一根立柱，并在首尾设端柱)
            int postStride = Math.Max(1, stepCount / 4);
            for (int i = 0; i <= stepCount; i++)
            {
                if (i == 0 || i == stepCount || i % postStride == 0)
                {
                    double t = (double)i / stepCount;
                    double x = run * t;
                    double z = rise * t;
                    AddMemberExtended(parts, "斜梯栏杆立柱", pipeProf,
                        new Point3d(x, 0, z - 20.0), new Point3d(x, 0, z + rH + 15.0), 10.0);
                    AddMemberExtended(parts, "斜梯栏杆立柱", pipeProf,
                        new Point3d(x, width, z - 20.0), new Point3d(x, width, z + rH + 15.0), 10.0);
                }
            }

            // 双侧连续顶部斜扶手与中部斜横杆
            AddMemberExtended(parts, "左侧顶部扶手", pipeProf,
                new Point3d(0, 0, rH), new Point3d(run, 0, rise + rH), 25.0);
            AddMemberExtended(parts, "右侧顶部扶手", pipeProf,
                new Point3d(0, width, rH), new Point3d(run, width, rise + rH), 25.0);
            AddMemberExtended(parts, "左侧中部横杆", pipeProf,
                new Point3d(0, 0, rH * 0.52), new Point3d(run, 0, rise + rH * 0.52), 25.0);
            AddMemberExtended(parts, "右侧中部横杆", pipeProf,
                new Point3d(0, width, rH * 0.52), new Point3d(run, width, rise + rH * 0.52), 25.0);

            // 柱底连接板
            AddBasePlate(parts, "梯脚底板", new Point3d(0, 0, 0), 180, 120, 16);
            AddBasePlate(parts, "梯脚底板", new Point3d(0, width, 0), 180, 120, 16);
        }

        // ==================== 13. 带安全护笼钢直梯 (GB 4053.1，新增) ====================
        private static void BuildSafetyCagedLadderParts(
            List<SolidMemberPart> parts,
            SectionProfile stileProf,
            SectionProfile hoopProf,
            SectionProfile rungProf,
            double ladderWidth,
            double cageDepth,
            double climbHeight,
            double cageStartZ)
        {
            double w = Math.Max(ladderWidth, 500.0);
            double h = Math.Max(climbHeight, 2600.0);
            double topExt = 1100.0;
            SectionProfile pipeStile = SectionCatalogService.GetByName("P48x3.5");
            SectionProfile pipeRung = SectionCatalogService.GetByName("P32x3.2");

            // 两侧直梯立管 (直通至平台上方 1100mm)
            AddMember(parts, "直梯左侧立管", pipeStile, new Point3d(0, 0, 0), new Point3d(0, 0, h + topExt));
            AddMember(parts, "直梯右侧立管", pipeStile, new Point3d(w, 0, 0), new Point3d(w, 0, h + topExt));

            // 水平防滑踏棍 (@300mm)
            int rungCount = Math.Max(4, (int)(h / 300.0));
            for (int i = 1; i <= rungCount; i++)
            {
                double z = i * 300.0;
                AddMemberExtended(parts, "直梯踏棍", pipeRung, new Point3d(0, 0, z), new Point3d(w, 0, z), 15.0);
            }

            // 安全护笼 (当爬高 >= 2200mm 时，从 cageStartZ 向上设置护笼圆弧箍与竖向连接扁钢)
            double startZ = Math.Min(Math.Max(cageStartZ, 2000.0), h - 400.0);
            double cageR = Math.Max(cageDepth * 0.5, 350.0);
            double centerX = w * 0.5;
            double centerY = -cageR * 0.85;

            int hoopCount = Math.Max(2, (int)((h + topExt - startZ) / 1100.0) + 1);
            int arcSegs = 8;
            var cageAngles = new double[arcSegs + 1];
            for (int k = 0; k <= arcSegs; k++)
            {
                // 从右立管 (angle = 0.15*PI) 绕背侧圆弧到左立管 (angle = -1.15*PI)
                cageAngles[k] = 0.15 * Math.PI - (1.30 * Math.PI * k / arcSegs);
            }

            for (int j = 0; j < hoopCount; j++)
            {
                double hz = startZ + (h + topExt - startZ) * j / Math.Max(1, hoopCount - 1);
                var hoop = new List<Point3d> { new Point3d(w, 0, hz) };
                int samples = 20;
                double a0 = cageAngles[0];
                double a1 = cageAngles[arcSegs];
                for (int k = 0; k <= samples; k++)
                {
                    double ang = a0 + (a1 - a0) * k / samples;
                    hoop.Add(new Point3d(centerX + cageR * Math.Cos(ang), centerY + cageR * Math.Sin(ang), hz));
                }
                hoop.Add(new Point3d(0, 0, hz));
                AddContinuousMember(parts, "护笼横箍", pipeRung, hoop, bendRadius: 0.0, smooth: true, closed: false);
            }

            // 护笼纵向立条 (5 根纵向立杆连接所有护笼横箍)
            for (int k = 1; k < arcSegs; k += 2)
            {
                double ang = cageAngles[k];
                double vx = centerX + cageR * Math.Cos(ang);
                double vy = centerY + cageR * Math.Sin(ang);
                AddMemberExtended(parts, "护笼纵向立杆", pipeRung,
                    new Point3d(vx, vy, startZ),
                    new Point3d(vx, vy, h + topExt), 15.0);
            }
        }

        // ==================== 14. 平台连续安全栏杆扶手 (GB 4053.3，支持两点线性动态绘制，新增) ====================
        private static void BuildHandrailGuardrailParts(
            List<SolidMemberPart> parts,
            SectionProfile postProf,
            SectionProfile railProf,
            SectionProfile subProf,
            double totalLength,
            double handrailHeight,
            double maxPostSpacing)
        {
            double len = Math.Max(totalLength, 400.0);
            double h = Math.Max(handrailHeight, 1050.0);
            double spacing = Math.Max(maxPostSpacing, 900.0);
            if (spacing > 1500.0) spacing = 1100.0;

            int postCount = Math.Max(2, (int)Math.Ceiling(len / spacing) + 1);
            double actualPitch = len / (postCount - 1);

            for (int i = 0; i < postCount; i++)
            {
                double x = i * actualPitch;
                AddMemberExtended(parts, "栏杆立柱", postProf,
                    new Point3d(x, 0, 0), new Point3d(x, 0, h), 12.0);
                AddBasePlate(parts, "立柱法兰底板", new Point3d(x, 0, 0), 120, 120, 12);
            }

            // 顶部连续扶手管 + 中横杆 + 底部踢脚扁钢
            AddMemberExtended(parts, "顶部连续扶手", railProf,
                new Point3d(0, 0, h), new Point3d(len, 0, h), 20.0);
            AddMemberExtended(parts, "中部安全横杆", railProf,
                new Point3d(0, 0, h * 0.52), new Point3d(len, 0, h * 0.52), 20.0);
            AddBasePlate(parts, "底部踢脚板(100x6)",
                new Point3d(len * 0.5, 0, 15.0), len, 6.0, 100.0);
        }

        // ==================== 15. 线性单根型钢构件 (钢梁/钢柱/斜撑，支持两点三维动态绘制，新增) ====================
        private static void BuildLinearSteelMemberParts(
            List<SolidMemberPart> parts,
            SectionProfile mainProf,
            double horizontalProjLen,
            double deltaZ)
        {
            Point3d pStart = Point3d.Origin;
            Point3d pEnd = new Point3d(Math.Max(horizontalProjLen, 50.0), 0, deltaZ);
            AddMember(parts, "主型钢杆件", mainProf, pStart, pEnd);
        }

        // ==================== 16. 容器鞍式钢支座 ====================
        private static void BuildVesselSupportParts(
            List<SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile webProf,
            SectionProfile ribProf,
            double vesselDia,
            double saddleWidth,
            double centerHeight,
            double wrapAngleDeg)
        {
            double r = Math.Max(vesselDia * 0.5, 500.0);
            double w = Math.Max(saddleWidth, 300.0);
            double h = Math.Max(centerHeight, r * 0.65);
            double baseW = r * 1.55;

            AddBasePlate(parts, "鞍座底板", new Point3d(0, 0, 0), baseW, w, 24.0);
            double webTopZ = Math.Max(h - r * 0.5, 280.0);
            AddBasePlate(parts, "鞍座主腹板", new Point3d(0, 0, 24.0), baseW * 0.90, 20.0, webTopZ - 24.0);

            for (int i = -1; i <= 1; i++)
            {
                double x = i * (baseW * 0.32);
                AddBasePlate(parts, "竖向加劲肋", new Point3d(x, 0, 24.0), 16.0, w * 0.88, webTopZ - 24.0);
            }

            double halfAngle = Math.Min(Math.Max(wrapAngleDeg, 90.0), 150.0) * 0.5 * Math.PI / 180.0;
            int arcSegs = 16;
            var saddleArc = new List<Point3d>(arcSegs + 1);
            for (int i = 0; i <= arcSegs; i++)
            {
                double a = -Math.PI * 0.5 - halfAngle + (2.0 * halfAngle * i / arcSegs);
                saddleArc.Add(new Point3d(r * Math.Cos(a), 0, h + r * Math.Sin(a)));
            }
            AddContinuousMember(parts, "弧形鞍面板", webProf, saddleArc, bendRadius: 0.0, smooth: true, closed: false);
        }

        private static void AddMember(
            List<SolidMemberPart> parts,
            string roleName,
            SectionProfile prof,
            Point3d p0,
            Point3d p1)
        {
            Solid3d s = SectionSolidBuilder.CreateMemberSolid(prof, p0, p1);
            if (s != null)
            {
                parts.Add(new SolidMemberPart
                {
                    Solid = s,
                    RoleName = roleName,
                    SectionName = prof.Name,
                    Section = prof,
                    IsPlate = false,
                    StartPt = p0,
                    EndPt = p1
                });
            }
        }

        private static void AddMemberExtended(
            List<SolidMemberPart> parts,
            string roleName,
            SectionProfile prof,
            Point3d p0,
            Point3d p1,
            double extendEachEndMm)
        {
            Vector3d dir = p1 - p0;
            if (dir.Length < 1e-3) return;
            Vector3d unit = dir.GetNormal();
            Point3d extP0 = p0 - unit * extendEachEndMm;
            Point3d extP1 = p1 + unit * extendEachEndMm;

            Solid3d s = SectionSolidBuilder.CreateMemberSolid(prof, extP0, extP1);
            if (s != null)
            {
                parts.Add(new SolidMemberPart
                {
                    Solid = s,
                    RoleName = roleName,
                    SectionName = prof.Name,
                    Section = prof,
                    IsPlate = false,
                    StartPt = p0,
                    EndPt = p1
                });
            }
        }

        private static void AddBasePlate(
            List<SolidMemberPart> parts,
            string roleName,
            Point3d bottomCenter,
            double sizeX,
            double sizeY,
            double thicknessZ)
        {
            Solid3d box = new Solid3d();
            box.SetDatabaseDefaults();
            box.CreateBox(Math.Max(sizeX, 10), Math.Max(sizeY, 10), Math.Max(thicknessZ, 4));
            box.TransformBy(Matrix3d.Displacement(
                bottomCenter + new Vector3d(0, 0, thicknessZ * 0.5) - Point3d.Origin));

            parts.Add(new SolidMemberPart
            {
                Solid = box,
                RoleName = roleName,
                SectionName = $"PL{thicknessZ:0}x{sizeX:0}x{sizeY:0}",
                Section = null,
                IsPlate = true,
                StartPt = bottomCenter,
                EndPt = bottomCenter + new Vector3d(0, 0, thicknessZ)
            });
        }

        /// <summary>
        /// 计算构件下料零件明细表（纯数学计算，耗时 < 0.1ms，且 PartNo #1, #2... 与轴测图引线编号 100% 对应）
        /// </summary>
        public static List<MemberPartBillItem> CalculatePartBillItems(SteelStructureParams p)
        {
            var list = new List<MemberPartBillItem>();
            SectionProfile col = SectionCatalogService.GetByName(p.ColumnSection);
            SectionProfile beam = SectionCatalogService.GetByName(p.BeamSection);
            SectionProfile brace = SectionCatalogService.GetByName(p.BraceSection);
            SectionProfile pipe = SectionCatalogService.GetByName("P42x3.5");

            double span = Math.Max(p.Span, 250.0);
            double len = Math.Max(p.Length, 200.0);
            double h = Math.Max(p.Height, 250.0);
            double subH = Math.Max(p.SubHeight, 80.0);

            int seq = 1;
            Action<string, SectionProfile, int, double> addRow = (role, prof, count, singleLenMm) =>
            {
                double totalLenM = (count * singleLenMm) * 0.001;
                list.Add(new MemberPartBillItem
                {
                    ParentTag = p.Tag,
                    PartNo = seq++,
                    PartRole = role,
                    SectionName = prof.Name,
                    ShapeTypeName = prof.ShapeTypeDisplayName,
                    Material = p.Material,
                    Count = count,
                    SingleLengthMm = Math.Round(singleLenMm, 0),
                    UnitWeightKgPerM = prof.UnitWeightKgPerM,
                    TotalWeightKg = Math.Round(totalLenM * prof.UnitWeightKgPerM, 2),
                    TotalSurfaceAreaM2 = Math.Round(totalLenM * prof.PerimeterMPerM, 2)
                });
            };

            Action<string, double, double, double, int> addPlate = (role, thicknessMm, widthMm, lengthMm, count) =>
            {
                double t = Math.Max(2.0, thicknessMm);
                double w2 = Math.Max(10.0, widthMm);
                double l2 = Math.Max(10.0, lengthMm);
                double oneKg = t * w2 * l2 * 7.85e-6;
                list.Add(new MemberPartBillItem
                {
                    ParentTag = p.Tag,
                    PartNo = seq++,
                    PartRole = role,
                    SectionName = $"PL{t:0}x{w2:0}x{l2:0}",
                    ShapeTypeName = "钢板",
                    Material = p.Material,
                    Count = count,
                    SingleLengthMm = Math.Round(l2, 0),
                    UnitWeightKgPerM = 0.0,
                    TotalWeightKg = Math.Round(oneKg * count, 2),
                    TotalSurfaceAreaM2 = Math.Round(2.0 * w2 * l2 * count / 1e6, 2)
                });
            };

            switch (p.StructureType)
            {
                case ParametricStructureType.PortalFrame:
                    addRow("框架立柱", col, 2, h);
                    addRow("顶部主梁", beam, 1, span);
                    addPlate("柱脚底板", 20, col.H + 100, col.B + 100, 2);
                    break;
                case ParametricStructureType.MultiSpanPortalFrame:
                {
                    int bays = Math.Max(2, p.BayCount);
                    addRow("边立柱", col, 2, h);
                    addRow("中立柱", col, bays - 1, h);
                    addRow("连续主梁", beam, 1, span);
                    addRow("梁柱隅撑", brace, bays * 2, Math.Min(span / bays * 0.25, 650.0));
                    addPlate("柱脚底板", 20, col.H + 100, col.B + 100, bays + 1);
                    break;
                }
                case ParametricStructureType.BracedFrame:
                {
                    addRow("框架立柱", col, 2, h);
                    addRow("顶部与中部横梁", beam, 2, span);
                    double diag = Math.Sqrt(Math.Pow(span * 0.5, 2) + Math.Pow(h - subH, 2));
                    addRow("人字斜撑", brace, 2, diag);
                    addPlate("柱脚底板", 20, col.H + 100, col.B + 100, 2);
                    break;
                }
                case ParametricStructureType.DoubleTierFrame:
                    addRow("管廊立柱", col, 2, h);
                    addRow("上下层横梁", beam, 2, span);
                    addRow("斜隅撑", brace, 2, 620.0);
                    addPlate("柱脚底板", 20, col.H + 100, col.B + 100, 2);
                    break;
                case ParametricStructureType.MultiBaySpaceFrame:
                    addRow("框架立柱", col, 4, h);
                    addRow("横向主梁", beam, 4, span);
                    addRow("纵向主梁", beam, 4, len);
                    addRow("平台次梁", brace, 2, span);
                    addRow("柱间斜撑", brace, 2, Math.Sqrt(len * len + subH * subH));
                    addPlate("柱脚底板", 20, col.H + 100, col.B + 100, 4);
                    break;
                case ParametricStructureType.CantileverBracket:
                {
                    addRow("附墙立柱", col, 1, h);
                    addRow("悬挑主梁", beam, 1, span);
                    double diag = Math.Sqrt(span * span * 0.78 + subH * subH);
                    addRow("斜撑杆", brace, 1, diag);
                    addPlate("柱脚锚固板", 20, col.H + 100, col.B + 100, 1);
                    break;
                }
                case ParametricStructureType.SpacePlatform:
                {
                    double railH = Math.Max(subH, 1050.0);
                    double perimeter = 2.0 * (span + len);
                    int railPosts = 8;
                    addRow("平台立柱", col, 4, h);
                    addRow("横向主梁", beam, 2, span);
                    addRow("纵向边梁", beam, 2, len);
                    addRow("平台次梁", brace, 2, span);
                    addRow("护栏立柱", pipe, railPosts, railH);
                    addRow("顶部扶手与中横杆", pipe, 8, perimeter / 4.0);
                    addPlate("柱脚底板", 20, col.H + 100, col.B + 100, 4);
                    addPlate("平台花纹钢板", 8, span, len, 1);
                    break;
                }
                case ParametricStructureType.CantileverWalkway:
                {
                    int bCount = Math.Max(2, (int)Math.Ceiling(span / 1800.0) + 1);
                    double railH = Math.Max(subH, 1050.0);
                    addRow("悬挑牛腿主梁", col, bCount, len);
                    addRow("牛腿三角斜撑", brace, bCount, len * 1.15);
                    addRow("走道内外纵梁", beam, 2, span);
                    addRow("外侧连续扶手与中杆", pipe, 2, span);
                    addRow("走道栏杆立柱", pipe, bCount, railH);
                    addPlate("附墙锚固板", 16, 180, Math.Max(p.Height * 0.4, 450.0) + 140, bCount);
                    addPlate("走道钢格栅板", 8, span, len, 1);
                    break;
                }
                case ParametricStructureType.RoofTruss:
                {
                    addRow("桁架立柱", col, 2, h);
                    addRow("桁架下弦杆", beam, 1, span);
                    double slopeLen = Math.Sqrt(Math.Pow(span * 0.5, 2) + Math.Pow(subH, 2));
                    addRow("双坡上弦杆", beam, 2, slopeLen);
                    addRow("腹杆组件", brace, 5, subH * 0.88);
                    addPlate("柱脚底板", 20, col.H + 100, col.B + 100, 2);
                    break;
                }
                case ParametricStructureType.PipeBridgeTruss:
                {
                    int panels = Math.Max(3, (int)Math.Round(span / Math.Max(h, 1500.0)));
                    addRow("上下弦纵杆", col, 4, span);
                    addRow("承重与联系横梁", beam, (panels + 1) * 2, len);
                    addRow("桁架竖杆", brace, (panels + 1) * 2, h);
                    double diag = Math.Sqrt(Math.Pow(span / panels, 2) + h * h);
                    addRow("侧面斜腹杆", brace, panels * 2, diag);
                    break;
                }
                case ParametricStructureType.SpiralStair:
                {
                    int steps = Math.Max(8, (int)Math.Round(h / 220.0));
                    double rad = Math.Max(subH, 90.0) * Math.PI / 180.0;
                    double arcOuter = Math.Sqrt(Math.Pow((span * 0.5) * rad, 2) + h * h);
                    addRow("内外螺旋斜梁", col, 2, arcOuter);
                    addRow("防滑踏步板", brace, steps, len);
                    addRow("螺旋扶手与栏杆柱", beam, steps + 2, 1150.0);
                    break;
                }
                case ParametricStructureType.StraightStair:
                {
                    double slopeL = Math.Sqrt(span * span + h * h);
                    int steps = Math.Max(4, (int)Math.Round(h / 200.0));
                    int posts = (Math.Max(2, stepCountToPosts(steps))) * 2;
                    addRow("双侧梯梁(斜梁)", col, 2, slopeL);
                    addRow("斜梯踏步板", brace, steps, len);
                    addRow("双侧连续斜扶手与横杆", pipe, 4, slopeL);
                    addRow("斜梯栏杆立柱", pipe, posts, Math.Max(subH, 1000.0));
                    addPlate("梯脚底板", 16, 180, 120, 2);
                    break;
                }
                case ParametricStructureType.SafetyCagedLadder:
                {
                    SectionProfile pStile = SectionCatalogService.GetByName("P48x3.5");
                    SectionProfile pRung = SectionCatalogService.GetByName("P32x3.2");
                    int rungs = Math.Max(4, (int)(h / 300.0));
                    int hoops = Math.Max(2, (int)((h + 1100.0 - subH) / 1100.0) + 1);
                    addRow("直梯双侧立管", pStile, 2, h + 1100.0);
                    addRow("防滑踏棍", pRung, rungs, span);
                    addRow("安全护笼横箍", pRung, hoops, Math.PI * Math.Max(len, 700.0) * 0.75);
                    addRow("护笼纵向立杆", pRung, 5, Math.Max(600.0, h + 1100.0 - subH));
                    break;
                }
                case ParametricStructureType.HandrailGuardrail:
                {
                    int postCount = Math.Max(2, (int)Math.Ceiling(span / Math.Max(subH, 1000.0)) + 1);
                    addRow("栏杆立柱", col, postCount, h);
                    addRow("顶部连续扶手管", beam, 1, span);
                    addRow("中部安全横杆", beam, 1, span);
                    addPlate("立柱法兰底板", 12, 120, 120, postCount);
                    break;
                }
                case ParametricStructureType.LinearSteelMember:
                {
                    double l3d = Math.Sqrt(span * span + p.EndPointOffsetZ * p.EndPointOffsetZ);
                    addRow("主型钢杆件", col, 1, l3d);
                    break;
                }
                case ParametricStructureType.ArcSteelMember:
                {
                    double sweep = Math.Min(330.0, Math.Max(15.0, subH)) * Math.PI / 180.0;
                    addRow("弧形型钢", col, 1, Math.Max(span, 80.0) * sweep);
                    break;
                }
                case ParametricStructureType.SpiralSteelMember:
                {
                    double radius = Math.Max(len, 30.0);
                    double turns = SpiralTurns(subH);
                    double axisLen = Math.Sqrt(span * span + p.EndPointOffsetZ * p.EndPointOffsetZ);
                    double helix = Math.Sqrt(Math.Pow(Math.PI * 2.0 * radius * turns, 2) + axisLen * axisLen);
                    addRow("螺旋型钢", col, 1, helix);
                    break;
                }
                case ParametricStructureType.VesselSupport:
                    addRow("鞍座底板与腹板", col, 2, span * 0.75);
                    addRow("弧形鞍面板", beam, 1, span * 0.85);
                    addRow("竖向加劲肋", brace, 3, h * 0.65);
                    addPlate("鞍座底板", 24, span * 0.78, len, 1);
                    addPlate("鞍座腹板", 20, span * 0.70, h * 0.55, 1);
                    break;
            }

            return list;
        }

        private static int stepCountToPosts(int steps) => Math.Max(3, steps / 3 + 2);

        public static ObjectId AppendToCurrentDatabase(Database db, SteelStructureParams p)
        {
            EnsureLayerExists(db, LayerModel3D, 140);
            Solid3d combined = BuildCombinedSolid(p);
            if (combined == null) return ObjectId.Null;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                combined.Layer = LayerModel3D;
                ObjectId id = ms.AppendEntity(combined);
                tr.AddNewlyCreatedDBObject(combined, true);

                SteelDataStorage.WriteParams(tr, combined, p);
                tr.Commit();
                return id;
            }
        }

        public static void UpdateStructureInDatabase(Database db, ObjectId entityId, SteelStructureParams newParams)
        {
            if (entityId.IsNull || entityId.IsErased) return;

            using (Solid3d newSolid = BuildCombinedSolid(newParams))
            {
                if (newSolid == null) return;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(entityId, OpenMode.ForWrite) is Solid3d existing)
                    {
                        // CopyFrom 会把新实体的默认图层（通常是 0）盖掉用户已经指定的图层
                        CopySolidKeepingLook(existing, newSolid);
                        SteelDataStorage.WriteParams(tr, existing, newParams);
                    }
                    tr.Commit();
                }
            }
        }

        /// <summary>
        /// 只替换三维几何，保留目标实体上用户预设的图层、颜色、线型、线宽和透明度。
        /// </summary>
        public static void CopySolidKeepingLook(Solid3d target, Solid3d source)
        {
            if (target == null || source == null) return;

            string layer = null;
            Color color = null;
            ObjectId linetypeId = ObjectId.Null;
            LineWeight lineWeight = LineWeight.ByLayer;
            Transparency transparency = default(Transparency);
            bool captured = false;
            try
            {
                layer = target.Layer;
                color = target.Color;
                linetypeId = target.LinetypeId;
                lineWeight = target.LineWeight;
                transparency = target.Transparency;
                captured = true;
            }
            catch { }

            target.CopyFrom(source);

            if (!captured) return;
            try { if (!string.IsNullOrEmpty(layer)) target.Layer = layer; } catch { }
            try { if (color != null) target.Color = color; } catch { }
            try { if (!linetypeId.IsNull) target.LinetypeId = linetypeId; } catch { }
            try { target.LineWeight = lineWeight; } catch { }
            try { target.Transparency = transparency; } catch { }
        }

        private static void BuildArcSteelMemberParts(
            List<SolidMemberPart> parts,
            SectionProfile sec,
            double radius,
            double sweepDeg,
            double endRise)
        {
            double r = Math.Max(radius, 80.0);
            double sweep = Math.Min(330.0, Math.Max(15.0, sweepDeg)) * Math.PI / 180.0;
            int n = Math.Max(12, (int)Math.Ceiling(sweep * 180.0 / Math.PI / 6.0));
            var path = new List<Point3d>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                double a = sweep * t;
                path.Add(new Point3d(r * Math.Cos(a), r * Math.Sin(a), endRise * t));
            }
            AddContinuousMember(parts, "弧形型钢", sec, path, 0.0, smooth: true, closed: false);
        }

        private static void BuildSpiralSteelMemberParts(
            List<SolidMemberPart> parts,
            SectionProfile sec,
            double horizontalLen,
            double helixRadius,
            double turnsOrDegrees,
            double endRise)
        {
            double radius = Math.Max(helixRadius, 30.0);
            double turns = SpiralTurns(turnsOrDegrees);
            double axisLen = Math.Sqrt(horizontalLen * horizontalLen + endRise * endRise);
            if (axisLen < 50.0) axisLen = Math.Max(horizontalLen, 200.0);
            Vector3d axis = new Vector3d(horizontalLen, 0, endRise);
            if (axis.Length < 10.0) axis = Vector3d.XAxis;
            axis = axis.GetNormal();
            Vector3d side = Math.Abs(axis.Z) > 0.9
                ? Vector3d.YAxis
                : Vector3d.ZAxis.CrossProduct(axis).GetNormal();
            Vector3d binormal = axis.CrossProduct(side).GetNormal();
            int n = Math.Max(24, (int)Math.Ceiling(turns * 24.0));
            var path = new List<Point3d>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                double a = t * turns * Math.PI * 2.0;
                path.Add(Point3d.Origin + axis * (axisLen * t)
                    + side * (radius * Math.Cos(a))
                    + binormal * (radius * Math.Sin(a)));
            }
            AddContinuousMember(parts, "螺旋型钢", sec, path, 0.0, smooth: true, closed: false);
        }

        private static double SpiralTurns(double subH)
        {
            if (subH > 30.0) return Math.Max(0.25, subH / 360.0);
            return Math.Max(0.25, subH);
        }

        /// <summary>
        /// 直杆端部伸进另一根杆件腹中时，用对方实体把伸入段切掉。
        /// 只切“端头插入”，不切角部对接，也不切几乎共线的搭接。曲线扫略件同样只在端头被切。
        /// </summary>
        private static void TrimIncomingEnds(List<SolidMemberPart> parts)
        {
            var hosts = new List<SolidMemberPart>();
            foreach (var part in parts)
            {
                if (part?.Solid == null || part.IsPlate || part.Section == null) continue;
                if ((part.EndPt - part.StartPt).Length < 20.0 &&
                    (part.PathPoints == null || part.PathPoints.Count < 2)) continue;
                hosts.Add(part);
            }
            if (hosts.Count < 2) return;

            var originals = new Dictionary<SolidMemberPart, Solid3d>();
            foreach (var host in hosts)
            {
                try { originals[host] = (Solid3d)host.Solid.Clone(); }
                catch { }
            }

            try
            {
                foreach (var guest in hosts)
                {
                    foreach (var host in hosts)
                    {
                        if (ReferenceEquals(guest, host) || !originals.ContainsKey(host)) continue;
                        if (!EndFramesInto(guest, host)) continue;
                        Solid3d tool = null;
                        try
                        {
                            tool = (Solid3d)originals[host].Clone();
                            guest.Solid.BooleanOperation(BooleanOperationType.BoolSubtract, tool);
                            tool = null;
                        }
                        catch
                        {
                            tool?.Dispose();
                        }
                    }
                }
            }
            finally
            {
                foreach (var solid in originals.Values) solid?.Dispose();
            }
        }

        private static bool EndFramesInto(SolidMemberPart guest, SolidMemberPart host)
        {
            if (guest.PathPoints != null && guest.PathPoints.Count >= 2)
            {
                return PointFramesInto(guest.PathPoints[0], guest, host) ||
                       PointFramesInto(guest.PathPoints[guest.PathPoints.Count - 1], guest, host);
            }
            return PointFramesInto(guest.StartPt, guest, host) ||
                   PointFramesInto(guest.EndPt, guest, host);
        }

        private static bool PointFramesInto(Point3d end, SolidMemberPart guest, SolidMemberPart host)
        {
            Vector3d axis = host.EndPt - host.StartPt;
            double len = axis.Length;
            if (len < 30.0) return false;
            Vector3d u = axis / len;
            double reach = 0.55 * Math.Max(host.Section.H, host.Section.B);
            double t = (end - host.StartPt).DotProduct(u);
            if (t < reach || t > len - reach) return false;
            Vector3d radial = (end - host.StartPt) - u * t;
            if (radial.Length > reach) return false;

            Vector3d guestAxis = guest.EndPt - guest.StartPt;
            if (guestAxis.Length > 20.0 && Math.Abs(guestAxis.GetNormal().DotProduct(u)) > 0.985)
                return false;
            return true;
        }

        /// <summary>
        /// 一根连续杆件沿整条路径扫略。折点可倒圆，弯曲路径用样条。扫略失败时退回分段挤出并互相搭接。
        /// </summary>
        private static void AddContinuousMember(
            List<SolidMemberPart> parts,
            string roleName,
            SectionProfile prof,
            IList<Point3d> path,
            double bendRadius,
            bool smooth,
            bool closed)
        {
            if (prof == null || path == null || path.Count < 2) return;

            List<Point3d> used = bendRadius > 1.0 ? FilletPath(path, bendRadius, closed) : CleanPath(path, closed);
            if (used.Count < 2) return;

            Solid3d swept = SectionSolidBuilder.SweepAlongPath(prof, used, smooth, closed);
            if (swept != null)
            {
                parts.Add(new SolidMemberPart
                {
                    Solid = swept,
                    RoleName = roleName,
                    SectionName = prof.Name,
                    Section = prof,
                    IsPlate = false,
                    StartPt = used[0],
                    EndPt = used[used.Count - 1],
                    PathPoints = used
                });
                return;
            }

            double overlap = Math.Max(12.0, Math.Max(prof.H, prof.B) * 0.35);
            int last = closed ? used.Count : used.Count - 1;
            for (int i = 0; i < last; i++)
            {
                Point3d a = used[i];
                Point3d b = used[(i + 1) % used.Count];
                if (a.DistanceTo(b) < 8.0) continue;
                AddMemberExtended(parts, roleName, prof, a, b, overlap);
            }
        }

        private static List<Point3d> SampleHelix(double radius, double height, double totalRad, double zOffset)
        {
            int segs = Math.Max(24, (int)Math.Ceiling(Math.Abs(totalRad) / (Math.PI / 18.0)));
            var pts = new List<Point3d>(segs + 1);
            for (int i = 0; i <= segs; i++)
            {
                double t = (double)i / segs;
                double a = t * totalRad;
                pts.Add(new Point3d(radius * Math.Cos(a), radius * Math.Sin(a), t * height + zOffset));
            }
            return pts;
        }

        private static List<Point3d> CleanPath(IList<Point3d> pts, bool closed)
        {
            var clean = new List<Point3d>();
            if (pts == null) return clean;
            foreach (Point3d p in pts)
            {
                if (clean.Count == 0 || clean[clean.Count - 1].DistanceTo(p) > 2.0)
                    clean.Add(p);
            }
            if (closed && clean.Count > 2 && clean[0].DistanceTo(clean[clean.Count - 1]) < 2.0)
                clean.RemoveAt(clean.Count - 1);
            return clean;
        }

        private static List<Point3d> FilletPath(IList<Point3d> pts, double radius, bool closed)
        {
            List<Point3d> clean = CleanPath(pts, closed);
            int n = clean.Count;
            if (n < 3 || radius < 5.0) return clean;

            var result = new List<Point3d>();
            if (!closed) AddPathPoint(result, clean[0]);

            int corners = closed ? n : n - 2;
            for (int c = 0; c < corners; c++)
            {
                int i = closed ? c : c + 1;
                Point3d prev = clean[(i - 1 + n) % n];
                Point3d curr = clean[i];
                Point3d next = clean[(i + 1) % n];
                AppendFillet(result, prev, curr, next, radius);
            }

            if (!closed) AddPathPoint(result, clean[n - 1]);
            return result.Count >= 2 ? result : clean;
        }

        private static void AppendFillet(List<Point3d> result, Point3d prev, Point3d curr, Point3d next, double radius)
        {
            Vector3d vIn = curr - prev;
            Vector3d vOut = next - curr;
            if (vIn.Length < 1.0 || vOut.Length < 1.0)
            {
                AddPathPoint(result, curr);
                return;
            }

            Vector3d uIn = vIn.GetNormal();
            Vector3d uOut = vOut.GetNormal();
            double cosTheta = Clamp(-uIn.DotProduct(uOut), -1.0, 1.0);
            double theta = Math.Acos(cosTheta);
            if (theta > Math.PI - 0.12 || theta < 0.12)
            {
                AddPathPoint(result, curr);
                return;
            }

            double r = radius;
            double tanLen = r / Math.Tan(theta * 0.5);
            double maxTan = 0.45 * Math.Min(vIn.Length, vOut.Length);
            if (tanLen > maxTan)
            {
                tanLen = maxTan;
                r = tanLen * Math.Tan(theta * 0.5);
            }
            if (r < 5.0)
            {
                AddPathPoint(result, curr);
                return;
            }

            Point3d tA = curr - uIn * tanLen;
            Point3d tB = curr + uOut * tanLen;
            Vector3d bis = -uIn + uOut;
            if (bis.Length < 1e-6)
            {
                AddPathPoint(result, curr);
                return;
            }

            Point3d center = curr + bis.GetNormal() * (r / Math.Sin(theta * 0.5));
            Vector3d a = tA - center;
            Vector3d b = tB - center;
            const int segs = 5;
            for (int s = 0; s <= segs; s++)
            {
                double u = (double)s / segs;
                AddPathPoint(result, center + Slerp(a, b, u));
            }
        }

        private static Vector3d Slerp(Vector3d a, Vector3d b, double t)
        {
            double len = a.Length;
            if (len < 1e-6) return a;
            Vector3d ua = a / len;
            Vector3d ub = b.GetNormal();
            double cos = Clamp(ua.DotProduct(ub), -1.0, 1.0);
            double ang = Math.Acos(cos);
            if (ang < 1e-4) return a;
            double s = Math.Sin(ang);
            Vector3d dir = ua * (Math.Sin((1.0 - t) * ang) / s) + ub * (Math.Sin(t * ang) / s);
            return dir * len;
        }

        private static double Clamp(double v, double lo, double hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private static void AddPathPoint(List<Point3d> pts, Point3d p)
        {
            if (pts.Count == 0 || pts[pts.Count - 1].DistanceTo(p) > 1.0)
                pts.Add(p);
        }

        private static void EnsureLayerExists(Database db, string layerName, short colorIndex)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(layerName))
                {
                    lt.UpgradeOpen();
                    LayerTableRecord ltr = new LayerTableRecord
                    {
                        Name = layerName,
                        Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex)
                    };
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                tr.Commit();
            }
        }
    }
}
