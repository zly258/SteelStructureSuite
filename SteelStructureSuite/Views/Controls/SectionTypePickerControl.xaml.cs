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
    /// 截面“类型”自定义下拉框：展开面板 = 顶部搜索框 + 类型树面板（大类 → 截面类型）。
    /// 选中“全部类型”时两个过滤值均为 null；选中大类只设 <see cref="SelectedCategory"/>；
    /// 选中具体类型同时设 <see cref="SelectedShapeType"/> 与其所属 <see cref="SelectedCategory"/>。
    /// </summary>
    public partial class SectionTypePickerControl : UserControl
    {
        public const string DefaultPlaceholder = "全部类型";

        private bool _syncing;

        public SectionTypePickerControl()
        {
            InitializeComponent();
            Loaded += OnControlLoaded;
        }

        public static readonly DependencyProperty SelectedCategoryProperty =
            DependencyProperty.Register(
                nameof(SelectedCategory),
                typeof(SectionCategory?),
                typeof(SectionTypePickerControl),
                new PropertyMetadata(null, OnSelectionPropertyChanged));

        /// <summary>当前选中的截面大类；为空表示不限。</summary>
        public SectionCategory? SelectedCategory
        {
            get => (SectionCategory?)GetValue(SelectedCategoryProperty);
            set => SetValue(SelectedCategoryProperty, value);
        }

        public static readonly DependencyProperty SelectedShapeTypeProperty =
            DependencyProperty.Register(
                nameof(SelectedShapeType),
                typeof(SectionShapeType?),
                typeof(SectionTypePickerControl),
                new PropertyMetadata(null, OnSelectionPropertyChanged));

        /// <summary>当前选中的截面类型；为空表示不限（仅大类或全部）。</summary>
        public SectionShapeType? SelectedShapeType
        {
            get => (SectionShapeType?)GetValue(SelectedShapeTypeProperty);
            set => SetValue(SelectedShapeTypeProperty, value);
        }

        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register(
                nameof(Placeholder),
                typeof(string),
                typeof(SectionTypePickerControl),
                new PropertyMetadata(DefaultPlaceholder, OnSelectionPropertyChanged));

        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        /// <summary>用户选择类型时触发。</summary>
        public event EventHandler SelectionChanged;

        /// <summary>外部数据变更后重建类型树。</summary>
        public void Refresh()
        {
            UpdateDisplayText();
            RebuildTree(TxtSearch?.Text);
        }

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            UpdateDisplayText();
            RebuildTree(TxtSearch?.Text);
        }

        private static void OnSelectionPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SectionTypePickerControl)d).UpdateDisplayText();
        }

        private void UpdateDisplayText()
        {
            if (BtnToggle == null) return;

            string text;
            if (SelectedShapeType.HasValue)
                text = SectionProfile.GetShapeDisplayName(SelectedShapeType.Value);
            else if (SelectedCategory.HasValue)
                text = SectionProfile.GetCategoryDisplayName(SelectedCategory.Value);
            else
                text = string.IsNullOrWhiteSpace(Placeholder) ? DefaultPlaceholder : Placeholder;

            BtnToggle.Content = text;
        }

        private void OnPopupOpened(object sender, EventArgs e)
        {
            RebuildTree(TxtSearch?.Text);
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
            RebuildTree(TxtSearch?.Text);
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

        private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_syncing) return;
            if (!(e.NewValue is TreeViewItem item)) return;

            if (item.Tag is SectionShapeType shape)
            {
                SetCurrentValue(SelectedShapeTypeProperty, shape);
                SetCurrentValue(SelectedCategoryProperty, SectionProfile.GetCategory(shape));
                BtnToggle.IsChecked = false;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (item.Tag is SectionCategory category)
            {
                SetCurrentValue(SelectedShapeTypeProperty, (SectionShapeType?)null);
                SetCurrentValue(SelectedCategoryProperty, category);
                BtnToggle.IsChecked = false;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (item.Tag == null)
            {
                SetCurrentValue(SelectedShapeTypeProperty, (SectionShapeType?)null);
                SetCurrentValue(SelectedCategoryProperty, (SectionCategory?)null);
                BtnToggle.IsChecked = false;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void RebuildTree(string query)
        {
            if (Tree == null) return;

            string keyword = (query ?? string.Empty).Trim();
            bool filtering = keyword.Length > 0;

            _syncing = true;
            Tree.Items.Clear();

            var allNode = new TreeViewItem { Header = DefaultPlaceholder, Tag = null, IsExpanded = true };

            foreach (SectionCategory category in SectionCatalogService.OrderedCategories)
            {
                string categoryName = SectionProfile.GetCategoryDisplayName(category);
                bool categoryMatches = filtering && Contains(categoryName, keyword);

                var categoryNode = new TreeViewItem { Header = categoryName, Tag = category };
                int shapeCount = 0;

                foreach (SectionShapeType shape in SectionCatalogService.GetShapeTypesByCategory(category))
                {
                    string shapeName = SectionProfile.GetShapeDisplayName(shape);
                    bool shapeMatches = filtering && Contains(shapeName, keyword);
                    if (filtering && !categoryMatches && !shapeMatches) continue;

                    categoryNode.Items.Add(new TreeViewItem
                    {
                        Header = shapeName,
                        Tag = shape,
                        IsExpanded = filtering,
                        ToolTip = shapeName
                    });
                    shapeCount++;
                }

                if (shapeCount == 0) continue;

                categoryNode.Header = categoryName + " (" + shapeCount + ")";
                categoryNode.IsExpanded = filtering;
                allNode.Items.Add(categoryNode);
            }

            Tree.Items.Add(allNode);
            TxtNoResult.Visibility = allNode.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _syncing = false;
        }

        private static bool Contains(string source, string keyword)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
