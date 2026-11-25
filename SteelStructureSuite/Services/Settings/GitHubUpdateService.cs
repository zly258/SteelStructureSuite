using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SteelStructureSuite.Views;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SteelStructureSuite.Services.Settings
{
    /// <summary>
    /// 启动后台检查 GitHub 最新 Release 版本。
    /// 全程在后台线程发起轻量 HTTP 请求，不阻塞 AutoCAD 启动；网络不可用或超时静默跳过；
    /// 仅当远端正式版高于当前插件版本且未被用户跳过时，在 AutoCAD 空闲时弹窗提示。
    /// </summary>
    public static class GitHubUpdateService
    {
        public const string LatestReleaseApiUrl = "https://api.github.com/repos/zly258/SteelStructureSuite/releases/latest";
        public const string ReleasesPageUrl = "https://github.com/zly258/SteelStructureSuite/releases/latest";
        public const string SkippedVersionSettingKey = "Update.SkippedVersion";

        private static int _started;
        private static readonly object PendingSync = new object();
        private static ReleaseUpdateInfo _pendingInfo;
        private static bool _idleHooked;

        public sealed class ReleaseUpdateInfo
        {
            public string CurrentVersion { get; set; }
            public string LatestTag { get; set; }
            public string Title { get; set; }
            public string ReleaseNotes { get; set; }
            public string HtmlUrl { get; set; }
        }

        /// <summary>插件初始化时调用：在后台线程异步检查 GitHub Release 更新。</summary>
        public static void StartBackgroundCheckOnLoad()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) return;

            Task.Run(async () =>
            {
                try
                {
                    // 等待 AutoCAD 完成初始化与功能区加载，避免与启动流程竞争资源
                    await Task.Delay(2500).ConfigureAwait(false);

                    ReleaseUpdateInfo info = FetchNewerReleaseIfAny();
                    if (info == null) return;

                    ScheduleNotificationOnAutoCadIdle(info);
                }
                catch
                {
                    // 断网、代理受限、超时或限流时静默忽略，绝不影响插件正常使用
                }
            });
        }

        /// <summary>插件卸载时清理可能挂起的空闲通知事件。</summary>
        public static void CancelPendingNotification()
        {
            lock (PendingSync)
            {
                _pendingInfo = null;
                if (_idleHooked)
                {
                    try { AcadApp.Idle -= OnApplicationIdleShowUpdate; } catch { }
                    _idleHooked = false;
                }
            }
        }

        /// <summary>标记某版本为“跳过此版本”，后续启动不再重复弹窗。</summary>
        public static void SkipVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;
            UserSettings.SetString(SkippedVersionSettingKey, tag.Trim());
            UserSettings.Save();
        }

        /// <summary>获取当前程序集版本号（形如 1.2.0）。</summary>
        public static string GetCurrentVersionString()
        {
            Assembly asm = typeof(GitHubUpdateService).Assembly;
            string raw =
                asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? asm.GetName().Version?.ToString()
                ?? "1.0.0";
            int plusIdx = raw.IndexOf('+');
            if (plusIdx > 0) raw = raw.Substring(0, plusIdx);
            return raw.Trim();
        }

        private static ReleaseUpdateInfo FetchNewerReleaseIfAny()
        {
            string currentStr = GetCurrentVersionString();
            if (!TryParseSemVer(currentStr, out Version currentVer)) return null;

            string json = DownloadLatestReleaseJson(currentStr);
            if (string.IsNullOrWhiteSpace(json)) return null;

            if (TryExtractJsonBool(json, "draft", out bool isDraft) && isDraft) return null;
            if (TryExtractJsonBool(json, "prerelease", out bool isPre) && isPre) return null;

            string tagName = ExtractJsonString(json, "tag_name");
            if (string.IsNullOrWhiteSpace(tagName)) return null;
            if (!TryParseSemVer(tagName, out Version remoteVer)) return null;

            if (remoteVer <= currentVer) return null;

            string skipped = UserSettings.GetString(SkippedVersionSettingKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(skipped) &&
                string.Equals(skipped.Trim(), tagName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string title = ExtractJsonString(json, "name");
            string htmlUrl = ExtractJsonString(json, "html_url");
            string body = ExtractJsonString(json, "body");

            return new ReleaseUpdateInfo
            {
                CurrentVersion = currentStr,
                LatestTag = tagName.Trim(),
                Title = string.IsNullOrWhiteSpace(title) ? tagName.Trim() : title.Trim(),
                ReleaseNotes = ExtractPreferredReleaseNotes(body),
                HtmlUrl = string.IsNullOrWhiteSpace(htmlUrl) ? ReleasesPageUrl : htmlUrl.Trim()
            };
        }

        private static string DownloadLatestReleaseJson(string currentVersion)
        {
#if NET8_0_OR_GREATER
            using (var handler = new System.Net.Http.HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
            using (var client = new System.Net.Http.HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(8)
            })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SteelStructureSuite/" + currentVersion);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                using (var resp = client.GetAsync(LatestReleaseApiUrl).GetAwaiter().GetResult())
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                }
            }
#else
            try
            {
                // .NET Framework 4.8 默认可能未启用 TLS 1.2，访问 GitHub API 必须开启
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch { }

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(LatestReleaseApiUrl);
            req.Method = "GET";
            req.UserAgent = "SteelStructureSuite/" + currentVersion;
            req.Accept = "application/vnd.github+json";
            req.Timeout = 8000;
            req.ReadWriteTimeout = 8000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            {
                if (resp.StatusCode != HttpStatusCode.OK) return null;
                using (Stream stream = resp.GetResponseStream())
                {
                    if (stream == null) return null;
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
#endif
        }

        private static void ScheduleNotificationOnAutoCadIdle(ReleaseUpdateInfo info)
        {
            lock (PendingSync)
            {
                _pendingInfo = info;
                if (!_idleHooked)
                {
                    AcadApp.Idle += OnApplicationIdleShowUpdate;
                    _idleHooked = true;
                }
            }
        }

        private static void OnApplicationIdleShowUpdate(object sender, EventArgs e)
        {
            ReleaseUpdateInfo info;
            lock (PendingSync)
            {
                if (_idleHooked)
                {
                    try { AcadApp.Idle -= OnApplicationIdleShowUpdate; } catch { }
                    _idleHooked = false;
                }
                info = _pendingInfo;
                _pendingInfo = null;
            }

            if (info == null) return;

            try
            {
                var win = new UpdateAvailableWindow(info);
                UserSettings.ShowModelessWindowOnAutoCad(win);
            }
            catch
            {
                // 弹窗异常时忽略，不影响主程序运行
            }
        }

        /// <summary>解析形如 v1.2.0 / 1.2.0 / 1.2.0.0 的语义化版本号。</summary>
        internal static bool TryParseSemVer(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string s = text.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(1);

            int sepIdx = s.IndexOfAny(new[] { '-', '+' });
            if (sepIdx > 0) s = s.Substring(0, sepIdx);

            string[] parts = s.Split('.');
            if (parts.Length < 2 || parts.Length > 4) return false;

            int[] nums = new int[4];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0)
                    return false;
                nums[i] = n;
            }

            version = new Version(nums[0], nums[1], nums[2], nums[3]);
            return true;
        }

        /// <summary>优先提取双语 Release 说明中的中文部分，保持弹窗简洁可读。</summary>
        private static string ExtractPreferredReleaseNotes(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "请点击下方「前往下载」查看本次更新的完整说明与安装包。";

            string normalized = body.Replace("\r\n", "\n").Trim();
            int enIdx = normalized.IndexOf("\n---\n", StringComparison.Ordinal);
            if (enIdx > 0 && normalized.IndexOf("## 简体中文", StringComparison.Ordinal) >= 0 &&
                normalized.IndexOf("## 简体中文", StringComparison.Ordinal) < enIdx)
            {
                normalized = normalized.Substring(0, enIdx).Trim();
            }

            // 去掉顶部语言切换锚点行
            if (normalized.StartsWith("[简体中文]", StringComparison.Ordinal) ||
                normalized.StartsWith("[English]", StringComparison.Ordinal))
            {
                int firstNl = normalized.IndexOf('\n');
                if (firstNl > 0) normalized = normalized.Substring(firstNl + 1).Trim();
            }

            return normalized.Replace("\n", Environment.NewLine);
        }

        // ---------- 极简顶层 JSON 字段提取（不引入外部 JSON 依赖，兼容 net48 与 net8，避免命中嵌套对象同名字段） ----------

        private static bool TryFindTopLevelJsonValueStart(string json, string fieldName, out int valueStart)
        {
            valueStart = -1;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(fieldName)) return false;

            int i = 0;
            int depth = 0;
            while (i < json.Length)
            {
                char ch = json[i];
                if (ch == '{' || ch == '[')
                {
                    depth++;
                    i++;
                    continue;
                }
                if (ch == '}' || ch == ']')
                {
                    depth--;
                    i++;
                    continue;
                }
                if (ch == '"')
                {
                    string keyOrVal = ReadJsonStringLiteral(json, ref i);
                    if (depth == 1)
                    {
                        int j = i;
                        while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                        if (j < json.Length && json[j] == ':')
                        {
                            j++;
                            while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                            if (string.Equals(keyOrVal, fieldName, StringComparison.Ordinal))
                            {
                                valueStart = j;
                                return true;
                            }
                            i = j;
                            continue;
                        }
                    }
                    continue;
                }
                i++;
            }
            return false;
        }

        private static string ExtractJsonString(string json, string fieldName)
        {
            if (!TryFindTopLevelJsonValueStart(json, fieldName, out int i)) return null;
            if (i < json.Length && json[i] == '"')
                return ReadJsonStringLiteral(json, ref i);
            return null;
        }

        private static bool TryExtractJsonBool(string json, string fieldName, out bool value)
        {
            value = false;
            if (!TryFindTopLevelJsonValueStart(json, fieldName, out int i)) return false;

            if (i + 4 <= json.Length && string.Compare(json, i, "true", 0, 4, StringComparison.Ordinal) == 0)
            {
                value = true;
                return true;
            }
            if (i + 5 <= json.Length && string.Compare(json, i, "false", 0, 5, StringComparison.Ordinal) == 0)
            {
                value = false;
                return true;
            }
            return false;
        }

        private static string ReadJsonStringLiteral(string s, ref int i)
        {
            var sb = new StringBuilder();
            if (i >= s.Length || s[i] != '"') return string.Empty;
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
                            if (i + 4 < s.Length &&
                                int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            {
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
    }
}
