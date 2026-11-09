using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Views
{
    public partial class DrawingConfigWindow : Window
    {
        public DrawingGenerationOptions Options { get; } = new DrawingGenerationOptions();

        private static readonly string[] SheetNames = { "A1", "A2", "A3", "A4" };
        private static readonly double[] ScaleValues = { 1.0, 2.0, 5.0, 10.0, 15.0, 20.0, 25.0, 30.0, 40.0, 50.0, 75.0, 100.0, 150.0, 200.0 };

        public DrawingConfigWindow(SteelStructureParams targetParams)
        {
            InitializeComponent();

            Title = $"施工详图出图设置 - [{targetParams.Tag}]";

            CmbSheetSize.Items.Add("A1 标准图框 (841×594 mm)");
            CmbSheetSize.Items.Add("A2 标准图框 (594×420 mm) [默认]");
            CmbSheetSize.Items.Add("A3 标准图框 (420×297 mm)");
            CmbSheetSize.Items.Add("A4 标准图框 (297×210 mm)");
            CmbSheetSize.SelectedIndex = 1;

            CmbScale.Items.Add("自动匹配标准比例 (推荐)");
            CmbScale.Items.Add("1:1");
            CmbScale.Items.Add("1:2");
            CmbScale.Items.Add("1:5");
            CmbScale.Items.Add("1:10");
            CmbScale.Items.Add("1:15");
            CmbScale.Items.Add("1:20");
            CmbScale.Items.Add("1:25");
            CmbScale.Items.Add("1:30");
            CmbScale.Items.Add("1:40");
            CmbScale.Items.Add("1:50");
            CmbScale.Items.Add("1:75");
            CmbScale.Items.Add("1:100");
            CmbScale.Items.Add("1:150");
            CmbScale.Items.Add("1:200");
            CmbScale.SelectedIndex = 0;

            TxtNewDwgPath.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"{targetParams.Tag}_{targetParams.StructureTypeDisplayName}.dwg");

            UpdateTargetControlsState();
        }

        private void OnOutputTargetChanged(object sender, RoutedEventArgs e)
        {
            UpdateTargetControlsState();
        }

        private void UpdateTargetControlsState()
        {
            if (TxtNewDwgPath == null || BtnBrowseDwg == null || ChkOpenAfterSave == null || BtnOk == null)
                return;

            bool isNewFile = RadNewDwg.IsChecked == true;
            TxtNewDwgPath.IsEnabled = isNewFile;
            BtnBrowseDwg.IsEnabled = isNewFile;
            ChkOpenAfterSave.IsEnabled = isNewFile;
            BtnOk.Content = isNewFile ? "创建新 DWG 文件" : "拾取点生成详图";
        }

        private void OnBrowseNewDwgClick(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog
            {
                Title = "保存独立施工详图 DWG 文件",
                Filter = "AutoCAD 图形 (*.dwg)|*.dwg",
                FileName = Path.GetFileName(TxtNewDwgPath.Text)
            };
            if (sfd.ShowDialog() == true)
            {
                TxtNewDwgPath.Text = sfd.FileName;
            }
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            int sIdx = CmbSheetSize.SelectedIndex;
            Options.SheetSizeName = (sIdx >= 0 && sIdx < SheetNames.Length) ? SheetNames[sIdx] : "A2";
            Options.IncludeFrontView = ChkFront.IsChecked == true;
            Options.IncludeSideView = ChkSide.IsChecked == true;
            Options.IncludePlanView = ChkPlan.IsChecked == true;
            Options.IncludeIsoView = ChkIso.IsChecked == true;
            Options.IncludeAutoDimensions = ChkDim.IsChecked == true;
            Options.IncludeLeaderAnnotations = ChkLeader.IsChecked == true;
            Options.IncludeEmbeddedBomTable = ChkBom.IsChecked == true;

            int scaleIdx = CmbScale.SelectedIndex;
            Options.AutoScale = scaleIdx <= 0;
            Options.DrawingScale = (scaleIdx >= 1 && scaleIdx - 1 < ScaleValues.Length)
                ? ScaleValues[scaleIdx - 1]
                : 20.0;
            Options.OutputToNewDwgFile = RadNewDwg.IsChecked == true;
            Options.NewDwgFilePath = TxtNewDwgPath.Text?.Trim() ?? string.Empty;
            Options.OpenNewDwgAfterSave = ChkOpenAfterSave.IsChecked == true;

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
