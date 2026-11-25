using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Microsoft.Win32;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Settings;
using SteelStructureSuite.Services.Takeoff;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SteelStructureSuite.Views
{
    public partial class MaterialTakeoffWindow : Window
    {
        private readonly Database _db;
        private List<MemberPartBillItem> _detailedItems = new List<MemberPartBillItem>();
        private List<SectionSummaryRow> _summaryRows = new List<SectionSummaryRow>();

        public bool RequestInsertCadTable => !string.IsNullOrEmpty(RequestInsertTableType);
        public string RequestInsertTableType { get; private set; }
        public List<SectionSummaryRow> ResultSummaryRows => _summaryRows;
        public List<MemberPartBillItem> ResultDetailedItems => _detailedItems;

        public MaterialTakeoffWindow(Database db)
        {
            InitializeComponent();
            Width = Math.Max(Width, 1040);
            Height = Math.Max(Height, 600);
            MinWidth = 880;
            MinHeight = 420;
            foreach (var dg in new[] { DgSummary, DgDetails })
            {
                if (dg == null) continue;
                dg.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                dg.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                foreach (var col in dg.Columns)
                {
                    if (col.MinWidth < 55) col.MinWidth = 55;
                }
            }
            _db = db;
            ReloadData(null);

            UserSettings.RestoreWindowState(this, "MaterialTakeoffWindow");
            Closed += (s, e) => UserSettings.SaveWindowState(this, "MaterialTakeoffWindow");
        }

        /// <summary>在顶部横幅就地提示，替代一次性弹窗。</summary>
        private void SetBanner(string text)
        {
            if (TxtBanner != null) TxtBanner.Text = text ?? string.Empty;
        }

        private void ReloadData(IEnumerable<ObjectId> filterIds)
        {
            var structures = MaterialTakeoffService.CollectStructures(_db, filterIds);
            _detailedItems = MaterialTakeoffService.BuildDetailedPartList(structures);
            _summaryRows = MaterialTakeoffService.BuildSectionSummary(_detailedItems);

            DgSummary.ItemsSource = null;
            DgSummary.ItemsSource = _summaryRows;

            DgDetails.ItemsSource = null;
            DgDetails.ItemsSource = _detailedItems;

            double totalKg = _summaryRows.Sum(r => r.TotalWeightKg);
            double totalM2 = _summaryRows.Sum(r => r.TotalSurfaceAreaM2);
            TxtBanner.Text =
                $"共 {structures.Count} 座构件，{_summaryRows.Count} 种规格  |  总重: {totalKg:0.00} kg ({totalKg / 1000.0:0.000} t)  |  面积: {totalM2:0.00} m²";
        }

        private void OnAllScopeClick(object sender, RoutedEventArgs e)
        {
            ReloadData(null);
        }

        private void OnPickSelectionClick(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Hide();
            try
            {
                PromptSelectionOptions pso = new PromptSelectionOptions
                {
                    MessageForAdding = "\n请框选要统计材料的钢结构三维实体: "
                };
                PromptSelectionResult psr = doc.Editor.GetSelection(pso);
                if (psr.Status == PromptStatus.OK && psr.Value != null)
                {
                    ReloadData(psr.Value.GetObjectIds());
                }
            }
            finally
            {
                Show();
                Activate();
            }
        }

        private void OnExportExcelClick(object sender, RoutedEventArgs e)
        {
            if (_summaryRows.Count == 0)
            {
                SetBanner("当前没有可导出的材料数据");
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog
            {
                Title = "导出材料统计表 (.xlsx)",
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                FileName = $"钢结构材料统计表_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    MaterialTakeoffService.ExportToExcelXlsx(sfd.FileName, _detailedItems, _summaryRows);
                    SetBanner("已导出 Excel：" + System.IO.Path.GetFileName(sfd.FileName));
                }
                catch (Exception ex)
                {
                    SetBanner("导出失败：" + ex.Message);
                }
            }
        }

        private void OnInsertSummaryClick(object sender, RoutedEventArgs e)
        {
            RequestInsertTableType = "summary";
            DialogResult = true;
            Close();
        }

        private void OnInsertDetailClick(object sender, RoutedEventArgs e)
        {
            RequestInsertTableType = "detail";
            DialogResult = true;
            Close();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            RequestInsertTableType = null;
            DialogResult = false;
            Close();
        }
    }
}

