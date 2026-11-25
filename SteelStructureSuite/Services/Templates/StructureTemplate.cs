using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Services.Templates
{
    /// <summary>
    /// 一条参数模板：完整保存一个参数化构件的全部可编辑参数，
    /// 可在建模窗口里一键套用，也可跨构件类型作为「通用模板」使用。
    /// </summary>
    public sealed class StructureTemplate
    {
        /// <summary>稳定唯一标识，用于重命名/覆盖时定位。</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>模板名称（用户可见，同一构件类型内唯一）。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>备注说明。</summary>
        public string Note { get; set; } = string.Empty;

        /// <summary>所属构件类型。为 null 表示「通用模板」，可在任意类型下套用。</summary>
        public ParametricStructureType? StructureType { get; set; }

        /// <summary>创建时间。</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>最后修改时间。</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>参数快照（只保存与几何/材料相关的字段）。</summary>
        public SteelStructureParams Parameters { get; set; } = new SteelStructureParams();

        /// <summary>是否通用模板（不限构件类型）。</summary>
        public bool IsGeneric => !StructureType.HasValue;

        public StructureTemplate Clone()
        {
            return new StructureTemplate
            {
                Id = Id,
                Name = Name,
                Note = Note,
                StructureType = StructureType,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                Parameters = Parameters?.Clone() ?? new SteelStructureParams()
            };
        }

        /// <summary>从当前构件参数生成一条模板。</summary>
        public static StructureTemplate FromParams(SteelStructureParams source, string name, string note, bool generic = false)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            SteelStructureParams snapshot = source.Clone();
            // 模板不应携带位号与插入点，套用后由使用者重新决定
            snapshot.Tag = string.Empty;
            snapshot.Origin = Autodesk.AutoCAD.Geometry.Point3d.Origin;
            snapshot.RotationRad = 0.0;
            snapshot.UseCurveFrame = false;

            return new StructureTemplate
            {
                Name = name?.Trim() ?? string.Empty,
                Note = note?.Trim() ?? string.Empty,
                StructureType = generic ? (ParametricStructureType?)null : source.StructureType,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Parameters = snapshot
            };
        }

        /// <summary>把模板套用到目标参数对象（保留目标原有位号与插入点）。</summary>
        public void ApplyTo(SteelStructureParams target)
        {
            if (target == null || Parameters == null) return;

            string keepTag = target.Tag;
            var keepOrigin = target.Origin;
            double keepRotation = target.RotationRad;
            bool keepCurveFrame = target.UseCurveFrame;
            var keepFrameX = target.FrameX;
            var keepFrameZ = target.FrameZ;

            target.CopyFrom(Parameters);

            target.Tag = keepTag;
            target.Origin = keepOrigin;
            target.RotationRad = keepRotation;
            target.UseCurveFrame = keepCurveFrame;
            target.FrameX = keepFrameX;
            target.FrameZ = keepFrameZ;
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Name) ? "(未命名模板)" : Name;
        }
    }
}
