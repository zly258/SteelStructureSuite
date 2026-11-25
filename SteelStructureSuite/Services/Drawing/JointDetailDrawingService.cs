using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Modeling;

namespace SteelStructureSuite.Services.Drawing
{
    /// <summary>
    /// 接头局部放大示意图：把构件中「斜接/切割」的位置单独放大画出来，
    /// 并标注切割角度、斜接长度、截面尺寸等关键数据，供加工与检验使用。
    ///
    /// 数据来源：<see cref="SteelJointService.PlanJoint"/> 的纯几何判定结果，
    /// 因此图中标注的角度与实际三维模型的斜接面完全一致。
    /// </summary>
    public static class JointDetailDrawingService
    {

        /// <summary>用绝对坐标画线（内部换算成 AppendLine2D 需要的相对坐标）。</summary>
        private static void Line(
            Transaction tr,
            BlockTableRecord ms,
            Point3d a,
            Point3d b,
            string layer)
        {
            SteelHlrDrawingService.AppendLine2D(
                tr, ms, Point3d.Origin,
                a.X, a.Y, b.X, b.Y, layer);
        }

        /// <summary>一个接头节点的描述。</summary>
        public sealed class JointNode
        {
            public string Label { get; set; }
            public string RoleA { get; set; }
            public string RoleB { get; set; }
            public Point3d Position { get; set; }
            /// <summary>斜接面与 A 杆轴线的夹角（度）。</summary>
            public double MiterAngleDeg { get; set; }
            /// <summary>两杆轴线的夹角（度）。</summary>
            public double IntersectionAngleDeg { get; set; }
            /// <summary>斜接面法向在 A 杆截面内的投影长度（mm），即斜接切口高度。</summary>
            public double MiterFaceLengthMm { get; set; }
            public double SectionAHeightMm { get; set; }
            public double SectionAWidthMm { get; set; }
            public double SectionBHeightMm { get; set; }
            public double SectionBWidthMm { get; set; }
        }

        /// <summary>
        /// 从构件零件列表里提取所有需要斜接的接头节点。
        /// </summary>
        public static List<JointNode> CollectJointNodes(IList<SteelStructureBuilder.SolidMemberPart> parts)
        {
            var nodes = new List<JointNode>();
            if (parts == null || parts.Count < 2) return nodes;

            var candidates = new List<int>();
            for (int i = 0; i < parts.Count; i++)
            {
                SteelStructureBuilder.SolidMemberPart p = parts[i];
                if (p?.Section == null || p.IsPlate) continue;
                if (p.PathPoints != null && p.PathPoints.Count > 2) continue;
                if (p.StartPt.DistanceTo(p.EndPt) < 20.0) continue;
                candidates.Add(i);
            }

            int seq = 1;
            for (int a = 0; a < candidates.Count; a++)
            {
                for (int b = a + 1; b < candidates.Count; b++)
                {
                    SteelStructureBuilder.SolidMemberPart pa = parts[candidates[a]];
                    SteelStructureBuilder.SolidMemberPart pb = parts[candidates[b]];

                    JointNode node = BuildNode(pa, pb);
                    if (node == null) continue;

                    node.Label = $"J{seq++}";
                    nodes.Add(node);
                }
            }

            // 同一个位置可能被多对杆件重复识别，按坐标去重
            var unique = new List<JointNode>();
            foreach (JointNode n in nodes)
            {
                bool dup = unique.Any(u => u.Position.DistanceTo(n.Position) < 1.0);
                if (!dup) unique.Add(n);
            }
            return unique;
        }

        private static JointNode BuildNode(
            SteelStructureBuilder.SolidMemberPart pa,
            SteelStructureBuilder.SolidMemberPart pb)
        {
            double hwA = Math.Max(pa.Section.H, pa.Section.B) * 0.5;
            double hwB = Math.Max(pb.Section.H, pb.Section.B) * 0.5;

            SteelJointService.JointCut cut = SteelJointService.PlanJoint(
                0, pa.StartPt, pa.EndPt, hwA,
                1, pb.StartPt, pb.EndPt, hwB);

            if (cut == null) return null;

            // 两杆轴线夹角
            Vector3d da = (pa.EndPt - pa.StartPt).GetNormal();
            Vector3d db = (pb.EndPt - pb.StartPt).GetNormal();
            double cos = Math.Abs(da.DotProduct(db));
            cos = Math.Max(-1.0, Math.Min(1.0, cos));
            double intersectionDeg = Math.Acos(cos) * 180.0 / Math.PI;
            if (intersectionDeg > 90.0) intersectionDeg = 180.0 - intersectionDeg;

            // 斜接面与 A 杆轴线的夹角
            double miterDeg = 90.0;
            if (cut.NormalA.Length > 1e-9)
            {
                double c = Math.Abs(da.DotProduct(cut.NormalA.GetNormal()));
                c = Math.Max(-1.0, Math.Min(1.0, c));
                miterDeg = Math.Acos(c) * 180.0 / Math.PI;
            }

            // 斜接切口高度 = 截面高 / sin(斜接角)
            double sinMiter = Math.Sin(miterDeg * Math.PI / 180.0);
            double faceLen = sinMiter > 1e-3
                ? pa.Section.H / sinMiter
                : pa.Section.H;

            return new JointNode
            {
                RoleA = pa.RoleName,
                RoleB = pb.RoleName,
                Position = cut.PlanePoint,
                MiterAngleDeg = miterDeg,
                IntersectionAngleDeg = intersectionDeg,
                MiterFaceLengthMm = faceLen,
                SectionAHeightMm = pa.Section.H,
                SectionAWidthMm = pa.Section.B,
                SectionBHeightMm = pb.Section.H,
                SectionBWidthMm = pb.Section.B
            };
        }

        /// <summary>
        /// 在图面上绘制接头局部放大示意图。
        /// </summary>
        /// <param name="tr">事务。</param>
        /// <param name="ms">模型空间。</param>
        /// <param name="nodes">接头节点。</param>
        /// <param name="bottomLeft">示意图区域左下角。</param>
        /// <param name="maxWidth">可用宽度（图纸单位）。</param>
        /// <param name="textStyleId">文字样式。</param>
        /// <param name="textH">字高。</param>
        /// <param name="layer">图层名。</param>
        /// <returns>实际占用高度；无节点时返回 0。</returns>
        public static double DrawJointDetails(
            Transaction tr,
            BlockTableRecord ms,
            IList<JointNode> nodes,
            Point3d bottomLeft,
            double maxWidth,
            ObjectId textStyleId,
            double textH,
            string layer)
        {
            if (nodes == null || nodes.Count == 0) return 0.0;
            if (tr == null || ms == null) return 0.0;

            // 每个接头一个方框，横向排布，超出宽度换行
            double boxSize = Math.Max(textH * 14.0, 26.0);
            double labelH = textH * 1.4;
            double cellW = boxSize + textH * 3.0;
            double cellH = boxSize + labelH * 3.2;

            int perRow = Math.Max(1, (int)(maxWidth / cellW));
            int rows = (nodes.Count + perRow - 1) / perRow;

            // 区域标题
            SteelHlrDrawingService.AppendSimSunText(
                tr, ms,
                new Point3d(bottomLeft.X, bottomLeft.Y + rows * cellH + textH * 0.6, 0),
                $"接头局部放大示意图（共 {nodes.Count} 处）",
                textStyleId, textH * 1.15, layer);

            for (int i = 0; i < nodes.Count; i++)
            {
                JointNode node = nodes[i];
                int col = i % perRow;
                int row = i / perRow;

                var cellOrigin = new Point3d(
                    bottomLeft.X + col * cellW,
                    bottomLeft.Y + (rows - 1 - row) * cellH,
                    0);

                DrawSingleJoint(tr, ms, node, cellOrigin, boxSize, textH, textStyleId, layer);
            }

            return rows * cellH + textH * 1.8;
        }

        private static void DrawSingleJoint(
            Transaction tr,
            BlockTableRecord ms,
            JointNode node,
            Point3d origin,
            double boxSize,
            double textH,
            ObjectId textStyleId,
            string layer)
        {
            // 外框
            SteelHlrDrawingService.DrawRectangle2D(tr, ms, origin, 0, 0, boxSize, boxSize, layer);

            double cx = origin.X + boxSize * 0.5;
            double cy = origin.Y + boxSize * 0.5;

            // 示意画法：一条竖杆 + 一条斜杆，斜接面按实际角度绘制
            double armLen = boxSize * 0.34;
            double halfW = Math.Max(textH * 0.35, armLen * 0.09);

            // 竖杆（A）
            Line(tr, ms, new Point3d(cx - halfW, cy - armLen, 0), new Point3d(cx - halfW, cy + armLen, 0), layer);
            Line(tr, ms, new Point3d(cx + halfW, cy - armLen, 0), new Point3d(cx + halfW, cy + armLen, 0), layer);

            // 斜杆（B）：按实际夹角倾斜
            double angleRad = node.IntersectionAngleDeg * Math.PI / 180.0;
            if (angleRad < 0.05) angleRad = Math.PI * 0.25;
            double dx = Math.Cos(angleRad) * armLen;
            double dy = Math.Sin(angleRad) * armLen;

            Vector3d dirB = new Vector3d(dx, dy, 0).GetNormal();
            Vector3d perpB = new Vector3d(-dirB.Y, dirB.X, 0);

            Point3d b0 = new Point3d(cx + perpB.X * halfW, cy + perpB.Y * halfW, 0);
            Point3d b1 = b0 + dirB * armLen * 1.6;
            Line(tr, ms, b0, b1, layer);

            Point3d b2 = new Point3d(cx - perpB.X * halfW, cy - perpB.Y * halfW, 0);
            Point3d b3 = b2 + dirB * armLen * 1.6;
            Line(tr, ms, b2, b3, layer);

            // 斜接面（用醒目角度画出实际切割线）
            Line(tr, ms, b0, b2, layer);

            // 角度弧与角度标注
            double arcR = armLen * 0.42;
            DrawAngleArc(tr, ms, new Point3d(cx, cy, 0), arcR, angleRad, layer);
            SteelHlrDrawingService.AppendSimSunText(
                tr, ms,
                new Point3d(cx + arcR * 0.62, cy + arcR * 0.30, 0),
                $"{node.IntersectionAngleDeg:0.#}°",
                textStyleId, textH * 0.9, layer);

            // 节点编号
            SteelHlrDrawingService.AppendSimSunText(
                tr, ms,
                new Point3d(origin.X + textH * 0.5, origin.Y + boxSize - textH * 1.3, 0),
                node.Label, textStyleId, textH, layer);

            // 下方说明：构件 + 切口长度
            double infoY = origin.Y - textH * 1.35;
            string line1 = $"{Short(node.RoleA)} / {Short(node.RoleB)}";
            string line2 = string.Format(
                CultureInfo.CurrentCulture,
                "斜接 {0:0.#}°  切口 {1:0} mm",
                node.MiterAngleDeg,
                node.MiterFaceLengthMm);

            SteelHlrDrawingService.AppendSimSunText(
                tr, ms, new Point3d(origin.X, infoY, 0), line1, textStyleId, textH * 0.85, layer);
            SteelHlrDrawingService.AppendSimSunText(
                tr, ms, new Point3d(origin.X, infoY - textH * 1.05, 0), line2, textStyleId, textH * 0.85, layer);
        }

        private static string Short(string role)
        {
            if (string.IsNullOrEmpty(role)) return "-";
            return role.Length <= 6 ? role : role.Substring(0, 6);
        }

        private static void DrawAngleArc(
            Transaction tr,
            BlockTableRecord ms,
            Point3d center,
            double radius,
            double angleRad,
            string layer)
        {
            const int segments = 12;
            for (int i = 0; i < segments; i++)
            {
                double a0 = angleRad * i / segments;
                double a1 = angleRad * (i + 1) / segments;
                var p0 = new Point3d(center.X + radius * Math.Cos(a0), center.Y + radius * Math.Sin(a0), 0);
                var p1 = new Point3d(center.X + radius * Math.Cos(a1), center.Y + radius * Math.Sin(a1), 0);
                Line(tr, ms, p0, p1, layer);
            }
        }
    }
}
