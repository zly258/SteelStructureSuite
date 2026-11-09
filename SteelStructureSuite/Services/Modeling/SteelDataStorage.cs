using System;
using System.Collections.Concurrent;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Services.Modeling
{
    public static class SteelDataStorage
    {
        public const string DictEntryName = "STEEL_STRUCTURE_SUITE_PARAM_V2";

        // 内存级高速缓存（按实体 Handle 索引），确保夹点拖拽瞬态克隆实体与连续修改时绝不丢失或重置任何参数
        private static readonly ConcurrentDictionary<long, SteelStructureParams> HandleCache =
            new ConcurrentDictionary<long, SteelStructureParams>();

        public static void CacheParamsByHandle(DBObject obj, SteelStructureParams p)
        {
            if (obj == null || p == null) return;
            try
            {
                long hVal = obj.Handle.Value;
                if (hVal != 0)
                {
                    HandleCache[hVal] = p.Clone();
                }
            }
            catch { }
        }

        public static void WriteParams(Transaction tr, DBObject obj, SteelStructureParams p)
        {
            if (obj == null || p == null) return;

            CacheParamsByHandle(obj, p);

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
                new TypedValue((int)DxfCode.Int32, p.BayCount)
            );

            xrec.Data = rb;
        }

        public static bool TryReadParams(DBObject obj, out SteelStructureParams p)
        {
            p = null;
            if (obj == null) return false;

            // 1. 扩展字典还在时，优先读数据库里的最新参数
            if (!obj.ExtensionDictionary.IsNull)
            {
                Database db = obj.Database;
                if (db != null)
                {
                    try
                    {
                        Transaction topTr = db.TransactionManager.TopTransaction;
                        if (topTr != null)
                        {
                            if (ReadFromTransaction(topTr, obj, out p))
                            {
                                CacheParamsByHandle(obj, p);
                                return true;
                            }
                        }
                        else
                        {
                            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                            {
                                bool ok = ReadFromTransaction(tr, obj, out p);
                                tr.Commit();
                                if (ok)
                                {
                                    CacheParamsByHandle(obj, p);
                                    return true;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            // 2. 夹点拖拽时的瞬态克隆经常没有扩展字典，必须回退到句柄缓存，否则夹点会整组消失
            try
            {
                long hVal = obj.Handle.Value;
                if (hVal != 0 && HandleCache.TryGetValue(hVal, out SteelStructureParams cached))
                {
                    p = cached.Clone();
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static bool ReadFromTransaction(Transaction tr, DBObject obj, out SteelStructureParams p)
        {
            p = null;
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
            return true;
        }
    }
}
