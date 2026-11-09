using Autodesk.AutoCAD.DatabaseServices;

namespace SteelStructureSuite.Domain.Models
{
    public class StructureRecordItem
    {
        public ObjectId EntityId { get; set; }
        public SteelStructureParams Params { get; set; }
        public string Tag => Params.Tag;
        public string TypeName => Params.StructureTypeDisplayName;
        public string SizeSummary => $"{Params.Span:0}×{Params.Length:0}×{Params.Height:0}";
        public string MainSection => Params.ColumnSection;
        public string Material => Params.Material;
        public double WeightKg { get; set; }
        public string PositionStr => $"({Params.Origin.X:0}, {Params.Origin.Y:0}, {Params.Origin.Z:0})";
    }
}
