using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Catalog;
using SteelStructureSuite.Services.Settings;

namespace SteelStructureSuite.Views
{
    public partial class SectionCatalogWindow : Window
    {
        private readonly Dictionary<SectionShapeType, DataGrid> _gridsByType = new Dictionary<SectionShapeType, DataGrid>();

        private static readonly SectionShapeType[] OrderedTypes =
        {
            SectionShapeType.HBeam,
            SectionShapeType.Channel,
            SectionShapeType.Angle,
            SectionShapeType.RectTube,
            SectionShapeType.PipeTube
        };

        public SectionCatalogWindow()
        {
            InitializeComponent();
            Width = Math.Max(Width, 960);
            Height = Math.Max(Height, 640);
            MinWidth = 840;
            MinHeight = 420;

            CmbType.Items.Add("H型钢");
            CmbType.Items.Add("槽钢");
            CmbType.Items.Add("等边角钢");
            CmbType.Items.Add("方矩管");
            CmbType.Items.Add("圆钢管");
            CmbType.SelectedIndex = 0;

            foreach (SectionShapeType shapeType in OrderedTypes)
            {
                DataGrid dg = CreateSectionDataGrid(shapeType);
                _gridsByType[shapeType] = dg;

                TabItem item = new TabItem
                {
                    Tag = shapeType,
                    Header = SectionCatalogService.GetShapeTabTitle(shapeType),
                    Content = dg
                };
                TabsSections.Items.Add(item);
            }

            if (TabsSections.Items.Count > 0)
            {
                TabsSections.SelectedIndex = 0;
            }
            RefreshAllTabGrids();

            // 恢复上次停留的截面类型页签
            int lastTab = UserSettings.GetInt("Catalog.LastTabIndex", 0);
            if (lastTab >= 0 && lastTab < TabsSections.Items.Count)
            {
                TabsSections.SelectedIndex = lastTab;
            }

            UserSettings.RestoreWindowState(this, "SectionCatalogWindow");
            Closed += (s, e) =>
            {
                UserSettings.SetInt("Catalog.LastTabIndex", TabsSections.SelectedIndex);
                UserSettings.Save();
                UserSettings.SaveWindowState(this, "SectionCatalogWindow");
            };
        }

        private static DataGrid CreateSectionDataGrid(SectionShapeType shapeType)
        {
            DataGrid dg = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                IsReadOnly = false,
                SelectionMode = DataGridSelectionMode.Single,
                RowHeaderWidth = 0,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(4)
            };

            dg.Columns.Add(new DataGridTextColumn { Header = "截面代号", Binding = new Binding("Name"), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star), MinWidth = 130 });
            dg.Columns.Add(new DataGridTextColumn { Header = "类别", Binding = new Binding("ShapeTypeDisplayName"), IsReadOnly = true, Width = new DataGridLength(0.9, DataGridLengthUnitType.Star), MinWidth = 80 });

            string hHeader = shapeType == SectionShapeType.PipeTube ? "外径 D (mm)" : "高度 H (mm)";
            string bHeader = shapeType == SectionShapeType.PipeTube ? "外径 D (mm)" : "宽度 B (mm)";
            dg.Columns.Add(new DataGridTextColumn { Header = hHeader, Binding = new Binding("H"), Width = new DataGridLength(0.95, DataGridLengthUnitType.Star), MinWidth = 90 });
            dg.Columns.Add(new DataGridTextColumn { Header = bHeader, Binding = new Binding("B"), Width = new DataGridLength(0.95, DataGridLengthUnitType.Star), MinWidth = 90 });
            dg.Columns.Add(new DataGridTextColumn { Header = "壁厚 tw (mm)", Binding = new Binding("Tw"), Width = new DataGridLength(0.95, DataGridLengthUnitType.Star), MinWidth = 90 });
            dg.Columns.Add(new DataGridTextColumn { Header = "翼缘厚 tf (mm)", Binding = new Binding("Tf"), Width = new DataGridLength(0.95, DataGridLengthUnitType.Star), MinWidth = 95 });
            dg.Columns.Add(new DataGridTextColumn { Header = "截面积 (cm²)", Binding = new Binding("AreaCm2") { StringFormat = "0.00" }, IsReadOnly = true, Width = new DataGridLength(0.95, DataGridLengthUnitType.Star), MinWidth = 95 });
            dg.Columns.Add(new DataGridTextColumn { Header = "单重 (kg/m)", Binding = new Binding("UnitWeightKgPerM") { StringFormat = "0.00" }, IsReadOnly = true, Width = new DataGridLength(1.0, DataGridLengthUnitType.Star), MinWidth = 95 });
            dg.Columns.Add(new DataGridTextColumn { Header = "表面积 (m²/m)", Binding = new Binding("PerimeterMPerM") { StringFormat = "0.000" }, IsReadOnly = true, Width = new DataGridLength(1.0, DataGridLengthUnitType.Star), MinWidth = 100 });

            return dg;
        }

        private void OnSectionSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TabsSections.SelectedItem is TabItem item &&
                item.Tag is SectionShapeType st &&
                _gridsByType.ContainsKey(st))
            {
                int idx = Array.IndexOf(OrderedTypes, st);
                if (idx >= 0 && CmbType != null)
                {
                    CmbType.SelectedIndex = idx;
                }
            }
        }

        private void RefreshAllTabGrids()
        {
            int totalCount = 0;
            for (int i = 0; i < OrderedTypes.Length; i++)
            {
                SectionShapeType st = OrderedTypes[i];
                var list = SectionCatalogService.GetByShapeType(st);
                totalCount += list.Count;

                if (_gridsByType.TryGetValue(st, out DataGrid dg))
                {
                    dg.ItemsSource = null;
                    dg.ItemsSource = list;
                }

                if (i < TabsSections.Items.Count && TabsSections.Items[i] is TabItem item)
                {
                    item.Header = $"{SectionCatalogService.GetShapeTabTitle(st)} ({list.Count})";
                }
            }

            TxtSummary.Text = $"共 5 个分表，合计 {totalCount} 种型钢规格";
        }

        private void OnAddSectionClick(object sender, RoutedEventArgs e)
        {
            string name = TxtName.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("请输入截面代号！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(TxtH.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double h) ||
                !double.TryParse(TxtB.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double b) ||
                !double.TryParse(TxtTw.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double tw) ||
                !double.TryParse(TxtTf.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double tf))
            {
                MessageBox.Show("截面尺寸必须为有效数值！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SectionShapeType shapeType = OrderedTypes[Math.Max(0, Math.Min(CmbType.SelectedIndex, OrderedTypes.Length - 1))];
            SectionCatalogService.AddOrUpdate(new SectionProfile(name, shapeType, h, b, tw, tf));

            TabsSections.SelectedIndex = Array.IndexOf(OrderedTypes, shapeType);
            RefreshAllTabGrids();
        }

        private void OnDeleteSectionClick(object sender, RoutedEventArgs e)
        {
            if (TabsSections.SelectedItem is TabItem item &&
                item.Tag is SectionShapeType st &&
                _gridsByType.TryGetValue(st, out DataGrid dg) &&
                dg.SelectedItem is SectionProfile sel)
            {
                SectionCatalogService.Remove(sel.Name);
                RefreshAllTabGrids();
            }
            else
            {
                MessageBox.Show("请先选中要删除的规格行。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnExportXlsxClick(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog
            {
                Title = "导出 5 分表型钢截面库 (.xlsx)",
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                FileName = $"国标型钢截面分表库_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    SectionCatalogService.ExportCatalogToXlsx(sfd.FileName);
                    MessageBox.Show($"已导出 5 个分表工作表至：\n{sfd.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
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
                FileName = $"国标型钢截面分表库_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    SectionCatalogService.ExportCatalogToCsv(sfd.FileName);
                    MessageBox.Show($"已导出 CSV：\n{sfd.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    RefreshAllTabGrids();
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
            if (MessageBox.Show("确定恢复默认国标截面库吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                SectionCatalogService.ResetToDefaults();
                RefreshAllTabGrids();
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

