using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Services.Modeling;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(SteelStructureSuite.Commands.LayoutCommands))]

namespace SteelStructureSuite.Commands
{
    public class LayoutCommands
    {
        [CommandMethod("SS_ALIGN", CommandFlags.Modal)]
        public void AlignSteelStructures()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptSelectionOptions pso = new PromptSelectionOptions
            {
                MessageForAdding = "\n请框选需要对齐的多个钢结构三维实体: "
            };
            PromptSelectionResult psr = ed.GetSelection(pso);
            if (psr.Status != PromptStatus.OK || psr.Value.Count < 2)
            {
                ed.WriteMessage("\n请至少选择 2 个钢结构构件进行对齐。\n");
                return;
            }

            PromptKeywordOptions pko = new PromptKeywordOptions(
                "\n请选择对齐基准 [左XMin(XMIN)/右XMax(XMAX)/前YMin(YMIN)/后YMax(YMAX)/底ZMin(ZMIN)/顶ZMax(ZMAX)/中心(CENTER)] <ZMIN>: ",
                "XMIN XMAX YMIN YMAX ZMIN ZMAX CENTER");
            pko.Keywords.Default = "ZMIN";
            PromptResult pkr = ed.GetKeywords(pko);
            if (pkr.Status != PromptStatus.OK) return;
            string mode = pkr.StringResult;

            using (DocumentLock dl = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId[] ids = psr.Value.GetObjectIds();
                Extents3d refExt = ((Entity)tr.GetObject(ids[0], OpenMode.ForRead)).GeometricExtents;

                for (int i = 1; i < ids.Length; i++)
                {
                    if (!(tr.GetObject(ids[i], OpenMode.ForWrite) is Entity ent)) continue;
                    Extents3d curExt = ent.GeometricExtents;
                    Vector3d disp = new Vector3d(0, 0, 0);

                    switch (mode)
                    {
                        case "XMIN": disp = new Vector3d(refExt.MinPoint.X - curExt.MinPoint.X, 0, 0); break;
                        case "XMAX": disp = new Vector3d(refExt.MaxPoint.X - curExt.MaxPoint.X, 0, 0); break;
                        case "YMIN": disp = new Vector3d(0, refExt.MinPoint.Y - curExt.MinPoint.Y, 0); break;
                        case "YMAX": disp = new Vector3d(0, refExt.MaxPoint.Y - curExt.MaxPoint.Y, 0); break;
                        case "ZMIN": disp = new Vector3d(0, 0, refExt.MinPoint.Z - curExt.MinPoint.Z); break;
                        case "ZMAX": disp = new Vector3d(0, 0, refExt.MaxPoint.Z - curExt.MaxPoint.Z); break;
                        case "CENTER":
                            Point3d refCenter = new Point3d(
                                (refExt.MinPoint.X + refExt.MaxPoint.X) * 0.5,
                                (refExt.MinPoint.Y + refExt.MaxPoint.Y) * 0.5,
                                (refExt.MinPoint.Z + refExt.MaxPoint.Z) * 0.5);
                            Point3d curCenter = new Point3d(
                                (curExt.MinPoint.X + curExt.MaxPoint.X) * 0.5,
                                (curExt.MinPoint.Y + curExt.MaxPoint.Y) * 0.5,
                                (curExt.MinPoint.Z + curExt.MaxPoint.Z) * 0.5);
                            disp = refCenter - curCenter;
                            break;
                    }

                    ent.TransformBy(Matrix3d.Displacement(disp));
                    if (SteelDataStorage.TryReadParams(ent, out SteelStructureParams p))
                    {
                        p.Origin = p.Origin + disp;
                        SteelDataStorage.WriteParams(tr, ent, p);
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\n[SteelStructureSuite] 已完成 {psr.Value.Count} 个钢结构构件的 [{mode}] 对齐。\n");
        }

        [CommandMethod("SS_DROPZ0", CommandFlags.Modal)]
        public void DropStructuresToZeroElevation()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptSelectionResult psr = ed.GetSelection();
            if (psr.Status != PromptStatus.OK) return;

            int count = 0;
            using (DocumentLock dl = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in psr.Value.GetObjectIds())
                {
                    if (!(tr.GetObject(id, OpenMode.ForWrite) is Entity ent)) continue;
                    Extents3d ext = ent.GeometricExtents;
                    Vector3d dz = new Vector3d(0, 0, -ext.MinPoint.Z);
                    ent.TransformBy(Matrix3d.Displacement(dz));

                    if (SteelDataStorage.TryReadParams(ent, out SteelStructureParams p))
                    {
                        p.Origin = new Point3d(p.Origin.X, p.Origin.Y, 0.0);
                        SteelDataStorage.WriteParams(tr, ent, p);
                    }
                    count++;
                }
                tr.Commit();
            }

            ed.WriteMessage($"\n[SteelStructureSuite] 已将 {count} 个钢结构构件底部标高归零至 Z = 0.000。\n");
        }

        [CommandMethod("SS_ARRAY", CommandFlags.Modal)]
        public void LinearArrayStructures()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptEntityOptions peo = new PromptEntityOptions("\n请选择要定距阵列复制的参数化钢结构构件: ");
            peo.SetRejectMessage("\n请选择参数化钢结构构件。");
            peo.AddAllowedClass(typeof(Solid3d), false);
            peo.AddAllowedClass(typeof(BlockReference), false);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            SteelStructureParams baseParams = null;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(per.ObjectId, OpenMode.ForRead) is Entity ent)
                {
                    SteelDataStorage.TryReadParams(ent, out baseParams);
                }
                tr.Commit();
            }

            if (baseParams == null)
            {
                ed.WriteMessage("\n所选实体不是参数化钢结构构件。\n");
                return;
            }

            PromptIntegerOptions pio = new PromptIntegerOptions("\n请输入阵列复制数量 (不含原构件) <3>: ")
            {
                DefaultValue = 3,
                LowerLimit = 1,
                UpperLimit = 50
            };
            PromptIntegerResult pir = ed.GetInteger(pio);
            if (pir.Status != PromptStatus.OK) return;

            PromptDistanceOptions pdo = new PromptDistanceOptions("\n请输入沿 Y 轴方向的柱距间隔 (mm) <6000>: ")
            {
                DefaultValue = 6000.0
            };
            PromptDoubleResult pdr = ed.GetDistance(pdo);
            if (pdr.Status != PromptStatus.OK) return;

            using (DocumentLock dl = doc.LockDocument())
            {
                for (int i = 1; i <= pir.Value; i++)
                {
                    SteelStructureParams copy = baseParams.Clone();
                    copy.Tag = $"{baseParams.Tag}-{i + 1}";
                    copy.Origin = baseParams.Origin + new Vector3d(0, pdr.Value * i, 0);
                    SteelStructureBuilder.AppendToCurrentDatabase(db, copy);
                }
            }

            ed.WriteMessage($"\n[SteelStructureSuite] 已沿 Y 方向按间距 {pdr.Value:0.#} mm 阵列生成 {pir.Value} 座钢结构。\n");
        }
    }
}
