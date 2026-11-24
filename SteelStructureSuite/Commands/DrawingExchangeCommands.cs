using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using SteelStructureSuite.Services.Interaction;
using SteelStructureSuite.Services.Settings;
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
            // 非模态窗口：转换作业由 MainThreadDispatcher 排到 AutoCAD 主线程执行，
            // 窗口只负责收集参数与显示日志，避免在 WPF 线程上操作 AutoCAD 数据库。
            UserSettings.ShowModelessWindowOnAutoCad(new DwgToDxfWindow());
        }

        [CommandMethod("SS_DWGMERGE", CommandFlags.Modal)]
        public void MergeDwgs()
        {
            if (Application.DocumentManager.MdiActiveDocument == null) return;
            var window = new DwgMergeWindow();
            UserSettings.ShowModelessWindowOnAutoCad(window);
        }

        /// <summary>
        /// 内部命令：执行 MainThreadDispatcher 队列里的 CAD 作业。
        /// 由窗口通过 SendStringToExecute 触发，保证在 AutoCAD 主线程上运行。
        /// </summary>
        [CommandMethod("SS_INTERNAL_RUN", CommandFlags.Modal)]
        public void RunPendingTasks()
        {
            MainThreadDispatcher.RunPending();
        }
    }
}
