using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using SimpleIPScanner.Services;

namespace SimpleIPScanner
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly UpdateService _updateService;

        public SettingsWindow(AppSettings settings, UpdateService updateService)
        {
            InitializeComponent();
            _settings = settings;
            _updateService = updateService;

            // Display current version from assembly metadata
            string version = typeof(App).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "Unknown";
            VersionText.Text = $"Version {version}";

            AutoUpdateCheckBox.IsChecked = _settings.AutoCheckUpdates;
        }

        private void AutoUpdate_Changed(object sender, RoutedEventArgs e)
        {
            _settings.AutoCheckUpdates = AutoUpdateCheckBox.IsChecked == true;
            _settings.Save();
        }

        private async void CheckNow_Click(object sender, RoutedEventArgs e)
        {
            CheckNowBtn.IsEnabled = false;
            UpdateStatusText.Text = "Checking…";
            UpdateStatusText.Foreground = (Brush)Application.Current.FindResource("TextSecondaryBrush");

            string? newVersion = await _updateService.CheckForUpdateAsync();

            if (newVersion != null)
            {
                UpdateStatusText.Text = $"Version {newVersion} is available!";
                UpdateStatusText.Foreground = (Brush)Application.Current.FindResource("AccentCyanBrush");
            }
            else
            {
                UpdateStatusText.Text = "You're up to date.";
                UpdateStatusText.Foreground = (Brush)Application.Current.FindResource("OnlineGreenBrush");
            }

            CheckNowBtn.IsEnabled = true;
        }

        private void GitHubLink_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/ChaseCorbin/SimpleIPScanner",
                UseShellExecute = true
            });
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
