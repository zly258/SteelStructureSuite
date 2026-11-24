using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Views.Controls
{
    /// <summary>
    /// 2D 截面实时轮廓预览控件（随表单参数实时渲染精确截面几何与外包络尺寸标注）
    /// 坐标系约定：几何坐标 X=水平宽(B)，Y=竖直高(H，向上为正)
    /// </summary>
    public class SectionPreviewControl : FrameworkElement
    {
        public static readonly DependencyProperty ProfileProperty =
            DependencyProperty.Register(
                nameof(Profile),
                typeof(SectionProfile),
                typeof(SectionPreviewControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public SectionProfile Profile
        {
            get => (SectionProfile)GetValue(ProfileProperty);
            set => SetValue(ProfileProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 40 || h < 40) return;

            Brush bgBrush = TryFindResource("Brush.SurfaceSunken") as Brush ?? Brushes.WhiteSmoke;
            Brush borderBrush = TryFindResource("Brush.Border") as Brush ?? Brushes.LightGray;
            Brush fillBrush = TryFindResource("Brush.Accent.Subtle") as Brush ?? new SolidColorBrush(Color.FromRgb(228, 238, 245));
            Brush strokeBrush = TryFindResource("Brush.Accent") as Brush ?? new SolidColorBrush(Color.FromRgb(47, 111, 159));
            Brush textBrush = TryFindResource("Brush.Text.Secondary") as Brush ?? Brushes.DimGray;
            Brush axisBrush = TryFindResource("Brush.Border.Strong") as Brush ?? Brushes.Silver;

            dc.DrawRectangle(bgBrush, new Pen(borderBrush, 1.0), new Rect(0.5, 0.5, w - 1, h - 1));

            SectionProfile sec = Profile;
            if (sec == null) return;

            // 留出四周边距用于尺寸标注
            double padLeft = 24;
            double padRight = 14;
            double padTop = 14;
            double padBottom = 22;
            double drawW = Math.Max(20, w - padLeft - padRight);
            double drawH = Math.Max(20, h - padTop - padBottom);
            double cx = padLeft + drawW * 0.5;
            double cy = padTop + drawH * 0.5;

            // 绘制淡色中心十字基准线
            Pen axisPen = new Pen(axisBrush, 0.8) { DashStyle = DashStyles.Dash };
            dc.DrawLine(axisPen, new Point(padLeft, cy), new Point(w - padRight, cy));
            dc.DrawLine(axisPen, new Point(cx, padTop), new Point(cx, h - padBottom));

            double secH = Math.Max(4.0, sec.H);
            double secB = GetEffectiveWidth(sec);
            double secTw = Math.Max(1.0, sec.Tw > 0 ? sec.Tw : 4.0);
            double secTf = Math.Max(1.0, sec.Tf > 0 ? sec.Tf : secTw);

            // 计算外包络尺寸
            double envW = secB;
            double envH = secH;
            if (sec.Shape == SectionShapeType.WeldedHBeamUnequal)
            {
                envW = Math.Max(secB, sec.B2 > 0 ? sec.B2 : secB);
            }
            else if (sec.Shape == SectionShapeType.WeldedCross)
            {
                envW = secH;
            }
            else if (sec.Shape == SectionShapeType.DoubleAngle || sec.Shape == SectionShapeType.DoubleChannel)
            {
                envW = 2.0 * secB + Math.Max(0.0, sec.Gap);
            }
            else if (sec.Shape == SectionShapeType.HatSection)
            {
                envW = secB + 2.0 * Math.Max(0.0, sec.Lip > 0 ? sec.Lip : 20.0);
            }
            else if (sec.Shape == SectionShapeType.ZBeam || sec.Shape == SectionShapeType.ColdFormedZ)
            {
                envW = Math.Max(secB, 2.0 * secB - secTw);
            }

            double scale = Math.Min(drawW / Math.Max(1.0, envW), drawH / Math.Max(1.0, envH)) * 0.86;
            Pen outlinePen = new Pen(strokeBrush, 1.5) { LineJoin = PenLineJoin.Miter };

            // 特殊圆形截面直接用 Ellipse 绘制（含圆管镂空）
            if (sec.Shape == SectionShapeType.PipeTube || sec.Shape == SectionShapeType.RoundBar)
            {
                double rOut = secH * 0.5 * scale;
                if (sec.Shape == SectionShapeType.PipeTube)
                {
                    double t = Math.Min(secTw, secH * 0.45);
                    double rIn = Math.Max(1.0, (secH * 0.5 - t) * scale);
                    CombinedGeometry ring = new CombinedGeometry(
                        GeometryCombineMode.Exclude,
                        new EllipseGeometry(new Point(cx, cy), rOut, rOut),
                        new EllipseGeometry(new Point(cx, cy), rIn, rIn));
                    dc.DrawGeometry(fillBrush, outlinePen, ring);
                }
                else
                {
                    dc.DrawEllipse(fillBrush, outlinePen, new Point(cx, cy), rOut, rOut);
                }
            }
            // 空心方管 / 矩形管 / 焊接箱型：绘制带内孔的回字形
            else if (sec.Shape == SectionShapeType.SquareTube ||
                     sec.Shape == SectionShapeType.RectTube ||
                     sec.Shape == SectionShapeType.WeldedBox)
            {
                double tw = Math.Min(secTw, secB * 0.42);
                double tf = sec.Shape == SectionShapeType.WeldedBox
                    ? Math.Min(secTf, secH * 0.42)
                    : Math.Min(secTw, secH * 0.42);

                double ow = secB * scale;
                double oh = secH * scale;
                double iw = Math.Max(2.0, (secB - 2.0 * tw) * scale);
                double ih = Math.Max(2.0, (secH - 2.0 * tf) * scale);

                CombinedGeometry box = new CombinedGeometry(
                    GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(cx - ow * 0.5, cy - oh * 0.5, ow, oh)),
                    new RectangleGeometry(new Rect(cx - iw * 0.5, cy - ih * 0.5, iw, ih)));
                dc.DrawGeometry(fillBrush, outlinePen, box);
            }
            else
            {
                // 通用多边形轮廓（含单轮廓与双拼轮廓）
                var polys = BuildPreviewPolygons(sec);
                foreach (var poly in polys)
                {
                    if (poly.Count < 3) continue;
                    StreamGeometry sg = new StreamGeometry();
                    using (StreamGeometryContext ctx = sg.Open())
                    {
                        ctx.BeginFigure(ToScreen(poly[0], cx, cy, scale), true, true);
                        for (int i = 1; i < poly.Count; i++)
                        {
                            ctx.LineTo(ToScreen(poly[i], cx, cy, scale), true, false);
                        }
                    }
                    sg.Freeze();
                    dc.DrawGeometry(fillBrush, outlinePen, sg);
                }
            }

            // 绘制底部宽度与左侧高度简要尺寸标注
            Typeface tfFont = new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            string hText = (sec.Shape == SectionShapeType.PipeTube || sec.Shape == SectionShapeType.RoundBar)
                ? $"D={secH:0.#}"
                : $"H={secH:0.#}";
            FormattedText ftH = new FormattedText(hText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tfFont, 10.5, textBrush, dpi);
            dc.PushTransform(new RotateTransform(-90, 11, cy));
            dc.DrawText(ftH, new Point(11 - ftH.Width * 0.5, cy - ftH.Height * 0.5));
            dc.Pop();

            string bText = (sec.Shape == SectionShapeType.PipeTube || sec.Shape == SectionShapeType.RoundBar)
                ? (sec.Shape == SectionShapeType.PipeTube ? $"t={secTw:0.#}" : "实心圆钢")
                : $"B={envW:0.#}";
            FormattedText ftB = new FormattedText(bText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tfFont, 10.5, textBrush, dpi);
            dc.DrawText(ftB, new Point(cx - ftB.Width * 0.5, h - padBottom + 3));
        }

        private static Point ToScreen(Point pt, double cx, double cy, double scale)
        {
            // pt.X 为水平宽方向，pt.Y 为竖直高方向（向上为正）
            return new Point(cx + pt.X * scale, cy - pt.Y * scale);
        }

        private static double GetEffectiveWidth(SectionProfile sec)
        {
            if (sec.Shape == SectionShapeType.SquareTube ||
                sec.Shape == SectionShapeType.SquareBar ||
                sec.Shape == SectionShapeType.PipeTube ||
                sec.Shape == SectionShapeType.RoundBar)
            {
                return Math.Max(4.0, sec.H);
            }
            return Math.Max(4.0, sec.B);
        }

        /// <summary>
        /// 构造 2D 预览坐标多边形（X=宽方向，Y=高方向，原点在截面中心）
        /// </summary>
        private static List<List<Point>> BuildPreviewPolygons(SectionProfile sec)
        {
            double h = Math.Max(4.0, sec.H);
            double b = GetEffectiveWidth(sec);
            double tw = Math.Max(1.0, Math.Min(sec.Tw > 0 ? sec.Tw : 4.0, Math.Min(h, b) * 0.42));
            double tf = Math.Max(1.0, Math.Min(sec.Tf > 0 ? sec.Tf : tw, h * 0.42));
            double hh = h * 0.5;
            double hb = b * 0.5;

            switch (sec.Shape)
            {
                case SectionShapeType.HBeam:
                case SectionShapeType.IBeam:
                case SectionShapeType.WeldedHBeam:
                {
                    double hw = tw * 0.5;
                    return Single(new List<Point>
                    {
                        new Point(-hb, -hh), new Point(hb, -hh),
                        new Point(hb, -hh + tf), new Point(hw, -hh + tf),
                        new Point(hw, hh - tf), new Point(hb, hh - tf),
                        new Point(hb, hh), new Point(-hb, hh),
                        new Point(-hb, hh - tf), new Point(-hw, hh - tf),
                        new Point(-hw, -hh + tf), new Point(-hb, -hh + tf)
                    });
                }

                case SectionShapeType.WeldedHBeamUnequal:
                {
                    double b2 = sec.B2 > 0 ? Math.Max(6.0, sec.B2) : b;
                    double tf2 = Math.Max(1.0, Math.Min(sec.Tf2 > 0 ? sec.Tf2 : tf, h * 0.42));
                    double hb1 = b * 0.5;
                    double hb2 = b2 * 0.5;
                    double hw = Math.Min(tw * 0.5, Math.Min(hb1, hb2) * 0.8);
                    return Single(new List<Point>
                    {
                        new Point(-hb2, -hh), new Point(hb2, -hh),
                        new Point(hb2, -hh + tf2), new Point(hw, -hh + tf2),
                        new Point(hw, hh - tf), new Point(hb1, hh - tf),
                        new Point(hb1, hh), new Point(-hb1, hh),
                        new Point(-hb1, hh - tf), new Point(-hw, hh - tf),
                        new Point(-hw, -hh + tf2), new Point(-hb2, -hh + tf2)
                    });
                }

                case SectionShapeType.TBeam:
                case SectionShapeType.WeldedTBeam:
                {
                    double hw = tw * 0.5;
                    return Single(new List<Point>
                    {
                        new Point(-hb, hh), new Point(hb, hh),
                        new Point(hb, hh - tf), new Point(hw, hh - tf),
                        new Point(hw, -hh), new Point(-hw, -hh),
                        new Point(-hw, hh - tf), new Point(-hb, hh - tf)
                    });
                }

                case SectionShapeType.WeldedCross:
                {
                    double hw = tw * 0.5;
                    double bf = Math.Min(b * 0.5, hh - tf - 1.0);
                    if (bf <= hw) bf = hw + 2.0;
                    return Single(new List<Point>
                    {
                        new Point(-bf, hh), new Point(bf, hh),
                        new Point(bf, hh - tf), new Point(hw, hh - tf),
                        new Point(hw, hw), new Point(hh - tf, hw),
                        new Point(hh - tf, bf), new Point(hh, bf),
                        new Point(hh, -bf), new Point(hh - tf, -bf),
                        new Point(hh - tf, -hw), new Point(hw, -hw),
                        new Point(hw, -hh + tf), new Point(bf, -hh + tf),
                        new Point(bf, -hh), new Point(-bf, -hh),
                        new Point(-bf, -hh + tf), new Point(-hw, -hh + tf),
                        new Point(-hw, -hw), new Point(-hh + tf, -hw),
                        new Point(-hh + tf, -bf), new Point(-hh, -bf),
                        new Point(-hh, bf), new Point(-hh + tf, bf),
                        new Point(-hh + tf, hw), new Point(-hw, hw),
                        new Point(-hw, hh - tf), new Point(-bf, hh - tf)
                    });
                }

                case SectionShapeType.Channel:
                case SectionShapeType.ColdFormedU:
                {
                    return Single(new List<Point>
                    {
                        new Point(-hb, -hh), new Point(hb, -hh),
                        new Point(hb, -hh + tf), new Point(-hb + tw, -hh + tf),
                        new Point(-hb + tw, hh - tf), new Point(hb, hh - tf),
                        new Point(hb, hh), new Point(-hb, hh)
                    });
                }

                case SectionShapeType.Angle:
                case SectionShapeType.UnequalAngle:
                {
                    return Single(new List<Point>
                    {
                        new Point(-hb, -hh), new Point(hb, -hh),
                        new Point(hb, -hh + tw), new Point(-hb + tw, -hh + tw),
                        new Point(-hb + tw, hh), new Point(-hb, hh)
                    });
                }

                case SectionShapeType.DoubleAngle:
                {
                    double hg = Math.Max(0.0, sec.Gap) * 0.5;
                    var right = new List<Point>
                    {
                        new Point(hg, -hh), new Point(hg + b, -hh),
                        new Point(hg + b, -hh + tw), new Point(hg + tw, -hh + tw),
                        new Point(hg + tw, hh), new Point(hg, hh)
                    };
                    var left = new List<Point>
                    {
                        new Point(-hg - b, -hh), new Point(-hg, -hh),
                        new Point(-hg, hh), new Point(-hg - tw, hh),
                        new Point(-hg - tw, -hh + tw), new Point(-hg - b, -hh + tw)
                    };
                    return new List<List<Point>> { left, right };
                }

                case SectionShapeType.DoubleChannel:
                {
                    double hg = Math.Max(0.0, sec.Gap) * 0.5;
                    var right = new List<Point>
                    {
                        new Point(hg, -hh), new Point(hg + b, -hh),
                        new Point(hg + b, -hh + tf), new Point(hg + tw, -hh + tf),
                        new Point(hg + tw, hh - tf), new Point(hg + b, hh - tf),
                        new Point(hg + b, hh), new Point(hg, hh)
                    };
                    var left = new List<Point>
                    {
                        new Point(-hg - b, -hh), new Point(-hg, -hh),
                        new Point(-hg, hh), new Point(-hg - b, hh),
                        new Point(-hg - b, hh - tf), new Point(-hg - tw, hh - tf),
                        new Point(-hg - tw, -hh + tf), new Point(-hg - b, -hh + tf)
                    };
                    return new List<List<Point>> { left, right };
                }

                case SectionShapeType.ColdFormedC:
                {
                    double t = Math.Max(1.0, Math.Min(tw, Math.Min(h, b) * 0.25));
                    double lip = Math.Max(t * 2.0, Math.Min(sec.Lip > 0 ? sec.Lip : 15.0, hh - t * 1.5));
                    return Single(new List<Point>
                    {
                        new Point(-hb, -hh), new Point(hb, -hh),
                        new Point(hb, -hh + lip), new Point(hb - t, -hh + lip),
                        new Point(hb - t, -hh + t), new Point(-hb + t, -hh + t),
                        new Point(-hb + t, hh - t), new Point(hb - t, hh - t),
                        new Point(hb - t, hh - lip), new Point(hb, hh - lip),
                        new Point(hb, hh), new Point(-hb, hh)
                    });
                }

                case SectionShapeType.ZBeam:
                {
                    double hw = tw * 0.5;
                    double fout = Math.Max(tw + 2.0, b);
                    return Single(new List<Point>
                    {
                        new Point(-fout + hw, -hh), new Point(hw, -hh),
                        new Point(hw, hh - tf), new Point(fout - hw, hh - tf),
                        new Point(fout - hw, hh), new Point(-hw, hh),
                        new Point(-hw, -hh + tf), new Point(-fout + hw, -hh + tf)
                    });
                }

                case SectionShapeType.ColdFormedZ:
                {
                    double t = Math.Max(1.0, Math.Min(tw, Math.Min(h, b) * 0.25));
                    double ht = t * 0.5;
                    double lip = Math.Max(t * 2.0, Math.Min(sec.Lip > 0 ? sec.Lip : 15.0, hh - t * 1.5));
                    double fout = Math.Max(t * 3.0, b);
                    return Single(new List<Point>
                    {
                        new Point(-fout + ht, -hh + lip), new Point(-fout + ht, -hh),
                        new Point(ht, -hh), new Point(ht, hh - t),
                        new Point(fout - ht - t, hh - t), new Point(fout - ht - t, hh - lip),
                        new Point(fout - ht, hh - lip), new Point(fout - ht, hh),
                        new Point(-ht, hh), new Point(-ht, -hh + t),
                        new Point(-fout + ht + t, -hh + t), new Point(-fout + ht + t, -hh + lip)
                    });
                }

                case SectionShapeType.HatSection:
                {
                    double t = Math.Max(1.0, Math.Min(tw, Math.Min(h, b) * 0.25));
                    double lip = Math.Max(t * 2.0, sec.Lip > 0 ? sec.Lip : 20.0);
                    double innerHb = Math.Max(t, hb - t);
                    return Single(new List<Point>
                    {
                        new Point(-hb - lip, -hh), new Point(-hb - lip, -hh + t),
                        new Point(-hb, -hh + t), new Point(-hb, hh),
                        new Point(hb, hh), new Point(hb, -hh + t),
                        new Point(hb + lip, -hh + t), new Point(hb + lip, -hh),
                        new Point(innerHb, -hh), new Point(innerHb, hh - t),
                        new Point(-innerHb, hh - t), new Point(-innerHb, -hh)
                    });
                }

                default:
                {
                    return Single(new List<Point>
                    {
                        new Point(-hb, -hh), new Point(hb, -hh),
                        new Point(hb, hh), new Point(-hb, hh)
                    });
                }
            }
        }

        private static List<List<Point>> Single(List<Point> pts) => new List<List<Point>> { pts };
    }
}
