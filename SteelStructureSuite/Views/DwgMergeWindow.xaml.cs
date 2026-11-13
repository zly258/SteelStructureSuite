using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using Autodesk.AutoCAD.ApplicationServices;
using Microsoft.Win32;
using SteelStructureSuite.Services.Drawing;
using SteelStructureSuite.Services.Interaction;
using SteelStructureSuite.Services.Settings;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SteelStructureSuite.Views
{
    public partial class DwgMergeWindow : Window
    {
        // 保留窗口引用，避免非模态窗口被 GC 回收。
        private static readonly List<WeakReference> _alive = new List<WeakReference>();

        private bool _closed;
        private bool _running;

        public string SavedPath { get; private set; }
        public bool OpenWhenClosed { get; private set; }

        public DwgMergeWindow()
        {
            InitializeComponent();
            _alive.Add(new WeakReference(this));

            bool vertical = UserSettings.GetString("Merge.Direction", "H") == "V";
            RadVertical.IsChecked = vertical;
            RadHorizontal.IsChecked = !vertical;
            TxtGap.Text = UserSettings.GetDouble("Merge.Gap", 0).ToString("0.###", CultureInfo.CurrentCulture);
            ChkExplode.IsChecked = UserSettings.GetBool("Merge.Explode", false);
            // 用新的设置键，避开旧版本存下的「默认打开」，
            // 合并结果是否自动打开现在默认关闭。
            ChkOpen.IsChecked = UserSettings.GetBool("Merge.OpenAfterV2", false);
            TxtOutput.Text = UserSettings.GetString("Merge.OutputPath", "");
            UpdateFileCount();

            UserSettings.RestoreWindowState(this, "DwgMergeWindow");
            Closed += (s, e) =>
            {
                _closed = true;
                SaveSettings();
                UserSettings.SaveWindowState(this, "DwgMergeWindow");
            };
        }

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择要合并的图纸（DWG 或 DXF）",
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
            }
            UpdateFileCount();
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

        /// <summary>
        /// 只收集参数，不在窗口线程上碰 AutoCAD 数据库。
        /// 合并动作交给 AutoCAD 主线程执行（数据库对象只能在主线程创建与释放）。
        /// </summary>
        private void OnMergeClick(object sender, RoutedEventArgs e)
        {
            if (_running) return;

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
                TxtLog.Text = "请指定输出文件名，例如「合并图纸.dwg」或「合并图纸.dxf」。";
                return;
            }
            string ext = Path.GetExtension(output);
            if (!ext.Equals(".dwg", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".dxf", StringComparison.OrdinalIgnoreCase))
            {
                TxtLog.Text = "输出文件必须是 .dwg 或 .dxf 格式，请显式指定扩展名。";
                return;
            }
            // 只给文件名时，落到第一个源文件所在目录。
            if (string.IsNullOrEmpty(Path.GetDirectoryName(output)))
            {
                string sourceDir = Path.GetDirectoryName(files[0]);
                if (!string.IsNullOrEmpty(sourceDir)) output = Path.Combine(sourceDir, output);
            }
            if (File.Exists(output))
            {
                MessageBoxResult answer = MessageBox.Show(this, "输出文件已存在，是否覆盖？", "合并图纸", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
            }

            SaveSettings();

            var direction = RadVertical.IsChecked == true ? DwgMergeDirection.Vertical : DwgMergeDirection.Horizontal;
            bool explode = ChkExplode.IsChecked == true;
            bool openAfter = ChkOpen.IsChecked == true;

            _running = true;
            BtnRun.IsEnabled = false;
            TxtLog.Text = "正在合并，请稍候…";

            try
            {
                MainThreadDispatcher.Post(() => RunMerge(files, output, direction, gap, explode, openAfter));
            }
            catch (System.Exception ex)
            {
                _running = false;
                BtnRun.IsEnabled = true;
                TxtLog.Text = "无法启动合并：" + ex.Message;
            }
        }

        /// <summary>运行在 AutoCAD 主线程上的合并作业。</summary>
        private void RunMerge(
            List<string> files,
            string output,
            DwgMergeDirection direction,
            double gap,
            bool explode,
            bool openAfter)
        {
            string log;
            bool merged = false;
            try
            {
                List<DwgFileResult> results = DwgExchangeService.MergeDrawings(
                    files, output, direction, gap, explode);
                log = DwgToDxfWindow.FormatLog(results);
                foreach (DwgFileResult item in results)
                {
                    if (item.Success && item.Message != null && item.Message.StartsWith("已合并", StringComparison.Ordinal))
                    {
                        merged = true;
                        SavedPath = output;
                        OpenWhenClosed = openAfter;
                    }
                }
            }
            catch (System.Exception ex)
            {
                log = "合并失败：" + ex.Message;
            }

            PostToUi(() =>
            {
                TxtLog.Text = log;
                BtnRun.IsEnabled = true;
                _running = false;
            });

            if (!merged || !openAfter) return;
            if (!File.Exists(output)) return;
            var info = new FileInfo(output);
            if (!info.Exists || info.Length <= 0) return;

            try
            {
                Application.DocumentManager.Open(output, false);
            }
            catch (System.Exception ex)
            {
                try
                {
                    Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                        "\n[SteelStructureSuite] 合并完成，但打开输出图纸失败：" + ex.Message + "\n");
                }
                catch (System.Exception)
                {
                }
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

        private void UpdateFileCount()
        {
            TxtFileCount.Text = LstFiles.Items.Count.ToString(CultureInfo.CurrentCulture);
        }

        private void PostToUi(Action action)
        {
            if (_closed) return;
            try
            {
                Dispatcher.Invoke(action);
            }
            catch (System.Exception)
            {
                // 窗口已关闭时忽略回填。
            }
        }

        private void SaveSettings()
        {
            UserSettings.SetString("Merge.Direction", RadVertical.IsChecked == true ? "V" : "H");
            double gap;
            if (TryParseGap(TxtGap.Text, out gap)) UserSettings.SetDouble("Merge.Gap", gap);
            UserSettings.SetBool("Merge.Explode", ChkExplode.IsChecked == true);
            UserSettings.SetBool("Merge.OpenAfterV2", ChkOpen.IsChecked == true);
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
