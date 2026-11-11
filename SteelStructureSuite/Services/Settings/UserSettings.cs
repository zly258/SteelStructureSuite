using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Forms;

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

        /// <summary>恢复窗口上次的尺寸与位置；无记录时保持调用方给定的初始值。</summary>
        public static void RestoreWindowState(Window window, string key)
        {
            if (window == null || string.IsNullOrEmpty(key)) return;
            double width = GetDouble(key + ".W", 0);
            double height = GetDouble(key + ".H", 0);
            double left = GetDouble(key + ".X", double.NaN);
            double top = GetDouble(key + ".Y", double.NaN);

            if (width > 200) window.Width = width;
            if (height > 200) window.Height = height;

            if (!double.IsNaN(left) && !double.IsNaN(top))
            {
                // 只有在当前显示器范围内才恢复位置，避免窗口跑到不可见的副屏上。
                System.Windows.Rect bounds = new System.Windows.Rect(left, top, window.Width, window.Height);
                bool visible = false;
                foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
                {
                    System.Windows.Rect work = new System.Windows.Rect(
                        screen.WorkingArea.Left, screen.WorkingArea.Top,
                        screen.WorkingArea.Width, screen.WorkingArea.Height);
                    if (work.IntersectsWith(bounds)) { visible = true; break; }
                }
                if (visible)
                {
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Left = left;
                    window.Top = top;
                }
            }
        }

        /// <summary>记录窗口当前的尺寸与位置。</summary>
        public static void SaveWindowState(Window window, string key)
        {
            if (window == null || string.IsNullOrEmpty(key)) return;
            if (window.WindowState != WindowState.Normal) return;
            SetDouble(key + ".W", window.Width);
            SetDouble(key + ".H", window.Height);
            if (!double.IsInfinity(window.Left) && !double.IsNaN(window.Left))
            {
                SetDouble(key + ".X", window.Left);
                SetDouble(key + ".Y", window.Top);
            }
            Save();
        }
    }
}



