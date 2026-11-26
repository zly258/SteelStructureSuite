using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SteelStructureSuite.Ribbon
{
    /// <summary>
    /// 功能区图标：恢复第一版分类色块徽标。
    /// 32×32 显示圆角色块 + 白色示意线，16×16 显示同色圆角块 + 白点。
    /// 按 8 倍分辨率栅格化后平均缩小，避免 AutoCAD 二次缩放产生锯齿。
    /// </summary>
    internal static class RibbonIconFactory
    {
        private static readonly Dictionary<string, BitmapSource> Cache =
            new Dictionary<string, BitmapSource>(StringComparer.Ordinal);

        public static BitmapSource Create(int size, string glyph, bool active = false)
        {
            int px = Math.Max(16, size);
            string key = px.ToString(System.Globalization.CultureInfo.InvariantCulture)
                         + ":" + (glyph ?? string.Empty)
                         + ":" + (active ? "1" : "0");
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out BitmapSource cached)) return cached;
                BitmapSource created = Render(px, glyph);
                Cache[key] = created;
                return created;
            }
        }

        private static BitmapSource Render(int size, string glyph)
        {
            const int factor = 8;
            int hi = size * factor;
            double scale = hi / (double)size;

            var visual = new DrawingVisual();
            RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));

                var fill = new SolidColorBrush(GetBadgeColor(glyph));
                fill.Freeze();
                var border = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1.0);
                border.Freeze();
                dc.DrawRoundedRectangle(fill, border, new Rect(1, 1, size - 2, size - 2), 4, 4);

                if (size >= 28)
                {
                    var whitePen = new Pen(Brushes.White, 2.2);
                    whitePen.Freeze();
                    dc.DrawLine(whitePen, new Point(7, 8), new Point(size - 7, 8));
                    dc.DrawLine(whitePen, new Point(size / 2.0, 8), new Point(size / 2.0, size - 8));
                    dc.DrawLine(whitePen, new Point(7, size - 8), new Point(size - 7, size - 8));
                }
                else
                {
                    dc.DrawEllipse(Brushes.White, null, new Point(size / 2.0, size / 2.0), 3.0, 3.0);
                }

                dc.Pop();
            }

            var hiBmp = new RenderTargetBitmap(hi, hi, 96, 96, PixelFormats.Pbgra32);
            hiBmp.Render(visual);
            hiBmp.Freeze();
            return Downsample(hiBmp, factor);
        }

        /// <summary>
        /// 按功能区面板类型统一配色（一个面板/大类一种颜色，视觉整洁统一）：
        ///   · 构建管理：深翠绿
        ///   · 构件模板：青碧蓝
        ///   · 参数化构件（6 大类 30 构件）：统一工程蓝
        ///   · 定位复写：琥珀橙
        ///   · 统计出图：靛雅紫
        ///   · 帮助：石板灰
        /// </summary>
        private static Color GetBadgeColor(string glyph)
        {
            switch ((glyph ?? string.Empty).Trim())
            {
                // 1. 构建管理
                case "manager":
                case "section":
                case "renumber":
                case "edit":
                case "regen":
                    return Color.FromRgb(16, 124, 65);

                // 2. 构件模板
                case "templ":
                case "tplsave":
                    return Color.FromRgb(14, 116, 144);

                // 4. 定位复写
                case "chain":
                case "align":
                case "array":
                case "drop":
                case "gripon":
                case "gripoff":
                    return Color.FromRgb(217, 119, 6);

                // 5. 统计出图
                case "bom":
                case "drawing":
                case "dxf":
                case "merge":
                case "settings":
                    return Color.FromRgb(109, 76, 159);

                // 6. 帮助
                case "help":
                case "ribbonmode":
                case "about":
                    return Color.FromRgb(82, 96, 109);

                // 3. 参数化构件（全部统一工程蓝）
                default:
                    return Color.FromRgb(0, 103, 192);
            }
        }

        private static BitmapSource Downsample(BitmapSource source, int factor)
        {
            int sw = source.PixelWidth;
            int sh = source.PixelHeight;
            int dw = sw / factor;
            int dh = sh / factor;
            var src = new byte[sw * sh * 4];
            source.CopyPixels(src, sw * 4, 0);
            var dst = new byte[dw * dh * 4];
            int area = factor * factor;
            for (int y = 0; y < dh; y++)
            {
                for (int x = 0; x < dw; x++)
                {
                    int b = 0, g = 0, r = 0, a = 0;
                    int y0 = y * factor;
                    int x0 = x * factor;
                    for (int oy = 0; oy < factor; oy++)
                    {
                        int row = (y0 + oy) * sw + x0;
                        for (int ox = 0; ox < factor; ox++)
                        {
                            int i = (row + ox) * 4;
                            b += src[i];
                            g += src[i + 1];
                            r += src[i + 2];
                            a += src[i + 3];
                        }
                    }

                    int o = (y * dw + x) * 4;
                    dst[o] = (byte)(b / area);
                    dst[o + 1] = (byte)(g / area);
                    dst[o + 2] = (byte)(r / area);
                    dst[o + 3] = (byte)(a / area);
                }
            }

            var bmp = new WriteableBitmap(dw, dh, 96, 96, PixelFormats.Pbgra32, null);
            bmp.WritePixels(new Int32Rect(0, 0, dw, dh), dst, dw * 4, 0);
            bmp.Freeze();
            return bmp;
        }
    }
}
