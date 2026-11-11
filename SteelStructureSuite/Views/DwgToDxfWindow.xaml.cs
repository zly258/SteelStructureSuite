using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SteelStructureSuite.Services.Drawing;
using SteelStructureSuite.Services.Settings;

namespace SteelStructureSuite.Views
{
    public sealed class DwgPathItem
    {
        public DwgPathItem(string fullPath)
        {
            FullPath = fullPath;
        }

        public string FullPath { get; private set; }
        public string Name
        {
            get { return Path.GetFileName(FullPath); }
        }
    }

    public partial class DwgToDxfWindow : Window
    {
        public DwgToDxfWindow()
        {
            InitializeComponent();
            foreach (DxfVersionChoice choice in DwgExchangeService.GetSupportedDxfVersions())
                CmbVersion.Items.Add(choice);

            string savedKey = UserSettings.GetString("Dxf.Version", "AC1032");
            int index = 0;
            for (int i = 0; i < CmbVersion.Items.Count; i++)
            {
                var choice = (DxfVersionChoice)CmbVersion.Items[i];
                if (string.Equals(choice.Key, savedKey, StringComparison.OrdinalIgnoreCase))
                    index = i;
            }
            if (CmbVersion.Items.Count > 0) CmbVersion.SelectedIndex = index;

            TxtOutput.Text = UserSettings.GetString("Dxf.OutputDir", "");
            ChkOverwrite.IsChecked = UserSettings.GetBool("Dxf.Overwrite", false);
            UserSettings.RestoreWindowState(this, "DwgToDxfWindow");
            Closed += (s, e) =>
            {
                SaveSettings();
                UserSettings.SaveWindowState(this, "DwgToDxfWindow");
            };
        }

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择 DWG 图纸",
                Filter = "DWG 图纸 (*.dwg)|*.dwg",
                Multiselect = true,
                CheckFileExists = true
            };
            string initial = UserSettings.GetString("Dxf.SourceDir", "");
            if (Directory.Exists(initial)) dlg.InitialDirectory = initial;
            if (dlg.ShowDialog(this) != true) return;
            AddFiles(dlg.FileNames);
            if (dlg.FileNames.Length > 0)
            {
                string folder = Path.GetDirectoryName(dlg.FileNames[0]);
                if (!string.IsNullOrEmpty(folder)) UserSettings.SetString("Dxf.SourceDir", folder);
                if (string.IsNullOrWhiteSpace(TxtOutput.Text)) TxtOutput.Text = folder ?? string.Empty;
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
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "选择 DXF 输出目录";
                if (Directory.Exists(TxtOutput.Text)) dlg.SelectedPath = TxtOutput.Text;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    TxtOutput.Text = dlg.SelectedPath;
            }
        }

        private void OnOpenFolderClick(object sender, RoutedEventArgs e)
        {
            string dir = TxtOutput.Text == null ? string.Empty : TxtOutput.Text.Trim();
            if (!Directory.Exists(dir))
            {
                TxtLog.Text = "输出目录不存在。";
                return;
            }
            Process.Start("explorer.exe", dir);
        }

        private void OnConvertClick(object sender, RoutedEventArgs e)
        {
            var files = CollectFiles();
            if (files.Count == 0)
            {
                TxtLog.Text = "请先添加 DWG 文件。";
                return;
            }
            string dir = TxtOutput.Text == null ? string.Empty : TxtOutput.Text.Trim();
            if (dir.Length == 0)
            {
                TxtLog.Text = "请指定输出目录。";
                return;
            }
            var choice = CmbVersion.SelectedItem as DxfVersionChoice;
            if (choice == null)
            {
                TxtLog.Text = "当前 AutoCAD 没有可用的 DXF 版本。";
                return;
            }

            SaveSettings();
            BtnRun.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                List<DwgFileResult> results = DwgExchangeService.ConvertToDxf(
                    files, dir, choice.Version, ChkOverwrite.IsChecked == true);
                TxtLog.Text = FormatLog(results);
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

        private void AddFiles(IEnumerable<string> paths)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DwgPathItem item in LstFiles.Items)
                known.Add(item.FullPath);
            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                string full;
                try { full = Path.GetFullPath(path); }
                catch { continue; }
                if (!known.Add(full)) continue;
                LstFiles.Items.Add(new DwgPathItem(full));
            }
        }

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

        private List<string> CollectFiles()
        {
            var files = new List<string>();
            foreach (DwgPathItem item in LstFiles.Items)
                files.Add(item.FullPath);
            return files;
        }

        private void SaveSettings()
        {
            UserSettings.SetString("Dxf.OutputDir", TxtOutput.Text == null ? string.Empty : TxtOutput.Text.Trim());
            var choice = CmbVersion.SelectedItem as DxfVersionChoice;
            if (choice != null) UserSettings.SetString("Dxf.Version", choice.Key);
            UserSettings.SetBool("Dxf.Overwrite", ChkOverwrite.IsChecked == true);
            UserSettings.Save();
        }

        internal static string FormatLog(IList<DwgFileResult> results)
        {
            var sb = new StringBuilder();
            int ok = 0;
            int total = 0;
            if (results != null)
            {
                foreach (DwgFileResult item in results)
                {
                    bool summary = item.Message != null && item.Message.StartsWith("已合并", StringComparison.Ordinal);
                    if (!summary)
                    {
                        total++;
                        if (item.Success) ok++;
                    }
                    string name = string.IsNullOrEmpty(item.SourcePath) ? item.OutputPath : Path.GetFileName(item.SourcePath);
                    sb.Append(item.Success ? "成功  " : "失败  ");
                    if (!string.IsNullOrEmpty(name)) sb.Append(name).Append("  ");
                    sb.Append(item.Message ?? string.Empty);
                    sb.AppendLine();
                }
            }
            sb.Insert(0, string.Format(CultureInfo.InvariantCulture, "完成 {0} / {1}{2}", ok, total, Environment.NewLine));
            return sb.ToString();
        }
    }
}
