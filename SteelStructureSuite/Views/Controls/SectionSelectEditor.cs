using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Windows;
using System.Windows.Forms.Design;

namespace SteelStructureSuite.Views.Controls
{
    /// <summary>
    /// 截面字段的 PropertyGrid 自定义编辑器：弹出一个带搜索的两级截面选择面板
    /// （类型 + 规格），返回选中的截面代号。体验与原建模窗口的截面下拉一致。
    /// </summary>
    public sealed class SectionSelectEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
            => UITypeEditorEditStyle.Modal;

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            string current = value as string;

            // 由 WinForms PropertyGrid 提供 IUIService，拿它的 owner 窗口做模态父窗
            var uiService = provider?.GetService(typeof(IUIService)) as IUIService;
            Window owner = uiService?.GetDialogOwnerWindow() as Window;

            var dialog = new SectionSelectDialog(current);
            bool ok;
            if (owner != null)
            {
                dialog.Owner = owner;
                ok = dialog.ShowDialog() == true;
            }
            else
            {
                ok = dialog.ShowDialog() == true;
            }

            return ok ? dialog.SelectedSectionName : value;
        }
    }
}
