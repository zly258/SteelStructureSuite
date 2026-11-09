using System;
using System.IO;
using System.Xml.Serialization;
using Autodesk.AutoCAD.Geometry;
using SteelStructureSuite.Domain.Enums;

namespace SteelStructureSuite.Domain.Models
{
    [Serializable]
    public class SteelStructureParams
    {
        public string Tag { get; set; } = "ST-101";
        public ParametricStructureType StructureType { get; set; } = ParametricStructureType.PortalFrame;

        [XmlIgnore]
        public Point3d Origin { get; set; } = Point3d.Origin;

        public double OriginX
        {
            get => Origin.X;
            set => Origin = new Point3d(value, Origin.Y, Origin.Z);
        }

        public double OriginY
        {
            get => Origin.Y;
            set => Origin = new Point3d(Origin.X, value, Origin.Z);
        }

        public double OriginZ
        {
            get => Origin.Z;
            set => Origin = new Point3d(Origin.X, Origin.Y, value);
        }

        public double Span { get; set; } = 3000.0;
        public double Length { get; set; } = 1500.0;
        public double Height { get; set; } = 3600.0;
        public double SubHeight { get; set; } = 1800.0;
        public string ColumnSection { get; set; } = "HW200x200x8x12";
        public string BeamSection { get; set; } = "HW200x200x8x12";
        public string BraceSection { get; set; } = "L100x100x8";
        public string Material { get; set; } = "Q355B";
        public double RotationRad { get; set; } = 0.0;
        public double EndPointOffsetZ { get; set; } = 0.0;
        public int BayCount { get; set; } = 2;

        public string StructureTypeDisplayName
        {
            get
            {
                switch (StructureType)
                {
                    case ParametricStructureType.PortalFrame: return "单跨门式刚架";
                    case ParametricStructureType.MultiSpanPortalFrame: return "多跨连续门式刚架";
                    case ParametricStructureType.BracedFrame: return "带人字斜撑框架";
                    case ParametricStructureType.DoubleTierFrame: return "双层管廊支架";
                    case ParametricStructureType.MultiBaySpaceFrame: return "多层空间装置钢框架";
                    case ParametricStructureType.CantileverBracket: return "悬臂三角托架";
                    case ParametricStructureType.SpacePlatform: return "四柱空间检修钢平台";
                    case ParametricStructureType.CantileverWalkway: return "悬挑走道平台(带护栏)";
                    case ParametricStructureType.RoofTruss: return "双坡屋面钢桁架";
                    case ParametricStructureType.PipeBridgeTruss: return "空间管廊桁架桥";
                    case ParametricStructureType.SpiralStair: return "储罐连续螺旋盘梯";
                    case ParametricStructureType.StraightStair: return "直跑钢斜梯(带双侧扶手)";
                    case ParametricStructureType.SafetyCagedLadder: return "带安全护笼钢直梯";
                    case ParametricStructureType.HandrailGuardrail: return "平台安全栏杆扶手";
                    case ParametricStructureType.LinearSteelMember: return "直线型钢(梁/柱/撑)";
                    case ParametricStructureType.VesselSupport: return "容器鞍式钢支座";
                    case ParametricStructureType.ArcSteelMember: return "弧形型钢";
                    case ParametricStructureType.SpiralSteelMember: return "螺旋型钢";
                    default: return "参数化钢结构";
                }
            }
        }

        /// <summary>功能区与模型树使用的构件大类，不写入图元。</summary>
        public string CategoryName
        {
            get
            {
                switch (StructureType)
                {
                    case ParametricStructureType.PortalFrame:
                    case ParametricStructureType.MultiSpanPortalFrame:
                    case ParametricStructureType.BracedFrame:
                    case ParametricStructureType.DoubleTierFrame:
                    case ParametricStructureType.MultiBaySpaceFrame:
                        return "框架";
                    case ParametricStructureType.SpacePlatform:
                    case ParametricStructureType.CantileverBracket:
                    case ParametricStructureType.CantileverWalkway:
                        return "平台托架";
                    case ParametricStructureType.RoofTruss:
                    case ParametricStructureType.PipeBridgeTruss:
                        return "桁架";
                    case ParametricStructureType.SpiralStair:
                    case ParametricStructureType.StraightStair:
                    case ParametricStructureType.SafetyCagedLadder:
                    case ParametricStructureType.HandrailGuardrail:
                        return "梯道栏杆";
                    case ParametricStructureType.LinearSteelMember:
                    case ParametricStructureType.ArcSteelMember:
                    case ParametricStructureType.SpiralSteelMember:
                        return "单根型钢";
                    case ParametricStructureType.VesselSupport:
                        return "支座";
                    default:
                        return "其他";
                }
            }
        }

        public int CategoryOrder
        {
            get
            {
                switch (CategoryName)
                {
                    case "框架": return 1;
                    case "平台托架": return 2;
                    case "桁架": return 3;
                    case "梯道栏杆": return 4;
                    case "单根型钢": return 5;
                    case "支座": return 6;
                    default: return 9;
                }
            }
        }

        /// <summary>
        /// 线状构件默认走两点：长度和方向就是绘制结果。
        /// 框架、桁架、平台也可以两点，但默认仍是单点放下。
        /// </summary>
        public bool PrefersLinearTwoPointDraw
        {
            get
            {
                return StructureType == ParametricStructureType.HandrailGuardrail ||
                       StructureType == ParametricStructureType.StraightStair ||
                       StructureType == ParametricStructureType.LinearSteelMember ||
                       StructureType == ParametricStructureType.ArcSteelMember ||
                       StructureType == ParametricStructureType.SpiralSteelMember ||
                       StructureType == ParametricStructureType.CantileverWalkway ||
                       StructureType == ParametricStructureType.PipeBridgeTruss;
            }
        }

        /// <summary>
        /// 能否两点放置。盘梯、护笼直梯、鞍座只按插入点放下，避免把外径或宽度拉乱。
        /// </summary>
        public bool SupportsTwoPointDraw
        {
            get
            {
                return PrefersLinearTwoPointDraw ||
                       StructureType == ParametricStructureType.PortalFrame ||
                       StructureType == ParametricStructureType.MultiSpanPortalFrame ||
                       StructureType == ParametricStructureType.BracedFrame ||
                       StructureType == ParametricStructureType.DoubleTierFrame ||
                       StructureType == ParametricStructureType.MultiBaySpaceFrame ||
                       StructureType == ParametricStructureType.CantileverBracket ||
                       StructureType == ParametricStructureType.SpacePlatform ||
                       StructureType == ParametricStructureType.RoofTruss;
            }
        }

        public string TwoPointButtonText
        {
            get
            {
                switch (StructureType)
                {
                    case ParametricStructureType.ArcSteelMember: return "两点定半径";
                    case ParametricStructureType.LinearSteelMember:
                    case ParametricStructureType.SpiralSteelMember: return "两点绘轴线";
                    case ParametricStructureType.StraightStair: return "两点绘梯段";
                    default: return PrefersLinearTwoPointDraw ? "两点绘制" : "两点定跨向";
                }
            }
        }

        public string TwoPointStartPrompt
        {
            get
            {
                switch (StructureType)
                {
                    case ParametricStructureType.ArcSteelMember:
                        return $"\n请拾取 [{Tag}] 弧形型钢的圆心: ";
                    case ParametricStructureType.LinearSteelMember:
                    case ParametricStructureType.SpiralSteelMember:
                        return $"\n请拾取 [{Tag}] 的轴线起点: ";
                    default:
                        return $"\n请拾取 [{Tag}] {StructureTypeDisplayName} 的起点: ";
                }
            }
        }

        public string TwoPointEndPrompt
        {
            get
            {
                switch (StructureType)
                {
                    case ParametricStructureType.ArcSteelMember:
                        return $"\n指定 [{StructureTypeDisplayName}] 的半径与起始方向（扫掠角取对话框）: ";
                    case ParametricStructureType.SpiralSteelMember:
                        return $"\n指定 [{StructureTypeDisplayName}] 的轴线终点（半径与圈数取对话框）: ";
                    case ParametricStructureType.LinearSteelMember:
                        return $"\n指定 [{StructureTypeDisplayName}] 的轴线终点: ";
                    case ParametricStructureType.StraightStair:
                        return $"\n指定 [{StructureTypeDisplayName}] 的梯段终点（平距为投影长，高差较大时作为层高）: ";
                    default:
                        return PrefersLinearTwoPointDraw
                            ? $"\n指定 [{StructureTypeDisplayName}] 的终点（长度与方向）: "
                            : $"\n指定 [{StructureTypeDisplayName}] 的跨度终点（只改跨度与方向，其余尺寸用对话框）: ";
                }
            }
        }

        /// <summary>
        /// 是否支持“连续折线绘制”。仅线状构件可首尾相接，框架/平台/梯子等不可连续拼接。
        /// </summary>
        public bool SupportsChainDraw
        {
            get
            {
                return StructureType == ParametricStructureType.HandrailGuardrail ||
                       StructureType == ParametricStructureType.LinearSteelMember ||
                       StructureType == ParametricStructureType.CantileverWalkway ||
                       StructureType == ParametricStructureType.PipeBridgeTruss;
            }
        }

        public string BuildSpecSummary()
        {
            switch (StructureType)
            {
                case ParametricStructureType.HandrailGuardrail:
                    return $"{StructureTypeDisplayName} L={Span:0.#}xH={Height:0.#} ({BeamSection}/{ColumnSection})";
                case ParametricStructureType.StraightStair:
                    return $"{StructureTypeDisplayName} L={Span:0.#}xB={Length:0.#}xH={Height:0.#} ({ColumnSection})";
                case ParametricStructureType.SafetyCagedLadder:
                    return $"{StructureTypeDisplayName} W={Span:0.#}xH={Height:0.#}";
                case ParametricStructureType.LinearSteelMember:
                    return $"{StructureTypeDisplayName} L={Span:0.#} ({ColumnSection})";
                case ParametricStructureType.ArcSteelMember:
                    return $"{StructureTypeDisplayName} R={Span:0.#} ∠{SubHeight:0.#}° ({ColumnSection})";
                case ParametricStructureType.SpiralSteelMember:
                    return $"{StructureTypeDisplayName} L={Span:0.#} R={Length:0.#} ×{SubHeight:0.#}圈 ({ColumnSection})";
                case ParametricStructureType.SpacePlatform:
                case ParametricStructureType.MultiBaySpaceFrame:
                case ParametricStructureType.CantileverWalkway:
                case ParametricStructureType.PipeBridgeTruss:
                    return $"{StructureTypeDisplayName} {Span:0.#}x{Length:0.#}x{Height:0.#} ({ColumnSection}/{BeamSection})";
                case ParametricStructureType.SpiralStair:
                    return $"{StructureTypeDisplayName} D={Span:0.#}xH={Height:0.#}";
                case ParametricStructureType.VesselSupport:
                    return $"{StructureTypeDisplayName} DN={Span:0.#}xH={Height:0.#}";
                default:
                    return $"{StructureTypeDisplayName} B={Span:0.#}xH={Height:0.#} ({ColumnSection}/{BeamSection})";
            }
        }

        public void SaveToXmlFile(string filePath)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(SteelStructureParams));
            using (StreamWriter sw = new StreamWriter(filePath, false, System.Text.Encoding.UTF8))
            {
                serializer.Serialize(sw, this);
            }
        }

        public static SteelStructureParams LoadFromXmlFile(string filePath)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(SteelStructureParams));
            using (StreamReader sr = new StreamReader(filePath, System.Text.Encoding.UTF8))
            {
                return (SteelStructureParams)serializer.Deserialize(sr);
            }
        }

        public SteelStructureParams Clone()
        {
            return new SteelStructureParams
            {
                Tag = Tag,
                StructureType = StructureType,
                Origin = Origin,
                Span = Span,
                Length = Length,
                Height = Height,
                SubHeight = SubHeight,
                ColumnSection = ColumnSection,
                BeamSection = BeamSection,
                BraceSection = BraceSection,
                Material = Material,
                RotationRad = RotationRad,
                EndPointOffsetZ = EndPointOffsetZ,
                BayCount = BayCount
            };
        }
    }
}
