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
        private const string MitLicenseText =
@"MIT License

Copyright (c) 2026 zhangly1403

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.";

        public AboutWindow()
        {
            InitializeComponent();

            Assembly asm = typeof(AboutWindow).Assembly;
            string version =
                asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? asm.GetName().Version?.ToString()
                ?? "1.0.0";
            TxtVersion.Text = version;

            TxtCopyright.Text = "Copyright © 2026 zhangly1403. All rights reserved.";

            // 关于窗口中直接展示完整 MIT 许可证条款
            TxtLicenseSummary.Text = MitLicenseText;
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

            MessageBox.Show(this, MitLicenseText, "MIT License",
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
