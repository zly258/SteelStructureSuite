using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Autodesk.AutoCAD.Geometry;
using SteelStructureSuite.Domain.Enums;

namespace SteelStructureSuite.Domain.Models
{
    public class StructurePresetItem
    {
        public string Name { get; set; }
        public double Span { get; set; }
        public double Length { get; set; }
        public double Height { get; set; }
        public double SubHeight { get; set; }
        public int BayCount { get; set; } = 2;
        public string ColSec { get; set; }
        public string BeamSec { get; set; }
        public string BraceSec { get; set; }
    }

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
        public bool UseCurveFrame { get; set; }
        public bool HelixCounterClockwise { get; set; } = true;
        public double TopRadius { get; set; }
        public double FrameXx { get; set; } = 1.0;
        public double FrameXy { get; set; }
        public double FrameXz { get; set; }
        public double FrameYx { get; set; }
        public double FrameYy { get; set; } = 1.0;
        public double FrameYz { get; set; }
        public double FrameZx { get; set; }
        public double FrameZy { get; set; }
        public double FrameZz { get; set; } = 1.0;

        [XmlIgnore]
        public Vector3d FrameX
        {
            get => new Vector3d(FrameXx, FrameXy, FrameXz);
            set
            {
                FrameXx = value.X;
                FrameXy = value.Y;
                FrameXz = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3d FrameY
        {
            get => new Vector3d(FrameYx, FrameYy, FrameYz);
            set
            {
                FrameYx = value.X;
                FrameYy = value.Y;
                FrameYz = value.Z;
            }
        }

        [XmlIgnore]
        public Vector3d FrameZ
        {
            get => new Vector3d(FrameZx, FrameZy, FrameZz);
            set
            {
                FrameZx = value.X;
                FrameZy = value.Y;
                FrameZz = value.Z;
            }
        }

        [XmlIgnore]
        public double RotationDeg
        {
            get => Math.Round(RotationRad * 180.0 / Math.PI, 1);
            set => RotationRad = value * Math.PI / 180.0;
        }

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
                    case ParametricStructureType.ArcSteelMember: return "弧形型钢";
                    case ParametricStructureType.SpiralSteelMember: return "螺旋型钢";
                    case ParametricStructureType.VesselSupport: return "卧式容器鞍式支座";
                    case ParametricStructureType.TowerCircularPlatform: return "塔器环形检修平台(HG/T)";
                    case ParametricStructureType.VerticalVesselSkirt: return "立式塔器钢裙座(NB/T)";
                    case ParametricStructureType.PipeShoeSupport: return "化工管道加劲管托(HG/T)";
                    case ParametricStructureType.TankRoofHandrail: return "储罐拱顶环形栏杆(SH/T)";
                    case ParametricStructureType.SubstationGantryFrame: return "变电站A字龙门构架(DL/T)";
                    case ParametricStructureType.CableTrenchSupport: return "电力电缆沟多层支架(DL/T)";
                    case ParametricStructureType.CraneRunwayGirder: return "冶金重型吊车梁(16G519)";
                    case ParametricStructureType.CrossBracingFrame: return "冶金柱间交叉支撑(19G518)";
                    case ParametricStructureType.PlatformAssembly: return "钢平台成套(柱+梁+板+栏杆+梯)";
                    case ParametricStructureType.ColumnRowAssembly: return "厂房柱列(柱+支撑+吊车梁+系杆)";
                    case ParametricStructureType.PipeRackAssembly: return "管廊成套区段(多层按跨数)";
                    case ParametricStructureType.StairAssembly: return "楼梯成套(梯梁+踏步+扶手+平台)";
                    default: return "参数化钢结构";
                }
            }
        }

        /// <summary>功能区与模型树使用的工业大类</summary>
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
                    case ParametricStructureType.RoofTruss:
                        return "厂房与装置框架";
                    case ParametricStructureType.SpacePlatform:
                    case ParametricStructureType.CantileverBracket:
                    case ParametricStructureType.CantileverWalkway:
                    case ParametricStructureType.PipeBridgeTruss:
                    case ParametricStructureType.SpiralStair:
                    case ParametricStructureType.StraightStair:
                    case ParametricStructureType.SafetyCagedLadder:
                    case ParametricStructureType.HandrailGuardrail:
                        return "平台梯道与栏杆";
                    case ParametricStructureType.VesselSupport:
                    case ParametricStructureType.TowerCircularPlatform:
                    case ParametricStructureType.VerticalVesselSkirt:
                    case ParametricStructureType.PipeShoeSupport:
                    case ParametricStructureType.TankRoofHandrail:
                        return "化工设备标准件";
                    case ParametricStructureType.SubstationGantryFrame:
                    case ParametricStructureType.CableTrenchSupport:
                    case ParametricStructureType.CraneRunwayGirder:
                    case ParametricStructureType.CrossBracingFrame:
                        return "电力与冶金标准件";
                    case ParametricStructureType.LinearSteelMember:
                    case ParametricStructureType.ArcSteelMember:
                    case ParametricStructureType.SpiralSteelMember:
                        return "单根型钢构件";
                    case ParametricStructureType.PlatformAssembly:
                    case ParametricStructureType.ColumnRowAssembly:
                    case ParametricStructureType.PipeRackAssembly:
                    case ParametricStructureType.StairAssembly:
                        return "区域组合构件";
                    default:
                        return "其他结构";
                }
            }
        }

        public int CategoryOrder
        {
            get
            {
                switch (CategoryName)
                {
                    case "厂房与装置框架": return 1;
                    case "平台梯道与栏杆": return 2;
                    case "化工设备标准件": return 3;
                    case "电力与冶金标准件": return 4;
                    case "单根型钢构件": return 5;
                    case "区域组合构件": return 6;
                    default: return 9;
                }
            }
        }

        public bool PrefersLinearTwoPointDraw
        {
            get
            {
                return StructureType == ParametricStructureType.HandrailGuardrail ||
                       StructureType == ParametricStructureType.StraightStair ||
                       StructureType == ParametricStructureType.LinearSteelMember ||
                       StructureType == ParametricStructureType.CantileverWalkway ||
                       StructureType == ParametricStructureType.PipeBridgeTruss ||
                       StructureType == ParametricStructureType.CableTrenchSupport ||
                       StructureType == ParametricStructureType.CraneRunwayGirder ||
                       StructureType == ParametricStructureType.CrossBracingFrame;
            }
        }

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
                       StructureType == ParametricStructureType.RoofTruss ||
                       StructureType == ParametricStructureType.SubstationGantryFrame ||
                       StructureType == ParametricStructureType.PipeShoeSupport;
            }
        }

        public bool RequiresPickedCurve =>
            StructureType == ParametricStructureType.ArcSteelMember ||
            StructureType == ParametricStructureType.SpiralSteelMember;

        public string PickedCurveButtonText =>
            StructureType == ParametricStructureType.SpiralSteelMember ? "选择螺旋线" : "选择圆弧";

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

        public bool SupportsChainDraw
        {
            get
            {
                return StructureType == ParametricStructureType.HandrailGuardrail ||
                       StructureType == ParametricStructureType.LinearSteelMember ||
                       StructureType == ParametricStructureType.CantileverWalkway ||
                       StructureType == ParametricStructureType.PipeBridgeTruss ||
                       StructureType == ParametricStructureType.CableTrenchSupport ||
                       StructureType == ParametricStructureType.CraneRunwayGirder ||
                       StructureType == ParametricStructureType.CrossBracingFrame;
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
                    return $"{StructureTypeDisplayName} H={Span:0.#} R={Length:0.#} ×{SubHeight:0.#}圈 ({ColumnSection})";
                case ParametricStructureType.SpacePlatform:
                case ParametricStructureType.MultiBaySpaceFrame:
                case ParametricStructureType.CantileverWalkway:
                case ParametricStructureType.PipeBridgeTruss:
                    return $"{StructureTypeDisplayName} {Span:0.#}x{Length:0.#}x{Height:0.#} ({ColumnSection}/{BeamSection})";
                case ParametricStructureType.SpiralStair:
                    return $"{StructureTypeDisplayName} D={Span:0.#}xH={Height:0.#}";
                case ParametricStructureType.VesselSupport:
                case ParametricStructureType.TowerCircularPlatform:
                case ParametricStructureType.VerticalVesselSkirt:
                case ParametricStructureType.PipeShoeSupport:
                case ParametricStructureType.TankRoofHandrail:
                    return $"{StructureTypeDisplayName} DN={Span:0.#}xH={Height:0.#}";
                case ParametricStructureType.SubstationGantryFrame:
                case ParametricStructureType.CableTrenchSupport:
                case ParametricStructureType.CraneRunwayGirder:
                case ParametricStructureType.CrossBracingFrame:
                    return $"{StructureTypeDisplayName} L={Span:0.#}xH={Height:0.#} ({ColumnSection})";
                default:
                    return $"{StructureTypeDisplayName} B={Span:0.#}xH={Height:0.#} ({ColumnSection}/{BeamSection})";
            }
        }

        public static List<StructurePresetItem> GetPresetsForType(ParametricStructureType type)
        {
            switch (type)
            {
                case ParametricStructureType.PortalFrame:
                    return new List<StructurePresetItem>
                    {
                        new StructurePresetItem { Name = "轻型门架 (B=3000, H=3600)", Span = 3000, Length = 1500, Height = 3600, SubHeight = 1800, ColSec = "HW200x200x8x12", BeamSec = "HW200x200x8x12", BraceSec = "L75x75x6" },
                        new StructurePresetItem { Name = "中型门架 (B=6000, H=4800)", Span = 6000, Length = 1500, Height = 4800, SubHeight = 2400, ColSec = "HW250x250x9x14", BeamSec = "HN300x150x6.5x9", BraceSec = "L100x100x8" },
                        new StructurePresetItem { Name = "重型门架 (B=9000, H=6500)", Span = 9000, Length = 1500, Height = 6500, SubHeight = 3200, ColSec = "HW300x300x10x15", BeamSec = "HW300x300x10x15", BraceSec = "L125x125x10" }
                    };
                case ParametricStructureType.TowerCircularPlatform:
                    return new List<StructurePresetItem>
                    {
                        new StructurePresetItem { Name = "HG/T DN1600 塔环形平台 (180°)", Span = 1600, Length = 1000, Height = 900, SubHeight = 180, BayCount = 5, ColSec = "C16a", BeamSec = "C14a", BraceSec = "L75x75x6" },
                        new StructurePresetItem { Name = "HG/T DN2400 塔环形平台 (270°)", Span = 2400, Length = 1100, Height = 1000, SubHeight = 270, BayCount = 7, ColSec = "C20a", BeamSec = "C16a", BraceSec = "L90x90x8" },
                        new StructurePresetItem { Name = "HG/T DN3600 塔全周平台 (360°)", Span = 3600, Length = 1200, Height = 1100, SubHeight = 360, BayCount = 10, ColSec = "HW200x200x8x12", BeamSec = "C20a", BraceSec = "L100x100x8" }
                    };
                case ParametricStructureType.VerticalVesselSkirt:
                    return new List<StructurePresetItem>
                    {
                        new StructurePresetItem { Name = "NB/T DN1800 塔器裙座 (H=3000)", Span = 1800, Length = 260, Height = 3000, SubHeight = 500, BayCount = 12, ColSec = "HW200x200x8x12", BeamSec = "C20a", BraceSec = "L75x75x6" },
                        new StructurePresetItem { Name = "NB/T DN2600 塔器裙座 (H=4200)", Span = 2600, Length = 320, Height = 4200, SubHeight = 600, BayCount = 16, ColSec = "HW250x250x9x14", BeamSec = "C25a", BraceSec = "L100x100x8" },
                        new StructurePresetItem { Name = "NB/T DN3600 重型裙座 (H=5500)", Span = 3600, Length = 380, Height = 5500, SubHeight = 700, BayCount = 20, ColSec = "HW300x300x10x15", BeamSec = "C25a", BraceSec = "L125x125x10" }
                    };
                case ParametricStructureType.PipeShoeSupport:
                    return new List<StructurePresetItem>
                    {
                        new StructurePresetItem { Name = "HG/T 21629 DN300 加劲管托", Span = 325, Length = 400, Height = 300, SubHeight = 260, ColSec = "HW150x150x7x10", BeamSec = "C14a", BraceSec = "L63x63x6" },
                        new StructurePresetItem { Name = "HG/T 21629 DN600 加劲管托", Span = 630, Length = 500, Height = 480, SubHeight = 420, ColSec = "HW200x200x8x12", BeamSec = "C16a", BraceSec = "L75x75x6" },
                        new StructurePresetItem { Name = "HG/T 21629 DN1000 重型管托", Span = 1020, Length = 600, Height = 720, SubHeight = 650, ColSec = "HW250x250x9x14", BeamSec = "C20a", BraceSec = "L100x100x8" }
                    };
                case ParametricStructureType.SubstationGantryFrame:
                    return new List<StructurePresetItem>
                    {
                        new StructurePresetItem { Name = "110kV 变电A字构架 (B=9000, H=11000)", Span = 9000, Length = 2200, Height = 11000, SubHeight = 2500, ColSec = "P219x8", BeamSec = "L100x100x8", BraceSec = "L63x63x6" },
                        new StructurePresetItem { Name = "220kV 变电A字构架 (B=14000, H=15000)", Span = 14000, Length = 3000, Height = 15000, SubHeight = 3500, ColSec = "P219x8", BeamSec = "L125x125x10", BraceSec = "L75x75x6" }
                    };
                case ParametricStructureType.CableTrenchSupport:
                    return new List<StructurePresetItem>
                    {
                        new StructurePresetItem { Name = "DL/T 3层电缆沟支架 (L=4000, W=450)", Span = 4000, Length = 450, Height = 1200, SubHeight = 300, BayCount = 3, ColSec = "C10", BeamSec = "L50x50x5", BraceSec = "L50x50x5" },
                        new StructurePresetItem { Name = "DL/T 5层电缆隧道支架 (L=6000, W=600)", Span = 6000, Length = 600, Height = 1850, SubHeight = 320, BayCount = 5, ColSec = "C12.6", BeamSec = "L63x63x6", BraceSec = "L50x50x5" }
                    };
                case ParametricStructureType.CraneRunwayGirder:
                    return new List<StructurePresetItem>
                    {
                        new StructurePresetItem { Name = "16G519 6m跨度吊车梁 (L=6000, H=800)", Span = 6000, Length = 650, Height = 800, SubHeight = 1000, ColSec = "HW300x300x10x15", BeamSec = "C20a", BraceSec = "L75x75x6" },
                        new StructurePresetItem { Name = "16G519 7.5m重型吊车梁 (L=7500, H=1000)", Span = 7500, Length = 800, Height = 1000, SubHeight = 1250, ColSec = "HW300x300x10x15", BeamSec = "C25a", BraceSec = "L100x100x8" },
                        new StructurePresetItem { Name = "16G519 9m冶金吊车梁 (L=9000, H=1200)", Span = 9000, Length = 950, Height = 1200, SubHeight = 1500, ColSec = "HW300x300x10x15", BeamSec = "C25a", BraceSec = "L125x125x10" }
                    };
                case ParametricStructureType.CrossBracingFrame:
                    return new List<StructurePresetItem>
                    {
                        new StructurePresetItem { Name = "19G518 6m柱距双层交叉支撑 (H=7200)", Span = 6000, Length = 250, Height = 7200, SubHeight = 4800, ColSec = "HW250x250x9x14", BeamSec = "F150x150x8", BraceSec = "L100x100x8" },
                        new StructurePresetItem { Name = "19G518 7.5m柱距重型柱间支撑 (H=9600)", Span = 7500, Length = 300, Height = 9600, SubHeight = 6500, ColSec = "HW300x300x10x15", BeamSec = "F200x200x10", BraceSec = "L125x125x10" }
                    };
                default:
                    return new List<StructurePresetItem>
                    {
                        new StructurePresetItem { Name = "标准工况 1 (轻型)", Span = 2400, Length = 1200, Height = 3000, SubHeight = 1050, ColSec = "HW150x150x7x10", BeamSec = "HM194x150x6x9", BraceSec = "L63x63x6" },
                        new StructurePresetItem { Name = "标准工况 2 (中型推荐)", Span = 3600, Length = 1800, Height = 4200, SubHeight = 1800, ColSec = "HW200x200x8x12", BeamSec = "HW200x200x8x12", BraceSec = "L75x75x6" },
                        new StructurePresetItem { Name = "标准工况 3 (重型)", Span = 5400, Length = 2400, Height = 5600, SubHeight = 2600, ColSec = "HW250x250x9x14", BeamSec = "HN300x150x6.5x9", BraceSec = "L100x100x8" }
                    };
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

        /// <summary>
        /// 从另一份参数整体复制（含位号、插入点、坐标系）。
        /// 供模板套用与预设应用使用，避免各处重复写字段。
        /// </summary>
        public void CopyFrom(SteelStructureParams source)
        {
            if (source == null || ReferenceEquals(source, this)) return;

            Tag = source.Tag;
            StructureType = source.StructureType;
            Origin = source.Origin;
            Span = source.Span;
            Length = source.Length;
            Height = source.Height;
            SubHeight = source.SubHeight;
            ColumnSection = source.ColumnSection;
            BeamSection = source.BeamSection;
            BraceSection = source.BraceSection;
            Material = source.Material;
            RotationRad = source.RotationRad;
            EndPointOffsetZ = source.EndPointOffsetZ;
            BayCount = source.BayCount;
            UseCurveFrame = source.UseCurveFrame;
            HelixCounterClockwise = source.HelixCounterClockwise;
            TopRadius = source.TopRadius;
            FrameXx = source.FrameXx;
            FrameXy = source.FrameXy;
            FrameXz = source.FrameXz;
            FrameYx = source.FrameYx;
            FrameYy = source.FrameYy;
            FrameYz = source.FrameYz;
            FrameZx = source.FrameZx;
            FrameZy = source.FrameZy;
            FrameZz = source.FrameZz;
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
                BayCount = BayCount,
                UseCurveFrame = UseCurveFrame,
                HelixCounterClockwise = HelixCounterClockwise,
                TopRadius = TopRadius,
                FrameXx = FrameXx,
                FrameXy = FrameXy,
                FrameXz = FrameXz,
                FrameYx = FrameYx,
                FrameYy = FrameYy,
                FrameYz = FrameYz,
                FrameZx = FrameZx,
                FrameZy = FrameZy,
                FrameZz = FrameZz
            };
        }
    }
}
