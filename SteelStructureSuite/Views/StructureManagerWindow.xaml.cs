using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Drawing;
using SteelStructureSuite.Services.Modeling;
using SteelStructureSuite.Services.Settings;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SteelStructureSuite.Views
{
    public partial class StructureManagerWindow : Window
    {
        private List<StructureRecordItem> _allRecords = new List<StructureRecordItem>();
        private ObjectId _lastHighlightedId = ObjectId.Null;

        public StructureManagerWindow()
        {
            InitializeComponent();
            Width = Math.Max(Width, 960);
            Height = Math.Max(Height, 560);
            MinWidth = 820;
            MinHeight = 420;
            if (DgStructures != null)
            {
                DgStructures.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                DgStructures.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                foreach (var col in DgStructures.Columns)
                {
                    if (col.MinWidth < 65) col.MinWidth = 65;
                }
            }
            ReloadAllStructures();

            UserSettings.RestoreWindowState(this, "StructureManagerWindow");
            Closed += (s, e) => UserSettings.SaveWindowState(this, "StructureManagerWindow");
        }

        private void ReloadAllStructures()
        {
            _allRecords.Clear();
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity ent &&
                        SteelDataStorage.TryReadParams(ent, out SteelStructureParams p))
                    {
                        var bill = SteelStructureBuilder.CalculatePartBillItems(p);
                        double kg = bill.Sum(x => x.TotalWeightKg);
                        _allRecords.Add(new StructureRecordItem
                        {
                            EntityId = id,
                            Params = p,
                            WeightKg = Math.Round(kg, 2)
                        });
                    }
                }
                tr.Commit();
            }

            _allRecords = _allRecords
                .OrderBy(r => r.Params.CategoryOrder)
                .ThenBy(r => r.Params.StructureType)
                .ThenBy(r => r.Tag)
                .ToList();
            RebuildTree();
            DgStructures.ItemsSource = _allRecords;

            double totalWeight = _allRecords.Sum(x => x.WeightKg);
            TxtStatus.Text = $"共 {_allRecords.Count} 座构件  |  总重: {totalWeight:0.00} kg";
        }

        private void RebuildTree()
        {
            TreeStructures.Items.Clear();

            TreeViewItem rootNode = new TreeViewItem
            {
                Header = $"全部钢结构 ({_allRecords.Count})",
                Tag = "ALL",
                IsExpanded = true
            };

            foreach (var cat in _allRecords.GroupBy(r => r.Params.CategoryName))
            {
                TreeViewItem catNode = new TreeViewItem
                {
                    Header = $"{cat.Key} ({cat.Count()})",
                    Tag = cat.Key,
                    IsExpanded = true
                };
                foreach (var grp in cat.GroupBy(r => r.Params.StructureType))
                {
                    string typeName = grp.First().TypeName;
                    TreeViewItem typeNode = new TreeViewItem
                    {
                        Header = $"{typeName} ({grp.Count()})",
                        Tag = grp.Key,
                        IsExpanded = true
                    };
                    foreach (var item in grp)
                    {
                        typeNode.Items.Add(new TreeViewItem
                        {
                            Header = $"{item.Tag} [{item.MainSection}]",
                            Tag = item
                        });
                    }
                    catNode.Items.Add(typeNode);
                }
                rootNode.Items.Add(catNode);
            }

            TreeStructures.Items.Add(rootNode);
        }

        private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (!(TreeStructures.SelectedItem is TreeViewItem node)) return;

            if (node.Tag is string s && s == "ALL")
            {
                DgStructures.ItemsSource = _allRecords;
            }
            else if (node.Tag is string category)
            {
                DgStructures.ItemsSource = _allRecords.Where(r => r.Params.CategoryName == category).ToList();
            }
            else if (node.Tag is ParametricStructureType st)
            {
                DgStructures.ItemsSource = _allRecords.Where(r => r.Params.StructureType == st).ToList();
            }
            else if (node.Tag is StructureRecordItem rec)
            {
                var singleTypeList = _allRecords.Where(r => r.Params.StructureType == rec.Params.StructureType).ToList();
                DgStructures.ItemsSource = singleTypeList;
                DgStructures.SelectedItem = rec;
                DgStructures.ScrollIntoView(rec);
            }
        }

        private StructureRecordItem GetSelectedRecord()
        {
            if (DgStructures.SelectedItem is StructureRecordItem r) return r;
            if (TreeStructures.SelectedItem is TreeViewItem node && node.Tag is StructureRecordItem tr) return tr;
            return null;
        }

        private void OnLocateAndHighlightClick(object sender, RoutedEventArgs e)
        {
            StructureRecordItem rec = GetSelectedRecord();
            if (rec == null)
            {
                MessageBox.Show("请先选择构件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            try
            {
                using (DocumentLock dl = doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    if (!_lastHighlightedId.IsNull && _lastHighlightedId.IsValid && !_lastHighlightedId.IsErased)
                    {
                        if (tr.GetObject(_lastHighlightedId, OpenMode.ForRead) is Entity prevEnt)
                        {
                            try { prevEnt.Unhighlight(); } catch { }
                        }
                    }

                    if (tr.GetObject(rec.EntityId, OpenMode.ForRead) is Entity ent)
                    {
                        Extents3d ext = ent.GeometricExtents;
                        ZoomViewportToExtents(ed, ext, 1.8);
                        ent.Highlight();
                        _lastHighlightedId = rec.EntityId;
                    }
                    tr.Commit();
                }

                ed.SetImpliedSelection(new ObjectId[] { rec.EntityId });
                ed.UpdateScreen();
                TxtStatus.Text = $"已定位并高亮: [{rec.Tag}] {rec.TypeName} {rec.PositionStr}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"定位失败: {ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void ZoomViewportToExtents(Editor ed, Extents3d ext, double marginFactor)
        {
            Point3d minPt = ext.MinPoint;
            Point3d maxPt = ext.MaxPoint;

            using (ViewTableRecord view = ed.GetCurrentView())
            {
                Matrix3d worldToEye = Matrix3d.WorldToPlane(view.ViewDirection) *
                                      Matrix3d.Displacement(Point3d.Origin - view.Target) *
                                      Matrix3d.Rotation(view.ViewTwist, view.ViewDirection, view.Target);

                Point3d eyeMin = minPt.TransformBy(worldToEye);
                Point3d eyeMax = maxPt.TransformBy(worldToEye);

                double w = Math.Max(Math.Abs(eyeMax.X - eyeMin.X), 1200.0) * marginFactor;
                double h = Math.Max(Math.Abs(eyeMax.Y - eyeMin.Y), 1200.0) * marginFactor;

                view.CenterPoint = new Point2d(
                    (eyeMin.X + eyeMax.X) * 0.5,
                    (eyeMin.Y + eyeMax.Y) * 0.5);
                view.Width = w;
                view.Height = h;
                ed.SetCurrentView(view);
            }
        }

        private void OnEditSelectedClick(object sender, RoutedEventArgs e)
        {
            StructureRecordItem rec = GetSelectedRecord();
            if (rec == null)
            {
                MessageBox.Show("请先选择要编辑的构件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var editWin = new StructureEditWindow(rec.Params, isEditMode: true, lockStructureType: true);
            if (editWin.ShowDialog() == true)
            {
                using (DocumentLock dl = doc.LockDocument())
                {
                    SteelStructureBuilder.UpdateStructureInDatabase(doc.Database, rec.EntityId, editWin.Data);
                }
                doc.Editor.Regen();
                ReloadAllStructures();
            }
        }

        private void OnDrawSelectedClick(object sender, RoutedEventArgs e)
        {
            StructureRecordItem rec = GetSelectedRecord();
            if (rec == null)
            {
                MessageBox.Show("请先选择要出图的构件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var cfgWin = new DrawingConfigWindow(rec.Params);
            if (cfgWin.ShowDialog() != true) return;

            SteelStructureParams parms = rec.Params.Clone();
            DrawingGenerationOptions options = cfgWin.Options;
            if (options.OutputToNewDwgFile)
            {
                string savePath = SteelHlrDrawingService.NormalizeDwgPath(options.NewDwgFilePath);
                RunInCommandContext(doc, () =>
                {
                    SteelHlrDrawingService.GenerateDetailDrawingToNewDwgFile(parms, savePath, options);
                    if (options.OpenNewDwgAfterSave && File.Exists(savePath))
                        Application.DocumentManager.Open(savePath, false);
                    MessageBox.Show($"已生成新 DWG 详图：\n{savePath}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            else
            {
                Hide();
                PromptPointResult ppr;
                try
                {
                    ppr = doc.Editor.GetPoint("\n请拾取详图图框插入点: ");
                }
                finally
                {
                    Show();
                    Activate();
                }
                if (ppr == null || ppr.Status != PromptStatus.OK) return;
                Point3d insertPt = ppr.Value;
                RunInCommandContext(doc, () =>
                {
                    SteelHlrDrawingService.GenerateDetailDrawingSheet(doc.Database, parms, insertPt, options);
                    doc.Editor.WriteMessage("\n[SteelStructureSuite] " + "详图已写入当前图。\n");
                });
            }
        }

        private static void RunInCommandContext(Document doc, Action action)
        {
            if (doc != null && !string.IsNullOrEmpty(doc.CommandInProgress))
            {
                action();
                return;
            }

            Application.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                try
                {
                    action();
                }
                catch (System.Exception ex)
                {
                    try
                    {
                        doc.Editor.WriteMessage("\n[SteelStructureSuite] " + ex.Message + "\n");
                    }
                    catch
                    {
                    }
                    MessageBox.Show(ex.Message, "出图失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return System.Threading.Tasks.Task.CompletedTask;
            }, null);
        }

        private void OnDeleteSelectedClick(object sender, RoutedEventArgs e)
        {
            StructureRecordItem rec = GetSelectedRecord();
            if (rec == null) return;

            if (MessageBox.Show($"确定删除构件 [{rec.Tag}] ({rec.TypeName}) 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (DocumentLock dl = doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(rec.EntityId, OpenMode.ForWrite) is Entity ent)
                {
                    ent.Erase();
                }
                tr.Commit();
            }

            doc.Editor.Regen();
            ReloadAllStructures();
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            ReloadAllStructures();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

