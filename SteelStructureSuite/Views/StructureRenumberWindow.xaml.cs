using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Modeling;
using SteelStructureSuite.Services.Settings;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SteelStructureSuite.Views
{
    /// <summary>
    /// 构件位号顺号重排窗口：支持按类型自定义前缀、可配编号位数（默认 3 位如 001）、排序规则与顺号前后对比预览。
    /// </summary>
    public partial class StructureRenumberWindow : Window
    {
        public sealed class TypePrefixRow : INotifyPropertyChanged
        {
            private string _prefix;

            public ParametricStructureType StructureType { get; set; }
            public int CategoryOrder { get; set; }
            public string TypeName { get; set; }
            public int Count { get; set; }

            public string Prefix
            {
                get => _prefix;
                set
                {
                    string v = (value ?? string.Empty).Trim();
                    if (_prefix != v)
                    {
                        _prefix = v;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Prefix)));
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        public sealed class RenumberPreviewRow : INotifyPropertyChanged
        {
            private bool _isSelected;

            public StructureRecordItem Record { get; set; }
            public string TypeName => Record?.TypeName ?? string.Empty;
            public string OldTag { get; set; }
            public string NewTag { get; set; }
            public bool IsPrefixMismatch { get; set; }
            public bool IsChanged => !string.Equals(OldTag, NewTag, StringComparison.Ordinal);
            public string StatusText { get; set; }
            public string MainSection => Record?.MainSection ?? string.Empty;
            public string PositionStr => Record?.PositionStr ?? string.Empty;

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected != value)
                    {
                        _isSelected = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        private readonly List<StructureRecordItem> _records;
        private readonly List<TypePrefixRow> _prefixRows = new List<TypePrefixRow>();
        private List<RenumberPreviewRow> _previewRows = new List<RenumberPreviewRow>();
        private bool _isInitializing = true;

        /// <summary>成功应用后实际更新的构件数量</summary>
        public int UpdatedCount { get; private set; }

        public StructureRenumberWindow(List<StructureRecordItem> records)
        {
            InitializeComponent();
            _records = records?.ToList() ?? new List<StructureRecordItem>();

            LoadSavedRuleSettings();
            if (_records.Count == 0 && ChkOnlyExistingTypes != null)
            {
                ChkOnlyExistingTypes.IsChecked = false;
            }
            BuildTypePrefixRows();
            _isInitializing = false;

            RebuildPreview();

            UserSettings.RestoreWindowState(this, "StructureRenumberWindow");
            Closed += (s, e) => UserSettings.SaveWindowState(this, "StructureRenumberWindow");
        }

        private void LoadSavedRuleSettings()
        {
            string actionMode = UserSettings.GetString("Renumber.ActionMode", "ForcePrefixAndRenumber");
            SelectComboByTag(CmbActionMode, actionMode, fallbackIndex: 0);

            int digits = UserSettings.GetInt("Renumber.Digits", 3);
            SelectComboByTag(CmbDigits, digits.ToString(CultureInfo.InvariantCulture), fallbackIndex: 1);

            int startNum = UserSettings.GetInt("Renumber.Start", 1);
            TxtStartNumber.Text = Math.Max(1, startNum).ToString(CultureInfo.InvariantCulture);

            string sep = UserSettings.GetString("Renumber.Separator", "-");
            SelectComboByTag(CmbSeparator, sep, fallbackIndex: 0);

            string mode = UserSettings.GetString("Renumber.Mode", "ByType");
            SelectComboByTag(CmbNumberingMode, mode, fallbackIndex: 0);

            string sort = UserSettings.GetString("Renumber.Sort", "XYZ");
            SelectComboByTag(CmbSortOrder, sort, fallbackIndex: 0);
        }

        private static void SelectComboByTag(ComboBox cmb, string tagVal, int fallbackIndex)
        {
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                if (cmb.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag as string ?? string.Empty, tagVal ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    cmb.SelectedIndex = i;
                    return;
                }
            }
            if (fallbackIndex >= 0 && fallbackIndex < cmb.Items.Count)
                cmb.SelectedIndex = fallbackIndex;
        }

        private void BuildTypePrefixRows()
        {
            _prefixRows.Clear();

            var countMap = _records
                .GroupBy(r => r.Params.StructureType)
                .ToDictionary(g => g.Key, g => g.Count());

            var allTypes = Enum.GetValues(typeof(ParametricStructureType))
                .Cast<ParametricStructureType>()
                .Select(st =>
                {
                    var sample = new SteelStructureParams { StructureType = st };
                    return new
                    {
                        Type = st,
                        CategoryOrder = sample.CategoryOrder,
                        TypeName = sample.StructureTypeDisplayName,
                        Count = countMap.TryGetValue(st, out int c) ? c : 0
                    };
                })
                .OrderBy(x => x.CategoryOrder)
                .ThenBy(x => (int)x.Type);

            foreach (var item in allTypes)
            {
                string prefix = UserSettings.GetTagPrefix(item.Type);
                var row = new TypePrefixRow
                {
                    StructureType = item.Type,
                    CategoryOrder = item.CategoryOrder,
                    TypeName = item.TypeName,
                    Count = item.Count,
                    Prefix = prefix
                };
                row.PropertyChanged += (s, e) =>
                {
                    if (!_isInitializing) RebuildPreview();
                };
                _prefixRows.Add(row);
            }

            RefreshPrefixGridFilter();
        }

        private void RefreshPrefixGridFilter()
        {
            if (DgPrefixes == null) return;
            bool onlyExisting = ChkOnlyExistingTypes?.IsChecked == true;
            var view = onlyExisting
                ? _prefixRows.Where(r => r.Count > 0).ToList()
                : _prefixRows.ToList();
            DgPrefixes.ItemsSource = null;
            DgPrefixes.ItemsSource = view;
        }

        private void OnPrefixFilterChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            RefreshPrefixGridFilter();
        }

        private void RebuildPreview()
        {
            if (_isInitializing) return;

            string actionMode = GetSelectedTag(CmbActionMode, "ForcePrefixAndRenumber");
            int digits = GetSelectedDigits();
            int startNum = GetStartNumber();
            string sep = GetSelectedTag(CmbSeparator, "-");
            bool byType = string.Equals(GetSelectedTag(CmbNumberingMode, "ByType"), "ByType", StringComparison.OrdinalIgnoreCase);
            string sortMode = GetSelectedTag(CmbSortOrder, "XYZ");

            var prefixMap = _prefixRows.ToDictionary(r => r.StructureType, r => r.Prefix ?? string.Empty);
            string numFmt = new string('0', digits);

            var newPreview = new List<RenumberPreviewRow>();

            if (byType)
            {
                var groups = _records
                    .GroupBy(r => r.Params.StructureType)
                    .OrderBy(g => g.First().Params.CategoryOrder)
                    .ThenBy(g => (int)g.Key);

                foreach (var grp in groups)
                {
                    string cfgPrefix = prefixMap.TryGetValue(grp.Key, out string p) ? p : GetDefaultPrefix(grp.Key);
                    var ordered = SortRecords(grp, sortMode);
                    int seq = startNum;
                    foreach (var rec in ordered)
                    {
                        string newTag = ComputeNewTag(rec.Tag, cfgPrefix, sep, seq, numFmt, actionMode);
                        seq++;
                        newPreview.Add(CreatePreviewRow(rec, newTag, cfgPrefix));
                    }
                }
            }
            else
            {
                var ordered = SortRecords(_records, sortMode);
                int seq = startNum;
                foreach (var rec in ordered)
                {
                    string cfgPrefix = prefixMap.TryGetValue(rec.Params.StructureType, out string p)
                        ? p
                        : GetDefaultPrefix(rec.Params.StructureType);
                    string newTag = ComputeNewTag(rec.Tag, cfgPrefix, sep, seq, numFmt, actionMode);
                    seq++;
                    newPreview.Add(CreatePreviewRow(rec, newTag, cfgPrefix));
                }
            }

            _previewRows = newPreview;
            foreach (var row in _previewRows)
            {
                row.PropertyChanged += (s, e) => UpdatePreviewSummary();
            }

            DgPreview.ItemsSource = null;
            DgPreview.ItemsSource = _previewRows;
            UpdatePreviewSummary();
        }

        private static string ComputeNewTag(string oldTag, string configuredPrefix, string sep, int seq, string numFmt, string actionMode)
        {
            if (string.Equals(actionMode, "ForcePrefixOnly", StringComparison.OrdinalIgnoreCase))
            {
                // 仅强制统一前缀与连接符/位数，尽量保留原序号（无数字时退回顺号 seq）
                int oldNum = ExtractTagNumber(oldTag);
                int effNum = oldNum == int.MaxValue ? seq : oldNum;
                return FormatTag(configuredPrefix, sep, effNum, numFmt);
            }

            if (string.Equals(actionMode, "RenumberKeepPrefix", StringComparison.OrdinalIgnoreCase))
            {
                // 仅顺号消除断号，保留原位号前缀
                string oldCleanPrefix = ExtractCleanAlphaPrefix(oldTag);
                string usePrefix = string.IsNullOrEmpty(oldCleanPrefix) ? configuredPrefix : oldCleanPrefix;
                return FormatTag(usePrefix, sep, seq, numFmt);
            }

            // 默认 ForcePrefixAndRenumber：强制统一为配置前缀并重新顺号
            return FormatTag(configuredPrefix, sep, seq, numFmt);
        }

        private static string ExtractCleanAlphaPrefix(string tag)
        {
            string raw = ExtractTagPrefix(tag);
            return raw.TrimEnd('-', '_', ' ');
        }

        private static RenumberPreviewRow CreatePreviewRow(StructureRecordItem rec, string newTag, string configuredPrefix)
        {
            string oldTag = rec.Tag ?? string.Empty;
            string oldCleanPrefix = ExtractCleanAlphaPrefix(oldTag);
            bool prefixMismatch = !string.Equals(oldCleanPrefix, configuredPrefix ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            bool changed = !string.Equals(oldTag, newTag, StringComparison.Ordinal);

            string status;
            if (!changed)
            {
                status = "无变化";
            }
            else if (prefixMismatch)
            {
                int oldNum = ExtractTagNumber(oldTag);
                int newNum = ExtractTagNumber(newTag);
                status = (oldNum == newNum) ? "统一前缀" : "统一前缀+顺号";
            }
            else
            {
                status = "顺号调整";
            }

            return new RenumberPreviewRow
            {
                Record = rec,
                OldTag = oldTag,
                NewTag = newTag,
                IsPrefixMismatch = prefixMismatch,
                StatusText = status,
                IsSelected = changed
            };
        }

        private static IEnumerable<StructureRecordItem> SortRecords(IEnumerable<StructureRecordItem> items, string sortMode)
        {
            switch (sortMode)
            {
                case "YXZ":
                    return items
                        .OrderBy(r => Math.Round(r.Params.Origin.Y, 1))
                        .ThenBy(r => Math.Round(r.Params.Origin.X, 1))
                        .ThenBy(r => Math.Round(r.Params.Origin.Z, 1))
                        .ThenBy(r => r.Tag, StringComparer.OrdinalIgnoreCase);

                case "OldTag":
                    return items
                        .OrderBy(r => ExtractTagPrefix(r.Tag), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(r => ExtractTagNumber(r.Tag))
                        .ThenBy(r => r.Tag, StringComparer.OrdinalIgnoreCase);

                default: // XYZ
                    return items
                        .OrderBy(r => Math.Round(r.Params.Origin.X, 1))
                        .ThenBy(r => Math.Round(r.Params.Origin.Y, 1))
                        .ThenBy(r => Math.Round(r.Params.Origin.Z, 1))
                        .ThenBy(r => r.Tag, StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string ExtractTagPrefix(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return string.Empty;
            int idx = tag.Length;
            while (idx > 0 && char.IsDigit(tag[idx - 1])) idx--;
            return tag.Substring(0, idx);
        }

        private static int ExtractTagNumber(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return int.MaxValue;
            int idx = tag.Length;
            while (idx > 0 && char.IsDigit(tag[idx - 1])) idx--;
            if (idx < tag.Length && int.TryParse(tag.Substring(idx), NumberStyles.Integer, CultureInfo.InvariantCulture, out int num))
                return num;
            return int.MaxValue;
        }

        private static string FormatTag(string prefix, string sep, int seq, string numFmt)
        {
            string numStr = seq.ToString(numFmt, CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(prefix))
                return numStr;
            return prefix + (sep ?? string.Empty) + numStr;
        }

        private int GetSelectedDigits()
        {
            string tag = GetSelectedTag(CmbDigits, "3");
            if (int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) && d >= 1 && d <= 6)
                return d;
            return 3;
        }

        private int GetStartNumber()
        {
            if (int.TryParse(TxtStartNumber?.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 0)
                return n;
            return 1;
        }

        private static string GetSelectedTag(ComboBox cmb, string fallback)
        {
            if (cmb?.SelectedItem is ComboBoxItem item && item.Tag != null)
                return item.Tag.ToString();
            return fallback;
        }

        private void UpdatePreviewSummary()
        {
            if (TxtPreviewSummary == null) return;
            int total = _previewRows.Count;
            int prefixMismatch = _previewRows.Count(r => r.IsPrefixMismatch);
            int selectedChanged = _previewRows.Count(r => r.IsSelected && r.IsChanged);
            TxtPreviewSummary.Text = $"共 {total} 项 | 前缀不符 {prefixMismatch} 项 | 将应用 {selectedChanged} 项";
        }

        private void OnRuleOptionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            RebuildPreview();
        }

        private void OnRuleTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            RebuildPreview();
        }

        private void OnPrefixCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(RebuildPreview));
        }

        private void OnSelectAllClick(object sender, RoutedEventArgs e)
        {
            foreach (var r in _previewRows) r.IsSelected = true;
            UpdatePreviewSummary();
        }

        private void OnSelectChangedOnlyClick(object sender, RoutedEventArgs e)
        {
            foreach (var r in _previewRows) r.IsSelected = r.IsChanged;
            UpdatePreviewSummary();
        }

        private void OnSelectPrefixMismatchClick(object sender, RoutedEventArgs e)
        {
            foreach (var r in _previewRows) r.IsSelected = r.IsPrefixMismatch && r.IsChanged;
            UpdatePreviewSummary();
        }

        private void OnSelectNoneClick(object sender, RoutedEventArgs e)
        {
            foreach (var r in _previewRows) r.IsSelected = false;
            UpdatePreviewSummary();
        }

        private void OnResetPrefixesClick(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            foreach (var row in _prefixRows)
            {
                row.Prefix = UserSettings.GetDefaultTagPrefix(row.StructureType);
            }
            _isInitializing = false;
            RefreshPrefixGridFilter();
            RebuildPreview();
        }

        private void OnSavePresetsOnlyClick(object sender, RoutedEventArgs e)
        {
            SaveCurrentRuleSettings();
            MessageBox.Show("已保存各类型编号前缀预设、编号位数与连接符设置。\n后续新建构件与顺号重排均会自动采用此配置。", "已保存", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveCurrentRuleSettings()
        {
            UserSettings.SetString("Renumber.ActionMode", GetSelectedTag(CmbActionMode, "ForcePrefixAndRenumber"));
            UserSettings.SetInt("Renumber.Digits", GetSelectedDigits());
            UserSettings.SetInt("Renumber.Start", GetStartNumber());
            UserSettings.SetString("Renumber.Separator", GetSelectedTag(CmbSeparator, "-"));
            UserSettings.SetString("Renumber.Mode", GetSelectedTag(CmbNumberingMode, "ByType"));
            UserSettings.SetString("Renumber.Sort", GetSelectedTag(CmbSortOrder, "XYZ"));
            foreach (var row in _prefixRows)
            {
                UserSettings.SetTagPrefix(row.StructureType, row.Prefix);
            }
            UserSettings.Save();
        }

        private void OnApplyClick(object sender, RoutedEventArgs e)
        {
            var targets = _previewRows.Where(r => r.IsSelected && r.IsChanged).ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show("当前没有勾选需要变更位号的构件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            SaveCurrentRuleSettings();

            int updated = 0;
            try
            {
                using (DocumentLock dl = doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (var item in targets)
                    {
                        ObjectId id = item.Record.EntityId;
                        if (id.IsNull || !id.IsValid || id.IsErased) continue;

                        if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent)
                        {
                            SteelStructureParams updatedParams = item.Record.Params.Clone();
                            updatedParams.Tag = item.NewTag;
                            SteelDataStorage.WriteParams(tr, ent, updatedParams);
                            updated++;
                        }
                    }
                    tr.Commit();
                }

                doc.Editor.Regen();
                UpdatedCount = updated;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"顺号更新失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static string GetDefaultPrefix(ParametricStructureType type) => UserSettings.GetDefaultTagPrefix(type);
    }
}
