using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Microsoft.Win32;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Drawing;
using SteelStructureSuite.Services.Settings;

namespace SteelStructureSuite.Views
{
    /// <summary>
    /// 批量出详图窗口：列出待出图构件、按复用规则估算张数、带进度与取消。
    /// </summary>
    public partial class BatchDrawingWindow : Window
    {
        private static readonly double[] ScaleValues =
        { 1.0, 2.0, 5.0, 10.0, 15.0, 20.0, 25.0, 30.0, 40.0, 50.0, 75.0, 100.0, 150.0, 200.0 };

        private readonly List<SteelStructureParams> _structures;
        private BatchDrawingPlan _plan;
        private bool _running;
        private volatile bool _cancelRequested;

        /// <summary>本次批量出图的结果，供调用方汇总。</summary>
        public BatchDrawingResult Result { get; private set; }

        public BatchDrawingWindow(IList<SteelStructureParams> structures)
        {
            InitializeComponent();

            _structures = structures?.Where(s => s != null).ToList() ?? new List<SteelStructureParams>();

            CmbSheetSize.Items.Add("A1 标准图框 (841×594)");
            CmbSheetSize.Items.Add("A2 标准图框 (594×420)");
            CmbSheetSize.Items.Add("A3 标准图框 (420×297)");
            CmbSheetSize.Items.Add("A4 标准图框 (297×210)");

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

            CmbOutput.Items.Add("当前 DWG（在空白处按网格排布）");
            CmbOutput.Items.Add("新建 DWG 文件");

            RestoreSettings();
            RebuildPlan();
        }

        private void RestoreSettings()
        {
            int sheetIdx = UserSettings.GetInt("BatchDrawing.SheetIndex", 1);
            if (sheetIdx >= 0 && sheetIdx < CmbSheetSize.Items.Count) CmbSheetSize.SelectedIndex = sheetIdx;
            else CmbSheetSize.SelectedIndex = 1;

            int scaleIdx = UserSettings.GetInt("BatchDrawing.ScaleIndex", 0);
            if (scaleIdx >= 0 && scaleIdx < CmbScale.Items.Count) CmbScale.SelectedIndex = scaleIdx;
            else CmbScale.SelectedIndex = 0;

            int outIdx = UserSettings.GetInt("BatchDrawing.OutputIndex", 0);
            if (outIdx >= 0 && outIdx < CmbOutput.Items.Count) CmbOutput.SelectedIndex = outIdx;
            else CmbOutput.SelectedIndex = 0;

            ChkReuse.IsChecked = UserSettings.GetBool("BatchDrawing.Reuse", true);
            ChkIndex.IsChecked = UserSettings.GetBool("BatchDrawing.Index", true);
            ChkSummaryBom.IsChecked = UserSettings.GetBool("BatchDrawing.SummaryBom", true);

            string lastPath = UserSettings.GetString("BatchDrawing.LastPath", string.Empty);
            TxtOutputPath.Text = lastPath;

            UpdateOutputState();
        }

        private void SaveSettings()
        {
            UserSettings.SetInt("BatchDrawing.SheetIndex", CmbSheetSize.SelectedIndex);
            UserSettings.SetInt("BatchDrawing.ScaleIndex", CmbScale.SelectedIndex);
            UserSettings.SetInt("BatchDrawing.OutputIndex", CmbOutput.SelectedIndex);
            UserSettings.SetBool("BatchDrawing.Reuse", ChkReuse.IsChecked == true);
            UserSettings.SetBool("BatchDrawing.Index", ChkIndex.IsChecked == true);
            UserSettings.SetBool("BatchDrawing.SummaryBom", ChkSummaryBom.IsChecked == true);
            UserSettings.SetString("BatchDrawing.LastPath", TxtOutputPath.Text ?? string.Empty);
            UserSettings.Save();
        }

        private void RebuildPlan()
        {
            bool reuse = ChkReuse.IsChecked == true;
            _plan = BatchDrawingPlan.Build(_structures, reuse);

            TxtSummary.Text = string.Format(
                CultureInfo.CurrentCulture,
                "共 {0} 个构件，{1} 种规格 → 将生成 {2} 张详图{3}",
                _plan.TotalStructureCount,
                _plan.SheetCount,
                _plan.SheetCount,
                _plan.SavedSheetCount > 0 ? $"（复用省下 {_plan.SavedSheetCount} 张）" : string.Empty);

            TxtReuseHint.Text = reuse
                ? "几何、截面、材质完全相同的构件共用一张图，图名注明件数与位号。"
                : "每个构件各出一张图，即使完全相同也不合并。";
        }

        private void OnReuseChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            RebuildPlan();
        }

        private void OnOutputChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateOutputState();
        }

        private void UpdateOutputState()
        {
            bool toFile = CmbOutput.SelectedIndex == 1;
            if (TxtOutputPath != null) TxtOutputPath.IsEnabled = toFile;
            if (BtnBrowse != null) BtnBrowse.IsEnabled = toFile;

            if (toFile && string.IsNullOrWhiteSpace(TxtOutputPath.Text))
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                TxtOutputPath.Text = Path.Combine(dir, $"钢结构详图_{stamp}.dwg");
            }
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "批量详图输出文件",
                Filter = "AutoCAD 图纸 (*.dwg)|*.dwg",
                DefaultExt = ".dwg",
                FileName = string.IsNullOrWhiteSpace(TxtOutputPath.Text)
                    ? "钢结构详图.dwg"
                    : Path.GetFileName(TxtOutputPath.Text),
                InitialDirectory = SafeDirectory(TxtOutputPath.Text)
            };

            if (dlg.ShowDialog(this) == true)
                TxtOutputPath.Text = dlg.FileName;
        }

        private static string SafeDirectory(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path ?? string.Empty);
                return !string.IsNullOrEmpty(dir) && Directory.Exists(dir)
                    ? dir
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            catch
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
        }

        private DrawingGenerationOptions BuildOptions()
        {
            var options = new DrawingGenerationOptions
            {
                SheetSizeName = new[] { "A1", "A2", "A3", "A4" }[
                    Math.Max(0, Math.Min(3, CmbSheetSize.SelectedIndex))],
                IncludeJointDetailViews = false,
                ReuseIdenticalStructures = ChkReuse.IsChecked == true,
                IncludeDrawingIndex = ChkIndex.IsChecked == true,
                IncludeSummaryBom = ChkSummaryBom.IsChecked == true
            };

            int scaleIdx = CmbScale.SelectedIndex;
            options.AutoScale = scaleIdx <= 0;
            options.DrawingScale = (scaleIdx >= 1 && scaleIdx - 1 < ScaleValues.Length)
                ? ScaleValues[scaleIdx - 1]
                : 20.0;

            options.OutputToNewDwgFile = CmbOutput.SelectedIndex == 1;
            options.NewDwgFilePath = TxtOutputPath.Text?.Trim() ?? string.Empty;
            return options;
        }

        private void OnStartClick(object sender, RoutedEventArgs e)
        {
            if (_running) return;
            if (_plan == null || _plan.SheetCount == 0)
            {
                MessageBox.Show(this, "没有可出图的构件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DrawingGenerationOptions options = BuildOptions();
            if (options.OutputToNewDwgFile && string.IsNullOrWhiteSpace(options.NewDwgFilePath))
            {
                MessageBox.Show(this, "请先指定输出文件路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveSettings();

            _running = true;
            _cancelRequested = false;
            BtnStart.IsEnabled = false;
            BtnCancel.Content = "取消生成";
            PrgBar.Value = 0;
            TxtLog.Clear();
            AppendLog($"开始批量出图：{_plan.TotalStructureCount} 个构件，{_plan.SheetCount} 张图。");

            try
            {
                if (options.OutputToNewDwgFile)
                {
                    Result = SteelHlrDrawingService.NormalizeDwgPath(options.NewDwgFilePath) == null
                        ? null
                        : RunToNewFile(_plan, options);
                }
                else
                {
                    Result = RunToCurrentDwg(_plan, options);
                }
            }
            catch (Exception ex)
            {
                AppendLog("出图失败：" + ex.Message);
                TxtStatus.Text = "出图失败";
                MessageBox.Show(this, "批量出图失败：\n" + ex.Message, "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _running = false;
                BtnStart.IsEnabled = true;
                BtnCancel.Content = "关闭";

                if (Result != null)
                {
                    PrgBar.Value = Result.Cancelled ? PrgBar.Value : 100;
                    TxtStatus.Text = Result.Cancelled
                        ? $"已取消，已生成 {Result.SheetCount} 张图。"
                        : $"完成：{Result.SheetCount} 张图，共 {Result.StructureCount} 个构件。";
                    AppendLog(TxtStatus.Text);
                    if (!string.IsNullOrEmpty(Result.OutputPath))
                        AppendLog("输出文件：" + Result.OutputPath);
                    foreach (string w in Result.Warnings)
                        AppendLog("警告：" + w);
                }
            }
        }

        private BatchDrawingResult RunToNewFile(BatchDrawingPlan plan, DrawingGenerationOptions options)
        {
            var result = new BatchDrawingResult();
            using (Database newDb = new Database(true, true))
            using (WorkingDatabaseScope.Use(newDb))
            {
                result = BatchDrawingService.GenerateBatch(
                    newDb, plan, Point3d.Origin, options,
                    OnProgress, () => _cancelRequested);

                if (result.SheetCount > 0)
                {
                    string path = SteelHlrDrawingService.NormalizeDwgPath(options.NewDwgFilePath);
                    SteelHlrDrawingService.SaveDatabaseToDwg(newDb, path);
                    result.OutputPath = path;
                }
            }
            return result;
        }

        private BatchDrawingResult RunToCurrentDwg(BatchDrawingPlan plan, DrawingGenerationOptions options)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("当前没有打开的图纸。");

            // 隐藏弹窗，让用户在当前图纸上交互指定排布起点（横向向右排列，互不重叠）
            Point3d origin;
            bool picked = false;
            this.Hide();
            try
            {
                Editor ed = doc.Editor;
                var ppo = new Autodesk.AutoCAD.EditorInput.PromptPointOptions(
                    "\n请指定批量出图的左下角起点（图框将从该点起横向向右排列，互不重叠）: ")
                {
                    AllowNone = false
                };
                Autodesk.AutoCAD.EditorInput.PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status == Autodesk.AutoCAD.EditorInput.PromptStatus.OK)
                {
                    origin = ppr.Value;
                    picked = true;
                }
                else
                {
                    origin = Point3d.Origin;
                }
            }
            finally
            {
                this.Show();
            }

            if (!picked)
                return new BatchDrawingResult { Cancelled = true };

            using (DocumentLock dl = doc.LockDocument())
            {
                return BatchDrawingService.GenerateBatch(
                    doc.Database, plan, origin, options,
                    OnProgress, () => _cancelRequested);
            }
        }

        /// <summary>取当前视口中心作为排布起点，避免图纸生成在离视口很远的地方。</summary>
        private static Point3d GetCurrentViewOrigin(Document doc)
        {
            try
            {
                using (Autodesk.AutoCAD.DatabaseServices.ViewTable vt =
                    (Autodesk.AutoCAD.DatabaseServices.ViewTable)doc.Database.ViewTableId.GetObject(OpenMode.ForRead))
                {
                    // 优先用当前视口（ViewportTable）的中心
                    using (Autodesk.AutoCAD.DatabaseServices.ViewportTable vpt =
                        (Autodesk.AutoCAD.DatabaseServices.ViewportTable)doc.Database.ViewportTableId.GetObject(OpenMode.ForRead))
                    {
                        foreach (ObjectId id in vpt)
                        {
                            var vp = (Autodesk.AutoCAD.DatabaseServices.Viewport)id.GetObject(OpenMode.ForRead);
                            if (vp.Number == 1 && !vp.On) continue;
                            if (vp.Number == 1)
                                return new Point3d(vp.CenterPoint.X, vp.CenterPoint.Y, 0);
                        }
                    }
                }
            }
            catch
            {
                // 取不到视口时退回原点
            }

            return Point3d.Origin;
        }

        private void OnProgress(BatchDrawingProgress p)
        {
            if (p == null) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<BatchDrawingProgress>(OnProgress), p);
                return;
            }

            PrgBar.Value = Math.Max(0, Math.Min(100, p.Percent));
            TxtProgress.Text = $"{p.Completed}/{p.Total}（{p.Percent}%）　{p.Message}";
            AppendLog(p.Message);
        }

        private void AppendLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
            TxtLog.ScrollToEnd();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            if (_running)
            {
                _cancelRequested = true;
                BtnCancel.IsEnabled = false;
                TxtStatus.Text = "正在取消，等待当前图纸完成...";
                AppendLog("用户请求取消，将在当前这张图完成后停止。");
                // 取消后按钮恢复为可用，避免卡死
                Dispatcher.BeginInvoke(new Action(() => BtnCancel.IsEnabled = true));
                return;
            }

            SaveSettings();
            Close();
        }
    }
}
