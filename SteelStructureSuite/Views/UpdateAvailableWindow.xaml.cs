using System;
using System.Diagnostics;
using System.Windows;
using SteelStructureSuite.Services.Settings;

namespace SteelStructureSuite.Views
{
    public partial class UpdateAvailableWindow : Window
    {
        private readonly GitHubUpdateService.ReleaseUpdateInfo _info;

        public UpdateAvailableWindow(GitHubUpdateService.ReleaseUpdateInfo info)
        {
            InitializeComponent();
            _info = info ?? throw new ArgumentNullException(nameof(info));

            TxtHeadline.Text = string.IsNullOrWhiteSpace(_info.Title)
                ? "检测到 SteelStructureSuite 新版本"
                : _info.Title;
            TxtCurrentVersion.Text = "v" + (_info.CurrentVersion ?? string.Empty).TrimStart('v', 'V');
            TxtLatestVersion.Text = _info.LatestTag;
            TxtReleaseNotes.Text = _info.ReleaseNotes ?? string.Empty;

            UserSettings.AlignWindowToAutoCadScreen(this);
        }

        private void OnOpenDownloadClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = string.IsNullOrWhiteSpace(_info.HtmlUrl)
                    ? GitHubUpdateService.ReleasesPageUrl
                    : _info.HtmlUrl;
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开浏览器：" + ex.Message, "打开下载页失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Close();
        }

        private void OnSkipVersionClick(object sender, RoutedEventArgs e)
        {
            GitHubUpdateService.SkipVersion(_info.LatestTag);
            Close();
        }

        private void OnRemindLaterClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
