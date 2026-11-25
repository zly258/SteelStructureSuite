using System.Windows;

namespace SteelStructureSuite.Views.Controls
{
    /// <summary>截面选择对话框：承载可搜索的截面规格选择控件，返回选中代号。</summary>
    public partial class SectionSelectDialog : Window
    {
        public string SelectedSectionName { get; private set; }

        public SectionSelectDialog(string currentSection)
        {
            InitializeComponent();

            if (!string.IsNullOrWhiteSpace(currentSection))
                Picker.SelectedSectionName = currentSection;
            Picker.Refresh();

            Loaded += (s, e) => Picker.Focus();
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            string name = Picker.SelectedSectionName;
            if (string.IsNullOrWhiteSpace(name)) return;

            SelectedSectionName = name;
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
