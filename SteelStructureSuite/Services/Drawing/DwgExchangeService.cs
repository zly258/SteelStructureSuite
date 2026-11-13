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

        public override string ToString()
        {
            return Label ?? string.Empty;
        }
    }

    public sealed class DwgFileResult
    {
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public bool Success { get; set; }
        /// <summary>true = 不是失败，而是主动跳过（例如目标文件已存在）。</summary>
        public bool Skipped { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// 批量 DWG 转 DXF，以及按模型空间范围把多张 DWG 横向或纵向合并到新图。
    ///
    /// 线程约定：本类所有方法都必须在 AutoCAD 主线程上调用。
    /// 数据库对象的创建与释放离开主线程会触发原生访问冲突，
    /// 因此界面层只能收集参数，实际作业由 MainThreadDispatcher 调度。
    /// </summary>
    public static class DwgExchangeService
    {
        private static readonly string[] VersionKeys =
        {
            "AC1009", "AC1015", "AC1018", "AC1021", "AC1024", "AC1027", "AC1032"
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
            string versionToken,
            bool overwrite,
            Action<string> progress = null)
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
                            result.Skipped = true;
                            result.Message = "目标已存在，未覆盖";
                            results.Add(result);
                            continue;
                        }
                        File.Delete(outPath);
                    }

                    // 优先在独立的核心控制台进程里导出：
                    // 含 AEC 对象的图纸在当前 CAD 进程内做 DxfOut 会让 aecbase.dbx
                    // 抛 c0000027 未处理异常，把整个 CAD 带崩；子进程里异常只影响该文件。
                    string source = path;
                    string tempCopy = null;
                    Document open = FindOpenDocument(path);
                    if (open != null)
                    {
                        // 图纸正在 AutoCAD 里打开：先存一份临时副本，
                        // 这样未保存的修改也能导出，同时不在当前进程里做 DXF 写出。
                        tempCopy = Path.Combine(
                            Path.GetTempPath(),
                            "sss-copy-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".dwg");
                        try
                        {
                            using (open.LockDocument())
                            {
                                open.Database.SaveAs(tempCopy, true, DwgVersion.Current, open.Database.SecurityParameters);
                            }
                            source = tempCopy;
                        }
                        catch (System.Exception ex)
                        {
                            result.Message = "无法复制正在打开的图纸：" + ex.Message;
                            results.Add(result);
                            continue;
                        }
                    }

                    if (IsDxfPath(path))
                    {
                        result.Message = "已经是 DXF 文件，无需转换";
                        results.Add(result);
                        TryDelete(tempCopy);
                        continue;
                    }

                    string error = DxfOutOfProcessConverter.Convert(
                        source, outPath, versionToken);
                    TryDelete(tempCopy);
                    if (error != null)
                    {
                        result.Message = error;
                        results.Add(result);
                        continue;
                    }

                    result.Success = true;
                    result.Message = "已写出（核心控制台）";
                    if (progress != null)
                    {
                        try { progress("成功  " + Path.GetFileName(path) + "  已写出"); }
                        catch (System.Exception) { }
                    }
                }
                catch (System.Exception ex)
                {
                    result.Message = ex.GetType().Name + ": " + ex.Message;
                    if (progress != null)
                    {
                        try { progress("失败  " + Path.GetFileName(path) + "  " + result.Message); }
                        catch (System.Exception) { }
                    }
                }
                results.Add(result);
            }

            return results;
        }

        /// <summary>把处理结果打印到命令行，必须在 AutoCAD 主线程上调用。</summary>
        public static void WriteSummary(IList<DwgFileResult> results)
        {
            if (results == null) return;
            try
            {
                int ok = 0;
                int skipped = 0;
                int failed = 0;
                foreach (DwgFileResult item in results)
                {
                    if (item.Skipped) skipped++;
                    else if (item.Success) ok++;
                    else failed++;
                }
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "\n[SteelStructureSuite] 图纸处理完成：成功 {0} / 跳过 {1} / 失败 {2}（共 {3}）。\n",
                        ok,
                        skipped,
                        failed,
                        results.Count));
            }
            catch (System.Exception)
            {
                // 命令行不可用时忽略。
            }
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
                results.Add(new DwgFileResult { Message = "请指定输出图纸" });
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
            // 源数据库与 WBLOCK 出来的临时库必须活到目标文件写完为止：
            // Database.Insert 只是登记引用，源库提前释放会让写出的 DWG 指向
            // 已释放的原生内存，AutoCAD 打开时会直接访问冲突。
            var keepAlive = new List<Database>();
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
                                    PlaceOne(open.Database, dest, path, direction, gap, explode, ref cursor, result, keepAlive);
                                }
                            }
                            else
                            {
                                var src = new Database(false, true);
                                keepAlive.Add(src);
                                try
                                {
                                    HostApplicationServices.WorkingDatabase = src;
                                    ReadSourceDrawing(src, path);
                                    HostApplicationServices.WorkingDatabase = dest;
                                    PlaceOne(src, dest, path, direction, gap, explode, ref cursor, result, keepAlive);
                                }
                                catch (System.Exception ex)
                                {
                                    result.Message = ex.GetType().Name + ": " + ex.Message;
                                    HostApplicationServices.WorkingDatabase = dest;
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

                    SaveMergedDrawing(dest, fullOut);
                }
            }
            finally
            {
                HostApplicationServices.WorkingDatabase = previous;
                foreach (Database db in keepAlive)
                {
                    try { db.Dispose(); }
                    catch (System.Exception) { }
                }
                keepAlive.Clear();
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


        private static void ReadSourceDrawing(Database db, string path)
        {
            if (IsDxfPath(path))
            {
                string log = Path.Combine(Path.GetTempPath(), "sss-dxfin.log");
                db.DxfIn(path, log);
                return;
            }
            db.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, false, null);
            db.CloseInput(true);
        }

        private static void SaveMergedDrawing(Database db, string path)
        {
            // 先写临时文件再替换目标：写失败时目标位置仍是上一份可用图纸，
            // 不会留下半截文件导致 AutoCAD 打开时崩溃。
            string dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) dir = ".";
            string temp = Path.Combine(
                dir,
                Path.GetFileNameWithoutExtension(path) + ".sstmp" + Path.GetExtension(path));

            try
            {
                if (File.Exists(temp)) File.Delete(temp);

                if (IsDxfPath(path))
                {
                    // DXF 一律走独立的核心控制台进程：合并结果里若含 AEC 对象，
                    // 进程内 DxfOut 会让 aecbase.dbx 抛 c0000027 把 CAD 带崩。
                    // 先存成临时 DWG，再让子进程转成 DXF。
                    string dwgTemp = Path.Combine(
                        dir,
                        Path.GetFileNameWithoutExtension(path) + ".sstmp.dwg");
                    try
                    {
                        if (File.Exists(dwgTemp)) File.Delete(dwgTemp);
                        db.SaveAs(dwgTemp, true, DwgVersion.Current, db.SecurityParameters);
                        if (!File.Exists(dwgTemp) || new FileInfo(dwgTemp).Length == 0)
                            throw new IOException("保存中间 DWG 失败。");

                        string error = DxfOutOfProcessConverter.Convert(
                            dwgTemp, path, DxfOutOfProcessConverter.GetVersionToken("AC1032"));
                        if (error != null) throw new IOException("导出 DXF 失败：" + error);
                        if (!File.Exists(path) || new FileInfo(path).Length == 0)
                            throw new IOException("导出的 DXF 为空：" + path);
                    }
                    finally
                    {
                        try { if (File.Exists(dwgTemp)) File.Delete(dwgTemp); }
                        catch (System.Exception) { }
                    }
                    return;
                }
                else
                {
                    // 与详图出图保持一致：按版本回退保存，
                    // 避免 SaveAs 覆盖现有文件时抛 eWrongDatabase 写出半个文件。
                    DwgVersion[] versions = { DwgVersion.Current, DwgVersion.AC1032, DwgVersion.AC1027, DwgVersion.AC1024 };
                    System.Exception lastError = null;
                    bool saved = false;
                    foreach (DwgVersion version in versions)
                    {
                        try
                        {
                            if (File.Exists(temp)) File.Delete(temp);
                            db.SaveAs(temp, true, version, db.SecurityParameters);
                            if (File.Exists(temp) && new FileInfo(temp).Length > 0)
                            {
                                saved = true;
                                break;
                            }
                        }
                        catch (System.Exception ex)
                        {
                            lastError = ex;
                        }
                    }
                    if (!saved)
                    {
                        throw new IOException(
                            "保存 DWG 失败：" + (lastError == null ? "未知错误" : lastError.Message),
                            lastError);
                    }
                }

                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                // 写完再读回来校验一次：读不回来的文件说明写出的内容
                // 指向了已释放的原生内存，打开时必然访问冲突，必须当场判失败。
                VerifySavedDrawing(path);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (System.Exception) { }
            }
        }

        private static bool IsDxfPath(string path)
        {
            return path != null && path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (System.Exception) { }
        }

        /// <summary>
        /// 写盘后再打开校验一次：只有能被 AutoCAD 正常读回来的文件才算成功。
        /// 读不回来的文件说明写出的内容指向了已释放的原生内存，
        /// 这种文件一旦被打开就会触发访问冲突，必须当场判定为失败。
        /// </summary>
        private static void VerifySavedDrawing(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
                throw new IOException("输出文件为空：" + path);

            var previous = HostApplicationServices.WorkingDatabase;
            using (var check = new Database(false, true))
            {
                try
                {
                    HostApplicationServices.WorkingDatabase = check;
                    check.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, false, null);
                    check.CloseInput(true);
                }
                finally
                {
                    HostApplicationServices.WorkingDatabase = previous;
                }
            }
        }

        private static void PlaceOne(
            Database src,
            Database dest,
            string path,
            DwgMergeDirection direction,
            double gap,
            bool explode,
            ref double cursor,
            DwgFileResult result,
            List<Database> keepAlive)
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

                Database extracted = src.Wblock(ids, basePoint);
                keepAlive.Add(extracted);
                HostApplicationServices.WorkingDatabase = dest;
                string blockName = NextBlockName(dest, Path.GetFileNameWithoutExtension(path));
                // preserveSourceDatabase 必须为 true：Insert 会按这个标志决定是否释放
                // 传入的数据库。传 false 时它会在插入后销毁 WBLOCK 出来的库，
                // 之后我们再 Dispose 一次就是重复释放，写出的 DWG 指向已释放的原生
                // 内存，AutoCAD 打开合并结果时直接访问冲突。
                ObjectId blockId = dest.Insert(blockName, extracted, true);
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

    }
}
