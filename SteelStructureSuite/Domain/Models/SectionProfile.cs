using System;
using System.Globalization;
using SteelStructureSuite.Domain.Enums;

namespace SteelStructureSuite.Domain.Models
{
    /// <summary>
    /// 钢结构截面规格定义模型
    /// 支持热轧型钢、焊接组合截面、冷弯薄壁型钢、空心钢管与实心型材
    /// </summary>
    public class SectionProfile
    {
        public string Name { get; set; } = "HW200x200x8x12";
        public SectionShapeType Shape { get; set; } = SectionShapeType.HBeam;

        public SectionShapeType ShapeType
        {
            get => Shape;
            set => Shape = value;
        }

        /// <summary>截面高度 H / 圆管外径 D / 圆钢直径 D / 方钢边长 a (mm)</summary>
        public double H { get; set; } = 200.0;

        /// <summary>截面宽度 B / 上翼缘宽 / 扁钢厚度 t (mm)</summary>
        public double B { get; set; } = 200.0;

        /// <summary>腹板厚 Tw / 管壁厚 t / 角钢肢厚 t (mm)</summary>
        public double Tw { get; set; } = 8.0;

        /// <summary>翼缘厚 Tf / 上翼缘厚 (mm)</summary>
        public double Tf { get; set; } = 12.0;

        /// <summary>下翼缘宽 B2（用于不等翼缘焊接 H 型钢，mm）</summary>
        public double B2 { get; set; } = 0.0;

        /// <summary>下翼缘厚 Tf2（用于不等翼缘焊接 H 型钢，mm）</summary>
        public double Tf2 { get; set; } = 0.0;

        /// <summary>卷边高度/唇边宽 C（用于冷弯卷边 C/Z 型钢、帽型钢，mm）</summary>
        public double Lip { get; set; } = 0.0;

        /// <summary>双拼间隙 Gap（用于双角钢 2L、双槽钢 2C，mm）</summary>
        public double Gap { get; set; } = 0.0;

        /// <summary>手册/自定义截面积覆盖值 (cm²)，为空或 ≤0 时按几何公式自动计算</summary>
        public double? ManualAreaCm2 { get; set; }

        /// <summary>手册/自定义理论米重覆盖值 (kg/m)，为空或 ≤0 时自动计算</summary>
        public double? ManualUnitWeightKgPerM { get; set; }

        /// <summary>手册/自定义外表面积覆盖值 (m²/m)，为空或 ≤0 时按几何公式自动计算</summary>
        public double? ManualUnitSurfaceM2PerM { get; set; }

        public bool HasManualProperties =>
            (ManualAreaCm2.HasValue && ManualAreaCm2.Value > 0) ||
            (ManualUnitWeightKgPerM.HasValue && ManualUnitWeightKgPerM.Value > 0) ||
            (ManualUnitSurfaceM2PerM.HasValue && ManualUnitSurfaceM2PerM.Value > 0);

        public string PropertySourceDisplayName => HasManualProperties ? "手册/自定义" : "公式计算";

        public SectionProfile WithHandbookProps(double areaCm2, double weightKgPerM, double? surfaceM2PerM = null)
        {
            if (areaCm2 > 0) ManualAreaCm2 = areaCm2;
            if (weightKgPerM > 0) ManualUnitWeightKgPerM = weightKgPerM;
            if (surfaceM2PerM.HasValue && surfaceM2PerM.Value > 0) ManualUnitSurfaceM2PerM = surfaceM2PerM.Value;
            return this;
        }

        public SectionProfile()
        {
        }

        public SectionProfile(
            string name,
            SectionShapeType shapeType,
            double h,
            double b,
            double tw,
            double tf,
            double b2 = 0.0,
            double tf2 = 0.0,
            double lip = 0.0,
            double gap = 0.0)
        {
            Name = name;
            Shape = shapeType;
            H = h;
            B = b;
            Tw = tw;
            Tf = tf;
            B2 = b2;
            Tf2 = tf2;
            Lip = lip;
            Gap = gap;
        }

        public SectionCategory Category => GetCategory(Shape);

        public string CategoryDisplayName => GetCategoryDisplayName(Category);

        public string ShapeDisplayName => GetShapeDisplayName(Shape);

        public string ShapeTypeDisplayName => ShapeDisplayName;

        /// <summary>
        /// 扩展参数摘要（用于表格紧凑列展示）
        /// </summary>
        public string ExtraParamsSummary
        {
            get
            {
                switch (Shape)
                {
                    case SectionShapeType.WeldedHBeamUnequal:
                        double effB2 = B2 > 0 ? B2 : B;
                        double effTf2 = Tf2 > 0 ? Tf2 : Tf;
                        return string.Format(CultureInfo.InvariantCulture, "B2={0:0.#}, tf2={1:0.#}", effB2, effTf2);
                    case SectionShapeType.ColdFormedC:
                    case SectionShapeType.ColdFormedZ:
                    case SectionShapeType.HatSection:
                        return string.Format(CultureInfo.InvariantCulture, "C={0:0.#}", Lip);
                    case SectionShapeType.DoubleAngle:
                    case SectionShapeType.DoubleChannel:
                        return string.Format(CultureInfo.InvariantCulture, "间隙={0:0.#}", Gap);
                    default:
                        return "-";
                }
            }
        }

        public double AreaCm2 => (ManualAreaCm2.HasValue && ManualAreaCm2.Value > 0)
            ? Math.Round(ManualAreaCm2.Value, 2)
            : GetCalculatedAreaCm2();

        public double UnitWeightKgPerM => GetUnitWeightKgPerM();
        public double UnitSurfaceM2PerM => GetUnitSurfaceAreaM2PerM();
        public double PerimeterMPerM => GetUnitSurfaceAreaM2PerM();

        public double GetCalculatedAreaCm2() => Math.Round(GetAreaMm2() * 0.01, 2);

        public double GetCalculatedUnitWeightKgPerM() => Math.Round(GetAreaMm2() * 0.00785, 2);

        /// <summary>
        /// 计算理论截面积 (mm²)
        /// </summary>
        public double GetAreaMm2()
        {
            double h = Math.Max(1.0, H);
            double b = Math.Max(1.0, B);
            double tw = Math.Max(0.5, Tw);
            double tf = Math.Max(0.5, Tf);

            switch (Shape)
            {
                case SectionShapeType.HBeam:
                case SectionShapeType.IBeam:
                case SectionShapeType.WeldedHBeam:
                case SectionShapeType.Channel:
                case SectionShapeType.ZBeam:
                case SectionShapeType.ColdFormedU:
                    return Math.Max(1.0, h - 2.0 * tf) * tw + 2.0 * b * tf;

                case SectionShapeType.WeldedHBeamUnequal:
                {
                    double b2 = B2 > 0 ? B2 : b;
                    double tf2 = Tf2 > 0 ? Tf2 : tf;
                    double webH = Math.Max(1.0, h - tf - tf2);
                    return webH * tw + b * tf + b2 * tf2;
                }

                case SectionShapeType.TBeam:
                case SectionShapeType.WeldedTBeam:
                    return Math.Max(1.0, h - tf) * tw + b * tf;

                case SectionShapeType.WeldedBox:
                {
                    double webH = Math.Max(0.0, h - 2.0 * tf);
                    return 2.0 * b * tf + 2.0 * webH * tw;
                }

                case SectionShapeType.WeldedCross:
                {
                    // 十字截面：两根正交 H 型钢（高度 H、翼缘宽 B、腹板厚 Tw、翼缘厚 Tf）合并
                    double hArea = Math.Max(1.0, h - 2.0 * tf) * tw + 2.0 * b * tf;
                    return 2.0 * hArea - tw * tw;
                }

                case SectionShapeType.Angle:
                case SectionShapeType.UnequalAngle:
                    return (h * tw) + Math.Max(0.0, b - tw) * tw;

                case SectionShapeType.DoubleAngle:
                    return 2.0 * ((h * tw) + Math.Max(0.0, b - tw) * tw);

                case SectionShapeType.DoubleChannel:
                    return 2.0 * (Math.Max(1.0, h - 2.0 * tf) * tw + 2.0 * b * tf);

                case SectionShapeType.ColdFormedC:
                case SectionShapeType.ColdFormedZ:
                {
                    double lip = Math.Max(0.0, Lip);
                    // 薄壁展开长度近似：H + 2*(B - tw) + 2*max(0, Lip - tw)
                    double len = h + 2.0 * Math.Max(0.0, b - tw) + 2.0 * Math.Max(0.0, lip - tw);
                    return len * tw;
                }

                case SectionShapeType.HatSection:
                {
                    double lip = Math.Max(0.0, Lip);
                    // 帽型钢：顶宽 B + 两侧腹板 2*(H - tw) + 两侧外翻边 2*Lip
                    double len = b + 2.0 * Math.Max(0.0, h - tw) + 2.0 * lip;
                    return len * tw;
                }

                case SectionShapeType.RectTube:
                case SectionShapeType.SquareTube:
                {
                    double effB = Shape == SectionShapeType.SquareTube ? h : b;
                    double innerH = Math.Max(0.0, h - 2.0 * tw);
                    double innerB = Math.Max(0.0, effB - 2.0 * tw);
                    return (h * effB) - (innerH * innerB);
                }

                case SectionShapeType.PipeTube:
                {
                    double rOut = h * 0.5;
                    double rIn = Math.Max(0.0, rOut - tw);
                    return Math.PI * (rOut * rOut - rIn * rIn);
                }

                case SectionShapeType.FlatBar:
                    return h * b;

                case SectionShapeType.RoundBar:
                {
                    double r = h * 0.5;
                    return Math.PI * r * r;
                }

                case SectionShapeType.SquareBar:
                    return h * h;

                default:
                    return h * b * 0.15;
            }
        }

        public double GetUnitWeightKgPerM()
        {
            if (ManualUnitWeightKgPerM.HasValue && ManualUnitWeightKgPerM.Value > 0)
                return Math.Round(ManualUnitWeightKgPerM.Value, 2);
            if (ManualAreaCm2.HasValue && ManualAreaCm2.Value > 0)
                return Math.Round(ManualAreaCm2.Value * 0.785, 2);
            return Math.Round(GetAreaMm2() * 0.00785, 2);
        }

        /// <summary>
        /// 计算单位长度外表面积 (m²/m)，用于防腐涂装算量（优先采用手册/自定义值）
        /// </summary>
        public double GetUnitSurfaceAreaM2PerM()
        {
            if (ManualUnitSurfaceM2PerM.HasValue && ManualUnitSurfaceM2PerM.Value > 0)
                return Math.Round(ManualUnitSurfaceM2PerM.Value, 3);
            return GetCalculatedUnitSurfaceM2PerM();
        }

        /// <summary>
        /// 按几何尺寸公式估算单位长度外表面积 (m²/m)
        /// </summary>
        public double GetCalculatedUnitSurfaceM2PerM()
        {
            double h = Math.Max(1.0, H);
            double b = Math.Max(1.0, B);
            double tw = Math.Max(0.5, Tw);
            double tf = Math.Max(0.5, Tf);
            double perimeterMm;

            switch (Shape)
            {
                case SectionShapeType.HBeam:
                case SectionShapeType.IBeam:
                case SectionShapeType.WeldedHBeam:
                case SectionShapeType.Channel:
                case SectionShapeType.ZBeam:
                case SectionShapeType.ColdFormedU:
                    perimeterMm = 2.0 * h + 4.0 * b - 2.0 * tw;
                    break;

                case SectionShapeType.WeldedHBeamUnequal:
                {
                    double b2 = B2 > 0 ? B2 : b;
                    perimeterMm = 2.0 * h + 2.0 * (b + b2) - 2.0 * tw;
                    break;
                }

                case SectionShapeType.TBeam:
                case SectionShapeType.WeldedTBeam:
                    perimeterMm = 2.0 * (h + b);
                    break;

                case SectionShapeType.WeldedBox:
                case SectionShapeType.RectTube:
                case SectionShapeType.FlatBar:
                    perimeterMm = 2.0 * (h + b);
                    break;

                case SectionShapeType.SquareTube:
                case SectionShapeType.SquareBar:
                    perimeterMm = 4.0 * h;
                    break;

                case SectionShapeType.WeldedCross:
                    perimeterMm = 4.0 * h + 8.0 * b - 8.0 * tw;
                    break;

                case SectionShapeType.Angle:
                case SectionShapeType.UnequalAngle:
                    perimeterMm = 2.0 * (h + b);
                    break;

                case SectionShapeType.DoubleAngle:
                    perimeterMm = 4.0 * (h + b);
                    break;

                case SectionShapeType.DoubleChannel:
                    perimeterMm = 2.0 * (2.0 * h + 4.0 * b - 2.0 * tw);
                    break;

                case SectionShapeType.ColdFormedC:
                case SectionShapeType.ColdFormedZ:
                {
                    double lip = Math.Max(0.0, Lip);
                    perimeterMm = 2.0 * (h + 2.0 * b + 2.0 * lip);
                    break;
                }

                case SectionShapeType.HatSection:
                {
                    double lip = Math.Max(0.0, Lip);
                    perimeterMm = 2.0 * (b + 2.0 * h + 2.0 * lip);
                    break;
                }

                case SectionShapeType.PipeTube:
                case SectionShapeType.RoundBar:
                    perimeterMm = Math.PI * h;
                    break;

                default:
                    perimeterMm = 2.0 * (h + b);
                    break;
            }

            return Math.Round(Math.Max(0.0, perimeterMm) * 0.001, 3);
        }

        public SectionProfile Clone()
        {
            return new SectionProfile(Name, Shape, H, B, Tw, Tf, B2, Tf2, Lip, Gap)
            {
                ManualAreaCm2 = ManualAreaCm2,
                ManualUnitWeightKgPerM = ManualUnitWeightKgPerM,
                ManualUnitSurfaceM2PerM = ManualUnitSurfaceM2PerM
            };
        }

        public static SectionCategory GetCategory(SectionShapeType shape)
        {
            switch (shape)
            {
                case SectionShapeType.HBeam:
                case SectionShapeType.IBeam:
                case SectionShapeType.Channel:
                case SectionShapeType.Angle:
                case SectionShapeType.UnequalAngle:
                case SectionShapeType.TBeam:
                    return SectionCategory.HotRolled;

                case SectionShapeType.WeldedHBeam:
                case SectionShapeType.WeldedHBeamUnequal:
                case SectionShapeType.WeldedBox:
                case SectionShapeType.WeldedTBeam:
                case SectionShapeType.WeldedCross:
                case SectionShapeType.DoubleAngle:
                case SectionShapeType.DoubleChannel:
                    return SectionCategory.WeldedComposite;

                case SectionShapeType.ColdFormedC:
                case SectionShapeType.ColdFormedZ:
                case SectionShapeType.ZBeam:
                case SectionShapeType.ColdFormedU:
                case SectionShapeType.HatSection:
                    return SectionCategory.ColdFormed;

                case SectionShapeType.SquareTube:
                case SectionShapeType.RectTube:
                case SectionShapeType.PipeTube:
                    return SectionCategory.HollowTube;

                case SectionShapeType.FlatBar:
                case SectionShapeType.RoundBar:
                case SectionShapeType.SquareBar:
                    return SectionCategory.SolidBar;

                default:
                    return SectionCategory.HotRolled;
            }
        }

        public static string GetCategoryDisplayName(SectionCategory category)
        {
            switch (category)
            {
                case SectionCategory.HotRolled: return "热轧标准型钢";
                case SectionCategory.WeldedComposite: return "焊接与组合型钢";
                case SectionCategory.ColdFormed: return "冷弯薄壁型钢";
                case SectionCategory.HollowTube: return "空心钢管";
                case SectionCategory.SolidBar: return "实心型材与板材";
                default: return "其他型钢";
            }
        }

        public static string GetShapeDisplayName(SectionShapeType shape)
        {
            switch (shape)
            {
                case SectionShapeType.HBeam: return "热轧H型钢";
                case SectionShapeType.IBeam: return "热轧工字钢";
                case SectionShapeType.Channel: return "热轧槽钢";
                case SectionShapeType.Angle: return "等边角钢";
                case SectionShapeType.UnequalAngle: return "不等边角钢";
                case SectionShapeType.TBeam: return "热轧T型钢";

                case SectionShapeType.WeldedHBeam: return "焊接H型钢";
                case SectionShapeType.WeldedHBeamUnequal: return "不等翼缘焊接H";
                case SectionShapeType.WeldedBox: return "焊接箱型截面";
                case SectionShapeType.WeldedTBeam: return "焊接T型钢";
                case SectionShapeType.WeldedCross: return "焊接十字截面";
                case SectionShapeType.DoubleAngle: return "双角钢组合";
                case SectionShapeType.DoubleChannel: return "双槽钢组合";

                case SectionShapeType.ColdFormedC: return "冷弯卷边C型钢";
                case SectionShapeType.ColdFormedZ: return "冷弯卷边Z型钢";
                case SectionShapeType.ZBeam: return "无卷边Z型钢";
                case SectionShapeType.ColdFormedU: return "冷弯U型钢";
                case SectionShapeType.HatSection: return "帽型钢(Ω)";

                case SectionShapeType.SquareTube: return "方管(SHS)";
                case SectionShapeType.RectTube: return "矩形管(RHS)";
                case SectionShapeType.PipeTube: return "圆钢管(CHS)";

                case SectionShapeType.FlatBar: return "扁钢";
                case SectionShapeType.RoundBar: return "圆钢";
                case SectionShapeType.SquareBar: return "方钢";

                default: return "型钢";
            }
        }
    }
}
