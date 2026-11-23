using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Modeling;

namespace SteelStructureSuite.Services.Interaction
{
    /// <summary>
    /// 单点动态跟随放置交互器（实时渲染构件三维轮廓，点击即落位）
    /// </summary>
    public class SteelSinglePointPlacementJig : DrawJig
    {
        private readonly SteelStructureParams _params;
        private Point3d _currentPt;

        public Point3d PickedPoint => _currentPt;

        public SteelSinglePointPlacementJig(SteelStructureParams p)
        {
            _params = p.Clone();
            _currentPt = p.Origin;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions opts = new JigPromptPointOptions(
                $"\n请移动鼠标动态预览并拾取 [{_params.Tag}] {_params.StructureTypeDisplayName} 插入点: ")
            {
                UserInputControls = UserInputControls.Accept3dCoordinates
            };

            PromptPointResult res = prompts.AcquirePoint(opts);
            if (res.Status != PromptStatus.OK) return SamplerStatus.Cancel;

            if (res.Value.DistanceTo(_currentPt) < 1e-2)
                return SamplerStatus.NoChange;

            _currentPt = res.Value;
            _params.Origin = _currentPt;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            var segs = SteelStructureBuilder.BuildFastPreviewWireframeLines(_params);
            short oldColor = draw.SubEntityTraits.Color;
            draw.SubEntityTraits.Color = 3; // 亮绿色动态预览线，清晰且 0 卡顿

            foreach (var seg in segs)
            {
                draw.Geometry.WorldLine(seg.Item1, seg.Item2);
            }

            draw.SubEntityTraits.Color = oldColor;
            return true;
        }
    }

    /// <summary>
    /// 两点线性动态绘制交互器（拾取起点 -> 鼠标实时拉出栏杆、直跑斜梯、单根钢梁/斜撑、悬挑走道、管廊桁架、框架并自动旋转对齐）
    /// </summary>
    public class SteelTwoPointLinearJig : DrawJig
    {
        private readonly SteelStructureParams _params;
        private readonly Point3d _startPt;
        private Point3d _endPt;

        public SteelStructureParams UpdatedParams => _params;

        public SteelTwoPointLinearJig(SteelStructureParams baseParams, Point3d startPt)
        {
            _params = baseParams.Clone();
            _startPt = startPt;
            _endPt = startPt + new Vector3d(baseParams.Span, 0, 0);
            _params.Origin = startPt;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions opts = new JigPromptPointOptions(_params.TwoPointEndPrompt)
            {
                UseBasePoint = true,
                BasePoint = _startPt,
                Cursor = CursorType.RubberBand,
                UserInputControls = UserInputControls.Accept3dCoordinates
            };

            PromptPointResult res = prompts.AcquirePoint(opts);
            if (res.Status != PromptStatus.OK) return SamplerStatus.Cancel;

            if (res.Value.DistanceTo(_endPt) < 1e-2)
                return SamplerStatus.NoChange;

            _endPt = res.Value;
            double dx = _endPt.X - _startPt.X;
            double dy = _endPt.Y - _startPt.Y;
            double dz = _endPt.Z - _startPt.Z;
            double horizDist = Math.Sqrt(dx * dx + dy * dy);

            if (horizDist > 100.0)
            {
                _params.Span = Math.Round(horizDist, 0);
                _params.RotationRad = Math.Atan2(dy, dx);
            }

            if (_params.StructureType == ParametricStructureType.ArcSteelMember)
            {
                if (horizDist > 80.0)
                {
                    _params.Span = Math.Round(horizDist, 0);
                    _params.RotationRad = Math.Atan2(dy, dx);
                }
            }
            else if (_params.StructureType == ParametricStructureType.LinearSteelMember ||
                     _params.StructureType == ParametricStructureType.SpiralSteelMember)
            {
                _params.EndPointOffsetZ = Math.Round(dz, 0);
                if (horizDist <= 100.0 && Math.Abs(dz) > 100.0)
                {
                    _params.Span = 50.0;
                    _params.EndPointOffsetZ = Math.Round(dz, 0);
                }
            }
            else if (_params.StructureType == ParametricStructureType.StraightStair && Math.Abs(dz) > 300.0)
            {
                _params.Height = Math.Round(Math.Abs(dz), 0);
            }

            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            short oldColor = draw.SubEntityTraits.Color;
            draw.SubEntityTraits.Color = 2; // 黄色基线
            draw.Geometry.WorldLine(_startPt, _endPt);

            draw.SubEntityTraits.Color = 3; // 绿色实时三维结构分格预览
            var segs = SteelStructureBuilder.BuildFastPreviewWireframeLines(_params);
            foreach (var seg in segs)
            {
                draw.Geometry.WorldLine(seg.Item1, seg.Item2);
            }

            draw.SubEntityTraits.Color = oldColor;
            return true;
        }
    }
}
