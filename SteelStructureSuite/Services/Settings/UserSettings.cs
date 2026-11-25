using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using SteelStructureSuite.Domain.Enums;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SteelStructureSuite.Services.Settings
{
    /// <summary>
    /// 用户界面设置持久化服务。
    ///
    /// 所有界面（出图设置、结构编辑、截面库、材料统计、构件管理）都通过本服务
    /// 读写“上次使用的设置”，重启 AutoCAD 后自动恢复，避免每次重复勾选。
    ///
    /// 存储位置：%APPDATA%\SteelStructureSuite\settings.json
    /// 存储格式：扁平字符串键值表（值统一以字符串保存，便于跨版本兼容）。
    /// </summary>
    public static class UserSettings
    {
        private static readonly object _sync = new object();
        private static Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        private static string SettingsDirectory
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(root, "SteelStructureSuite");
            }
        }

        private static string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

        /// <summary>设置文件所在目录（供“打开设置目录”等命令使用）。</summary>
        public static string SettingsFolderPath => SettingsDirectory;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_sync)
            {
                if (_loaded) return;
                _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string path = SettingsFilePath;
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path, Encoding.UTF8);
                        Dictionary<string, string> map = ParseJsonMap(json);
                        if (map != null)
                        {
                            foreach (KeyValuePair<string, string> kv in map)
                            {
                                if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                                    _values[kv.Key] = kv.Value;
                            }
                        }
                    }
                }
                catch
                {
                    // 设置文件损坏时退回默认值，不影响插件正常使用。
                }
                _loaded = true;
            }
        }

        /// <summary>把内存中的设置写回磁盘。</summary>
        public static void Save()
        {
            lock (_sync)
            {
                EnsureLoaded();
                try
                {
                    Directory.CreateDirectory(SettingsDirectory);
                    string json = BuildJsonMap(_values);
                    string tmp = SettingsFilePath + ".tmp";
                    File.WriteAllText(tmp, json, Encoding.UTF8);
                    if (File.Exists(SettingsFilePath)) File.Delete(SettingsFilePath);
                    File.Move(tmp, SettingsFilePath);
                }
                catch
                {
                    // 写盘失败（例如只读配置目录）不应影响建模流程。
                }
            }
        }

        // ---------- 极简 JSON（扁平字符串字典）读写，避免引入额外程序集依赖 ----------

        private static string BuildJsonMap(Dictionary<string, string> map)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, string> kv in map)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(EscapeJson(kv.Key)).Append("\":");
                sb.Append('"').Append(EscapeJson(kv.Value ?? string.Empty)).Append('"');
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static string EscapeJson(string value)
        {
            var sb = new StringBuilder(value.Length + 8);
            foreach (char ch in value)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < ' ')
                        {
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        private static Dictionary<string, string> ParseJsonMap(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(json)) return result;

            int i = 0;
            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != '{') return result;
            i++;

            while (true)
            {
                SkipWhitespace(json, ref i);
                if (i >= json.Length) break;
                if (json[i] == '}') break;
                if (json[i] == ',') { i++; continue; }
                if (json[i] != '"') break;

                string key = ReadJsonString(json, ref i);
                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++;
                SkipWhitespace(json, ref i);
                string value = string.Empty;
                if (i < json.Length && json[i] == '"')
                {
                    value = ReadJsonString(json, ref i);
                }
                else
                {
                    int start = i;
                    while (i < json.Length && json[i] != ',' && json[i] != '}') i++;
                    value = json.Substring(start, i - start).Trim();
                }
                if (!string.IsNullOrEmpty(key)) result[key] = value;
            }

            return result;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static string ReadJsonString(string s, ref int i)
        {
            var sb = new StringBuilder();
            if (i >= s.Length || s[i] != '"') return sb.ToString();
            i++;
            while (i < s.Length)
            {
                char ch = s[i];
                if (ch == '"') { i++; break; }
                if (ch == '\\' && i + 1 < s.Length)
                {
                    i++;
                    char esc = s[i];
                    switch (esc)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 < s.Length)
                            {
                                int code = int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(esc); break;
                    }
                    i++;
                    continue;
                }
                sb.Append(ch);
                i++;
            }
            return sb.ToString();
        }
        public static string GetString(string key, string defaultValue = "")
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;
            EnsureLoaded();
            lock (_sync)
            {
                return _values.TryGetValue(key, out string v) ? v : defaultValue;
            }
        }

        public static void SetString(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            EnsureLoaded();
            lock (_sync)
            {
                if (value == null) _values.Remove(key);
                else _values[key] = value;
            }
        }

        public static bool GetBool(string key, bool defaultValue = false)
        {
            string v = GetString(key, defaultValue ? "1" : "0");
            return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static void SetBool(string key, bool value) => SetString(key, value ? "1" : "0");

        public static int GetInt(string key, int defaultValue = 0)
        {
            string v = GetString(key, null);
            if (string.IsNullOrEmpty(v)) return defaultValue;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : defaultValue;
        }

        public static void SetInt(string key, int value) =>
            SetString(key, value.ToString(CultureInfo.InvariantCulture));

        public static double GetDouble(string key, double defaultValue = 0.0)
        {
            string v = GetString(key, null);
            if (string.IsNullOrEmpty(v)) return defaultValue;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : defaultValue;
        }

        public static void SetDouble(string key, double value) =>
            SetString(key, value.ToString("R", CultureInfo.InvariantCulture));

        /// <summary>获取各类型构件的默认位号前缀预设。</summary>
        public static string GetDefaultTagPrefix(ParametricStructureType type)
        {
            switch (type)
            {
                case ParametricStructureType.PortalFrame: return "PF";
                case ParametricStructureType.MultiSpanPortalFrame: return "MPF";
                case ParametricStructureType.BracedFrame: return "BF";
                case ParametricStructureType.DoubleTierFrame: return "PR";
                case ParametricStructureType.MultiBaySpaceFrame: return "SF";
                case ParametricStructureType.RoofTruss: return "RT";
                case ParametricStructureType.PipeBridgeTruss: return "PB";

                case ParametricStructureType.CantileverBracket: return "BK";
                case ParametricStructureType.SpacePlatform: return "PL";
                case ParametricStructureType.CantileverWalkway: return "WK";
                case ParametricStructureType.SpiralStair: return "SP";
                case ParametricStructureType.StraightStair: return "ST";
                case ParametricStructureType.SafetyCagedLadder: return "LD";
                case ParametricStructureType.HandrailGuardrail: return "HR";

                case ParametricStructureType.LinearSteelMember: return "MB";
                case ParametricStructureType.ArcSteelMember: return "AM";
                case ParametricStructureType.SpiralSteelMember: return "SM";

                case ParametricStructureType.VesselSupport: return "SD";
                case ParametricStructureType.TowerCircularPlatform: return "TP";
                case ParametricStructureType.VerticalVesselSkirt: return "SK";
                case ParametricStructureType.PipeShoeSupport: return "PS";
                case ParametricStructureType.TankRoofHandrail: return "TR";

                case ParametricStructureType.SubstationGantryFrame: return "GT";
                case ParametricStructureType.CableTrenchSupport: return "CT";
                case ParametricStructureType.CraneRunwayGirder: return "CR";
                case ParametricStructureType.CrossBracingFrame: return "XB";

                case ParametricStructureType.PlatformAssembly: return "ZP";
                case ParametricStructureType.ColumnRowAssembly: return "ZC";
                case ParametricStructureType.PipeRackAssembly: return "ZR";
                case ParametricStructureType.StairAssembly: return "ZS";
                case ParametricStructureType.PortalHallAssembly: return "ZH";
                case ParametricStructureType.WalkwayBridgeAssembly: return "ZW";
                case ParametricStructureType.EquipmentFrameAssembly: return "ZE";
                case ParametricStructureType.CanopyAssembly: return "ZN";

                default: return "SS";
            }
        }

        /// <summary>获取指定构件类型当前生效的位号前缀（优先返回用户修改后的自定义前缀）。</summary>
        public static string GetTagPrefix(ParametricStructureType type)
        {
            string key = "Renumber.Prefix." + type;
            string saved = GetString(key, null);
            return saved == null ? GetDefaultTagPrefix(type) : saved.Trim();
        }

        /// <summary>保存指定构件类型的自定义位号前缀。</summary>
        public static void SetTagPrefix(ParametricStructureType type, string prefix)
        {
            SetString("Renumber.Prefix." + type, (prefix ?? string.Empty).Trim());
        }

        /// <summary>按当前前缀预设、连接符与编号位数（默认 3 位，如 PF-001）格式化位号。</summary>
        public static string FormatTag(ParametricStructureType type, int sequenceNumber)
        {
            string prefix = GetTagPrefix(type);
            int digits = Math.Max(1, Math.Min(6, GetInt("Renumber.Digits", 3)));
            string sep = GetString("Renumber.Separator", "-");
            string numStr = Math.Max(0, sequenceNumber).ToString(new string('0', digits), CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(prefix) ? numStr : prefix + sep + numStr;
        }

        // ---------- 窗口几何（大小 / 位置）记忆 ----------

        /// <summary>清除全部界面记忆。截面库不受影响。</summary>
        public static void Reset()
        {
            lock (_sync)
            {
                _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _loaded = true;
                try
                {
                    if (File.Exists(SettingsFilePath)) File.Delete(SettingsFilePath);
                    string tmp = SettingsFilePath + ".tmp";
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                catch
                {
                    // 文件被占用时至少清空内存，下次启动不再读到旧值。
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        /// <summary>获取 AutoCAD 主窗口句柄，供双屏定位使用。</summary>
        private static IntPtr GetAutoCadMainWindowHandle()
        {
            try
            {
                if (AcadApp.MainWindow != null && AcadApp.MainWindow.Handle != IntPtr.Zero)
                    return AcadApp.MainWindow.Handle;
            }
            catch { }

            try
            {
                return Process.GetCurrentProcess().MainWindowHandle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// 在 AutoCAD 主窗口所在屏幕上以模态方式打开 WPF 窗口（显式禁用 AutoCAD 自身的注册表位置覆写）。
        /// </summary>
        public static bool? ShowModalWindowOnAutoCad(Window window)
        {
            if (window == null) return false;
            return AcadApp.ShowModalWindow(GetAutoCadMainWindowHandle(), window, false);
        }

        /// <summary>
        /// 在 AutoCAD 主窗口所在屏幕上以非模态方式打开 WPF 窗口（显式禁用 AutoCAD 自身的注册表位置覆写）。
        /// </summary>
        public static void ShowModelessWindowOnAutoCad(Window window)
        {
            if (window == null) return;
            AcadApp.ShowModelessWindow(GetAutoCadMainWindowHandle(), window, false);
        }

        /// <summary>
        /// 确保窗口始终弹出在 AutoCAD 主窗口当前所在的显示器上（支持双屏/多屏与异 DPI 缩放）。
        /// 若提供 key 且上次保存的位置仍在当前 AutoCAD 同一块屏幕内，则恢复上次位置；
        /// 若 AutoCAD 已切换至另一块屏幕或首次打开，则自动居中于 AutoCAD 主窗口。
        /// </summary>
        public static void AlignWindowToAutoCadScreen(Window window, string key = null)
        {
            if (window == null) return;

            // 先设为 Manual，避免 WPF 的 CenterScreen 强制把窗口拉回 Windows 主屏
            window.WindowStartupLocation = WindowStartupLocation.Manual;

            void ApplyPosition()
            {
                try
                {
                    IntPtr winHwnd = new WindowInteropHelper(window).Handle;
                    if (winHwnd == IntPtr.Zero) return;

                    IntPtr acadHwnd = GetAutoCadMainWindowHandle();
                    Screen cadScreen = acadHwnd != IntPtr.Zero
                        ? Screen.FromHandle(acadHwnd)
                        : Screen.PrimaryScreen;
                    System.Drawing.Rectangle work = cadScreen.WorkingArea;

                    if (!GetWindowRect(winHwnd, out NativeRect winRect)) return;
                    int winW = Math.Max(100, winRect.Right - winRect.Left);
                    int winH = Math.Max(100, winRect.Bottom - winRect.Top);

                    // 若窗口比当前屏工作区还大，先限制尺寸防止溢出
                    bool resizeNeeded = false;
                    if (winW > work.Width) { winW = work.Width; resizeNeeded = true; }
                    if (winH > work.Height) { winH = work.Height; resizeNeeded = true; }

                    int targetX;
                    int targetY;

                    if (!string.IsNullOrEmpty(key))
                    {
                        double savedPx = GetDouble(key + ".PX", double.NaN);
                        double savedPy = GetDouble(key + ".PY", double.NaN);
                        if (!double.IsNaN(savedPx) && !double.IsNaN(savedPy))
                        {
                            int sx = (int)Math.Round(savedPx);
                            int sy = (int)Math.Round(savedPy);
                            // 判断上次窗口中心是否仍在 AutoCAD 当前所在的同一块显示器内
                            System.Drawing.Point savedCenter = new System.Drawing.Point(sx + winW / 2, sy + winH / 2);
                            Screen savedScreen = Screen.FromPoint(savedCenter);
                            if (string.Equals(savedScreen.DeviceName, cadScreen.DeviceName, StringComparison.OrdinalIgnoreCase))
                            {
                                targetX = Clamp(sx, work.Left, Math.Max(work.Left, work.Right - winW));
                                targetY = Clamp(sy, work.Top, Math.Max(work.Top, work.Bottom - winH));
                                SetWindowPos(
                                    winHwnd, IntPtr.Zero, targetX, targetY,
                                    resizeNeeded ? winW : 0,
                                    resizeNeeded ? winH : 0,
                                    (resizeNeeded ? 0u : SwpNoSize) | SwpNoZOrder | SwpNoActivate);
                                return;
                            }
                        }
                    }

                    // 优先居中于 AutoCAD 主窗口，若主窗口不可用则居中于 AutoCAD 所在屏工作区
                    if (acadHwnd != IntPtr.Zero && GetWindowRect(acadHwnd, out NativeRect acadRect) &&
                        (acadRect.Right - acadRect.Left) > 200 && (acadRect.Bottom - acadRect.Top) > 200)
                    {
                        int acadCenterX = (acadRect.Left + acadRect.Right) / 2;
                        int acadCenterY = (acadRect.Top + acadRect.Bottom) / 2;
                        targetX = acadCenterX - winW / 2;
                        targetY = acadCenterY - winH / 2;
                    }
                    else
                    {
                        targetX = work.Left + (work.Width - winW) / 2;
                        targetY = work.Top + (work.Height - winH) / 2;
                    }

                    targetX = Clamp(targetX, work.Left, Math.Max(work.Left, work.Right - winW));
                    targetY = Clamp(targetY, work.Top, Math.Max(work.Top, work.Bottom - winH));

                    SetWindowPos(
                        winHwnd, IntPtr.Zero, targetX, targetY,
                        resizeNeeded ? winW : 0,
                        resizeNeeded ? winH : 0,
                        (resizeNeeded ? 0u : SwpNoSize) | SwpNoZOrder | SwpNoActivate);
                }
                catch
                {
                    // 定位异常时保持默认行为，不影响弹窗打开
                }
            }

            if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            {
                ApplyPosition();
            }
            else
            {
                EventHandler sourceHandler = null;
                sourceHandler = (s, e) =>
                {
                    window.SourceInitialized -= sourceHandler;
                    ApplyPosition();
                };
                window.SourceInitialized += sourceHandler;
            }

            // Loaded 时窗口已完成最终布局尺寸测量，再次对齐可消除 SizeToContent/异 DPI 导致的居中偏差
            RoutedEventHandler loadedHandler = null;
            loadedHandler = (s, e) =>
            {
                window.Loaded -= loadedHandler;
                ApplyPosition();
            };
            window.Loaded += loadedHandler;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>恢复窗口上次的尺寸与位置，并确保始终与 AutoCAD 位于同一显示器。</summary>
        public static void RestoreWindowState(Window window, string key)
        {
            if (window == null || string.IsNullOrEmpty(key)) return;
            double width = GetDouble(key + ".W", 0);
            double height = GetDouble(key + ".H", 0);

            if (width > 200) window.Width = width;
            if (height > 200) window.Height = height;

            AlignWindowToAutoCadScreen(window, key);
        }

        /// <summary>记录窗口当前的尺寸与物理屏幕位置（兼容多屏异 DPI）。</summary>
        public static void SaveWindowState(Window window, string key)
        {
            if (window == null || string.IsNullOrEmpty(key)) return;
            if (window.WindowState != WindowState.Normal) return;
            SetDouble(key + ".W", window.Width);
            SetDouble(key + ".H", window.Height);

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out NativeRect rect))
            {
                SetDouble(key + ".PX", rect.Left);
                SetDouble(key + ".PY", rect.Top);
            }

            if (!double.IsInfinity(window.Left) && !double.IsNaN(window.Left))
            {
                SetDouble(key + ".X", window.Left);
                SetDouble(key + ".Y", window.Top);
            }
            Save();
        }
    }
}



