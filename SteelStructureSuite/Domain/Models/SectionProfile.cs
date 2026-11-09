using System;
using SteelStructureSuite.Domain.Enums;

namespace SteelStructureSuite.Domain.Models
{
    public class SectionProfile
    {
        public string Name { get; set; } = "HW200x200x8x12";
        public SectionShapeType Shape { get; set; } = SectionShapeType.HBeam;

        public SectionShapeType ShapeType
        {
            get => Shape;
            set => Shape = value;
        }

        public double H { get; set; } = 200.0;
        public double B { get; set; } = 200.0;
        public double Tw { get; set; } = 8.0;
        public double Tf { get; set; } = 12.0;

        public SectionProfile()
        {
        }

        public SectionProfile(string name, SectionShapeType shapeType, double h, double b, double tw, double tf)
        {
            Name = name;
            Shape = shapeType;
            H = h;
            B = b;
            Tw = tw;
            Tf = tf;
        }

        public string ShapeDisplayName
        {
            get
            {
                switch (Shape)
                {
                    case SectionShapeType.HBeam: return "H型钢";
                    case SectionShapeType.Channel: return "槽钢";
                    case SectionShapeType.Angle: return "等边角钢";
                    case SectionShapeType.RectTube: return "方矩管";
                    case SectionShapeType.PipeTube: return "圆钢管";
                    default: return "型钢";
                }
            }
        }

        public string ShapeTypeDisplayName => ShapeDisplayName;

        public double AreaCm2 => Math.Round(GetAreaMm2() * 0.01, 2);
        public double UnitWeightKgPerM => GetUnitWeightKgPerM();
        public double UnitSurfaceM2PerM => GetUnitSurfaceAreaM2PerM();
        public double PerimeterMPerM => GetUnitSurfaceAreaM2PerM();

        public double GetAreaMm2()
        {
            switch (Shape)
            {
                case SectionShapeType.HBeam:
                case SectionShapeType.Channel:
                    return Math.Max(10.0, H - 2.0 * Tf) * Tw + 2.0 * B * Tf;
                case SectionShapeType.Angle:
                    return (H * Tw) + Math.Max(10.0, B - Tw) * Tw;
                case SectionShapeType.RectTube:
                {
                    double innerH = Math.Max(0.0, H - 2.0 * Tw);
                    double innerB = Math.Max(0.0, B - 2.0 * Tw);
                    return (H * B) - (innerH * innerB);
                }
                case SectionShapeType.PipeTube:
                {
                    double rOut = H * 0.5;
                    double rIn = Math.Max(0.0, rOut - Tw);
                    return Math.PI * (rOut * rOut - rIn * rIn);
                }
                default:
                    return H * B * 0.15;
            }
        }

        public double GetUnitWeightKgPerM()
        {
            return Math.Round(GetAreaMm2() * 0.00785, 2);
        }

        public double GetUnitSurfaceAreaM2PerM()
        {
            double perimeterMm;
            switch (Shape)
            {
                case SectionShapeType.HBeam:
                case SectionShapeType.Channel:
                    perimeterMm = 2.0 * H + 4.0 * B - 2.0 * Tw;
                    break;
                case SectionShapeType.Angle:
                case SectionShapeType.RectTube:
                    perimeterMm = 2.0 * (H + B);
                    break;
                case SectionShapeType.PipeTube:
                    perimeterMm = Math.PI * H;
                    break;
                default:
                    perimeterMm = 2.0 * (H + B);
                    break;
            }
            return Math.Round(perimeterMm * 0.001, 3);
        }

        public SectionProfile Clone()
        {
            return new SectionProfile(Name, Shape, H, B, Tw, Tf);
        }
    }
}
