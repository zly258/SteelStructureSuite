using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Catalog;

namespace SteelStructureSuite.Services.Modeling
{
    /// <summary>
    /// 区域组合构件构建器：一次布置「一整片区域」而不是单根构件。
    ///
    /// 设计原则：
    ///   · 内部只产出 <see cref="SteelStructureBuilder.SolidMemberPart"/>，
    ///     因此材料表、详图、批量出图、接头斜接全部自动兼容；
    ///   · 复用与单构件相同的截面与构造习惯，尺寸参数用现有的
    ///     Span / Length / Height / SubHeight / BayCount，不新增字段；
    ///   · 每个区域构件都按「可调跨数 / 柱距 / 层数」参数化，
    ///     而不是写死固定数量。
    /// </summary>
    internal static class ZoneAssemblyBuilder
    {
        private const double JointOverlap = 22.0;

        // ==================================================================
        // 1. 钢平台成套：柱 + 主次梁 + 铺板 + 四周栏杆 + 单跑斜梯
        // ==================================================================
        /// <summary>
        /// 钢平台成套。
        /// Span = 平台长(X)，Length = 平台宽(Y)，Height = 平台标高，SubHeight = 护栏高度，BayCount = 柱距分格数。
        /// </summary>
        internal static void BuildPlatformAssembly(
            List<SteelStructureBuilder.SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            SectionProfile braceProf,
            double span,
            double len,
            double h,
            double railH,
            int bayCount)
        {
            if (parts == null) return;

            span = Math.Max(span, 1200.0);
            len = Math.Max(len, 1200.0);
            h = Math.Max(h, 800.0);
            railH = Math.Max(railH, 1050.0);
            int baysX = Math.Max(1, Math.Min(8, bayCount));
            int baysY = Math.Max(1, Math.Min(6, Math.Max(1, bayCount - 1)));

            SectionProfile pipeProf = SectionCatalogService.GetByName("P42x3.5");

            double beamZ = h - beamProf.H * 0.5;
            double colTop = Math.Max(300.0, h - beamProf.H);

            // ---- 1) 立柱网格 + 柱脚底板 ----
            var gridPoints = new List<Point3d>();
            for (int ix = 0; ix <= baysX; ix++)
            {
                for (int iy = 0; iy <= baysY; iy++)
                {
                    double x = span * ix / baysX;
                    double y = len * iy / baysY;
                    var basePt = new Point3d(x, y, 0);
                    gridPoints.Add(basePt);

                    SteelStructureBuilder.AddMember(parts, "平台立柱", colProf, basePt, new Point3d(x, y, colTop));
                    SteelStructureBuilder.AddBasePlate(parts, "柱脚底板", basePt, colProf.H + 120, colProf.B + 120, 20);
                }
            }

            // ---- 2) 柱间交叉支撑（仅外周边，避免内部遮挡） ----
            for (int ix = 0; ix <= baysX; ix++)
            {
                for (int iy = 0; iy < baysY; iy++)
                {
                    if (ix != 0 && ix != baysX) continue;
                    double x = span * ix / baysX;
                    double y0 = len * iy / baysY;
                    double y1 = len * (iy + 1) / baysY;
                    SteelStructureBuilder.AddMemberExtended(parts, "柱间支撑", braceProf,
                        new Point3d(x, y0, colTop * 0.08), new Point3d(x, y1, colTop * 0.92), JointOverlap);
                }
            }
            for (int iy = 0; iy <= baysY; iy++)
            {
                for (int ix = 0; ix < baysX; ix++)
                {
                    if (iy != 0 && iy != baysY) continue;
                    double y = len * iy / baysY;
                    double x0 = span * ix / baysX;
                    double x1 = span * (ix + 1) / baysX;
                    SteelStructureBuilder.AddMemberExtended(parts, "柱间支撑", braceProf,
                        new Point3d(x0, y, colTop * 0.08), new Point3d(x1, y, colTop * 0.92), JointOverlap);
                }
            }

            // ---- 3) 周边主梁 ----
            for (int iy = 0; iy <= baysY; iy++)
            {
                double y = len * iy / baysY;
                SteelStructureBuilder.AddMember(parts, "平台主梁", beamProf,
                    new Point3d(0, y, beamZ), new Point3d(span, y, beamZ));
            }
            for (int ix = 0; ix <= baysX; ix++)
            {
                double x = span * ix / baysX;
                SteelStructureBuilder.AddMember(parts, "平台主梁", beamProf,
                    new Point3d(x, 0, beamZ), new Point3d(x, len, beamZ));
            }

            // ---- 4) 次梁（每个分格一条中线） ----
            for (int iy = 0; iy < baysY; iy++)
            {
                double y = len * (iy + 0.5) / baysY;
                SteelStructureBuilder.AddMember(parts, "平台次梁", braceProf,
                    new Point3d(0, y, h - braceProf.H * 0.5),
                    new Point3d(span, y, h - braceProf.H * 0.5));
            }

            // ---- 5) 铺板 ----
            SteelStructureBuilder.AddBasePlate(parts, "平台花纹钢板",
                new Point3d(span * 0.5, len * 0.5, h), span, len, 8.0);

            // ---- 6) 四周栏杆（立柱 + 上下横杆） ----
            if (pipeProf != null)
            {
                double railTopZ = h + railH;
                double midZ = h + railH * 0.5;

                // 四角立柱
                var corners = new[]
                {
                    new Point3d(0, 0, 0), new Point3d(span, 0, 0),
                    new Point3d(span, len, 0), new Point3d(0, len, 0)
                };
                foreach (Point3d c in corners)
                {
                    SteelStructureBuilder.AddMemberExtended(parts, "护栏立柱", pipeProf,
                        new Point3d(c.X, c.Y, h), new Point3d(c.X, c.Y, railTopZ), 12.0);
                }

                // 边线扶手 + 中横杆（按分格分段，避免跨度过大）
                void RailLine(Point3d a, Point3d b)
                {
                    SteelStructureBuilder.AddMember(parts, "顶部扶手", pipeProf,
                        new Point3d(a.X, a.Y, railTopZ), new Point3d(b.X, b.Y, railTopZ));
                    SteelStructureBuilder.AddMember(parts, "中横杆", pipeProf,
                        new Point3d(a.X, a.Y, midZ), new Point3d(b.X, b.Y, midZ));
                }

                for (int ix = 0; ix < baysX; ix++)
                {
                    double x0 = span * ix / baysX, x1 = span * (ix + 1) / baysX;
                    RailLine(new Point3d(x0, 0, 0), new Point3d(x1, 0, 0));
                    RailLine(new Point3d(x0, len, 0), new Point3d(x1, len, 0));
                }
                for (int iy = 0; iy < baysY; iy++)
                {
                    double y0 = len * iy / baysY, y1 = len * (iy + 1) / baysY;
                    RailLine(new Point3d(0, y0, 0), new Point3d(0, y1, 0));
                    RailLine(new Point3d(span, y0, 0), new Point3d(span, y1, 0));
                }

                // 中间立柱（长边每隔一格加一根）
                for (int ix = 1; ix < baysX; ix++)
                {
                    double x = span * ix / baysX;
                    SteelStructureBuilder.AddMemberExtended(parts, "护栏立柱", pipeProf,
                        new Point3d(x, 0, h), new Point3d(x, 0, railTopZ), 12.0);
                    SteelStructureBuilder.AddMemberExtended(parts, "护栏立柱", pipeProf,
                        new Point3d(x, len, h), new Point3d(x, len, railTopZ), 12.0);
                }
            }

            // ---- 7) 单跑斜梯（沿 +Y 方向从地面到平台，含扶手） ----
            BuildAccessStair(parts, colProf, braceProf, pipeProf, span * 0.5, len, h, railH);
        }

        /// <summary>在平台边缘生成一部单跑斜梯（含两侧扶手）。</summary>
        private static void BuildAccessStair(
            List<SteelStructureBuilder.SolidMemberPart> parts,
            SectionProfile stringerProf,
            SectionProfile stepProf,
            SectionProfile pipeProf,
            double xCenter,
            double platformEdgeY,
            double platformH,
            double railH)
        {
            // 斜梯水平投影：按 45° 估算，最短 1.2m
            double runY = Math.Max(1200.0, platformH);
            double bottomY = platformEdgeY + runY;
            double stairWidth = 900.0;
            double halfW = stairWidth * 0.5;

            var leftBottom = new Point3d(xCenter - halfW, bottomY, 0);
            var leftTop = new Point3d(xCenter - halfW, platformEdgeY, platformH);
            var rightBottom = new Point3d(xCenter + halfW, bottomY, 0);
            var rightTop = new Point3d(xCenter + halfW, platformEdgeY, platformH);

            SteelStructureBuilder.AddMemberExtended(parts, "梯梁", stringerProf, leftBottom, leftTop, JointOverlap);
            SteelStructureBuilder.AddMemberExtended(parts, "梯梁", stringerProf, rightBottom, rightTop, JointOverlap);

            // 踏步：按 180mm 高估算步数
            int steps = Math.Max(3, (int)Math.Round(platformH / 180.0));
            for (int i = 1; i < steps; i++)
            {
                double t = (double)i / steps;
                double y = bottomY + (platformEdgeY - bottomY) * t;
                double z = platformH * t;
                SteelStructureBuilder.AddMember(parts, "踏步", stepProf,
                    new Point3d(xCenter - halfW, y, z), new Point3d(xCenter + halfW, y, z));
            }

            // 扶手
            if (pipeProf != null)
            {
                double railOffset = Math.Max(railH, 900.0);
                SteelStructureBuilder.AddMemberExtended(parts, "梯扶手", pipeProf,
                    leftBottom + new Vector3d(0, 0, railOffset),
                    leftTop + new Vector3d(0, 0, railOffset), JointOverlap);
                SteelStructureBuilder.AddMemberExtended(parts, "梯扶手", pipeProf,
                    rightBottom + new Vector3d(0, 0, railOffset),
                    rightTop + new Vector3d(0, 0, railOffset), JointOverlap);
            }
        }

        // ==================================================================
        // 2. 厂房柱列：一排柱 + 柱间支撑 + 吊车梁 + 系杆
        // ==================================================================
        /// <summary>
        /// 厂房柱列。
        /// Span = 柱距，Length = 总长度，Height = 柱高，SubHeight = 吊车梁标高，BayCount = 柱数。
        /// </summary>
        internal static void BuildColumnRowAssembly(
            List<SteelStructureBuilder.SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            SectionProfile braceProf,
            double colSpacing,
            double totalLength,
            double h,
            double craneBeamZ,
            int columnCount)
        {
            if (parts == null) return;

            int count = Math.Max(2, Math.Min(30, columnCount));
            colSpacing = Math.Max(colSpacing, 1500.0);
            // 总长优先：若给了总长则按总长均分，否则用柱距推算
            double span = totalLength > colSpacing * 1.5
                ? totalLength / (count - 1)
                : colSpacing;

            h = Math.Max(h, 1500.0);
            double craneZ = craneBeamZ > 0 && craneBeamZ < h
                ? craneBeamZ
                : h * 0.62;

            double colTop = Math.Max(500.0, h - beamProf.H);

            // ---- 1) 柱子 + 柱脚 ----
            for (int i = 0; i < count; i++)
            {
                double x = i * span;
                var basePt = new Point3d(x, 0, 0);
                SteelStructureBuilder.AddMember(parts, "厂房柱", colProf, basePt, new Point3d(x, 0, colTop));
                SteelStructureBuilder.AddBasePlate(parts, "柱脚底板", basePt, colProf.H + 160, colProf.B + 160, 24);
            }

            // ---- 2) 柱顶系杆（贯通） ----
            SteelStructureBuilder.AddMember(parts, "柱顶系杆", beamProf,
                new Point3d(0, 0, colTop + beamProf.H * 0.5),
                new Point3d((count - 1) * span, 0, colTop + beamProf.H * 0.5));

            // ---- 3) 吊车梁（贯通，位于吊车梁标高） ----
            SteelStructureBuilder.AddMember(parts, "吊车梁", beamProf,
                new Point3d(0, 0, craneZ), new Point3d((count - 1) * span, 0, craneZ));

            // ---- 4) 柱间交叉支撑（每隔一跨设置一道） ----
            for (int i = 0; i < count - 1; i++)
            {
                if (i % 2 != 0) continue;
                double x0 = i * span;
                double x1 = (i + 1) * span;
                SteelStructureBuilder.AddMemberExtended(parts, "柱间支撑", braceProf,
                    new Point3d(x0, 0, craneZ * 0.15), new Point3d(x1, 0, craneZ * 0.85), JointOverlap);
                SteelStructureBuilder.AddMemberExtended(parts, "柱间支撑", braceProf,
                    new Point3d(x1, 0, craneZ * 0.15), new Point3d(x0, 0, craneZ * 0.85), JointOverlap);
            }

            // ---- 5) 牛腿（每根柱吊车梁位置一个小托） ----
            for (int i = 0; i < count; i++)
            {
                double x = i * span;
                SteelStructureBuilder.AddBasePlate(parts, "柱牛腿",
                    new Point3d(x, 0, craneZ - beamProf.H * 0.5 - 30),
                    colProf.H * 0.7, colProf.B * 0.8, 30);
            }
        }

        // ==================================================================
        // 3. 管廊成套区段：多层管廊按跨数生成
        // ==================================================================
        /// <summary>
        /// 管廊成套区段。
        /// Span = 跨度，Length = 总长，Height = 顶层标高，SubHeight = 层间距，BayCount = 跨数。
        /// </summary>
        internal static void BuildPipeRackAssembly(
            List<SteelStructureBuilder.SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            SectionProfile braceProf,
            double baySpan,
            double totalLength,
            double topH,
            double tierGap,
            int bayCount)
        {
            if (parts == null) return;

            int bays = Math.Max(1, Math.Min(20, bayCount));
            baySpan = Math.Max(baySpan, 1500.0);
            double span = totalLength > baySpan * 1.5 ? totalLength / bays : baySpan;

            topH = Math.Max(topH, 2000.0);
            double gap = tierGap > 500.0 && tierGap < topH * 0.8 ? tierGap : topH * 0.42;

            // 层数：从地面到顶层按层间距推算，至少 2 层
            int tiers = Math.Max(2, Math.Min(5, (int)Math.Round(topH / gap) + 1));

            // ---- 1) 两端（及中间）门架柱 ----
            for (int i = 0; i <= bays; i++)
            {
                double x = i * span;
                for (int side = 0; side < 2; side++)
                {
                    double y = side == 0 ? 0.0 : gap * 1.6;   // 管廊宽度取层间距的 1.6 倍
                    var basePt = new Point3d(x, y, 0);
                    SteelStructureBuilder.AddMember(parts, "管廊立柱", colProf, basePt,
                        new Point3d(x, y, Math.Max(500.0, topH - beamProf.H)));
                    SteelStructureBuilder.AddBasePlate(parts, "柱脚底板", basePt,
                        colProf.H + 140, colProf.B + 140, 22);
                }
            }

            // ---- 2) 各层横梁 ----
            for (int t = 0; t < tiers; t++)
            {
                double z = topH - t * gap;
                if (z < 600.0) break;

                for (int i = 0; i <= bays; i++)
                {
                    double x = i * span;
                    SteelStructureBuilder.AddMember(parts, $"第{t + 1}层横梁", beamProf,
                        new Point3d(x, 0, z), new Point3d(x, gap * 1.6, z));
                }

                // 纵梁（贯通整段）
                SteelStructureBuilder.AddMember(parts, $"第{t + 1}层纵梁", beamProf,
                    new Point3d(0, 0, z), new Point3d(bays * span, 0, z));
                SteelStructureBuilder.AddMember(parts, $"第{t + 1}层纵梁", beamProf,
                    new Point3d(0, gap * 1.6, z), new Point3d(bays * span, gap * 1.6, z));
            }

            // ---- 3) 柱间交叉支撑（端跨） ----
            for (int i = 0; i < bays; i++)
            {
                if (i != 0 && i != bays - 1) continue;
                double x0 = i * span;
                double x1 = (i + 1) * span;
                SteelStructureBuilder.AddMemberExtended(parts, "柱间支撑", braceProf,
                    new Point3d(x0, 0, topH * 0.12), new Point3d(x1, 0, topH * 0.88), JointOverlap);
                SteelStructureBuilder.AddMemberExtended(parts, "柱间支撑", braceProf,
                    new Point3d(x0, gap * 1.6, topH * 0.12), new Point3d(x1, gap * 1.6, topH * 0.88), JointOverlap);
            }
        }

        // ==================================================================
        // 4. 楼梯成套：梯梁 + 踏步 + 扶手 + 顶部平台
        // ==================================================================
        /// <summary>
        /// 楼梯成套。
        /// Span = 水平投影长，Length = 梯段净宽，Height = 爬升高度，SubHeight = 扶手高，BayCount = 踏步数(0=自动)。
        /// </summary>
        internal static void BuildStairAssembly(
            List<SteelStructureBuilder.SolidMemberPart> parts,
            SectionProfile stringerProf,
            SectionProfile stepProf,
            SectionProfile railProf,
            double runLength,
            double width,
            double riseHeight,
            double railH,
            int stepCount)
        {
            if (parts == null) return;

            runLength = Math.Max(runLength, 900.0);
            width = Math.Max(width, 600.0);
            riseHeight = Math.Max(riseHeight, 600.0);
            railH = Math.Max(railH, 900.0);

            int steps = stepCount > 1
                ? Math.Min(40, stepCount)
                : Math.Max(3, (int)Math.Round(riseHeight / 175.0));

            double halfW = width * 0.5;

            // ---- 1) 两侧梯梁 ----
            var leftBottom = new Point3d(-halfW, 0, 0);
            var leftTop = new Point3d(-halfW, runLength, riseHeight);
            var rightBottom = new Point3d(halfW, 0, 0);
            var rightTop = new Point3d(halfW, runLength, riseHeight);

            SteelStructureBuilder.AddMemberExtended(parts, "梯梁", stringerProf, leftBottom, leftTop, JointOverlap);
            SteelStructureBuilder.AddMemberExtended(parts, "梯梁", stringerProf, rightBottom, rightTop, JointOverlap);

            // ---- 2) 踏步 ----
            for (int i = 1; i < steps; i++)
            {
                double t = (double)i / steps;
                double y = runLength * t;
                double z = riseHeight * t;
                SteelStructureBuilder.AddMember(parts, "踏步", stepProf,
                    new Point3d(-halfW, y, z), new Point3d(halfW, y, z));
            }

            // ---- 3) 扶手（上下两根 + 立柱） ----
            if (railProf != null)
            {
                double topOffset = railH;
                double midOffset = railH * 0.5;

                SteelStructureBuilder.AddMemberExtended(parts, "梯扶手", railProf,
                    leftBottom + new Vector3d(0, 0, topOffset),
                    leftTop + new Vector3d(0, 0, topOffset), JointOverlap);
                SteelStructureBuilder.AddMemberExtended(parts, "梯扶手", railProf,
                    rightBottom + new Vector3d(0, 0, topOffset),
                    rightTop + new Vector3d(0, 0, topOffset), JointOverlap);

                SteelStructureBuilder.AddMemberExtended(parts, "中横杆", railProf,
                    leftBottom + new Vector3d(0, 0, midOffset),
                    leftTop + new Vector3d(0, 0, midOffset), JointOverlap);
                SteelStructureBuilder.AddMemberExtended(parts, "中横杆", railProf,
                    rightBottom + new Vector3d(0, 0, midOffset),
                    rightTop + new Vector3d(0, 0, midOffset), JointOverlap);

                // 扶手立柱：每 4 步一根
                int postStep = Math.Max(1, steps / 4);
                for (int i = 0; i <= steps; i += postStep)
                {
                    double t = (double)i / steps;
                    double y = runLength * t;
                    double z = riseHeight * t;
                    SteelStructureBuilder.AddMemberExtended(parts, "扶手立柱", railProf,
                        new Point3d(-halfW, y, z), new Point3d(-halfW, y, z + railH), 10.0);
                    SteelStructureBuilder.AddMemberExtended(parts, "扶手立柱", railProf,
                        new Point3d(halfW, y, z), new Point3d(halfW, y, z + railH), 10.0);
                }
            }

            // ---- 4) 顶部平台（休息平台） ----
            double platformDepth = Math.Max(width * 0.9, 900.0);
            SteelStructureBuilder.AddBasePlate(parts, "休息平台板",
                new Point3d(0, runLength + platformDepth * 0.5, riseHeight),
                width, platformDepth, 8.0);

            // 平台护栏（三边）
            if (railProf != null)
            {
                double z = riseHeight + railH;
                double y0 = runLength;
                double y1 = runLength + platformDepth;

                SteelStructureBuilder.AddMember(parts, "平台扶手", railProf,
                    new Point3d(-halfW, y1, z), new Point3d(halfW, y1, z));
                SteelStructureBuilder.AddMember(parts, "平台扶手", railProf,
                    new Point3d(-halfW, y0, z), new Point3d(-halfW, y1, z));
                SteelStructureBuilder.AddMember(parts, "平台扶手", railProf,
                    new Point3d(halfW, y0, z), new Point3d(halfW, y1, z));

                foreach (double sx in new[] { -halfW, halfW })
                {
                    SteelStructureBuilder.AddMemberExtended(parts, "平台护栏立柱", railProf,
                        new Point3d(sx, y1, riseHeight), new Point3d(sx, y1, z), 10.0);
                }
            }
        }
    }
}
