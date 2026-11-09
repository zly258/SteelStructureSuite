using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Catalog;
using SteelStructureSuite.Services.Modeling;

namespace SteelStructureSuite.Views
{
    public partial class StructureEditWindow : Window
    {
        public SteelStructureParams Data { get; private set; }
        public bool IsEditMode { get; }
        public bool LockStructureType { get; }
        public bool UseTwoPointLinearDraw { get; private set; }
        public bool ChainPlacementMode { get; }

        private bool _isInitializing = true;
        private readonly List<ParametricStructureType> _availableTypes = new List<ParametricStructureType>();

        private static readonly string[] ShapeFilterNames =
        {
            "全部截面",
            "H型钢",
            "槽钢",
            "角钢",
            "方矩管",
            "圆钢管"
        };

        public StructureEditWindow(
            SteelStructureParams initialParams,
            bool isEditMode = false,
            bool lockStructureType = true,
            bool chainPlacementMode = false)
        {
            InitializeComponent();

            Data = initialParams?.Clone() ?? new SteelStructureParams();
            IsEditMode = isEditMode;
            LockStructureType = lockStructureType;
            ChainPlacementMode = chainPlacementMode;

            Title = isEditMode
                ? $"编辑 {Data.StructureTypeDisplayName} - [{Data.Tag}]"
                : (chainPlacementMode ? $"连续绘制 - {Data.StructureTypeDisplayName}" : Data.StructureTypeDisplayName);

            BuildAvailableTypeList();
            foreach (ParametricStructureType t in _availableTypes)
                CmbStructureType.Items.Add(DisplayNameOf(t));

            int selectedIdx = _availableTypes.IndexOf(Data.StructureType);
            CmbStructureType.SelectedIndex = selectedIdx >= 0 ? selectedIdx : 0;
            if (selectedIdx < 0 && _availableTypes.Count > 0)
            {
                Data.StructureType = _availableTypes[0];
            }

            if (lockStructureType)
            {
                LblType.Visibility = Visibility.Collapsed;
                CmbStructureType.Visibility = Visibility.Collapsed;
            }

            TxtTag.Text = Data.Tag;
            foreach (string m in new[] { "Q235B", "Q355B", "Q355D", "Q420B", "S30408" })
                CmbMaterial.Items.Add(m);
            CmbMaterial.SelectedItem = Data.Material ?? "Q355B";
            if (CmbMaterial.SelectedIndex < 0) CmbMaterial.SelectedIndex = 1;

            TxtSpan.Text = Data.Span.ToString("0.#", CultureInfo.InvariantCulture);
            TxtLength.Text = Data.Length.ToString("0.#", CultureInfo.InvariantCulture);
            TxtHeight.Text = Data.Height.ToString("0.#", CultureInfo.InvariantCulture);
            TxtSubHeight.Text = Data.SubHeight.ToString("0.#", CultureInfo.InvariantCulture);

            foreach (string f in ShapeFilterNames)
            {
                CmbColTypeFilter.Items.Add(f);
                CmbBeamTypeFilter.Items.Add(f);
                CmbBraceTypeFilter.Items.Add(f);
            }
            CmbColTypeFilter.SelectedIndex = 0;
            CmbBeamTypeFilter.SelectedIndex = 0;
            CmbBraceTypeFilter.SelectedIndex = 0;

            PopulateSectionCombo(CmbColSec, 0, Data.ColumnSection);
            PopulateSectionCombo(CmbBeamSec, 0, Data.BeamSection);
            PopulateSectionCombo(CmbBraceSec, 0, Data.BraceSection);

            if (isEditMode)
            {
                BtnUpdate.Visibility = Visibility.Visible;
                BtnUpdate.IsDefault = true;
            }
            else
            {
                BtnUpdate.Visibility = Visibility.Collapsed;
            }
            UpdatePlacementButtons();

            SchematicView.SizeChanged += (s, e) => SchematicView.RenderSchematic(Data);

            _isInitializing = false;
            UpdatePerComponentFieldVisibilityAndLabels();
            RefreshLivePreview();
        }

        private void UpdatePlacementButtons()
        {
            if (BtnSinglePt == null || BtnLinearTwoPt == null) return;

            if (IsEditMode)
            {
                BtnSinglePt.Visibility = Visibility.Collapsed;
                BtnLinearTwoPt.Visibility = Visibility.Collapsed;
                return;
            }

            BtnSinglePt.Visibility = Visibility.Visible;

            if (ChainPlacementMode)
            {
                BtnSinglePt.Content = "开始连续绘制";
                BtnSinglePt.IsDefault = true;
                BtnLinearTwoPt.Visibility = Visibility.Collapsed;
                return;
            }

            BtnSinglePt.Content = "单点放置";
            BtnSinglePt.ToolTip = "按对话框里的尺寸，只拾取一个插入点。";
            bool canLinear = Data.SupportsTwoPointDraw;
            BtnLinearTwoPt.Visibility = canLinear ? Visibility.Visible : Visibility.Collapsed;
            BtnLinearTwoPt.Content = Data.TwoPointButtonText;
            BtnLinearTwoPt.IsDefault = canLinear && Data.PrefersLinearTwoPointDraw;
            BtnSinglePt.IsDefault = !BtnLinearTwoPt.IsDefault;
            BtnLinearTwoPt.ToolTip = Data.PrefersLinearTwoPointDraw
                ? "拾取起点和终点，长度和方向跟着鼠标走。"
                : "拾取两点，只改跨度和方向，柱高、进深等仍用对话框中的数值。";
            BtnSinglePt.FontWeight = BtnSinglePt.IsDefault ? FontWeights.Bold : FontWeights.Normal;
            BtnLinearTwoPt.FontWeight = BtnLinearTwoPt.IsDefault ? FontWeights.Bold : FontWeights.Normal;
        }

        private void BuildAvailableTypeList()
        {
            _availableTypes.Clear();
            foreach (ParametricStructureType t in Enum.GetValues(typeof(ParametricStructureType)))
            {
                var probe = new SteelStructureParams { StructureType = t };
                if (ChainPlacementMode && !probe.SupportsChainDraw) continue;
                _availableTypes.Add(t);
            }
        }

        private static string DisplayNameOf(ParametricStructureType t)
        {
            return new SteelStructureParams { StructureType = t }.StructureTypeDisplayName;
        }

        private static void SetRowVisible(UIElement label, UIElement input, bool visible)
        {
            Visibility v = visible ? Visibility.Visible : Visibility.Collapsed;
            label.Visibility = v;
            input.Visibility = v;
        }

        private static void SetSectionRowVisible(TextBlock lbl, ComboBox filter, ComboBox sec, bool visible)
        {
            Visibility v = visible ? Visibility.Visible : Visibility.Collapsed;
            lbl.Visibility = v;
            filter.Visibility = v;
            sec.Visibility = v;
        }

        private static void PopulateSectionCombo(ComboBox cmb, int filterIdx, string preferredName)
        {
            if (cmb == null) return;
            List<string> names;
            switch (filterIdx)
            {
                case 1: names = SectionCatalogService.GetNamesByShapeType(SectionShapeType.HBeam); break;
                case 2: names = SectionCatalogService.GetNamesByShapeType(SectionShapeType.Channel); break;
                case 3: names = SectionCatalogService.GetNamesByShapeType(SectionShapeType.Angle); break;
                case 4: names = SectionCatalogService.GetNamesByShapeType(SectionShapeType.RectTube); break;
                case 5: names = SectionCatalogService.GetNamesByShapeType(SectionShapeType.PipeTube); break;
                default: names = SectionCatalogService.GetAllNames(); break;
            }

            cmb.Items.Clear();
            foreach (string n in names) cmb.Items.Add(n);

            if (!string.IsNullOrEmpty(preferredName) && names.Contains(preferredName))
                cmb.SelectedItem = preferredName;
            else if (cmb.Items.Count > 0)
                cmb.SelectedIndex = 0;
        }

        private void OnColFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            PopulateSectionCombo(CmbColSec, CmbColTypeFilter.SelectedIndex, CmbColSec.SelectedItem as string);
        }

        private void OnBeamFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            PopulateSectionCombo(CmbBeamSec, CmbBeamTypeFilter.SelectedIndex, CmbBeamSec.SelectedItem as string);
        }

        private void OnBraceFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            PopulateSectionCombo(CmbBraceSec, CmbBraceTypeFilter.SelectedIndex, CmbBraceSec.SelectedItem as string);
        }

        private void OnStructureTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            Data.StructureType = GetSelectedType();
            if (!LockStructureType)
            {
                Title = ChainPlacementMode
                    ? $"连续绘制 - {Data.StructureTypeDisplayName}"
                    : Data.StructureTypeDisplayName;
            }
            UpdatePerComponentFieldVisibilityAndLabels();
            UpdatePlacementButtons();
            RefreshLivePreview();
        }

        private ParametricStructureType GetSelectedType()
        {
            int idx = CmbStructureType.SelectedIndex;
            return idx >= 0 && idx < _availableTypes.Count ? _availableTypes[idx] : Data.StructureType;
        }

        private void UpdatePerComponentFieldVisibilityAndLabels()
        {
            ParametricStructureType st = Data.StructureType;

            SetRowVisible(LblSpan, TxtSpan, true);
            SetRowVisible(LblLength, TxtLength, true);
            SetRowVisible(LblHeight, TxtHeight, true);
            SetRowVisible(LblSubHeight, TxtSubHeight, true);
            SetSectionRowVisible(LblColSec, CmbColTypeFilter, CmbColSec, true);
            SetSectionRowVisible(LblBeamSec, CmbBeamTypeFilter, CmbBeamSec, true);
            SetSectionRowVisible(LblBraceSec, CmbBraceTypeFilter, CmbBraceSec, true);

            switch (st)
            {
                case ParametricStructureType.PortalFrame:
                    LblSpan.Text = "跨度 B (mm):";
                    LblHeight.Text = "柱高 H (mm):";
                    SetRowVisible(LblLength, TxtLength, false);
                    SetRowVisible(LblSubHeight, TxtSubHeight, false);
                    LblColSec.Text = "立柱截面:";
                    LblBeamSec.Text = "主梁截面:";
                    SetSectionRowVisible(LblBraceSec, CmbBraceTypeFilter, CmbBraceSec, false);
                    break;

                case ParametricStructureType.MultiSpanPortalFrame:
                    LblSpan.Text = "总跨度 B (mm):";
                    LblHeight.Text = "柱高 H (mm):";
                    SetRowVisible(LblLength, TxtLength, false);
                    SetRowVisible(LblSubHeight, TxtSubHeight, false);
                    LblColSec.Text = "立柱截面:";
                    LblBeamSec.Text = "主梁截面:";
                    LblBraceSec.Text = "隅撑截面:";
                    break;

                case ParametricStructureType.BracedFrame:
                    LblSpan.Text = "跨度 B (mm):";
                    LblHeight.Text = "总高 H (mm):";
                    LblSubHeight.Text = "中梁标高 h1:";
                    SetRowVisible(LblLength, TxtLength, false);
                    LblColSec.Text = "立柱截面:";
                    LblBeamSec.Text = "横梁截面:";
                    LblBraceSec.Text = "斜撑截面:";
                    break;

                case ParametricStructureType.DoubleTierFrame:
                    LblSpan.Text = "管廊跨度 B:";
                    LblHeight.Text = "顶层标高 H:";
                    LblSubHeight.Text = "一层标高 h1:";
                    SetRowVisible(LblLength, TxtLength, false);
                    LblColSec.Text = "立柱截面:";
                    LblBeamSec.Text = "横梁截面:";
                    LblBraceSec.Text = "隅撑截面:";
                    break;

                case ParametricStructureType.MultiBaySpaceFrame:
                    LblSpan.Text = "横向跨度 B:";
                    LblLength.Text = "纵向进深 L:";
                    LblHeight.Text = "顶层标高 H:";
                    LblSubHeight.Text = "一层标高 h1:";
                    LblColSec.Text = "框架柱截面:";
                    LblBeamSec.Text = "主梁截面:";
                    LblBraceSec.Text = "次梁/斜撑:";
                    break;

                case ParametricStructureType.CantileverBracket:
                    LblSpan.Text = "挑出长度 L:";
                    LblHeight.Text = "立柱高度 H:";
                    LblSubHeight.Text = "斜撑落差 h1:";
                    SetRowVisible(LblLength, TxtLength, false);
                    LblColSec.Text = "附墙柱截面:";
                    LblBeamSec.Text = "挑梁截面:";
                    LblBraceSec.Text = "斜撑截面:";
                    break;

                case ParametricStructureType.SpacePlatform:
                    LblSpan.Text = "平台跨度 B:";
                    LblLength.Text = "平台长度 L:";
                    LblHeight.Text = "平台标高 H:";
                    LblSubHeight.Text = "护栏高度 h1:";
                    LblColSec.Text = "平台柱截面:";
                    LblBeamSec.Text = "主梁截面:";
                    LblBraceSec.Text = "次梁截面:";
                    break;

                case ParametricStructureType.CantileverWalkway:
                    LblSpan.Text = "走道总长 L:";
                    LblLength.Text = "走道宽度 W:";
                    LblHeight.Text = "安装标高 H:";
                    LblSubHeight.Text = "护栏高度 h1:";
                    LblColSec.Text = "牛腿梁截面:";
                    LblBeamSec.Text = "走道边梁:";
                    LblBraceSec.Text = "三角斜撑:";
                    break;

                case ParametricStructureType.RoofTruss:
                    LblSpan.Text = "屋架跨度 B:";
                    LblHeight.Text = "柱顶檐高 H:";
                    LblSubHeight.Text = "屋脊矢高 h1:";
                    SetRowVisible(LblLength, TxtLength, false);
                    LblColSec.Text = "桁架柱截面:";
                    LblBeamSec.Text = "上下弦截面:";
                    LblBraceSec.Text = "腹杆截面:";
                    break;

                case ParametricStructureType.PipeBridgeTruss:
                    LblSpan.Text = "廊桥跨度 L:";
                    LblLength.Text = "廊桥净宽 W:";
                    LblHeight.Text = "桁架高度 H:";
                    SetRowVisible(LblSubHeight, TxtSubHeight, false);
                    LblColSec.Text = "弦杆截面:";
                    LblBeamSec.Text = "横梁截面:";
                    LblBraceSec.Text = "腹杆截面:";
                    break;

                case ParametricStructureType.SpiralStair:
                    LblSpan.Text = "盘梯外径 D:";
                    LblLength.Text = "踏步净宽 W:";
                    LblHeight.Text = "爬升总高 H:";
                    LblSubHeight.Text = "旋转角度 (°):";
                    LblColSec.Text = "螺旋斜梁:";
                    LblBeamSec.Text = "栏杆扶手:";
                    LblBraceSec.Text = "踏步板截面:";
                    break;

                case ParametricStructureType.StraightStair:
                    LblSpan.Text = "水平投影长 L:";
                    LblLength.Text = "梯段净宽 W:";
                    LblHeight.Text = "爬升层高 H:";
                    LblSubHeight.Text = "扶手高度 h1:";
                    LblColSec.Text = "梯梁(斜梁):";
                    LblBeamSec.Text = "栏杆扶手管:";
                    LblBraceSec.Text = "踏步板截面:";
                    break;

                case ParametricStructureType.SafetyCagedLadder:
                    LblSpan.Text = "直梯净宽 W:";
                    LblLength.Text = "护笼直径 D:";
                    LblHeight.Text = "爬升高度 H:";
                    LblSubHeight.Text = "起笼高度 h1:";
                    LblColSec.Text = "立管截面:";
                    LblBeamSec.Text = "踏棍/护笼:";
                    SetSectionRowVisible(LblBraceSec, CmbBraceTypeFilter, CmbBraceSec, false);
                    break;

                case ParametricStructureType.HandrailGuardrail:
                    LblSpan.Text = "栏杆总长 L:";
                    LblHeight.Text = "栏杆高度 H:";
                    LblSubHeight.Text = "立柱间距 S:";
                    SetRowVisible(LblLength, TxtLength, false);
                    LblColSec.Text = "立柱截面:";
                    LblBeamSec.Text = "扶手横杆:";
                    SetSectionRowVisible(LblBraceSec, CmbBraceTypeFilter, CmbBraceSec, false);
                    break;

                case ParametricStructureType.LinearSteelMember:
                    LblSpan.Text = "杆件长度 L:";
                    SetRowVisible(LblLength, TxtLength, false);
                    SetRowVisible(LblHeight, TxtHeight, false);
                    SetRowVisible(LblSubHeight, TxtSubHeight, false);
                    LblColSec.Text = "型钢截面:";
                    SetSectionRowVisible(LblBeamSec, CmbBeamTypeFilter, CmbBeamSec, false);
                    SetSectionRowVisible(LblBraceSec, CmbBraceTypeFilter, CmbBraceSec, false);
                    break;

                case ParametricStructureType.ArcSteelMember:
                    LblSpan.Text = "圆弧半径 R:";
                    LblSubHeight.Text = "扫掠角 (°):";
                    SetRowVisible(LblLength, TxtLength, false);
                    SetRowVisible(LblHeight, TxtHeight, false);
                    LblColSec.Text = "型钢截面:";
                    SetSectionRowVisible(LblBeamSec, CmbBeamTypeFilter, CmbBeamSec, false);
                    SetSectionRowVisible(LblBraceSec, CmbBraceTypeFilter, CmbBraceSec, false);
                    break;

                case ParametricStructureType.SpiralSteelMember:
                    LblSpan.Text = "轴线水平长 L:";
                    LblLength.Text = "螺旋半径 R:";
                    LblSubHeight.Text = "圈数 n:";
                    SetRowVisible(LblHeight, TxtHeight, false);
                    LblColSec.Text = "型钢截面:";
                    SetSectionRowVisible(LblBeamSec, CmbBeamTypeFilter, CmbBeamSec, false);
                    SetSectionRowVisible(LblBraceSec, CmbBraceTypeFilter, CmbBraceSec, false);
                    break;

                case ParametricStructureType.VesselSupport:
                    LblSpan.Text = "容器外径 DN:";
                    LblLength.Text = "鞍座宽度 B:";
                    LblHeight.Text = "中心标高 H:";
                    LblSubHeight.Text = "包角 (°):";
                    LblColSec.Text = "底板/腹板:";
                    LblBeamSec.Text = "弧面板截面:";
                    SetSectionRowVisible(LblBraceSec, CmbBraceTypeFilter, CmbBraceSec, false);
                    break;
            }
        }

        private void OnAnyParameterChanged(object sender, EventArgs e)
        {
            if (_isInitializing) return;
            RefreshLivePreview();
        }

        private bool TryReadFormToData(out string errorMsg)
        {
            errorMsg = null;
            if (!double.TryParse(TxtSpan.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double span) || span < 100)
            {
                errorMsg = "主尺寸必须大于 100 mm。";
                return false;
            }
            if (!double.TryParse(TxtLength.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double len) || len < 50)
            {
                len = Data.Length > 50 ? Data.Length : 1000;
            }
            if (!double.TryParse(TxtHeight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double h) || h < 100)
            {
                h = Data.Height > 100 ? Data.Height : 1000;
            }
            double minSub = Data.StructureType == ParametricStructureType.SpiralSteelMember ? 0.25 : 10.0;
            if (!double.TryParse(TxtSubHeight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double subH) || subH < minSub)
            {
                subH = Data.SubHeight >= minSub ? Data.SubHeight : (minSub < 1.0 ? 3.0 : 500.0);
            }

            Data.Tag = string.IsNullOrWhiteSpace(TxtTag.Text) ? "ST-101" : TxtTag.Text.Trim();
            if (!LockStructureType)
            {
                Data.StructureType = GetSelectedType();
            }
            Data.Material = (CmbMaterial.SelectedItem as string) ?? "Q355B";
            Data.Span = span;
            Data.Length = len;
            Data.Height = h;
            Data.SubHeight = subH;
            Data.ColumnSection = (CmbColSec.SelectedItem as string) ?? Data.ColumnSection;
            Data.BeamSection = (CmbBeamSec.SelectedItem as string) ?? Data.BeamSection;
            Data.BraceSection = (CmbBraceSec.SelectedItem as string) ?? Data.BraceSection;
            return true;
        }

        private void RefreshLivePreview()
        {
            if (_isInitializing) return;
            if (!TryReadFormToData(out _)) return;

            SchematicView?.RenderSchematic(Data);

            var items = SteelStructureBuilder.CalculatePartBillItems(Data);
            DgPreviewBom.ItemsSource = items;

            double sumKg = items.Sum(x => x.TotalWeightKg);
            double sumM2 = items.Sum(x => x.TotalSurfaceAreaM2);
            TxtLiveWeightSummary.Text =
                $"总重: {sumKg:0.00} kg  |  刷漆面积: {sumM2:0.00} m²  |  零件种数: {items.Count}";
            TabBom.Header = $"材料清单 ({sumKg:0.#}kg)";
        }

        private void OnSinglePointConfirmClick(object sender, RoutedEventArgs e)
        {
            OnConfirmWithMode(useLinearDraw: false);
        }

        private void OnLinearDrawConfirmClick(object sender, RoutedEventArgs e)
        {
            OnConfirmWithMode(useLinearDraw: true);
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnConfirmWithMode(bool useLinearDraw)
        {
            if (!TryReadFormToData(out string err))
            {
                MessageBox.Show(err, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            UseTwoPointLinearDraw = useLinearDraw;
            DialogResult = true;
            Close();
        }
    }
}
