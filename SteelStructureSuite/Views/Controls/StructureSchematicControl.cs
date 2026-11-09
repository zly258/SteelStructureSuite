using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Views.Controls
{
    /// <summary>
    /// 单个构件专属二维带尺寸参数示意图画布（根据当前构件类型与实时参数绘制结构简图与尺寸标注线）
    /// </summary>
    public class StructureSchematicControl : Border
    {
        private readonly Canvas _canvas;

        private static readonly Brush BgBrush = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        private static readonly Brush BorderLineBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
        private static readonly Brush SteelMemberBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189));
        private static readonly Brush SteelSubBrush = new SolidColorBrush(Color.FromRgb(16, 124, 65));
        private static readonly Brush DimLineBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6));
        private static readonly Brush DimTextBrush = new SolidColorBrush(Color.FromRgb(180, 83, 9));

        public StructureSchematicControl()
        {
            Height = 190;
            Background = BgBrush;
            BorderBrush = BorderLineBrush;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(4);
            Margin = new Thickness(0, 0, 0, 8);
            ClipToBounds = true;

            _canvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Child = _canvas;
        }

        public void RenderSchematic(SteelStructureParams p)
        {
            _canvas.Children.Clear();
            if (p == null) return;

            double w = _canvas.ActualWidth > 50 ? _canvas.ActualWidth : 410;
            double h = _canvas.ActualHeight > 50 ? _canvas.ActualHeight : 190;

            bool multiView = NeedsSecondaryView(p.StructureType);
            double x0 = multiView ? 54 : 68;
            double x1 = multiView ? w * 0.61 : w - 68;
            double yBot = h - 34, yTop = 26;
            double midX = (x0 + x1) * 0.5;
            double midY = (yBot + yTop) * 0.5;

            TextBlock mainTitle = new TextBlock
            {
                Text = multiView ? "正立面" : "主视图",
                FontSize = 10.5,
                Foreground = DimTextBrush,
                FontWeight = FontWeights.SemiBold
            };
            Canvas.SetLeft(mainTitle, 8);
            Canvas.SetTop(mainTitle, 4);
            _canvas.Children.Add(mainTitle);

            switch (p.StructureType)
            {
                case ParametricStructureType.PortalFrame:
                    DrawLine(x0, yBot, x0, yTop, SteelMemberBrush, 3.0);
                    DrawLine(x1, yBot, x1, yTop, SteelMemberBrush, 3.0);
                    DrawLine(x0 - 8, yTop, x1 + 8, yTop, SteelMemberBrush, 3.2);
                    DrawBaseSupport(x0, yBot);
                    DrawBaseSupport(x1, yBot);
                    DrawHorizDim(x0, x1, yBot + 18, $"跨度 B = {p.Span:0}");
                    DrawVertDim(x0 - 24, yBot, yTop, $"H = {p.Height:0}");
                    break;

                case ParametricStructureType.MultiSpanPortalFrame:
                {
                    int bays = Math.Max(2, Math.Min(5, p.BayCount));
                    for (int i = 0; i <= bays; i++)
                    {
                        double xi = x0 + (x1 - x0) * i / bays;
                        DrawLine(xi, yBot, xi, yTop, SteelMemberBrush, 2.8);
                        DrawBaseSupport(xi, yBot);
                    }
                    DrawLine(x0 - 8, yTop, x1 + 8, yTop, SteelMemberBrush, 3.2);
                    DrawHorizDim(x0, x1, yBot + 18, $"总跨 B = {p.Span:0} ({bays}跨)");
                    DrawVertDim(x0 - 24, yBot, yTop, $"H = {p.Height:0}");
                    break;
                }

                case ParametricStructureType.BracedFrame:
                    DrawLine(x0, yBot, x0, yTop, SteelMemberBrush, 3.0);
                    DrawLine(x1, yBot, x1, yTop, SteelMemberBrush, 3.0);
                    DrawLine(x0, yTop, x1, yTop, SteelMemberBrush, 3.0);
                    DrawLine(x0, midY, x1, midY, SteelMemberBrush, 2.4);
                    DrawLine(x0, midY, midX, yTop, SteelSubBrush, 1.8);
                    DrawLine(x1, midY, midX, yTop, SteelSubBrush, 1.8);
                    DrawHorizDim(x0, x1, yBot + 18, $"跨度 B = {p.Span:0}");
                    DrawVertDim(x0 - 24, yBot, yTop, $"H = {p.Height:0}");
                    DrawVertDim(x1 + 22, yBot, midY, $"h1 = {p.SubHeight:0}");
                    break;

                case ParametricStructureType.DoubleTierFrame:
                    DrawLine(x0, yBot, x0, yTop, SteelMemberBrush, 3.0);
                    DrawLine(x1, yBot, x1, yTop, SteelMemberBrush, 3.0);
                    DrawLine(x0 - 8, yTop, x1 + 8, yTop, SteelMemberBrush, 3.0);
                    DrawLine(x0 - 8, midY, x1 + 8, midY, SteelMemberBrush, 2.6);
                    DrawLine(x0, midY + 16, x0 + 22, midY, SteelSubBrush, 1.8);
                    DrawLine(x1, midY + 16, x1 - 22, midY, SteelSubBrush, 1.8);
                    DrawHorizDim(x0, x1, yBot + 18, $"跨度 B = {p.Span:0}");
                    DrawVertDim(x0 - 24, yBot, yTop, $"H = {p.Height:0}");
                    DrawVertDim(x1 + 22, yBot, midY, $"h1 = {p.SubHeight:0}");
                    break;

                case ParametricStructureType.MultiBaySpaceFrame:
                {
                    double offX = 32, offY = -18;
                    DrawRect(x0, midY, x1 - x0, yBot - midY, SteelMemberBrush, 2.2);
                    DrawRect(x0 + offX, yTop, x1 - x0, midY - yTop, SteelSubBrush, 1.8);
                    DrawLine(x0, midY, x0 + offX, yTop, SteelMemberBrush, 2.0);
                    DrawLine(x1, midY, x1 + offX, yTop, SteelMemberBrush, 2.0);
                    DrawLine(x0, yBot, x0 + offX, yBot + offY, SteelMemberBrush, 2.0);
                    DrawLine(x1, yBot, x1 + offX, yBot + offY, SteelMemberBrush, 2.0);
                    DrawHorizDim(x0, x1, yBot + 18, $"B={p.Span:0} × L={p.Length:0}");
                    DrawVertDim(x0 - 24, yBot, yTop, $"H = {p.Height:0}");
                    break;
                }

                case ParametricStructureType.CantileverBracket:
                    DrawLine(x0 + 25, yBot, x0 + 25, yTop, SteelMemberBrush, 3.5);
                    DrawLine(x0 + 25, yTop + 10, x1 - 25, yTop + 10, SteelMemberBrush, 3.0);
                    DrawLine(x0 + 25, midY + 18, x1 - 40, yTop + 10, SteelSubBrush, 2.2);
                    DrawHorizDim(x0 + 25, x1 - 25, yBot + 18, $"挑出 L = {p.Span:0}");
                    DrawVertDim(x0 - 5, yBot, yTop, $"H = {p.Height:0}");
                    DrawVertDim(x1 - 10, midY + 18, yTop + 10, $"h1 = {p.SubHeight:0}");
                    break;

                case ParametricStructureType.SpacePlatform:
                    DrawLine(x0, yBot, x0, midY, SteelMemberBrush, 3.0);
                    DrawLine(x1, yBot, x1, midY, SteelMemberBrush, 3.0);
                    DrawLine(x0, midY, x1, midY, SteelMemberBrush, 3.2);
                    DrawRect(x0, yTop, x1 - x0, midY - yTop, SteelSubBrush, 1.6);
                    DrawLine(x0, (yTop + midY) * 0.5, x1, (yTop + midY) * 0.5, SteelSubBrush, 1.2);
                    DrawHorizDim(x0, x1, yBot + 18, $"B={p.Span:0} × L={p.Length:0}");
                    DrawVertDim(x0 - 24, yBot, midY, $"H = {p.Height:0}");
                    DrawVertDim(x1 + 22, midY, yTop, $"栏杆 {p.SubHeight:0}");
                    break;

                case ParametricStructureType.CantileverWalkway:
                    DrawLine(x0, midY, x1, midY, SteelMemberBrush, 3.0);
                    for (int i = 0; i < 4; i++)
                    {
                        double xi = x0 + (x1 - x0) * i / 3.0;
                        DrawLine(xi, midY, xi, yTop, SteelSubBrush, 1.8);
                        DrawLine(xi, midY, xi + 14, yBot - 8, SteelMemberBrush, 1.8);
                    }
                    DrawLine(x0, yTop, x1, yTop, SteelSubBrush, 2.0);
                    DrawLine(x0, (yTop + midY) * 0.5, x1, (yTop + midY) * 0.5, SteelSubBrush, 1.4);
                    DrawHorizDim(x0, x1, yBot + 18, $"走道长 L = {p.Span:0} (宽 {p.Length:0})");
                    DrawVertDim(x0 - 24, yBot, midY, $"标高 {p.Height:0}");
                    break;

                case ParametricStructureType.RoofTruss:
                    DrawLine(x0, yBot, x0, midY + 10, SteelMemberBrush, 3.0);
                    DrawLine(x1, yBot, x1, midY + 10, SteelMemberBrush, 3.0);
                    DrawLine(x0, midY + 10, x1, midY + 10, SteelMemberBrush, 2.5);
                    DrawLine(x0, midY + 10, midX, yTop, SteelMemberBrush, 2.5);
                    DrawLine(x1, midY + 10, midX, yTop, SteelMemberBrush, 2.5);
                    DrawLine(midX, midY + 10, midX, yTop, SteelSubBrush, 1.6);
                    DrawLine((x0 + midX) * 0.5, midY + 10, (x0 + midX) * 0.5, (midY + 10 + yTop) * 0.5, SteelSubBrush, 1.6);
                    DrawLine((x1 + midX) * 0.5, midY + 10, (x1 + midX) * 0.5, (midY + 10 + yTop) * 0.5, SteelSubBrush, 1.6);
                    DrawHorizDim(x0, x1, yBot + 18, $"跨度 B = {p.Span:0}");
                    DrawVertDim(x0 - 24, yBot, midY + 10, $"H = {p.Height:0}");
                    DrawVertDim(x1 + 22, midY + 10, yTop, $"矢高 {p.SubHeight:0}");
                    break;

                case ParametricStructureType.PipeBridgeTruss:
                    DrawRect(x0, yTop + 12, x1 - x0, yBot - yTop - 22, SteelMemberBrush, 2.6);
                    for (int i = 0; i < 4; i++)
                    {
                        double xa = x0 + (x1 - x0) * i / 4.0;
                        double xb = x0 + (x1 - x0) * (i + 1) / 4.0;
                        DrawLine(xa, yTop + 12, xa, yBot - 10, SteelSubBrush, 1.6);
                        DrawLine(xa, yBot - 10, xb, yTop + 12, SteelSubBrush, 1.5);
                    }
                    DrawHorizDim(x0, x1, yBot + 18, $"廊桥跨度 L = {p.Span:0} (宽 {p.Length:0})");
                    DrawVertDim(x0 - 24, yBot - 10, yTop + 12, $"桁高 {p.Height:0}");
                    break;

                case ParametricStructureType.SpiralStair:
                    DrawLine(midX - 18, yBot, midX - 18, yTop, SteelMemberBrush, 2.2);
                    DrawLine(midX + 18, yBot, midX + 18, yTop, SteelMemberBrush, 2.2);
                    for (int i = 0; i < 6; i++)
                    {
                        double yi = yBot - (yBot - yTop) * i / 6.0;
                        double xi = x0 + (x1 - x0) * i / 6.0;
                        DrawLine(xi, yi, xi + 34, yi - 14, SteelSubBrush, 2.2);
                        DrawLine(xi, yi, xi, yi - 24, SteelMemberBrush, 1.5);
                    }
                    DrawHorizDim(x0, x1, yBot + 18, $"外径 D = {p.Span:0} (踏宽 {p.Length:0})");
                    DrawVertDim(x0 - 24, yBot, yTop, $"H = {p.Height:0}");
                    break;

                case ParametricStructureType.StraightStair:
                    DrawLine(x0, yBot, x1, midY - 6, SteelMemberBrush, 3.0);
                    DrawLine(x0, midY + 4, x1, yTop, SteelSubBrush, 2.0);
                    DrawLine(x0, yBot, x0, midY + 4, SteelSubBrush, 1.8);
                    DrawLine(x1, midY - 6, x1, yTop, SteelSubBrush, 1.8);
                    for (int i = 1; i <= 5; i++)
                    {
                        double t = i / 6.0;
                        double sx = x0 + (x1 - x0) * t;
                        double sy = yBot + (midY - 6 - yBot) * t;
                        DrawLine(sx - 10, sy, sx + 8, sy, SteelMemberBrush, 2.0);
                    }
                    DrawHorizDim(x0, x1, yBot + 18, $"水平长 L = {p.Span:0} (梯宽 {p.Length:0})");
                    DrawVertDim(x1 + 22, yBot, midY - 6, $"层高 H = {p.Height:0}");
                    break;

                case ParametricStructureType.SafetyCagedLadder:
                    DrawLine(midX - 24, yBot, midX - 24, yTop, SteelMemberBrush, 2.8);
                    DrawLine(midX + 24, yBot, midX + 24, yTop, SteelMemberBrush, 2.8);
                    for (int i = 1; i <= 6; i++)
                    {
                        double ry = yBot - (yBot - yTop) * i / 6.5;
                        DrawLine(midX - 24, ry, midX + 24, ry, SteelSubBrush, 1.6);
                    }
                    DrawRect(midX - 42, yTop + 6, 84, (midY - yTop) + 8, SteelSubBrush, 1.5);
                    DrawHorizDim(midX - 24, midX + 24, yBot + 18, $"梯宽 W = {p.Span:0}");
                    DrawVertDim(midX - 65, yBot, yTop, $"H = {p.Height:0}");
                    break;

                case ParametricStructureType.HandrailGuardrail:
                {
                    DrawLine(x0, yTop + 8, x1, yTop + 8, SteelMemberBrush, 2.8);
                    DrawLine(x0, midY + 4, x1, midY + 4, SteelSubBrush, 2.0);
                    DrawLine(x0, yBot - 6, x1, yBot - 6, SteelMemberBrush, 2.4);
                    for (int i = 0; i < 4; i++)
                    {
                        double px = x0 + (x1 - x0) * i / 3.0;
                        DrawLine(px, yBot, px, yTop + 8, SteelMemberBrush, 2.4);
                        DrawBaseSupport(px, yBot);
                    }
                    DrawHorizDim(x0, x1, yBot + 18, $"栏杆长 L = {p.Span:0} (间距≤{p.SubHeight:0})");
                    DrawVertDim(x0 - 24, yBot, yTop + 8, $"H = {p.Height:0}");
                    break;
                }

                case ParametricStructureType.LinearSteelMember:
                    DrawLine(x0, midY + 12, x1, midY - 12, SteelMemberBrush, 4.5);
                    DrawLine(x0, midY + 4, x1, midY - 20, SteelSubBrush, 1.5);
                    DrawLine(x0, midY + 20, x1, midY - 4, SteelSubBrush, 1.5);
                    DrawHorizDim(x0, x1, yBot + 18, $"杆件长 L = {p.Span:0} ({p.ColumnSection})");
                    break;

                case ParametricStructureType.ArcSteelMember:
                {
                    double sweep = Math.Min(300.0, Math.Max(20.0, p.SubHeight)) * Math.PI / 180.0;
                    int n = 16;
                    double cx = (x0 + x1) * 0.5;
                    double cy = (yBot + yTop) * 0.55;
                    double rx = (x1 - x0) * 0.42;
                    double ry = (yTop - yBot) * 0.38;
                    double px0 = cx + rx;
                    double py0 = cy;
                    for (int i = 1; i <= n; i++)
                    {
                        double a = sweep * i / n;
                        double px = cx + rx * Math.Cos(a);
                        double py = cy - ry * Math.Sin(a);
                        DrawLine(px0, py0, px, py, SteelMemberBrush, 4.2);
                        px0 = px;
                        py0 = py;
                    }
                    DrawHorizDim(x0, x1, yBot + 16, $"半径 R = {p.Span:0}  扫掠 {p.SubHeight:0}°");
                    break;
                }

                case ParametricStructureType.SpiralSteelMember:
                {
                    int n = 28;
                    double px0 = x0;
                    double py0 = midY;
                    for (int i = 1; i <= n; i++)
                    {
                        double t = (double)i / n;
                        double a = t * Math.Max(1.0, p.SubHeight) * Math.PI * 2.0;
                        double px = x0 + (x1 - x0) * t;
                        double py = midY + Math.Sin(a) * (yTop - yBot) * 0.28;
                        DrawLine(px0, py0, px, py, SteelMemberBrush, 3.2);
                        px0 = px;
                        py0 = py;
                    }
                    DrawHorizDim(x0, x1, yBot + 16, $"轴线 L = {p.Span:0}  R = {p.Length:0}  {p.SubHeight:0.#}圈");
                    break;
                }

                case ParametricStructureType.VesselSupport:
                    DrawLine(x0 + 20, yBot, x1 - 20, yBot, SteelMemberBrush, 3.2);
                    DrawLine(x0 + 35, yBot, x0 + 50, midY, SteelMemberBrush, 2.4);
                    DrawLine(x1 - 35, yBot, x1 - 50, midY, SteelMemberBrush, 2.4);
                    DrawLine(midX, yBot, midX, midY + 10, SteelSubBrush, 2.0);
                    DrawLine(x0 + 50, midY, midX, midY + 16, SteelMemberBrush, 2.8);
                    DrawLine(x1 - 50, midY, midX, midY + 16, SteelMemberBrush, 2.8);
                    DrawHorizDim(x0 + 20, x1 - 20, yBot + 18, $"容器外径 DN = {p.Span:0}");
                    DrawVertDim(x0 - 10, yBot, midY, $"H = {p.Height:0}");
                    break;

                case ParametricStructureType.TowerCircularPlatform:
                {
                    double cx = midX, cy = midY + 6;
                    double rIn = 26, rOut = 54;
                    DrawEllipse(cx - rIn, cy - rIn, rIn * 2, rIn * 2, SteelMemberBrush, 2.2);
                    DrawEllipse(cx - rOut, cy - rOut, rOut * 2, rOut * 2, SteelSubBrush, 1.8);
                    for (int i = 0; i < 6; i++)
                    {
                        double a = i * Math.PI / 3.0;
                        DrawLine(cx + rIn * Math.Cos(a), cy + rIn * Math.Sin(a),
                                 cx + rOut * Math.Cos(a), cy + rOut * Math.Sin(a), SteelMemberBrush, 2.0);
                    }
                    DrawHorizDim(x0 + 10, x1 - 10, yBot + 18, $"塔径 DN={p.Span:0} 环宽 W={p.Length:0} ({p.SubHeight:0}°)");
                    break;
                }

                case ParametricStructureType.VerticalVesselSkirt:
                    DrawRect(midX - 42, yTop + 6, 84, yBot - yTop - 6, SteelMemberBrush, 2.8);
                    DrawLine(midX - 58, yBot, midX + 58, yBot, SteelMemberBrush, 3.6);
                    DrawEllipse(midX - 14, midY - 10, 28, 28, SteelSubBrush, 1.8);
                    DrawHorizDim(midX - 58, midX + 58, yBot + 18, $"裙座 DN={p.Span:0} (底环宽 {p.Length:0})");
                    DrawVertDim(midX - 75, yBot, yTop + 6, $"H = {p.Height:0}");
                    break;

                case ParametricStructureType.PipeShoeSupport:
                    DrawLine(x0 + 25, yBot, x1 - 25, yBot, SteelMemberBrush, 3.2);
                    DrawLine(midX, yBot, midX, midY + 8, SteelMemberBrush, 3.0);
                    DrawLine(midX - 28, yBot, midX - 18, midY + 12, SteelSubBrush, 1.8);
                    DrawLine(midX + 28, yBot, midX + 18, midY + 12, SteelSubBrush, 1.8);
                    DrawEllipse(midX - 24, yTop + 10, 48, 48, SteelSubBrush, 2.2);
                    DrawHorizDim(x0 + 25, x1 - 25, yBot + 18, $"管径 DN={p.Span:0} 托长 L={p.Length:0}");
                    DrawVertDim(x0, yBot, midY, $"H = {p.Height:0}");
                    break;

                case ParametricStructureType.TankRoofHandrail:
                {
                    double cx = midX, cy = midY + 4;
                    double r = 46;
                    DrawEllipse(cx - r, cy - r, r * 2, r * 2, SteelMemberBrush, 2.4);
                    DrawEllipse(cx - r * 0.85, cy - r * 0.85, r * 1.7, r * 1.7, SteelSubBrush, 1.4);
                    DrawHorizDim(x0 + 15, x1 - 15, yBot + 18, $"罐顶直径 D={p.Span:0} 栏高 H={p.Height:0} ({p.SubHeight:0}°)");
                    break;
                }

                case ParametricStructureType.SubstationGantryFrame:
                    DrawLine(x0, yBot, x0 + 18, yTop + 18, SteelMemberBrush, 2.8);
                    DrawLine(x0 + 36, yBot, x0 + 18, yTop + 18, SteelMemberBrush, 2.8);
                    DrawLine(x1 - 36, yBot, x1 - 18, yTop + 18, SteelMemberBrush, 2.8);
                    DrawLine(x1, yBot, x1 - 18, yTop + 18, SteelMemberBrush, 2.8);
                    DrawRect(x0 + 18, yTop + 18, x1 - x0 - 36, 16, SteelSubBrush, 2.0);
                    DrawLine(x0 + 18, yTop + 18, x0 + 18, yTop - 4, SteelSubBrush, 1.8);
                    DrawLine(x1 - 18, yTop + 18, x1 - 18, yTop - 4, SteelSubBrush, 1.8);
                    DrawHorizDim(x0, x1, yBot + 18, $"构架跨度 B = {p.Span:0}");
                    DrawVertDim(x0 - 20, yBot, yTop + 18, $"H = {p.Height:0}");
                    break;

                case ParametricStructureType.CableTrenchSupport:
                {
                    int cols = 3;
                    int tiers = Math.Max(2, Math.Min(5, p.BayCount));
                    for (int c = 0; c < cols; c++)
                    {
                        double cx = x0 + (x1 - x0) * c / (cols - 1.0);
                        DrawLine(cx, yBot, cx, yTop + 8, SteelMemberBrush, 2.8);
                        for (int t = 1; t <= tiers; t++)
                        {
                            double ty = yTop + 8 + (yBot - yTop - 20) * t / (tiers + 0.5);
                            DrawLine(cx, ty, cx + 28, ty, SteelSubBrush, 2.0);
                        }
                    }
                    DrawLine(x0, yBot - 6, x1, yBot - 6, SteelMemberBrush, 2.0);
                    DrawHorizDim(x0, x1, yBot + 18, $"总长 L={p.Span:0} 挑臂 W={p.Length:0} ({tiers}层)");
                    DrawVertDim(x0 - 22, yBot, yTop + 8, $"H = {p.Height:0}");
                    break;
                }

                case ParametricStructureType.CraneRunwayGirder:
                    DrawRect(x0, midY - 18, x1 - x0, 36, SteelMemberBrush, 2.8);
                    DrawLine(x0 - 6, midY - 18, x1 + 6, midY - 18, SteelMemberBrush, 3.6);
                    DrawLine(x0 - 6, midY + 18, x1 + 6, midY + 18, SteelMemberBrush, 3.6);
                    for (int i = 0; i <= 5; i++)
                    {
                        double rx = x0 + (x1 - x0) * i / 5.0;
                        DrawLine(rx, midY - 18, rx, midY + 18, SteelSubBrush, 1.6);
                    }
                    DrawHorizDim(x0, x1, yBot + 18, $"吊车梁跨度 L = {p.Span:0} (制动宽 {p.Length:0})");
                    DrawVertDim(x0 - 22, midY + 18, midY - 18, $"梁高 {p.Height:0}");
                    break;

                case ParametricStructureType.CrossBracingFrame:
                    DrawLine(x0, yBot, x0, yTop, SteelMemberBrush, 3.2);
                    DrawLine(x1, yBot, x1, yTop, SteelMemberBrush, 3.2);
                    DrawLine(x0, yTop, x1, yTop, SteelMemberBrush, 2.4);
                    DrawLine(x0, midY - 6, x1, midY - 6, SteelMemberBrush, 2.4);
                    DrawLine(x0, yBot, x1, midY - 6, SteelSubBrush, 1.8);
                    DrawLine(x1, yBot, x0, midY - 6, SteelSubBrush, 1.8);
                    DrawLine(x0, midY - 6, x1, yTop, SteelSubBrush, 1.6);
                    DrawLine(x1, midY - 6, x0, yTop, SteelSubBrush, 1.6);
                    DrawHorizDim(x0, x1, yBot + 18, $"柱距 L = {p.Span:0}");
                    DrawVertDim(x0 - 22, yBot, yTop, $"H = {p.Height:0}");
                    break;
            }

            if (multiView)
            {
                DrawSecondaryView(p, w * 0.665 + 4, 20, w - 10, h - 12);
            }
        }

        private static bool NeedsSecondaryView(ParametricStructureType type)
        {
            return type == ParametricStructureType.SpacePlatform ||
                   type == ParametricStructureType.MultiBaySpaceFrame ||
                   type == ParametricStructureType.CantileverWalkway ||
                   type == ParametricStructureType.PipeBridgeTruss ||
                   type == ParametricStructureType.SpiralStair ||
                   type == ParametricStructureType.VesselSupport ||
                   type == ParametricStructureType.SafetyCagedLadder ||
                   type == ParametricStructureType.MultiSpanPortalFrame ||
                   type == ParametricStructureType.DoubleTierFrame ||
                   type == ParametricStructureType.StraightStair;
        }

        /// <summary>
        /// 第二视图：平面/侧视/横断面，补充主视图无法表达的三维关系。
        /// </summary>
        private void DrawSecondaryView(SteelStructureParams p, double sx0, double sy0, double sx1, double sy1)
        {
            if (sx1 - sx0 < 70 || sy1 - sy0 < 70) return;

            DrawRect(sx0, sy0, sx1 - sx0, sy1 - sy0, BorderLineBrush, 1.0);

            string title;
            string sizeText;

            switch (p.StructureType)
            {
                case ParametricStructureType.SpiralStair:
                {
                    title = "平面";
                    sizeText = $"D={p.Span:0} W={p.Length:0}";
                    double cx = (sx0 + sx1) * 0.5;
                    double cy = (sy0 + sy1) * 0.5;
                    double r = Math.Min(sx1 - sx0, sy1 - sy0) * 0.34;
                    DrawEllipse(cx - r, cy - r, r * 2, r * 2, SteelMemberBrush, 2.0);
                    DrawEllipse(cx - r * 0.55, cy - r * 0.55, r * 1.1, r * 1.1, SteelSubBrush, 1.4);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4.0;
                        DrawLine(cx + r * 0.55 * Math.Cos(a), cy + r * 0.55 * Math.Sin(a),
                                 cx + r * Math.Cos(a), cy + r * Math.Sin(a), SteelSubBrush, 1.0);
                    }
                    break;
                }

                case ParametricStructureType.VesselSupport:
                {
                    title = "侧视";
                    sizeText = $"DN={p.Span:0} B={p.Length:0}";
                    double baseY = sy1 - 24;
                    double left = sx0 + 14, right = sx1 - 14;
                    DrawLine(left, baseY, right, baseY, SteelMemberBrush, 2.4);
                    DrawLine((sx0 + sx1) * 0.5, baseY, (sx0 + sx1) * 0.5, sy0 + 30, SteelSubBrush, 1.6);

                    // 容器圆弧
                    double cx = (sx0 + sx1) * 0.5;
                    double r = Math.Min((sx1 - sx0) * 0.38, (sy1 - sy0) * 0.30);
                    double cy = sy0 + 44;
                    for (int i = 0; i <= 6; i++)
                    {
                        double a0 = Math.PI + Math.PI * i / 6.0;
                        double a1 = Math.PI + Math.PI * (i + 1) / 6.0;
                        DrawLine(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0),
                                 cx + r * Math.Cos(a1), cy + r * Math.Sin(a1), SteelMemberBrush, 2.0);
                    }
                    break;
                }

                case ParametricStructureType.CantileverWalkway:
                {
                    title = "侧视";
                    sizeText = $"W={p.Length:0} H={p.Height:0}";
                    double wallX = sx0 + 16;
                    double deckY = sy0 + 38;
                    double deckX = sx1 - 14;
                    double kneeY = deckY + Math.Min((sy1 - sy0) * 0.34, 46);
                    DrawLine(wallX, sy0 + 18, wallX, sy1 - 20, SteelMemberBrush, 2.6);
                    DrawLine(wallX, deckY, deckX, deckY, SteelMemberBrush, 2.4);
                    DrawLine(wallX, kneeY, deckX * 0.86 + wallX * 0.14, deckY, SteelSubBrush, 1.8);
                    DrawLine(deckX, deckY, deckX, deckY - 26, SteelSubBrush, 1.8);
                    DrawLine(wallX, deckY - 26, deckX, deckY - 26, SteelSubBrush, 1.6);
                    break;
                }

                case ParametricStructureType.PipeBridgeTruss:
                {
                    title = "横断面";
                    sizeText = $"W={p.Length:0} H={p.Height:0}";
                    double left = sx0 + 16, right = sx1 - 16;
                    double top = sy0 + 30, bottom = sy1 - 24;
                    DrawRect(left, top, right - left, bottom - top, SteelMemberBrush, 2.2);
                    DrawLine(left, top, right, bottom, SteelSubBrush, 1.2);
                    DrawLine(left, bottom, right, top, SteelSubBrush, 1.2);
                    break;
                }

                case ParametricStructureType.SafetyCagedLadder:
                {
                    title = "平面";
                    sizeText = $"W={p.Span:0} D={p.Length:0}";
                    double cx = (sx0 + sx1) * 0.5;
                    double cy = (sy0 + sy1) * 0.5 - 4;
                    double r = Math.Min(sx1 - sx0, sy1 - sy0) * 0.32;
                    DrawEllipse(cx - r, cy - r, r * 2, r * 2, SteelSubBrush, 1.6);
                    double hw = Math.Min(18, r * 0.55);
                    DrawLine(cx - hw, cy - r, cx - hw, cy + r, SteelMemberBrush, 2.2);
                    DrawLine(cx + hw, cy - r, cx + hw, cy + r, SteelMemberBrush, 2.2);
                    for (int i = 1; i <= 4; i++)
                    {
                        double y = cy - r + (2 * r) * i / 5.0;
                        DrawLine(cx - hw, y, cx + hw, y, SteelSubBrush, 1.1);
                    }
                    break;
                }

                case ParametricStructureType.StraightStair:
                {
                    title = "平面";
                    sizeText = $"L={p.Span:0} W={p.Length:0}";
                    double left = sx0 + 16, right = sx1 - 16;
                    double top = sy0 + 32, bottom = sy1 - 24;
                    DrawRect(left, top, right - left, bottom - top, SteelMemberBrush, 2.0);
                    for (int i = 1; i <= 5; i++)
                    {
                        double x = left + (right - left) * i / 6.0;
                        DrawLine(x, top, x, bottom, SteelSubBrush, 1.0);
                    }
                    break;
                }

                case ParametricStructureType.MultiSpanPortalFrame:
                {
                    title = "平面";
                    sizeText = $"B={p.Span:0}  {Math.Max(2, p.BayCount)}跨";
                    double left = sx0 + 16, right = sx1 - 16;
                    double top = sy0 + 32, bottom = sy1 - 24;
                    DrawRect(left, top, right - left, bottom - top, SteelMemberBrush, 2.0);
                    int bays = Math.Max(2, Math.Min(6, p.BayCount));
                    for (int i = 1; i < bays; i++)
                    {
                        double x = left + (right - left) * i / bays;
                        DrawLine(x, top, x, bottom, SteelSubBrush, 1.2);
                    }
                    for (int i = 0; i <= bays; i++)
                    {
                        double x = left + (right - left) * i / bays;
                        DrawEllipse(x - 2.2, (top + bottom) * 0.5 - 2.2, 4.4, 4.4, SteelMemberBrush, 1.5);
                    }
                    break;
                }

                case ParametricStructureType.DoubleTierFrame:
                {
                    title = "平面";
                    sizeText = $"B={p.Span:0} h1={p.SubHeight:0}";
                    double left = sx0 + 16, right = sx1 - 16;
                    double top = sy0 + 32, bottom = sy1 - 24;
                    DrawRect(left, top, right - left, bottom - top, SteelMemberBrush, 1.6);
                    DrawLine(left, top + (bottom - top) * 0.32, right, top + (bottom - top) * 0.32, SteelMemberBrush, 2.2);
                    DrawLine(left, top + (bottom - top) * 0.68, right, top + (bottom - top) * 0.68, SteelSubBrush, 1.6);
                    break;
                }

                default:
                {
                    // 框架/平台类：平面柱网与梁格
                    title = "平面";
                    sizeText = $"B={p.Span:0} L={p.Length:0}";
                    double left = sx0 + 16, right = sx1 - 16;
                    double top = sy0 + 32, bottom = sy1 - 24;
                    DrawRect(left, top, right - left, bottom - top, SteelMemberBrush, 2.0);
                    DrawLine(left, (top + bottom) * 0.5, right, (top + bottom) * 0.5, SteelSubBrush, 1.2);
                    DrawLine((left + right) * 0.5, top, (left + right) * 0.5, bottom, SteelSubBrush, 1.2);
                    foreach (double fx in new[] { 0.0, 1.0 })
                    {
                        foreach (double fy in new[] { 0.0, 1.0 })
                        {
                            double px = left + (right - left) * fx;
                            double py = top + (bottom - top) * fy;
                            DrawEllipse(px - 2.2, py - 2.2, 4.4, 4.4, SteelMemberBrush, 1.6);
                        }
                    }
                    break;
                }
            }

            TextBlock titleTb = new TextBlock
            {
                Text = title,
                FontSize = 10.5,
                Foreground = DimTextBrush,
                FontWeight = FontWeights.SemiBold
            };
            Canvas.SetLeft(titleTb, sx0 + 6);
            Canvas.SetTop(titleTb, sy0 + 4);
            _canvas.Children.Add(titleTb);

            TextBlock sizeTb = new TextBlock
            {
                Text = sizeText,
                FontSize = 9.5,
                Foreground = DimTextBrush
            };
            Canvas.SetLeft(sizeTb, sx0 + 6);
            Canvas.SetTop(sizeTb, sy1 - 17);
            _canvas.Children.Add(sizeTb);
        }

        private void DrawEllipse(double x, double y, double w, double h, Brush stroke, double thickness)
        {
            Ellipse e = new Ellipse
            {
                Width = Math.Max(2, w),
                Height = Math.Max(2, h),
                Stroke = stroke,
                StrokeThickness = thickness
            };
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            _canvas.Children.Add(e);
        }

        private void DrawLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness)
        {
            _canvas.Children.Add(new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = stroke,
                StrokeThickness = thickness
            });
        }

        private void DrawRect(double x, double y, double w, double h, Brush stroke, double thickness)
        {
            Rectangle r = new Rectangle
            {
                Width = Math.Max(2, w),
                Height = Math.Max(2, h),
                Stroke = stroke,
                StrokeThickness = thickness
            };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            _canvas.Children.Add(r);
        }

        private void DrawBaseSupport(double x, double y)
        {
            DrawLine(x - 10, y, x + 10, y, SteelMemberBrush, 3.0);
        }

        private void DrawHorizDim(double x1, double x2, double y, string text)
        {
            DrawLine(x1, y, x2, y, DimLineBrush, 1.2);
            DrawLine(x1, y - 4, x1, y + 4, DimLineBrush, 1.2);
            DrawLine(x2, y - 4, x2, y + 4, DimLineBrush, 1.2);

            TextBlock tb = new TextBlock
            {
                Text = text,
                Foreground = DimTextBrush,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Background = BgBrush,
                Padding = new Thickness(4, 0, 4, 0)
            };
            Canvas.SetLeft(tb, (x1 + x2) * 0.5 - text.Length * 3.4);
            Canvas.SetTop(tb, y - 8);
            _canvas.Children.Add(tb);
        }

        private void DrawVertDim(double x, double yBot, double yTop, string text)
        {
            DrawLine(x, yBot, x, yTop, DimLineBrush, 1.2);
            DrawLine(x - 4, yBot, x + 4, yBot, DimLineBrush, 1.2);
            DrawLine(x - 4, yTop, x + 4, yTop, DimLineBrush, 1.2);

            TextBlock tb = new TextBlock
            {
                Text = text,
                Foreground = DimTextBrush,
                FontSize = 11.0,
                FontWeight = FontWeights.SemiBold,
                Background = BgBrush,
                Padding = new Thickness(2, 0, 2, 0)
            };
            Canvas.SetLeft(tb, Math.Max(2, x - 26));
            Canvas.SetTop(tb, (yBot + yTop) * 0.5 - 8);
            _canvas.Children.Add(tb);
        }
    }
}
