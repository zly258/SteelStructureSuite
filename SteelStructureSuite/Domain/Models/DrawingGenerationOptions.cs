namespace SteelStructureSuite.Domain.Models
{
    public class DrawingGenerationOptions
    {
        public string SheetSizeName { get; set; } = "A2"; // A1, A2, A3, A4
        public bool IncludeFrontView { get; set; } = true;
        public bool IncludeSideView { get; set; } = true;
        public bool IncludePlanView { get; set; } = true;
        public bool IncludeIsoView { get; set; } = false;  // 轴测图默认关闭：三视图符号化更干净、精度更可控
        public bool IncludeAutoDimensions { get; set; } = true;
        public bool IncludeLeaderAnnotations { get; set; } = true;
        public bool IncludeEmbeddedBomTable { get; set; } = true;
        public double DrawingScale { get; set; } = 20.0;
        public bool AutoScale { get; set; } = true;
        public bool OutputToNewDwgFile { get; set; } = false;
        public string NewDwgFilePath { get; set; } = string.Empty;
        public bool OpenNewDwgAfterSave { get; set; } = true;

        /// <summary>是否绘制接头局部放大示意图（斜接/切割处的尺寸图）。</summary>
        public bool IncludeJointDetailViews { get; set; } = false;

        /// <summary>批量出图时是否把完全相同的构件合并为一张图。</summary>
        public bool ReuseIdenticalStructures { get; set; } = true;

        /// <summary>批量出图时是否生成图纸目录表。</summary>
        public bool IncludeDrawingIndex { get; set; } = true;

        /// <summary>批量出图时是否生成汇总材料表。</summary>
        public bool IncludeSummaryBom { get; set; } = true;

        public DrawingGenerationOptions Clone()
        {
            return new DrawingGenerationOptions
            {
                SheetSizeName = SheetSizeName,
                IncludeFrontView = IncludeFrontView,
                IncludeSideView = IncludeSideView,
                IncludePlanView = IncludePlanView,
                IncludeIsoView = IncludeIsoView,
                IncludeAutoDimensions = IncludeAutoDimensions,
                IncludeLeaderAnnotations = IncludeLeaderAnnotations,
                IncludeEmbeddedBomTable = IncludeEmbeddedBomTable,
                DrawingScale = DrawingScale,
                AutoScale = AutoScale,
                OutputToNewDwgFile = OutputToNewDwgFile,
                NewDwgFilePath = NewDwgFilePath,
                OpenNewDwgAfterSave = OpenNewDwgAfterSave,
                IncludeJointDetailViews = IncludeJointDetailViews,
                ReuseIdenticalStructures = ReuseIdenticalStructures,
                IncludeDrawingIndex = IncludeDrawingIndex,
                IncludeSummaryBom = IncludeSummaryBom
            };
        }
    }
}
