namespace SteelStructureSuite.Domain.Models
{
    public class MemberPartBillItem
    {
        public string ParentTag { get; set; }
        public int PartNo { get; set; }
        public string PartRole { get; set; }
        public string SectionName { get; set; }
        public string ShapeTypeName { get; set; }
        public string Material { get; set; }
        public int Count { get; set; }
        public double SingleLengthMm { get; set; }
        public double UnitWeightKgPerM { get; set; }
        public double TotalWeightKg { get; set; }
        public double TotalSurfaceAreaM2 { get; set; }
    }
}
