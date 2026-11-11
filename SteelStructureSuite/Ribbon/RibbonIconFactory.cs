using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SteelStructureSuite.Ribbon
{
    /// <summary>
    /// 功能区图标。蓝底圆角块 + 白色单线外形。
    /// 先按 8 倍分辨率栅格化，再做像素平均缩小到 96 DPI 的 32×32 / 16×16，
    /// 避免把高 DPI 位图交给 AutoCAD 二次缩放后出现锯齿。
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
                BitmapSource created = Render(px, glyph, active);
                Cache[key] = created;
                return created;
            }
        }

        private static BitmapSource Render(int size, string glyph, bool active)
        {
            const int factor = 8;
            int hi = size * factor;
            double scale = hi / 32.0;
            var visual = new DrawingVisual();
            RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                var canvas = new IconCanvas(dc, size >= 24, active);
                DrawGlyph(canvas, glyph ?? string.Empty);
                canvas.Finish();
                dc.Pop();
            }

            var hiBmp = new RenderTargetBitmap(hi, hi, 96, 96, PixelFormats.Pbgra32);
            hiBmp.Render(visual);
            hiBmp.Freeze();
            return Downsample(hiBmp, factor);
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

        private static void DrawGlyph(IconCanvas c, string key)
        {
            switch (key)
            {
                case "manager":
                    c.Poly(7.2, 14.0, 16.6, 14.0, 16.6, 25.6, 7.2, 25.6, 7.2, 14.0);
                    c.Poly(7.2, 14.0, 14.2, 7.8, 23.6, 7.8, 16.6, 14.0);
                    c.Poly(16.6, 14.0, 23.6, 7.8, 23.6, 19.4, 16.6, 25.6);
                    break;
                case "section":
                    c.Poly(5.2, 5.4, 26.8, 5.4, 26.8, 10.2, 18.8, 10.2, 18.8, 21.8, 26.8, 21.8, 26.8, 26.6, 5.2, 26.6, 5.2, 21.8, 13.2, 21.8, 13.2, 10.2, 5.2, 10.2, 5.2, 5.4);
                    break;
                case "chain":
                    c.Poly(5.0, 23.4, 12.2, 12.4, 20.0, 18.2, 26.8, 7.4);
                    c.Dot(5.0, 23.4, 1.75);
                    c.Dot(12.2, 12.4, 1.75);
                    c.Dot(20.0, 18.2, 1.75);
                    c.Dot(26.8, 7.4, 1.75);
                    break;
                case "edit":
                    c.Poly(19.4, 6.2, 25.2, 12.0, 14.0, 23.2, 8.2, 17.4, 19.4, 6.2);
                    c.Sharp(8.2, 17.4, 6.0, 25.2, 14.0, 23.2);
                    c.L(17.8, 8.8, 22.6, 13.6);
                    break;
                case "bom":
                    c.Box(5.2, 5.2, 26.8, 26.8);
                    c.L(5.2, 11.0, 26.8, 11.0);
                    c.L(5.2, 17.0, 26.8, 17.0);
                    c.L(5.2, 22.6, 26.8, 22.6);
                    c.L(12.4, 5.2, 12.4, 26.8);
                    break;
                case "drawing":
                    c.Box(5.4, 4.8, 26.6, 27.2);
                    c.Poly(9.4, 17.4, 9.4, 12.4, 16, 8.2, 22.6, 12.4, 22.6, 17.4);
                    c.L(8.6, 17.4, 23.4, 17.4);
                    c.L(9.2, 21.4, 23.2, 21.4);
                    c.L(9.2, 24.4, 17.6, 24.4);
                    break;
                case "help":
                    c.Arc(16, 12.4, 6.4, 6.0, 200, 250);
                    c.L(16, 18.4, 16, 21.8);
                    c.Dot(16, 24.8, 1.45);
                    break;
                case "about":
                    c.O(16, 16, 11.1, 11.1);
                    c.Dot(16, 10.3, 1.4);
                    c.L(16, 14.3, 16, 22.3);
                    break;
                case "portal":
                    c.Poly(7.4, 26.4, 7.4, 13.4, 16, 5.8, 24.6, 13.4, 24.6, 26.4);
                    c.L(4.6, 26.4, 10.2, 26.4);
                    c.L(21.8, 26.4, 27.4, 26.4);
                    break;
                case "multiportal":
                    c.L(5.6, 26.4, 5.6, 13.0);
                    c.L(16, 26.4, 16, 13.0);
                    c.L(26.4, 26.4, 26.4, 13.0);
                    c.Poly(4.2, 13.0, 16, 5.4, 27.8, 13.0);
                    c.L(4.2, 26.4, 8.0, 26.4);
                    c.L(13.6, 26.4, 18.4, 26.4);
                    c.L(24.0, 26.4, 27.8, 26.4);
                    break;
                case "braced":
                    c.Poly(7.4, 26.4, 7.4, 13.4, 16, 5.8, 24.6, 13.4, 24.6, 26.4);
                    c.L(7.4, 22.2, 16, 13.4);
                    c.L(24.6, 22.2, 16, 13.4);
                    c.L(4.6, 26.4, 10.2, 26.4);
                    c.L(21.8, 26.4, 27.4, 26.4);
                    break;
                case "double":
                    c.L(7.2, 26.4, 7.2, 9.2);
                    c.L(24.8, 26.4, 24.8, 9.2);
                    c.L(4.6, 9.2, 27.4, 9.2);
                    c.L(4.6, 16.8, 27.4, 16.8);
                    c.O(12.4, 7.45, 2.55, 1.75);
                    c.O(19.6, 7.45, 2.55, 1.75);
                    c.L(4.6, 26.4, 9.8, 26.4);
                    c.L(22.2, 26.4, 27.4, 26.4);
                    break;
                case "space":
                    c.Poly(7.8, 9.2, 16.6, 9.2, 16.6, 26.2, 7.8, 26.2, 7.8, 9.2);
                    c.Poly(7.8, 9.2, 13.0, 5.2, 21.8, 5.2, 16.6, 9.2);
                    c.Poly(16.6, 9.2, 21.8, 5.2, 21.8, 22.2, 16.6, 26.2);
                    c.L(7.8, 14.8, 16.6, 14.8);
                    c.L(16.6, 14.8, 21.8, 10.8);
                    c.L(7.8, 20.4, 16.6, 20.4);
                    c.L(16.6, 20.4, 21.8, 16.4);
                    c.L(12.2, 9.2, 12.2, 26.2);
                    break;
                case "truss":
                    c.Poly(4.4, 25.2, 16, 5.6, 27.6, 25.2, 4.4, 25.2);
                    c.L(10.2, 25.2, 16, 15.4);
                    c.L(21.8, 25.2, 16, 15.4);
                    c.L(10.6, 17.6, 21.4, 17.6);
                    break;
                case "platform":
                    c.L(5.4, 7.2, 26.6, 7.2);
                    c.L(8.2, 7.2, 8.2, 13.0);
                    c.L(23.8, 7.2, 23.8, 13.0);
                    c.L(4.6, 13.0, 27.4, 13.0);
                    c.L(4.6, 16.2, 27.4, 16.2);
                    c.L(8.2, 16.2, 8.2, 26.4);
                    c.L(23.8, 16.2, 23.8, 26.4);
                    break;
                case "bracket":
                    c.L(7.0, 4.8, 7.0, 26.6);
                    c.L(4.4, 26.6, 10.6, 26.6);
                    c.L(7.0, 8.4, 26.8, 8.4);
                    c.L(7.0, 23.6, 26.8, 8.4);
                    break;
                case "walkway":
                    c.L(5.0, 7.0, 26.8, 7.0);
                    c.L(8.0, 7.0, 8.0, 12.6);
                    c.L(4.6, 12.6, 27.4, 12.6);
                    c.L(4.6, 15.8, 27.4, 15.8);
                    c.L(8.0, 15.8, 8.0, 26.4);
                    c.L(8.0, 23.4, 24.2, 15.8);
                    break;
                case "bridge":
                    c.L(4.2, 20.2, 27.8, 20.2);
                    c.Poly(4.2, 20.2, 9.0, 8.0, 23.0, 8.0, 27.8, 20.2);
                    c.L(9.0, 8.0, 9.0, 20.2);
                    c.L(16, 8.0, 16, 20.2);
                    c.L(23.0, 8.0, 23.0, 20.2);
                    break;
                case "stair":
                    c.Poly(4.2, 26.2, 11.0, 26.2, 11.0, 19.4, 18.2, 19.4, 18.2, 12.6, 25.4, 12.6);
                    c.Poly(7.6, 21.2, 14.8, 14.4, 26.4, 6.4);
                    break;
                case "ladder":
                    c.L(9.0, 4.8, 9.0, 27.0);
                    c.L(17.0, 4.8, 17.0, 27.0);
                    c.L(9.0, 8.6, 17.0, 8.6);
                    c.L(9.0, 14.0, 17.0, 14.0);
                    c.L(9.0, 19.4, 17.0, 19.4);
                    c.L(9.0, 24.8, 17.0, 24.8);
                    c.Arc(17.0, 16.2, 8.2, 9.2, -90, 180);
                    break;
                case "spiral":
                    c.Dot(16, 16.4, 1.65);
                    c.Arc(16, 16.4, 5.4, 5.4, 130, 270);
                    c.Arc(16, 16.4, 9.5, 9.5, 50, 280);
                    c.L(19.5, 12.3, 22.1, 10.3);
                    break;
                case "rail":
                    c.L(4.8, 8.0, 27.2, 8.0);
                    c.L(4.8, 14.4, 27.2, 14.4);
                    c.L(8.0, 8.0, 8.0, 26.4);
                    c.L(16, 8.0, 16, 26.4);
                    c.L(24.0, 8.0, 24.0, 26.4);
                    break;
                case "tower":
                    c.L(13.4, 4.8, 13.4, 26.6);
                    c.L(18.6, 4.8, 18.6, 26.6);
                    c.L(13.4, 4.8, 18.6, 4.8);
                    c.L(4.6, 14.6, 27.4, 14.6);
                    c.L(4.6, 17.0, 27.4, 17.0);
                    c.L(13.4, 21.4, 6.2, 17.0);
                    c.L(18.6, 21.4, 25.8, 17.0);
                    c.L(6.6, 14.6, 6.6, 9.8);
                    c.L(25.4, 14.6, 25.4, 9.8);
                    c.L(5.2, 9.8, 26.8, 9.8);
                    break;
                case "skirt":
                    c.Poly(11.2, 8.2, 20.8, 8.2, 26.0, 26.4, 6.0, 26.4, 11.2, 8.2);
                    c.Box(13.2, 4.6, 18.8, 8.2);
                    c.L(8.8, 17.2, 23.2, 17.2);
                    c.O(16, 21.2, 2.25, 2.25);
                    break;
                case "shoe":
                    c.Box(4.8, 22.6, 27.2, 26.8);
                    c.L(16, 22.6, 16, 16.4);
                    c.L(10.4, 19.4, 21.6, 19.4);
                    c.O(16, 11.2, 5.0, 5.0);
                    break;
                case "tank":
                    c.L(8.8, 14.4, 8.8, 22.8);
                    c.L(23.2, 14.4, 23.2, 22.8);
                    c.O(16, 14.4, 7.2, 2.55);
                    c.O(16, 22.8, 7.2, 2.55);
                    c.L(10.4, 13.4, 10.4, 7.2);
                    c.L(16, 12.6, 16, 7.2);
                    c.L(21.6, 13.4, 21.6, 7.2);
                    c.L(8.6, 7.2, 23.4, 7.2);
                    break;
                case "saddle":
                    c.O(16, 8.8, 6.4, 2.6);
                    c.Arc(16, 10.6, 8.8, 4.6, 180, -180);
                    c.L(7.2, 10.6, 5.6, 22.2);
                    c.L(24.8, 10.6, 26.4, 22.2);
                    c.L(16, 15.2, 16, 22.2);
                    c.Box(4.4, 22.2, 27.6, 26.6);
                    break;
                case "gantry":
                    c.Poly(5.8, 26.4, 16, 6.6, 26.2, 26.4);
                    c.L(9.6, 16.0, 22.4, 16.0);
                    c.L(8.0, 20.4, 24.0, 20.4);
                    c.L(16, 6.6, 16, 4.0);
                    break;
                case "cable":
                    c.L(8.0, 5.0, 8.0, 24.0);
                    c.L(4.6, 26.6, 12.8, 26.6);
                    c.L(8.0, 24.0, 8.0, 26.6);
                    c.L(8.0, 8.2, 26.2, 8.2);
                    c.L(26.2, 8.2, 26.2, 10.8);
                    c.L(8.0, 14.6, 21.4, 14.6);
                    c.L(21.4, 14.6, 21.4, 17.0);
                    c.L(8.0, 20.8, 16.6, 20.8);
                    c.L(16.6, 20.8, 16.6, 23.0);
                    break;
                case "crane":
                    c.L(4.2, 8.2, 27.8, 8.2);
                    c.O(12.0, 8.2, 1.55, 1.55);
                    c.O(20.0, 8.2, 1.55, 1.55);
                    c.L(8.6, 5.0, 8.6, 8.2);
                    c.L(23.4, 5.0, 23.4, 8.2);
                    c.L(4.2, 11.4, 27.8, 11.4);
                    c.L(4.2, 20.6, 27.8, 20.6);
                    c.L(4.2, 11.4, 4.2, 20.6);
                    c.L(27.8, 11.4, 27.8, 20.6);
                    c.L(12.0, 11.4, 12.0, 20.6);
                    c.L(20.0, 11.4, 20.0, 20.6);
                    break;
                case "xbrace":
                    c.L(5.6, 4.6, 5.6, 26.6);
                    c.L(26.4, 4.6, 26.4, 26.6);
                    c.L(5.6, 6.2, 26.4, 6.2);
                    c.L(5.6, 16.0, 26.4, 16.0);
                    c.L(5.6, 7.6, 26.4, 25.2);
                    c.L(26.4, 7.6, 5.6, 25.2);
                    break;
                case "member":
                    c.L(4.8, 19.4, 25.0, 10.6);
                    c.L(7.6, 23.8, 27.8, 15.0);
                    c.L(4.8, 19.4, 7.6, 23.8);
                    c.L(25.0, 10.6, 27.8, 15.0);
                    break;
                case "arc":
                    c.Arc(16, 28.6, 12.0, 13.8, 208, 124);
                    c.Arc(16, 28.6, 12.0, 9.2, 208, 124);
                    c.L(5.40, 21.6, 5.40, 24.8);
                    c.L(26.60, 21.6, 26.60, 24.8);
                    break;
                case "helix":
                    c.Arc(16, 23.2, 9.0, 3.35, 205, 310);
                    c.Arc(16, 16.0, 9.0, 3.35, 25, 310);
                    c.Arc(16, 8.8, 9.0, 3.35, 205, 250);
                    break;
                case "align":
                    c.L(6.2, 4.8, 6.2, 27.0);
                    c.L(6.2, 8.6, 26.4, 8.6);
                    c.L(6.2, 16.0, 20.2, 16.0);
                    c.L(6.2, 23.2, 14.6, 23.2);
                    break;
                case "array":
                    c.Box(4.4, 16.0, 14.6, 26.2);
                    c.Box(11.0, 11.0, 21.2, 21.2);
                    c.Box(17.6, 6.0, 27.8, 16.2);
                    break;
                case "drop":
                    c.L(16, 4.8, 16, 16.0);
                    c.Sharp(8.8, 14.0, 16, 22.4, 23.2, 14.0);
                    c.L(5.2, 26.6, 26.8, 26.6);
                    break;
                case "gripon":
                    c.Box(7.4, 7.4, 24.6, 24.6);
                    c.Poly(10.2, 16.2, 14.4, 20.8, 22.2, 11.2);
                    break;
                case "gripoff":
                    c.Box(7.4, 7.4, 24.6, 24.6);
                    c.L(9.6, 9.6, 22.4, 22.4);
                    break;
                case "solid":
                    c.Poly(8.0, 13.2, 16.4, 8.6, 25.2, 13.4, 16.8, 18.0, 8.0, 13.2);
                    c.L(8.0, 13.2, 8.0, 22.6);
                    c.L(16.8, 18.0, 16.8, 27.4);
                    c.L(25.2, 13.4, 25.2, 22.8);
                    c.Poly(8.0, 22.6, 16.8, 27.4, 25.2, 22.8);
                    break;
                case "wire":
                    c.Poly(8.0, 13.6, 16.4, 9.0, 25.2, 13.8, 16.8, 18.4, 8.0, 13.6);
                    c.L(8.0, 13.6, 8.0, 23.0);
                    c.L(16.8, 18.4, 16.8, 27.8);
                    c.L(25.2, 13.8, 25.2, 23.2);
                    c.Poly(8.0, 23.0, 16.8, 27.8, 25.2, 23.2);
                    c.L(16.4, 9.0, 16.8, 18.4);
                    break;
                case "dxf":
                    c.Poly(5.6, 5.4, 17.2, 5.4, 17.2, 11.4, 24.8, 11.4, 24.8, 26.6, 5.6, 26.6, 5.6, 5.4);
                    c.Poly(17.2, 5.4, 17.2, 11.4, 24.8, 11.4);
                    c.L(9.2, 18.6, 16.4, 18.6);
                    c.Head(21.2, 18.6, 0, 4.8);
                    break;
                case "merge":
                    c.Box(4.2, 8.4, 13.4, 23.6);
                    c.Box(18.6, 8.4, 27.8, 23.6);
                    c.L(13.4, 16.0, 18.6, 16.0);
                    c.L(16.0, 13.6, 16.0, 18.4);
                    break;
                case "regen":
                    c.Arc(16, 16.2, 8.3, 8.3, -30, 300);
                    c.Head(18.4, 7.9, 0, 5.4);
                    break;
                default:
                    c.L(5, 16, 27, 16);
                    c.L(5, 11, 5, 21);
                    c.L(27, 11, 27, 21);
                    break;
            }
        }

        private sealed class IconCanvas
        {
            private readonly DrawingContext _dc;
            private readonly bool _large;
            private readonly bool _active;
            private readonly Pen _ink;
            private readonly Pen _miter;
            private readonly Brush _white;
            private readonly List<Action> _ops = new List<Action>();

            public IconCanvas(DrawingContext dc, bool large, bool active)
            {
                _dc = dc;
                _large = large;
                _active = active;
                _white = Freeze(new SolidColorBrush(Colors.White));
                var blue = Freeze(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)));
                const double inset = 0.85;
                _dc.DrawRoundedRectangle(
                    blue,
                    null,
                    new Rect(inset, inset, 32 - 2 * inset, 32 - 2 * inset),
                    5.2,
                    5.2);
                double thickness = large ? 2.15 : 3.35;
                _ink = MakePen(thickness, PenLineJoin.Round);
                _miter = MakePen(thickness, PenLineJoin.Miter);
            }

            public void L(double x1, double y1, double x2, double y2)
            {
                Poly(x1, y1, x2, y2);
            }

            public void Box(double x1, double y1, double x2, double y2)
            {
                Poly(x1, y1, x2, y1, x2, y2, x1, y2, x1, y1);
            }

            public void Poly(params double[] v)
            {
                Stroke(_ink, v);
            }

            public void Sharp(params double[] v)
            {
                Stroke(_miter, v);
            }

            public void Dot(double cx, double cy, double r)
            {
                double rr = _large ? r : r * 1.2;
                _ops.Add(() => _dc.DrawEllipse(_white, null, new Point(cx, cy), rr, rr));
            }

            public void O(double cx, double cy, double rx, double ry)
            {
                _ops.Add(() => _dc.DrawEllipse(null, _ink, new Point(cx, cy), rx, ry));
            }

            public void Arc(double cx, double cy, double rx, double ry, double startDeg, double sweepDeg)
            {
                if (Math.Abs(sweepDeg) < 1) return;
                double start = startDeg * Math.PI / 180.0;
                double end = (startDeg + sweepDeg) * Math.PI / 180.0;
                var a = new Point(cx + rx * Math.Cos(start), cy + ry * Math.Sin(start));
                var b = new Point(cx + rx * Math.Cos(end), cy + ry * Math.Sin(end));
                var geometry = new StreamGeometry();
                using (StreamGeometryContext ctx = geometry.Open())
                {
                    ctx.BeginFigure(a, false, false);
                    ctx.ArcTo(
                        b,
                        new Size(Math.Abs(rx), Math.Abs(ry)),
                        0,
                        Math.Abs(sweepDeg) > 180,
                        sweepDeg >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                        true,
                        false);
                }

                if (geometry.CanFreeze) geometry.Freeze();
                _ops.Add(() => _dc.DrawGeometry(null, _ink, geometry));
            }

            public void Head(double tipX, double tipY, double dirDeg, double length)
            {
                double a = dirDeg * Math.PI / 180.0;
                double dx = Math.Cos(a);
                double dy = Math.Sin(a);
                double wing = length * 0.58;
                Sharp(
                    tipX - dx * length - dy * wing, tipY - dy * length + dx * wing,
                    tipX, tipY,
                    tipX - dx * length + dy * wing, tipY - dy * length - dx * wing);
            }

            public void Finish()
            {
                foreach (Action op in _ops) op();
                if (_active)
                {
                    double r = _large ? 1.55 : 2.05;
                    _dc.DrawEllipse(_white, null, new Point(26.3, 5.7), r, r);
                }
            }

            private void Stroke(Pen pen, double[] v)
            {
                if (v == null || v.Length < 4) return;
                bool closed = v.Length >= 6
                              && Math.Abs(v[0] - v[v.Length - 2]) < 0.05
                              && Math.Abs(v[1] - v[v.Length - 1]) < 0.05;
                int end = closed ? v.Length - 2 : v.Length;
                var geometry = new StreamGeometry();
                using (StreamGeometryContext ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(v[0], v[1]), false, closed);
                    for (int i = 2; i + 1 < end; i += 2)
                        ctx.LineTo(new Point(v[i], v[i + 1]), true, false);
                }

                if (geometry.CanFreeze) geometry.Freeze();
                _ops.Add(() => _dc.DrawGeometry(null, pen, geometry));
            }

            private static Pen MakePen(double thickness, PenLineJoin join)
            {
                var pen = new Pen(Freeze(new SolidColorBrush(Colors.White)), thickness)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = join,
                    MiterLimit = 2.4
                };
                if (pen.CanFreeze) pen.Freeze();
                return pen;
            }

            private static T Freeze<T>(T freezable) where T : Freezable
            {
                if (freezable.CanFreeze) freezable.Freeze();
                return freezable;
            }
        }
    }
}
