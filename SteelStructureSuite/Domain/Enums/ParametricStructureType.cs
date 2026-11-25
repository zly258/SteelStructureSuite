namespace SteelStructureSuite.Domain.Enums
{
    public enum ParametricStructureType
    {
        // 一、厂房框架与桁架 (Frames & Trusses)
        PortalFrame = 0,            // 1. 单跨门式刚架
        MultiSpanPortalFrame = 1,   // 2. 多跨连续门式刚架
        BracedFrame = 2,            // 3. 带人字斜撑框架
        DoubleTierFrame = 3,        // 4. 双层管廊支架
        MultiBaySpaceFrame = 4,     // 5. 多层空间装置钢框架
        RoofTruss = 8,              // 6. 双坡屋面钢桁架
        PipeBridgeTruss = 9,        // 7. 空间管廊桁架桥(支持线性绘制)

        // 二、平台、托架与梯道栏杆 (Platforms, Stairs & Handrails)
        CantileverBracket = 5,      // 8. 悬臂三角托架
        SpacePlatform = 6,          // 9. 四柱空间检修钢平台(带护栏)
        CantileverWalkway = 7,      // 10. 悬挑走道平台(带护栏，支持线性绘制)
        SpiralStair = 10,           // 11. 储罐连续螺旋盘梯
        StraightStair = 11,         // 12. 直跑钢斜梯(带双侧扶手，支持线性绘制)
        SafetyCagedLadder = 12,     // 13. 带安全护笼钢直梯(GB 4053.1)
        HandrailGuardrail = 13,     // 14. 平台连续安全栏杆/扶手(GB 4053.3，支持线性绘制)

        // 三、单根型钢构件 (Single Profile Members)
        LinearSteelMember = 14,     // 15. 直线单根型钢(梁/柱/撑，支持两点三维动态绘制)
        ArcSteelMember = 16,        // 16. 弧形单根型钢(选择圆弧后截面扫掠)
        SpiralSteelMember = 17,     // 17. 螺旋单根型钢(选择螺旋线后截面扫掠)

        // 四、化工行业标准组件 (Chemical & Petrochemical - HG/T 21574, HG/T 21629, NB/T 47041, SH/T 3070)
        VesselSupport = 15,         // 18. 卧式容器鞍式钢支座(NB/T 47042)
        TowerCircularPlatform = 18, // 19. 塔器环形检修平台(HG/T 21574)
        VerticalVesselSkirt = 19,   // 20. 立式塔器/容器钢裙座(NB/T 47041)
        PipeShoeSupport = 20,       // 21. 化工管道加劲管托/支座(HG/T 21629)
        TankRoofHandrail = 21,      // 22. 储罐拱顶环形安全栏杆(SH/T 3070)

        // 五、电力与冶金行业标准组件 (Electric Power & Metallurgy - DL/T 5218, DL/T 5221, 16G519, 19G518)
        SubstationGantryFrame = 22, // 23. 变电站A字型龙门构架(DL/T 5218)
        CableTrenchSupport = 23,    // 24. 电力电缆沟多层支架(DL/T 5221，支持线性绘制)
        CraneRunwayGirder = 24,     // 25. 冶金厂房重型吊车梁系统(16G519，支持线性绘制)
        CrossBracingFrame = 25,     // 26. 冶金厂房柱间交叉支撑(19G518，支持线性绘制)

        // 六、区域组合构件 (Zone Assemblies - 一次布置一整片区域，内部复用单构件构建器)
        PlatformAssembly = 26,      // 27. 钢平台成套(柱+主次梁+铺板+栏杆+斜梯)
        ColumnRowAssembly = 27,     // 28. 厂房柱列(柱+柱间支撑+吊车梁+系杆，按柱距阵列)
        PipeRackAssembly = 28,      // 29. 管廊成套区段(多层管廊按跨数生成)
        StairAssembly = 29,         // 30. 楼梯成套(梯梁+踏步+扶手+平台)
        PortalHallAssembly = 30,    // 31. 轻钢厂房成套(多榀门架+屋面檩条+系杆+柱间支撑)
        WalkwayBridgeAssembly = 31, // 32. 连廊通廊成套(支墩柱+纵横梁+花纹板+双侧栏杆+支撑)
        EquipmentFrameAssembly = 32,// 33. 设备框架成套(多层多跨框架柱+各层主次梁+顶板+护栏+支撑)
        CanopyAssembly = 33         // 34. 钢雨棚成套(多榀立柱+悬挑斜梁+斜撑+纵向檩条)
    }
}
