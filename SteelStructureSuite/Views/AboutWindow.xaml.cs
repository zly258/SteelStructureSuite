using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using SteelStructureSuite.Services.Settings;

namespace SteelStructureSuite.Views
{
    public partial class AboutWindow : Window
    {
        private const string GplV3LicenseNotice =
@"GNU General Public License v3.0 (GPL-3.0)

Copyright (C) 2026 zhangly1403

SteelStructureSuite 依据 GNU 通用公共许可证第三版（GPL v3.0）发布。
你可以自由使用、修改和再分发本软件；若分发修改版或衍生作品，必须以相同的 GPL v3.0 协议开放源代码并保留版权与许可声明。
本软件按“现状”提供，不附带任何明示或暗示的担保。

点击下方「完整许可证」按钮可查看随包分发的完整 GNU GPL v3.0 文本。";

        public AboutWindow()
        {
            InitializeComponent();

            Assembly asm = typeof(AboutWindow).Assembly;
            string version =
                asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? asm.GetName().Version?.ToString()
                ?? "1.1.0";
            TxtVersion.Text = version;

            TxtCopyright.Text = "Copyright © 2026 zhangly1403 · GNU GPL v3.0";

            TxtLicenseSummary.Text = GplV3LicenseNotice;
            UserSettings.AlignWindowToAutoCadScreen(this);
        }

        private void OnOpenLicenseClick(object sender, RoutedEventArgs e)
        {
            string path = FindLicenseFile();
            if (path != null)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"")
                    {
                        UseShellExecute = true
                    });
                    return;
                }
                catch
                {
                    // 落到下方 MessageBox 兜底
                }
            }

            MessageBox.Show(this, GplV3LicenseNotice, "GNU GPL v3.0",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static string FindLicenseFile()
        {
            string baseDir = Path.GetDirectoryName(typeof(AboutWindow).Assembly.Location)
                             ?? AppDomain.CurrentDomain.BaseDirectory;

            string[] candidates =
            {
                Path.Combine(baseDir, "Help", "LICENSE.txt"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "Help", "LICENSE.txt")),
                Path.Combine(baseDir, "LICENSE.txt"),
                Path.Combine(baseDir, "LICENSE"),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\LICENSE")),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\LICENSE"))
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
