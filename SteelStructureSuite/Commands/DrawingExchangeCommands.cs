using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using SteelStructureSuite.Views;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(SteelStructureSuite.Commands.DrawingExchangeCommands))]

namespace SteelStructureSuite.Commands
{
    public class DrawingExchangeCommands
    {
        [CommandMethod("SS_DWG2DXF", CommandFlags.Modal)]
        public void ConvertDwgToDxf()
        {
            if (Application.DocumentManager.MdiActiveDocument == null) return;
            Application.ShowModalWindow(new DwgToDxfWindow());
        }

        [CommandMethod("SS_DWGMERGE", CommandFlags.Modal)]
        public void MergeDwgs()
        {
            if (Application.DocumentManager.MdiActiveDocument == null) return;
            var window = new DwgMergeWindow();
            Application.ShowModalWindow(window);
            if (window.OpenWhenClosed && !string.IsNullOrEmpty(window.SavedPath) && System.IO.File.Exists(window.SavedPath))
                Application.DocumentManager.Open(window.SavedPath, false);
        }
    }
}
