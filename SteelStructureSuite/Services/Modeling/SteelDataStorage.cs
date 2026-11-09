using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Services.Modeling
{
    public static class SteelDataStorage
    {
        public const string DictEntryName = "STEEL_STRUCTURE_SUITE_PARAM_V2";

        private static readonly ConcurrentDictionary<long, SteelStructureParams> HandleCache =
            new ConcurrentDictionary<long, SteelStructureParams>();

        private static readonly ConcurrentDictionary<long, SteelStructureParams> PendingEdits =
            new ConcurrentDictionary<long, SteelStructureParams>();

        // 记录每个实体上次生成三维 Solid3d 时的理论包围盒中心点，用于自动检测用户是否用 MOVE / COPY / GIZMO 平移了实体
        private static readonly ConcurrentDictionary<long, Point3d> ExpectedExtentsCenterCache =
            new ConcurrentDictionary<long, Point3d>();

        // 当前活跃拖拽会话中的参数备份（当 AutoCAD 传入 Handle==0 的瞬态克隆实体时使用）
        private static volatile SteelStructureParams _activeTransientDragParams;

        public static void SetActiveTransientDragParams(SteelStructureParams p)
        {
            _activeTransientDragParams = p?.Clone();
        }

        public static void ClearActiveTransientDragParams()
        {
            _activeTransientDragParams = null;
        }

        public static Point3d? TryGetEntityExtentsCenter(DBObject obj)
        {
            if (obj is Entity ent)
            {
                try
                {
                    Extents3d ext = ent.GeometricExtents;
                    return new Point3d(
                        (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                        (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                        (ext.MinPoint.Z + ext.MaxPoint.Z) * 0.5);
                }
                catch { }
            }
            return null;
        }

        public static void CacheParamsByHandle(DBObject obj, SteelStructureParams p, Point3d? knownExtentsCenter = null)
        {
            if (obj == null || p == null) return;
            try
            {
                long hVal = obj.Handle.Value;
                if (hVal != 0)
                {
                    HandleCache[hVal] = p.Clone();
                    Point3d? c = knownExtentsCenter ?? TryGetEntityExtentsCenter(obj);
                    if (c.HasValue)
                    {
                        ExpectedExtentsCenterCache[hVal] = c.Value;
                    }
                }
            }
            catch { }
        }

        public static void WriteParams(Transaction tr, DBObject obj, SteelStructureParams p)
        {
            if (obj == null || p == null) return;

            Point3d? extCenter = TryGetEntityExtentsCenter(obj);
            CacheParamsByHandle(obj, p, extCenter);

            if (obj.ExtensionDictionary.IsNull)
            {
                if (!obj.IsWriteEnabled) obj.UpgradeOpen();
                obj.CreateExtensionDictionary();
            }

            DBDictionary extDict = (DBDictionary)tr.GetObject(obj.ExtensionDictionary, OpenMode.ForWrite);
            Xrecord xrec;

            if (extDict.Contains(DictEntryName))
            {
                xrec = (Xrecord)tr.GetObject(extDict.GetAt(DictEntryName), OpenMode.ForWrite);
            }
            else
            {
                xrec = new Xrecord();
                extDict.SetAt(DictEntryName, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }

            Point3d anchorCenter = extCenter ?? p.Origin;

            ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.Text, p.Tag ?? "ST-101"),
                new TypedValue((int)DxfCode.Int32, (int)p.StructureType),
                new TypedValue((int)DxfCode.ExtendedDataWorldXCoordinate, p.Origin),
                new TypedValue((int)DxfCode.Real, p.Span),
                new TypedValue((int)DxfCode.Real, p.Length),
                new TypedValue((int)DxfCode.Real, p.Height),
                new TypedValue((int)DxfCode.Real, p.SubHeight),
                new TypedValue((int)DxfCode.Text, p.ColumnSection ?? "HW200x200x8x12"),
                new TypedValue((int)DxfCode.Text, p.BeamSection ?? "HW200x200x8x12"),
                new TypedValue((int)DxfCode.Text, p.BraceSection ?? "L100x100x8"),
                new TypedValue((int)DxfCode.Text, p.Material ?? "Q355B"),
                new TypedValue((int)DxfCode.Real, p.RotationRad),
                new TypedValue((int)DxfCode.Real, p.EndPointOffsetZ),
                new TypedValue((int)DxfCode.Int32, p.BayCount),
                new TypedValue((int)DxfCode.ExtendedDataWorldXCoordinate, anchorCenter)
            );

            xrec.Data = rb;
        }

        public static void SetPendingEdit(DBObject obj, SteelStructureParams p, long fallbackHandle = 0)
        {
            SetActiveTransientDragParams(p);
            CacheParamsByHandle(obj, p);
            try
            {
                long h = obj != null ? obj.Handle.Value : 0;
                if (h == 0) h = fallbackHandle;
                if (h != 0 && p != null)
                {
                    PendingEdits[h] = p.Clone();
                    HandleCache[h] = p.Clone();
                    Point3d? c = TryGetEntityExtentsCenter(obj);
                    if (c.HasValue)
                        ExpectedExtentsCenterCache[h] = c.Value;
                }
            }
            catch { }
        }

        public static void DiscardPendingEdits()
        {
            PendingEdits.Clear();
            ClearActiveTransientDragParams();
        }

        public static void FlushPendingEdits(Database db)
        {
            ClearActiveTransientDragParams();
            if (db == null || PendingEdits.IsEmpty) return;
            KeyValuePair<long, SteelStructureParams>[] snapshot = PendingEdits.ToArray();
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (KeyValuePair<long, SteelStructureParams> kv in snapshot)
                    {
                        try
                        {
                            ObjectId id = db.GetObjectId(false, new Handle(kv.Key), 0);
                            if (!id.IsValid || id.IsErased)
                            {
                                PendingEdits.TryRemove(kv.Key, out _);
                                continue;
                            }
                            DBObject obj = tr.GetObject(id, OpenMode.ForWrite, false);
                            WriteParams(tr, obj, kv.Value);
                            PendingEdits.TryRemove(kv.Key, out _);
                        }
                        catch { }
                    }
                    tr.Commit();
                }
            }
            catch { }
        }

        public static bool TryReadParams(DBObject obj, out SteelStructureParams p)
        {
            p = null;
            if (obj == null) return false;

            long h = 0;
            try { h = obj.Handle.Value; } catch { }

            // 1. 优先读取正在进行夹点编辑的 PendingEdits
            if (h != 0 && PendingEdits.TryGetValue(h, out SteelStructureParams pending) && pending != null)
            {
                p = pending.Clone();
                return true;
            }

            // 2. 从实体扩展字典读取最新参数，并自动检测实体是否被 AutoCAD 原生 MOVE / COPY / GIZMO 平移过！
            if (!obj.ExtensionDictionary.IsNull)
            {
                Database db = obj.Database;
                if (db != null)
                {
                    try
                    {
                        Point3d? storedAnchor = null;
                        Transaction topTr = db.TransactionManager.TopTransaction;
                        bool ok;
                        if (topTr != null)
                        {
                            ok = ReadFromTransaction(topTr, obj, out p, out storedAnchor);
                        }
                        else
                        {
                            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                            {
                                ok = ReadFromTransaction(tr, obj, out p, out storedAnchor);
                                tr.Commit();
                            }
                        }

                        if (ok && p != null)
                        {
                            AutoCalibrateOriginIfEntityMoved(obj, p, h, storedAnchor);
                            CacheParamsByHandle(obj, p);
                            return true;
                        }
                    }
                    catch { }
                }
            }

            // 3. 回退到句柄缓存（处理克隆实体但保留了源 Handle 的情况）
            if (h != 0 && HandleCache.TryGetValue(h, out SteelStructureParams cached) && cached != null)
            {
                p = cached.Clone();
                AutoCalibrateOriginIfEntityMoved(obj, p, h, null);
                return true;
            }

            // 4. 若是 Handle == 0 的夹点拖拽瞬态克隆实体，返回当前活跃拖拽参数
            if (h == 0 && _activeTransientDragParams != null)
            {
                p = _activeTransientDragParams.Clone();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 自动校准因 AutoCAD 原生 MOVE / COPY / ALIGN 命令或夹点平移导致的实体实际几何中心与记录 Origin 之间的偏差，
        /// 彻底杜绝“移动构件后夹点留在原地不同步”的问题！
        /// </summary>
        private static void AutoCalibrateOriginIfEntityMoved(
            DBObject obj,
            SteelStructureParams p,
            long handle,
            Point3d? xrecAnchorCenter)
        {
            Point3d? actualCenter = TryGetEntityExtentsCenter(obj);
            if (!actualCenter.HasValue) return;

            Point3d expectedCenter;
            if (handle != 0 && ExpectedExtentsCenterCache.TryGetValue(handle, out Point3d cachedCenter))
            {
                expectedCenter = cachedCenter;
            }
            else if (xrecAnchorCenter.HasValue)
            {
                expectedCenter = xrecAnchorCenter.Value;
            }
            else
            {
                return;
            }

            Vector3d drift = actualCenter.Value - expectedCenter;
            if (drift.Length > 1.0 && drift.Length < 1e8)
            {
                p.Origin = p.Origin + drift;
                if (handle != 0)
                {
                    ExpectedExtentsCenterCache[handle] = actualCenter.Value;
                    HandleCache[handle] = p.Clone();
                }
            }
        }

        private static bool ReadFromTransaction(
            Transaction tr,
            DBObject obj,
            out SteelStructureParams p,
            out Point3d? storedAnchorCenter)
        {
            p = null;
            storedAnchorCenter = null;

            DBDictionary extDict = tr.GetObject(obj.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
            if (extDict == null || !extDict.Contains(DictEntryName)) return false;

            Xrecord xrec = tr.GetObject(extDict.GetAt(DictEntryName), OpenMode.ForRead) as Xrecord;
            if (xrec?.Data == null) return false;

            TypedValue[] vals = xrec.Data.AsArray();
            if (vals.Length < 11) return false;

            p = new SteelStructureParams
            {
                Tag = vals[0].Value as string ?? "ST-101",
                StructureType = (ParametricStructureType)Convert.ToInt32(vals[1].Value),
                Origin = (Point3d)vals[2].Value,
                Span = Convert.ToDouble(vals[3].Value),
                Length = Convert.ToDouble(vals[4].Value),
                Height = Convert.ToDouble(vals[5].Value),
                SubHeight = Convert.ToDouble(vals[6].Value),
                ColumnSection = vals[7].Value as string ?? "HW200x200x8x12",
                BeamSection = vals[8].Value as string ?? "HW200x200x8x12",
                BraceSection = vals[9].Value as string ?? "L100x100x8",
                Material = vals[10].Value as string ?? "Q355B",
                RotationRad = vals.Length >= 12 ? Convert.ToDouble(vals[11].Value) : 0.0,
                EndPointOffsetZ = vals.Length >= 13 ? Convert.ToDouble(vals[12].Value) : 0.0,
                BayCount = vals.Length >= 14 ? Convert.ToInt32(vals[13].Value) : 2
            };

            if (vals.Length >= 15 && vals[14].Value is Point3d anchorPt)
            {
                storedAnchorCenter = anchorPt;
            }
            return true;
        }
    }
}
