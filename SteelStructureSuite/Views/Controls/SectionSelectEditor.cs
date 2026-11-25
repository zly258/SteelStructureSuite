using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Windows.Forms.Design;

namespace SteelStructureSuite.Views.Controls
{
    /// <summary>
    /// 截面字段的 PropertyGrid 自定义编辑器：在单元格下方内嵌展开
    /// 「搜索框 + 两级树（大类→类型→规格）」面板，选中即写回，不弹独立对话框。
    ///
    /// 交互沿用既有截面下拉：检索、展开大类、点选规格；双击或回车提交，点别处也会提交。
    /// </summary>
    public sealed class SectionSelectEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
            => UITypeEditorEditStyle.DropDown;

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            var editorService = provider?.GetService(typeof(IWindowsFormsEditorService)) as IWindowsFormsEditorService;
            if (editorService == null) return value;

            var panel = new SectionDropdownControl(value as string)
            {
                Width = 340,
                Height = 340
            };

            string committed = value as string;

            panel.CommitRequested = () =>
            {
                if (!string.IsNullOrEmpty(panel.SelectedName))
                    committed = panel.SelectedName;
                editorService.CloseDropDown();
            };

            editorService.DropDownControl(panel);

            // 用户点别处关闭下拉时，若已选中规格则采用
            if (!string.IsNullOrEmpty(panel.SelectedName))
                committed = panel.SelectedName;

            panel.Dispose();
            return committed;
        }

        public override bool IsDropDownResizable => true;
    }
}
