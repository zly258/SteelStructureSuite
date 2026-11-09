namespace SteelStructureSuite.Domain.Models
{
    public class SectionSummaryRow
    {
        public int Index { get; set; }
        public string PartNoRange { get; set; }
        public string SectionName { get; set; }
        public string ShapeTypeName { get; set; }
        public string Material { get; set; }
        public int TotalCount { get; set; }
        public double TotalLengthM { get; set; }
        public double UnitWeightKgPerM { get; set; }
        public double TotalWeightKg { get; set; }
        public double TotalSurfaceAreaM2 { get; set; }
    }
}
