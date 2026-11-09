namespace SteelStructureSuite.Domain.Enums
{
    public enum ParametricStructureType
    {
        PortalFrame = 0,          // 1. 单跨门式刚架
        MultiSpanPortalFrame = 1, // 2. 多跨连续门式刚架
        BracedFrame = 2,          // 3. 带人字斜撑框架
        DoubleTierFrame = 3,      // 4. 双层管廊支架
        MultiBaySpaceFrame = 4,   // 5. 多层空间装置钢框架
        CantileverBracket = 5,    // 6. 悬臂三角托架
        SpacePlatform = 6,        // 7. 四柱空间检修钢平台(带护栏)
        CantileverWalkway = 7,    // 8. 悬挑走道平台(带护栏，支持线性绘制)
        RoofTruss = 8,            // 9. 双坡屋面钢桁架
        PipeBridgeTruss = 9,      // 10. 空间管廊桁架桥(支持线性绘制)
        SpiralStair = 10,         // 11. 储罐连续螺旋盘梯
        StraightStair = 11,       // 12. 直跑钢斜梯(带双侧连续扶手栏杆，支持线性绘制)
        SafetyCagedLadder = 12,   // 13. 带安全护笼钢直梯(GB 4053.1)
        HandrailGuardrail = 13,   // 14. 平台连续安全栏杆/扶手(GB 4053.3，支持线性绘制)
        LinearSteelMember = 14,   // 15. 线性单根型钢构件(梁/柱/斜撑，支持两点三维动态绘制)
        VesselSupport = 15,       // 16. 容器鞍式钢支座
        ArcSteelMember = 16,      // 17. 单根弧形型钢（截面沿圆弧扫略，可两点绘制）
        SpiralSteelMember = 17    // 18. 单根螺旋型钢（截面沿螺旋线扫略，可两点绘制）
    }
}
