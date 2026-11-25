using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Settings;
using SteelStructureSuite.Services.Templates;

namespace SteelStructureSuite.Views
{
    /// <summary>
    /// 参数模板管理窗口。
    ///
    /// 交互原则：不弹输入框。名称与备注在表格里直接双击改，
    /// 删除走「状态栏提示 + 再次点击确认」，导入导出结果只写到状态栏。
    /// </summary>
    public partial class TemplateManagerWindow : Window
    {
        /// <summary>表格行（可编辑，改完写回存储）。</summary>
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

            public string TypeDisplay => Template.IsGeneric
                ? "通用"
                : (Template.StructureType.HasValue
                    ? new SteelStructureParams { StructureType = Template.StructureType.Value }.StructureTypeDisplayName
                    : "-");

            public string SpanText => Template.Parameters?.Span.ToString("0.#") ?? "-";
            public string HeightText => Template.Parameters?.Height.ToString("0.#") ?? "-";
            public string ColumnSection => Template.Parameters?.ColumnSection ?? "-";
        }

        private List<TemplateRow> _allRows = new List<TemplateRow>();
        private string _pendingDeleteId;

        public TemplateManagerWindow()
        {
            InitializeComponent();

            UserSettings.RestoreWindowState(this, "TemplateManagerWindow");
            Closed += (s, e) => UserSettings.SaveWindowState(this, "TemplateManagerWindow");

            ReloadList();
            SetStatus($"共 {_allRows.Count} 个模板");
        }

        private void SetStatus(string text)
        {
            TxtStatus.Text = text ?? string.Empty;
        }

        private void ReloadList()
        {
            _pendingDeleteId = null;
            _allRows = StructureTemplateStore.GetAll()
                .Select(t => new TemplateRow { Template = t })
                .ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string key = (TxtFilter?.Text ?? string.Empty).Trim();
            IEnumerable<TemplateRow> rows = _allRows;

            if (key.Length > 0)
            {
                rows = rows.Where(r =>
                    Contains(r.Name, key) ||
                    Contains(r.Note, key) ||
                    Contains(r.TypeDisplay, key) ||
                    Contains(r.ColumnSection, key));
            }

            var list = rows.ToList();
            DgTemplates.ItemsSource = list;
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

            // 选中另一行时清除待删除标记
            if (_pendingDeleteId != null && _pendingDeleteId != row.Template.Id)
                _pendingDeleteId = null;

            SteelStructureParams p = row.Template.Parameters;
            if (p == null) return;

            SetStatus($"{row.TypeDisplay}｜跨度 {p.Span:0.#}　高 {p.Height:0.#}　" +
                      $"柱 {p.ColumnSection}　梁 {p.BeamSection}　{p.Material}");
        }

        /// <summary>表格内编辑结束：写回存储，不弹任何提示。</summary>
        private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (!(e.Row.Item is TemplateRow row)) return;

            // 等绑定把值写回行对象
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string name = (row.Name ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    // 名称不能为空：还原并提示
                    ReloadList();
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
                    ReloadList();
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

            // 二次确认不弹窗：第一次点标记，第二次才真删
            if (_pendingDeleteId != row.Template.Id)
            {
                _pendingDeleteId = row.Template.Id;
                SetStatus($"再点一次「删除」确认删除「{row.Name}」");
                return;
            }

            StructureTemplateStore.Delete(row.Template.Id);
            SetStatus($"已删除「{row.Name}」");
            ReloadList();
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
                int added = StructureTemplateStore.ImportFrom(dlg.FileName);
                ReloadList();
                SetStatus($"导入完成：新增 {added} 个（重复已跳过）");
            }
            catch (Exception ex)
            {
                SetStatus("导入失败：" + ex.Message);
            }
        }

        private void OnExportClick(object sender, RoutedEventArgs e)
        {
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
                SetStatus("已导出到 " + Path.GetFileName(dlg.FileName));
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
