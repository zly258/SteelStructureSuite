using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SteelStructureSuite.Services.Drawing;
using SteelStructureSuite.Services.Settings;
namespace SteelStructureSuite.Views
{
    public partial class DwgMergeWindow : Window
    {
        public string SavedPath { get; private set; }
        public bool OpenWhenClosed { get; private set; }

        public DwgMergeWindow()
        {
            InitializeComponent();
            bool vertical = UserSettings.GetString("Merge.Direction", "H") == "V";
            RadVertical.IsChecked = vertical;
            RadHorizontal.IsChecked = !vertical;
            TxtGap.Text = UserSettings.GetDouble("Merge.Gap", 0).ToString("0.###", CultureInfo.CurrentCulture);
            ChkExplode.IsChecked = UserSettings.GetBool("Merge.Explode", false);
            ChkOpen.IsChecked = UserSettings.GetBool("Merge.OpenAfter", true);
            TxtOutput.Text = UserSettings.GetString("Merge.OutputPath", "");
            UserSettings.RestoreWindowState(this, "DwgMergeWindow");
            Closed += (s, e) =>
            {
                SaveSettings();
                UserSettings.SaveWindowState(this, "DwgMergeWindow");
            };
        }

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择要合并的图纸",
                Filter = "图纸 (*.dwg;*.dxf)|*.dwg;*.dxf|DWG 图纸 (*.dwg)|*.dwg|DXF 图纸 (*.dxf)|*.dxf",
                Multiselect = true,
                CheckFileExists = true
            };
            string initial = UserSettings.GetString("Merge.SourceDir", "");
            if (Directory.Exists(initial)) dlg.InitialDirectory = initial;
            if (dlg.ShowDialog(this) != true) return;

            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DwgPathItem item in LstFiles.Items) known.Add(item.FullPath);
            foreach (string path in dlg.FileNames)
            {
                string full;
                try { full = Path.GetFullPath(path); }
                catch { continue; }
                if (!known.Add(full)) continue;
                LstFiles.Items.Add(new DwgPathItem(full));
            }
            if (dlg.FileNames.Length > 0)
            {
                string folder = Path.GetDirectoryName(dlg.FileNames[0]);
                if (!string.IsNullOrEmpty(folder)) UserSettings.SetString("Merge.SourceDir", folder);
                if (string.IsNullOrWhiteSpace(TxtOutput.Text))
                    TxtOutput.Text = Path.Combine(folder ?? string.Empty, "合并图纸.dwg");
            }
        }

        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            int index = LstFiles.SelectedIndex;
            if (index < 0) return;
            LstFiles.Items.RemoveAt(index);
            if (LstFiles.Items.Count > 0)
                LstFiles.SelectedIndex = Math.Min(index, LstFiles.Items.Count - 1);
        }

        private void OnClearClick(object sender, RoutedEventArgs e) => LstFiles.Items.Clear();

        private void OnUpClick(object sender, RoutedEventArgs e) => MoveSelected(-1);

        private void OnDownClick(object sender, RoutedEventArgs e) => MoveSelected(1);

        private void OnBrowseOutputClick(object sender, RoutedEventArgs e)
        {
            string currentName = string.IsNullOrWhiteSpace(TxtOutput.Text) ? "合并图纸.dwg" : Path.GetFileName(TxtOutput.Text);
            bool dxf = currentName.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase);
            var dlg = new SaveFileDialog
            {
                Title = "合并后的图纸",
                Filter = "DWG 图纸 (*.dwg)|*.dwg|DXF 图纸 (*.dxf)|*.dxf",
                FilterIndex = dxf ? 2 : 1,
                FileName = currentName,
                AddExtension = true,
                OverwritePrompt = false
            };
            string dir = string.IsNullOrWhiteSpace(TxtOutput.Text) ? string.Empty : Path.GetDirectoryName(TxtOutput.Text);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
            if (dlg.ShowDialog(this) == true) TxtOutput.Text = dlg.FileName;
        }

        private void OnMergeClick(object sender, RoutedEventArgs e)
        {
            var files = new List<string>();
            foreach (DwgPathItem item in LstFiles.Items) files.Add(item.FullPath);
            if (files.Count == 0)
            {
                TxtLog.Text = "请先添加 DWG 或 DXF 文件。";
                return;
            }

            double gap;
            if (!TryParseGap(TxtGap.Text, out gap))
            {
                TxtLog.Text = "间距必须是大于或等于 0 的数值。";
                return;
            }

            string output = TxtOutput.Text == null ? string.Empty : TxtOutput.Text.Trim();
            if (output.Length == 0)
            {
                TxtLog.Text = "请指定输出图纸。";
                return;
            }
            if (!output.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) && !output.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)) output += ".dwg";
            if (File.Exists(output))
            {
                MessageBoxResult answer = MessageBox.Show(this, "输出文件已存在，是否覆盖？", "合并图纸", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
            }

            SaveSettings();
            BtnRun.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var direction = RadVertical.IsChecked == true ? DwgMergeDirection.Vertical : DwgMergeDirection.Horizontal;
                List<DwgFileResult> results = DwgExchangeService.MergeDrawings(
                    files, output, direction, gap, ChkExplode.IsChecked == true);
                TxtLog.Text = DwgToDxfWindow.FormatLog(results);
                foreach (DwgFileResult item in results)
                {
                    if (item.Success && item.Message != null && item.Message.StartsWith("已合并", StringComparison.Ordinal))
                    {
                        SavedPath = output;
                        OpenWhenClosed = ChkOpen.IsChecked == true;
                    }
                }
            }
            catch (Exception ex)
            {
                TxtLog.Text = ex.Message;
            }
            finally
            {
                Mouse.OverrideCursor = null;
                BtnRun.IsEnabled = true;
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private void MoveSelected(int delta)
        {
            int index = LstFiles.SelectedIndex;
            int next = index + delta;
            if (index < 0 || next < 0 || next >= LstFiles.Items.Count) return;
            object item = LstFiles.Items[index];
            LstFiles.Items.RemoveAt(index);
            LstFiles.Items.Insert(next, item);
            LstFiles.SelectedIndex = next;
        }

        private void SaveSettings()
        {
            UserSettings.SetString("Merge.Direction", RadVertical.IsChecked == true ? "V" : "H");
            double gap;
            if (TryParseGap(TxtGap.Text, out gap)) UserSettings.SetDouble("Merge.Gap", gap);
            UserSettings.SetBool("Merge.Explode", ChkExplode.IsChecked == true);
            UserSettings.SetBool("Merge.OpenAfter", ChkOpen.IsChecked == true);
            UserSettings.SetString("Merge.OutputPath", TxtOutput.Text == null ? string.Empty : TxtOutput.Text.Trim());
            UserSettings.Save();
        }

        private static bool TryParseGap(string text, out double gap)
        {
            text = text == null ? string.Empty : text.Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out gap)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out gap))
            {
                return gap >= 0 && !double.IsNaN(gap) && !double.IsInfinity(gap);
            }
            gap = 0;
            return false;
        }
    }
}
