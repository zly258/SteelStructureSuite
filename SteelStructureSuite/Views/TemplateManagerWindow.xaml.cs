using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Settings;
using SteelStructureSuite.Services.Templates;

namespace SteelStructureSuite.Views
{
    /// <summary>
    /// 参数模板管理窗口（左侧构件分类树 + 右侧模板列表与模糊检索）。
    /// 名称与备注可在表格内直接双击编辑，删除走 MessageBox 确认，支持 JSON 导入导出。
    /// </summary>
    public partial class TemplateManagerWindow : Window
    {
        private enum TreeFilterKind
        {
            All,
            Generic,
            Category,
            StructureType
        }

        private sealed class TreeNodeTag
        {
            public TreeFilterKind Kind { get; set; }
            public string CategoryName { get; set; }
            public ParametricStructureType StructureType { get; set; }
            public string DisplayTitle { get; set; }
            public string Key { get; set; }
        }

        private sealed class TemplateRow
        {
            public StructureTemplate Template { get; set; }

            public string Name
            {
                get => Template.Name;
                set
                {
                    if (string.Equals(Template.Name, value, StringComparison.Ordinal)) return;
                    Template.Name = (value ?? string.Empty).Trim();
                }
            }

            public string Note
            {
                get => Template.Note;
                set
                {
                    if (string.Equals(Template.Note, value, StringComparison.Ordinal)) return;
                    Template.Note = (value ?? string.Empty).Trim();
                }
            }

            public string CategoryName => Template.IsGeneric
                ? "通用"
                : (Template.StructureType.HasValue
                    ? new SteelStructureParams { StructureType = Template.StructureType.Value }.CategoryName
                    : "其他结构");

            public string TypeDisplay => Template.IsGeneric
                ? "通用"
                : (Template.StructureType.HasValue
                    ? new SteelStructureParams { StructureType = Template.StructureType.Value }.StructureTypeDisplayName
                    : "-");

            public string SpanText => Template.Parameters?.Span.ToString("0.#") ?? "-";
            public string HeightText => Template.Parameters?.Height.ToString("0.#") ?? "-";
            public string ColumnSection => Template.Parameters?.ColumnSection ?? "-";
            public string Material => Template.Parameters?.Material ?? string.Empty;
        }

        private static readonly string[] OrderedCategoryNames =
        {
            "单根型钢构件",
            "厂房与装置框架",
            "平台梯道与栏杆",
            "化工设备标准件",
            "电力与冶金标准件",
            "区域组合构件"
        };

        private List<TemplateRow> _allRows = new List<TemplateRow>();
        private TreeNodeTag _currentFilter = new TreeNodeTag { Kind = TreeFilterKind.All, DisplayTitle = "全部模板", Key = "ALL" };
        private bool _suppressTreeEvent;

        public TemplateManagerWindow()
        {
            InitializeComponent();

            UserSettings.RestoreWindowState(this, "TemplateManagerWindow");
            Closed += (s, e) =>
            {
                UserSettings.SetString("TemplateMgr.LastTreeNode", _currentFilter?.Key ?? "ALL");
                UserSettings.SaveWindowState(this, "TemplateManagerWindow");
            };

            string lastKey = UserSettings.GetString("TemplateMgr.LastTreeNode", "ALL");
            ReloadAll(lastKey);
            SetStatus($"共 {_allRows.Count} 个自定义模板");
        }

        private void SetStatus(string text)
        {
            TxtStatus.Text = text ?? string.Empty;
        }

        private void ReloadAll(string selectKey = null)
        {
            _allRows = StructureTemplateStore.GetAll()
                .Select(t => new TemplateRow { Template = t })
                .ToList();
            RebuildCategoryTree(selectKey ?? _currentFilter?.Key ?? "ALL");
            ApplyFilter();
        }

        private void RebuildCategoryTree(string selectKey)
        {
            _suppressTreeEvent = true;
            TreeCategories.Items.Clear();

            var rootItem = new TreeViewItem
            {
                Header = $"全部模板 ({_allRows.Count})",
                IsExpanded = true,
                Tag = new TreeNodeTag { Kind = TreeFilterKind.All, DisplayTitle = "全部模板", Key = "ALL" }
            };
            TreeViewItem itemToSelect = rootItem;

            int genericCount = _allRows.Count(r => r.Template.IsGeneric);
            var genericItem = new TreeViewItem
            {
                Header = $"通用模板 ({genericCount})",
                Tag = new TreeNodeTag { Kind = TreeFilterKind.Generic, DisplayTitle = "通用模板", Key = "GENERIC" }
            };
            if (string.Equals(selectKey, "GENERIC", StringComparison.OrdinalIgnoreCase))
                itemToSelect = genericItem;
            rootItem.Items.Add(genericItem);

            var allTypes = Enum.GetValues(typeof(ParametricStructureType))
                .Cast<ParametricStructureType>()
                .Select(t => new SteelStructureParams { StructureType = t })
                .ToList();

            foreach (string catName in OrderedCategoryNames)
            {
                int catCount = _allRows.Count(r => !r.Template.IsGeneric && r.CategoryName == catName);
                string catKey = "CAT_" + catName;
                var catItem = new TreeViewItem
                {
                    Header = $"{catName} ({catCount})",
                    IsExpanded = catCount > 0,
                    Tag = new TreeNodeTag
                    {
                        Kind = TreeFilterKind.Category,
                        CategoryName = catName,
                        DisplayTitle = catName,
                        Key = catKey
                    }
                };
                if (string.Equals(selectKey, catKey, StringComparison.OrdinalIgnoreCase))
                    itemToSelect = catItem;

                foreach (var probe in allTypes.Where(x => x.CategoryName == catName))
                {
                    ParametricStructureType st = probe.StructureType;
                    int typeCount = _allRows.Count(r => r.Template.StructureType == st);
                    string typeKey = $"TYPE_{(int)st}";
                    var typeItem = new TreeViewItem
                    {
                        Header = $"{probe.StructureTypeDisplayName} ({typeCount})",
                        Tag = new TreeNodeTag
                        {
                            Kind = TreeFilterKind.StructureType,
                            CategoryName = catName,
                            StructureType = st,
                            DisplayTitle = probe.StructureTypeDisplayName,
                            Key = typeKey
                        }
                    };
                    if (string.Equals(selectKey, typeKey, StringComparison.OrdinalIgnoreCase))
                    {
                        catItem.IsExpanded = true;
                        itemToSelect = typeItem;
                    }
                    catItem.Items.Add(typeItem);
                }

                rootItem.Items.Add(catItem);
            }

            TreeCategories.Items.Add(rootItem);
            if (itemToSelect != null)
            {
                itemToSelect.IsSelected = true;
                _currentFilter = itemToSelect.Tag as TreeNodeTag ?? _currentFilter;
            }
            _suppressTreeEvent = false;
        }

        private void OnTreeCategorySelected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_suppressTreeEvent) return;
            if (e.NewValue is TreeViewItem item && item.Tag is TreeNodeTag tag)
            {
                _currentFilter = tag;
                ApplyFilter();
            }
        }

        private void ApplyFilter()
        {
            IEnumerable<TemplateRow> rows = _allRows;

            switch (_currentFilter?.Kind ?? TreeFilterKind.All)
            {
                case TreeFilterKind.Generic:
                    rows = rows.Where(r => r.Template.IsGeneric);
                    break;
                case TreeFilterKind.Category:
                    rows = rows.Where(r => !r.Template.IsGeneric && r.CategoryName == _currentFilter.CategoryName);
                    break;
                case TreeFilterKind.StructureType:
                    rows = rows.Where(r => r.Template.StructureType == _currentFilter.StructureType);
                    break;
            }

            string key = (TxtFilter?.Text ?? string.Empty).Trim();
            if (key.Length > 0)
            {
                rows = rows.Where(r =>
                    Contains(r.Name, key) ||
                    Contains(r.Note, key) ||
                    Contains(r.TypeDisplay, key) ||
                    Contains(r.CategoryName, key) ||
                    Contains(r.ColumnSection, key) ||
                    Contains(r.Material, key));
            }

            var list = rows.ToList();
            DgTemplates.ItemsSource = list;
            TxtCurrentCategory.Text = _currentFilter?.DisplayTitle ?? "全部模板";
            TxtCount.Text = $"{list.Count} 个模板";

            if (list.Count > 0 && DgTemplates.SelectedItem == null)
                DgTemplates.SelectedIndex = 0;
        }

        private static bool Contains(string source, string key)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(key, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void OnFilterChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;
            ApplyFilter();
        }

        private void OnClearFilterClick(object sender, RoutedEventArgs e)
        {
            TxtFilter.Text = string.Empty;
            TxtFilter.Focus();
        }

        private void OnTemplateSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(DgTemplates.SelectedItem is TemplateRow row)) return;

            SteelStructureParams p = row.Template.Parameters;
            if (p == null) return;

            SetStatus($"{row.TypeDisplay}｜跨度 {p.Span:0.#}　高 {p.Height:0.#}　" +
                      $"柱 {p.ColumnSection}　梁 {p.BeamSection}　{p.Material}");
        }

        /// <summary>表格内编辑结束：写回存储。</summary>
        private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (!(e.Row.Item is TemplateRow row)) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                string name = (row.Name ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    ReloadAll();
                    SetStatus("名称不能为空，已还原");
                    return;
                }

                try
                {
                    StructureTemplateStore.Save(row.Template);
                    SetStatus($"已保存「{row.Name}」");
                }
                catch (Exception ex)
                {
                    ReloadAll();
                    SetStatus("保存失败：" + ex.Message);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (!(DgTemplates.SelectedItem is TemplateRow row))
            {
                SetStatus("请先选中一个模板");
                return;
            }

            if (MessageBox.Show(
                    this,
                    $"确定删除模板「{row.Name}」吗？删除后无法恢复。",
                    "确认删除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            StructureTemplateStore.Delete(row.Template.Id);
            SetStatus($"已删除「{row.Name}」");
            ReloadAll();
        }

        private void OnImportClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "导入参数模板",
                Filter = "模板文件 (*.json)|*.json",
                DefaultExt = ".json"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                int added = StructureTemplateStore.ImportFrom(dlg.FileName, out int skipped);
                ReloadAll();
                SetStatus(skipped > 0
                    ? $"导入完成：新增 {added} 个，跳过重复 {skipped} 个"
                    : $"导入完成：新增 {added} 个模板");
            }
            catch (Exception ex)
            {
                SetStatus("导入失败：" + ex.Message);
            }
        }

        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            if (_allRows.Count == 0)
            {
                SetStatus("当前没有可导出的自定义模板，请先在建模窗口点击「存为模板」");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "导出参数模板",
                Filter = "模板文件 (*.json)|*.json",
                DefaultExt = ".json",
                FileName = $"钢结构参数模板_{DateTime.Now:yyyyMMdd}.json"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                StructureTemplateStore.ExportTo(dlg.FileName);
                SetStatus($"已导出 {_allRows.Count} 个模板到 " + Path.GetFileName(dlg.FileName));
            }
            catch (Exception ex)
            {
                SetStatus("导出失败：" + ex.Message);
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            UserSettings.Save();
            DialogResult = true;
            Close();
        }
    }
}
