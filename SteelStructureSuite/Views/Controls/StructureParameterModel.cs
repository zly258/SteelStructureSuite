using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Design;
using System.Linq;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Views.Controls
{
    /// <summary>
    /// 构件参数 PropertyGrid 模型。
    ///
    /// 它本身不决定哪些字段可见、中文标签叫什么——那些由建模窗口里既有的
    /// 按构件类型的显隐/标签逻辑计算后，通过 <see cref="FieldSpec"/> 传进来。
    /// 这样避免把那 30 种构件类型的映射规则在新旧两处各维护一份。
    ///
    /// 每个字段的读写都直接作用在窗口的 <see cref="SteelStructureParams"/> 上，
    /// PropertyGrid 改完即写入真值对象。
    /// </summary>
    public sealed class StructureParameterModel : ICustomTypeDescriptor
    {
        /// <summary>一个字段的说明：标签、分组、类型、读写委托、是否可见、编辑器类型。</summary>
        public sealed class FieldSpec
        {
            public string Label;
            public string Category;
            public Type ValueType;
            public Func<object> Get;
            public Action<object> Set;
            public bool Visible = true;
            /// <summary>截面字段：使用自定义两级截面选择编辑器。</summary>
            public bool IsSection;
            /// <summary>材质字段：使用标准值下拉。</summary>
            public bool IsMaterial;
            /// <summary>只读字段。</summary>
            public bool ReadOnly;
            /// <summary>说明文字。</summary>
            public string Description;
        }

        private readonly List<FieldSpec> _fields;

        public StructureParameterModel(List<FieldSpec> fields)
        {
            _fields = fields ?? new List<FieldSpec>();
        }

        public void ReplaceFields(List<FieldSpec> fields)
        {
            _fields.Clear();
            if (fields != null) _fields.AddRange(fields);
        }

        // ---------------- ICustomTypeDescriptor ----------------

        public PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            var props = _fields
                .Where(f => f.Visible && f.Get != null && f.Set != null)
                .Select(f => (PropertyDescriptor)new FieldDescriptor(f))
                .ToArray();
            return new PropertyDescriptorCollection(props);
        }

        public PropertyDescriptorCollection GetProperties() => GetProperties(null);

        public object GetPropertyOwner(PropertyDescriptor pd) => this;
        public string GetClassName() => "构件参数";
        public string GetComponentName() => "构件参数";
        public AttributeCollection GetAttributes() => AttributeCollection.Empty;
        public EventDescriptorCollection GetEvents(Attribute[] attributes) => EventDescriptorCollection.Empty;
        public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;
        public TypeConverter GetConverter() => null;
        public object GetEditor(Type editorBaseType) => null;
        public PropertyDescriptor GetDefaultProperty() => null;
        public EventDescriptor GetDefaultEvent() => null;

        // ---------------- 字段描述符 ----------------

        private sealed class FieldDescriptor : PropertyDescriptor
        {
            private readonly FieldSpec _spec;

            public FieldDescriptor(FieldSpec spec)
                : base(spec.Label, BuildAttributes(spec))
            {
                _spec = spec;
            }

            private static Attribute[] BuildAttributes(FieldSpec spec)
            {
                var attrs = new List<Attribute>
                {
                    new DisplayNameAttribute(spec.Label),
                    new CategoryAttribute(spec.Category ?? "参数"),
                    new ReadOnlyAttribute(spec.ReadOnly)
                };

                if (!string.IsNullOrEmpty(spec.Description))
                    attrs.Add(new DescriptionAttribute(spec.Description));

                if (spec.IsSection)
                    attrs.Add(new EditorAttribute(typeof(SectionSelectEditor), typeof(UITypeEditor)));
                else if (spec.IsMaterial)
                    attrs.Add(new TypeConverterAttribute(typeof(MaterialConverter)));

                return attrs.ToArray();
            }

            public override string DisplayName => _spec.Label;
            public override string Category => _spec.Category ?? "参数";
            public override Type ComponentType => typeof(StructureParameterModel);
            public override Type PropertyType => _spec.ValueType;
            public override bool IsReadOnly => _spec.ReadOnly;
            public override bool CanResetValue(object component) => false;
            public override object GetValue(object component) => _spec.Get();
            public override void SetValue(object component, object value)
            {
                if (_spec.ReadOnly) return;
                _spec.Set(value);
            }
            public override void ResetValue(object component) { }
            public override bool ShouldSerializeValue(object component) => true;
        }
    }

    /// <summary>材质标准值下拉（PropertyGrid 自动渲染为下拉框）。</summary>
    public sealed class MaterialConverter : StringConverter
    {
        private static readonly string[] Materials =
            { "Q235B", "Q355B", "Q355D", "Q420B", "S30408", "15CrMoR" };

        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            => new StandardValuesCollection(Materials);
    }
}
