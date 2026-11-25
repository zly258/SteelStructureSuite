using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SteelStructureSuite.Commands;

using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Catalog;
using SteelStructureSuite.Services.Modeling;
using SteelStructureSuite.Services.Settings;
using SteelStructureSuite.Services.Templates;
using SteelStructureSuite.Views.Controls;

namespace SteelStructureSuite.Views
{
    public partial class StructureEditWindow : Window
    {
        public SteelStructureParams Data { get; private set; }
        public bool IsEditMode { get; }
        public bool LockStructureType { get; }
        public bool UseTwoPointLinearDraw { get; private set; }
        public bool UseCurvePick { get; private set; }
        public bool ChainPlacementMode { get; }

        private bool _isInitializing = true;
        private readonly List<ParametricStructureType> _availableTypes = new List<ParametricStructureType>();
        private List<StructureTemplate> _currentTemplates = new List<StructureTemplate>();

        public StructureEditWindow(
            SteelStructureParams initialParams,
            bool isEditMode = false,
            bool lockStructureType = true,
            bool chainPlacementMode = false)
        {
            InitializeComponent();
            Width = Math.Max(Width, 590);
            MinWidth = 550;
            MinHeight = 420;
            if (DgPreviewBom != null)
            {
                DgPreviewBom.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                DgPreviewBom.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                foreach (var col in DgPreviewBom.Columns)
                {
                    if (col.MinWidth < 52) col.MinWidth = 52;
                }
            }

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
            foreach (string m in new[] { "Q235B", "Q355B", "Q355D", "Q420B", "S30408", "15CrMoR" })
                CmbMaterial.Items.Add(m);
            string lastMaterial = UserSettings.GetString("Edit.Material", null);
            CmbMaterial.SelectedItem =
                (!string.IsNullOrEmpty(lastMaterial) && CmbMaterial.Items.Contains(lastMaterial))
                    ? lastMaterial
                    : Data.Material ?? "Q355B";
            if (CmbMaterial.SelectedIndex < 0) CmbMaterial.SelectedIndex = 1;

            if (isEditMode)
            {
                SyncControlsFromData();
            }
            else
            {
                ApplyRememberedSections();
            }

            ReloadPresetsForCurrentType();

            if (isEditMode)
            {
                BtnUpdate.Visibility = Visibility.Visible;
                BtnUpdate.IsDefault = true;
                ApplyActionStyle(BtnUpdate, true);
            }
            else
            {
                BtnUpdate.Visibility = Visibility.Collapsed;
            }
            UpdatePlacementButtons();

            SchematicView.Loaded += (s, e) => SchematicView.RenderSchematic(Data);
            SchematicView.SizeChanged += (s, e) => SchematicView.RenderSchematic(Data);

            _isInitializing = false;
            UpdatePerComponentFieldVisibilityAndLabels();
            RefreshLivePreview();

            UserSettings.RestoreWindowState(this, "StructureEditWindow");
            Closed += (s, e) =>
            {
                SaveLastSettings();
                UserSettings.SaveWindowState(this, "StructureEditWindow");
            };
        }

        /// <summary>只恢复当前构件类型自己记住的截面，没有记录时用该类型的初始预设。</summary>
        private void ApplyRememberedSections()
        {
            ApplySectionChoice(ColTypePicker, ColSpecPicker, "ColSec", Data.ColumnSection, v => Data.ColumnSection = v);
            ApplySectionChoice(BeamTypePicker, BeamSpecPicker, "BeamSec", Data.BeamSection, v => Data.BeamSection = v);
            ApplySectionChoice(BraceTypePicker, BraceSpecPicker, "BraceSec", Data.BraceSection, v => Data.BraceSection = v);
            SyncControlsFromData();
        }

        private void ApplySectionChoice(SectionTypePickerControl typePicker, SectionPickerControl specPicker, string sectionPart, string fallback, Action<string> assign)
        {
            string saved = UserSettings.GetString(SectionSettingKey(sectionPart), null);
            bool savedOk = !string.IsNullOrEmpty(saved) && SectionCatalogService.GetAllNames().Contains(saved);
            string preferred = savedOk ? saved : fallback;
            ApplySectionToPickers(typePicker, specPicker, preferred);
            if (!string.IsNullOrEmpty(preferred)) assign(preferred);
        }

        /// <summary>按截面代号反推所属类型，同时同步“类型”下拉与“规格”下拉的筛选。</summary>
        private static void ApplySectionToPickers(SectionTypePickerControl typePicker, SectionPickerControl specPicker, string sectionName)
        {
            SectionShapeType? shape = null;
            if (!string.IsNullOrWhiteSpace(sectionName))
            {
                SectionProfile profile = SectionCatalogService.GetByName(sectionName);
                if (profile != null && string.Equals(profile.Name, sectionName, StringComparison.OrdinalIgnoreCase))
                    shape = profile.Shape;
            }

            if (typePicker != null)
            {
                typePicker.SelectedShapeType = shape;
                typePicker.SelectedCategory = shape.HasValue ? SectionProfile.GetCategory(shape.Value) : (SectionCategory?)null;
            }
            if (specPicker != null)
            {
                specPicker.FilterShapeType = shape;
                specPicker.SelectedSectionName = sectionName;
            }
        }

        private string SectionSettingKey(string part)
        {
            return "Edit." + Data.StructureType.ToString() + "." + part;
        }

        /// <summary>按当前构件类型分别记住截面，避免门架、栏杆、管托互相串用。</summary>
        private void SaveLastSettings()
        {
            UserSettings.SetString("Edit.Material", CmbMaterial.SelectedItem as string);
            UserSettings.SetString(SectionSettingKey("ColSec"), ColSpecPicker?.SelectedSectionName);
            UserSettings.SetString(SectionSettingKey("BeamSec"), BeamSpecPicker?.SelectedSectionName);
            UserSettings.SetString(SectionSettingKey("BraceSec"), BraceSpecPicker?.SelectedSectionName);
            UserSettings.Save();
        }

        private void SyncControlsFromData()
        {
            TxtSpan.Text = Data.Span.ToString("0.#", CultureInfo.InvariantCulture);
            TxtLength.Text = Data.Length.ToString("0.#", CultureInfo.InvariantCulture);
            TxtHeight.Text = Data.Height.ToString("0.#", CultureInfo.InvariantCulture);
            TxtSubHeight.Text = Data.SubHeight.ToString("0.#", CultureInfo.InvariantCulture);
            TxtBayCount.Text = Data.BayCount.ToString(CultureInfo.InvariantCulture);
            TxtRotationDeg.Text = Data.RotationDeg.ToString("0.#", CultureInfo.InvariantCulture);
            TxtEndOffsetZ.Text = Data.EndPointOffsetZ.ToString("0.#", CultureInfo.InvariantCulture);

            ApplySectionToPickers(ColTypePicker, ColSpecPicker, Data.ColumnSection);
            ApplySectionToPickers(BeamTypePicker, BeamSpecPicker, Data.BeamSection);
            ApplySectionToPickers(BraceTypePicker, BraceSpecPicker, Data.BraceSection);
        }

        /// <summary>重新加载当前构件类型可用的参数模板（该类型专属 + 通用模板）。</summary>
        private void ReloadPresetsForCurrentType()
        {
            _currentTemplates = StructureTemplateStore.GetForType(Data.StructureType);

            _isInitializing = true;
            CmbPreset.Items.Clear();
            CmbPreset.Items.Add("-- 选择参数模板快速套用 --");
            foreach (StructureTemplate tpl in _currentTemplates)
            {
                string suffix = tpl.IsGeneric ? "（通用）" : string.Empty;
                CmbPreset.Items.Add(tpl.Name + suffix);
            }
            CmbPreset.SelectedIndex = 0;
            _isInitializing = false;
        }

        private void OnPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            int idx = CmbPreset.SelectedIndex - 1;
            if (idx < 0 || idx >= _currentTemplates.Count) return;

            StructureTemplate tpl = _currentTemplates[idx];

            _isInitializing = true;
            // 模板整体套用（保留当前位号、插入点与坐标系）
            tpl.ApplyTo(Data);
            SyncControlsFromData();
            _isInitializing = false;

            UpdatePerComponentFieldVisibilityAndLabels();
            RefreshLivePreview();
        }

        /// <summary>把当前全部参数保存成一条模板。</summary>
        private void OnSaveAsTemplateClick(object sender, RoutedEventArgs e)
        {
            if (!TryReadFormToData(out string err))
            {
                MessageBox.Show(this, err, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new TemplateNameWindow(Data.StructureTypeDisplayName)
            {
                Owner = this
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                StructureTemplate tpl = StructureTemplate.FromParams(
                    Data, dlg.TemplateName, dlg.TemplateNote, dlg.IsGeneric);

                StructureTemplateStore.Save(tpl);
                ReloadPresetsForCurrentType();

                MessageBox.Show(this,
                    $"模板「{tpl.Name}」已保存，可在模板下拉中直接套用。",
                    "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存模板失败：" + ex.Message, "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>打开模板管理窗口。</summary>
        private void OnManageTemplatesClick(object sender, RoutedEventArgs e)
        {
            var win = new TemplateManagerWindow { Owner = this };
            win.ShowDialog();

            // 管理窗口可能改名或删除，回来后刷新下拉
            ReloadPresetsForCurrentType();
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

            if (Data.RequiresPickedCurve)
            {
                BtnSinglePt.Visibility = Visibility.Collapsed;
                BtnLinearTwoPt.Visibility = Visibility.Visible;
                BtnLinearTwoPt.Content = Data.PickedCurveButtonText;
                BtnLinearTwoPt.IsDefault = true;
                BtnSinglePt.IsDefault = false;
                ApplyActionStyle(BtnSinglePt, false);
                ApplyActionStyle(BtnLinearTwoPt, true);
                return;
            }

            BtnSinglePt.Content = "单点放置";
            bool canLinear = Data.SupportsTwoPointDraw;
            BtnLinearTwoPt.Visibility = canLinear ? Visibility.Visible : Visibility.Collapsed;
            BtnLinearTwoPt.Content = Data.TwoPointButtonText;
            BtnLinearTwoPt.IsDefault = canLinear && Data.PrefersLinearTwoPointDraw;
            BtnSinglePt.IsDefault = !BtnLinearTwoPt.IsDefault;
            ApplyActionStyle(BtnSinglePt, BtnSinglePt.IsDefault);
            ApplyActionStyle(BtnLinearTwoPt, BtnLinearTwoPt.IsDefault);
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

        private static void SetSectionRowVisible(TextBlock lbl, SectionTypePickerControl typePicker, SectionPickerControl specPicker, bool visible)
        {
            Visibility v = visible ? Visibility.Visible : Visibility.Collapsed;
            lbl.Visibility = v;
            if (typePicker != null) typePicker.Visibility = v;
            if (specPicker != null) specPicker.Visibility = v;
        }

        private void OnColTypeChanged(object sender, EventArgs e)
        {
            if (_isInitializing) return;
            ApplySectionType(ColTypePicker, ColSpecPicker);
        }

        private void OnBeamTypeChanged(object sender, EventArgs e)
        {
            if (_isInitializing) return;
            ApplySectionType(BeamTypePicker, BeamSpecPicker);
        }

        private void OnBraceTypeChanged(object sender, EventArgs e)
        {
            if (_isInitializing) return;
            ApplySectionType(BraceTypePicker, BraceSpecPicker);
        }

        /// <summary>“类型”变更后，把筛选同步到“规格”下拉，并保证规格选中项落在筛选结果内。</summary>
        private void ApplySectionType(SectionTypePickerControl typePicker, SectionPickerControl specPicker)
        {
            if (specPicker == null) return;
            specPicker.FilterCategory = typePicker?.SelectedCategory;
            specPicker.FilterShapeType = typePicker?.SelectedShapeType;
            specPicker.EnsureSelectionInFilteredList();
            RefreshLivePreview();
        }

        private void OnStructureTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            SaveLastSettings();
            ParametricStructureType type = GetSelectedType();
            string tag = Data.Tag;
            string material = CmbMaterial.SelectedItem as string ?? Data.Material;
            double rotation = Data.RotationDeg;
            SteelStructureParams fresh = ModelingCommands.CreateDefaultParamsForType(type);
            fresh.Tag = tag;
            if (!string.IsNullOrEmpty(material)) fresh.Material = material;
            fresh.RotationDeg = rotation;
            Data = fresh;
            if (!LockStructureType)
            {
                Title = ChainPlacementMode
                    ? $"连续绘制 - {Data.StructureTypeDisplayName}"
                    : Data.StructureTypeDisplayName;
            }
            _isInitializing = true;
            TxtTag.Text = Data.Tag;
            ApplyRememberedSections();
            ReloadPresetsForCurrentType();
            _isInitializing = false;
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
            SetRowVisible(LblBayCount, TxtBayCount, false);
            SetRowVisible(LblEndDz, TxtEndOffsetZ, false);
            SetSectionRowVisible(LblColSec, ColTypePicker, ColSpecPicker, true);
            SetSectionRowVisible(LblBeamSec, BeamTypePicker, BeamSpecPicker, true);
            SetSectionRowVisible(LblBraceSec, BraceTypePicker, BraceSpecPicker, true);

            switch (st)
            {
                case ParametricStructureType.PortalFrame:
                    LblSpan.Text = "跨度 B (mm):";
                    LblHeight.Text = "柱高 H (mm):";
                    SetRowVisible(LblLength, TxtLength, false);
                    SetRowVisible(LblSubHeight, TxtSubHeight, false);
                    LblColSec.Text = "立柱截面:";
                    LblBeamSec.Text = "主梁截面:";
                    SetSectionRowVisible(LblBraceSec, BraceTypePicker, BraceSpecPicker, false);
                    break;

                case ParametricStructureType.MultiSpanPortalFrame:
                    LblSpan.Text = "总跨度 B (mm):";
                    LblHeight.Text = "柱高 H (mm):";
                    LblBayCount.Text = "跨数 N (2~6):";
                    SetRowVisible(LblLength, TxtLength, false);
                    SetRowVisible(LblSubHeight, TxtSubHeight, false);
                    SetRowVisible(LblBayCount, TxtBayCount, true);
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
                    SetSectionRowVisible(LblBraceSec, BraceTypePicker, BraceSpecPicker, false);
                    break;

                case ParametricStructureType.HandrailGuardrail:
                    LblSpan.Text = "栏杆总长 L:";
                    LblHeight.Text = "栏杆高度 H:";
                    LblSubHeight.Text = "立柱间距 S:";
                    SetRowVisible(LblLength, TxtLength, false);
                    LblColSec.Text = "立柱截面:";
                    LblBeamSec.Text = "扶手横杆:";
                    SetSectionRowVisible(LblBraceSec, BraceTypePicker, BraceSpecPicker, false);
                    break;

                case ParametricStructureType.LinearSteelMember:
                    LblSpan.Text = "水平长度 L:";
                    SetRowVisible(LblLength, TxtLength, false);
                    SetRowVisible(LblHeight, TxtHeight, false);
                    SetRowVisible(LblSubHeight, TxtSubHeight, false);
                    SetRowVisible(LblEndDz, TxtEndOffsetZ, true);
                    LblColSec.Text = "型钢截面:";
                    SetSectionRowVisible(LblBeamSec, BeamTypePicker, BeamSpecPicker, false);
                    SetSectionRowVisible(LblBraceSec, BraceTypePicker, BraceSpecPicker, false);
                    break;

                case ParametricStructureType.ArcSteelMember:
                    LblSpan.Text = "圆弧半径 R:";
                    LblSubHeight.Text = "扫掠角 (°):";
                    SetRowVisible(LblLength, TxtLength, false);
                    SetRowVisible(LblHeight, TxtHeight, false);
                    SetRowVisible(LblEndDz, TxtEndOffsetZ, true);
                    LblColSec.Text = "型钢截面:";
                    SetSectionRowVisible(LblBeamSec, BeamTypePicker, BeamSpecPicker, false);
                    SetSectionRowVisible(LblBraceSec, BraceTypePicker, BraceSpecPicker, false);
                    break;

                case ParametricStructureType.SpiralSteelMember:
                    LblSpan.Text = "螺旋高度 H:";
                    LblLength.Text = "底部半径 R:";
                    LblSubHeight.Text = "螺旋圈数 n:";
                    SetRowVisible(LblHeight, TxtHeight, false);
                    SetRowVisible(LblEndDz, TxtEndOffsetZ, false);
                    LblColSec.Text = "型钢截面:";
                    SetSectionRowVisible(LblBeamSec, BeamTypePicker, BeamSpecPicker, false);
                    SetSectionRowVisible(LblBraceSec, BraceTypePicker, BraceSpecPicker, false);
                    break;

                case ParametricStructureType.VesselSupport:
                    LblSpan.Text = "容器外径 DN:";
                    LblLength.Text = "鞍座宽度 B:";
                    LblHeight.Text = "中心标高 H:";
                    LblSubHeight.Text = "包角 (°):";
                    LblColSec.Text = "底板/腹板:";
                    LblBeamSec.Text = "弧面板截面:";
                    SetSectionRowVisible(LblBraceSec, BraceTypePicker, BraceSpecPicker, false);
                    break;

                case ParametricStructureType.TowerCircularPlatform:
                    LblSpan.Text = "塔体外径 DN:";
                    LblLength.Text = "平台环宽 W:";
                    LblHeight.Text = "牛腿高差 H:";
                    LblSubHeight.Text = "平台包角 (°):";
                    LblColSec.Text = "牛腿主梁:";
                    LblBeamSec.Text = "内外环梁:";
                    LblBraceSec.Text = "牛腿斜撑:";
                    break;

                case ParametricStructureType.VerticalVesselSkirt:
                    LblSpan.Text = "裙座外径 DN:";
                    LblLength.Text = "底环板宽 B:";
                    LblHeight.Text = "裙座总高 H:";
                    LblSubHeight.Text = "引出孔径 d:";
                    LblBayCount.Text = "座圈筋板数:";
                    SetRowVisible(LblBayCount, TxtBayCount, true);
                    LblColSec.Text = "筒壁参考:";
                    SetSectionRowVisible(LblBeamSec, BeamTypePicker, BeamSpecPicker, false);
                    SetSectionRowVisible(LblBraceSec, BraceTypePicker, BraceSpecPicker, false);
                    break;

                case ParametricStructureType.PipeShoeSupport:
                    LblSpan.Text = "管道外径 DN:";
                    LblLength.Text = "管托长度 L:";
                    LblHeight.Text = "管中心高 H:";
                    LblSubHeight.Text = "底板宽度 B:";
                    LblColSec.Text = "底板/腹板:";
                    LblBeamSec.Text = "弧形鞍板:";
                    LblBraceSec.Text = "双螺栓管卡:";
                    break;

                case ParametricStructureType.TankRoofHandrail:
                    LblSpan.Text = "储罐直径 D:";
                    LblHeight.Text = "栏杆高度 H:";
                    LblSubHeight.Text = "环绕包角 (°):";
                    SetRowVisible(LblLength, TxtLength, false);
                    LblColSec.Text = "栏杆立柱:";
                    LblBeamSec.Text = "环形扶手管:";
                    SetSectionRowVisible(LblBraceSec, BraceTypePicker, BraceSpecPicker, false);
                    break;

                case ParametricStructureType.SubstationGantryFrame:
                    LblSpan.Text = "构架跨度 B:";
                    LblLength.Text = "A字柱根开 L:";
                    LblHeight.Text = "横梁标高 H:";
                    LblSubHeight.Text = "避雷针高 h1:";
                    LblColSec.Text = "A字柱主材:";
                    LblBeamSec.Text = "格构梁弦杆:";
                    LblBraceSec.Text = "格构腹杆:";
                    break;

                case ParametricStructureType.CableTrenchSupport:
                    LblSpan.Text = "电缆沟总长 L:";
                    LblLength.Text = "托臂挑宽 W:";
                    LblHeight.Text = "立柱总高 H:";
                    LblSubHeight.Text = "层间净距 s:";
                    LblBayCount.Text = "托臂层数 (2~6):";
                    SetRowVisible(LblBayCount, TxtBayCount, true);
                    LblColSec.Text = "预埋槽钢柱:";
                    LblBeamSec.Text = "角钢托臂:";
                    SetSectionRowVisible(LblBraceSec, BraceTypePicker, BraceSpecPicker, false);
                    break;

                case ParametricStructureType.CraneRunwayGirder:
                    LblSpan.Text = "吊车梁跨度 L:";
                    LblLength.Text = "制动板宽 B:";
                    LblHeight.Text = "吊车梁高 H:";
                    LblSubHeight.Text = "加劲肋间距 s:";
                    LblColSec.Text = "主梁参考:";
                    LblBeamSec.Text = "制动边梁:";
                    SetSectionRowVisible(LblBraceSec, BraceTypePicker, BraceSpecPicker, false);
                    break;

                case ParametricStructureType.CrossBracingFrame:
                    LblSpan.Text = "厂房柱距 L:";
                    LblHeight.Text = "厂房柱高 H:";
                    LblSubHeight.Text = "牛腿标高 h1:";
                    SetRowVisible(LblLength, TxtLength, false);
                    LblColSec.Text = "厂房主柱:";
                    LblBeamSec.Text = "刚性系杆:";
                    LblBraceSec.Text = "交叉斜撑:";
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
            if (!double.TryParse(TxtSpan.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double span) || span < 80)
            {
                errorMsg = "主尺寸必须大于 80 mm。";
                return false;
            }
            if (!double.TryParse(TxtLength.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double len) || len < 30)
            {
                len = Data.Length > 30 ? Data.Length : 1000;
            }
            if (!double.TryParse(TxtHeight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double h) || h < 80)
            {
                h = Data.Height > 80 ? Data.Height : 1000;
            }
            double minSub = Data.StructureType == ParametricStructureType.SpiralSteelMember ? 0.05 : 1.0;
            if (!double.TryParse(TxtSubHeight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double subH) || subH < minSub)
            {
                subH = Data.SubHeight >= minSub ? Data.SubHeight : (Data.StructureType == ParametricStructureType.SpiralSteelMember ? 3.0 : 500);
            }
            if (int.TryParse(TxtBayCount.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bays) && bays >= 1 && bays <= 36)
            {
                Data.BayCount = bays;
            }
            if (double.TryParse(TxtRotationDeg.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double rotDeg))
            {
                Data.RotationDeg = rotDeg;
            }
            if (double.TryParse(TxtEndOffsetZ.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double dz))
            {
                Data.EndPointOffsetZ = dz;
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
            if (!string.IsNullOrWhiteSpace(ColSpecPicker?.SelectedSectionName)) Data.ColumnSection = ColSpecPicker.SelectedSectionName;
            if (!string.IsNullOrWhiteSpace(BeamSpecPicker?.SelectedSectionName)) Data.BeamSection = BeamSpecPicker.SelectedSectionName;
            if (!string.IsNullOrWhiteSpace(BraceSpecPicker?.SelectedSectionName)) Data.BraceSection = BraceSpecPicker.SelectedSectionName;
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
            TbiBom.Header = $"材料清单 ({sumKg:0.#}kg)";
        }

        private void OnSinglePointConfirmClick(object sender, RoutedEventArgs e)
        {
            OnConfirmWithMode(useLinearDraw: false);
        }

        private void OnLinearDrawConfirmClick(object sender, RoutedEventArgs e)
        {
            OnConfirmWithMode(useLinearDraw: true);
        }


        private void ApplyActionStyle(System.Windows.Controls.Button button, bool primary)
        {
            // 主题样式已移除，统一使用 WPF 默认按钮外观。
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

            UseCurvePick = Data.RequiresPickedCurve;
            UseTwoPointLinearDraw = useLinearDraw && !UseCurvePick;
            DialogResult = true;
            Close();
        }
    }
}

