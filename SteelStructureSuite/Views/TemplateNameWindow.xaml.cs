using System;
using System.Globalization;
using System.Windows;

namespace SteelStructureSuite.Views
{
    /// <summary>
    /// 「保存为模板」输入对话框：模板名称、备注、是否通用。
    /// </summary>
    public partial class TemplateNameWindow : Window
    {
        public string TemplateName { get; private set; } = string.Empty;
        public string TemplateNote { get; private set; } = string.Empty;
        public bool IsGeneric => ChkGeneric.IsChecked == true;

        public TemplateNameWindow(string structureTypeName)
        {
            InitializeComponent();

            TxtHint.Text = $"把当前「{structureTypeName}」的全部参数保存为可复用的模板。" +
                           "模板保存在本地，可随时套用、重命名、删除或导出分享。";

            // 预填一个可读的默认名
            TxtName.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} {1:MMdd-HHmm}",
                structureTypeName,
                DateTime.Now);

            Loaded += (s, e) =>
            {
                TxtName.Focus();
                TxtName.SelectAll();
            };
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            string name = (TxtName.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "请输入模板名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtName.Focus();
                return;
            }

            TemplateName = name;
            TemplateNote = (TxtNote.Text ?? string.Empty).Trim();
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
