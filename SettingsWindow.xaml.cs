using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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

            ModeCommonRadio.IsChecked  = _settings.PortScanMode == PortScanMode.Common;
            ModeAllRadio.IsChecked     = _settings.PortScanMode == PortScanMode.All;
            ModeCustomRadio.IsChecked  = _settings.PortScanMode == PortScanMode.Custom;
            CustomPortsBox.Text        = _settings.CustomPorts;
            CustomPortsBox.IsEnabled   = _settings.PortScanMode == PortScanMode.Custom;
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

        private void PortMode_Changed(object sender, RoutedEventArgs e)
        {
            if (ModeCommonRadio == null) return; // guard against firing before InitializeComponent

            var mode = ModeAllRadio.IsChecked    == true ? PortScanMode.All
                     : ModeCustomRadio.IsChecked == true ? PortScanMode.Custom
                     : PortScanMode.Common;

            _settings.PortScanMode    = mode;
            CustomPortsBox.IsEnabled  = mode == PortScanMode.Custom;
            _settings.Save();
        }

        private void CustomPorts_Changed(object sender, TextChangedEventArgs e)
        {
            if (_settings == null) return;
            _settings.CustomPorts = CustomPortsBox.Text;
            _settings.Save();
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
