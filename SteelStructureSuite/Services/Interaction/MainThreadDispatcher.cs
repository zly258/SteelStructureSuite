using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SteelStructureSuite.Services.Interaction
{
    /// <summary>
    /// 把需要在 AutoCAD 主线程上执行的 CAD 作业排队执行。
    ///
    /// WPF 窗口（模态或非模态）运行在自己的 Dispatcher 线程上，
    /// 而 AutoCAD 的数据库对象（Database / Transaction / Entity）只允许在
    /// AutoCAD 主线程上创建与释放。在窗口事件里直接做合并、转 DXF 之类的
    /// 数据库操作，会在离开命令上下文后触发原生访问冲突，
    /// 甚至写出无法打开的 DWG。
    ///
    /// 用法：窗口里 <see cref="Post"/> 一个动作，AutoCAD 主线程通过
    /// SS_INTERNAL_RUN 命令执行队列中的动作，再由窗口自己回填界面。
    /// </summary>
    public static class MainThreadDispatcher
    {
        private const string RunCommand = "SS_INTERNAL_RUN ";

        private static readonly object Sync = new object();
        private static readonly Queue<Action> Pending = new Queue<Action>();

        /// <summary>排队一个在 AutoCAD 主线程上执行的动作。</summary>
        public static void Post(Action action)
        {
            if (action == null) return;
            if (Application.DocumentManager.MdiActiveDocument == null)
            {
                throw new InvalidOperationException("没有活动文档，无法执行 CAD 作业。");
            }
            lock (Sync)
            {
                Pending.Enqueue(action);
            }
            RequestRun();
        }

        private static void RequestRun()
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                doc.SendStringToExecute(RunCommand, true, false, false);
            }
            catch (System.Exception)
            {
                // 没有活动文档时（例如全部文档已关闭）丢弃这次调度。
            }
        }

        /// <summary>
        /// 在 AutoCAD 主线程上执行排队中的动作。由 SS_INTERNAL_RUN 命令调用。
        /// </summary>
        public static void RunPending()
        {
            while (true)
            {
                Action action;
                lock (Sync)
                {
                    if (Pending.Count == 0) return;
                    action = Pending.Dequeue();
                }

                try
                {
                    action();
                }
                catch (System.Exception ex)
                {
                    try
                    {
                        Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                            "\n[SteelStructureSuite] 任务执行失败：" + ex.Message + "\n");
                    }
                    catch (System.Exception)
                    {
                        // 命令行不可用时忽略。
                    }
                }
            }
        }
    }
}
