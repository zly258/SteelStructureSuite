namespace SteelStructureSuite.Domain.Enums
{
    /// <summary>
    /// 截面所属工艺/形态大类（用于侧边分类树一级分组）
    /// </summary>
    public enum SectionCategory
    {
        /// <summary>热轧标准型钢</summary>
        HotRolled = 0,
        /// <summary>焊接与组合型钢</summary>
        WeldedComposite = 1,
        /// <summary>冷弯薄壁型钢</summary>
        ColdFormed = 2,
        /// <summary>空心钢管</summary>
        HollowTube = 3,
        /// <summary>实心型材与板材</summary>
        SolidBar = 4
    }

    /// <summary>
    /// 钢结构截面类型（0~4 保持与旧版本兼容）
    /// </summary>
    public enum SectionShapeType
    {
        // ---- 兼容原有 5 类（0~4 编号保持不变） ----
        /// <summary>热轧 H 型钢 (HW/HM/HN)</summary>
        HBeam = 0,
        /// <summary>热轧普通槽钢 (C)</summary>
        Channel = 1,
        /// <summary>热轧等边角钢 (L)</summary>
        Angle = 2,
        /// <summary>矩形空心管 (RHS)</summary>
        RectTube = 3,
        /// <summary>圆钢管 (CHS/P)</summary>
        PipeTube = 4,

        // ---- 热轧标准型钢扩展 ----
        /// <summary>热轧工字钢 (I)</summary>
        IBeam = 5,
        /// <summary>热轧不等边角钢 (L)</summary>
        UnequalAngle = 6,
        /// <summary>热轧剖分 T 型钢 (TW/TM/TN)</summary>
        TBeam = 7,

        // ---- 焊接与组合型钢 ----
        /// <summary>焊接等翼缘 H 型钢 (BH/WH)</summary>
        WeldedHBeam = 8,
        /// <summary>不等翼缘焊接 H 型钢 (BHU)</summary>
        WeldedHBeamUnequal = 9,
        /// <summary>焊接箱型截面 (BOX)</summary>
        WeldedBox = 10,
        /// <summary>焊接 T 型钢 (WT/BT)</summary>
        WeldedTBeam = 11,
        /// <summary>焊接十字形截面 (CROSS)</summary>
        WeldedCross = 12,
        /// <summary>双角钢组合截面 (2L)</summary>
        DoubleAngle = 13,
        /// <summary>双槽钢组合截面 (2C)</summary>
        DoubleChannel = 14,

        // ---- 冷弯薄壁型钢 ----
        /// <summary>冷弯卷边 C 型钢</summary>
        ColdFormedC = 15,
        /// <summary>冷弯卷边 Z 型钢</summary>
        ColdFormedZ = 16,
        /// <summary>无卷边 Z 型钢 (Z)</summary>
        ZBeam = 17,
        /// <summary>冷弯 U 型钢 (无卷边槽)</summary>
        ColdFormedU = 18,
        /// <summary>帽型钢 / Ω 型钢 (Hat)</summary>
        HatSection = 19,

        // ---- 空心钢管扩展 ----
        /// <summary>方形空心管 (SHS)</summary>
        SquareTube = 20,

        // ---- 实心型材与板材 ----
        /// <summary>扁钢 / 钢板条 (Flat)</summary>
        FlatBar = 21,
        /// <summary>实心圆钢 (Round)</summary>
        RoundBar = 22,
        /// <summary>实心方钢 (Square)</summary>
        SquareBar = 23
    }
}
