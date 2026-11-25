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
using SteelStructureSuite.Views.Controls;

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
        private System.Windows.Forms.PropertyGrid _propertyGrid;
        private readonly SectionParameterModel _paramModel = new SectionParameterModel();

        public SectionCatalogWindow()
        {
            InitializeComponent();

            InitPropertyGrid();
            RebuildCategoryTree(UserSettings.GetString("Catalog.LastTreeNode", "ALL"));
            RefreshTableAndSummary();
            UpdateLivePreview();
            // 不显示具体路径，避免泄露本机目录；需要时点「打开目录」即可
            TxtStorageInfo.Text = "本地截面库（UTF-8 CSV，可用 Excel 编辑）";

            UserSettings.RestoreWindowState(this, "SectionCatalogWindow");
            Closed += (s, e) =>
            {
                UserSettings.SetString("Catalog.LastTreeNode", _currentFilter?.Key ?? "ALL");
                UserSettings.Save();
                UserSettings.SaveWindowState(this, "SectionCatalogWindow");
            };
        }

        /// <summary>
        /// 初始化 WinForms PropertyGrid：无工具栏、按类别分组、按字母序展示。
        /// 字段集合与中文名由 <see cref="SectionParameterModel"/> 按截面类型动态决定。
        /// </summary>
        /// <summary>在底部状态栏就地显示提示，替代一次性成功/失败弹窗。</summary>
        private void SetStatus(string text)
        {
            if (TxtStorageInfo != null) TxtStorageInfo.Text = text ?? string.Empty;
        }

        private void InitPropertyGrid()
        {
            _propertyGrid = new System.Windows.Forms.PropertyGrid
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                ToolbarVisible = false,
                HelpVisible = true,
                PropertySort = System.Windows.Forms.PropertySort.Categorized,
                LineColor = System.Drawing.SystemColors.ControlDark,
                CategoryForeColor = System.Drawing.SystemColors.ControlText,
                BackColor = System.Drawing.SystemColors.Window,
                ViewBackColor = System.Drawing.SystemColors.Window,
                ViewForeColor = System.Drawing.SystemColors.WindowText
            };
            _propertyGrid.PropertyValueChanged += OnPropertyValueChanged;
            _paramModel.Changed += (s, e) => OnPropertyValueChanged(_propertyGrid, null);
            _propertyGrid.SelectedObject = _paramModel;

            PropertyHost.Child = _propertyGrid;
        }

        /// <summary>PropertyGrid 值变化：几何尺寸联动刷新物理特性与 2D 预览；截面类型变化则重建字段集合。</summary>
        private void OnPropertyValueChanged(object sender, System.Windows.Forms.PropertyValueChangedEventArgs e)
        {
            if (_isSuppressingEvents) return;

            string changedName = e?.ChangedItem?.PropertyDescriptor?.Name;
            bool shapeChanged = string.Equals(changedName, "Shape", StringComparison.Ordinal);
            bool metricChanged = string.Equals(changedName, "AreaCm2", StringComparison.Ordinal)
                              || string.Equals(changedName, "UnitWeightKgPerM", StringComparison.Ordinal)
                              || string.Equals(changedName, "UnitSurfaceM2PerM", StringComparison.Ordinal);

            if (shapeChanged)
            {
                _paramModel.HasManualMetricsReset();
                _propertyGrid.Refresh();   // 按新类型重建可见字段与标签
            }

            if (!metricChanged)
            {
                _paramModel.RefreshAutoMetricsIfNeeded();
            }

            UpdateMetricSourceText();
            UpdateLivePreview(refreshAutoMetrics: false);
            _propertyGrid.Refresh();
        }

        private void UpdateMetricSourceText()
        {
            if (TxtMetricSource != null)
            {
                TxtMetricSource.Text = _paramModel.HasManualMetrics ? "手册/自定义" : "公式计算";
            }
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

                // 选中具体截面类型节点时，联动把该类型的第一条规格载入参数面板
                if (tag.Kind == TreeFilterKind.ShapeType)
                {
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

        /// <summary>把选中的截面规格载入 PropertyGrid 并刷新预览。</summary>
        private void FillFormFromProfile(SectionProfile sec)
        {
            if (sec == null) return;

            _isSuppressingEvents = true;
            _paramModel.LoadFrom(sec);
            _propertyGrid.SelectedObject = _paramModel;
            _propertyGrid.Refresh();
            _isSuppressingEvents = false;

            UpdateMetricSourceText();
            UpdateLivePreview(refreshAutoMetrics: false);
        }

        private void OnRecalcMetricsClick(object sender, RoutedEventArgs e)
        {
            _isSuppressingEvents = true;
            _paramModel.RecalculateMetrics();
            _isSuppressingEvents = false;

            UpdateMetricSourceText();
            UpdateLivePreview(refreshAutoMetrics: false);
            _propertyGrid.Refresh();
        }

        private bool TryBuildProfileFromForm(out SectionProfile profile, bool requireName)
        {
            profile = null;
            if (requireName && string.IsNullOrWhiteSpace(_paramModel.Name))
                return false;

            SectionProfile built = _paramModel.ToProfile();
            if (built.H <= 0) return false;
            if (_paramModel.ShowB && built.B <= 0) return false;
            if (_paramModel.ShowTw && built.Tw <= 0) return false;
            if (_paramModel.ShowTf && built.Tf <= 0) return false;
            if (_paramModel.ShowB2 && built.B2 <= 0) return false;
            if (_paramModel.ShowTf2 && built.Tf2 <= 0) return false;

            profile = built;
            return true;
        }

        private void UpdateLivePreview(bool refreshAutoMetrics = true)
        {
            if (PreviewControl == null) return;

            if (refreshAutoMetrics)
            {
                _isSuppressingEvents = true;
                _paramModel.RefreshAutoMetricsIfNeeded();
                _isSuppressingEvents = false;
                UpdateMetricSourceText();
                _propertyGrid?.Refresh();
            }

            if (TryBuildProfileFromForm(out SectionProfile sec, requireName: false))
            {
                PreviewControl.Profile = sec;
            }
        }

        private void OnAddSectionClick(object sender, RoutedEventArgs e)
        {
            string name = _paramModel.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("请输入截面代号！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                _propertyGrid?.Focus();
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

        /// <summary>统一导出入口：按扩展名自动选择 Excel 或 CSV，不再放两个按钮。</summary>
        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出截面库",
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx|CSV 文件 (*.csv)|*.csv",
                DefaultExt = ".xlsx",
                FileName = $"截面库_{DateTime.Now:yyyyMMdd}"
            };
            if (dlg.ShowDialog(this) != true) return;

            bool csv = dlg.FilterIndex == 2 ||
                       dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

            if (csv) ExportCsvCore(dlg.FileName);
            else ExportXlsxCore(dlg.FileName);
        }

        private void ExportXlsxCore(string path)
        {
            try
            {
                SectionCatalogService.ExportCatalogToXlsx(path);
                SetStatus("已导出 Excel：" + System.IO.Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                SetStatus("导出失败：" + ex.Message);
            }
        }

        private void ExportCsvCore(string path)
        {
            try
            {
                SectionCatalogService.ExportCatalogToCsv(path);
                SetStatus("已导出 CSV：" + System.IO.Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                SetStatus("导出失败：" + ex.Message);
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
                    SetStatus($"已导入/更新 {count} 条截面规格");
                }
                catch (Exception ex)
                {
                    SetStatus("导入失败：" + ex.Message);
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
                SetStatus("无法打开目录：" + ex.Message);
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
