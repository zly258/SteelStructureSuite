using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml;
using SteelStructureSuite.Domain.Enums;
using SteelStructureSuite.Domain.Models;

namespace SteelStructureSuite.Services.Catalog
{
    /// <summary>
    /// 型钢截面分类库管理器
    /// 按 5 大类、24 种截面类型独立管理（涵盖热轧型钢、焊接组合截面、冷弯薄壁型钢、空心钢管、实心型材），
    /// 支持持久化保存、多工作表 Excel (.xlsx) 与 CSV 批量导出与导入。
    /// </summary>
    public static class SectionCatalogService
    {
        public static readonly SectionCategory[] OrderedCategories =
        {
            SectionCategory.HotRolled,
            SectionCategory.WeldedComposite,
            SectionCategory.ColdFormed,
            SectionCategory.HollowTube,
            SectionCategory.SolidBar
        };

        public static readonly SectionShapeType[] OrderedShapeTypes =
        {
            // 1. 热轧标准型钢
            SectionShapeType.HBeam,
            SectionShapeType.IBeam,
            SectionShapeType.Channel,
            SectionShapeType.Angle,
            SectionShapeType.UnequalAngle,
            SectionShapeType.TBeam,

            // 2. 焊接与组合型钢
            SectionShapeType.WeldedHBeam,
            SectionShapeType.WeldedHBeamUnequal,
            SectionShapeType.WeldedBox,
            SectionShapeType.WeldedTBeam,
            SectionShapeType.WeldedCross,
            SectionShapeType.DoubleAngle,
            SectionShapeType.DoubleChannel,

            // 3. 冷弯薄壁型钢
            SectionShapeType.ColdFormedC,
            SectionShapeType.ColdFormedZ,
            SectionShapeType.ZBeam,
            SectionShapeType.ColdFormedU,
            SectionShapeType.HatSection,

            // 4. 空心钢管
            SectionShapeType.SquareTube,
            SectionShapeType.RectTube,
            SectionShapeType.PipeTube,

            // 5. 实心型材与板材
            SectionShapeType.FlatBar,
            SectionShapeType.RoundBar,
            SectionShapeType.SquareBar
        };

        private static Dictionary<SectionShapeType, List<SectionProfile>> _tables = InitializeTables();

        private static Dictionary<SectionShapeType, List<SectionProfile>> InitializeTables()
        {
            var tables = CreateDefaultTables();
            try
            {
                string savedPath = GetLocalCatalogCsvPath();
                if (File.Exists(savedPath))
                {
                    ImportFromCsvInto(tables, savedPath);
                }
            }
            catch
            {
                // 读取本地缓存失败时安全回退默认国标库
            }
            return tables;
        }

        private static string GetLocalCatalogCsvPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteelStructureSuite");
            return Path.Combine(dir, "SectionCatalog_MultiTable.csv");
        }

        private static Dictionary<SectionShapeType, List<SectionProfile>> CreateDefaultTables()
        {
            return new Dictionary<SectionShapeType, List<SectionProfile>>
            {
                // ================= 1. 热轧标准型钢 =================
                [SectionShapeType.HBeam] = new List<SectionProfile>
                {
                    new SectionProfile("HW100x100x6x8", SectionShapeType.HBeam, 100, 100, 6, 8),
                    new SectionProfile("HW125x125x6.5x9", SectionShapeType.HBeam, 125, 125, 6.5, 9),
                    new SectionProfile("HW150x150x7x10", SectionShapeType.HBeam, 150, 150, 7, 10),
                    new SectionProfile("HW175x175x7.5x11", SectionShapeType.HBeam, 175, 175, 7.5, 11),
                    new SectionProfile("HW200x200x8x12", SectionShapeType.HBeam, 200, 200, 8, 12),
                    new SectionProfile("HW250x250x9x14", SectionShapeType.HBeam, 250, 250, 9, 14),
                    new SectionProfile("HW300x300x10x15", SectionShapeType.HBeam, 300, 300, 10, 15),
                    new SectionProfile("HW350x350x12x19", SectionShapeType.HBeam, 350, 350, 12, 19),
                    new SectionProfile("HW400x400x13x21", SectionShapeType.HBeam, 400, 400, 13, 21),
                    new SectionProfile("HM194x150x6x9", SectionShapeType.HBeam, 194, 150, 6, 9),
                    new SectionProfile("HM244x175x7x11", SectionShapeType.HBeam, 244, 175, 7, 11),
                    new SectionProfile("HM294x200x8x12", SectionShapeType.HBeam, 294, 200, 8, 12),
                    new SectionProfile("HM340x250x9x14", SectionShapeType.HBeam, 340, 250, 9, 14),
                    new SectionProfile("HM390x300x10x16", SectionShapeType.HBeam, 390, 300, 10, 16),
                    new SectionProfile("HN200x100x5.5x8", SectionShapeType.HBeam, 200, 100, 5.5, 8),
                    new SectionProfile("HN250x125x6x9", SectionShapeType.HBeam, 250, 125, 6, 9),
                    new SectionProfile("HN300x150x6.5x9", SectionShapeType.HBeam, 300, 150, 6.5, 9),
                    new SectionProfile("HN350x175x7x11", SectionShapeType.HBeam, 350, 175, 7, 11),
                    new SectionProfile("HN400x200x8x13", SectionShapeType.HBeam, 400, 200, 8, 13),
                    new SectionProfile("HN450x200x9x14", SectionShapeType.HBeam, 450, 200, 9, 14),
                    new SectionProfile("HN500x200x10x16", SectionShapeType.HBeam, 500, 200, 10, 16)
                },
                [SectionShapeType.IBeam] = new List<SectionProfile>
                {
                    new SectionProfile("I10", SectionShapeType.IBeam, 100, 68, 4.5, 7.6),
                    new SectionProfile("I12.6", SectionShapeType.IBeam, 126, 74, 5.0, 8.4),
                    new SectionProfile("I14", SectionShapeType.IBeam, 140, 80, 5.5, 9.1),
                    new SectionProfile("I16", SectionShapeType.IBeam, 160, 88, 6.0, 9.9),
                    new SectionProfile("I18", SectionShapeType.IBeam, 180, 94, 6.5, 10.7),
                    new SectionProfile("I20a", SectionShapeType.IBeam, 200, 100, 7.0, 11.4),
                    new SectionProfile("I22a", SectionShapeType.IBeam, 220, 110, 7.5, 12.3),
                    new SectionProfile("I25a", SectionShapeType.IBeam, 250, 116, 8.0, 13.0),
                    new SectionProfile("I28a", SectionShapeType.IBeam, 280, 122, 8.5, 13.7),
                    new SectionProfile("I32a", SectionShapeType.IBeam, 320, 130, 9.5, 15.0),
                    new SectionProfile("I36a", SectionShapeType.IBeam, 360, 136, 10.0, 15.8),
                    new SectionProfile("I40a", SectionShapeType.IBeam, 400, 142, 10.5, 16.5)
                },
                [SectionShapeType.Channel] = new List<SectionProfile>
                {
                    new SectionProfile("C8", SectionShapeType.Channel, 80, 43, 5.0, 8.0),
                    new SectionProfile("C10", SectionShapeType.Channel, 100, 48, 5.3, 8.5),
                    new SectionProfile("C12.6", SectionShapeType.Channel, 126, 53, 5.5, 9.0),
                    new SectionProfile("C14a", SectionShapeType.Channel, 140, 58, 6.0, 9.5),
                    new SectionProfile("C16a", SectionShapeType.Channel, 160, 63, 6.5, 10.0),
                    new SectionProfile("C18a", SectionShapeType.Channel, 180, 68, 7.0, 10.5),
                    new SectionProfile("C20a", SectionShapeType.Channel, 200, 73, 7.0, 11.0),
                    new SectionProfile("C22a", SectionShapeType.Channel, 220, 77, 7.0, 11.5),
                    new SectionProfile("C25a", SectionShapeType.Channel, 250, 78, 8.0, 12.0),
                    new SectionProfile("C28a", SectionShapeType.Channel, 280, 82, 7.5, 12.5),
                    new SectionProfile("C32a", SectionShapeType.Channel, 320, 88, 8.0, 14.0)
                },
                [SectionShapeType.Angle] = new List<SectionProfile>
                {
                    new SectionProfile("L40x40x4", SectionShapeType.Angle, 40, 40, 4, 4),
                    new SectionProfile("L50x50x5", SectionShapeType.Angle, 50, 50, 5, 5),
                    new SectionProfile("L63x63x6", SectionShapeType.Angle, 63, 63, 6, 6),
                    new SectionProfile("L75x75x6", SectionShapeType.Angle, 75, 75, 6, 6),
                    new SectionProfile("L80x80x8", SectionShapeType.Angle, 80, 80, 8, 8),
                    new SectionProfile("L90x90x8", SectionShapeType.Angle, 90, 90, 8, 8),
                    new SectionProfile("L100x100x8", SectionShapeType.Angle, 100, 100, 8, 8),
                    new SectionProfile("L110x110x10", SectionShapeType.Angle, 110, 110, 10, 10),
                    new SectionProfile("L125x125x10", SectionShapeType.Angle, 125, 125, 10, 10),
                    new SectionProfile("L140x140x12", SectionShapeType.Angle, 140, 140, 12, 12),
                    new SectionProfile("L160x160x12", SectionShapeType.Angle, 160, 160, 12, 12)
                },
                [SectionShapeType.UnequalAngle] = new List<SectionProfile>
                {
                    new SectionProfile("L63x40x5", SectionShapeType.UnequalAngle, 63, 40, 5, 5),
                    new SectionProfile("L75x50x6", SectionShapeType.UnequalAngle, 75, 50, 6, 6),
                    new SectionProfile("L90x56x6", SectionShapeType.UnequalAngle, 90, 56, 6, 6),
                    new SectionProfile("L100x63x8", SectionShapeType.UnequalAngle, 100, 63, 8, 8),
                    new SectionProfile("L125x80x10", SectionShapeType.UnequalAngle, 125, 80, 10, 10),
                    new SectionProfile("L140x90x10", SectionShapeType.UnequalAngle, 140, 90, 10, 10),
                    new SectionProfile("L160x100x12", SectionShapeType.UnequalAngle, 160, 100, 12, 12)
                },
                [SectionShapeType.TBeam] = new List<SectionProfile>
                {
                    new SectionProfile("TW100x200x8x12", SectionShapeType.TBeam, 100, 200, 8, 12),
                    new SectionProfile("TW125x250x9x14", SectionShapeType.TBeam, 125, 250, 9, 14),
                    new SectionProfile("TW150x300x10x15", SectionShapeType.TBeam, 150, 300, 10, 15),
                    new SectionProfile("TM122x175x7x11", SectionShapeType.TBeam, 122, 175, 7, 11),
                    new SectionProfile("TM147x200x8x12", SectionShapeType.TBeam, 147, 200, 8, 12),
                    new SectionProfile("TN100x100x5.5x8", SectionShapeType.TBeam, 100, 100, 5.5, 8),
                    new SectionProfile("TN150x150x6.5x9", SectionShapeType.TBeam, 150, 150, 6.5, 9)
                },

                // ================= 2. 焊接与组合型钢 =================
                [SectionShapeType.WeldedHBeam] = new List<SectionProfile>
                {
                    new SectionProfile("BH300x200x8x12", SectionShapeType.WeldedHBeam, 300, 200, 8, 12),
                    new SectionProfile("BH400x200x8x12", SectionShapeType.WeldedHBeam, 400, 200, 8, 12),
                    new SectionProfile("BH450x220x8x14", SectionShapeType.WeldedHBeam, 450, 220, 8, 14),
                    new SectionProfile("BH500x250x10x16", SectionShapeType.WeldedHBeam, 500, 250, 10, 16),
                    new SectionProfile("BH600x250x10x16", SectionShapeType.WeldedHBeam, 600, 250, 10, 16),
                    new SectionProfile("BH700x300x12x20", SectionShapeType.WeldedHBeam, 700, 300, 12, 20),
                    new SectionProfile("BH800x350x14x22", SectionShapeType.WeldedHBeam, 800, 350, 14, 22)
                },
                [SectionShapeType.WeldedHBeamUnequal] = new List<SectionProfile>
                {
                    new SectionProfile("BHU400x180x240x8x12x16", SectionShapeType.WeldedHBeamUnequal, 400, 180, 8, 12, 240, 16),
                    new SectionProfile("BHU500x200x280x10x14x18", SectionShapeType.WeldedHBeamUnequal, 500, 200, 10, 14, 280, 18),
                    new SectionProfile("BHU600x220x320x12x16x22", SectionShapeType.WeldedHBeamUnequal, 600, 220, 12, 16, 320, 22),
                    new SectionProfile("BHU750x250x350x12x18x25", SectionShapeType.WeldedHBeamUnequal, 750, 250, 12, 18, 350, 25)
                },
                [SectionShapeType.WeldedBox] = new List<SectionProfile>
                {
                    new SectionProfile("BOX250x250x12x12", SectionShapeType.WeldedBox, 250, 250, 12, 12),
                    new SectionProfile("BOX300x300x14x14", SectionShapeType.WeldedBox, 300, 300, 14, 14),
                    new SectionProfile("BOX350x350x16x16", SectionShapeType.WeldedBox, 350, 350, 16, 16),
                    new SectionProfile("BOX400x400x16x20", SectionShapeType.WeldedBox, 400, 400, 16, 20),
                    new SectionProfile("BOX500x300x14x20", SectionShapeType.WeldedBox, 500, 300, 14, 20),
                    new SectionProfile("BOX600x400x18x24", SectionShapeType.WeldedBox, 600, 400, 18, 24)
                },
                [SectionShapeType.WeldedTBeam] = new List<SectionProfile>
                {
                    new SectionProfile("WT150x150x8x12", SectionShapeType.WeldedTBeam, 150, 150, 8, 12),
                    new SectionProfile("WT200x200x10x14", SectionShapeType.WeldedTBeam, 200, 200, 10, 14),
                    new SectionProfile("WT250x220x10x16", SectionShapeType.WeldedTBeam, 250, 220, 10, 16),
                    new SectionProfile("WT300x250x12x18", SectionShapeType.WeldedTBeam, 300, 250, 12, 18)
                },
                [SectionShapeType.WeldedCross] = new List<SectionProfile>
                {
                    new SectionProfile("CROSS300x180x10x16", SectionShapeType.WeldedCross, 300, 180, 10, 16),
                    new SectionProfile("CROSS400x200x12x18", SectionShapeType.WeldedCross, 400, 200, 12, 18),
                    new SectionProfile("CROSS500x250x14x22", SectionShapeType.WeldedCross, 500, 250, 14, 22),
                    new SectionProfile("CROSS600x300x16x25", SectionShapeType.WeldedCross, 600, 300, 16, 25)
                },
                [SectionShapeType.DoubleAngle] = new List<SectionProfile>
                {
                    new SectionProfile("2L63x63x6", SectionShapeType.DoubleAngle, 63, 63, 6, 6, 0, 0, 0, 8),
                    new SectionProfile("2L75x75x6", SectionShapeType.DoubleAngle, 75, 75, 6, 6, 0, 0, 0, 10),
                    new SectionProfile("2L90x90x8", SectionShapeType.DoubleAngle, 90, 90, 8, 8, 0, 0, 0, 10),
                    new SectionProfile("2L100x100x8", SectionShapeType.DoubleAngle, 100, 100, 8, 8, 0, 0, 0, 12),
                    new SectionProfile("2L125x125x10", SectionShapeType.DoubleAngle, 125, 125, 10, 10, 0, 0, 0, 12)
                },
                [SectionShapeType.DoubleChannel] = new List<SectionProfile>
                {
                    new SectionProfile("2C14a", SectionShapeType.DoubleChannel, 140, 58, 6.0, 9.5, 0, 0, 0, 10),
                    new SectionProfile("2C16a", SectionShapeType.DoubleChannel, 160, 63, 6.5, 10.0, 0, 0, 0, 10),
                    new SectionProfile("2C20a", SectionShapeType.DoubleChannel, 200, 73, 7.0, 11.0, 0, 0, 0, 12),
                    new SectionProfile("2C25a", SectionShapeType.DoubleChannel, 250, 78, 8.0, 12.0, 0, 0, 0, 12)
                },

                // ================= 3. 冷弯薄壁型钢 =================
                [SectionShapeType.ColdFormedC] = new List<SectionProfile>
                {
                    new SectionProfile("C120x50x20x2.0", SectionShapeType.ColdFormedC, 120, 50, 2.0, 2.0, 0, 0, 20),
                    new SectionProfile("C140x50x20x2.0", SectionShapeType.ColdFormedC, 140, 50, 2.0, 2.0, 0, 0, 20),
                    new SectionProfile("C160x60x20x2.5", SectionShapeType.ColdFormedC, 160, 60, 2.5, 2.5, 0, 0, 20),
                    new SectionProfile("C180x70x20x2.5", SectionShapeType.ColdFormedC, 180, 70, 2.5, 2.5, 0, 0, 20),
                    new SectionProfile("C200x70x20x2.5", SectionShapeType.ColdFormedC, 200, 70, 2.5, 2.5, 0, 0, 20),
                    new SectionProfile("C220x75x20x3.0", SectionShapeType.ColdFormedC, 220, 75, 3.0, 3.0, 0, 0, 20),
                    new SectionProfile("C250x75x20x3.0", SectionShapeType.ColdFormedC, 250, 75, 3.0, 3.0, 0, 0, 20)
                },
                [SectionShapeType.ColdFormedZ] = new List<SectionProfile>
                {
                    new SectionProfile("Z140x50x20x2.0", SectionShapeType.ColdFormedZ, 140, 50, 2.0, 2.0, 0, 0, 20),
                    new SectionProfile("Z160x60x20x2.5", SectionShapeType.ColdFormedZ, 160, 60, 2.5, 2.5, 0, 0, 20),
                    new SectionProfile("Z180x70x20x2.5", SectionShapeType.ColdFormedZ, 180, 70, 2.5, 2.5, 0, 0, 20),
                    new SectionProfile("Z200x70x20x2.5", SectionShapeType.ColdFormedZ, 200, 70, 2.5, 2.5, 0, 0, 20),
                    new SectionProfile("Z220x75x20x3.0", SectionShapeType.ColdFormedZ, 220, 75, 3.0, 3.0, 0, 0, 20),
                    new SectionProfile("Z250x75x20x3.0", SectionShapeType.ColdFormedZ, 250, 75, 3.0, 3.0, 0, 0, 20)
                },
                [SectionShapeType.ZBeam] = new List<SectionProfile>
                {
                    new SectionProfile("Z100x50x3.0", SectionShapeType.ZBeam, 100, 50, 3.0, 3.0),
                    new SectionProfile("Z120x60x4.0", SectionShapeType.ZBeam, 120, 60, 4.0, 4.0),
                    new SectionProfile("Z160x65x4.0", SectionShapeType.ZBeam, 160, 65, 4.0, 4.0),
                    new SectionProfile("Z200x75x5.0", SectionShapeType.ZBeam, 200, 75, 5.0, 5.0)
                },
                [SectionShapeType.ColdFormedU] = new List<SectionProfile>
                {
                    new SectionProfile("U80x40x3.0", SectionShapeType.ColdFormedU, 80, 40, 3.0, 3.0),
                    new SectionProfile("U100x50x3.0", SectionShapeType.ColdFormedU, 100, 50, 3.0, 3.0),
                    new SectionProfile("U120x50x4.0", SectionShapeType.ColdFormedU, 120, 50, 4.0, 4.0),
                    new SectionProfile("U160x60x4.0", SectionShapeType.ColdFormedU, 160, 60, 4.0, 4.0),
                    new SectionProfile("U200x70x5.0", SectionShapeType.ColdFormedU, 200, 70, 5.0, 5.0)
                },
                [SectionShapeType.HatSection] = new List<SectionProfile>
                {
                    new SectionProfile("HAT40x60x20x2.0", SectionShapeType.HatSection, 40, 60, 2.0, 2.0, 0, 0, 20),
                    new SectionProfile("HAT60x80x25x2.5", SectionShapeType.HatSection, 60, 80, 2.5, 2.5, 0, 0, 25),
                    new SectionProfile("HAT80x100x25x3.0", SectionShapeType.HatSection, 80, 100, 3.0, 3.0, 0, 0, 25),
                    new SectionProfile("HAT100x120x30x3.0", SectionShapeType.HatSection, 100, 120, 3.0, 3.0, 0, 0, 30)
                },

                // ================= 4. 空心钢管 =================
                [SectionShapeType.SquareTube] = new List<SectionProfile>
                {
                    new SectionProfile("SHS60x60x4", SectionShapeType.SquareTube, 60, 60, 4, 4),
                    new SectionProfile("SHS80x80x5", SectionShapeType.SquareTube, 80, 80, 5, 5),
                    new SectionProfile("F100x100x6", SectionShapeType.SquareTube, 100, 100, 6, 6),
                    new SectionProfile("F120x120x6", SectionShapeType.SquareTube, 120, 120, 6, 6),
                    new SectionProfile("F150x150x8", SectionShapeType.SquareTube, 150, 150, 8, 8),
                    new SectionProfile("F200x200x10", SectionShapeType.SquareTube, 200, 200, 10, 10),
                    new SectionProfile("SHS250x250x10", SectionShapeType.SquareTube, 250, 250, 10, 10),
                    new SectionProfile("SHS300x300x12", SectionShapeType.SquareTube, 300, 300, 12, 12)
                },
                [SectionShapeType.RectTube] = new List<SectionProfile>
                {
                    new SectionProfile("RHS100x50x4", SectionShapeType.RectTube, 100, 50, 4, 4),
                    new SectionProfile("RHS120x60x5", SectionShapeType.RectTube, 120, 60, 5, 5),
                    new SectionProfile("RHS150x100x6", SectionShapeType.RectTube, 150, 100, 6, 6),
                    new SectionProfile("F200x100x8", SectionShapeType.RectTube, 200, 100, 8, 8),
                    new SectionProfile("RHS250x150x8", SectionShapeType.RectTube, 250, 150, 8, 8),
                    new SectionProfile("RHS300x150x10", SectionShapeType.RectTube, 300, 150, 10, 10),
                    new SectionProfile("RHS300x200x10", SectionShapeType.RectTube, 300, 200, 10, 10)
                },
                [SectionShapeType.PipeTube] = new List<SectionProfile>
                {
                    new SectionProfile("P32x3.2", SectionShapeType.PipeTube, 32, 32, 3.2, 3.2),
                    new SectionProfile("P42x3.5", SectionShapeType.PipeTube, 42, 42, 3.5, 3.5),
                    new SectionProfile("P48x3.5", SectionShapeType.PipeTube, 48, 48, 3.5, 3.5),
                    new SectionProfile("P60x4", SectionShapeType.PipeTube, 60, 60, 4, 4),
                    new SectionProfile("P76x4", SectionShapeType.PipeTube, 76, 76, 4, 4),
                    new SectionProfile("P89x5", SectionShapeType.PipeTube, 89, 89, 5, 5),
                    new SectionProfile("P114x6", SectionShapeType.PipeTube, 114, 114, 6, 6),
                    new SectionProfile("P140x6", SectionShapeType.PipeTube, 140, 140, 6, 6),
                    new SectionProfile("P159x6", SectionShapeType.PipeTube, 159, 159, 6, 6),
                    new SectionProfile("P219x8", SectionShapeType.PipeTube, 219, 219, 8, 8),
                    new SectionProfile("P273x8", SectionShapeType.PipeTube, 273, 273, 8, 8),
                    new SectionProfile("P325x10", SectionShapeType.PipeTube, 325, 325, 10, 10),
                    new SectionProfile("P426x12", SectionShapeType.PipeTube, 426, 426, 12, 12)
                },

                // ================= 5. 实心型材与板材 =================
                [SectionShapeType.FlatBar] = new List<SectionProfile>
                {
                    new SectionProfile("-50x6", SectionShapeType.FlatBar, 50, 6, 6, 6),
                    new SectionProfile("-80x8", SectionShapeType.FlatBar, 80, 8, 8, 8),
                    new SectionProfile("-100x8", SectionShapeType.FlatBar, 100, 8, 8, 8),
                    new SectionProfile("-100x10", SectionShapeType.FlatBar, 100, 10, 10, 10),
                    new SectionProfile("-120x10", SectionShapeType.FlatBar, 120, 10, 10, 10),
                    new SectionProfile("-150x12", SectionShapeType.FlatBar, 150, 12, 12, 12),
                    new SectionProfile("-200x14", SectionShapeType.FlatBar, 200, 14, 14, 14)
                },
                [SectionShapeType.RoundBar] = new List<SectionProfile>
                {
                    new SectionProfile("D12", SectionShapeType.RoundBar, 12, 12, 12, 12),
                    new SectionProfile("D16", SectionShapeType.RoundBar, 16, 16, 16, 16),
                    new SectionProfile("D20", SectionShapeType.RoundBar, 20, 20, 20, 20),
                    new SectionProfile("D22", SectionShapeType.RoundBar, 22, 22, 22, 22),
                    new SectionProfile("D25", SectionShapeType.RoundBar, 25, 25, 25, 25),
                    new SectionProfile("D30", SectionShapeType.RoundBar, 30, 30, 30, 30),
                    new SectionProfile("D36", SectionShapeType.RoundBar, 36, 36, 36, 36)
                },
                [SectionShapeType.SquareBar] = new List<SectionProfile>
                {
                    new SectionProfile("S16x16", SectionShapeType.SquareBar, 16, 16, 16, 16),
                    new SectionProfile("S20x20", SectionShapeType.SquareBar, 20, 20, 20, 20),
                    new SectionProfile("S25x25", SectionShapeType.SquareBar, 25, 25, 25, 25),
                    new SectionProfile("S30x30", SectionShapeType.SquareBar, 30, 30, 30, 30),
                    new SectionProfile("S40x40", SectionShapeType.SquareBar, 40, 40, 40, 40)
                }
            };
        }

        public static List<SectionShapeType> GetShapeTypesByCategory(SectionCategory category)
        {
            return OrderedShapeTypes.Where(t => SectionProfile.GetCategory(t) == category).ToList();
        }

        public static string GetShapeTabTitle(SectionShapeType shapeType)
        {
            return SectionProfile.GetShapeDisplayName(shapeType);
        }

        public static List<SectionProfile> GetByShapeType(SectionShapeType type)
        {
            if (_tables.TryGetValue(type, out var list))
                return list.ToList();
            return new List<SectionProfile>();
        }

        public static List<SectionProfile> GetByCategory(SectionCategory category)
        {
            var result = new List<SectionProfile>();
            foreach (SectionShapeType t in GetShapeTypesByCategory(category))
            {
                if (_tables.TryGetValue(t, out var list))
                    result.AddRange(list);
            }
            return result;
        }

        public static List<SectionProfile> GetSectionsByType(SectionShapeType type) => GetByShapeType(type);

        public static List<string> GetNamesByShapeType(SectionShapeType type)
        {
            if (type == SectionShapeType.RectTube)
            {
                // 兼容原“方矩管”筛选逻辑：同时包含方管与矩形管
                return GetByShapeType(SectionShapeType.SquareTube)
                    .Concat(GetByShapeType(SectionShapeType.RectTube))
                    .Select(x => x.Name)
                    .ToList();
            }
            return GetByShapeType(type).Select(x => x.Name).ToList();
        }

        public static List<SectionProfile> GetAllSections()
        {
            var all = new List<SectionProfile>();
            foreach (SectionShapeType t in OrderedShapeTypes)
            {
                if (_tables.TryGetValue(t, out var list))
                    all.AddRange(list);
            }
            return all;
        }

        public static List<string> GetAllNames()
        {
            return GetAllSections().Select(x => x.Name).ToList();
        }

        public static SectionProfile GetByName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                string key = name.Trim();
                foreach (var list in _tables.Values)
                {
                    var found = list.FirstOrDefault(s => string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase));
                    if (found != null) return found;
                }
            }
            var hList = _tables[SectionShapeType.HBeam];
            return hList.Count > 4 ? hList[4] : hList.FirstOrDefault() ?? new SectionProfile();
        }

        public static SectionProfile GetSectionOrDefault(string name) => GetByName(name);

        public static void AddOrUpdate(SectionProfile profile)
        {
            AddOrUpdateInto(_tables, profile);
        }

        private static void AddOrUpdateInto(Dictionary<SectionShapeType, List<SectionProfile>> tables, SectionProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.Name)) return;
            string name = profile.Name.Trim();

            // 如果同名截面原本在其它分类下，先移除旧记录避免重名冲突
            foreach (var kv in tables)
            {
                if (kv.Key == profile.Shape) continue;
                int oldIdx = kv.Value.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                if (oldIdx >= 0 && kv.Value.Count > 1)
                {
                    kv.Value.RemoveAt(oldIdx);
                }
            }

            if (!tables.TryGetValue(profile.Shape, out var list))
            {
                list = new List<SectionProfile>();
                tables[profile.Shape] = list;
            }

            SectionProfile copy = profile.Clone();
            copy.Name = name;
            int idx = list.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                list[idx] = copy;
            else
                list.Add(copy);
        }

        public static void AddOrUpdateSection(SectionProfile profile) => AddOrUpdate(profile);

        public static bool Remove(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string key = name.Trim();
            foreach (var kv in _tables)
            {
                var list = kv.Value;
                int idx = list.FindIndex(s => string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0 && list.Count > 1)
                {
                    list.RemoveAt(idx);
                    return true;
                }
            }
            return false;
        }

        public static void ResetToDefaults()
        {
            _tables = CreateDefaultTables();
        }

        public static void SaveCatalog()
        {
            try
            {
                string path = GetLocalCatalogCsvPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                ExportCatalogToCsv(path);
            }
            catch { }
        }

        public static void ExportCatalogToCsv(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("截面代号(Name),截面类型(ShapeType),高度H(mm),宽度B(mm),腹板厚Tw(mm),翼缘厚Tf(mm),下翼缘宽B2(mm),下翼缘厚Tf2(mm),卷边C(mm),双拼间隙Gap(mm),截面积(cm2),理论单重(kg/m),表面积(m2/m)");
            foreach (var s in GetAllSections())
            {
                sb.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2:0.##},{3:0.##},{4:0.##},{5:0.##},{6:0.##},{7:0.##},{8:0.##},{9:0.##},{10:0.00},{11:0.00},{12:0.000}\r\n",
                    s.Name, s.Shape, s.H, s.B, s.Tw, s.Tf, s.B2, s.Tf2, s.Lip, s.Gap,
                    s.AreaCm2, s.UnitWeightKgPerM, s.PerimeterMPerM);
            }
            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>
        /// 按 5 大类工作表导出多 Sheet Excel (.xlsx)，包含全部扩展参数列
        /// </summary>
        public static void ExportCatalogToXlsx(string filePath)
        {
            if (File.Exists(filePath)) File.Delete(filePath);

            string[] sheetNames =
            {
                "1_热轧标准型钢",
                "2_焊接与组合型钢",
                "3_冷弯薄壁型钢",
                "4_空心钢管",
                "5_实心型材与板材"
            };

            using (FileStream fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.ReadWrite))
            using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "[Content_Types].xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/worksheets/sheet2.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/worksheets/sheet3.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/worksheets/sheet4.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/worksheets/sheet5.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
</Types>");

                WriteEntry(archive, "_rels/.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>");

                var wbSb = new StringBuilder();
                wbSb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
                wbSb.AppendLine(@"<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">");
                wbSb.AppendLine("  <sheets>");
                for (int i = 0; i < sheetNames.Length; i++)
                {
                    wbSb.AppendLine($@"    <sheet name=""{SecurityElement.Escape(sheetNames[i])}"" sheetId=""{i + 1}"" r:id=""rId{i + 1}""/>");
                }
                wbSb.AppendLine("  </sheets>");
                wbSb.AppendLine("</workbook>");
                WriteEntry(archive, "xl/workbook.xml", wbSb.ToString());

                WriteEntry(archive, "xl/_rels/workbook.xml.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
  <Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet2.xml""/>
  <Relationship Id=""rId3"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet3.xml""/>
  <Relationship Id=""rId4"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet4.xml""/>
  <Relationship Id=""rId5"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet5.xml""/>
</Relationships>");

                for (int i = 0; i < OrderedCategories.Length; i++)
                {
                    var list = GetByCategory(OrderedCategories[i]);
                    WriteEntry(archive, $"xl/worksheets/sheet{i + 1}.xml", BuildSectionSheetXml(list));
                }
            }
        }

        public static int ImportCatalogFromFile(string filePath, bool replaceExisting = false)
        {
            if (!File.Exists(filePath)) return 0;
            if (replaceExisting)
            {
                foreach (var k in _tables.Keys.ToList()) _tables[k].Clear();
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".csv")
            {
                return ImportFromCsvInto(_tables, filePath);
            }

            int importedCount = 0;
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                List<string> sharedStrings = new List<string>();
                ZipArchiveEntry ssEntry = archive.GetEntry("xl/sharedStrings.xml");
                if (ssEntry != null)
                {
                    using (Stream s = ssEntry.Open())
                    {
                        XmlDocument doc = new XmlDocument();
                        doc.Load(s);
                        XmlNodeList tNodes = doc.GetElementsByTagName("t");
                        foreach (XmlNode tn in tNodes)
                        {
                            sharedStrings.Add(tn.InnerText);
                        }
                    }
                }

                // 兼容旧版 5 工作表默认类型映射
                SectionShapeType[] legacyTypes =
                {
                    SectionShapeType.HBeam,
                    SectionShapeType.Channel,
                    SectionShapeType.Angle,
                    SectionShapeType.RectTube,
                    SectionShapeType.PipeTube
                };

                var sheetEntries = archive.Entries
                    .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                             && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = 0; i < sheetEntries.Count; i++)
                {
                    SectionShapeType fallbackType = i < legacyTypes.Length ? legacyTypes[i] : SectionShapeType.HBeam;

                    using (Stream s = sheetEntries[i].Open())
                    {
                        XmlDocument doc = new XmlDocument();
                        doc.Load(s);
                        XmlNodeList rowNodes = doc.GetElementsByTagName("row");

                        foreach (XmlNode rowNode in rowNodes)
                        {
                            var rAttr = rowNode.Attributes?["r"];
                            if (rAttr != null && rAttr.Value == "1") continue;

                            List<string> cellVals = new List<string>();
                            foreach (XmlNode cNode in rowNode.ChildNodes)
                            {
                                if (cNode.LocalName != "c") continue;
                                string tAttr = cNode.Attributes?["t"]?.Value ?? "";
                                string val = "";
                                if (tAttr == "inlineStr")
                                {
                                    val = cNode.InnerText;
                                }
                                else if (tAttr == "s")
                                {
                                    if (int.TryParse(cNode.InnerText, out int ssIdx) && ssIdx >= 0 && ssIdx < sharedStrings.Count)
                                        val = sharedStrings[ssIdx];
                                }
                                else
                                {
                                    val = cNode.InnerText;
                                }
                                cellVals.Add(val.Trim());
                            }

                            if (cellVals.Count < 5 || string.IsNullOrWhiteSpace(cellVals[0])) continue;

                            // 新格式：[0]Name, [1]ShapeType, [2]H, [3]B, [4]Tw, [5]Tf, [6]B2, [7]Tf2, [8]Lip, [9]Gap
                            if (cellVals.Count >= 6 && Enum.TryParse(cellVals[1], true, out SectionShapeType parsedShape) &&
                                TryParsePositive(cellVals[2], out double nh) &&
                                TryParseNonNegative(cellVals[3], out double nb) &&
                                TryParseNonNegative(cellVals[4], out double ntw) &&
                                TryParseNonNegative(cellVals[5], out double ntf))
                            {
                                double b2 = cellVals.Count > 6 && TryParseNonNegative(cellVals[6], out double vB2) ? vB2 : 0.0;
                                double tf2 = cellVals.Count > 7 && TryParseNonNegative(cellVals[7], out double vTf2) ? vTf2 : 0.0;
                                double lip = cellVals.Count > 8 && TryParseNonNegative(cellVals[8], out double vLip) ? vLip : 0.0;
                                double gap = cellVals.Count > 9 && TryParseNonNegative(cellVals[9], out double vGap) ? vGap : 0.0;

                                AddOrUpdate(new SectionProfile(cellVals[0], parsedShape, nh, nb, ntw, ntf, b2, tf2, lip, gap));
                                importedCount++;
                            }
                            // 兼容旧格式：[0]Name, [1]H, [2]B, [3]Tw, [4]Tf
                            else if (TryParsePositive(cellVals[1], out double h) &&
                                     TryParsePositive(cellVals[2], out double b) &&
                                     TryParsePositive(cellVals[3], out double tw) &&
                                     TryParsePositive(cellVals[4], out double tf))
                            {
                                AddOrUpdate(new SectionProfile(cellVals[0], fallbackType, h, b, tw, tf));
                                importedCount++;
                            }
                        }
                    }
                }
            }

            return importedCount;
        }

        private static int ImportFromCsvInto(Dictionary<SectionShapeType, List<SectionProfile>> tables, string filePath)
        {
            int count = 0;
            bool isNewFormat = false;
            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (i == 0 && (line.Contains("ShapeType") || line.Contains("截面代号")))
                {
                    isNewFormat = line.Contains("B2") || line.Contains("Lip") || line.Contains("Gap");
                    continue;
                }

                string[] parts = line.Split(',');
                if (parts.Length < 6) continue;
                if (Enum.TryParse(parts[1].Trim(), true, out SectionShapeType shape) &&
                    TryParsePositive(parts[2], out double h) &&
                    TryParseNonNegative(parts[3], out double b) &&
                    TryParseNonNegative(parts[4], out double tw) &&
                    TryParseNonNegative(parts[5], out double tf))
                {
                    double b2 = 0.0, tf2 = 0.0, lip = 0.0, gap = 0.0;
                    if (isNewFormat && parts.Length >= 10)
                    {
                        TryParseNonNegative(parts[6], out b2);
                        TryParseNonNegative(parts[7], out tf2);
                        TryParseNonNegative(parts[8], out lip);
                        TryParseNonNegative(parts[9], out gap);
                    }

                    AddOrUpdateInto(tables, new SectionProfile(parts[0].Trim(), shape, h, b, tw, tf, b2, tf2, lip, gap));
                    count++;
                }
            }
            return count;
        }

        private static bool TryParsePositive(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value) && value > 0;
        }

        private static bool TryParseNonNegative(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value) && value >= 0;
        }

        private static string BuildSectionSheetXml(List<SectionProfile> list)
        {
            var sb = new StringBuilder();
            sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
            sb.Append(@"<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><sheetData>");
            sb.Append(@"<row r=""1"">");
            string[] cols =
            {
                "截面代号(Name)",
                "截面类型(ShapeType)",
                "高度/外径H(mm)",
                "宽度B(mm)",
                "腹板/壁厚Tw(mm)",
                "翼缘厚Tf(mm)",
                "下翼缘宽B2(mm)",
                "下翼缘厚Tf2(mm)",
                "卷边C(mm)",
                "双拼间隙Gap(mm)",
                "截面积(cm2)",
                "理论米重(kg/m)",
                "表面积(m2/m)"
            };
            for (int c = 0; c < cols.Length; c++)
            {
                char colChar = (char)('A' + c);
                sb.Append($@"<c r=""{colChar}1"" t=""inlineStr""><is><t>{SecurityElement.Escape(cols[c])}</t></is></c>");
            }
            sb.Append(@"</row>");

            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                int r = i + 2;
                sb.Append($@"<row r=""{r}"">");
                sb.Append($@"<c r=""A{r}"" t=""inlineStr""><is><t>{SecurityElement.Escape(s.Name)}</t></is></c>");
                sb.Append($@"<c r=""B{r}"" t=""inlineStr""><is><t>{s.Shape}</t></is></c>");
                sb.Append($@"<c r=""C{r}""><v>{s.H.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""D{r}""><v>{s.B.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""E{r}""><v>{s.Tw.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""F{r}""><v>{s.Tf.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""G{r}""><v>{s.B2.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""H{r}""><v>{s.Tf2.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""I{r}""><v>{s.Lip.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""J{r}""><v>{s.Gap.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""K{r}""><v>{s.AreaCm2.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""L{r}""><v>{s.UnitWeightKgPerM.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append($@"<c r=""M{r}""><v>{s.PerimeterMPerM.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sb.Append(@"</row>");
            }

            sb.Append(@"</sheetData></worksheet>");
            return sb.ToString();
        }

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
            ZipArchiveEntry e = archive.CreateEntry(path, CompressionLevel.Optimal);
            using (Stream s = e.Open())
            using (StreamWriter sw = new StreamWriter(s, new UTF8Encoding(false)))
            {
                sw.Write(content);
            }
        }
    }
}
