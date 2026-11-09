using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using SteelStructureSuite.Domain.Models;
using SteelStructureSuite.Ribbon;
using SteelStructureSuite.Services.Interaction;
using SteelStructureSuite.Services.Modeling;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(SteelStructureSuite.Core.PluginApplication))]

namespace SteelStructureSuite.Core
{
    public class PluginApplication : IExtensionApplication
    {
        private static SteelGripOverrule _gripOverrule;
        private static bool _overrulesRegistered;
        private static bool _doubleClickBound;
        private static bool _ribbonAutoLoaded;

        public static bool IsOverruleEnabled => _overrulesRegistered;

        public void Initialize()
        {
            try
            {
                EnableOverrules();
                SteelContextMenu.RegisterContextMenu();
                BindDoubleClickEditHandler();

                Application.Idle += OnApplicationIdleAutoLoadRibbon;
                ComponentManager.ItemInitialized += OnComponentManagerItemInitialized;
                TryLoadRibbonImmediately();

                Editor ed = Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage(
                    "\n==================================================================" +
                    "\n[SteelStructureSuite] 参数化钢结构套件已加载 (Ribbon 已自动激活)" +
                    "\n  - 模型管理: SS_MANAGER      截面管理: SS_SECTIONS" +
                    "\n  - 材料统计: SS_BOM          施工详图: SS_DRAWING" +
                    "\n  - 开启复写: SS_OVERRULE_ON  关闭复写: SS_OVERRULE_OFF" +
                    "\n==================================================================\n");
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    $"\n[SteelStructureSuite] 初始化异常: {ex.Message}\n");
            }
        }

        public void Terminate()
        {
            Application.Idle -= OnApplicationIdleAutoLoadRibbon;
            ComponentManager.ItemInitialized -= OnComponentManagerItemInitialized;
            DisableOverrules();
            SteelContextMenu.UnregisterContextMenu();
        }

        private static void TryLoadRibbonImmediately()
        {
            if (_ribbonAutoLoaded) return;
            if (ComponentManager.Ribbon != null)
            {
                SteelRibbonBuilder.BuildOrShowRibbon(activateTab: true);
                _ribbonAutoLoaded = true;
            }
        }

        private static void OnApplicationIdleAutoLoadRibbon(object sender, EventArgs e)
        {
            if (ComponentManager.Ribbon != null)
            {
                Application.Idle -= OnApplicationIdleAutoLoadRibbon;
                if (!_ribbonAutoLoaded)
                {
                    SteelRibbonBuilder.BuildOrShowRibbon(activateTab: true);
                    _ribbonAutoLoaded = true;
                }
            }
        }

        private static void OnComponentManagerItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon != null)
            {
                ComponentManager.ItemInitialized -= OnComponentManagerItemInitialized;
                if (!_ribbonAutoLoaded)
                {
                    SteelRibbonBuilder.BuildOrShowRibbon(activateTab: true);
                    _ribbonAutoLoaded = true;
                }
            }
        }

        public static void EnableOverrules()
        {
            if (_overrulesRegistered) return;

            RXClass solidClass = RXObject.GetClass(typeof(Solid3d));
            _gripOverrule = new SteelGripOverrule();
            Overrule.AddOverrule(solidClass, _gripOverrule, true);
            Overrule.Overruling = true;
            _overrulesRegistered = true;
        }

        public static void DisableOverrules()
        {
            if (!_overrulesRegistered) return;

            RXClass solidClass = RXObject.GetClass(typeof(Solid3d));
            if (_gripOverrule != null)
            {
                Overrule.RemoveOverrule(solidClass, _gripOverrule);
                _gripOverrule = null;
            }
            _overrulesRegistered = false;
        }

        private static void BindDoubleClickEditHandler()
        {
            if (_doubleClickBound) return;
            Application.BeginDoubleClick += OnBeginDoubleClick;
            _doubleClickBound = true;
        }

        private static void OnBeginDoubleClick(object sender, BeginDoubleClickEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                PromptSelectionResult psr = doc.Editor.SelectImplied();
                if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count != 1)
                    return;

                ObjectId id = psr.Value.GetObjectIds()[0];
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Solid3d solid &&
                        SteelDataStorage.TryReadParams(solid, out SteelStructureParams _))
                    {
                        doc.SendStringToExecute("SS_EDIT ", true, false, false);
                    }
                    tr.Commit();
                }
            }
            catch { }
        }
    }
}
