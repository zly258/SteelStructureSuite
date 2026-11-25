using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Settings;

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
            Width = Math.Max(Width, 530);
            MinWidth = 490;
            MinHeight = 320;

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

            RestoreLastSettings(targetParams);

            UpdateTargetControlsState();

            UserSettings.RestoreWindowState(this, "DrawingConfigWindow");
            Closed += (s, e) => UserSettings.SaveWindowState(this, "DrawingConfigWindow");
        }

        /// <summary>
        /// 恢复上一次使用的出图设置（图幅、比例、各视图勾选、输出目标等）。
        /// </summary>
        private void RestoreLastSettings(SteelStructureParams targetParams)
        {
            int sheetIdx = UserSettings.GetInt("Drawing.SheetSizeIndex", 1);
            if (sheetIdx >= 0 && sheetIdx < SheetNames.Length) CmbSheetSize.SelectedIndex = sheetIdx;

            int scaleIdx = UserSettings.GetInt("Drawing.ScaleIndex", 0);
            if (scaleIdx >= 0 && scaleIdx <= ScaleValues.Length) CmbScale.SelectedIndex = scaleIdx;

            ChkFront.IsChecked = UserSettings.GetBool("Drawing.FrontView", true);
            ChkSide.IsChecked = UserSettings.GetBool("Drawing.SideView", true);
            ChkPlan.IsChecked = UserSettings.GetBool("Drawing.PlanView", true);
            ChkIso.IsChecked = UserSettings.GetBool("Drawing.IsoView", true);
            ChkDim.IsChecked = UserSettings.GetBool("Drawing.Dimensions", true);
            ChkLeader.IsChecked = UserSettings.GetBool("Drawing.Leaders", true);
            ChkBom.IsChecked = UserSettings.GetBool("Drawing.Bom", true);
            ChkJoint.IsChecked = UserSettings.GetBool("Drawing.JointDetail", true);

            bool toNewDwg = UserSettings.GetBool("Drawing.OutputToNewDwg", false);
            RadNewDwg.IsChecked = toNewDwg;
            RadCurrentDwg.IsChecked = !toNewDwg;
            ChkOpenAfterSave.IsChecked = UserSettings.GetBool("Drawing.OpenAfterSave", true);

            // 默认文件名：构件编号 + 类型显示名（标准号里含 "/" 等非法字符，必须净化）
            string displayName = SafeFileNamePart(targetParams.StructureTypeDisplayName);
            string fileName = SafeFileNamePart(targetParams.Tag) + "_" + displayName + ".dwg";
            string folder = UserSettings.GetString("Drawing.LastDwgFolder", string.Empty);
            if (string.IsNullOrEmpty(folder))
                folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            TxtNewDwgPath.Text = Path.Combine(folder, fileName);
        }

        /// <summary>
        /// 去除文件名中的 Windows 非法字符（标准号 "HG/T 21543"、"DL/T 5218" 等都含斜杠，
        /// 直接拼进文件名会导致 SaveAs 失败并报 eWrongDatabase）。
        /// </summary>
        private static string SafeFileNamePart(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "详图";
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if (char.IsControl(ch)) continue;
                if ("< > : \"/ \\ | ? *".IndexOf(ch) >= 0) continue;
                sb.Append(ch);
            }
            string result = sb.ToString().Trim().TrimEnd('.');
            return result.Length == 0 ? "详图" : result;
        }

        /// <summary>
        /// 保存本次使用的出图设置，供下次打开时恢复。
        /// </summary>
        private void SaveLastSettings()
        {
            UserSettings.SetInt("Drawing.SheetSizeIndex", CmbSheetSize.SelectedIndex);
            UserSettings.SetInt("Drawing.ScaleIndex", CmbScale.SelectedIndex);
            UserSettings.SetBool("Drawing.FrontView", ChkFront.IsChecked == true);
            UserSettings.SetBool("Drawing.SideView", ChkSide.IsChecked == true);
            UserSettings.SetBool("Drawing.PlanView", ChkPlan.IsChecked == true);
            UserSettings.SetBool("Drawing.IsoView", ChkIso.IsChecked == true);
            UserSettings.SetBool("Drawing.Dimensions", ChkDim.IsChecked == true);
            UserSettings.SetBool("Drawing.Leaders", ChkLeader.IsChecked == true);
            UserSettings.SetBool("Drawing.Bom", ChkBom.IsChecked == true);
            UserSettings.SetBool("Drawing.JointDetail", ChkJoint.IsChecked == true);
            UserSettings.SetBool("Drawing.OutputToNewDwg", RadNewDwg.IsChecked == true);
            UserSettings.SetBool("Drawing.OpenAfterSave", ChkOpenAfterSave.IsChecked == true);

            try
            {
                string folder = Path.GetDirectoryName(TxtNewDwgPath.Text);
                if (!string.IsNullOrEmpty(folder)) UserSettings.SetString("Drawing.LastDwgFolder", folder);
            }
            catch
            {
                // 路径非法时忽略，不影响出图
            }

            UserSettings.Save();
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
            Options.IncludeJointDetailViews = ChkJoint.IsChecked == true;

            int scaleIdx = CmbScale.SelectedIndex;
            Options.AutoScale = scaleIdx <= 0;
            Options.DrawingScale = (scaleIdx >= 1 && scaleIdx - 1 < ScaleValues.Length)
                ? ScaleValues[scaleIdx - 1]
                : 20.0;
            Options.OutputToNewDwgFile = RadNewDwg.IsChecked == true;
            Options.NewDwgFilePath = TxtNewDwgPath.Text?.Trim() ?? string.Empty;
            Options.OpenNewDwgAfterSave = ChkOpenAfterSave.IsChecked == true;

            SaveLastSettings();

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
