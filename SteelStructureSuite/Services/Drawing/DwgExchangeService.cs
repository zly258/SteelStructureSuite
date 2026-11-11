using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SteelStructureSuite.Services.Drawing
{
    public enum DwgMergeDirection
    {
        Horizontal = 0,
        Vertical = 1
    }

    public sealed class DxfVersionChoice
    {
        public DxfVersionChoice(string key, string label, DwgVersion version)
        {
            Key = key;
            Label = label;
            Version = version;
        }

        public string Key { get; }
        public string Label { get; }
        public DwgVersion Version { get; }
    }

    public sealed class DwgFileResult
    {
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// 批量 DWG 转 DXF，以及按模型空间范围把多张 DWG 横向或纵向合并到新图。
    /// </summary>
    public static class DwgExchangeService
    {
        private const int DxfPrecision = 16;

        private static readonly string[] VersionKeys =
        {
            "AC1009", "AC1015", "AC1800", "AC1021", "AC1024", "AC1027", "AC1032"
        };

        private static readonly string[] VersionLabels =
        {
            "R12", "2000", "2004", "2007", "2010", "2013", "2018"
        };

        public static IList<DxfVersionChoice> GetSupportedDxfVersions()
        {
            var list = new List<DxfVersionChoice>();
            for (int i = 0; i < VersionKeys.Length; i++)
            {
                DwgVersion version;
                if (!Enum.TryParse(VersionKeys[i], out version)) continue;
                if (!Enum.IsDefined(typeof(DwgVersion), version)) continue;
                list.Add(new DxfVersionChoice(VersionKeys[i], "DXF " + VersionLabels[i], version));
            }

            if (Enum.IsDefined(typeof(DwgVersion), DwgVersion.Current))
            {
                bool exists = false;
                foreach (DxfVersionChoice item in list)
                {
                    if (item.Version == DwgVersion.Current) { exists = true; break; }
                }
                if (!exists && DwgVersion.Current != DwgVersion.Unknown && DwgVersion.Current != DwgVersion.Newest)
                {
                    list.Add(new DxfVersionChoice(DwgVersion.Current.ToString(), "当前版本", DwgVersion.Current));
                }
            }
            return list;
        }

        public static List<DwgFileResult> ConvertToDxf(
            IList<string> sourceFiles,
            string outputDirectory,
            DwgVersion version,
            bool overwrite)
        {
            var results = new List<DwgFileResult>();
            if (sourceFiles == null) return results;
            Directory.CreateDirectory(outputDirectory);

            foreach (string raw in sourceFiles)
            {
                string path = raw == null ? string.Empty : raw.Trim();
                var result = new DwgFileResult { SourcePath = path };
                try
                {
                    if (path.Length == 0 || !File.Exists(path))
                    {
                        result.Message = "文件不存在";
                        results.Add(result);
                        continue;
                    }

                    string outPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(path) + ".dxf");
                    result.OutputPath = outPath;
                    if (File.Exists(outPath))
                    {
                        if (!overwrite)
                        {
                            result.Message = "目标已存在，已跳过";
                            results.Add(result);
                            continue;
                        }
                        File.Delete(outPath);
                    }

                    Document open = FindOpenDocument(path);
                    if (open != null)
                    {
                        using (open.LockDocument())
                        {
                            open.Database.DxfOut(outPath, DxfPrecision, version);
                        }
                    }
                    else
                    {
                        Database previous = HostApplicationServices.WorkingDatabase;
                        using (var db = new Database(false, true))
                        {
                            try
                            {
                                HostApplicationServices.WorkingDatabase = db;
                                db.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, false, null);
                                db.CloseInput(true);
                                db.DxfOut(outPath, DxfPrecision, version);
                            }
                            finally
                            {
                                HostApplicationServices.WorkingDatabase = previous;
                            }
                        }
                    }

                    result.Success = true;
                    result.Message = "已写出";
                }
                catch (System.Exception ex)
                {
                    result.Message = ex.Message;
                }
                results.Add(result);
            }

            WriteSummary(results);
            return results;
        }

        public static List<DwgFileResult> MergeDrawings(
            IList<string> sourceFiles,
            string outputPath,
            DwgMergeDirection direction,
            double gap,
            bool explode)
        {
            var results = new List<DwgFileResult>();
            if (sourceFiles == null || sourceFiles.Count == 0)
            {
                results.Add(new DwgFileResult { Message = "没有要合并的图纸" });
                return results;
            }
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                results.Add(new DwgFileResult { Message = "请指定输出 DWG" });
                return results;
            }
            if (gap < 0 || double.IsNaN(gap) || double.IsInfinity(gap))
            {
                results.Add(new DwgFileResult { Message = "间距必须是大于或等于 0 的数值" });
                return results;
            }

            string fullOut = Path.GetFullPath(outputPath);
            foreach (string raw in sourceFiles)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (string.Equals(Path.GetFullPath(raw.Trim()), fullOut, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new DwgFileResult { SourcePath = raw, Message = "输出文件不能与源图纸相同" });
                    return results;
                }
            }
            if (FindOpenDocument(fullOut) != null)
            {
                results.Add(new DwgFileResult { OutputPath = fullOut, Message = "目标文件已在 AutoCAD 中打开，请先关闭" });
                return results;
            }

            string dir = Path.GetDirectoryName(fullOut);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            Database previous = HostApplicationServices.WorkingDatabase;
            int placed = 0;
            try
            {
                using (var dest = new Database(true, true))
                {
                    HostApplicationServices.WorkingDatabase = dest;
                    double cursor = 0;
                    foreach (string raw in sourceFiles)
                    {
                        string path = raw == null ? string.Empty : raw.Trim();
                        var result = new DwgFileResult { SourcePath = path, OutputPath = fullOut };
                        if (path.Length == 0 || !File.Exists(path))
                        {
                            result.Message = "文件不存在";
                            results.Add(result);
                            continue;
                        }

                        try
                        {
                            Document open = FindOpenDocument(path);
                            if (open != null)
                            {
                                using (open.LockDocument())
                                {
                                    PlaceOne(open.Database, dest, path, direction, gap, explode, ref cursor, result);
                                }
                            }
                            else
                            {
                                using (var src = new Database(false, true))
                                {
                                    HostApplicationServices.WorkingDatabase = src;
                                    src.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, false, null);
                                    src.CloseInput(true);
                                    HostApplicationServices.WorkingDatabase = dest;
                                    PlaceOne(src, dest, path, direction, gap, explode, ref cursor, result);
                                }
                            }
                            if (result.Success) placed++;
                        }
                        catch (System.Exception ex)
                        {
                            result.Message = ex.Message;
                            HostApplicationServices.WorkingDatabase = dest;
                        }
                        results.Add(result);
                    }

                    if (placed == 0)
                    {
                        results.Insert(0, new DwgFileResult { OutputPath = fullOut, Message = "没有成功放入的图纸，未保存" });
                        return results;
                    }

                    dest.SaveAs(fullOut, DwgVersion.Current);
                }
            }
            finally
            {
                HostApplicationServices.WorkingDatabase = previous;
            }

            results.Insert(0, new DwgFileResult
            {
                Success = true,
                OutputPath = fullOut,
                Message = string.Format(CultureInfo.InvariantCulture, "已合并 {0} 张图纸", placed)
            });
            WriteSummary(results);
            return results;
        }

        private static void PlaceOne(
            Database src,
            Database dest,
            string path,
            DwgMergeDirection direction,
            double gap,
            bool explode,
            ref double cursor,
            DwgFileResult result)
        {
            Database saved = HostApplicationServices.WorkingDatabase;
            ObjectIdCollection ids;
            double minX;
            double minY;
            double width;
            double height;
            try
            {
                HostApplicationServices.WorkingDatabase = src;
                if (!TryCollectModelExtents(src, out ids, out minX, out minY, out width, out height))
                {
                    result.Message = "模型空间没有可合并的图元";
                    return;
                }
                double x = direction == DwgMergeDirection.Horizontal ? cursor : 0;
                double y = direction == DwgMergeDirection.Vertical ? cursor : 0;
                var basePoint = new Point3d(minX, minY, 0);

                using (Database extracted = src.Wblock(ids, basePoint))
                {
                    HostApplicationServices.WorkingDatabase = dest;
                    string blockName = NextBlockName(dest, Path.GetFileNameWithoutExtension(path));
                    ObjectId blockId = dest.Insert(blockName, extracted, false);
                bool exploded = false;
                using (Transaction tr = dest.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(dest.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    var br = new BlockReference(new Point3d(x, y, 0), blockId);
                    ms.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);
                    if (explode)
                    {
                        var pieces = new DBObjectCollection();
                        br.Explode(pieces);
                        foreach (DBObject obj in pieces)
                        {
                            var ent = obj as Entity;
                            if (ent == null)
                            {
                                obj.Dispose();
                                continue;
                            }
                            ms.AppendEntity(ent);
                            tr.AddNewlyCreatedDBObject(ent, true);
                        }
                        br.Erase();
                        exploded = true;
                    }
                    tr.Commit();
                }

                if (exploded)
                {
                    using (Transaction tr = dest.TransactionManager.StartTransaction())
                    {
                        var btr = tr.GetObject(blockId, OpenMode.ForWrite, false) as BlockTableRecord;
                        if (btr != null && !btr.IsErased)
                        {
                            ObjectIdCollection refs = btr.GetBlockReferenceIds(true, false);
                            if (refs == null || refs.Count == 0) btr.Erase();
                        }
                        tr.Commit();
                    }
                }
            }

            double size = direction == DwgMergeDirection.Horizontal ? width : height;
            cursor += size + gap;
            result.Success = true;
            result.Message = string.Format(
                CultureInfo.InvariantCulture,
                explode ? "已分解放入，范围 {0:0.###} x {1:0.###}" : "已按图块放入，范围 {0:0.###} x {1:0.###}",
                width,
                height);
            }
            finally
            {
                HostApplicationServices.WorkingDatabase = saved;
            }
        }

        private static bool TryCollectModelExtents(
            Database db,
            out ObjectIdCollection ids,
            out double minX,
            out double minY,
            out double width,
            out double height)
        {
            ids = new ObjectIdCollection();
            minX = 0;
            minY = 0;
            width = 0;
            height = 0;
            bool anyExtent = false;
            Extents3d ext = new Extents3d();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    ids.Add(id);
                    try
                    {
                        Extents3d item = ent.GeometricExtents;
                        if (!anyExtent)
                        {
                            ext = item;
                            anyExtent = true;
                        }
                        else
                        {
                            ext.AddExtents(item);
                        }
                    }
                    catch (System.Exception)
                    {
                        // 没有范围的图元仍然参与复制，只是不参与排布尺寸。
                    }
                }
                tr.Commit();
            }

            if (ids.Count == 0) return false;
            if (!anyExtent) return true;
            minX = ext.MinPoint.X;
            minY = ext.MinPoint.Y;
            width = Math.Max(0, ext.MaxPoint.X - ext.MinPoint.X);
            height = Math.Max(0, ext.MaxPoint.Y - ext.MinPoint.Y);
            return true;
        }

        private static string NextBlockName(Database dest, string preferred)
        {
            string seed = SanitizeBlockName(preferred);
            using (Transaction tr = dest.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(dest.BlockTableId, OpenMode.ForRead);
                string candidate = seed;
                int n = 1;
                while (bt.Has(candidate))
                {
                    candidate = seed + "_" + n.ToString(CultureInfo.InvariantCulture);
                    n++;
                }
                tr.Commit();
                return candidate;
            }
        }

        private static string SanitizeBlockName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "DWG";
            var chars = new char[raw.Length];
            int n = 0;
            foreach (char ch in raw.Trim())
            {
                bool ok = char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch > 127;
                chars[n++] = ok ? ch : '_';
            }
            string name = new string(chars, 0, n).Trim('_');
            if (name.Length == 0) name = "DWG";
            if (name.Length > 180) name = name.Substring(0, 180);
            return name;
        }

        private static Document FindOpenDocument(string path)
        {
            string full;
            try { full = Path.GetFullPath(path); }
            catch { return null; }

            foreach (Document doc in Application.DocumentManager)
            {
                if (doc == null || string.IsNullOrEmpty(doc.Name)) continue;
                try
                {
                    if (string.Equals(Path.GetFullPath(doc.Name), full, StringComparison.OrdinalIgnoreCase))
                        return doc;
                }
                catch (System.Exception)
                {
                    // 未命名的新建图没有磁盘路径。
                }
            }
            return null;
        }

        private static void WriteSummary(List<DwgFileResult> results)
        {
            try
            {
                int ok = 0;
                foreach (DwgFileResult item in results)
                {
                    if (item.Success) ok++;
                }
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    string.Format(CultureInfo.InvariantCulture, "\n[SteelStructureSuite] 图纸处理完成：成功 {0} / {1}。\n", ok, results.Count));
            }
            catch (System.Exception)
            {
                // 命令行不可用时忽略。
            }
        }
    }
}
