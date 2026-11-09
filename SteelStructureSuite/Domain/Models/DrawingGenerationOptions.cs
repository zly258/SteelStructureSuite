namespace SteelStructureSuite.Domain.Models
{
    public class DrawingGenerationOptions
    {
        public string SheetSizeName { get; set; } = "A2"; // A1, A2, A3, A4
        public bool IncludeFrontView { get; set; } = true;
        public bool IncludeSideView { get; set; } = true;
        public bool IncludePlanView { get; set; } = true;
        public bool IncludeIsoView { get; set; } = true;
        public bool IncludeAutoDimensions { get; set; } = true;
        public bool IncludeLeaderAnnotations { get; set; } = true;
        public bool IncludeEmbeddedBomTable { get; set; } = true;
        public double DrawingScale { get; set; } = 20.0;
        public bool AutoScale { get; set; } = true;
        public bool OutputToNewDwgFile { get; set; } = false;
        public string NewDwgFilePath { get; set; } = string.Empty;
        public bool OpenNewDwgAfterSave { get; set; } = true;
    }
}
