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
    /// 26 种参数化钢结构构件专属双视图（左：主视/正立面图；右：平面/侧视/横断面图）实时带尺寸参数示意图画布
    /// - 每种构件均按真实工程结构形态与当前输入参数实时绘制主次构件、支座、节点与尺寸标注
    /// - 所有尺寸文字均采用自适应居中标签框与边界防裁切保护，杜绝任何参数值遮挡或显示不全
    /// </summary>
    public class StructureSchematicControl : Border
    {
        private readonly Canvas _canvas;

        private static readonly Brush BgBrush = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        private static readonly Brush SubBoxBgBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        private static readonly Brush BorderLineBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
        private static readonly Brush AxisLineBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        private static readonly Brush SteelMemberBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189));
        private static readonly Brush SteelSubBrush = new SolidColorBrush(Color.FromRgb(16, 124, 65));
        private static readonly Brush PlateBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105));
        private static readonly Brush DimLineBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6));
        private static readonly Brush DimTextBrush = new SolidColorBrush(Color.FromRgb(180, 83, 9));
        private static readonly Brush TitleBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59));

        public StructureSchematicControl()
        {
            Height = 228;
            MinHeight = 215;
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

            double w = _canvas.ActualWidth > 80 ? _canvas.ActualWidth : 520;
            double h = _canvas.ActualHeight > 80 ? _canvas.ActualHeight : 228;

            // 左区：主视图 / 正立面图 (占 58% 宽度)；右区：平面 / 侧视 / 横断面图 (占 42% 宽度)
            double splitX = Math.Round(w * 0.58);
            double lx0 = 58;
            double lx1 = splitX - 38;
            double lyTop = 30;
            double lyBot = h - 36;
            double lMidX = (lx0 + lx1) * 0.5;
            double lMidY = (lyTop + lyBot) * 0.5;

            // 右侧辅视图背景框
            double rxBox0 = splitX + 6;
            double ryBox0 = 8;
            double rxBox1 = w - 8;
            double ryBox1 = h - 8;
            DrawFilledRect(rxBox0, ryBox0, rxBox1 - rxBox0, ryBox1 - ryBox0, SubBoxBgBrush, BorderLineBrush, 1.0);

            double rx0 = rxBox0 + 24;
            double rx1 = rxBox1 - 20;
            double ryTop = ryBox0 + 30;
            double ryBot = ryBox1 - 30;
            double rMidX = (rx0 + rx1) * 0.5;
            double rMidY = (ryTop + ryBot) * 0.5;

            string leftTitle = "正立面示意图";
            string rightTitle = "平面 / 断面示意图";

            switch (p.StructureType)
            {
                // ==================== 1. 单跨门式刚架 ====================
                case ParametricStructureType.PortalFrame:
                {
                    leftTitle = "正立面 (单跨刚架)";
                    rightTitle = "柱网平面 & 截面";
                    DrawLine(lx0, lyBot, lx0, lyTop, SteelMemberBrush, 3.2);
                    DrawLine(lx1, lyBot, lx1, lyTop, SteelMemberBrush, 3.2);
                    DrawLine(lx0 - 8, lyTop, lx1 + 8, lyTop, SteelMemberBrush, 3.5);
                    DrawBaseSupport(lx0, lyBot);
                    DrawBaseSupport(lx1, lyBot);
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"跨度 B = {p.Span:0}", w);
                    DrawVertDim(lx0 - 26, lyBot, lyTop, $"H={p.Height:0}", h);

                    // 右侧：柱脚+H型钢柱截面与顶梁布置
                    DrawDashAxis(rx0, rMidY, rx1, rMidY);
                    DrawHColumnPlanSymbol(rx0 + 10, rMidY, 16, 14);
                    DrawHColumnPlanSymbol(rx1 - 10, rMidY, 16, 14);
                    DrawLine(rx0 + 10, rMidY, rx1 - 10, rMidY, SteelMemberBrush, 2.4);
                    DrawHorizDim(rx0 + 10, rx1 - 10, rMidY + 26, $"B = {p.Span:0}", w);
                    DrawTagBadge(rMidX, ryTop + 8, $"柱: {p.ColumnSection}");
                    DrawTagBadge(rMidX, ryBot - 6, $"梁: {p.BeamSection}");
                    break;
                }

                // ==================== 2. 多跨连续门式刚架 ====================
                case ParametricStructureType.MultiSpanPortalFrame:
                {
                    int bays = Math.Max(2, Math.Min(6, p.BayCount));
                    leftTitle = $"正立面 ({bays}跨连续刚架)";
                    rightTitle = "多跨柱网平面";
                    for (int i = 0; i <= bays; i++)
                    {
                        double xi = lx0 + (lx1 - lx0) * i / bays;
                        DrawLine(xi, lyBot, xi, lyTop, SteelMemberBrush, 2.8);
                        DrawBaseSupport(xi, lyBot);
                        if (i < bays)
                        {
                            double xNext = lx0 + (lx1 - lx0) * (i + 1) / bays;
                            DrawLine(xi, lyTop + 14, xi + 12, lyTop, SteelSubBrush, 1.6);
                            DrawLine(xNext, lyTop + 14, xNext - 12, lyTop, SteelSubBrush, 1.6);
                        }
                    }
                    DrawLine(lx0 - 8, lyTop, lx1 + 8, lyTop, SteelMemberBrush, 3.4);
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"总跨 B = {p.Span:0} ({bays}跨)", w);
                    DrawVertDim(lx0 - 26, lyBot, lyTop, $"H={p.Height:0}", h);

                    DrawDashAxis(rx0, rMidY, rx1, rMidY);
                    for (int i = 0; i <= bays; i++)
                    {
                        double rxi = rx0 + (rx1 - rx0) * i / bays;
                        DrawLine(rxi, ryTop + 10, rxi, ryBot - 16, AxisLineBrush, 0.9, true);
                        DrawHColumnPlanSymbol(rxi, rMidY, 12, 10);
                    }
                    DrawLine(rx0, rMidY, rx1, rMidY, SteelMemberBrush, 2.2);
                    DrawHorizDim(rx0, rx1, ryBot + 6, $"单跨 B/{bays} = {p.Span / bays:0}", w);
                    break;
                }

                // ==================== 3. 带人字斜撑框架 ====================
                case ParametricStructureType.BracedFrame:
                {
                    leftTitle = "正立面 (人字撑架)";
                    rightTitle = "侧视 & 撑杆节点";
                    DrawLine(lx0, lyBot, lx0, lyTop, SteelMemberBrush, 3.2);
                    DrawLine(lx1, lyBot, lx1, lyTop, SteelMemberBrush, 3.2);
                    DrawLine(lx0 - 6, lyTop, lx1 + 6, lyTop, SteelMemberBrush, 3.2);
                    DrawLine(lx0, lMidY + 6, lx1, lMidY + 6, SteelMemberBrush, 2.6);
                    DrawLine(lx0, lMidY + 6, lMidX, lyTop, SteelSubBrush, 2.2);
                    DrawLine(lx1, lMidY + 6, lMidX, lyTop, SteelSubBrush, 2.2);
                    DrawBaseSupport(lx0, lyBot);
                    DrawBaseSupport(lx1, lyBot);
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"跨度 B = {p.Span:0}", w);
                    DrawVertDim(lx0 - 26, lyBot, lyTop, $"H={p.Height:0}", h);
                    DrawVertDim(lx1 + 18, lyBot, lMidY + 6, $"h1={p.SubHeight:0}", h);

                    DrawLine(rMidX, ryBot, rMidX, ryTop, SteelMemberBrush, 3.2);
                    DrawBaseSupport(rMidX, ryBot);
                    DrawFilledRect(rMidX - 10, ryTop, 20, 12, PlateBrush, SteelMemberBrush, 1.2);
                    DrawFilledRect(rMidX - 10, rMidY, 20, 12, PlateBrush, SteelSubBrush, 1.2);
                    DrawTagBadge(rMidX, ryBot - 10, $"斜撑: {p.BraceSection}");
                    break;
                }

                // ==================== 4. 双层管廊支架 ====================
                case ParametricStructureType.DoubleTierFrame:
                {
                    leftTitle = "正立面 (双层管廊架)";
                    rightTitle = "管廊层高断面";
                    DrawLine(lx0, lyBot, lx0, lyTop, SteelMemberBrush, 3.2);
                    DrawLine(lx1, lyBot, lx1, lyTop, SteelMemberBrush, 3.2);
                    DrawLine(lx0 - 8, lyTop, lx1 + 8, lyTop, SteelMemberBrush, 3.2);
                    DrawLine(lx0 - 8, lMidY + 8, lx1 + 8, lMidY + 8, SteelMemberBrush, 2.8);
                    DrawLine(lx0, lMidY + 24, lx0 + 20, lMidY + 8, SteelSubBrush, 1.8);
                    DrawLine(lx1, lMidY + 24, lx1 - 20, lMidY + 8, SteelSubBrush, 1.8);
                    DrawBaseSupport(lx0, lyBot);
                    DrawBaseSupport(lx1, lyBot);
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"管廊跨度 B = {p.Span:0}", w);
                    DrawVertDim(lx0 - 26, lyBot, lyTop, $"H={p.Height:0}", h);
                    DrawVertDim(lx1 + 18, lyBot, lMidY + 8, $"h1={p.SubHeight:0}", h);

                    // 右侧：显示双层横梁上的工艺管道示意圆
                    DrawLine(rx0 + 10, ryTop + 10, rx1 - 10, ryTop + 10, SteelMemberBrush, 2.8);
                    DrawLine(rx0 + 10, rMidY + 10, rx1 - 10, rMidY + 10, SteelMemberBrush, 2.8);
                    foreach (double px in new[] { rx0 + 28, rMidX, rx1 - 28 })
                    {
                        DrawEllipse(px - 7, ryTop - 4, 14, 14, SteelSubBrush, 1.6);
                        DrawEllipse(px - 8, rMidY - 6, 16, 16, SteelSubBrush, 1.6);
                    }
                    DrawTagBadge(rMidX, ryBot - 4, $"顶层 H={p.Height:0} / 一层 h1={p.SubHeight:0}");
                    break;
                }

                // ==================== 5. 多层空间装置钢框架 ====================
                case ParametricStructureType.MultiBaySpaceFrame:
                {
                    leftTitle = "正立面 (横向 B×H)";
                    rightTitle = "侧立面 (进深 L & 柱撑)";
                    // 左：正立面两层框架
                    DrawLine(lx0, lyBot, lx0, lyTop, SteelMemberBrush, 3.2);
                    DrawLine(lx1, lyBot, lx1, lyTop, SteelMemberBrush, 3.2);
                    DrawLine(lx0, lyTop, lx1, lyTop, SteelMemberBrush, 3.0);
                    DrawLine(lx0, lMidY + 4, lx1, lMidY + 4, SteelMemberBrush, 2.8);
                    DrawBaseSupport(lx0, lyBot);
                    DrawBaseSupport(lx1, lyBot);
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"横向跨度 B = {p.Span:0}", w);
                    DrawVertDim(lx0 - 26, lyBot, lyTop, $"H={p.Height:0}", h);
                    DrawVertDim(lx1 + 16, lyBot, lMidY + 4, $"h1={p.SubHeight:0}", h);

                    // 右：侧立面（纵向进深 L + 一层柱间斜撑 + 次梁节点）
                    DrawLine(rx0, ryBot, rx0, ryTop, SteelMemberBrush, 2.8);
                    DrawLine(rx1, ryBot, rx1, ryTop, SteelMemberBrush, 2.8);
                    DrawLine(rx0, ryTop, rx1, ryTop, SteelMemberBrush, 2.6);
                    DrawLine(rx0, rMidY + 4, rx1, rMidY + 4, SteelMemberBrush, 2.4);
                    DrawLine(rx0, ryBot, rx1, rMidY + 4, SteelSubBrush, 2.0);
                    DrawFilledRect(rMidX - 4, ryTop, 8, 8, SteelSubBrush, SteelSubBrush, 1.2);
                    DrawFilledRect(rMidX - 4, rMidY + 4, 8, 8, SteelSubBrush, SteelSubBrush, 1.2);
                    DrawBaseSupport(rx0, ryBot);
                    DrawBaseSupport(rx1, ryBot);
                    DrawHorizDim(rx0, rx1, ryBot + 14, $"进深 L = {p.Length:0}", w);
                    break;
                }

                // ==================== 6. 悬臂三角托架 ====================
                case ParametricStructureType.CantileverBracket:
                {
                    leftTitle = "侧立面 (悬臂三角托架)";
                    rightTitle = "俯视平面 & 锚板";
                    double wallX = lx0 + 18;
                    DrawWallHatch(wallX, lyTop - 4, lyBot + 4);
                    DrawLine(wallX, lyBot, wallX, lyTop, SteelMemberBrush, 3.6);
                    DrawLine(wallX, lyTop + 8, lx1, lyTop + 8, SteelMemberBrush, 3.2);
                    DrawLine(wallX, lMidY + 20, lx1 - 16, lyTop + 8, SteelSubBrush, 2.4);
                    DrawBaseSupport(wallX, lyBot);
                    DrawHorizDim(wallX, lx1, lyBot + 18, $"挑出 L = {p.Span:0}", w);
                    DrawVertDim(wallX - 28, lyBot, lyTop, $"H={p.Height:0}", h);
                    DrawVertDim(lx1 + 14, lMidY + 20, lyTop + 8, $"h1={p.SubHeight:0}", h);

                    DrawFilledRect(rx0, rMidY - 18, 10, 36, PlateBrush, SteelMemberBrush, 1.5);
                    DrawLine(rx0 + 10, rMidY - 6, rx1, rMidY - 6, SteelMemberBrush, 2.2);
                    DrawLine(rx0 + 10, rMidY + 6, rx1, rMidY + 6, SteelMemberBrush, 2.2);
                    DrawLine(rx1, rMidY - 6, rx1, rMidY + 6, SteelMemberBrush, 2.2);
                    DrawTagBadge(rMidX, ryBot - 4, $"挑梁: {p.BeamSection}");
                    break;
                }

                // ==================== 7. 四柱空间检修钢平台 ====================
                case ParametricStructureType.SpacePlatform:
                {
                    leftTitle = "正立面 (带双层护栏)";
                    rightTitle = "平台平面梁格 (B×L)";
                    double deckY = lyTop + 38;
                    DrawLine(lx0, lyBot, lx0, deckY, SteelMemberBrush, 3.2);
                    DrawLine(lx1, lyBot, lx1, deckY, SteelMemberBrush, 3.2);
                    DrawLine(lx0 - 4, deckY, lx1 + 4, deckY, SteelMemberBrush, 3.4);
                    // 平台护栏
                    for (int i = 0; i <= 3; i++)
                    {
                        double px = lx0 + (lx1 - lx0) * i / 3.0;
                        DrawLine(px, deckY, px, lyTop, SteelSubBrush, 1.6);
                    }
                    DrawLine(lx0, lyTop, lx1, lyTop, SteelSubBrush, 2.0);
                    DrawLine(lx0, (lyTop + deckY) * 0.5, lx1, (lyTop + deckY) * 0.5, SteelSubBrush, 1.4);
                    DrawBaseSupport(lx0, lyBot);
                    DrawBaseSupport(lx1, lyBot);
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"平台跨度 B = {p.Span:0}", w);
                    DrawVertDim(lx0 - 26, lyBot, deckY, $"H={p.Height:0}", h);
                    DrawVertDim(lx1 + 16, deckY, lyTop, $"h1={p.SubHeight:0}", h);

                    // 右侧：平面图（四柱 + 边梁 + 2根次梁）
                    DrawRect(rx0, ryTop, rx1 - rx0, ryBot - ryTop, SteelMemberBrush, 2.4);
                    double sy1 = ryTop + (ryBot - ryTop) / 3.0;
                    double sy2 = ryTop + (ryBot - ryTop) * 2.0 / 3.0;
                    DrawLine(rx0, sy1, rx1, sy1, SteelSubBrush, 1.5);
                    DrawLine(rx0, sy2, rx1, sy2, SteelSubBrush, 1.5);
                    foreach (double cx in new[] { rx0, rx1 })
                        foreach (double cy in new[] { ryTop, ryBot })
                            DrawHColumnPlanSymbol(cx, cy, 10, 10);
                    DrawHorizDim(rx0, rx1, ryBot + 14, $"B={p.Span:0} × L={p.Length:0}", w);
                    break;
                }

                // ==================== 8. 悬挑走道平台 ====================
                case ParametricStructureType.CantileverWalkway:
                {
                    leftTitle = "纵立面 (悬挑走道)";
                    rightTitle = "横断面 (牛腿 W×H)";
                    double deckY = lMidY + 4;
                    DrawLine(lx0, deckY, lx1, deckY, SteelMemberBrush, 3.0);
                    DrawLine(lx0, lyTop, lx1, lyTop, SteelSubBrush, 2.0);
                    DrawLine(lx0, (lyTop + deckY) * 0.5, lx1, (lyTop + deckY) * 0.5, SteelSubBrush, 1.4);
                    for (int i = 0; i < 4; i++)
                    {
                        double xi = lx0 + (lx1 - lx0) * i / 3.0;
                        DrawLine(xi, deckY, xi, lyTop, SteelSubBrush, 1.6);
                        DrawLine(xi, deckY, xi, deckY + 28, SteelMemberBrush, 2.2);
                        DrawLine(xi - 10, deckY + 28, xi + 10, deckY, SteelSubBrush, 1.5);
                    }
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"走道总长 L = {p.Span:0}", w);
                    DrawVertDim(lx0 - 26, lyBot, deckY, $"H={p.Height:0}", h);

                    // 右侧：悬挑牛腿横断面
                    double wallX = rx0 + 8;
                    DrawWallHatch(wallX, ryTop, ryBot);
                    DrawLine(wallX, rMidY + 8, rx1 - 6, rMidY + 8, SteelMemberBrush, 2.8);
                    DrawLine(wallX, ryBot - 8, rx1 - 16, rMidY + 8, SteelSubBrush, 2.0);
                    DrawLine(rx1 - 6, rMidY + 8, rx1 - 6, ryTop + 4, SteelSubBrush, 2.0);
                    DrawHorizDim(wallX, rx1 - 6, ryBot + 14, $"走道宽 W = {p.Length:0}", w);
                    DrawVertDim(rx1 + 10, rMidY + 8, ryTop + 4, $"h1={p.SubHeight:0}", h);
                    break;
                }

                // ==================== 9. 双坡屋面钢桁架 ====================
                case ParametricStructureType.RoofTruss:
                {
                    leftTitle = "正立面 (双坡屋架)";
                    rightTitle = "屋脊节点 & 坡度";
                    double eaveY = lMidY + 14;
                    DrawLine(lx0, lyBot, lx0, eaveY, SteelMemberBrush, 3.0);
                    DrawLine(lx1, lyBot, lx1, eaveY, SteelMemberBrush, 3.0);
                    DrawLine(lx0, eaveY, lx1, eaveY, SteelMemberBrush, 2.6);
                    DrawLine(lx0, eaveY, lMidX, lyTop, SteelMemberBrush, 2.8);
                    DrawLine(lx1, eaveY, lMidX, lyTop, SteelMemberBrush, 2.8);
                    DrawLine(lMidX, eaveY, lMidX, lyTop, SteelSubBrush, 1.8);
                    double q1 = (lx0 + lMidX) * 0.5, q3 = (lx1 + lMidX) * 0.5;
                    double qY = (eaveY + lyTop) * 0.5;
                    DrawLine(q1, eaveY, q1, qY, SteelSubBrush, 1.6);
                    DrawLine(q3, eaveY, q3, qY, SteelSubBrush, 1.6);
                    DrawLine(q1, qY, lMidX, eaveY, SteelSubBrush, 1.6);
                    DrawLine(q3, qY, lMidX, eaveY, SteelSubBrush, 1.6);
                    DrawBaseSupport(lx0, lyBot);
                    DrawBaseSupport(lx1, lyBot);
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"屋架跨度 B = {p.Span:0}", w);
                    DrawVertDim(lx0 - 26, lyBot, eaveY, $"檐高 H={p.Height:0}", h);
                    DrawVertDim(lx1 + 16, eaveY, lyTop, $"矢高 h1={p.SubHeight:0}", h);

                    // 右侧：屋脊放大节点
                    DrawLine(rx0, rMidY + 18, rMidX, ryTop + 14, SteelMemberBrush, 3.0);
                    DrawLine(rx1, rMidY + 18, rMidX, ryTop + 14, SteelMemberBrush, 3.0);
                    DrawLine(rx0, rMidY + 34, rx1, rMidY + 34, SteelMemberBrush, 2.4);
                    DrawLine(rMidX, ryTop + 14, rMidX, rMidY + 34, SteelSubBrush, 2.0);
                    DrawTagBadge(rMidX, ryBot - 8, $"总高 H+h1 = {p.Height + p.SubHeight:0}");
                    break;
                }

                // ==================== 10. 空间管廊桁架桥 ====================
                case ParametricStructureType.PipeBridgeTruss:
                {
                    leftTitle = "正立面 (空间桁架廊桥)";
                    rightTitle = "横断面 (净宽 W×桁高 H)";
                    double tTop = lyTop + 12, tBot = lyBot - 10;
                    DrawRect(lx0, tTop, lx1 - lx0, tBot - tTop, SteelMemberBrush, 2.8);
                    for (int i = 0; i < 4; i++)
                    {
                        double xa = lx0 + (lx1 - lx0) * i / 4.0;
                        double xb = lx0 + (lx1 - lx0) * (i + 1) / 4.0;
                        DrawLine(xa, tTop, xa, tBot, SteelSubBrush, 1.8);
                        DrawLine(xa, tBot, xb, tTop, SteelSubBrush, 1.6);
                    }
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"廊桥跨度 L = {p.Span:0}", w);
                    DrawVertDim(lx0 - 26, tBot, tTop, $"H={p.Height:0}", h);

                    // 右侧：箱形桁架横断面
                    DrawRect(rx0 + 6, ryTop + 8, rx1 - rx0 - 12, ryBot - ryTop - 16, SteelMemberBrush, 2.6);
                    DrawLine(rx0 + 6, ryTop + 8, rx1 - 6, ryBot - 8, SteelSubBrush, 1.4);
                    DrawLine(rx0 + 6, ryBot - 8, rx1 - 6, ryTop + 8, SteelSubBrush, 1.4);
                    DrawHorizDim(rx0 + 6, rx1 - 6, ryBot + 14, $"净宽 W = {p.Length:0}", w);
                    break;
                }

                // ==================== 11. 储罐连续螺旋盘梯 ====================
                case ParametricStructureType.SpiralStair:
                {
                    leftTitle = "立面展开 (连续螺旋盘梯)";
                    rightTitle = $"平面旋转 ({p.SubHeight:0}°)";
                    // 左：储罐壁 + 连续螺旋爬升斜梁与扶手
                    DrawRect(lx0 + 10, lyTop, lx1 - lx0 - 20, lyBot - lyTop, BorderLineBrush, 1.2);
                    int steps = 8;
                    for (int i = 0; i < steps; i++)
                    {
                        double t0 = (double)i / steps, t1 = (double)(i + 1) / steps;
                        double xA = lx0 + 14 + (lx1 - lx0 - 28) * t0;
                        double xB = lx0 + 14 + (lx1 - lx0 - 28) * t1;
                        double yA = lyBot - (lyBot - lyTop - 22) * t0;
                        double yB = lyBot - (lyBot - lyTop - 22) * t1;
                        DrawLine(xA, yA, xB, yB, SteelMemberBrush, 2.8);
                        DrawLine(xA, yA - 20, xB, yB - 20, SteelSubBrush, 1.8);
                        DrawLine(xA, yA, xA, yA - 20, SteelSubBrush, 1.4);
                        DrawLine(xA - 5, yA, xA + 8, yA, PlateBrush, 2.0);
                    }
                    DrawHorizDim(lx0 + 10, lx1 - 10, lyBot + 18, $"外径 D = {p.Span:0}", w);
                    DrawVertDim(lx0 - 18, lyBot, lyTop, $"H={p.Height:0}", h);

                    // 右：同心圆弧扇形平面 (按真实旋转角 SubHeight 绘制)
                    double rOut = Math.Min(rx1 - rx0, ryBot - ryTop) * 0.44;
                    double rIn = rOut * 0.62;
                    double sweepDeg = Math.Min(360.0, Math.Max(60.0, p.SubHeight));
                    DrawCircleArc(rMidX, rMidY, rIn, 0, sweepDeg, SteelMemberBrush, 2.0);
                    DrawCircleArc(rMidX, rMidY, rOut, 0, sweepDeg, SteelSubBrush, 2.4);
                    int rSteps = 8;
                    for (int i = 0; i <= rSteps; i++)
                    {
                        double a = (sweepDeg * i / rSteps) * Math.PI / 180.0;
                        DrawLine(rMidX + rIn * Math.Cos(a), rMidY - rIn * Math.Sin(a),
                                 rMidX + rOut * Math.Cos(a), rMidY - rOut * Math.Sin(a), SteelMemberBrush, 1.4);
                    }
                    DrawTagBadge(rMidX, ryBot + 10, $"踏宽 W={p.Length:0} | 旋转 {p.SubHeight:0}°");
                    break;
                }

                // ==================== 12. 直跑钢斜梯 ====================
                case ParametricStructureType.StraightStair:
                {
                    leftTitle = "侧立面 (直跑钢斜梯)";
                    rightTitle = "梯段平面 (W×L)";
                    double topStepY = lMidY - 8;
                    DrawLine(lx0, lyBot, lx1, topStepY, SteelMemberBrush, 3.2);
                    DrawLine(lx0, lMidY + 2, lx1, lyTop, SteelSubBrush, 2.2);
                    DrawLine(lx0, (lyBot + lMidY + 2) * 0.5, lx1, (topStepY + lyTop) * 0.5, SteelSubBrush, 1.4);
                    for (int i = 0; i <= 5; i++)
                    {
                        double t = i / 5.0;
                        double sx = lx0 + (lx1 - lx0) * t;
                        double sy = lyBot + (topStepY - lyBot) * t;
                        DrawLine(sx - 6, sy, sx + 8, sy, PlateBrush, 2.2);
                        if (i % 2 == 0 || i == 5)
                        {
                            double ry = (lMidY + 2) + (lyTop - (lMidY + 2)) * t;
                            DrawLine(sx, sy, sx, ry, SteelSubBrush, 1.6);
                        }
                    }
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"水平长 L = {p.Span:0}", w);
                    DrawVertDim(lx1 + 16, lyBot, topStepY, $"H={p.Height:0}", h);
                    DrawVertDim(lx0 - 26, lyBot, lMidY + 2, $"h1={p.SubHeight:0}", h);

                    DrawRect(rx0, ryTop + 14, rx1 - rx0, ryBot - ryTop - 28, SteelMemberBrush, 2.4);
                    for (int i = 1; i <= 5; i++)
                    {
                        double tx = rx0 + (rx1 - rx0) * i / 6.0;
                        DrawLine(tx, ryTop + 14, tx, ryBot - 14, SteelSubBrush, 1.4);
                    }
                    DrawHorizDim(rx0, rx1, ryBot + 12, $"梯宽 W = {p.Length:0}", w);
                    break;
                }

                // ==================== 13. 带安全护笼钢直梯 ====================
                case ParametricStructureType.SafetyCagedLadder:
                {
                    leftTitle = "正立面 (护笼直梯)";
                    rightTitle = "护笼平面 (W×D)";
                    double ladW = 36;
                    double cageStartY = lyBot - (lyBot - lyTop) * 0.38;
                    DrawLine(lMidX - ladW * 0.5, lyBot, lMidX - ladW * 0.5, lyTop, SteelMemberBrush, 2.8);
                    DrawLine(lMidX + ladW * 0.5, lyBot, lMidX + ladW * 0.5, lyTop, SteelMemberBrush, 2.8);
                    for (int i = 1; i <= 7; i++)
                    {
                        double ry = lyBot - (lyBot - lyTop) * i / 7.5;
                        DrawLine(lMidX - ladW * 0.5, ry, lMidX + ladW * 0.5, ry, SteelSubBrush, 1.6);
                    }
                    // 护笼横箍与立条
                    DrawRect(lMidX - 36, lyTop, 72, cageStartY - lyTop, SteelSubBrush, 1.8);
                    for (int j = 1; j <= 3; j++)
                    {
                        double hy = lyTop + (cageStartY - lyTop) * j / 4.0;
                        DrawLine(lMidX - 36, hy, lMidX + 36, hy, SteelSubBrush, 1.6);
                    }
                    DrawHorizDim(lMidX - 36, lMidX + 36, lyBot + 18, $"梯宽 W = {p.Span:0}", w);
                    DrawVertDim(lMidX - 58, lyBot, lyTop, $"H={p.Height:0}", h);
                    DrawVertDim(lMidX + 52, lyBot, cageStartY, $"h1={p.SubHeight:0}", h);

                    // 右侧：护笼圆弧平面
                    double rCage = Math.Min(rx1 - rx0, ryBot - ryTop) * 0.38;
                    DrawLine(rMidX - 18, rMidY - rCage * 0.6, rMidX + 18, rMidY - rCage * 0.6, SteelMemberBrush, 3.0);
                    DrawCircleArc(rMidX, rMidY, rCage, 160, 380, SteelSubBrush, 2.2);
                    DrawHorizDim(rMidX - rCage, rMidX + rCage, ryBot + 12, $"笼径 D = {p.Length:0}", w);
                    break;
                }

                // ==================== 14. 平台连续安全栏杆/扶手 ====================
                case ParametricStructureType.HandrailGuardrail:
                {
                    leftTitle = "正立面 (连续安全栏杆)";
                    rightTitle = "立柱断面 & 踢脚板";
                    DrawLine(lx0, lyTop + 6, lx1, lyTop + 6, SteelMemberBrush, 3.0);
                    DrawLine(lx0, lMidY + 4, lx1, lMidY + 4, SteelSubBrush, 2.2);
                    DrawFilledRect(lx0, lyBot - 10, lx1 - lx0, 8, PlateBrush, SteelMemberBrush, 1.2);
                    for (int i = 0; i < 4; i++)
                    {
                        double px = lx0 + (lx1 - lx0) * i / 3.0;
                        DrawLine(px, lyBot, px, lyTop + 6, SteelMemberBrush, 2.6);
                        DrawBaseSupport(px, lyBot);
                    }
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"总长 L = {p.Span:0} (间距≤{p.SubHeight:0})", w);
                    DrawVertDim(lx0 - 26, lyBot, lyTop + 6, $"H={p.Height:0}", h);

                    DrawLine(rMidX, ryBot, rMidX, ryTop + 8, SteelMemberBrush, 3.2);
                    DrawBaseSupport(rMidX, ryBot);
                    DrawEllipse(rMidX - 6, ryTop + 2, 12, 12, SteelSubBrush, 2.0);
                    DrawEllipse(rMidX - 5, rMidY, 10, 10, SteelSubBrush, 1.8);
                    DrawFilledRect(rMidX + 2, ryBot - 18, 6, 16, PlateBrush, PlateBrush, 1.0);
                    DrawTagBadge(rMidX, ryBot + 12, $"立柱: {p.ColumnSection}");
                    break;
                }

                // ==================== 15. 直线单根型钢 ====================
                case ParametricStructureType.LinearSteelMember:
                {
                    leftTitle = "立面 (三维空间直杆)";
                    rightTitle = "型钢截面轮廓";
                    bool hasRise = Math.Abs(p.EndPointOffsetZ) > 10;
                    double yStart = hasRise ? lyBot - 12 : lMidY;
                    double yEnd = hasRise ? lyTop + 14 : lMidY;
                    DrawDashAxis(lx0, yStart, lx1, yStart);
                    DrawLine(lx0, yStart, lx1, yEnd, SteelMemberBrush, 5.0);
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"水平长 L = {p.Span:0}", w);
                    if (hasRise)
                    {
                        DrawVertDim(lx1 + 16, yStart, yEnd, $"ΔZ={p.EndPointOffsetZ:0}", h);
                    }

                    DrawHColumnPlanSymbol(rMidX, rMidY - 6, 36, 44);
                    DrawTagBadge(rMidX, ryBot + 10, $"{p.ColumnSection} ({p.Material})");
                    break;
                }

                // ==================== 16. 弧形单根型钢 ====================
                case ParametricStructureType.ArcSteelMember:
                {
                    leftTitle = $"平面 (圆弧扫掠 {p.SubHeight:0}°)";
                    rightTitle = "立面爬升 & 截面";
                    double rArc = Math.Min(lx1 - lx0, lyBot - lyTop) * 0.44;
                    double sweep = Math.Min(330.0, Math.Max(20.0, p.SubHeight));
                    DrawCircleArc(lMidX, lMidY + 8, rArc, 0, sweep, SteelMemberBrush, 4.0);
                    double radEnd = sweep * Math.PI / 180.0;
                    DrawLine(lMidX, lMidY + 8, lMidX + rArc, lMidY + 8, AxisLineBrush, 1.2, true);
                    DrawLine(lMidX, lMidY + 8, lMidX + rArc * Math.Cos(radEnd), lMidY + 8 - rArc * Math.Sin(radEnd), AxisLineBrush, 1.2, true);
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"半径 R = {p.Span:0} | 扫掠 {p.SubHeight:0}°", w);

                    DrawLine(rx0, ryBot - 16, rx1, ryTop + 20, SteelMemberBrush, 3.6);
                    DrawTagBadge(rMidX, ryBot + 10, $"高差 ΔZ = {p.EndPointOffsetZ:0} | {p.ColumnSection}");
                    break;
                }

                // ==================== 17. 螺旋单根型钢 ====================
                case ParametricStructureType.SpiralSteelMember:
                {
                    leftTitle = $"主视图 (螺旋 {p.SubHeight:0.#} 圈)";
                    rightTitle = "端视螺旋圆";
                    DrawRect(lx0, lyTop + 14, lx1 - lx0, lyBot - lyTop - 28, BorderLineBrush, 1.0);
                    int n = 32;
                    double px0 = lx0, py0 = lMidY;
                    double turns = p.SubHeight > 30 ? p.SubHeight / 360.0 : Math.Max(0.5, p.SubHeight);
                    for (int i = 1; i <= n; i++)
                    {
                        double t = (double)i / n;
                        double a = t * Math.Min(turns, 5.0) * Math.PI * 2.0;
                        double px = lx0 + (lx1 - lx0) * t;
                        double py = lMidY + Math.Sin(a) * (lyBot - lyTop - 32) * 0.46;
                        DrawLine(px0, py0, px, py, SteelMemberBrush, 3.0);
                        px0 = px; py0 = py;
                    }
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"轴长 L = {p.Span:0}", w);
                    DrawVertDim(lx0 - 26, lyBot - 14, lyTop + 14, $"2R={p.Length * 2:0}", h);

                    double rHelix = Math.Min(rx1 - rx0, ryBot - ryTop) * 0.38;
                    DrawEllipse(rMidX - rHelix, rMidY - rHelix, rHelix * 2, rHelix * 2, SteelMemberBrush, 2.8);
                    DrawHorizDim(rMidX - rHelix, rMidX + rHelix, ryBot + 12, $"R = {p.Length:0}", w);
                    break;
                }

                // ==================== 18. 卧式容器鞍式钢支座 ====================
                case ParametricStructureType.VesselSupport:
                {
                    leftTitle = $"正立面 (包角 {p.SubHeight:0}°)";
                    rightTitle = "侧视图 (鞍宽 B)";
                    double rV = Math.Min((lx1 - lx0) * 0.42, (lyBot - lyTop) * 0.46);
                    double cyV = lyTop + rV + 2;
                    // 容器虚线圆与月牙形鞍座板
                    DrawEllipse(lMidX - rV, cyV - rV, rV * 2, rV * 2, BorderLineBrush, 1.4);
                    DrawCircleArc(lMidX, cyV, rV, 210, 330, SteelMemberBrush, 3.6);
                    double saddleL = lMidX - rV * 0.86, saddleR = lMidX + rV * 0.86;
                    double arcBotY = cyV + rV * 0.5;
                    DrawLine(saddleL - 8, lyBot, saddleR + 8, lyBot, SteelMemberBrush, 3.6);
                    DrawLine(saddleL, lyBot, saddleL + 8, arcBotY, SteelMemberBrush, 2.6);
                    DrawLine(saddleR, lyBot, saddleR - 8, arcBotY, SteelMemberBrush, 2.6);
                    DrawLine(lMidX, lyBot, lMidX, cyV + rV, SteelSubBrush, 2.2);
                    DrawHorizDim(lMidX - rV, lMidX + rV, lyBot + 18, $"容器外径 DN = {p.Span:0}", w);
                    DrawVertDim(lx0 - 20, lyBot, cyV, $"H={p.Height:0}", h);

                    // 右：侧视图
                    DrawLine(rx0 + 8, ryBot, rx1 - 8, ryBot, SteelMemberBrush, 3.4);
                    DrawLine(rMidX, ryBot, rMidX, ryTop + 26, SteelMemberBrush, 2.8);
                    DrawLine(rx0 + 16, ryBot, rx0 + 24, ryTop + 26, SteelSubBrush, 1.8);
                    DrawLine(rx1 - 16, ryBot, rx1 - 24, ryTop + 26, SteelSubBrush, 1.8);
                    DrawLine(rx0 + 6, ryTop + 26, rx1 - 6, ryTop + 26, SteelMemberBrush, 2.6);
                    DrawHorizDim(rx0 + 8, rx1 - 8, ryBot + 14, $"鞍宽 B = {p.Length:0}", w);
                    break;
                }

                // ==================== 19. 塔器环形检修平台 ====================
                case ParametricStructureType.TowerCircularPlatform:
                {
                    leftTitle = $"俯视平面 (包角 {p.SubHeight:0}°)";
                    rightTitle = "悬挑牛腿断面";
                    double rOut = Math.Min(lx1 - lx0, lyBot - lyTop) * 0.45;
                    double rIn = rOut * 0.52;
                    double sweep = Math.Min(360.0, Math.Max(60.0, p.SubHeight));
                    DrawEllipse(lMidX - rIn, lMidY - rIn, rIn * 2, rIn * 2, PlateBrush, 2.2);
                    DrawCircleArc(lMidX, lMidY, rOut, 0, sweep, SteelMemberBrush, 2.8);
                    int bCnt = 6;
                    for (int i = 0; i <= bCnt; i++)
                    {
                        double a = (sweep * i / bCnt) * Math.PI / 180.0;
                        DrawLine(lMidX + rIn * Math.Cos(a), lMidY - rIn * Math.Sin(a),
                                 lMidX + rOut * Math.Cos(a), lMidY - rOut * Math.Sin(a), SteelSubBrush, 1.8);
                    }
                    DrawHorizDim(lMidX - rOut, lMidX + rOut, lyBot + 18, $"塔径 DN = {p.Span:0}", w);

                    // 右：塔壁 + 牛腿 + 斜撑 + 护栏断面
                    double tWallX = rx0 + 10;
                    DrawLine(tWallX, ryTop, tWallX, ryBot, PlateBrush, 3.4);
                    DrawLine(tWallX, rMidY, rx1 - 8, rMidY, SteelMemberBrush, 2.8);
                    DrawLine(tWallX, ryBot - 10, rx1 - 18, rMidY, SteelSubBrush, 2.2);
                    DrawLine(rx1 - 8, rMidY, rx1 - 8, ryTop + 6, SteelSubBrush, 2.0);
                    DrawHorizDim(tWallX, rx1 - 8, ryBot + 14, $"环宽 W = {p.Length:0}", w);
                    DrawVertDim(tWallX - 18, ryBot - 10, rMidY, $"H={p.Height:0}", h);
                    break;
                }

                // ==================== 20. 立式塔器/容器钢裙座 ====================
                case ParametricStructureType.VerticalVesselSkirt:
                {
                    leftTitle = "正立面 (钢裙座 & 引出孔)";
                    rightTitle = $"底环俯视 ({p.BayCount}筋板)";
                    double skW = Math.Min(92.0, (lx1 - lx0) * 0.56);
                    DrawRect(lMidX - skW * 0.5, lyTop + 4, skW, lyBot - lyTop - 4, SteelMemberBrush, 2.8);
                    DrawLine(lMidX - skW * 0.5 - 16, lyBot, lMidX + skW * 0.5 + 16, lyBot, SteelMemberBrush, 4.0);
                    // 座圈筋板
                    DrawLine(lMidX - skW * 0.5 - 12, lyBot, lMidX - skW * 0.5, lyBot - 22, SteelSubBrush, 2.0);
                    DrawLine(lMidX + skW * 0.5 + 12, lyBot, lMidX + skW * 0.5, lyBot - 22, SteelSubBrush, 2.0);
                    // 检查孔
                    DrawEllipse(lMidX - 14, lMidY - 12, 28, 28, SteelSubBrush, 2.0);
                    DrawHorizDim(lMidX - skW * 0.5, lMidX + skW * 0.5, lyBot + 18, $"裙座外径 DN = {p.Span:0}", w);
                    DrawVertDim(lMidX - skW * 0.5 - 28, lyBot, lyTop + 4, $"H={p.Height:0}", h);

                    double rSk = Math.Min(rx1 - rx0, ryBot - ryTop) * 0.34;
                    double rRing = rSk + 10;
                    DrawEllipse(rMidX - rRing, rMidY - rRing, rRing * 2, rRing * 2, SteelMemberBrush, 2.0);
                    DrawEllipse(rMidX - rSk, rMidY - rSk, rSk * 2, rSk * 2, SteelSubBrush, 2.2);
                    int gCnt = Math.Max(8, Math.Min(16, p.BayCount));
                    for (int i = 0; i < gCnt; i++)
                    {
                        double a = 2.0 * Math.PI * i / gCnt;
                        DrawLine(rMidX + rSk * Math.Cos(a), rMidY + rSk * Math.Sin(a),
                                 rMidX + rRing * Math.Cos(a), rMidY + rRing * Math.Sin(a), SteelMemberBrush, 1.6);
                    }
                    DrawTagBadge(rMidX, ryBot + 10, $"环宽 B={p.Length:0} | 孔 d={p.SubHeight:0}");
                    break;
                }

                // ==================== 21. 化工管道加劲管托/支座 ====================
                case ParametricStructureType.PipeShoeSupport:
                {
                    leftTitle = "横断面 (双管卡加劲管托)";
                    rightTitle = "侧立面 (管托长 L)";
                    double rP = Math.Min(32.0, (lyBot - lyTop) * 0.25);
                    double cyP = lyTop + rP + 10;
                    DrawEllipse(lMidX - rP, cyP - rP, rP * 2, rP * 2, SteelSubBrush, 2.4);
                    // 管卡耳板
                    DrawLine(lMidX - rP - 10, cyP, lMidX + rP + 10, cyP, SteelMemberBrush, 2.2);
                    // 腹板与肋板
                    double shoeHalfB = Math.Min(48.0, (lx1 - lx0) * 0.34);
                    DrawLine(lMidX, cyP + rP, lMidX, lyBot, SteelMemberBrush, 3.2);
                    DrawLine(lMidX - shoeHalfB * 0.85, lyBot, lMidX - rP * 0.6, cyP + rP * 0.8, SteelSubBrush, 1.8);
                    DrawLine(lMidX + shoeHalfB * 0.85, lyBot, lMidX + rP * 0.6, cyP + rP * 0.8, SteelSubBrush, 1.8);
                    DrawLine(lMidX - shoeHalfB, lyBot, lMidX + shoeHalfB, lyBot, SteelMemberBrush, 3.6);
                    DrawHorizDim(lMidX - shoeHalfB, lMidX + shoeHalfB, lyBot + 18, $"底板宽 B={p.SubHeight:0} (DN{p.Span:0})", w);
                    DrawVertDim(lMidX - shoeHalfB - 24, lyBot, cyP, $"H={p.Height:0}", h);

                    // 右：侧视图
                    DrawLine(rx0 + 6, ryTop + 14, rx1 - 6, ryTop + 14, SteelSubBrush, 2.0);
                    DrawLine(rx0 + 6, rMidY + 4, rx1 - 6, rMidY + 4, SteelSubBrush, 2.2);
                    DrawRect(rx0 + 10, rMidY + 4, rx1 - rx0 - 20, ryBot - rMidY - 4, SteelMemberBrush, 2.2);
                    double rib1 = rx0 + (rx1 - rx0) * 0.32, rib2 = rx0 + (rx1 - rx0) * 0.68;
                    DrawLine(rib1, rMidY + 4, rib1, ryBot, SteelSubBrush, 1.8);
                    DrawLine(rib2, rMidY + 4, rib2, ryBot, SteelSubBrush, 1.8);
                    DrawHorizDim(rx0 + 10, rx1 - 10, ryBot + 14, $"管托长 L = {p.Length:0}", w);
                    break;
                }

                // ==================== 22. 储罐拱顶环形安全栏杆 ====================
                case ParametricStructureType.TankRoofHandrail:
                {
                    leftTitle = $"俯视平面 (包角 {p.SubHeight:0}°)";
                    rightTitle = "罐顶护栏立面";
                    double rT = Math.Min(lx1 - lx0, lyBot - lyTop) * 0.44;
                    double sweep = Math.Min(360.0, Math.Max(60.0, p.SubHeight));
                    DrawEllipse(lMidX - rT, lMidY - rT, rT * 2, rT * 2, BorderLineBrush, 1.4);
                    DrawCircleArc(lMidX, lMidY, rT, 0, sweep, SteelMemberBrush, 3.0);
                    for (int i = 0; i <= 8; i++)
                    {
                        double a = (sweep * i / 8.0) * Math.PI / 180.0;
                        double px = lMidX + rT * Math.Cos(a), py = lMidY - rT * Math.Sin(a);
                        DrawEllipse(px - 2.5, py - 2.5, 5, 5, SteelSubBrush, 1.6);
                    }
                    DrawHorizDim(lMidX - rT, lMidX + rT, lyBot + 18, $"储罐直径 D = {p.Span:0}", w);

                    DrawLine(rx0, ryBot - 6, rx1, ryBot - 6, PlateBrush, 2.4);
                    DrawLine(rx0 + 6, ryTop + 12, rx1 - 6, ryTop + 12, SteelMemberBrush, 2.6);
                    DrawLine(rx0 + 6, rMidY + 6, rx1 - 6, rMidY + 6, SteelSubBrush, 1.8);
                    for (int i = 0; i < 4; i++)
                    {
                        double px = rx0 + 6 + (rx1 - rx0 - 12) * i / 3.0;
                        DrawLine(px, ryBot - 6, px, ryTop + 12, SteelMemberBrush, 2.0);
                    }
                    DrawVertDim(rx0 - 14, ryBot - 6, ryTop + 12, $"H={p.Height:0}", h);
                    break;
                }

                // ==================== 23. 变电站A字型龙门构架 ====================
                case ParametricStructureType.SubstationGantryFrame:
                {
                    leftTitle = "正立面 (龙门构架 & 避雷针)";
                    rightTitle = "侧立面 (A字柱根开 L)";
                    double beamY = lyTop + 34;
                    DrawLine(lx0 + 10, lyBot, lx0 + 10, beamY, SteelMemberBrush, 3.2);
                    DrawLine(lx1 - 10, lyBot, lx1 - 10, beamY, SteelMemberBrush, 3.2);
                    DrawRect(lx0 + 10, beamY, lx1 - lx0 - 20, 16, SteelSubBrush, 2.2);
                    for (int i = 0; i < 5; i++)
                    {
                        double xa = lx0 + 10 + (lx1 - lx0 - 20) * i / 5.0;
                        double xb = lx0 + 10 + (lx1 - lx0 - 20) * (i + 1) / 5.0;
                        DrawLine(xa, beamY, xb, beamY + 16, SteelSubBrush, 1.3);
                    }
                    // 柱顶避雷针
                    DrawLine(lx0 + 10, beamY, lx0 + 10, lyTop, SteelSubBrush, 2.0);
                    DrawLine(lx1 - 10, beamY, lx1 - 10, lyTop, SteelSubBrush, 2.0);
                    DrawBaseSupport(lx0 + 10, lyBot);
                    DrawBaseSupport(lx1 - 10, lyBot);
                    DrawHorizDim(lx0 + 10, lx1 - 10, lyBot + 18, $"构架跨度 B = {p.Span:0}", w);
                    DrawVertDim(lx0 - 18, lyBot, beamY, $"H={p.Height:0}", h);
                    DrawVertDim(lx1 + 8, beamY, lyTop, $"h1={p.SubHeight:0}", h);

                    // 右：A 字型双肢柱侧立面
                    double aApexY = ryTop + 26;
                    DrawLine(rx0 + 8, ryBot, rMidX, aApexY, SteelMemberBrush, 2.8);
                    DrawLine(rx1 - 8, ryBot, rMidX, aApexY, SteelMemberBrush, 2.8);
                    DrawLine(rMidX, aApexY, rMidX, ryTop, SteelSubBrush, 1.8);
                    for (int k = 1; k <= 3; k++)
                    {
                        double t = k / 4.0;
                        double yk = ryBot - (ryBot - aApexY) * t;
                        double dx = (rx1 - rx0 - 16) * 0.5 * (1.0 - t);
                        DrawLine(rMidX - dx, yk, rMidX + dx, yk, SteelSubBrush, 1.5);
                    }
                    DrawHorizDim(rx0 + 8, rx1 - 8, ryBot + 14, $"A柱根开 L = {p.Length:0}", w);
                    break;
                }

                // ==================== 24. 电力电缆沟多层支架 ====================
                case ParametricStructureType.CableTrenchSupport:
                {
                    int tiers = Math.Max(2, Math.Min(6, p.BayCount));
                    leftTitle = $"横断面 ({tiers}层悬臂托臂)";
                    rightTitle = "电缆沟纵立面 (总长 L)";
                    double postX = lx0 + 26;
                    DrawWallHatch(postX, lyTop, lyBot);
                    DrawLine(postX, lyBot, postX, lyTop + 6, SteelMemberBrush, 3.6);
                    for (int t = 0; t < tiers; t++)
                    {
                        double ay = lyTop + 18 + (lyBot - lyTop - 36) * t / Math.Max(1, tiers - 1);
                        DrawLine(postX, ay, lx1 - 14, ay, SteelSubBrush, 2.6);
                        DrawLine(postX, ay + 8, postX + 22, ay, SteelSubBrush, 1.6);
                    }
                    DrawHorizDim(postX, lx1 - 14, lyBot + 18, $"托臂挑宽 W = {p.Length:0} ({tiers}层)", w);
                    DrawVertDim(postX - 28, lyBot, lyTop + 6, $"H={p.Height:0}", h);

                    // 右：纵向多榀立柱与接地扁钢
                    for (int c = 0; c < 4; c++)
                    {
                        double cx = rx0 + (rx1 - rx0) * c / 3.0;
                        DrawLine(cx, ryBot, cx, ryTop + 8, SteelMemberBrush, 2.4);
                    }
                    DrawLine(rx0, ryBot - 8, rx1, ryBot - 8, PlateBrush, 2.6);
                    DrawHorizDim(rx0, rx1, ryBot + 14, $"沟长 L={p.Span:0} 层距 s={p.SubHeight:0}", w);
                    break;
                }

                // ==================== 25. 冶金厂房重型吊车梁系统 ====================
                case ParametricStructureType.CraneRunwayGirder:
                {
                    leftTitle = "正立面 (焊接吊车梁 & 加劲肋)";
                    rightTitle = "横断面 (QU轨 & 制动板)";
                    double gTop = lMidY - 22, gBot = lMidY + 22;
                    DrawRect(lx0, gTop, lx1 - lx0, gBot - gTop, SteelMemberBrush, 2.6);
                    DrawLine(lx0 - 4, gTop, lx1 + 4, gTop, SteelMemberBrush, 3.8);
                    DrawLine(lx0 - 4, gBot, lx1 + 4, gBot, SteelMemberBrush, 3.8);
                    DrawLine(lx0, gTop - 6, lx1, gTop - 6, SteelSubBrush, 2.2);
                    for (int i = 0; i <= 5; i++)
                    {
                        double sx = lx0 + (lx1 - lx0) * i / 5.0;
                        DrawLine(sx, gTop, sx, gBot, SteelSubBrush, 1.6);
                    }
                    DrawFilledRect(lx1 - 14, gTop - 16, 12, 16, PlateBrush, SteelMemberBrush, 1.2);
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"跨度 L = {p.Span:0} (肋距 {p.SubHeight:0})", w);
                    DrawVertDim(lx0 - 26, gBot, gTop, $"H={p.Height:0}", h);

                    // 右：工字梁 + 钢轨 + 走道制动板横断面
                    double webX = rx0 + 22;
                    DrawLine(webX - 18, ryTop + 18, webX + 18, ryTop + 18, SteelMemberBrush, 3.6);
                    DrawLine(webX - 16, ryBot - 10, webX + 16, ryBot - 10, SteelMemberBrush, 3.6);
                    DrawLine(webX, ryTop + 18, webX, ryBot - 10, SteelMemberBrush, 2.8);
                    DrawFilledRect(webX - 6, ryTop + 8, 12, 10, SteelSubBrush, SteelSubBrush, 1.4);
                    // 制动走道板与边梁
                    DrawLine(webX + 18, ryTop + 18, rx1 - 8, ryTop + 18, SteelSubBrush, 2.6);
                    DrawLine(rx1 - 8, ryTop + 18, rx1 - 8, ryTop + 30, SteelSubBrush, 2.4);
                    DrawHorizDim(webX, rx1 - 8, ryBot + 14, $"制动宽 B = {p.Length:0}", w);
                    break;
                }

                // ==================== 26. 冶金厂房柱间交叉支撑 ====================
                case ParametricStructureType.CrossBracingFrame:
                {
                    leftTitle = "正立面 (上下柱双层十字支撑)";
                    rightTitle = "中央十字节点板";
                    double corbelY = lMidY - 4;
                    DrawLine(lx0, lyBot, lx0, lyTop, SteelMemberBrush, 3.4);
                    DrawLine(lx1, lyBot, lx1, lyTop, SteelMemberBrush, 3.4);
                    DrawLine(lx0, lyTop, lx1, lyTop, SteelMemberBrush, 2.6);
                    DrawLine(lx0, corbelY, lx1, corbelY, SteelMemberBrush, 2.6);
                    // 下柱交叉支撑 + 节点板
                    DrawLine(lx0, lyBot, lx1, corbelY, SteelSubBrush, 2.2);
                    DrawLine(lx1, lyBot, lx0, corbelY, SteelSubBrush, 2.2);
                    DrawFilledRect(lMidX - 7, (lyBot + corbelY) * 0.5 - 7, 14, 14, PlateBrush, SteelSubBrush, 1.2);
                    // 上柱交叉支撑 + 节点板
                    DrawLine(lx0, corbelY, lx1, lyTop, SteelSubBrush, 1.8);
                    DrawLine(lx1, corbelY, lx0, lyTop, SteelSubBrush, 1.8);
                    DrawFilledRect(lMidX - 6, (corbelY + lyTop) * 0.5 - 6, 12, 12, PlateBrush, SteelSubBrush, 1.2);
                    DrawBaseSupport(lx0, lyBot);
                    DrawBaseSupport(lx1, lyBot);
                    DrawHorizDim(lx0, lx1, lyBot + 18, $"厂房柱距 L = {p.Span:0}", w);
                    DrawVertDim(lx0 - 26, lyBot, lyTop, $"H={p.Height:0}", h);
                    DrawVertDim(lx1 + 16, lyBot, corbelY, $"h1={p.SubHeight:0}", h);

                    // 右：十字交叉节点板详图
                    DrawFilledRect(rMidX - 24, rMidY - 24, 48, 48, PlateBrush, SteelMemberBrush, 1.8);
                    DrawLine(rx0 + 8, ryBot - 8, rx1 - 8, ryTop + 8, SteelSubBrush, 2.8);
                    DrawLine(rx0 + 8, ryTop + 8, rx1 - 8, ryBot - 8, SteelSubBrush, 2.8);
                    DrawTagBadge(rMidX, ryBot + 10, $"撑杆: {p.BraceSection}");
                    break;
                }
            }

            // 顶部视图标题条
            AddTitleText(10, 5, leftTitle);
            AddTitleText(rxBox0 + 8, ryBox0 + 4, rightTitle);
        }

        private void AddTitleText(double x, double y, string text)
        {
            TextBlock tb = new TextBlock
            {
                Text = text,
                FontSize = 11.0,
                Foreground = TitleBrush,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            _canvas.Children.Add(tb);
        }

        private void DrawTagBadge(double centerX, double topY, string text)
        {
            Border badge = new Border
            {
                Background = BgBrush,
                BorderBrush = BorderLineBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 10.5,
                    Foreground = TitleBrush,
                    FontWeight = FontWeights.SemiBold
                }
            };
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double bw = badge.DesiredSize.Width;
            Canvas.SetLeft(badge, centerX - bw * 0.5);
            Canvas.SetTop(badge, topY);
            _canvas.Children.Add(badge);
        }

        private void DrawHColumnPlanSymbol(double cx, double cy, double w, double h)
        {
            double hw = w * 0.5, hh = h * 0.5;
            DrawLine(cx - hw, cy - hh, cx + hw, cy - hh, SteelMemberBrush, 2.4);
            DrawLine(cx - hw, cy + hh, cx + hw, cy + hh, SteelMemberBrush, 2.4);
            DrawLine(cx, cy - hh, cx, cy + hh, SteelMemberBrush, 2.0);
        }

        private void DrawWallHatch(double wallX, double yTop, double yBot)
        {
            DrawLine(wallX, yTop, wallX, yBot, PlateBrush, 2.0);
            for (double y = yTop + 6; y <= yBot; y += 10)
            {
                DrawLine(wallX, y, wallX - 7, y + 6, BorderLineBrush, 1.2);
            }
        }

        private void DrawCircleArc(double cx, double cy, double r, double startDeg, double sweepDeg, Brush stroke, double thickness)
        {
            int segs = Math.Max(12, (int)Math.Ceiling(Math.Abs(sweepDeg) / 10.0));
            double a0 = startDeg * Math.PI / 180.0;
            double px0 = cx + r * Math.Cos(a0);
            double py0 = cy - r * Math.Sin(a0);
            for (int i = 1; i <= segs; i++)
            {
                double a = (startDeg + sweepDeg * i / segs) * Math.PI / 180.0;
                double px = cx + r * Math.Cos(a);
                double py = cy - r * Math.Sin(a);
                DrawLine(px0, py0, px, py, stroke, thickness);
                px0 = px;
                py0 = py;
            }
        }

        private void DrawDashAxis(double x1, double y1, double x2, double y2)
        {
            DrawLine(x1, y1, x2, y2, AxisLineBrush, 1.0, dashed: true);
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

        private void DrawLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness, bool dashed = false)
        {
            Line ln = new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = stroke,
                StrokeThickness = thickness
            };
            if (dashed)
            {
                ln.StrokeDashArray = new DoubleCollection { 4, 2 };
            }
            _canvas.Children.Add(ln);
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

        private void DrawFilledRect(double x, double y, double w, double h, Brush fill, Brush stroke, double thickness)
        {
            Rectangle r = new Rectangle
            {
                Width = Math.Max(2, w),
                Height = Math.Max(2, h),
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness,
                RadiusX = 3,
                RadiusY = 3
            };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            _canvas.Children.Add(r);
        }

        private void DrawBaseSupport(double x, double y)
        {
            DrawLine(x - 10, y, x + 10, y, PlateBrush, 3.2);
        }

        private void DrawHorizDim(double x1, double x2, double y, string text, double canvasW)
        {
            DrawLine(x1, y, x2, y, DimLineBrush, 1.3);
            DrawLine(x1, y - 4, x1, y + 4, DimLineBrush, 1.3);
            DrawLine(x2, y - 4, x2, y + 4, DimLineBrush, 1.3);

            Border label = new Border
            {
                Background = BgBrush,
                Padding = new Thickness(4, 0, 4, 0),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = DimTextBrush,
                    FontSize = 11.0,
                    FontWeight = FontWeights.Bold
                }
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double lw = label.DesiredSize.Width;
            double lh = label.DesiredSize.Height;

            double left = (x1 + x2) * 0.5 - lw * 0.5;
            left = Math.Max(4, Math.Min(canvasW - lw - 4, left));
            Canvas.SetLeft(label, left);
            Canvas.SetTop(label, y - lh * 0.5);
            _canvas.Children.Add(label);
        }

        private void DrawVertDim(double x, double yBot, double yTop, string text, double canvasH)
        {
            DrawLine(x, yBot, x, yTop, DimLineBrush, 1.3);
            DrawLine(x - 4, yBot, x + 4, yBot, DimLineBrush, 1.3);
            DrawLine(x - 4, yTop, x + 4, yTop, DimLineBrush, 1.3);

            Border label = new Border
            {
                Background = BgBrush,
                Padding = new Thickness(2, 0, 2, 0),
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

            double left = Math.Max(2, x - lw * 0.5);
            double top = (yBot + yTop) * 0.5 - lh * 0.5;
            top = Math.Max(18, Math.Min(canvasH - lh - 4, top));
            Canvas.SetLeft(label, left);
            Canvas.SetTop(label, top);
            _canvas.Children.Add(label);
        }
    }
}
