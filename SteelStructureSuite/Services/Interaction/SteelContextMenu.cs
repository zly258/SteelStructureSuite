using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SteelStructureSuite.Services.Interaction
{
    /// <summary>
    /// 参数化钢结构右键快捷菜单（借鉴手册第 12 页：选中模型后右键直接弹出“修改钢结构参数(M)”等快捷项）
    /// </summary>
    public static class SteelContextMenu
    {
        private static ContextMenuExtension _menuExt;

        public static void RegisterContextMenu()
        {
            if (_menuExt != null) return;

            _menuExt = new ContextMenuExtension
            {
                Title = "参数化钢结构操作"
            };

            MenuItem itemEdit = new MenuItem("修改钢结构参数(M)...");
            itemEdit.Click += (s, e) => SendCommand("SS_EDIT ");
            _menuExt.MenuItems.Add(itemEdit);

            MenuItem itemDraw = new MenuItem("生成 HLR 消隐与自动标注详图(D)...");
            itemDraw.Click += (s, e) => SendCommand("SS_DRAWING ");
            _menuExt.MenuItems.Add(itemDraw);

            MenuItem itemBom = new MenuItem("材料统计与导出 Excel .xlsx (B)...");
            itemBom.Click += (s, e) => SendCommand("SS_BOM ");
            _menuExt.MenuItems.Add(itemBom);

            RXClass targetClass = RXObject.GetClass(typeof(Solid3d));
            Application.AddObjectContextMenuExtension(targetClass, _menuExt);
        }

        public static void UnregisterContextMenu()
        {
            if (_menuExt == null) return;
            try
            {
                RXClass targetClass = RXObject.GetClass(typeof(Solid3d));
                Application.RemoveObjectContextMenuExtension(targetClass, _menuExt);
            }
            catch { }
            _menuExt = null;
        }

        private static void SendCommand(string cmd)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute(cmd, true, false, true);
        }
    }
}
