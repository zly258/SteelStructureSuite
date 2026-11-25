using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Views.Controls
{
    /// <summary>
    /// 截面类型枚举的显示名转换器：PropertyGrid 下拉里显示中文名称。
    /// </summary>
    public sealed class SectionShapeTypeConverter : EnumConverter
    {
        public SectionShapeTypeConverter(Type type) : base(type)
        {
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is SectionShapeType shape)
                return SectionProfile.GetShapeDisplayName(shape);
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string text)
            {
                foreach (SectionShapeType shape in Enum.GetValues(typeof(SectionShapeType)))
                {
                    if (string.Equals(SectionProfile.GetShapeDisplayName(shape), text, StringComparison.Ordinal))
                        return shape;
                }
            }
            return base.ConvertFrom(context, culture, value);
        }
    }

    /// <summary>
    /// 供 WinForms PropertyGrid 编辑的截面参数模型。
    /// 通过 <see cref="ICustomTypeDescriptor"/> 按当前截面类型动态决定
    /// 「显示哪些字段 + 字段中文名 + 所属分组」，从而无需在界面里手写显隐逻辑。
    /// </summary>
    public sealed class SectionParameterModel : ICustomTypeDescriptor
    {
        private static readonly System.ComponentModel.PropertyDescriptorCollection Empty =
            new System.ComponentModel.PropertyDescriptorCollection(null);

        private bool _suppressAutoMetrics;

        public SectionParameterModel()
        {
            Shape = SectionShapeType.HBeam;
            Name = "HW200x200x8x12";
            H = 200;
            B = 200;
            Tw = 8;
            Tf = 12;
            B2 = 240;
            Tf2 = 16;
            Lip = 20;
            Gap = 10;
        }

        /// <summary>参数被改动后触发，供窗口刷新 2D 预览与自动物理特性。</summary>
        public event EventHandler Changed;

        // -----------------------------------------------------------------
        // 数据字段
        // -----------------------------------------------------------------

        public SectionShapeType Shape { get; set; }
        public string Name { get; set; }
        public double H { get; set; }
        public double B { get; set; }
        public double Tw { get; set; }
        public double Tf { get; set; }
        public double B2 { get; set; }
        public double Tf2 { get; set; }
        public double Lip { get; set; }
        public double Gap { get; set; }

        /// <summary>物理特性（可手工覆盖；留空/清零表示按公式计算）。</summary>
        public double AreaCm2 { get; set; }
        public double UnitWeightKgPerM { get; set; }
        public double UnitSurfaceM2PerM { get; set; }

        /// <summary>物理特性是否来自手册/自定义覆盖值。</summary>
        public bool HasManualMetrics { get; private set; }

        // -----------------------------------------------------------------
        // 字段显隐规则
        // -----------------------------------------------------------------

        public bool ShowB => Shape != SectionShapeType.SquareTube
                          && Shape != SectionShapeType.PipeTube
                          && Shape != SectionShapeType.RoundBar
                          && Shape != SectionShapeType.SquareBar;

        public bool ShowTw => Shape != SectionShapeType.FlatBar
                           && Shape != SectionShapeType.RoundBar
                           && Shape != SectionShapeType.SquareBar;

        public bool ShowTf => Shape != SectionShapeType.Angle
                           && Shape != SectionShapeType.UnequalAngle
                           && Shape != SectionShapeType.DoubleAngle
                           && Shape != SectionShapeType.ColdFormedC
                           && Shape != SectionShapeType.ColdFormedZ
                           && Shape != SectionShapeType.HatSection
                           && Shape != SectionShapeType.SquareTube
                           && Shape != SectionShapeType.RectTube
                           && Shape != SectionShapeType.PipeTube
                           && Shape != SectionShapeType.FlatBar
                           && Shape != SectionShapeType.RoundBar
                           && Shape != SectionShapeType.SquareBar;

        public bool ShowB2 => Shape == SectionShapeType.WeldedHBeamUnequal;
        public bool ShowTf2 => Shape == SectionShapeType.WeldedHBeamUnequal;

        public bool ShowLip => Shape == SectionShapeType.ColdFormedC
                            || Shape == SectionShapeType.ColdFormedZ
                            || Shape == SectionShapeType.HatSection;

        public bool ShowGap => Shape == SectionShapeType.DoubleAngle
                            || Shape == SectionShapeType.DoubleChannel;

        // -----------------------------------------------------------------
        // 字段中文名（随截面类型变化）
        // -----------------------------------------------------------------

        public string LabelH
        {
            get
            {
                switch (Shape)
                {
                    case SectionShapeType.Angle:
                    case SectionShapeType.UnequalAngle: return "长肢 H (mm)";
                    case SectionShapeType.DoubleAngle: return "肢高 H (mm)";
                    case SectionShapeType.SquareTube: return "边长 H (mm)";
                    case SectionShapeType.PipeTube: return "外径 D (mm)";
                    case SectionShapeType.FlatBar: return "板宽 H (mm)";
                    case SectionShapeType.RoundBar: return "直径 D (mm)";
                    case SectionShapeType.SquareBar: return "边长 a (mm)";
                    default: return "高度 H (mm)";
                }
            }
        }

        public string LabelB
        {
            get
            {
                switch (Shape)
                {
                    case SectionShapeType.Angle: return "肢宽 B (mm)";
                    case SectionShapeType.UnequalAngle: return "短肢 B (mm)";
                    case SectionShapeType.DoubleAngle: return "肢宽 B (mm)";
                    case SectionShapeType.WeldedHBeamUnequal: return "上翼缘宽 B (mm)";
                    case SectionShapeType.HatSection: return "顶宽 B (mm)";
                    case SectionShapeType.FlatBar: return "板厚 t (mm)";
                    default: return "宽度 B (mm)";
                }
            }
        }

        public string LabelTw
        {
            get
            {
                switch (Shape)
                {
                    case SectionShapeType.Angle:
                    case SectionShapeType.UnequalAngle:
                    case SectionShapeType.DoubleAngle: return "肢厚 t (mm)";
                    case SectionShapeType.ColdFormedC:
                    case SectionShapeType.ColdFormedZ:
                    case SectionShapeType.ZBeam:
                    case SectionShapeType.ColdFormedU:
                    case SectionShapeType.HatSection:
                    case SectionShapeType.SquareTube:
                    case SectionShapeType.RectTube:
                    case SectionShapeType.PipeTube: return "壁厚 t (mm)";
                    default: return "腹板 tw (mm)";
                }
            }
        }

        public string LabelTf => Shape == SectionShapeType.WeldedHBeamUnequal
            ? "上翼缘厚 tf (mm)"
            : "翼缘 tf (mm)";

        public string LabelLip => Shape == SectionShapeType.HatSection
            ? "外翻边 C (mm)"
            : "卷边高度 C (mm)";

        // -----------------------------------------------------------------
        // 与 SectionProfile 互转
        // -----------------------------------------------------------------

        public void LoadFrom(SectionProfile sec)
        {
            if (sec == null) return;
            _suppressAutoMetrics = true;

            Shape = sec.Shape;
            Name = sec.Name;
            H = sec.H;
            B = sec.B;
            Tw = sec.Tw;
            Tf = sec.Tf;
            B2 = sec.B2 > 0 ? sec.B2 : sec.B;
            Tf2 = sec.Tf2 > 0 ? sec.Tf2 : sec.Tf;
            Lip = sec.Lip > 0 ? sec.Lip : 20.0;
            Gap = sec.Gap > 0 ? sec.Gap : 10.0;

            AreaCm2 = sec.AreaCm2;
            UnitWeightKgPerM = sec.UnitWeightKgPerM;
            UnitSurfaceM2PerM = sec.PerimeterMPerM;
            HasManualMetrics = sec.HasManualProperties;

            _suppressAutoMetrics = false;
        }

        public SectionProfile ToProfile(string fallbackName = "PREVIEW")
        {
            var profile = new SectionProfile(
                string.IsNullOrWhiteSpace(Name) ? fallbackName : Name.Trim(),
                Shape,
                Math.Max(1.0, H),
                ShowB ? Math.Max(1.0, B) : Math.Max(1.0, H),
                ShowTw ? Math.Max(0.5, Tw) : Math.Max(0.5, Tf),
                ShowTf ? Math.Max(0.5, Tf) : Math.Max(0.5, Tw),
                ShowB2 ? B2 : 0.0,
                ShowTf2 ? Tf2 : 0.0,
                ShowLip ? Lip : 0.0,
                ShowGap ? Gap : 0.0);

            if (HasManualMetrics)
            {
                if (AreaCm2 > 0) profile.ManualAreaCm2 = AreaCm2;
                if (UnitWeightKgPerM > 0) profile.ManualUnitWeightKgPerM = UnitWeightKgPerM;
                if (UnitSurfaceM2PerM > 0) profile.ManualUnitSurfaceM2PerM = UnitSurfaceM2PerM;
            }
            return profile;
        }

        /// <summary>按几何公式重算物理特性（清除手工覆盖）。</summary>
        public void RecalculateMetrics()
        {
            SectionProfile geometry = ToProfile();
            HasManualMetrics = false;
            AreaCm2 = geometry.GetCalculatedAreaCm2();
            UnitWeightKgPerM = geometry.GetCalculatedUnitWeightKgPerM();
            UnitSurfaceM2PerM = geometry.GetCalculatedUnitSurfaceM2PerM();
        }

        /// <summary>几何尺寸变更后，若未手工覆盖则同步刷新物理特性。</summary>
        public void RefreshAutoMetricsIfNeeded()
        {
            if (_suppressAutoMetrics || HasManualMetrics) return;
            SectionProfile geometry = ToProfile();
            AreaCm2 = geometry.GetCalculatedAreaCm2();
            UnitWeightKgPerM = geometry.GetCalculatedUnitWeightKgPerM();
            UnitSurfaceM2PerM = geometry.GetCalculatedUnitSurfaceM2PerM();
        }

        /// <summary>标记物理特性为手工值（PropertyGrid 直接编辑这三项时调用）。</summary>
        public void MarkMetricsManual()
        {
            HasManualMetrics = true;
        }

        /// <summary>切换截面类型时清除手工覆盖，回到公式计算。</summary>
        public void HasManualMetricsReset()
        {
            HasManualMetrics = false;
        }

        /// <summary>由外部（如“按公式重算”按钮）触发变更通知。</summary>
        public void NotifyChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        // -----------------------------------------------------------------
        // ICustomTypeDescriptor：按截面类型动态生成属性列表
        // -----------------------------------------------------------------

        public System.ComponentModel.PropertyDescriptorCollection GetProperties()
        {
            var list = new List<System.ComponentModel.PropertyDescriptor>();

            list.Add(Prop("Shape", "截面类型", "基本标识", "截面所属的型钢类型",
                typeof(SectionShapeType), () => Shape, v => Shape = (SectionShapeType)v,
                converter: new SectionShapeTypeConverter(typeof(SectionShapeType))));

            list.Add(Prop("Name", "截面代号", "基本标识", "自定义或国标截面代号，如 HW200x200x8x12、C160x60x20x2.5",
                typeof(string), () => Name, v => Name = v as string));

            list.Add(Prop("H", LabelH, "几何尺寸", "截面高度 / 外径 / 边长，单位 mm",
                typeof(double), () => H, v => { H = ToDouble(v); }));
            if (ShowB)
                list.Add(Prop("B", LabelB, "几何尺寸", "截面宽度 / 翼缘宽 / 板厚，单位 mm",
                    typeof(double), () => B, v => { B = ToDouble(v); }));
            if (ShowTw)
                list.Add(Prop("Tw", LabelTw, "几何尺寸", "腹板厚 / 管壁厚 / 肢厚，单位 mm",
                    typeof(double), () => Tw, v => { Tw = ToDouble(v); }));
            if (ShowTf)
                list.Add(Prop("Tf", LabelTf, "几何尺寸", "翼缘厚度，单位 mm",
                    typeof(double), () => Tf, v => { Tf = ToDouble(v); }));
            if (ShowB2)
                list.Add(Prop("B2", "下翼缘宽 B2 (mm)", "几何尺寸", "不等翼缘焊接 H 型钢下翼缘宽度，单位 mm",
                    typeof(double), () => B2, v => { B2 = ToDouble(v); }));
            if (ShowTf2)
                list.Add(Prop("Tf2", "下翼缘厚 tf2 (mm)", "几何尺寸", "不等翼缘焊接 H 型钢下翼缘厚度，单位 mm",
                    typeof(double), () => Tf2, v => { Tf2 = ToDouble(v); }));
            if (ShowLip)
                list.Add(Prop("Lip", LabelLip, "几何尺寸", "冷弯卷边高度或帽型钢外翻边宽，单位 mm",
                    typeof(double), () => Lip, v => { Lip = ToDouble(v); }));
            if (ShowGap)
                list.Add(Prop("Gap", "双拼间隙 Gap (mm)", "几何尺寸", "双拼截面中间节点板间隙，单位 mm",
                    typeof(double), () => Gap, v => { Gap = ToDouble(v); }));

            list.Add(Prop("AreaCm2", "截面积 (cm²)", "物理特性", "可直接修改为国标手册值；点「按公式重算」恢复公式计算",
                typeof(double), () => AreaCm2, v => { AreaCm2 = ToDouble(v); MarkMetricsManual(); }));
            list.Add(Prop("UnitWeightKgPerM", "理论单重 (kg/m)", "物理特性", "可直接修改为国标手册米重；点「按公式重算」恢复公式计算",
                typeof(double), () => UnitWeightKgPerM, v => { UnitWeightKgPerM = ToDouble(v); MarkMetricsManual(); }));
            list.Add(Prop("UnitSurfaceM2PerM", "表面积 (m²/m)", "物理特性", "可直接修改为国标手册表面积；点「按公式重算」恢复公式计算",
                typeof(double), () => UnitSurfaceM2PerM, v => { UnitSurfaceM2PerM = ToDouble(v); MarkMetricsManual(); }));

            return new PropertyDescriptorCollection(list.ToArray());
        }

        public System.ComponentModel.PropertyDescriptorCollection GetProperties(Attribute[] attributes) => GetProperties();

        public System.ComponentModel.AttributeCollection GetAttributes()
            => System.ComponentModel.AttributeCollection.Empty;

        public string GetClassName()
            => System.ComponentModel.TypeDescriptor.GetClassName(typeof(SectionParameterModel));

        public string GetComponentName()
            => System.ComponentModel.TypeDescriptor.GetComponentName(typeof(SectionParameterModel));

        public System.ComponentModel.TypeConverter GetConverter()
            => System.ComponentModel.TypeDescriptor.GetConverter(typeof(SectionParameterModel));

        public System.ComponentModel.EventDescriptor GetDefaultEvent() => null;

        public System.ComponentModel.PropertyDescriptor GetDefaultProperty() => null;

        public object GetEditor(Type editorBaseType) => null;

        public System.ComponentModel.EventDescriptorCollection GetEvents()
            => System.ComponentModel.EventDescriptorCollection.Empty;

        public System.ComponentModel.EventDescriptorCollection GetEvents(Attribute[] attributes)
            => System.ComponentModel.EventDescriptorCollection.Empty;

        public object GetPropertyOwner(System.ComponentModel.PropertyDescriptor pd) => this;

        private static double ToDouble(object value)
        {
            if (value == null) return 0.0;
            if (value is double d) return d;
            double.TryParse(Convert.ToString(value, CultureInfo.CurrentCulture), NumberStyles.Float, CultureInfo.CurrentCulture, out double parsed);
            return parsed;
        }

        private static System.ComponentModel.PropertyDescriptor Prop(
            string name,
            string displayName,
            string category,
            string description,
            Type propertyType,
            Func<object> getter,
            Action<object> setter,
            bool readOnly = false,
            System.ComponentModel.TypeConverter converter = null)
        {
            var attrs = new List<System.Attribute>
            {
                new DisplayNameAttribute(displayName),
                new CategoryAttribute(category),
                new DescriptionAttribute(description)
            };
            if (readOnly) attrs.Add(new ReadOnlyAttribute(true));

            return new LambdaPropertyDescriptor(
                name, attrs.ToArray(), propertyType, getter, setter, converter);
        }

        /// <summary>基于委托的只读/可写属性描述符。</summary>
        private sealed class LambdaPropertyDescriptor : System.ComponentModel.PropertyDescriptor
        {
            private readonly Type _propertyType;
            private readonly Func<object> _getter;
            private readonly Action<object> _setter;
            private readonly System.ComponentModel.TypeConverter _converter;

            public LambdaPropertyDescriptor(
                string name,
                System.Attribute[] attributes,
                Type propertyType,
                Func<object> getter,
                Action<object> setter,
                System.ComponentModel.TypeConverter converter)
                : base(name, attributes)
            {
                _propertyType = propertyType;
                _getter = getter;
                _setter = setter;
                _converter = converter;
            }

            public override Type ComponentType => typeof(SectionParameterModel);
            public override bool IsReadOnly => _setter == null;
            public override Type PropertyType => _propertyType;
            public override System.ComponentModel.TypeConverter Converter => _converter ?? base.Converter;

            public override bool CanResetValue(object component) => false;
            public override object GetValue(object component) => _getter();
            public override void SetValue(object component, object value) => _setter?.Invoke(value);
            public override void ResetValue(object component) { }
            public override bool ShouldSerializeValue(object component) => false;
        }
    }
}
