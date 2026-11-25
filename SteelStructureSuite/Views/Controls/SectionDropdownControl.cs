using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Catalog;

namespace SteelStructureSuite.Views.Controls
{
    /// <summary>
    /// 截面选择下拉面板（WinForms 原生，供 PropertyGrid 的 DropDown 编辑器内嵌使用）。
    ///
    /// 结构与既有的 WPF 截面下拉一致：顶部搜索框 + 两级树（大类 → 截面类型 → 规格）。
    /// 直接在 PropertyGrid 单元格下方展开，不弹独立对话框。
    /// </summary>
    public sealed class SectionDropdownControl : UserControl
    {
        private readonly TextBox _search;
        private readonly TreeView _tree;
        private bool _suppress;

        /// <summary>当前选中的截面代号。</summary>
        public string SelectedName { get; private set; } = string.Empty;

        /// <summary>请求提交（回车或双击规格）。由编辑器注入以关闭下拉。</summary>
        public Action CommitRequested { get; set; }

        public SectionDropdownControl(string initialName)
        {
            SelectedName = initialName ?? string.Empty;

            BorderStyle = BorderStyle.FixedSingle;
            Padding = new Padding(4);
            BackColor = SystemColors.Window;

            _search = new TextBox
            {
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 4)
            };
            _search.TextChanged += (s, e) => { if (!_suppress) BuildTree(_search.Text); };
            _search.KeyDown += OnSearchKeyDown;

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                FullRowSelect = false,
                ShowLines = true,
                ShowPlusMinus = true
            };
            _tree.AfterSelect += OnAfterSelect;
            _tree.NodeMouseDoubleClick += (s, e) => RaiseCommit();
            _tree.KeyDown += OnTreeKeyDown;

            // 顺序：先填满填控件，再把搜索框压到顶部
            Controls.Add(_tree);
            Controls.Add(_search);

            BuildTree(string.Empty);
            SelectByName(SelectedName);
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (!string.IsNullOrEmpty(SelectedName)) RaiseCommit();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down)
            {
                _tree.Focus();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                CommitRequested = null;   // 取消：不提交
                e.Handled = true;
            }
        }

        private void OnTreeKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { RaiseCommit(); e.Handled = true; }
        }

        private void RaiseCommit()
        {
            if (string.IsNullOrEmpty(SelectedName)) return;
            CommitRequested?.Invoke();
        }

        private void OnAfterSelect(object sender, TreeViewEventArgs e)
        {
            if (_suppress) return;
            if (e.Node?.Tag is string name && !string.IsNullOrEmpty(name))
                SelectedName = name;
        }

        private void BuildTree(string key)
        {
            _suppress = true;
            string filter = (key ?? string.Empty).Trim();
            _tree.BeginUpdate();
            _tree.Nodes.Clear();

            bool Match(string text) =>
                filter.Length == 0 ||
                (!string.IsNullOrEmpty(text) &&
                 text.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0);

            foreach (SectionCategory cat in Enum.GetValues(typeof(SectionCategory)))
            {
                List<SectionShapeType> shapes = SectionCatalogService.GetShapeTypesByCategory(cat);
                if (shapes == null || shapes.Count == 0) continue;

                var catNode = new TreeNode(CategoryDisplayName(cat));

                foreach (SectionShapeType shape in shapes)
                {
                    List<SectionProfile> profiles = SectionCatalogService.GetByShapeType(shape);
                    if (profiles == null || profiles.Count == 0) continue;

                    bool shapeMatch = Match(SectionCatalogService.GetShapeTabTitle(shape));
                    List<SectionProfile> hits = profiles
                        .Where(p => shapeMatch || Match(p.Name))
                        .ToList();
                    if (hits.Count == 0) continue;

                    var shapeNode = new TreeNode($"{SectionCatalogService.GetShapeTabTitle(shape)} ({hits.Count})");
                    foreach (SectionProfile p in hits)
                        shapeNode.Nodes.Add(new TreeNode(p.Name) { Tag = p.Name });

                    catNode.Nodes.Add(shapeNode);
                }

                if (catNode.Nodes.Count > 0)
                    _tree.Nodes.Add(catNode);
            }

            // 筛选时全部展开
            if (filter.Length > 0)
                _tree.ExpandAll();
            else if (_tree.Nodes.Count > 0)
                _tree.Nodes[0].Expand();

            _tree.EndUpdate();
            _suppress = false;
        }

        private void SelectByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            _suppress = true;
            foreach (TreeNode catNode in _tree.Nodes)
            {
                foreach (TreeNode shapeNode in catNode.Nodes)
                {
                    foreach (TreeNode specNode in shapeNode.Nodes)
                    {
                        if (string.Equals(specNode.Tag as string, name, StringComparison.OrdinalIgnoreCase))
                        {
                            catNode.Expand();
                            shapeNode.Expand();
                            _tree.SelectedNode = specNode;
                            specNode.EnsureVisible();
                            _suppress = false;
                            return;
                        }
                    }
                }
            }
            _suppress = false;
        }

        private static string CategoryDisplayName(SectionCategory cat)
        {
            switch (cat)
            {
                case SectionCategory.HotRolled: return "热轧标准型钢";
                case SectionCategory.WeldedComposite: return "焊接与组合型钢";
                case SectionCategory.ColdFormed: return "冷弯薄壁型钢";
                case SectionCategory.HollowTube: return "空心钢管";
                case SectionCategory.SolidBar: return "实心型材";
                default: return cat.ToString();
            }
        }
    }
}
