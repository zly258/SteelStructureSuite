using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// 参数模板管理窗口：查看、重命名、删除、导入导出参数模板。
    /// 新建模板在建模窗口里通过「保存为模板」完成。
    /// </summary>
    public partial class TemplateManagerWindow : Window
    {
        /// <summary>供 DataGrid 显示的模板行。</summary>
        private sealed class TemplateRow
        {
            public StructureTemplate Template { get; set; }
            public string Name => Template.Name;
            public string TypeDisplay => Template.IsGeneric
                ? "（通用）"
                : (Template.StructureType.HasValue
                    ? new SteelStructureParams { StructureType = Template.StructureType.Value }.StructureTypeDisplayName
                    : "-");
            public string SpanText => Template.Parameters == null ? "-" : Template.Parameters.Span.ToString("0.#");
            public string HeightText => Template.Parameters == null ? "-" : Template.Parameters.Height.ToString("0.#");
            public string ColumnSection => Template.Parameters?.ColumnSection ?? "-";
            public string Note => Template.Note ?? string.Empty;
        }

        private List<TemplateRow> _allRows = new List<TemplateRow>();
        private bool _dirty;

        public TemplateManagerWindow()
        {
            InitializeComponent();

            TxtStorage.Text = $"模板库文件（JSON，可用文本编辑器查看/备份）：{StructureTemplateStore.StoreFilePath}";

            UserSettings.RestoreWindowState(this, "TemplateManagerWindow");
            Closed += (s, e) => UserSettings.SaveWindowState(this, "TemplateManagerWindow");

            ReloadList();
        }

        private void ReloadList()
        {
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
            else
                UpdateDetail();
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
            UpdateDetail();
        }

        private void UpdateDetail()
        {
            if (!(DgTemplates.SelectedItem is TemplateRow row))
            {
                TxtName.Text = string.Empty;
                TxtNote.Text = string.Empty;
                TxtDetail.Text = string.Empty;
                return;
            }

            TxtName.Text = row.Template.Name;
            TxtNote.Text = row.Template.Note;

            SteelStructureParams p = row.Template.Parameters;
            if (p == null)
            {
                TxtDetail.Text = "(无参数)";
                return;
            }

            var lines = new List<string>
            {
                $"构件类型   : {row.TypeDisplay}",
                $"跨度 Span  : {p.Span:0.##}",
                $"长度 Length: {p.Length:0.##}",
                $"高度 Height: {p.Height:0.##}",
                $"次高 SubH  : {p.SubHeight:0.##}",
                $"跨数 Bay   : {p.BayCount}",
                $"立柱截面   : {p.ColumnSection}",
                $"主梁截面   : {p.BeamSection}",
                $"斜撑截面   : {p.BraceSection}",
                $"材质       : {p.Material}",
                $"终点高差   : {p.EndPointOffsetZ:0.##}",
                $"顶部半径   : {p.TopRadius:0.##}",
                "",
                $"创建时间   : {row.Template.CreatedAt:yyyy-MM-dd HH:mm}",
                $"修改时间   : {row.Template.UpdatedAt:yyyy-MM-dd HH:mm}"
            };
            TxtDetail.Text = string.Join(Environment.NewLine, lines);
        }

        private void OnRenameClick(object sender, RoutedEventArgs e)
        {
            if (!(DgTemplates.SelectedItem is TemplateRow row))
            {
                MessageBox.Show(this, "请先选中一个模板。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string name = (TxtName.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "模板名称不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!StructureTemplateStore.Rename(row.Template.Id, name))
            {
                MessageBox.Show(this, "重命名失败：已存在同类型同名模板，或名称非法。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _dirty = true;
            ReloadList();
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (!(DgTemplates.SelectedItem is TemplateRow row))
            {
                MessageBox.Show(this, "请先选中一个模板。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBoxResult confirm = MessageBox.Show(
                this,
                $"确定删除模板「{row.Name}」吗？此操作不可撤销。",
                "删除确认",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;

            StructureTemplateStore.Delete(row.Template.Id);
            _dirty = true;
            ReloadList();
        }

        private void OnImportClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "导入参数模板",
                Filter = "模板文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                DefaultExt = ".json"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                int added = StructureTemplateStore.ImportFrom(dlg.FileName);
                _dirty = true;
                ReloadList();
                MessageBox.Show(this, $"导入完成：新增 {added} 个模板（同名同类型已自动跳过）。", "导入结果",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导入失败：" + ex.Message, "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(this, "导出完成：\n" + dlg.FileName, "导出结果",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnOpenFolderClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string file = StructureTemplateStore.StoreFilePath;
                string dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                if (File.Exists(file))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
                else if (!string.IsNullOrEmpty(dir))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开目录：" + ex.Message, "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnSaveCloseClick(object sender, RoutedEventArgs e)
        {
            if (_dirty)
                UserSettings.Save();

            DialogResult = true;
            Close();
        }
    }
}
