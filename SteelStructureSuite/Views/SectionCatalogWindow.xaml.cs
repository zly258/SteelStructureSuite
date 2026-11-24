using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Catalog;
using SteelStructureSuite.Services.Settings;

namespace SteelStructureSuite.Views
{
    /// <summary>
    /// 型钢截面库管理窗口（左侧分类树 + 右侧规格表格 + 动态参数编辑与 2D 实时预览）
    /// </summary>
    public partial class SectionCatalogWindow : Window
    {
        private enum TreeFilterKind
        {
            All,
            Category,
            ShapeType
        }

        private sealed class TreeNodeTag
        {
            public TreeFilterKind Kind { get; set; }
            public SectionCategory Category { get; set; }
            public SectionShapeType ShapeType { get; set; }
            public string Key { get; set; }
        }

        private sealed class ShapeTypeComboItem
        {
            public SectionShapeType ShapeType { get; set; }
            public string DisplayText { get; set; }
            public override string ToString() => DisplayText;
        }

        private TreeNodeTag _currentFilter = new TreeNodeTag { Kind = TreeFilterKind.All, Key = "ALL" };
        private bool _isSuppressingEvents;
        private bool _isManualMetricsActive;

        public SectionCatalogWindow()
        {
            InitializeComponent();

            InitEditTypeCombo();
            RebuildCategoryTree(UserSettings.GetString("Catalog.LastTreeNode", "ALL"));
            RefreshTableAndSummary();
            UpdateParameterFieldLayout(GetSelectedEditShapeType());
            UpdateLivePreview();
            TxtStorageInfo.Text = $"本地数据文件 (UTF-8 CSV，支持 Excel 编辑与多表导入导出): {SectionCatalogService.CatalogFilePath}";

            UserSettings.RestoreWindowState(this, "SectionCatalogWindow");
            Closed += (s, e) =>
            {
                UserSettings.SetString("Catalog.LastTreeNode", _currentFilter?.Key ?? "ALL");
                UserSettings.Save();
                UserSettings.SaveWindowState(this, "SectionCatalogWindow");
            };
        }

        private void InitEditTypeCombo()
        {
            _isSuppressingEvents = true;
            CmbType.Items.Clear();
            foreach (SectionShapeType st in SectionCatalogService.OrderedShapeTypes)
            {
                CmbType.Items.Add(new ShapeTypeComboItem
                {
                    ShapeType = st,
                    DisplayText = SectionProfile.GetShapeDisplayName(st)
                });
            }
            if (CmbType.Items.Count > 0)
            {
                CmbType.SelectedIndex = 0;
            }
            _isSuppressingEvents = false;
        }

        private void RebuildCategoryTree(string selectKey)
        {
            _isSuppressingEvents = true;
            TreeCategories.Items.Clear();

            var allSections = SectionCatalogService.GetAllSections();
            TreeViewItem rootItem = new TreeViewItem
            {
                Header = $"全部截面 ({allSections.Count})",
                IsExpanded = true,
                Tag = new TreeNodeTag { Kind = TreeFilterKind.All, Key = "ALL" }
            };

            TreeViewItem itemToSelect = rootItem;

            foreach (SectionCategory cat in SectionCatalogService.OrderedCategories)
            {
                var catSections = SectionCatalogService.GetByCategory(cat);
                string catKey = $"CAT_{(int)cat}";
                TreeViewItem catItem = new TreeViewItem
                {
                    Header = $"{SectionProfile.GetCategoryDisplayName(cat)} ({catSections.Count})",
                    IsExpanded = true,
                    Tag = new TreeNodeTag { Kind = TreeFilterKind.Category, Category = cat, Key = catKey }
                };

                if (string.Equals(selectKey, catKey, StringComparison.OrdinalIgnoreCase))
                {
                    itemToSelect = catItem;
                }

                foreach (SectionShapeType st in SectionCatalogService.GetShapeTypesByCategory(cat))
                {
                    var shapeSections = SectionCatalogService.GetByShapeType(st);
                    string shapeKey = $"SHAPE_{(int)st}";
                    TreeViewItem shapeItem = new TreeViewItem
                    {
                        Header = $"{SectionProfile.GetShapeDisplayName(st)} ({shapeSections.Count})",
                        Tag = new TreeNodeTag
                        {
                            Kind = TreeFilterKind.ShapeType,
                            Category = cat,
                            ShapeType = st,
                            Key = shapeKey
                        }
                    };

                    if (string.Equals(selectKey, shapeKey, StringComparison.OrdinalIgnoreCase))
                    {
                        itemToSelect = shapeItem;
                    }

                    catItem.Items.Add(shapeItem);
                }

                rootItem.Items.Add(catItem);
            }

            TreeCategories.Items.Add(rootItem);
            if (itemToSelect != null)
            {
                itemToSelect.IsSelected = true;
                _currentFilter = itemToSelect.Tag as TreeNodeTag ?? _currentFilter;
            }
            _isSuppressingEvents = false;
        }

        private void RefreshTableAndSummary()
        {
            var all = SectionCatalogService.GetAllSections();
            TxtSummary.Text = $"共 {SectionCatalogService.OrderedCategories.Length} 大类 {SectionCatalogService.OrderedShapeTypes.Length} 种截面类型，合计 {all.Count} 条规格";

            List<SectionProfile> source;
            switch (_currentFilter.Kind)
            {
                case TreeFilterKind.Category:
                    source = SectionCatalogService.GetByCategory(_currentFilter.Category);
                    TxtCurrentCategory.Text = SectionProfile.GetCategoryDisplayName(_currentFilter.Category);
                    break;
                case TreeFilterKind.ShapeType:
                    source = SectionCatalogService.GetByShapeType(_currentFilter.ShapeType);
                    TxtCurrentCategory.Text = SectionProfile.GetShapeDisplayName(_currentFilter.ShapeType);
                    break;
                default:
                    source = all;
                    TxtCurrentCategory.Text = "全部截面";
                    break;
            }

            string keyword = TxtSearch?.Text?.Trim();
            if (!string.IsNullOrEmpty(keyword))
            {
                source = source.Where(s =>
                    (s.Name != null && s.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (s.ShapeTypeDisplayName != null && s.ShapeTypeDisplayName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (s.CategoryDisplayName != null && s.CategoryDisplayName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                ).ToList();
            }

            GridSections.ItemsSource = null;
            GridSections.ItemsSource = source;
            TxtFilteredCount.Text = $"{source.Count} 项";
            BtnDelete.IsEnabled = GridSections.SelectedItem is SectionProfile;
        }

        private void OnTreeCategorySelected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_isSuppressingEvents) return;
            if (e.NewValue is TreeViewItem item && item.Tag is TreeNodeTag tag)
            {
                _currentFilter = tag;
                RefreshTableAndSummary();

                // 选中具体截面类型节点时，联动切换下方编辑框的目标类型
                if (tag.Kind == TreeFilterKind.ShapeType)
                {
                    SelectEditComboShape(tag.ShapeType);
                    var list = SectionCatalogService.GetByShapeType(tag.ShapeType);
                    if (list.Count > 0)
                    {
                        FillFormFromProfile(list[0]);
                    }
                }
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSuppressingEvents) return;
            RefreshTableAndSummary();
        }

        private void OnClearSearchClick(object sender, RoutedEventArgs e)
        {
            TxtSearch.Text = string.Empty;
            TxtSearch.Focus();
        }

        private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BtnDelete.IsEnabled = GridSections.SelectedItem is SectionProfile;
            if (GridSections.SelectedItem is SectionProfile sel)
            {
                FillFormFromProfile(sel);
            }
        }

        private void FillFormFromProfile(SectionProfile sec)
        {
            if (sec == null) return;
            _isSuppressingEvents = true;
            SelectEditComboShape(sec.Shape);
            TxtName.Text = sec.Name;
            TxtH.Text = sec.H.ToString("0.##", CultureInfo.InvariantCulture);
            TxtB.Text = sec.B.ToString("0.##", CultureInfo.InvariantCulture);
            TxtTw.Text = sec.Tw.ToString("0.##", CultureInfo.InvariantCulture);
            TxtTf.Text = sec.Tf.ToString("0.##", CultureInfo.InvariantCulture);
            TxtB2.Text = (sec.B2 > 0 ? sec.B2 : sec.B).ToString("0.##", CultureInfo.InvariantCulture);
            TxtTf2.Text = (sec.Tf2 > 0 ? sec.Tf2 : sec.Tf).ToString("0.##", CultureInfo.InvariantCulture);
            TxtLip.Text = (sec.Lip > 0 ? sec.Lip : 20.0).ToString("0.##", CultureInfo.InvariantCulture);
            TxtGap.Text = (sec.Gap > 0 ? sec.Gap : 10.0).ToString("0.##", CultureInfo.InvariantCulture);

            _isManualMetricsActive = sec.HasManualProperties;
            TxtArea.Text = sec.AreaCm2.ToString("0.00", CultureInfo.InvariantCulture);
            TxtWeight.Text = sec.UnitWeightKgPerM.ToString("0.00", CultureInfo.InvariantCulture);
            TxtSurface.Text = sec.PerimeterMPerM.ToString("0.000", CultureInfo.InvariantCulture);
            TxtMetricSource.Text = sec.PropertySourceDisplayName;
            _isSuppressingEvents = false;

            UpdateParameterFieldLayout(sec.Shape);
            UpdateLivePreview(refreshAutoMetrics: false);
        }

        private void SelectEditComboShape(SectionShapeType shapeType)
        {
            for (int i = 0; i < CmbType.Items.Count; i++)
            {
                if (CmbType.Items[i] is ShapeTypeComboItem item && item.ShapeType == shapeType)
                {
                    CmbType.SelectedIndex = i;
                    return;
                }
            }
        }

        private SectionShapeType GetSelectedEditShapeType()
        {
            if (CmbType.SelectedItem is ShapeTypeComboItem item)
                return item.ShapeType;
            return SectionShapeType.HBeam;
        }

        private void OnEditTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSuppressingEvents) return;
            _isManualMetricsActive = false;
            SectionShapeType shape = GetSelectedEditShapeType();
            UpdateParameterFieldLayout(shape);
            UpdateLivePreview(refreshAutoMetrics: true);
        }

        /// <summary>
        /// 按截面类型动态调整参数输入框的标签名称与显隐状态，减少误输入
        /// </summary>
        private void UpdateParameterFieldLayout(SectionShapeType shape)
        {
            // 先重置默认状态
            PanelH.Visibility = Visibility.Visible;
            PanelB.Visibility = Visibility.Visible;
            PanelTw.Visibility = Visibility.Visible;
            PanelTf.Visibility = Visibility.Visible;
            PanelB2.Visibility = Visibility.Collapsed;
            PanelTf2.Visibility = Visibility.Collapsed;
            PanelLip.Visibility = Visibility.Collapsed;
            PanelGap.Visibility = Visibility.Collapsed;

            LblH.Text = "高度 H";
            LblB.Text = "宽度 B";
            LblTw.Text = "腹板 tw";
            LblTf.Text = "翼缘 tf";

            switch (shape)
            {
                case SectionShapeType.WeldedHBeamUnequal:
                    LblB.Text = "上翼缘宽 B";
                    LblTf.Text = "上翼缘厚 tf";
                    PanelB2.Visibility = Visibility.Visible;
                    PanelTf2.Visibility = Visibility.Visible;
                    break;

                case SectionShapeType.Angle:
                case SectionShapeType.UnequalAngle:
                    LblH.Text = "长肢 H";
                    LblB.Text = shape == SectionShapeType.Angle ? "肢宽 B" : "短肢 B";
                    LblTw.Text = "肢厚 t";
                    PanelTf.Visibility = Visibility.Collapsed;
                    break;

                case SectionShapeType.DoubleAngle:
                    LblH.Text = "肢高 H";
                    LblB.Text = "肢宽 B";
                    LblTw.Text = "肢厚 t";
                    PanelTf.Visibility = Visibility.Collapsed;
                    PanelGap.Visibility = Visibility.Visible;
                    break;

                case SectionShapeType.DoubleChannel:
                    PanelGap.Visibility = Visibility.Visible;
                    break;

                case SectionShapeType.ColdFormedC:
                case SectionShapeType.ColdFormedZ:
                    LblTw.Text = "壁厚 t";
                    PanelTf.Visibility = Visibility.Collapsed;
                    LblLip.Text = "卷边高度 C";
                    PanelLip.Visibility = Visibility.Visible;
                    break;

                case SectionShapeType.HatSection:
                    LblB.Text = "顶宽 B";
                    LblTw.Text = "壁厚 t";
                    PanelTf.Visibility = Visibility.Collapsed;
                    LblLip.Text = "外翻边 C";
                    PanelLip.Visibility = Visibility.Visible;
                    break;

                case SectionShapeType.ZBeam:
                case SectionShapeType.ColdFormedU:
                    LblTw.Text = "壁厚 t";
                    LblTf.Text = "翼缘厚 tf";
                    break;

                case SectionShapeType.SquareTube:
                    LblH.Text = "边长 H";
                    LblTw.Text = "壁厚 t";
                    PanelB.Visibility = Visibility.Collapsed;
                    PanelTf.Visibility = Visibility.Collapsed;
                    break;

                case SectionShapeType.RectTube:
                    LblTw.Text = "壁厚 t";
                    PanelTf.Visibility = Visibility.Collapsed;
                    break;

                case SectionShapeType.PipeTube:
                    LblH.Text = "外径 D";
                    LblTw.Text = "壁厚 t";
                    PanelB.Visibility = Visibility.Collapsed;
                    PanelTf.Visibility = Visibility.Collapsed;
                    break;

                case SectionShapeType.FlatBar:
                    LblH.Text = "板宽 H";
                    LblB.Text = "板厚 t";
                    PanelTw.Visibility = Visibility.Collapsed;
                    PanelTf.Visibility = Visibility.Collapsed;
                    break;

                case SectionShapeType.RoundBar:
                    LblH.Text = "直径 D";
                    PanelB.Visibility = Visibility.Collapsed;
                    PanelTw.Visibility = Visibility.Collapsed;
                    PanelTf.Visibility = Visibility.Collapsed;
                    break;

                case SectionShapeType.SquareBar:
                    LblH.Text = "边长 a";
                    PanelB.Visibility = Visibility.Collapsed;
                    PanelTw.Visibility = Visibility.Collapsed;
                    PanelTf.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void OnParamInputChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSuppressingEvents) return;
            UpdateLivePreview(refreshAutoMetrics: !_isManualMetricsActive);
        }

        private void OnManualMetricChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSuppressingEvents) return;
            bool allEmpty = string.IsNullOrWhiteSpace(TxtArea.Text)
                         && string.IsNullOrWhiteSpace(TxtWeight.Text)
                         && string.IsNullOrWhiteSpace(TxtSurface.Text);
            _isManualMetricsActive = !allEmpty;
            if (TxtMetricSource != null)
            {
                TxtMetricSource.Text = _isManualMetricsActive ? "手册/自定义" : "公式计算";
            }
        }

        private void OnRecalcMetricsClick(object sender, RoutedEventArgs e)
        {
            _isManualMetricsActive = false;
            UpdateLivePreview(refreshAutoMetrics: true);
        }

        private bool TryBuildProfileFromForm(out SectionProfile profile, bool requireName)
        {
            profile = null;
            string name = TxtName.Text?.Trim();
            if (requireName && string.IsNullOrEmpty(name))
                return false;

            SectionShapeType shape = GetSelectedEditShapeType();
            if (!TryParseVal(TxtH.Text, out double h) || h <= 0)
                return false;

            double b = h;
            if (PanelB.Visibility == Visibility.Visible)
            {
                if (!TryParseVal(TxtB.Text, out b) || b <= 0) return false;
            }

            double tw = b;
            if (PanelTw.Visibility == Visibility.Visible)
            {
                if (!TryParseVal(TxtTw.Text, out tw) || tw <= 0) return false;
            }

            double tf = tw;
            if (PanelTf.Visibility == Visibility.Visible)
            {
                if (!TryParseVal(TxtTf.Text, out tf) || tf <= 0) return false;
            }

            double b2 = 0.0, tf2 = 0.0, lip = 0.0, gap = 0.0;
            if (PanelB2.Visibility == Visibility.Visible)
            {
                if (!TryParseVal(TxtB2.Text, out b2) || b2 <= 0) return false;
            }
            if (PanelTf2.Visibility == Visibility.Visible)
            {
                if (!TryParseVal(TxtTf2.Text, out tf2) || tf2 <= 0) return false;
            }
            if (PanelLip.Visibility == Visibility.Visible)
            {
                if (!TryParseVal(TxtLip.Text, out lip) || lip < 0) return false;
            }
            if (PanelGap.Visibility == Visibility.Visible)
            {
                if (!TryParseVal(TxtGap.Text, out gap) || gap < 0) return false;
            }

            profile = new SectionProfile(
                string.IsNullOrEmpty(name) ? "PREVIEW" : name,
                shape, h, b, tw, tf, b2, tf2, lip, gap);

            if (_isManualMetricsActive)
            {
                if (TryParseVal(TxtArea.Text, out double mArea) && mArea > 0)
                    profile.ManualAreaCm2 = mArea;
                if (TryParseVal(TxtWeight.Text, out double mWeight) && mWeight > 0)
                    profile.ManualUnitWeightKgPerM = mWeight;
                if (TryParseVal(TxtSurface.Text, out double mSurface) && mSurface > 0)
                    profile.ManualUnitSurfaceM2PerM = mSurface;
            }

            return true;
        }

        private void UpdateLivePreview(bool refreshAutoMetrics = true)
        {
            if (PreviewControl == null || TxtMetricSource == null) return;
            if (TryBuildProfileFromForm(out SectionProfile sec, requireName: false))
            {
                PreviewControl.Profile = sec;
                if (refreshAutoMetrics)
                {
                    _isSuppressingEvents = true;
                    TxtArea.Text = sec.GetCalculatedAreaCm2().ToString("0.00", CultureInfo.InvariantCulture);
                    TxtWeight.Text = sec.GetCalculatedUnitWeightKgPerM().ToString("0.00", CultureInfo.InvariantCulture);
                    TxtSurface.Text = sec.GetCalculatedUnitSurfaceM2PerM().ToString("0.000", CultureInfo.InvariantCulture);
                    TxtMetricSource.Text = "公式计算";
                    _isSuppressingEvents = false;
                }
                else
                {
                    TxtMetricSource.Text = _isManualMetricsActive ? "手册/自定义" : "公式计算";
                }
            }
        }

        private static bool TryParseVal(string text, out double val)
        {
            return double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out val);
        }

        private void OnAddSectionClick(object sender, RoutedEventArgs e)
        {
            string name = TxtName.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("请输入截面代号！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtName.Focus();
                return;
            }

            if (!TryBuildProfileFromForm(out SectionProfile profile, requireName: true))
            {
                MessageBox.Show("截面尺寸参数必须为有效数值！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SectionCatalogService.AddOrUpdate(profile);
            string targetNodeKey = $"SHAPE_{(int)profile.Shape}";
            RebuildCategoryTree(targetNodeKey);
            RefreshTableAndSummary();

            // 选中刚新增或更新的行
            if (GridSections.ItemsSource is IEnumerable<SectionProfile> list)
            {
                var added = list.FirstOrDefault(x => string.Equals(x.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
                if (added != null)
                {
                    GridSections.SelectedItem = added;
                    GridSections.ScrollIntoView(added);
                }
            }
        }

        private void OnDeleteSectionClick(object sender, RoutedEventArgs e)
        {
            if (GridSections.SelectedItem is SectionProfile sel)
            {
                if (!SectionCatalogService.Remove(sel.Name))
                {
                    MessageBox.Show("该截面类型至少需保留 1 条默认规格，无法删除最后一条。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                RebuildCategoryTree(_currentFilter?.Key ?? "ALL");
                RefreshTableAndSummary();
            }
            else
            {
                MessageBox.Show("请先在表格中选中要删除的规格行。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnExportXlsxClick(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog
            {
                Title = "导出型钢截面库 (.xlsx)",
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                FileName = $"钢结构型钢截面库_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    SectionCatalogService.ExportCatalogToXlsx(sfd.FileName);
                    MessageBox.Show($"已按 5 大类工作表导出 Excel 至：\n{sfd.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OnExportCsvClick(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog
            {
                Title = "导出型钢截面库 (.csv)",
                Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = $"钢结构型钢截面库_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    SectionCatalogService.ExportCatalogToCsv(sfd.FileName);
                    MessageBox.Show($"已导出 CSV 至：\n{sfd.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OnImportClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "导入型钢截面库 (.xlsx / .csv)",
                Filter = "Excel 或 CSV (*.xlsx;*.csv)|*.xlsx;*.csv"
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    int count = SectionCatalogService.ImportCatalogFromFile(ofd.FileName, replaceExisting: false);
                    RebuildCategoryTree(_currentFilter?.Key ?? "ALL");
                    RefreshTableAndSummary();
                    MessageBox.Show($"已成功导入/更新 {count} 条截面规格！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导入失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OnResetDefaultClick(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定恢复默认国标与工程截面库吗？当前自定义修改将被覆盖。", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                SectionCatalogService.ResetToDefaults();
                RebuildCategoryTree(_currentFilter?.Key ?? "ALL");
                RefreshTableAndSummary();
            }
        }

        private void OnOpenStorageFolderClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string filePath = SectionCatalogService.CatalogFilePath;
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                if (File.Exists(filePath))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
                }
                else if (!string.IsNullOrEmpty(dir))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开目录：{ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnSaveCloseClick(object sender, RoutedEventArgs e)
        {
            SectionCatalogService.SaveCatalog();
            DialogResult = true;
            Close();
        }
    }
}
