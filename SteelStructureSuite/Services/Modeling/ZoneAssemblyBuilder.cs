using System;
using System.Collections.Generic;
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
    ///     材料表、详图、批量出图全部自动兼容；
    ///   · 所有节点、柱脚、护栏、踏步、梯梁、平台支撑均按真实钢结构构造对齐，
    ///     杜绝悬空平台、栏杆堵死梯口、斜撑过密、角钢冒充踏步板等造型错误。
    /// </summary>
    internal static class ZoneAssemblyBuilder
    {
        private const double JointOverlap = 20.0;

        // ==================================================================
        // 1. 钢平台成套：柱 + 主次梁 + 铺板 + 四周栏杆（留梯口） + 单跑斜梯
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

            span = Math.Max(span, 1800.0);
            len = Math.Max(len, 1500.0);
            h = Math.Max(h, 1200.0);
            railH = Math.Max(railH, 1050.0);

            // 合理控制柱距（最小约 2200mm），避免短跨平台塞入过密立柱与支撑
            int maxBaysX = Math.Max(1, (int)Math.Floor(span / 2200.0));
            int maxBaysY = Math.Max(1, (int)Math.Floor(len / 2200.0));
            int baysX = Math.Max(1, Math.Min(Math.Min(6, bayCount), maxBaysX));
            int baysY = Math.Max(1, Math.Min(Math.Min(4, Math.Max(1, bayCount - 1)), maxBaysY));

            SectionProfile pipeProf = SectionCatalogService.GetByName("P42x3.5");
            // 斜梯采用标准槽钢梯梁，避免用粗大柱截面当梯梁
            SectionProfile stairStringerProf = SectionCatalogService.TryFindByName("C16a")
                ?? SectionCatalogService.TryFindByName("C160x60x20x2.5")
                ?? beamProf;

            double beamZ = h - beamProf.H * 0.5;
            double colTop = Math.Max(300.0, h - beamProf.H);
            double subBeamZ = h - braceProf.H * 0.5;

            // ---- 1) 立柱网格 + 柱脚底板 ----
            for (int ix = 0; ix <= baysX; ix++)
            {
                for (int iy = 0; iy <= baysY; iy++)
                {
                    double x = span * ix / baysX;
                    double y = len * iy / baysY;
                    var basePt = new Point3d(x, y, 0);

                    SteelStructureBuilder.AddMember(parts, "平台立柱", colProf, basePt, new Point3d(x, y, colTop));
                    SteelStructureBuilder.AddBasePlate(parts, "柱脚底板", basePt, colProf.H + 100, colProf.B + 100, 20);
                }
            }

            // ---- 2) 柱间交叉支撑（仅外周端跨设置，简洁清晰不杂乱） ----
            double braceZ0 = Math.Min(150.0, colTop * 0.08);
            double braceZ1 = Math.Max(braceZ0 + 300.0, colTop - 80.0);
            // X=0 与 X=span 两侧的第 0 跨
            foreach (double x in new[] { 0.0, span })
            {
                double y0 = 0.0;
                double y1 = len / baysY;
                SteelStructureBuilder.AddMember(parts, "柱间支撑", braceProf,
                    new Point3d(x, y0, braceZ0), new Point3d(x, y1, braceZ1));
                SteelStructureBuilder.AddMember(parts, "柱间支撑", braceProf,
                    new Point3d(x, y1, braceZ0), new Point3d(x, y0, braceZ1));
            }
            // Y=0 后侧端跨（避开 Y=len 斜梯侧）
            {
                double x0 = 0.0;
                double x1 = span / baysX;
                SteelStructureBuilder.AddMember(parts, "柱间支撑", braceProf,
                    new Point3d(x0, 0.0, braceZ0), new Point3d(x1, 0.0, braceZ1));
                SteelStructureBuilder.AddMember(parts, "柱间支撑", braceProf,
                    new Point3d(x1, 0.0, braceZ0), new Point3d(x0, 0.0, braceZ1));
            }

            // ---- 3) 主梁格 ----
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

            // ---- 4) 次梁（每跨中间一道） ----
            for (int iy = 0; iy < baysY; iy++)
            {
                double y = len * (iy + 0.5) / baysY;
                SteelStructureBuilder.AddMember(parts, "平台次梁", braceProf,
                    new Point3d(0, y, subBeamZ), new Point3d(span, y, subBeamZ));
            }

            // ---- 5) 平台花纹钢板 ----
            SteelStructureBuilder.AddBasePlate(parts, "平台花纹钢板",
                new Point3d(span * 0.5, len * 0.5, h), span, len, 8.0);

            // 斜梯开口位置（位于 +Y 边中央）
            double stairWidth = Math.Min(900.0, Math.Max(700.0, span * 0.35));
            double stairX = span * 0.5;
            double stairX0 = Math.Max(200.0, stairX - stairWidth * 0.5);
            double stairX1 = Math.Min(span - 200.0, stairX + stairWidth * 0.5);
            stairWidth = Math.Max(600.0, stairX1 - stairX0);
            stairX = (stairX0 + stairX1) * 0.5;

            // ---- 6) 四周安全栏杆（含立柱、顶扶手、中横杆；+Y 侧留出斜梯出入口） ----
            if (pipeProf != null)
            {
                double railTopZ = h + railH;
                double midZ = h + railH * 0.52;

                void AddPost(double px, double py)
                {
                    SteelStructureBuilder.AddMember(parts, "护栏立柱", pipeProf,
                        new Point3d(px, py, h), new Point3d(px, py, railTopZ));
                }

                void AddRailSpan(double ax, double ay, double bx, double by)
                {
                    if (new Point3d(ax, ay, 0).DistanceTo(new Point3d(bx, by, 0)) < 80.0) return;
                    SteelStructureBuilder.AddMember(parts, "顶部扶手", pipeProf,
                        new Point3d(ax, ay, railTopZ), new Point3d(bx, by, railTopZ));
                    SteelStructureBuilder.AddMember(parts, "中横杆", pipeProf,
                        new Point3d(ax, ay, midZ), new Point3d(bx, by, midZ));
                }

                // 后侧边 Y=0（整边贯通 + 均匀立柱）
                int postsX = Math.Max(2, (int)Math.Ceiling(span / 1500.0));
                for (int i = 0; i <= postsX; i++)
                    AddPost(span * i / postsX, 0.0);
                AddRailSpan(0.0, 0.0, span, 0.0);

                // 左右两边 X=0, X=span（整边贯通 + 均匀立柱）
                int postsY = Math.Max(2, (int)Math.Ceiling(len / 1500.0));
                for (int i = 1; i < postsY; i++)
                {
                    double py = len * i / postsY;
                    AddPost(0.0, py);
                    AddPost(span, py);
                }
                AddPost(0.0, len);
                AddPost(span, len);
                AddRailSpan(0.0, 0.0, 0.0, len);
                AddRailSpan(span, 0.0, span, len);

                // 前侧边 Y=len（左右两段，中间留出斜梯通道 [stairX0, stairX1]）
                AddPost(stairX0, len);
                AddPost(stairX1, len);
                if (stairX0 > 1500.0) AddPost(stairX0 * 0.5, len);
                if (span - stairX1 > 1500.0) AddPost((stairX1 + span) * 0.5, len);
                AddRailSpan(0.0, len, stairX0, len);
                AddRailSpan(stairX1, len, span, len);
            }

            // ---- 7) 单跑斜梯（沿 +Y 方向从地面升至平台梯口，含踏步板与完整双侧扶手） ----
            BuildAccessStair(parts, stairStringerProf, pipeProf, stairX, stairWidth, len, h, railH);
        }

        /// <summary>在平台边缘生成一部构造完整的单跑斜梯（槽钢梯梁 + 水平踏步板 + 双侧扶手与立柱 + 梯脚底板）。</summary>
        private static void BuildAccessStair(
            List<SteelStructureBuilder.SolidMemberPart> parts,
            SectionProfile stringerProf,
            SectionProfile pipeProf,
            double xCenter,
            double stairWidth,
            double platformEdgeY,
            double platformH,
            double railH)
        {
            double runY = Math.Max(1200.0, platformH);
            double bottomY = platformEdgeY + runY;
            double halfW = stairWidth * 0.5;

            var leftBottom = new Point3d(xCenter - halfW, bottomY, 0);
            var leftTop = new Point3d(xCenter - halfW, platformEdgeY, platformH);
            var rightBottom = new Point3d(xCenter + halfW, bottomY, 0);
            var rightTop = new Point3d(xCenter + halfW, platformEdgeY, platformH);

            // 左右梯梁
            SteelStructureBuilder.AddMember(parts, "斜梯梯梁", stringerProf, leftBottom, leftTop);
            SteelStructureBuilder.AddMember(parts, "斜梯梯梁", stringerProf, rightBottom, rightTop);

            // 梯脚底板
            SteelStructureBuilder.AddBasePlate(parts, "梯脚底板", leftBottom, 160, 180, 14);
            SteelStructureBuilder.AddBasePlate(parts, "梯脚底板", rightBottom, 160, 180, 14);

            // 水平防滑踏步板（真实钢板，避免型钢冒充踏步）
            int steps = Math.Max(4, (int)Math.Round(platformH / 190.0));
            for (int i = 1; i < steps; i++)
            {
                double t = (double)i / steps;
                double y = bottomY + (platformEdgeY - bottomY) * t;
                double z = platformH * t;
                SteelStructureBuilder.AddRotatedPlate(parts, "斜梯踏步板",
                    new Point3d(xCenter, y, z), stairWidth, 240.0, 8.0, 0.0);
            }

            // 双侧连续扶手 + 中横杆 + 均匀立柱（顶部自然衔接平台梯口立柱）
            if (pipeProf != null)
            {
                double topOff = Math.Max(railH, 900.0);
                double midOff = topOff * 0.52;

                SteelStructureBuilder.AddMember(parts, "梯顶扶手", pipeProf,
                    leftBottom + new Vector3d(0, 0, topOff), leftTop + new Vector3d(0, 0, topOff));
                SteelStructureBuilder.AddMember(parts, "梯顶扶手", pipeProf,
                    rightBottom + new Vector3d(0, 0, topOff), rightTop + new Vector3d(0, 0, topOff));

                SteelStructureBuilder.AddMember(parts, "梯中横杆", pipeProf,
                    leftBottom + new Vector3d(0, 0, midOff), leftTop + new Vector3d(0, 0, midOff));
                SteelStructureBuilder.AddMember(parts, "梯中横杆", pipeProf,
                    rightBottom + new Vector3d(0, 0, midOff), rightTop + new Vector3d(0, 0, midOff));

                int postStride = Math.Max(1, steps / 4);
                for (int i = 0; i < steps; i++)
                {
                    if (i != 0 && i % postStride != 0) continue;
                    double t = (double)i / steps;
                    double y = bottomY + (platformEdgeY - bottomY) * t;
                    double z = platformH * t;
                    SteelStructureBuilder.AddMember(parts, "梯扶手立柱", pipeProf,
                        new Point3d(xCenter - halfW, y, z), new Point3d(xCenter - halfW, y, z + topOff));
                    SteelStructureBuilder.AddMember(parts, "梯扶手立柱", pipeProf,
                        new Point3d(xCenter + halfW, y, z), new Point3d(xCenter + halfW, y, z + topOff));
                }
            }
        }

        // ==================================================================
        // 2. 厂房柱列：一排柱 + 牛腿 + 偏心吊车梁 + 柱顶系杆 + 柱间支撑
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
            double span = totalLength > colSpacing * 1.5
                ? totalLength / (count - 1)
                : colSpacing;

            h = Math.Max(h, 2500.0);
            double craneZ = craneBeamZ > 600.0 && craneBeamZ < h - 600.0
                ? craneBeamZ
                : h * 0.65;

            double colTop = Math.Max(500.0, h - beamProf.H);
            double totalX = (count - 1) * span;

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
                new Point3d(totalX, 0, colTop + beamProf.H * 0.5));

            // ---- 3) 柱侧牛腿 + 吊车梁（沿 +Y 方向外挑，不穿入柱身） ----
            double corbelOut = colProf.B * 0.5 + beamProf.B * 0.5 + 80.0;
            double corbelZ = craneZ - beamProf.H * 0.5 - 60.0;
            for (int i = 0; i < count; i++)
            {
                double x = i * span;
                SteelStructureBuilder.AddMember(parts, "柱牛腿", braceProf,
                    new Point3d(x, 0, corbelZ),
                    new Point3d(x, corbelOut + beamProf.B * 0.5, corbelZ));
                SteelStructureBuilder.AddMember(parts, "牛腿斜撑", braceProf,
                    new Point3d(x, 0, corbelZ - 350.0),
                    new Point3d(x, corbelOut, corbelZ));
            }

            SteelStructureBuilder.AddMember(parts, "吊车梁", beamProf,
                new Point3d(0, corbelOut, craneZ),
                new Point3d(totalX, corbelOut, craneZ));

            // ---- 4) 柱间交叉支撑（居中跨或隔跨设置，两端对齐柱中心） ----
            double braceZ0 = 150.0;
            double braceZ1 = Math.Max(braceZ0 + 500.0, craneZ - 150.0);
            for (int i = 0; i < count - 1; i++)
            {
                if (i % 2 != 0) continue;
                double x0 = i * span;
                double x1 = (i + 1) * span;
                SteelStructureBuilder.AddMember(parts, "柱间支撑", braceProf,
                    new Point3d(x0, 0, braceZ0), new Point3d(x1, 0, braceZ1));
                SteelStructureBuilder.AddMember(parts, "柱间支撑", braceProf,
                    new Point3d(x1, 0, braceZ0), new Point3d(x0, 0, braceZ1));
            }
        }

        // ==================================================================
        // 3. 管廊成套区段：多层管廊按跨数生成
        // ==================================================================
        /// <summary>
        /// 管廊成套区段。
        /// Span = 管廊跨度(宽度 Y)，Length = 总长(X)，Height = 顶层标高，SubHeight = 层间距，BayCount = 纵向跨数。
        /// </summary>
        internal static void BuildPipeRackAssembly(
            List<SteelStructureBuilder.SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            SectionProfile braceProf,
            double rackWidth,
            double totalLength,
            double topH,
            double tierGap,
            int bayCount)
        {
            if (parts == null) return;

            int bays = Math.Max(1, Math.Min(20, bayCount));
            rackWidth = Math.Max(rackWidth, 1800.0);
            double bayPitch = totalLength > 1500.0 ? totalLength / bays : 4000.0;
            bayPitch = Math.Max(bayPitch, 1800.0);

            topH = Math.Max(topH, 2400.0);
            double gap = tierGap > 800.0 && tierGap < topH * 0.75 ? tierGap : topH * 0.42;
            int tiers = Math.Max(2, Math.Min(4, (int)Math.Floor((topH - 1200.0) / gap) + 1));

            double topBeamZ = topH - beamProf.H * 0.5;
            double colTop = Math.Max(500.0, topH - beamProf.H);
            double totalX = bays * bayPitch;

            // ---- 1) 各榀门架立柱 + 柱脚底板 ----
            for (int i = 0; i <= bays; i++)
            {
                double x = i * bayPitch;
                for (int side = 0; side < 2; side++)
                {
                    double y = side == 0 ? 0.0 : rackWidth;
                    var basePt = new Point3d(x, y, 0);
                    SteelStructureBuilder.AddMember(parts, "管廊立柱", colProf, basePt, new Point3d(x, y, colTop));
                    SteelStructureBuilder.AddBasePlate(parts, "柱脚底板", basePt, colProf.H + 140, colProf.B + 140, 22);
                }
            }

            // ---- 2) 各层横梁与纵梁 ----
            for (int t = 0; t < tiers; t++)
            {
                double z = topBeamZ - t * gap;
                if (z < 1200.0) break;

                for (int i = 0; i <= bays; i++)
                {
                    double x = i * bayPitch;
                    SteelStructureBuilder.AddMember(parts, $"第{t + 1}层横梁", beamProf,
                        new Point3d(x, 0, z), new Point3d(x, rackWidth, z));
                }

                SteelStructureBuilder.AddMember(parts, $"第{t + 1}层纵梁", beamProf,
                    new Point3d(0, 0, z), new Point3d(totalX, 0, z));
                SteelStructureBuilder.AddMember(parts, $"第{t + 1}层纵梁", beamProf,
                    new Point3d(0, rackWidth, z), new Point3d(totalX, rackWidth, z));
            }

            // ---- 3) 端跨纵向柱间交叉支撑 ----
            double braceZ0 = 150.0;
            double braceZ1 = Math.Max(braceZ0 + 500.0, colTop - 100.0);
            for (int i = 0; i < bays; i++)
            {
                if (i != 0 && i != bays - 1) continue;
                double x0 = i * bayPitch;
                double x1 = (i + 1) * bayPitch;
                foreach (double y in new[] { 0.0, rackWidth })
                {
                    SteelStructureBuilder.AddMember(parts, "柱间支撑", braceProf,
                        new Point3d(x0, y, braceZ0), new Point3d(x1, y, braceZ1));
                    SteelStructureBuilder.AddMember(parts, "柱间支撑", braceProf,
                        new Point3d(x1, y, braceZ0), new Point3d(x0, y, braceZ1));
                }
            }
        }

        // ==================================================================
        // 4. 楼梯成套：梯梁 + 水平踏步板 + 双侧扶手 + 落地支撑休息平台
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

            runLength = Math.Max(runLength, 1200.0);
            width = Math.Max(width, 700.0);
            riseHeight = Math.Max(riseHeight, 800.0);
            railH = Math.Max(railH, 1000.0);

            int steps = stepCount > 1
                ? Math.Min(40, stepCount)
                : Math.Max(4, (int)Math.Round(riseHeight / 180.0));

            double halfW = width * 0.5;
            SectionProfile pipeProf = railProf ?? SectionCatalogService.GetByName("P42x3.5");
            SectionProfile landingColProf = SectionCatalogService.TryFindByName("F100x100x6")
                ?? SectionCatalogService.TryFindByName("HW150x150x7x10")
                ?? stringerProf;

            // ---- 1) 两侧梯梁 + 梯脚底板 ----
            var leftBottom = new Point3d(-halfW, 0, 0);
            var leftTop = new Point3d(-halfW, runLength, riseHeight);
            var rightBottom = new Point3d(halfW, 0, 0);
            var rightTop = new Point3d(halfW, runLength, riseHeight);

            SteelStructureBuilder.AddMember(parts, "左梯梁", stringerProf, leftBottom, leftTop);
            SteelStructureBuilder.AddMember(parts, "右梯梁", stringerProf, rightBottom, rightTop);
            SteelStructureBuilder.AddBasePlate(parts, "梯脚底板", leftBottom, 160, 180, 14);
            SteelStructureBuilder.AddBasePlate(parts, "梯脚底板", rightBottom, 160, 180, 14);

            // ---- 2) 水平防滑踏步板（真实钢板） ----
            for (int i = 1; i < steps; i++)
            {
                double t = (double)i / steps;
                double y = runLength * t;
                double z = riseHeight * t;
                SteelStructureBuilder.AddRotatedPlate(parts, "踏步板",
                    new Point3d(0, y, z), width, 240.0, 8.0, 0.0);
            }

            // ---- 3) 梯段双侧扶手（顶部扶手 + 中横杆 + 首尾及中间立柱） ----
            if (pipeProf != null)
            {
                double topOffset = railH;
                double midOffset = railH * 0.52;

                SteelStructureBuilder.AddMember(parts, "梯扶手", pipeProf,
                    leftBottom + new Vector3d(0, 0, topOffset), leftTop + new Vector3d(0, 0, topOffset));
                SteelStructureBuilder.AddMember(parts, "梯扶手", pipeProf,
                    rightBottom + new Vector3d(0, 0, topOffset), rightTop + new Vector3d(0, 0, topOffset));

                SteelStructureBuilder.AddMember(parts, "梯中横杆", pipeProf,
                    leftBottom + new Vector3d(0, 0, midOffset), leftTop + new Vector3d(0, 0, midOffset));
                SteelStructureBuilder.AddMember(parts, "梯中横杆", pipeProf,
                    rightBottom + new Vector3d(0, 0, midOffset), rightTop + new Vector3d(0, 0, midOffset));

                int postStep = Math.Max(1, steps / 4);
                for (int i = 0; i <= steps; i++)
                {
                    if (i != 0 && i != steps && i % postStep != 0) continue;
                    double t = (double)i / steps;
                    double y = runLength * t;
                    double z = riseHeight * t;
                    SteelStructureBuilder.AddMember(parts, "扶手立柱", pipeProf,
                        new Point3d(-halfW, y, z), new Point3d(-halfW, y, z + railH));
                    SteelStructureBuilder.AddMember(parts, "扶手立柱", pipeProf,
                        new Point3d(halfW, y, z), new Point3d(halfW, y, z + railH));
                }
            }

            // ---- 4) 顶部落地支撑休息平台（4 根落地支撑柱 + 柱脚 + 四周平台梁 + 平台花纹板） ----
            double platformDepth = Math.Max(width, 1000.0);
            double y0 = runLength;
            double y1 = runLength + platformDepth;
            double beamH = Math.Max(80.0, stringerProf.H);
            double landingBeamZ = riseHeight - beamH * 0.5;
            double landingColTop = Math.Max(200.0, riseHeight - beamH);

            // 4 根落地支撑柱 + 柱脚底板
            var landingCorners = new[]
            {
                new Point3d(-halfW, y0, 0),
                new Point3d(halfW, y0, 0),
                new Point3d(-halfW, y1, 0),
                new Point3d(halfW, y1, 0)
            };
            foreach (Point3d c in landingCorners)
            {
                SteelStructureBuilder.AddMember(parts, "平台支撑柱", landingColProf,
                    c, new Point3d(c.X, c.Y, landingColTop));
                SteelStructureBuilder.AddBasePlate(parts, "平台柱脚底板",
                    c, landingColProf.H + 80, landingColProf.B + 80, 16);
            }

            // 四周平台边梁
            SteelStructureBuilder.AddMember(parts, "平台边梁", stringerProf,
                new Point3d(-halfW, y0, landingBeamZ), new Point3d(halfW, y0, landingBeamZ));
            SteelStructureBuilder.AddMember(parts, "平台边梁", stringerProf,
                new Point3d(-halfW, y1, landingBeamZ), new Point3d(halfW, y1, landingBeamZ));
            SteelStructureBuilder.AddMember(parts, "平台边梁", stringerProf,
                new Point3d(-halfW, y0, landingBeamZ), new Point3d(-halfW, y1, landingBeamZ));
            SteelStructureBuilder.AddMember(parts, "平台边梁", stringerProf,
                new Point3d(halfW, y0, landingBeamZ), new Point3d(halfW, y1, landingBeamZ));

            // 休息平台花纹钢板
            SteelStructureBuilder.AddBasePlate(parts, "休息平台板",
                new Point3d(0, (y0 + y1) * 0.5, riseHeight),
                width, platformDepth, 8.0);

            // 平台三边护栏（顶扶手 + 中横杆 + 后侧立柱，与梯段扶手严密衔接）
            if (pipeProf != null)
            {
                double topZ = riseHeight + railH;
                double midZ = riseHeight + railH * 0.52;

                // 顶部扶手（左、右、后三边）
                SteelStructureBuilder.AddMember(parts, "平台扶手", pipeProf,
                    new Point3d(-halfW, y1, topZ), new Point3d(halfW, y1, topZ));
                SteelStructureBuilder.AddMember(parts, "平台扶手", pipeProf,
                    new Point3d(-halfW, y0, topZ), new Point3d(-halfW, y1, topZ));
                SteelStructureBuilder.AddMember(parts, "平台扶手", pipeProf,
                    new Point3d(halfW, y0, topZ), new Point3d(halfW, y1, topZ));

                // 中横杆（左、右、后三边）
                SteelStructureBuilder.AddMember(parts, "平台中横杆", pipeProf,
                    new Point3d(-halfW, y1, midZ), new Point3d(halfW, y1, midZ));
                SteelStructureBuilder.AddMember(parts, "平台中横杆", pipeProf,
                    new Point3d(-halfW, y0, midZ), new Point3d(-halfW, y1, midZ));
                SteelStructureBuilder.AddMember(parts, "平台中横杆", pipeProf,
                    new Point3d(halfW, y0, midZ), new Point3d(halfW, y1, midZ));

                // 后侧两根角柱（前侧两根已由梯段顶端立柱提供）
                foreach (double sx in new[] { -halfW, halfW })
                {
                    SteelStructureBuilder.AddMember(parts, "平台护栏立柱", pipeProf,
                        new Point3d(sx, y1, riseHeight), new Point3d(sx, y1, topZ));
                }
            }
        }

        // ==================================================================
        // 5. 轻钢厂房成套：多榀双坡门架 + 纵向系杆/屋面檩条 + 端跨柱间支撑
        // ==================================================================
        internal static void BuildPortalHallAssembly(
            List<SteelStructureBuilder.SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            SectionProfile braceProf,
            double span,
            double totalLength,
            double eaveH,
            double ridgeRise,
            int bayCount)
        {
            if (parts == null) return;

            span = Math.Max(span, 6000.0);
            int bays = Math.Max(2, Math.Min(16, bayCount));
            double totalY = Math.Max(totalLength, bays * 4000.0);
            double bayPitch = totalY / bays;
            eaveH = Math.Max(eaveH, 3600.0);
            ridgeRise = Math.Max(ridgeRise, span * 0.06);
            double ridgeZ = eaveH + ridgeRise;
            double colTop = Math.Max(1000.0, eaveH - beamProf.H * 0.5);

            SectionProfile purlinProf = SectionCatalogService.TryFindByName("C160x60x20x2.5") ?? braceProf;

            // ---- 1) 多榀双坡门架（立柱 + 柱脚 + 左右屋面斜梁） ----
            for (int i = 0; i <= bays; i++)
            {
                double y = i * bayPitch;
                var leftBase = new Point3d(0, y, 0);
                var rightBase = new Point3d(span, y, 0);
                SteelStructureBuilder.AddMember(parts, "厂房钢柱", colProf, leftBase, new Point3d(0, y, colTop));
                SteelStructureBuilder.AddMember(parts, "厂房钢柱", colProf, rightBase, new Point3d(span, y, colTop));
                SteelStructureBuilder.AddBasePlate(parts, "柱脚底板", leftBase, colProf.H + 140, colProf.B + 140, 24);
                SteelStructureBuilder.AddBasePlate(parts, "柱脚底板", rightBase, colProf.H + 140, colProf.B + 140, 24);

                Point3d leftEave = new Point3d(0, y, eaveH);
                Point3d rightEave = new Point3d(span, y, eaveH);
                Point3d ridgePt = new Point3d(span * 0.5, y, ridgeZ);
                SteelStructureBuilder.AddMember(parts, "屋面斜梁", beamProf, leftEave, ridgePt);
                SteelStructureBuilder.AddMember(parts, "屋面斜梁", beamProf, rightEave, ridgePt);
            }

            // ---- 2) 纵向柱顶系杆 + 屋面檩条 ----
            SteelStructureBuilder.AddMember(parts, "柱顶系杆", braceProf,
                new Point3d(0, 0, eaveH), new Point3d(0, totalY, eaveH));
            SteelStructureBuilder.AddMember(parts, "柱顶系杆", braceProf,
                new Point3d(span, 0, eaveH), new Point3d(span, totalY, eaveH));
            SteelStructureBuilder.AddMember(parts, "屋脊系杆", purlinProf,
                new Point3d(span * 0.5, 0, ridgeZ), new Point3d(span * 0.5, totalY, ridgeZ));

            int purlinsPerSlope = Math.Max(2, Math.Min(6, (int)Math.Round((span * 0.5) / 1800.0)));
            for (int k = 1; k < purlinsPerSlope; k++)
            {
                double t = (double)k / purlinsPerSlope;
                double z = eaveH + ridgeRise * t;
                double xL = (span * 0.5) * t;
                double xR = span - xL;
                SteelStructureBuilder.AddMember(parts, "屋面檩条", purlinProf,
                    new Point3d(xL, 0, z), new Point3d(xL, totalY, z));
                SteelStructureBuilder.AddMember(parts, "屋面檩条", purlinProf,
                    new Point3d(xR, 0, z), new Point3d(xR, totalY, z));
            }

            // ---- 3) 端跨柱间交叉支撑 ----
            double braceZ0 = 180.0;
            double braceZ1 = Math.Max(braceZ0 + 600.0, colTop - 120.0);
            for (int i = 0; i < bays; i++)
            {
                if (i != 0 && i != bays - 1) continue;
                double y0 = i * bayPitch;
                double y1 = (i + 1) * bayPitch;
                foreach (double x in new[] { 0.0, span })
                {
                    SteelStructureBuilder.AddMember(parts, "柱间支撑", braceProf,
                        new Point3d(x, y0, braceZ0), new Point3d(x, y1, braceZ1));
                    SteelStructureBuilder.AddMember(parts, "柱间支撑", braceProf,
                        new Point3d(x, y1, braceZ0), new Point3d(x, y0, braceZ1));
                }
            }
        }

        // ==================================================================
        // 6. 连廊通廊成套：落地支墩柱 + 纵横主梁 + 铺板 + 双侧连续安全栏杆
        // ==================================================================
        internal static void BuildWalkwayBridgeAssembly(
            List<SteelStructureBuilder.SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            SectionProfile braceProf,
            double totalLength,
            double width,
            double deckH,
            double railH,
            int bayCount)
        {
            if (parts == null) return;

            totalLength = Math.Max(totalLength, 4000.0);
            width = Math.Max(width, 1000.0);
            deckH = Math.Max(deckH, 2000.0);
            railH = Math.Max(railH, 1050.0);
            int bays = Math.Max(2, Math.Min(12, bayCount));
            double bayPitch = totalLength / bays;

            SectionProfile pipeProf = SectionCatalogService.GetByName("P42x3.5");
            double beamZ = deckH - beamProf.H * 0.5;
            double colTop = Math.Max(500.0, deckH - beamProf.H);

            // ---- 1) 支墩立柱 + 柱脚底板 + 横梁 ----
            for (int i = 0; i <= bays; i++)
            {
                double x = i * bayPitch;
                foreach (double y in new[] { 0.0, width })
                {
                    var basePt = new Point3d(x, y, 0);
                    SteelStructureBuilder.AddMember(parts, "通廊支墩柱", colProf, basePt, new Point3d(x, y, colTop));
                    SteelStructureBuilder.AddBasePlate(parts, "柱脚底板", basePt, colProf.H + 120, colProf.B + 120, 20);
                }
                SteelStructureBuilder.AddMember(parts, "通廊横梁", beamProf,
                    new Point3d(x, 0, beamZ), new Point3d(x, width, beamZ));
            }

            // ---- 2) 贯通纵梁 + 次横梁 + 铺板 ----
            SteelStructureBuilder.AddMember(parts, "通廊纵梁", beamProf,
                new Point3d(0, 0, beamZ), new Point3d(totalLength, 0, beamZ));
            SteelStructureBuilder.AddMember(parts, "通廊纵梁", beamProf,
                new Point3d(0, width, beamZ), new Point3d(totalLength, width, beamZ));

            int subCross = Math.Max(bays * 2, (int)Math.Ceiling(totalLength / 1800.0));
            for (int i = 1; i < subCross; i++)
            {
                double x = totalLength * i / subCross;
                SteelStructureBuilder.AddMember(parts, "通廊次梁", braceProf,
                    new Point3d(x, 0, deckH - braceProf.H * 0.5),
                    new Point3d(x, width, deckH - braceProf.H * 0.5));
            }

            SteelStructureBuilder.AddBasePlate(parts, "通廊花纹钢板",
                new Point3d(totalLength * 0.5, width * 0.5, deckH), totalLength, width, 8.0);

            // ---- 3) 端跨柱间支撑 ----
            double braceZ0 = 150.0;
            double braceZ1 = Math.Max(braceZ0 + 500.0, colTop - 100.0);
            for (int i = 0; i < bays; i++)
            {
                if (i != 0 && i != bays - 1) continue;
                double x0 = i * bayPitch;
                double x1 = (i + 1) * bayPitch;
                foreach (double y in new[] { 0.0, width })
                {
                    SteelStructureBuilder.AddMember(parts, "柱间支撑", braceProf,
                        new Point3d(x0, y, braceZ0), new Point3d(x1, y, braceZ1));
                    SteelStructureBuilder.AddMember(parts, "柱间支撑", braceProf,
                        new Point3d(x1, y, braceZ0), new Point3d(x0, y, braceZ1));
                }
            }

            // ---- 4) 两侧连续安全栏杆 ----
            if (pipeProf != null)
            {
                double topZ = deckH + railH;
                double midZ = deckH + railH * 0.52;
                int posts = Math.Max(3, (int)Math.Ceiling(totalLength / 1500.0));
                foreach (double y in new[] { 0.0, width })
                {
                    SteelStructureBuilder.AddMember(parts, "通廊顶扶手", pipeProf,
                        new Point3d(0, y, topZ), new Point3d(totalLength, y, topZ));
                    SteelStructureBuilder.AddMember(parts, "通廊中横杆", pipeProf,
                        new Point3d(0, y, midZ), new Point3d(totalLength, y, midZ));
                    for (int i = 0; i <= posts; i++)
                    {
                        double x = totalLength * i / posts;
                        SteelStructureBuilder.AddMember(parts, "通廊护栏立柱", pipeProf,
                            new Point3d(x, y, deckH), new Point3d(x, y, topZ));
                    }
                }
            }
        }

        // ==================================================================
        // 7. 设备框架成套：多层多跨网格柱 + 各层主次梁 + 顶层操作板 + 四周护栏
        // ==================================================================
        internal static void BuildEquipmentFrameAssembly(
            List<SteelStructureBuilder.SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            SectionProfile braceProf,
            double span,
            double len,
            double totalH,
            double floorH,
            int bayCount)
        {
            if (parts == null) return;

            span = Math.Max(span, 3000.0);
            len = Math.Max(len, 2400.0);
            totalH = Math.Max(totalH, 3600.0);
            double tierH = floorH > 1500.0 && floorH < totalH * 0.75 ? floorH : totalH * 0.5;
            int floors = Math.Max(2, Math.Min(5, (int)Math.Round(totalH / tierH)));
            int baysX = Math.Max(1, Math.Min(4, Math.Min(bayCount, Math.Max(1, (int)Math.Floor(span / 2500.0)))));
            int baysY = Math.Max(1, Math.Min(3, Math.Max(1, (int)Math.Floor(len / 2500.0))));

            SectionProfile pipeProf = SectionCatalogService.GetByName("P42x3.5");
            double colTop = Math.Max(1000.0, totalH - beamProf.H);

            // ---- 1) 框架柱网 + 柱脚底板 ----
            for (int ix = 0; ix <= baysX; ix++)
            {
                for (int iy = 0; iy <= baysY; iy++)
                {
                    double x = span * ix / baysX;
                    double y = len * iy / baysY;
                    var basePt = new Point3d(x, y, 0);
                    SteelStructureBuilder.AddMember(parts, "框架柱", colProf, basePt, new Point3d(x, y, colTop));
                    SteelStructureBuilder.AddBasePlate(parts, "柱脚底板", basePt, colProf.H + 140, colProf.B + 140, 22);
                }
            }

            // ---- 2) 各层主梁格与次梁 ----
            for (int f = 1; f <= floors; f++)
            {
                double fTop = totalH * f / floors;
                double bZ = fTop - beamProf.H * 0.5;
                for (int iy = 0; iy <= baysY; iy++)
                {
                    double y = len * iy / baysY;
                    SteelStructureBuilder.AddMember(parts, $"第{f}层主梁", beamProf,
                        new Point3d(0, y, bZ), new Point3d(span, y, bZ));
                }
                for (int ix = 0; ix <= baysX; ix++)
                {
                    double x = span * ix / baysX;
                    SteelStructureBuilder.AddMember(parts, $"第{f}层主梁", beamProf,
                        new Point3d(x, 0, bZ), new Point3d(x, len, bZ));
                }
                for (int iy = 0; iy < baysY; iy++)
                {
                    double y = len * (iy + 0.5) / baysY;
                    SteelStructureBuilder.AddMember(parts, $"第{f}层次梁", braceProf,
                        new Point3d(0, y, fTop - braceProf.H * 0.5),
                        new Point3d(span, y, fTop - braceProf.H * 0.5));
                }
            }

            // ---- 3) 顶层操作平台花纹板 + 四周护栏 ----
            SteelStructureBuilder.AddBasePlate(parts, "顶层操作平台板",
                new Point3d(span * 0.5, len * 0.5, totalH), span, len, 8.0);

            if (pipeProf != null)
            {
                double railH = 1050.0;
                double topZ = totalH + railH;
                double midZ = totalH + railH * 0.52;
                int px = Math.Max(2, (int)Math.Ceiling(span / 1500.0));
                int py = Math.Max(2, (int)Math.Ceiling(len / 1500.0));

                foreach (double y in new[] { 0.0, len })
                {
                    SteelStructureBuilder.AddMember(parts, "顶层护栏扶手", pipeProf,
                        new Point3d(0, y, topZ), new Point3d(span, y, topZ));
                    SteelStructureBuilder.AddMember(parts, "顶层护栏中杆", pipeProf,
                        new Point3d(0, y, midZ), new Point3d(span, y, midZ));
                    for (int i = 0; i <= px; i++)
                        SteelStructureBuilder.AddMember(parts, "顶层护栏立柱", pipeProf,
                            new Point3d(span * i / px, y, totalH), new Point3d(span * i / px, y, topZ));
                }
                foreach (double x in new[] { 0.0, span })
                {
                    SteelStructureBuilder.AddMember(parts, "顶层护栏扶手", pipeProf,
                        new Point3d(x, 0, topZ), new Point3d(x, len, topZ));
                    SteelStructureBuilder.AddMember(parts, "顶层护栏中杆", pipeProf,
                        new Point3d(x, 0, midZ), new Point3d(x, len, midZ));
                    for (int i = 1; i < py; i++)
                        SteelStructureBuilder.AddMember(parts, "顶层护栏立柱", pipeProf,
                            new Point3d(x, len * i / py, totalH), new Point3d(x, len * i / py, topZ));
                }
            }

            // ---- 4) 首层外周边端跨柱间支撑 ----
            double z1 = totalH / floors - beamProf.H;
            double bz0 = 150.0;
            double bz1 = Math.Max(bz0 + 400.0, z1 - 80.0);
            foreach (double x in new[] { 0.0, span })
            {
                SteelStructureBuilder.AddMember(parts, "柱间支撑", braceProf,
                    new Point3d(x, 0, bz0), new Point3d(x, len / baysY, bz1));
                SteelStructureBuilder.AddMember(parts, "柱间支撑", braceProf,
                    new Point3d(x, len / baysY, bz0), new Point3d(x, 0, bz1));
            }
        }

        // ==================================================================
        // 8. 钢雨棚成套：多榀立柱 + 悬挑斜梁 + 斜撑 + 纵向檩条
        // ==================================================================
        internal static void BuildCanopyAssembly(
            List<SteelStructureBuilder.SolidMemberPart> parts,
            SectionProfile colProf,
            SectionProfile beamProf,
            SectionProfile purlinProf,
            double totalLength,
            double cantileverW,
            double eaveH,
            double slopeRise,
            int bayCount)
        {
            if (parts == null) return;

            totalLength = Math.Max(totalLength, 3000.0);
            cantileverW = Math.Max(cantileverW, 1800.0);
            eaveH = Math.Max(eaveH, 2600.0);
            slopeRise = Math.Max(slopeRise, 250.0);
            int bays = Math.Max(2, Math.Min(12, bayCount));
            double bayPitch = totalLength / bays;
            double tipZ = eaveH + slopeRise;
            double braceDrop = Math.Min(eaveH * 0.42, cantileverW * 0.45);

            // ---- 1) 多榀雨棚立柱 + 悬挑斜梁 + 三角斜撑 ----
            for (int i = 0; i <= bays; i++)
            {
                double x = i * bayPitch;
                var basePt = new Point3d(x, 0, 0);
                SteelStructureBuilder.AddMember(parts, "雨棚立柱", colProf, basePt, new Point3d(x, 0, eaveH));
                SteelStructureBuilder.AddBasePlate(parts, "柱脚底板", basePt, colProf.H + 120, colProf.B + 120, 18);

                SteelStructureBuilder.AddMember(parts, "悬挑斜梁", beamProf,
                    new Point3d(x, 0, eaveH), new Point3d(x, cantileverW, tipZ));
                SteelStructureBuilder.AddMember(parts, "悬挑斜撑", purlinProf,
                    new Point3d(x, 0, eaveH - braceDrop),
                    new Point3d(x, cantileverW * 0.58, eaveH + slopeRise * 0.58));
            }

            // ---- 2) 纵向屋面檩条与柱顶系杆 ----
            int purlinCount = Math.Max(3, Math.Min(7, (int)Math.Round(cantileverW / 1000.0) + 1));
            for (int k = 0; k < purlinCount; k++)
            {
                double t = (double)k / (purlinCount - 1);
                double y = cantileverW * t;
                double z = eaveH + slopeRise * t + beamProf.H * 0.5;
                SteelStructureBuilder.AddMember(parts, k == 0 ? "柱顶系杆" : "雨棚檩条", purlinProf,
                    new Point3d(0, y, z), new Point3d(totalLength, y, z));
            }
        }
    }
}
