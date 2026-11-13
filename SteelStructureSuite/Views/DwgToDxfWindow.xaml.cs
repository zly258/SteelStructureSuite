using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Win32;
using SteelStructureSuite.Services.Drawing;
using SteelStructureSuite.Services.Interaction;
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
        // 保留窗口引用，避免非模态窗口被 GC 回收。
        private static readonly List<WeakReference> _alive = new List<WeakReference>();

        private bool _closed;

        public DwgToDxfWindow()
        {
            InitializeComponent();
            _alive.Add(new WeakReference(this));
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
            UpdateFileCount();
            UserSettings.RestoreWindowState(this, "DwgToDxfWindow");
            Closed += (s, e) =>
            {
                _closed = true;
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
            UpdateFileCount();
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            LstFiles.Items.Clear();
            UpdateFileCount();
        }

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
            TxtLog.Text = "正在转换，请稍候…";
            // 界面状态必须在窗口线程上读取，不能留到后台线程里取。
            bool overwrite = ChkOverwrite.IsChecked == true;
            string versionToken = DxfOutOfProcessConverter.GetVersionToken(choice.Key);

            // 转换全部走独立的核心控制台进程，不再操作当前 CAD 的数据库，
            // 因此可以放在后台线程上跑，CAD 界面不会卡住。
            try
            {
                var worker = new System.Threading.Thread(
                    () => RunConvert(files, dir, versionToken, overwrite));
                worker.IsBackground = true;
                worker.Start();
            }
            catch (Exception ex)
            {
                BtnRun.IsEnabled = true;
                TxtLog.Text = "无法启动转换：" + ex.Message;
            }
        }

        /// <summary>后台线程上的批量转换作业。</summary>
        private void RunConvert(List<string> files, string dir, string versionToken, bool overwrite)
        {
            string log;
            List<DwgFileResult> results = null;
            try
            {
                results = DwgExchangeService.ConvertToDxf(files, dir, versionToken, overwrite, OnProgress);
                log = FormatLog(results);
            }
            catch (Exception ex)
            {
                log = "转换失败：" + ex.Message;
            }

            PostToUi(() =>
            {
                TxtLog.Text = log;
                BtnRun.IsEnabled = true;
            });

            // 命令行汇总必须在 AutoCAD 主线程上打印。
            if (results == null) return;
            try
            {
                MainThreadDispatcher.Post(() => DwgExchangeService.WriteSummary(results));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>把单文件进度回填到日志区。</summary>
        private void OnProgress(string line)
        {
            PostToUi(() =>
            {
                TxtLog.AppendText(line + Environment.NewLine);
                TxtLog.ScrollToEnd();
            });
        }

        private void PostToUi(Action action)
        {
            if (_closed) return;
            try
            {
                Dispatcher.Invoke(action);
            }
            catch (Exception)
            {
                // 窗口已关闭时忽略回填。
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
            UpdateFileCount();
        }

        private void UpdateFileCount()
        {
            TxtFileCount.Text = LstFiles.Items.Count.ToString(CultureInfo.CurrentCulture);
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
            int skipped = 0;
            int failed = 0;
            int total = 0;
            if (results != null)
            {
                foreach (DwgFileResult item in results)
                {
                    bool summary = item.Message != null && item.Message.StartsWith("已合并", StringComparison.Ordinal);
                    if (!summary)
                    {
                        total++;
                        if (item.Skipped) skipped++;
                        else if (item.Success) ok++;
                        else failed++;
                    }
                    string name = string.IsNullOrEmpty(item.SourcePath) ? item.OutputPath : Path.GetFileName(item.SourcePath);
                    sb.Append(item.Skipped ? "跳过  " : item.Success ? "成功  " : "失败  ");
                    if (!string.IsNullOrEmpty(name)) sb.Append(name).Append("  ");
                    sb.Append(item.Message ?? string.Empty);
                    sb.AppendLine();
                }
            }
            sb.Insert(0, string.Format(
                CultureInfo.InvariantCulture,
                "完成 成功 {0} / 跳过 {1} / 失败 {2}（共 {3}）{4}",
                ok,
                skipped,
                failed,
                total,
                Environment.NewLine));
            return sb.ToString();
        }
    }
}
