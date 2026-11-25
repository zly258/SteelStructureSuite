using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Catalog;

namespace SteelStructureSuite.Views.Controls
{
    /// <summary>
    /// 截面“规格”自定义下拉框：展开面板 = 顶部搜索框 + 规格列表（list）。
    /// 值出口 <see cref="SelectedSectionName"/> 为截面代号字符串；可用
    /// <see cref="FilterCategory"/> / <see cref="FilterShapeType"/> 限定列表范围（由“类型”下拉联动）。
    /// </summary>
    public partial class SectionPickerControl : UserControl
    {
        public const string DefaultPlaceholder = "选择规格";

        private bool _syncing;

        private sealed class SectionListItem
        {
            public string Name { get; set; }
            public string ShapeName { get; set; }
            public string CategoryName { get; set; }
            public SectionProfile Profile { get; set; }
        }

        public SectionPickerControl()
        {
            InitializeComponent();
            Loaded += OnControlLoaded;
        }

        public static readonly DependencyProperty SelectedSectionNameProperty =
            DependencyProperty.Register(
                nameof(SelectedSectionName),
                typeof(string),
                typeof(SectionPickerControl),
                new FrameworkPropertyMetadata(
                    string.Empty,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedSectionNameChanged));

        /// <summary>当前选中的截面代号（字符串）。空字符串表示未选择。</summary>
        public string SelectedSectionName
        {
            get => (string)GetValue(SelectedSectionNameProperty);
            set => SetValue(SelectedSectionNameProperty, value);
        }

        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register(
                nameof(Placeholder),
                typeof(string),
                typeof(SectionPickerControl),
                new PropertyMetadata(DefaultPlaceholder, OnPlaceholderChanged));

        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        public static readonly DependencyProperty FilterCategoryProperty =
            DependencyProperty.Register(
                nameof(FilterCategory),
                typeof(SectionCategory?),
                typeof(SectionPickerControl),
                new PropertyMetadata(null, OnFilterChanged));

        /// <summary>可选：只显示某一截面大类的规格；为空表示全部。</summary>
        public SectionCategory? FilterCategory
        {
            get => (SectionCategory?)GetValue(FilterCategoryProperty);
            set => SetValue(FilterCategoryProperty, value);
        }

        public static readonly DependencyProperty FilterShapeTypeProperty =
            DependencyProperty.Register(
                nameof(FilterShapeType),
                typeof(SectionShapeType?),
                typeof(SectionPickerControl),
                new PropertyMetadata(null, OnFilterChanged));

        /// <summary>可选：只显示某一截面类型下的规格；为空表示全部。</summary>
        public SectionShapeType? FilterShapeType
        {
            get => (SectionShapeType?)GetValue(FilterShapeTypeProperty);
            set => SetValue(FilterShapeTypeProperty, value);
        }

        /// <summary>当前选中截面的完整定义（未选择时为 null）。</summary>
        public SectionProfile SelectedProfile { get; private set; }

        /// <summary>用户从列表中选择某个截面时触发。</summary>
        public event EventHandler SelectionChanged;

        /// <summary>外部数据变更（如截面库导入）后调用。</summary>
        public void Refresh()
        {
            UpdateDisplayText();
            RebuildList(TxtSearch?.Text);
        }

        /// <summary>若当前选中项不在当前筛选结果中，则自动选中第一条（供“类型”变更后调用）。</summary>
        public void EnsureSelectionInFilteredList()
        {
            var items = LstSections?.ItemsSource as List<SectionListItem>;
            if (items == null || items.Count == 0) return;
            string current = SelectedSectionName;
            if (!items.Any(i => string.Equals(i.Name, current, StringComparison.OrdinalIgnoreCase)))
            {
                SetCurrentValue(SelectedSectionNameProperty, items[0].Name);
                ApplySelection(items[0].Name, raiseEvent: false);
            }
        }

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            UpdateDisplayText();
            RebuildList(TxtSearch?.Text);
        }

        private static void OnSelectedSectionNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SectionPickerControl)d).ApplySelection((string)e.NewValue, raiseEvent: false);
        }

        private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SectionPickerControl)d).UpdateDisplayText();
        }

        private static void OnFilterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (SectionPickerControl)d;
            if (control.IsLoaded)
                control.RebuildList(control.TxtSearch?.Text);
        }

        private void UpdateDisplayText()
        {
            if (BtnToggle == null) return;
            string name = SelectedSectionName;
            bool hasValue = !string.IsNullOrWhiteSpace(name);
            string placeholder = string.IsNullOrWhiteSpace(Placeholder) ? DefaultPlaceholder : Placeholder;
            BtnToggle.Content = hasValue ? name : placeholder;
        }

        private void ApplySelection(string sectionName, bool raiseEvent)
        {
            bool hasValue = !string.IsNullOrWhiteSpace(sectionName);
            SelectedProfile = hasValue ? ResolveProfile(sectionName) : null;
            UpdateDisplayText();

            if (raiseEvent)
                SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private static SectionProfile ResolveProfile(string sectionName)
        {
            if (string.IsNullOrWhiteSpace(sectionName)) return null;
            string key = sectionName.Trim();
            SectionProfile profile = SectionCatalogService.GetByName(key);
            return profile != null && string.Equals(profile.Name, key, StringComparison.OrdinalIgnoreCase)
                ? profile
                : null;
        }

        private void OnPopupOpened(object sender, EventArgs e)
        {
            RebuildList(TxtSearch?.Text);
            TxtSearch?.Focus();
            if (TxtSearch != null)
                TxtSearch.CaretIndex = TxtSearch.Text.Length;
        }

        private void OnPopupClosed(object sender, EventArgs e)
        {
            if (TxtSearch != null && TxtSearch.Text.Length > 0)
                TxtSearch.Text = string.Empty;
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            RebuildList(TxtSearch?.Text);
        }

        private void OnSearchPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                BtnToggle.IsChecked = false;
                BtnToggle.Focus();
                e.Handled = true;
            }
        }

        private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            if (LstSections.SelectedItem is SectionListItem item)
            {
                SetCurrentValue(SelectedSectionNameProperty, item.Name);
                ApplySelection(item.Name, raiseEvent: true);
                BtnToggle.IsChecked = false;
            }
        }

        private void RebuildList(string query)
        {
            if (LstSections == null) return;

            string keyword = (query ?? string.Empty).Trim();

            IEnumerable<SectionProfile> profiles;
            if (FilterShapeType.HasValue)
                profiles = SectionCatalogService.GetByShapeType(FilterShapeType.Value);
            else if (FilterCategory.HasValue)
                profiles = SectionCatalogService.GetByCategory(FilterCategory.Value);
            else
                profiles = SectionCatalogService.GetAllSections();

            var items = new List<SectionListItem>();
            foreach (SectionProfile p in profiles)
            {
                if (keyword.Length > 0 &&
                    !Contains(p.Name, keyword) &&
                    !Contains(p.ShapeDisplayName, keyword) &&
                    !Contains(p.CategoryDisplayName, keyword))
                    continue;

                items.Add(new SectionListItem
                {
                    Name = p.Name,
                    ShapeName = p.ShapeDisplayName,
                    CategoryName = p.CategoryDisplayName,
                    Profile = p
                });
            }

            _syncing = true;
            LstSections.ItemsSource = items;
            string current = SelectedSectionName;
            SectionListItem match = items.FirstOrDefault(i => string.Equals(i.Name, current, StringComparison.OrdinalIgnoreCase));
            if (match != null) LstSections.SelectedItem = match;
            _syncing = false;

            TxtNoResult.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool Contains(string source, string keyword)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
