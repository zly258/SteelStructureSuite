using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Views.Controls
{
    /// <summary>
    /// 30 种参数化钢结构构件专属三维轴测示意图画布（单图自适应视图）。
    /// - 统一采用工程轴测投影，将三维杆件、节点、支座、护栏与尺寸标注绘制在同一画布内；
    /// - 自动按画布当前宽高居中缩放，随窗口及分隔条拖拽实时适配，无截断、无遮挡。
    /// </summary>
    public class StructureSchematicControl : Border
    {
        private readonly Canvas _canvas;

        private static readonly Brush BgBrush = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        private static readonly Brush BorderLineBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
        private static readonly Brush RefLineBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        private static readonly Brush AxisLineBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        private static readonly Brush SteelMemberBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189));
        private static readonly Brush SteelSubBrush = new SolidColorBrush(Color.FromRgb(16, 124, 65));
        private static readonly Brush PlateBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105));
        private static readonly Brush DimLineBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6));
        private static readonly Brush DimTextBrush = new SolidColorBrush(Color.FromRgb(180, 83, 9));
        private static readonly Brush TitleBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        private static readonly Brush TriadXBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        private static readonly Brush TriadYBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74));
        private static readonly Brush TriadZBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));

        public StructureSchematicControl()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            MinHeight = 240;
            Background = BgBrush;
            BorderBrush = BorderLineBrush;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(3);
            Margin = new Thickness(0);
            ClipToBounds = true;

            _canvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Child = _canvas;
        }

        private struct Pt3
        {
            public double X, Y, Z;
            public Pt3(double x, double y, double z) { X = x; Y = y; Z = z; }
        }

        private sealed class Seg3
        {
            public Pt3 A, B;
            public Brush Stroke;
            public double Thickness;
            public bool Dashed;
            public int Layer; // 0=ref, 1=plate, 2=sub, 3=primary
        }

        private sealed class Dim3
        {
            public Pt3 A, B;
            public Pt3 Offset;
            public string Text;
        }

        private sealed class IsoScene
        {
            public readonly List<Seg3> Segments = new List<Seg3>();
            public readonly List<Dim3> Dims = new List<Dim3>();

            public void Line(double x1, double y1, double z1, double x2, double y2, double z2,
                Brush stroke, double thickness = 2.2, bool dashed = false, int layer = 3)
            {
                Segments.Add(new Seg3
                {
                    A = new Pt3(x1, y1, z1),
                    B = new Pt3(x2, y2, z2),
                    Stroke = stroke,
                    Thickness = thickness,
                    Dashed = dashed,
                    Layer = layer
                });
            }

            public void Primary(double x1, double y1, double z1, double x2, double y2, double z2, double thickness = 2.6)
                => Line(x1, y1, z1, x2, y2, z2, SteelMemberBrush, thickness, false, 3);

            public void Sub(double x1, double y1, double z1, double x2, double y2, double z2, double thickness = 1.7)
                => Line(x1, y1, z1, x2, y2, z2, SteelSubBrush, thickness, false, 2);

            public void Plate(double x1, double y1, double z1, double x2, double y2, double z2, double thickness = 1.8)
                => Line(x1, y1, z1, x2, y2, z2, PlateBrush, thickness, false, 1);

            public void Ref(double x1, double y1, double z1, double x2, double y2, double z2, bool dashed = true)
                => Line(x1, y1, z1, x2, y2, z2, RefLineBrush, 1.1, dashed, 0);

            public void Base(double x, double y, double z = 0, double r = 0.08)
            {
                Plate(x - r, y - r, z, x + r, y - r, z, 2.0);
                Plate(x + r, y - r, z, x + r, y + r, z, 2.0);
                Plate(x + r, y + r, z, x - r, y + r, z, 2.0);
                Plate(x - r, y + r, z, x - r, y - r, z, 2.0);
            }

            public void Box(double x0, double y0, double z0, double dx, double dy, double dz,
                Brush stroke, double thickness = 2.0, bool dashed = false, int layer = 2)
            {
                double x1 = x0 + dx, y1 = y0 + dy, z1 = z0 + dz;
                Line(x0, y0, z0, x1, y0, z0, stroke, thickness, dashed, layer);
                Line(x1, y0, z0, x1, y1, z0, stroke, thickness, dashed, layer);
                Line(x1, y1, z0, x0, y1, z0, stroke, thickness, dashed, layer);
                Line(x0, y1, z0, x0, y0, z0, stroke, thickness, dashed, layer);

                Line(x0, y0, z1, x1, y0, z1, stroke, thickness, dashed, layer);
                Line(x1, y0, z1, x1, y1, z1, stroke, thickness, dashed, layer);
                Line(x1, y1, z1, x0, y1, z1, stroke, thickness, dashed, layer);
                Line(x0, y1, z1, x0, y0, z1, stroke, thickness, dashed, layer);

                Line(x0, y0, z0, x0, y0, z1, stroke, thickness, dashed, layer);
                Line(x1, y0, z0, x1, y0, z1, stroke, thickness, dashed, layer);
                Line(x1, y1, z0, x1, y1, z1, stroke, thickness, dashed, layer);
                Line(x0, y1, z0, x0, y1, z1, stroke, thickness, dashed, layer);
            }

            public void ArcXY(double cx, double cy, double cz, double r, double startDeg, double sweepDeg,
                Brush stroke, double thickness = 2.2, bool dashed = false, int layer = 3)
            {
                int segs = Math.Max(14, (int)Math.Ceiling(Math.Abs(sweepDeg) / 9.0));
                double a0 = startDeg * Math.PI / 180.0;
                double px = cx + r * Math.Cos(a0);
                double py = cy + r * Math.Sin(a0);
                for (int i = 1; i <= segs; i++)
                {
                    double a = (startDeg + sweepDeg * i / segs) * Math.PI / 180.0;
                    double nx = cx + r * Math.Cos(a);
                    double ny = cy + r * Math.Sin(a);
                    Line(px, py, cz, nx, ny, cz, stroke, thickness, dashed, layer);
                    px = nx; py = ny;
                }
            }

            public void ArcXZ(double cx, double cy, double cz, double r, double startDeg, double sweepDeg,
                Brush stroke, double thickness = 2.2, bool dashed = false, int layer = 3)
            {
                int segs = Math.Max(14, (int)Math.Ceiling(Math.Abs(sweepDeg) / 9.0));
                double a0 = startDeg * Math.PI / 180.0;
                double px = cx + r * Math.Cos(a0);
                double pz = cz + r * Math.Sin(a0);
                for (int i = 1; i <= segs; i++)
                {
                    double a = (startDeg + sweepDeg * i / segs) * Math.PI / 180.0;
                    double nx = cx + r * Math.Cos(a);
                    double nz = cz + r * Math.Sin(a);
                    Line(px, cy, pz, nx, cy, nz, stroke, thickness, dashed, layer);
                    px = nx; pz = nz;
                }
            }

            public void Dim(double x1, double y1, double z1, double x2, double y2, double z2,
                double ox, double oy, double oz, string text)
            {
                Dims.Add(new Dim3
                {
                    A = new Pt3(x1, y1, z1),
                    B = new Pt3(x2, y2, z2),
                    Offset = new Pt3(ox, oy, oz),
                    Text = text
                });
            }
        }

        /// <summary>经典工程轴测投影：X 向右下，Y 向右上（进深），Z 竖直向上。</summary>
        private static Point ProjectRaw(Pt3 p)
        {
            double u = p.X * 0.88 + p.Y * 0.56;
            double v = p.X * 0.28 - p.Y * 0.42 - p.Z * 0.96;
            return new Point(u, v);
        }

        private static double ClampRatio(double value, double reference, double min, double max)
        {
            if (reference <= 1e-6 || value <= 1e-6) return (min + max) * 0.5;
            double r = value / reference;
            if (r < min) return min;
            if (r > max) return max;
            return r;
        }

        public void RenderSchematic(SteelStructureParams p)
        {
            _canvas.Children.Clear();
            if (p == null) return;

            double w = _canvas.ActualWidth > 80 ? _canvas.ActualWidth : 440;
            double h = _canvas.ActualHeight > 80 ? _canvas.ActualHeight : 360;

            var scene = new IsoScene();
            BuildIsoScene(scene, p);

            // 计算全部 3D 几何与尺寸线的投影包围盒
            double minU = double.PositiveInfinity, maxU = double.NegativeInfinity;
            double minV = double.PositiveInfinity, maxV = double.NegativeInfinity;

            void IncludePt(Pt3 pt)
            {
                Point q = ProjectRaw(pt);
                if (q.X < minU) minU = q.X;
                if (q.X > maxU) maxU = q.X;
                if (q.Y < minV) minV = q.Y;
                if (q.Y > maxV) maxV = q.Y;
            }

            foreach (var s in scene.Segments)
            {
                IncludePt(s.A);
                IncludePt(s.B);
            }
            foreach (var d in scene.Dims)
            {
                IncludePt(new Pt3(d.A.X + d.Offset.X, d.A.Y + d.Offset.Y, d.A.Z + d.Offset.Z));
                IncludePt(new Pt3(d.B.X + d.Offset.X, d.B.Y + d.Offset.Y, d.B.Z + d.Offset.Z));
            }

            if (double.IsInfinity(minU) || double.IsInfinity(minV))
            {
                minU = -1; maxU = 1; minV = -1; maxV = 1;
            }

            double padL = 48, padR = 48, padT = 38, padB = 42;
            double availW = Math.Max(60, w - padL - padR);
            double availH = Math.Max(60, h - padT - padB);
            double spanU = Math.Max(0.15, maxU - minU);
            double spanV = Math.Max(0.15, maxV - minV);
            double scale = Math.Min(availW / spanU, availH / spanV);

            double midU = (minU + maxU) * 0.5;
            double midV = (minV + maxV) * 0.5;
            double cx = padL + availW * 0.5;
            double cy = padT + availH * 0.5;

            Point ToCanvas(Pt3 pt)
            {
                Point q = ProjectRaw(pt);
                return new Point(cx + (q.X - midU) * scale, cy + (q.Y - midV) * scale);
            }

            // 按层级排序绘制线段（参考线 -> 钢板 -> 次构件 -> 主构件）
            scene.Segments.Sort((a, b) => a.Layer.CompareTo(b.Layer));
            foreach (var seg in scene.Segments)
            {
                Point p1 = ToCanvas(seg.A);
                Point p2 = ToCanvas(seg.B);
                DrawLine(p1.X, p1.Y, p2.X, p2.Y, seg.Stroke, seg.Thickness, seg.Dashed);
            }

            // 绘制三维尺寸标注
            foreach (var d in scene.Dims)
            {
                Pt3 da = new Pt3(d.A.X + d.Offset.X, d.A.Y + d.Offset.Y, d.A.Z + d.Offset.Z);
                Pt3 db = new Pt3(d.B.X + d.Offset.X, d.B.Y + d.Offset.Y, d.B.Z + d.Offset.Z);
                Point a0 = ToCanvas(d.A);
                Point b0 = ToCanvas(d.B);
                Point a1 = ToCanvas(da);
                Point b1 = ToCanvas(db);

                DrawLine(a0.X, a0.Y, a1.X, a1.Y, DimLineBrush, 0.9, dashed: true);
                DrawLine(b0.X, b0.Y, b1.X, b1.Y, DimLineBrush, 0.9, dashed: true);
                DrawLine(a1.X, a1.Y, b1.X, b1.Y, DimLineBrush, 1.3);
                DrawTick(a1, b1);
                DrawTick(b1, a1);
                DrawDimBadge((a1.X + b1.X) * 0.5, (a1.Y + b1.Y) * 0.5, d.Text, w, h);
            }

            // 顶部标题
            AddTitleText(10, 7, $"轴测示意图 · {p.StructureTypeDisplayName}");

            // 左下角迷你轴测坐标指示
            DrawIsoTriad(24, h - 22);

            // 右下角截面/材质摘要
            string summary = string.IsNullOrWhiteSpace(p.ColumnSection)
                ? (p.Material ?? "Q355B")
                : $"{p.ColumnSection} · {p.Material ?? "Q355B"}";
            DrawBottomRightBadge(w - 10, h - 10, summary);
        }

        private void BuildIsoScene(IsoScene s, SteelStructureParams p)
        {
            double B = 2.0;
            double H = ClampRatio(p.Height, p.Span, 0.75, 1.85) * 1.45;
            double L = ClampRatio(p.Length, p.Span, 0.45, 1.60) * 1.45;
            double h1 = ClampRatio(p.SubHeight, p.Height > 1 ? p.Height : p.Span, 0.28, 0.75) * H;

            switch (p.StructureType)
            {
                // ==================== 1. 单跨门式刚架 ====================
                case ParametricStructureType.PortalFrame:
                {
                    double depth = 0.22;
                    s.Primary(0, 0, 0, 0, 0, H, 3.2);
                    s.Primary(B, 0, 0, B, 0, H, 3.2);
                    s.Primary(-0.08, 0, H, B + 0.08, 0, H, 3.4);
                    // 翼缘厚度示意（轴测纵深感）
                    s.Sub(0, depth, 0, 0, depth, H, 1.3);
                    s.Sub(B, depth, 0, B, depth, H, 1.3);
                    s.Sub(-0.08, depth, H, B + 0.08, depth, H, 1.3);
                    s.Plate(0, 0, H, 0, depth, H, 1.4);
                    s.Plate(B, 0, H, B, depth, H, 1.4);
                    s.Base(0, depth * 0.5, 0, 0.14);
                    s.Base(B, depth * 0.5, 0, 0.14);
                    s.Dim(0, 0, 0, B, 0, 0, 0, -0.35, 0, $"B={p.Span:0}");
                    s.Dim(0, 0, 0, 0, 0, H, -0.32, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 2. 多跨连续门式刚架 ====================
                case ParametricStructureType.MultiSpanPortalFrame:
                {
                    int bays = Math.Max(2, Math.Min(6, p.BayCount));
                    double totalB = 2.4;
                    for (int i = 0; i <= bays; i++)
                    {
                        double x = totalB * i / bays;
                        s.Primary(x, 0, 0, x, 0, H, 2.8);
                        s.Base(x, 0, 0, 0.10);
                        if (i < bays)
                        {
                            double xn = totalB * (i + 1) / bays;
                            s.Sub(x, 0, H - 0.22, x + 0.18, 0, H, 1.6);
                            s.Sub(xn, 0, H - 0.22, xn - 0.18, 0, H, 1.6);
                        }
                    }
                    s.Primary(-0.08, 0, H, totalB + 0.08, 0, H, 3.2);
                    // 后排浅色示意纵向延展
                    double d = 0.7;
                    for (int i = 0; i <= bays; i++)
                    {
                        double x = totalB * i / bays;
                        s.Ref(x, d, 0, x, d, H);
                        s.Ref(x, 0, H, x, d, H);
                    }
                    s.Ref(0, d, H, totalB, d, H);
                    s.Dim(0, 0, 0, totalB, 0, 0, 0, -0.35, 0, $"总跨 B={p.Span:0} ({bays}跨)");
                    s.Dim(0, 0, 0, 0, 0, H, -0.32, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 3. 带人字斜撑框架 ====================
                case ParametricStructureType.BracedFrame:
                {
                    s.Primary(0, 0, 0, 0, 0, H, 3.0);
                    s.Primary(B, 0, 0, B, 0, H, 3.0);
                    s.Primary(-0.06, 0, H, B + 0.06, 0, H, 3.0);
                    s.Primary(0, 0, h1, B, 0, h1, 2.6);
                    s.Sub(0, 0, h1, B * 0.5, 0, H, 2.2);
                    s.Sub(B, 0, h1, B * 0.5, 0, H, 2.2);
                    s.Base(0, 0, 0, 0.12);
                    s.Base(B, 0, 0, 0.12);
                    // 纵深参考
                    s.Ref(0, 0.45, 0, 0, 0.45, H);
                    s.Ref(B, 0.45, 0, B, 0.45, H);
                    s.Ref(0, 0, H, 0, 0.45, H);
                    s.Ref(B, 0, H, B, 0.45, H);
                    s.Dim(0, 0, 0, B, 0, 0, 0, -0.35, 0, $"B={p.Span:0}");
                    s.Dim(0, 0, 0, 0, 0, H, -0.32, 0, 0, $"H={p.Height:0}");
                    s.Dim(B, 0, 0, B, 0, h1, 0.30, 0, 0, $"h1={p.SubHeight:0}");
                    break;
                }

                // ==================== 4. 双层管廊支架 ====================
                case ParametricStructureType.DoubleTierFrame:
                {
                    double d = 0.55;
                    s.Primary(0, 0, 0, 0, 0, H, 3.0);
                    s.Primary(B, 0, 0, B, 0, H, 3.0);
                    s.Primary(-0.1, 0, H, B + 0.1, 0, H, 3.0);
                    s.Primary(-0.1, 0, h1, B + 0.1, 0, h1, 2.8);
                    s.Sub(0, 0, h1 - 0.25, 0.24, 0, h1, 1.8);
                    s.Sub(B, 0, h1 - 0.25, B - 0.24, 0, h1, 1.8);
                    s.Base(0, 0, 0, 0.12);
                    s.Base(B, 0, 0, 0.12);
                    // 工艺管道示意线沿 Y 方向穿过两层横梁
                    foreach (double px in new[] { B * 0.25, B * 0.5, B * 0.75 })
                    {
                        s.Ref(px, -0.25, H + 0.10, px, d + 0.25, H + 0.10, dashed: false);
                        s.Ref(px, -0.25, h1 + 0.10, px, d + 0.25, h1 + 0.10, dashed: false);
                    }
                    s.Dim(0, 0, 0, B, 0, 0, 0, -0.35, 0, $"B={p.Span:0}");
                    s.Dim(0, 0, 0, 0, 0, H, -0.32, 0, 0, $"H={p.Height:0}");
                    s.Dim(B, 0, 0, B, 0, h1, 0.32, 0, 0, $"h1={p.SubHeight:0}");
                    break;
                }

                // ==================== 5. 多层空间装置钢框架 ====================
                case ParametricStructureType.MultiBaySpaceFrame:
                {
                    double[] xs = { 0, B };
                    double[] ys = { 0, L };
                    foreach (double x in xs)
                        foreach (double y in ys)
                        {
                            s.Primary(x, y, 0, x, y, H, 2.8);
                            s.Base(x, y, 0, 0.10);
                        }
                    foreach (double z in new[] { h1, H })
                    {
                        s.Primary(0, 0, z, B, 0, z, 2.6);
                        s.Primary(0, L, z, B, L, z, 2.4);
                        s.Primary(0, 0, z, 0, L, z, 2.4);
                        s.Primary(B, 0, z, B, L, z, 2.6);
                        s.Sub(B * 0.5, 0, z, B * 0.5, L, z, 1.6);
                    }
                    // 纵向柱间斜撑
                    s.Sub(B, 0, 0, B, L, h1, 1.9);
                    s.Sub(0, 0, 0, 0, L, h1, 1.6);
                    s.Dim(0, 0, 0, B, 0, 0, 0, -0.32, 0, $"B={p.Span:0}");
                    s.Dim(B, 0, 0, B, L, 0, 0.30, 0, 0, $"L={p.Length:0}");
                    s.Dim(0, 0, 0, 0, 0, H, -0.32, 0, 0, $"H={p.Height:0}");
                    s.Dim(B, L, 0, B, L, h1, 0.30, 0, 0, $"h1={p.SubHeight:0}");
                    break;
                }

                // ==================== 6. 悬臂三角托架 ====================
                case ParametricStructureType.CantileverBracket:
                {
                    double wallD = 0.6;
                    // 附墙参考面
                    s.Ref(0, -wallD * 0.5, 0, 0, wallD * 0.5, 0);
                    s.Ref(0, -wallD * 0.5, H, 0, wallD * 0.5, H);
                    s.Ref(0, -wallD * 0.5, 0, 0, -wallD * 0.5, H);
                    s.Ref(0, wallD * 0.5, 0, 0, wallD * 0.5, H);
                    // 附墙柱 + 挑梁 + 斜撑
                    s.Primary(0, 0, 0, 0, 0, H, 3.2);
                    s.Primary(0, 0, H - 0.1, B, 0, H - 0.1, 3.0);
                    double braceZ = Math.Max(0.15, H - 0.1 - h1);
                    s.Sub(0, 0, braceZ, B * 0.85, 0, H - 0.1, 2.3);
                    s.Base(0, 0, 0, 0.12);
                    s.Dim(0, 0, H - 0.1, B, 0, H - 0.1, 0, 0, 0.28, $"挑出 L={p.Span:0}");
                    s.Dim(0, 0, 0, 0, 0, H, -0.32, 0, 0, $"H={p.Height:0}");
                    s.Dim(B, 0, braceZ, B, 0, H - 0.1, 0.26, 0, 0, $"h1={p.SubHeight:0}");
                    break;
                }

                // ==================== 7. 四柱空间检修钢平台 ====================
                case ParametricStructureType.SpacePlatform:
                {
                    double railH = Math.Min(0.55, Math.Max(0.32, H * 0.32));
                    DrawPlatform3D(s, B, L, H, railH, withStair: false);
                    s.Dim(0, 0, 0, B, 0, 0, 0, -0.32, 0, $"B={p.Span:0}");
                    s.Dim(B, 0, 0, B, L, 0, 0.30, 0, 0, $"L={p.Length:0}");
                    s.Dim(0, 0, 0, 0, 0, H, -0.32, 0, 0, $"H={p.Height:0}");
                    s.Dim(B, L, H, B, L, H + railH, 0.28, 0, 0, $"h1={p.SubHeight:0}");
                    break;
                }

                // ==================== 8. 悬挑走道平台 ====================
                case ParametricStructureType.CantileverWalkway:
                {
                    double len = 2.4;
                    double wWalk = ClampRatio(p.Length, p.Span, 0.28, 0.65) * len;
                    double zDeck = 1.0;
                    double railH = 0.48;
                    // 走道纵梁与铺板框
                    s.Primary(0, 0, zDeck, len, 0, zDeck, 2.8);
                    s.Primary(0, wWalk, zDeck, len, wWalk, zDeck, 2.6);
                    int nBr = 4;
                    for (int i = 0; i < nBr; i++)
                    {
                        double x = len * i / (nBr - 1);
                        // 牛腿与三角斜撑
                        s.Primary(x, 0, zDeck, x, wWalk, zDeck, 2.2);
                        s.Primary(x, wWalk, zDeck, x, wWalk, zDeck - 0.45, 2.2);
                        s.Sub(x, wWalk, zDeck - 0.45, x, 0.08, zDeck, 1.8);
                        // 外侧栏杆立柱
                        s.Sub(x, 0, zDeck, x, 0, zDeck + railH, 1.6);
                    }
                    s.Sub(0, 0, zDeck + railH, len, 0, zDeck + railH, 2.0);
                    s.Sub(0, 0, zDeck + railH * 0.5, len, 0, zDeck + railH * 0.5, 1.4);
                    s.Dim(0, 0, zDeck - 0.45, len, 0, zDeck - 0.45, 0, -0.28, 0, $"L={p.Span:0}");
                    s.Dim(len, 0, zDeck, len, wWalk, zDeck, 0.28, 0, 0, $"W={p.Length:0}");
                    s.Dim(0, 0, zDeck, 0, 0, zDeck + railH, -0.28, 0, 0, $"h1={p.SubHeight:0}");
                    break;
                }

                // ==================== 9. 双坡屋面钢桁架 ====================
                case ParametricStructureType.RoofTruss:
                {
                    double ridge = Math.Max(0.45, Math.Min(0.95, h1));
                    double zTop = H + ridge;
                    s.Primary(0, 0, 0, 0, 0, H, 3.0);
                    s.Primary(B, 0, 0, B, 0, H, 3.0);
                    s.Primary(0, 0, H, B, 0, H, 2.8);
                    s.Primary(0, 0, H, B * 0.5, 0, zTop, 3.0);
                    s.Primary(B, 0, H, B * 0.5, 0, zTop, 3.0);
                    s.Sub(B * 0.5, 0, H, B * 0.5, 0, zTop, 1.8);
                    s.Sub(B * 0.25, 0, H, B * 0.25, 0, H + ridge * 0.5, 1.6);
                    s.Sub(B * 0.75, 0, H, B * 0.75, 0, H + ridge * 0.5, 1.6);
                    s.Sub(B * 0.25, 0, H + ridge * 0.5, B * 0.5, 0, H, 1.6);
                    s.Sub(B * 0.75, 0, H + ridge * 0.5, B * 0.5, 0, H, 1.6);
                    s.Base(0, 0, 0, 0.12);
                    s.Base(B, 0, 0, 0.12);
                    // 纵深第二榀屋架浅色参考
                    double d = 0.75;
                    s.Ref(0, d, 0, 0, d, H); s.Ref(B, d, 0, B, d, H);
                    s.Ref(0, d, H, B, d, H); s.Ref(0, d, H, B * 0.5, d, zTop); s.Ref(B, d, H, B * 0.5, d, zTop);
                    s.Ref(B * 0.5, 0, zTop, B * 0.5, d, zTop); s.Ref(0, 0, H, 0, d, H); s.Ref(B, 0, H, B, d, H);
                    s.Dim(0, 0, 0, B, 0, 0, 0, -0.35, 0, $"B={p.Span:0}");
                    s.Dim(0, 0, 0, 0, 0, H, -0.32, 0, 0, $"H={p.Height:0}");
                    s.Dim(B, 0, H, B, 0, zTop, 0.30, 0, 0, $"h1={p.SubHeight:0}");
                    break;
                }

                // ==================== 10. 空间管廊桁架桥 ====================
                case ParametricStructureType.PipeBridgeTruss:
                {
                    double len = 2.5;
                    double wid = ClampRatio(p.Length, p.Span, 0.25, 0.55) * len;
                    double th = ClampRatio(p.Height, p.Span, 0.25, 0.55) * len;
                    s.Box(0, 0, 0, len, wid, th, SteelMemberBrush, 2.5, false, 3);
                    int segs = 4;
                    for (int i = 0; i < segs; i++)
                    {
                        double xa = len * i / segs;
                        double xb = len * (i + 1) / segs;
                        if (i > 0)
                        {
                            s.Sub(xa, 0, 0, xa, 0, th, 1.6);
                            s.Sub(xa, wid, 0, xa, wid, th, 1.4);
                            s.Sub(xa, 0, 0, xa, wid, 0, 1.4);
                            s.Sub(xa, 0, th, xa, wid, th, 1.4);
                        }
                        s.Sub(xa, 0, 0, xb, 0, th, 1.6);
                        s.Sub(xa, wid, 0, xb, wid, th, 1.3);
                        s.Sub(xa, 0, th, xb, wid, th, 1.2);
                    }
                    s.Dim(0, 0, 0, len, 0, 0, 0, -0.32, 0, $"L={p.Span:0}");
                    s.Dim(len, 0, 0, len, wid, 0, 0.28, 0, 0, $"W={p.Length:0}");
                    s.Dim(0, 0, 0, 0, 0, th, -0.30, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 11. 储罐连续螺旋盘梯 ====================
                case ParametricStructureType.SpiralStair:
                {
                    double rIn = 0.72;
                    double rOut = 1.08;
                    double hTot = 1.85;
                    double sweepDeg = Math.Min(360.0, Math.Max(90.0, p.SubHeight));
                    // 储罐虚线圆柱
                    s.ArcXY(0, 0, 0, rIn, 0, 360, RefLineBrush, 1.2, true, 0);
                    s.ArcXY(0, 0, hTot, rIn, 0, 360, RefLineBrush, 1.2, true, 0);
                    s.Ref(-rIn, 0, 0, -rIn, 0, hTot);
                    s.Ref(rIn, 0, 0, rIn, 0, hTot);

                    int steps = 14;
                    double railH = 0.34;
                    for (int i = 0; i < steps; i++)
                    {
                        double t0 = (double)i / steps, t1 = (double)(i + 1) / steps;
                        double a0 = (sweepDeg * t0) * Math.PI / 180.0;
                        double a1 = (sweepDeg * t1) * Math.PI / 180.0;
                        double z0 = hTot * t0, z1 = hTot * t1;
                        // 外侧螺旋斜梁与扶手
                        s.Primary(rOut * Math.Cos(a0), rOut * Math.Sin(a0), z0,
                                  rOut * Math.Cos(a1), rOut * Math.Sin(a1), z1, 2.6);
                        s.Sub(rOut * Math.Cos(a0), rOut * Math.Sin(a0), z0 + railH,
                              rOut * Math.Cos(a1), rOut * Math.Sin(a1), z1 + railH, 1.8);
                        if (i % 2 == 0 || i == steps - 1)
                        {
                            s.Sub(rOut * Math.Cos(a0), rOut * Math.Sin(a0), z0,
                                  rOut * Math.Cos(a0), rOut * Math.Sin(a0), z0 + railH, 1.4);
                        }
                        // 踏步板
                        s.Plate(rIn * Math.Cos(a0), rIn * Math.Sin(a0), z0,
                                rOut * Math.Cos(a0), rOut * Math.Sin(a0), z0, 1.8);
                    }
                    s.Dim(-rOut, 0, 0, rOut, 0, 0, 0, -0.45, 0, $"D={p.Span:0} ({p.SubHeight:0}°)");
                    s.Dim(rOut, 0, 0, rOut, 0, hTot, 0.32, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 12. 直跑钢斜梯 ====================
                case ParametricStructureType.StraightStair:
                {
                    double len = 2.0;
                    double wid = ClampRatio(p.Length, p.Span, 0.28, 0.65) * len;
                    double hStair = ClampRatio(p.Height, p.Span, 0.55, 1.25) * 1.4;
                    double railH = 0.42;
                    DrawStairFlight3D(s, 0, 0, 0, len, wid, hStair, railH);
                    s.Dim(0, 0, 0, len, 0, 0, 0, -0.30, 0, $"L={p.Span:0}");
                    s.Dim(len, 0, 0, len, wid, 0, 0.28, 0, 0, $"W={p.Length:0}");
                    s.Dim(len, 0, 0, len, 0, hStair, 0.28, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 13. 带安全护笼钢直梯 ====================
                case ParametricStructureType.SafetyCagedLadder:
                {
                    double ladW = 0.46;
                    double hLad = 2.2;
                    double cageZ = hLad * 0.38;
                    double rCage = 0.36;
                    // 两侧立管
                    s.Primary(-ladW * 0.5, 0, 0, -ladW * 0.5, 0, hLad, 2.8);
                    s.Primary(ladW * 0.5, 0, 0, ladW * 0.5, 0, hLad, 2.8);
                    // 踏棍
                    for (int i = 1; i <= 8; i++)
                    {
                        double z = hLad * i / 8.8;
                        s.Sub(-ladW * 0.5, 0, z, ladW * 0.5, 0, z, 1.7);
                    }
                    // 护笼圆箍 + 竖条
                    for (int k = 0; k <= 3; k++)
                    {
                        double z = cageZ + (hLad - cageZ) * k / 3.0;
                        s.ArcXY(0, -rCage * 0.65, z, rCage, 170, 200, SteelSubBrush, 1.7, false, 2);
                    }
                    foreach (double deg in new[] { 200.0, 270.0, 340.0 })
                    {
                        double rad = deg * Math.PI / 180.0;
                        double x = rCage * Math.Cos(rad);
                        double y = -rCage * 0.65 + rCage * Math.Sin(rad);
                        s.Sub(x, y, cageZ, x, y, hLad, 1.4);
                    }
                    s.Dim(-ladW * 0.5, 0, 0, ladW * 0.5, 0, 0, 0, -0.45, 0, $"W={p.Span:0}");
                    s.Dim(ladW * 0.5, 0, 0, ladW * 0.5, 0, hLad, 0.36, 0, 0, $"H={p.Height:0}");
                    s.Dim(-ladW * 0.5, 0, 0, -ladW * 0.5, 0, cageZ, -0.34, 0, 0, $"h1={p.SubHeight:0}");
                    break;
                }

                // ==================== 14. 平台连续安全栏杆/扶手 ====================
                case ParametricStructureType.HandrailGuardrail:
                {
                    double len = 2.4;
                    double hRail = 1.05;
                    s.Primary(0, 0, hRail, len, 0, hRail, 2.8);
                    s.Sub(0, 0, hRail * 0.52, len, 0, hRail * 0.52, 2.0);
                    s.Plate(0, 0, 0.08, len, 0, 0.08, 2.4);
                    for (int i = 0; i < 4; i++)
                    {
                        double x = len * i / 3.0;
                        s.Primary(x, 0, 0, x, 0, hRail, 2.4);
                        s.Base(x, 0, 0, 0.08);
                    }
                    s.Ref(-0.15, -0.25, 0, len + 0.15, -0.25, 0);
                    s.Ref(-0.15, 0.35, 0, len + 0.15, 0.35, 0);
                    s.Dim(0, 0, 0, len, 0, 0, 0, -0.35, 0, $"L={p.Span:0} (间距≤{p.SubHeight:0})");
                    s.Dim(0, 0, 0, 0, 0, hRail, -0.30, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 15. 直线单根型钢 ====================
                case ParametricStructureType.LinearSteelMember:
                {
                    double len = 2.4;
                    double dz = Math.Abs(p.EndPointOffsetZ) > 10 ? ClampRatio(Math.Abs(p.EndPointOffsetZ), p.Span, 0.2, 0.75) * len * Math.Sign(p.EndPointOffsetZ) : 0;
                    double bw = 0.18, bh = 0.24;
                    // 轴测挤出工字轮廓
                    s.Primary(0, -bw, 0, len, -bw, dz, 2.4);
                    s.Primary(0, bw, 0, len, bw, dz, 2.4);
                    s.Primary(0, -bw, bh, len, -bw, dz + bh, 2.6);
                    s.Primary(0, bw, bh, len, bw, dz + bh, 2.6);
                    s.Sub(0, 0, 0, len, 0, dz, 1.5);
                    s.Sub(0, 0, bh, len, 0, dz + bh, 1.5);
                    // 两端 H 截面轮廓
                    foreach (var end in new[] { (0.0, 0.0), (len, dz) })
                    {
                        s.Primary(end.Item1, -bw, end.Item2, end.Item1, bw, end.Item2, 2.2);
                        s.Primary(end.Item1, -bw, end.Item2 + bh, end.Item1, bw, end.Item2 + bh, 2.2);
                        s.Primary(end.Item1, 0, end.Item2, end.Item1, 0, end.Item2 + bh, 2.2);
                    }
                    s.Dim(0, 0, 0, len, 0, 0, 0, -0.38, 0, $"L={p.Span:0}");
                    if (Math.Abs(p.EndPointOffsetZ) > 10)
                        s.Dim(len, 0, 0, len, 0, dz, 0.30, 0, 0, $"ΔZ={p.EndPointOffsetZ:0}");
                    break;
                }

                // ==================== 16. 弧形单根型钢 ====================
                case ParametricStructureType.ArcSteelMember:
                {
                    double r = 1.15;
                    double sweep = Math.Min(360.0, Math.Max(20.0, p.SubHeight));
                    double dz = Math.Abs(p.EndPointOffsetZ) > 10 ? 0.55 * Math.Sign(p.EndPointOffsetZ) : 0;
                    int segs = 24;
                    for (int i = 0; i < segs; i++)
                    {
                        double t0 = (double)i / segs, t1 = (double)(i + 1) / segs;
                        double a0 = sweep * t0 * Math.PI / 180.0;
                        double a1 = sweep * t1 * Math.PI / 180.0;
                        double z0 = dz * t0, z1 = dz * t1;
                        s.Primary(r * Math.Cos(a0), r * Math.Sin(a0), z0,
                                  r * Math.Cos(a1), r * Math.Sin(a1), z1, 3.0);
                        s.Sub(r * Math.Cos(a0), r * Math.Sin(a0), z0 + 0.16,
                              r * Math.Cos(a1), r * Math.Sin(a1), z1 + 0.16, 1.8);
                    }
                    s.Ref(0, 0, 0, r, 0, 0);
                    double aEnd = sweep * Math.PI / 180.0;
                    s.Ref(0, 0, 0, r * Math.Cos(aEnd), r * Math.Sin(aEnd), 0);
                    s.Dim(0, 0, 0, r, 0, 0, 0, -0.30, 0, $"R={p.Span:0} · {p.SubHeight:0}°");
                    break;
                }

                // ==================== 17. 螺旋单根型钢 ====================
                case ParametricStructureType.SpiralSteelMember:
                {
                    double r = 0.85;
                    double hHelix = 2.0;
                    double turns = Math.Max(0.25, Math.Min(4.5, p.SubHeight));
                    int segs = 72;
                    for (int i = 0; i < segs; i++)
                    {
                        double t0 = (double)i / segs, t1 = (double)(i + 1) / segs;
                        double a0 = turns * 2 * Math.PI * t0;
                        double a1 = turns * 2 * Math.PI * t1;
                        s.Primary(r * Math.Cos(a0), r * Math.Sin(a0), hHelix * t0,
                                  r * Math.Cos(a1), r * Math.Sin(a1), hHelix * t1, 2.8);
                    }
                    s.Ref(0, 0, 0, 0, 0, hHelix);
                    s.ArcXY(0, 0, 0, r, 0, 360, RefLineBrush, 1.0, true, 0);
                    s.Dim(0, 0, 0, r, 0, 0, 0, -0.32, 0, $"R={p.Length:0} ({p.SubHeight:0.#}圈)");
                    s.Dim(r, 0, 0, r, 0, hHelix, 0.30, 0, 0, $"H={p.Span:0}");
                    break;
                }

                // ==================== 18. 卧式容器鞍式钢支座 ====================
                case ParametricStructureType.VesselSupport:
                {
                    double r = 0.72;
                    double wSad = 0.65;
                    double cz = 1.15;
                    double wrap = Math.Min(150.0, Math.Max(90.0, p.SubHeight));
                    double aStart = 270.0 - wrap * 0.5;
                    // 容器筒体虚线轮廓
                    s.ArcXZ(0, -0.35, cz, r, 0, 360, RefLineBrush, 1.1, true, 0);
                    s.ArcXZ(0, wSad + 0.35, cz, r, 0, 360, RefLineBrush, 1.1, true, 0);
                    s.Ref(-r, -0.35, cz, -r, wSad + 0.35, cz);
                    s.Ref(r, -0.35, cz, r, wSad + 0.35, cz);
                    // 弧形鞍板与腹板/底板
                    s.ArcXZ(0, 0, cz, r, aStart, wrap, SteelMemberBrush, 2.8, false, 3);
                    s.ArcXZ(0, wSad, cz, r, aStart, wrap, SteelMemberBrush, 2.4, false, 3);
                    double aL = aStart * Math.PI / 180.0, aR = (aStart + wrap) * Math.PI / 180.0;
                    double xL = r * Math.Cos(aL), zL = cz + r * Math.Sin(aL);
                    double xR = r * Math.Cos(aR), zR = cz + r * Math.Sin(aR);
                    s.Primary(xL, 0, zL, xL, wSad, zL, 2.4);
                    s.Primary(xR, 0, zR, xR, wSad, zR, 2.4);
                    s.Box(xL - 0.12, 0, 0, (xR - xL) + 0.24, wSad, 0.08, PlateBrush, 2.0, false, 1);
                    s.Primary(xL, wSad * 0.5, 0.08, xL, wSad * 0.5, zL, 2.4);
                    s.Primary(xR, wSad * 0.5, 0.08, xR, wSad * 0.5, zR, 2.4);
                    s.Sub(0, 0, 0.08, 0, 0, cz - r, 1.8);
                    s.Sub(0, wSad, 0.08, 0, wSad, cz - r, 1.8);
                    s.Dim(-r, 0, 0, r, 0, 0, 0, -0.35, 0, $"DN={p.Span:0}");
                    s.Dim(xR + 0.12, 0, 0, xR + 0.12, wSad, 0, 0.28, 0, 0, $"B={p.Length:0}");
                    s.Dim(-r, 0, 0, -r, 0, cz, -0.34, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 19. 塔器环形检修平台 ====================
                case ParametricStructureType.TowerCircularPlatform:
                {
                    double rIn = 0.65;
                    double rOut = 1.15;
                    double zDeck = 1.05;
                    double railH = 0.42;
                    double sweep = Math.Min(360.0, Math.Max(90.0, p.SubHeight));
                    // 塔壁圆柱
                    s.ArcXY(0, 0, 0, rIn, 0, 360, RefLineBrush, 1.2, true, 0);
                    s.ArcXY(0, 0, zDeck + 0.6, rIn, 0, 360, RefLineBrush, 1.2, true, 0);
                    s.Ref(-rIn, 0, 0, -rIn, 0, zDeck + 0.6);
                    s.Ref(rIn, 0, 0, rIn, 0, zDeck + 0.6);
                    // 内外环梁与扶手
                    s.Primary(0, 0, 0, 0, 0, 0, 0); // noop
                    s.ArcXY(0, 0, zDeck, rIn, 0, sweep, SteelMemberBrush, 2.2, false, 3);
                    s.ArcXY(0, 0, zDeck, rOut, 0, sweep, SteelMemberBrush, 2.8, false, 3);
                    s.ArcXY(0, 0, zDeck + railH, rOut, 0, sweep, SteelSubBrush, 1.8, false, 2);
                    int bCnt = 6;
                    for (int i = 0; i <= bCnt; i++)
                    {
                        double a = (sweep * i / bCnt) * Math.PI / 180.0;
                        double xi = rIn * Math.Cos(a), yi = rIn * Math.Sin(a);
                        double xo = rOut * Math.Cos(a), yo = rOut * Math.Sin(a);
                        s.Primary(xi, yi, zDeck, xo, yo, zDeck, 2.2);
                        s.Sub(xi, yi, zDeck - 0.55, xo * 0.88, yo * 0.88, zDeck, 1.6);
                        s.Sub(xo, yo, zDeck, xo, yo, zDeck + railH, 1.5);
                    }
                    s.Dim(-rIn, 0, 0, rIn, 0, 0, 0, -0.45, 0, $"DN={p.Span:0} ({p.SubHeight:0}°)");
                    s.Dim(rIn, 0, zDeck, rOut, 0, zDeck, 0, -0.35, 0, $"W={p.Length:0}");
                    break;
                }

                // ==================== 20. 立式塔器/容器钢裙座 ====================
                case ParametricStructureType.VerticalVesselSkirt:
                {
                    double rSk = 0.68;
                    double rRing = 0.86;
                    double hSk = 1.75;
                    s.ArcXY(0, 0, 0, rRing, 0, 360, PlateBrush, 2.4, false, 1);
                    s.ArcXY(0, 0, 0, rSk, 0, 360, SteelMemberBrush, 2.4, false, 3);
                    s.ArcXY(0, 0, hSk, rSk, 0, 360, SteelMemberBrush, 2.6, false, 3);
                    s.Primary(-rSk, 0, 0, -rSk, 0, hSk, 2.6);
                    s.Primary(rSk, 0, 0, rSk, 0, hSk, 2.6);
                    int ribs = Math.Max(8, Math.Min(16, p.BayCount));
                    for (int i = 0; i < ribs; i++)
                    {
                        double a = 2.0 * Math.PI * i / ribs;
                        s.Sub(rSk * Math.Cos(a), rSk * Math.Sin(a), 0.34,
                              rRing * Math.Cos(a), rRing * Math.Sin(a), 0, 1.5);
                    }
                    // 检查孔
                    s.ArcXZ(0, -rSk, hSk * 0.48, 0.20, 0, 360, SteelSubBrush, 2.0, false, 2);
                    s.Dim(-rSk, 0, 0, rSk, 0, 0, 0, -0.42, 0, $"DN={p.Span:0} ({ribs}筋)");
                    s.Dim(rRing, 0, 0, rRing, 0, hSk, 0.32, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 21. 化工管道加劲管托/支座 ====================
                case ParametricStructureType.PipeShoeSupport:
                {
                    double rP = 0.42;
                    double len = 1.45;
                    double bw = 0.78;
                    double cz = 1.05;
                    // 工艺管道圆柱
                    s.ArcXZ(0, -0.3, cz, rP, 0, 360, RefLineBrush, 1.2, true, 0);
                    s.ArcXZ(0, len + 0.3, cz, rP, 0, 360, RefLineBrush, 1.2, true, 0);
                    s.Ref(-rP, -0.3, cz, -rP, len + 0.3, cz);
                    s.Ref(rP, -0.3, cz, rP, len + 0.3, cz);
                    // 底板 + 腹板 + 加劲肋 + 双管卡
                    s.Box(-bw * 0.5, 0, 0, bw, len, 0.08, PlateBrush, 2.0, false, 1);
                    s.Primary(0, 0, 0.08, 0, 0, cz - rP, 2.6);
                    s.Primary(0, len, 0.08, 0, len, cz - rP, 2.4);
                    s.Primary(0, 0, cz - rP, 0, len, cz - rP, 2.6);
                    foreach (double ry in new[] { len * 0.25, len * 0.75 })
                    {
                        s.Sub(-bw * 0.42, ry, 0.08, 0, ry, cz - rP, 1.8);
                        s.Sub(bw * 0.42, ry, 0.08, 0, ry, cz - rP, 1.8);
                        s.ArcXZ(0, ry, cz, rP + 0.05, 0, 360, SteelSubBrush, 1.9, false, 2);
                    }
                    s.Dim(-bw * 0.5, 0, 0, bw * 0.5, 0, 0, 0, -0.35, 0, $"B={p.SubHeight:0} (DN{p.Span:0})");
                    s.Dim(bw * 0.5, 0, 0, bw * 0.5, len, 0, 0.28, 0, 0, $"L={p.Length:0}");
                    s.Dim(-bw * 0.5, 0, 0, -bw * 0.5, 0, cz, -0.32, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 22. 储罐拱顶环形安全栏杆 ====================
                case ParametricStructureType.TankRoofHandrail:
                {
                    double rT = 1.15;
                    double hR = 0.55;
                    double sweep = Math.Min(360.0, Math.Max(90.0, p.SubHeight));
                    s.ArcXY(0, 0, 0, rT, 0, 360, RefLineBrush, 1.2, true, 0);
                    s.ArcXY(0, 0, hR, rT, 0, sweep, SteelMemberBrush, 2.6, false, 3);
                    s.ArcXY(0, 0, hR * 0.5, rT, 0, sweep, SteelSubBrush, 1.7, false, 2);
                    int posts = 8;
                    for (int i = 0; i <= posts; i++)
                    {
                        double a = (sweep * i / posts) * Math.PI / 180.0;
                        double x = rT * Math.Cos(a), y = rT * Math.Sin(a);
                        s.Primary(x, y, 0, x, y, hR, 2.0);
                    }
                    s.Dim(-rT, 0, 0, rT, 0, 0, 0, -0.40, 0, $"D={p.Span:0} ({p.SubHeight:0}°)");
                    s.Dim(rT, 0, 0, rT, 0, hR, 0.30, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 23. 变电站A字型龙门构架 ====================
                case ParametricStructureType.SubstationGantryFrame:
                {
                    double span = 2.3;
                    double spread = 0.72;
                    double hBeam = 1.65;
                    double hRod = 0.45;
                    // 左右 A 字柱
                    foreach (double x in new[] { 0.0, span })
                    {
                        s.Primary(x, 0, 0, x, spread * 0.5, hBeam, 2.8);
                        s.Primary(x, spread, 0, x, spread * 0.5, hBeam, 2.8);
                        s.Base(x, 0, 0, 0.10);
                        s.Base(x, spread, 0, 0.10);
                        for (int k = 1; k <= 3; k++)
                        {
                            double t = k / 4.0;
                            s.Sub(x, spread * 0.5 * t, hBeam * t, x, spread * (1.0 - 0.5 * t), hBeam * t, 1.4);
                        }
                        // 柱顶避雷针
                        s.Sub(x, spread * 0.5, hBeam, x, spread * 0.5, hBeam + hRod, 2.0);
                    }
                    // 顶部格构横梁
                    s.Box(0, spread * 0.5 - 0.12, hBeam - 0.18, span, 0.24, 0.22, SteelSubBrush, 1.8, false, 2);
                    s.Dim(0, 0, 0, span, 0, 0, 0, -0.35, 0, $"B={p.Span:0}");
                    s.Dim(span, 0, 0, span, spread, 0, 0.30, 0, 0, $"L={p.Length:0}");
                    s.Dim(0, 0, 0, 0, 0, hBeam, -0.34, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 24. 电力电缆沟多层支架 ====================
                case ParametricStructureType.CableTrenchSupport:
                {
                    int tiers = Math.Max(2, Math.Min(6, p.BayCount));
                    double len = 2.2;
                    double armW = 0.62;
                    double hCol = 1.55;
                    int posts = 4;
                    for (int i = 0; i < posts; i++)
                    {
                        double x = len * i / (posts - 1);
                        s.Primary(x, 0, 0, x, 0, hCol, 2.6);
                        for (int t = 0; t < tiers; t++)
                        {
                            double z = 0.25 + (hCol - 0.40) * t / Math.Max(1, tiers - 1);
                            s.Sub(x, 0, z, x, -armW, z, 2.0);
                        }
                    }
                    // 纵向接地扁钢与托盘示意
                    s.Plate(0, 0, 0.08, len, 0, 0.08, 2.2);
                    s.Ref(0, -armW, 0.25, len, -armW, 0.25);
                    s.Dim(0, 0, 0, len, 0, 0, 0, 0.35, 0, $"L={p.Span:0} ({tiers}层)");
                    s.Dim(0, 0, hCol, 0, -armW, hCol, -0.25, 0, 0, $"W={p.Length:0}");
                    s.Dim(len, 0, 0, len, 0, hCol, 0.32, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 25. 冶金厂房重型吊车梁系统 ====================
                case ParametricStructureType.CraneRunwayGirder:
                {
                    double len = 2.5;
                    double gh = 0.68;
                    double brakeW = 0.55;
                    // 吊车梁腹板/上下翼缘
                    s.Primary(0, 0, 0, len, 0, 0, 3.0);
                    s.Primary(0, 0, gh, len, 0, gh, 3.0);
                    s.Primary(0, 0, 0, 0, 0, gh, 2.6);
                    s.Primary(len, 0, 0, len, 0, gh, 2.6);
                    // 钢轨
                    s.Sub(0, 0, gh + 0.08, len, 0, gh + 0.08, 2.2);
                    // 加劲肋
                    for (int i = 1; i <= 4; i++)
                    {
                        double x = len * i / 5.0;
                        s.Sub(x, 0, 0, x, 0, gh, 1.6);
                    }
                    // 制动走道板与边梁
                    s.Plate(0, 0, gh, 0, brakeW, gh, 2.0);
                    s.Plate(len, 0, gh, len, brakeW, gh, 2.0);
                    s.Sub(0, brakeW, gh, len, brakeW, gh, 2.2);
                    s.Dim(0, 0, 0, len, 0, 0, 0, -0.35, 0, $"L={p.Span:0}");
                    s.Dim(len, 0, gh, len, brakeW, gh, 0.28, 0, 0, $"B={p.Length:0}");
                    s.Dim(0, 0, 0, 0, 0, gh, -0.32, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 26. 冶金厂房柱间交叉支撑 ====================
                case ParametricStructureType.CrossBracingFrame:
                {
                    double zCorbel = h1;
                    s.Primary(0, 0, 0, 0, 0, H, 3.2);
                    s.Primary(B, 0, 0, B, 0, H, 3.2);
                    s.Primary(0, 0, H, B, 0, H, 2.6);
                    s.Primary(0, 0, zCorbel, B, 0, zCorbel, 2.6);
                    // 下柱交叉支撑
                    s.Sub(0, 0, 0, B, 0, zCorbel, 2.1);
                    s.Sub(B, 0, 0, 0, 0, zCorbel, 2.1);
                    // 上柱交叉支撑
                    s.Sub(0, 0, zCorbel, B, 0, H, 1.8);
                    s.Sub(B, 0, zCorbel, 0, 0, H, 1.8);
                    s.Base(0, 0, 0, 0.12);
                    s.Base(B, 0, 0, 0.12);
                    s.Dim(0, 0, 0, B, 0, 0, 0, -0.35, 0, $"柱距 L={p.Span:0}");
                    s.Dim(0, 0, 0, 0, 0, H, -0.32, 0, 0, $"H={p.Height:0}");
                    s.Dim(B, 0, 0, B, 0, zCorbel, 0.30, 0, 0, $"h1={p.SubHeight:0}");
                    break;
                }

                // ==================== 27. 钢平台成套（区域组合） ====================
                case ParametricStructureType.PlatformAssembly:
                {
                    double railH = Math.Min(0.50, Math.Max(0.30, H * 0.30));
                    DrawPlatform3D(s, B, L, H, railH, withStair: true);
                    s.Dim(0, 0, 0, B, 0, 0, 0, -0.35, 0, $"B={p.Span:0}");
                    s.Dim(B, 0, 0, B, L, 0, 0.32, 0, 0, $"L={p.Length:0}");
                    s.Dim(0, 0, 0, 0, 0, H, -0.34, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 28. 厂房柱列（区域组合） ====================
                case ParametricStructureType.ColumnRowAssembly:
                {
                    int bays = Math.Max(2, Math.Min(5, p.BayCount));
                    double totalL = 2.6;
                    double zCrane = H * 0.68;
                    for (int i = 0; i <= bays; i++)
                    {
                        double x = totalL * i / bays;
                        s.Primary(x, 0, 0, x, 0, H, 2.8);
                        s.Base(x, 0, 0, 0.10);
                    }
                    // 顶系杆与吊车梁
                    s.Primary(0, 0, H, totalL, 0, H, 2.4);
                    s.Primary(0, 0.14, zCrane, totalL, 0.14, zCrane, 2.8);
                    // 中跨交叉支撑
                    int mid = bays / 2;
                    double xa = totalL * mid / bays, xb = totalL * (mid + 1) / bays;
                    s.Sub(xa, 0, 0, xb, 0, zCrane, 2.0);
                    s.Sub(xb, 0, 0, xa, 0, zCrane, 2.0);
                    s.Sub(xa, 0, zCrane, xb, 0, H, 1.7);
                    s.Sub(xb, 0, zCrane, xa, 0, H, 1.7);
                    s.Dim(0, 0, 0, totalL, 0, 0, 0, -0.35, 0, $"柱距×{bays}跨");
                    s.Dim(0, 0, 0, 0, 0, H, -0.34, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 29. 管廊成套区段（区域组合） ====================
                case ParametricStructureType.PipeRackAssembly:
                {
                    int bays = Math.Max(2, Math.Min(5, p.BayCount));
                    double totalY = 2.2;
                    double wRack = 1.35;
                    for (int i = 0; i <= bays; i++)
                    {
                        double y = totalY * i / bays;
                        s.Primary(0, y, 0, 0, y, H, 2.6);
                        s.Primary(wRack, y, 0, wRack, y, H, 2.6);
                        s.Primary(-0.08, y, H, wRack + 0.08, y, H, 2.6);
                        s.Primary(-0.08, y, h1, wRack + 0.08, y, h1, 2.4);
                        s.Base(0, y, 0, 0.08);
                        s.Base(wRack, y, 0, 0.08);
                    }
                    // 纵向系杆
                    s.Sub(0, 0, H, 0, totalY, H, 1.8);
                    s.Sub(wRack, 0, H, wRack, totalY, H, 1.8);
                    s.Sub(0, 0, h1, 0, totalY, h1, 1.6);
                    s.Sub(wRack, 0, h1, wRack, totalY, h1, 1.6);
                    // 纵向支撑
                    s.Sub(wRack, 0, 0, wRack, totalY / bays, h1, 1.8);
                    s.Dim(0, 0, 0, wRack, 0, 0, 0, -0.34, 0, $"B={p.Span:0}");
                    s.Dim(wRack, 0, 0, wRack, totalY, 0, 0.30, 0, 0, $"L×{bays}跨");
                    s.Dim(0, 0, 0, 0, 0, H, -0.32, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 30. 楼梯成套（区域组合） ====================
                case ParametricStructureType.StairAssembly:
                {
                    double len = 1.9;
                    double wid = 0.72;
                    double hSt = 1.35;
                    double railH = 0.40;
                    double platL = 0.72;
                    DrawStairFlight3D(s, 0, 0, 0, len, wid, hSt, railH);
                    // 顶部休息平台 + 平台栏杆
                    s.Primary(len, 0, hSt, len + platL, 0, hSt, 2.6);
                    s.Primary(len, wid, hSt, len + platL, wid, hSt, 2.6);
                    s.Primary(len + platL, 0, hSt, len + platL, wid, hSt, 2.6);
                    s.Primary(len + platL, 0, 0, len + platL, 0, hSt, 2.4);
                    s.Primary(len + platL, wid, 0, len + platL, wid, hSt, 2.4);
                    s.Sub(len, 0, hSt + railH, len + platL, 0, hSt + railH, 1.8);
                    s.Sub(len + platL, 0, hSt + railH, len + platL, wid, hSt + railH, 1.8);
                    s.Sub(len, wid, hSt + railH, len + platL, wid, hSt + railH, 1.8);
                    s.Sub(len + platL, 0, hSt, len + platL, 0, hSt + railH, 1.5);
                    s.Sub(len + platL, wid, hSt, len + platL, wid, hSt + railH, 1.5);
                    s.Dim(0, 0, 0, len + platL, 0, 0, 0, -0.34, 0, $"L={p.Span:0}");
                    s.Dim(len + platL, 0, 0, len + platL, wid, 0, 0.30, 0, 0, $"W={p.Length:0}");
                    s.Dim(len + platL, wid, 0, len + platL, wid, hSt, 0.30, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 31. 轻钢厂房成套（区域组合） ====================
                case ParametricStructureType.PortalHallAssembly:
                {
                    int bays = Math.Max(2, Math.Min(5, p.BayCount));
                    double hallW = 1.65;
                    double hallL = 2.35;
                    double eaveH = 1.15;
                    double ridgeZ = eaveH + 0.42;
                    for (int i = 0; i <= bays; i++)
                    {
                        double y = hallL * i / bays;
                        s.Primary(0, y, 0, 0, y, eaveH, 2.4);
                        s.Primary(hallW, y, 0, hallW, y, eaveH, 2.4);
                        s.Primary(0, y, eaveH, hallW * 0.5, y, ridgeZ, 2.4);
                        s.Primary(hallW, y, eaveH, hallW * 0.5, y, ridgeZ, 2.4);
                        s.Base(0, y, 0, 0.07);
                        s.Base(hallW, y, 0, 0.07);
                    }
                    s.Sub(0, 0, eaveH, 0, hallL, eaveH, 1.5);
                    s.Sub(hallW, 0, eaveH, hallW, hallL, eaveH, 1.5);
                    s.Sub(hallW * 0.5, 0, ridgeZ, hallW * 0.5, hallL, ridgeZ, 1.5);
                    s.Sub(hallW, 0, 0, hallW, hallL / bays, eaveH, 1.5);
                    s.Sub(hallW, hallL / bays, 0, hallW, 0, eaveH, 1.5);
                    s.Dim(0, 0, 0, hallW, 0, 0, 0, -0.34, 0, $"B={p.Span:0}");
                    s.Dim(hallW, 0, 0, hallW, hallL, 0, 0.30, 0, 0, $"L={p.Length:0} ({bays}跨)");
                    s.Dim(0, 0, 0, 0, 0, eaveH, -0.32, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 32. 连廊通廊成套（区域组合） ====================
                case ParametricStructureType.WalkwayBridgeAssembly:
                {
                    int bays = Math.Max(2, Math.Min(4, p.BayCount));
                    double totX = 2.55;
                    double w = 0.72;
                    double dH = 1.20;
                    double rH = 0.38;
                    for (int i = 0; i <= bays; i++)
                    {
                        double x = totX * i / bays;
                        s.Primary(x, 0, 0, x, 0, dH, 2.4);
                        s.Primary(x, w, 0, x, w, dH, 2.4);
                        s.Primary(x, 0, dH, x, w, dH, 2.2);
                        s.Base(x, 0, 0, 0.07);
                        s.Base(x, w, 0, 0.07);
                    }
                    s.Primary(0, 0, dH, totX, 0, dH, 2.6);
                    s.Primary(0, w, dH, totX, w, dH, 2.6);
                    s.Sub(0, 0, dH + rH, totX, 0, dH + rH, 1.6);
                    s.Sub(0, w, dH + rH, totX, w, dH + rH, 1.6);
                    for (int i = 0; i <= bays * 2; i++)
                    {
                        double x = totX * i / (bays * 2.0);
                        s.Sub(x, 0, dH, x, 0, dH + rH, 1.3);
                        s.Sub(x, w, dH, x, w, dH + rH, 1.3);
                    }
                    s.Sub(0, 0, 0, totX / bays, 0, dH, 1.5);
                    s.Sub(totX / bays, 0, 0, 0, 0, dH, 1.5);
                    s.Dim(0, 0, 0, totX, 0, 0, 0, -0.34, 0, $"L={p.Span:0}");
                    s.Dim(totX, 0, 0, totX, w, 0, 0.30, 0, 0, $"W={p.Length:0}");
                    s.Dim(0, 0, 0, 0, 0, dH, -0.32, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 33. 设备框架成套（区域组合） ====================
                case ParametricStructureType.EquipmentFrameAssembly:
                {
                    int floors = 3;
                    int bx = Math.Max(1, Math.Min(3, p.BayCount));
                    double fw = 1.9, fl = 1.3, fh = 1.85, rH = 0.32;
                    for (int ix = 0; ix <= bx; ix++)
                        foreach (double y in new[] { 0.0, fl })
                        {
                            double x = fw * ix / bx;
                            s.Primary(x, y, 0, x, y, fh, 2.4);
                            s.Base(x, y, 0, 0.07);
                        }
                    for (int f = 1; f <= floors; f++)
                    {
                        double z = fh * f / floors;
                        s.Primary(0, 0, z, fw, 0, z, 2.2);
                        s.Primary(0, fl, z, fw, fl, z, 2.2);
                        for (int ix = 0; ix <= bx; ix++)
                            s.Primary(fw * ix / bx, 0, z, fw * ix / bx, fl, z, 2.0);
                    }
                    s.Sub(0, 0, fh + rH, fw, 0, fh + rH, 1.5);
                    s.Sub(0, fl, fh + rH, fw, fl, fh + rH, 1.5);
                    s.Sub(0, 0, fh + rH, 0, fl, fh + rH, 1.5);
                    s.Sub(fw, 0, fh + rH, fw, fl, fh + rH, 1.5);
                    s.Sub(fw, 0, 0, fw, fl, fh / floors, 1.6);
                    s.Dim(0, 0, 0, fw, 0, 0, 0, -0.34, 0, $"B={p.Span:0}");
                    s.Dim(fw, 0, 0, fw, fl, 0, 0.30, 0, 0, $"L={p.Length:0}");
                    s.Dim(0, 0, 0, 0, 0, fh, -0.32, 0, 0, $"H={p.Height:0}");
                    break;
                }

                // ==================== 34. 钢雨棚成套（区域组合） ====================
                case ParametricStructureType.CanopyAssembly:
                {
                    int bays = Math.Max(2, Math.Min(5, p.BayCount));
                    double totX = 2.45;
                    double cw = 1.15;
                    double eH = 1.25;
                    double rise = 0.32;
                    for (int i = 0; i <= bays; i++)
                    {
                        double x = totX * i / bays;
                        s.Primary(x, 0, 0, x, 0, eH, 2.5);
                        s.Base(x, 0, 0, 0.08);
                        s.Primary(x, 0, eH, x, cw, eH + rise, 2.5);
                        s.Sub(x, 0, eH * 0.58, x, cw * 0.58, eH + rise * 0.58, 1.6);
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        double t = k / 3.0;
                        s.Sub(0, cw * t, eH + rise * t, totX, cw * t, eH + rise * t, 1.6);
                    }
                    s.Dim(0, 0, 0, totX, 0, 0, 0, -0.34, 0, $"L={p.Span:0} ({bays}跨)");
                    s.Dim(totX, 0, eH, totX, cw, eH, 0.30, 0, 0, $"W={p.Length:0}");
                    s.Dim(0, 0, 0, 0, 0, eH, -0.32, 0, 0, $"H={p.Height:0}");
                    break;
                }

                default:
                {
                    s.Box(0, 0, 0, B, L, H, SteelMemberBrush, 2.4, false, 3);
                    s.Dim(0, 0, 0, B, 0, 0, 0, -0.32, 0, $"B={p.Span:0}");
                    s.Dim(0, 0, 0, 0, 0, H, -0.32, 0, 0, $"H={p.Height:0}");
                    break;
                }
            }
        }

        private static void DrawPlatform3D(IsoScene s, double B, double L, double H, double railH, bool withStair)
        {
            foreach (double x in new[] { 0.0, B })
                foreach (double y in new[] { 0.0, L })
                {
                    s.Primary(x, y, 0, x, y, H, 2.8);
                    s.Base(x, y, 0, 0.10);
                }
            // 主梁框
            s.Primary(0, 0, H, B, 0, H, 2.8);
            s.Primary(0, L, H, B, L, H, 2.6);
            s.Primary(0, 0, H, 0, L, H, 2.6);
            s.Primary(B, 0, H, B, L, H, 2.8);
            // 次梁
            s.Sub(B / 3.0, 0, H, B / 3.0, L, H, 1.6);
            s.Sub(B * 2.0 / 3.0, 0, H, B * 2.0 / 3.0, L, H, 1.6);
            // 护栏四边（若带斜梯则前侧留出口）
            double frontStart = withStair ? B * 0.42 : 0.0;
            s.Sub(frontStart, 0, H + railH, B, 0, H + railH, 1.8);
            s.Sub(0, L, H + railH, B, L, H + railH, 1.8);
            s.Sub(0, 0, H + railH, 0, L, H + railH, 1.8);
            s.Sub(B, 0, H + railH, B, L, H + railH, 1.8);
            foreach (double x in new[] { 0.0, B * 0.5, B })
            {
                if (!withStair || x >= frontStart)
                    s.Sub(x, 0, H, x, 0, H + railH, 1.4);
                s.Sub(x, L, H, x, L, H + railH, 1.4);
            }
            s.Sub(0, L * 0.5, H, 0, L * 0.5, H + railH, 1.4);
            s.Sub(B, L * 0.5, H, B, L * 0.5, H + railH, 1.4);

            if (withStair)
            {
                // 前侧附带斜梯
                double sw = B * 0.38;
                double sRun = H * 0.85;
                s.Primary(0, -sRun, 0, 0, 0, H, 2.4);
                s.Primary(sw, -sRun, 0, sw, 0, H, 2.4);
                s.Sub(0, -sRun, railH, 0, 0, H + railH, 1.6);
                s.Sub(sw, -sRun, railH, sw, 0, H + railH, 1.6);
                for (int i = 1; i <= 5; i++)
                {
                    double t = i / 6.0;
                    s.Plate(0, -sRun * (1 - t), H * t, sw, -sRun * (1 - t), H * t, 1.5);
                }
            }
        }

        private static void DrawStairFlight3D(IsoScene s, double x0, double y0, double z0, double len, double wid, double h, double railH)
        {
            // 两侧梯梁
            s.Primary(x0, y0, z0, x0 + len, y0, z0 + h, 2.8);
            s.Primary(x0, y0 + wid, z0, x0 + len, y0 + wid, z0 + h, 2.6);
            // 两侧扶手
            s.Sub(x0, y0, z0 + railH, x0 + len, y0, z0 + h + railH, 1.9);
            s.Sub(x0, y0 + wid, z0 + railH, x0 + len, y0 + wid, z0 + h + railH, 1.6);
            // 踏步与栏杆立柱
            int steps = 7;
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                double sx = x0 + len * t;
                double sz = z0 + h * t;
                s.Plate(sx, y0, sz, sx, y0 + wid, sz, 1.8);
                if (i == 0 || i == steps || i == steps / 2)
                {
                    s.Sub(sx, y0, sz, sx, y0, sz + railH, 1.5);
                    s.Sub(sx, y0 + wid, sz, sx, y0 + wid, sz + railH, 1.3);
                }
            }
        }

        private void DrawIsoTriad(double ox, double oy)
        {
            const double len = 18.0;
            // X (+0.88, +0.28), Y (+0.56, -0.42), Z (0, -0.96)
            DrawLine(ox, oy, ox + len * 0.88, oy + len * 0.28, TriadXBrush, 1.5);
            DrawLine(ox, oy, ox + len * 0.56, oy - len * 0.42, TriadYBrush, 1.5);
            DrawLine(ox, oy, ox, oy - len * 0.96, TriadZBrush, 1.5);

            AddMiniAxisLabel(ox + len * 0.95, oy + len * 0.28 - 6, "X", TriadXBrush);
            AddMiniAxisLabel(ox + len * 0.62, oy - len * 0.48 - 6, "Y", TriadYBrush);
            AddMiniAxisLabel(ox - 4, oy - len * 0.96 - 13, "Z", TriadZBrush);
        }

        private void AddMiniAxisLabel(double x, double y, string text, Brush brush)
        {
            TextBlock tb = new TextBlock
            {
                Text = text,
                FontSize = 9.5,
                Foreground = brush,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            _canvas.Children.Add(tb);
        }

        private void AddTitleText(double x, double y, string text)
        {
            TextBlock tb = new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                Foreground = TitleBrush,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            _canvas.Children.Add(tb);
        }

        private void DrawBottomRightBadge(double rightX, double bottomY, string text)
        {
            Border badge = new Border
            {
                Background = BgBrush,
                BorderBrush = BorderLineBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 10.5,
                    Foreground = TitleBrush,
                    FontWeight = FontWeights.SemiBold
                }
            };
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(badge, Math.Max(60, rightX - badge.DesiredSize.Width));
            Canvas.SetTop(badge, Math.Max(26, bottomY - badge.DesiredSize.Height));
            _canvas.Children.Add(badge);
        }

        private void DrawTick(Point p, Point other)
        {
            double dx = other.X - p.X, dy = other.Y - p.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3) return;
            double nx = -dy / len * 3.5;
            double ny = dx / len * 3.5;
            DrawLine(p.X - nx, p.Y - ny, p.X + nx, p.Y + ny, DimLineBrush, 1.3);
        }

        private void DrawDimBadge(double midX, double midY, string text, double canvasW, double canvasH)
        {
            Border label = new Border
            {
                Background = BgBrush,
                Padding = new Thickness(3, 0, 3, 0),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = DimTextBrush,
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold
                }
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double lw = label.DesiredSize.Width;
            double lh = label.DesiredSize.Height;

            double left = Math.Max(4, Math.Min(canvasW - lw - 4, midX - lw * 0.5));
            double top = Math.Max(24, Math.Min(canvasH - lh - 4, midY - lh * 0.5));
            Canvas.SetLeft(label, left);
            Canvas.SetTop(label, top);
            _canvas.Children.Add(label);
        }

        private void DrawLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness, bool dashed = false)
        {
            if (thickness <= 0) return;
            Line ln = new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            if (dashed)
            {
                ln.StrokeDashArray = new DoubleCollection { 4, 2.5 };
            }
            _canvas.Children.Add(ln);
        }
    }
}
