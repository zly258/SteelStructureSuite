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

        private static Color GetBadgeColor(string glyph)
        {
            switch ((glyph ?? string.Empty).Trim())
            {
                case "manager":
                case "renumber":
                case "platform":
                case "bracket":
                case "walkway":
                case "bridge":
                case "gripon":
                    return Color.FromRgb(16, 124, 65);

                case "chain":
                case "stair":
                case "ladder":
                case "spiral":
                case "rail":
                case "crane":
                case "xbrace":
                case "bom":
                    return Color.FromRgb(196, 89, 17);

                case "tower":
                case "skirt":
                case "shoe":
                case "tank":
                case "saddle":
                case "edit":
                case "drawing":
                    return Color.FromRgb(138, 43, 226);

                case "gripoff":
                    return Color.FromRgb(180, 50, 50);

                case "regen":
                case "dxf":
                case "merge":
                case "about":
                    return Color.FromRgb(95, 99, 104);

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
