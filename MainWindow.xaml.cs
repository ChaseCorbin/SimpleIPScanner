using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SimpleIPScanner.Models;
using SimpleIPScanner.Services;

namespace SimpleIPScanner
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ScanResult> _results = new();
        private readonly ObservableCollection<DnsBenchmarkResult> _dnsResults = new();
        private readonly ICollectionView _resultsView;
        private CancellationTokenSource? _cts;
        private CancellationTokenSource? _dnsCts;
        private bool _isScanning;
        private bool _isDnsBenchmarking;

        public MainWindow()
        {
            InitializeComponent();
            _resultsView = CollectionViewSource.GetDefaultView(_results);
            _resultsView.Filter = item =>
            {
                if (item is not ScanResult result) return false;
                if (ShowAllToggle?.IsChecked == true) return true;
                return result.IsOnline || (result.MAC != null && result.MAC != "N/A");
            };
            ResultsGrid.ItemsSource = _resultsView;
            DnsResultsGrid.ItemsSource = _dnsResults;

            // Auto-detect the subnet for the active network interface
            CidrInput.Text = NetworkScanner.GetActiveSubnet();

            // Start downloading the full IEEE OUI database in the background
            _ = InitializeOuiDatabase();
        }

        private void FilterToggle_Changed(object sender, RoutedEventArgs e)
        {
            _resultsView?.Refresh();
        }

        private async Task InitializeOuiDatabase()
        {
            StatusText.Text = "Downloading MAC vendor database...";
            try
            {
                await MacVendorLookup.InitializeAsync();
                StatusText.Text = "Enter a subnet and click Scan to begin";
            }
            catch
            {
                StatusText.Text = "MAC vendor database unavailable — using fallback. Enter a subnet and click Scan.";
            }
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e) => await StartScan();

        private void StopButton_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private async void CidrInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !_isScanning) await StartScan();
        }

        /// <summary>
        /// Re-scan a single IP when the 🔄 button is clicked on a row.
        /// </summary>
        private async void RescanButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is ScanResult existing)
            {
                existing.IsScanning = true;
                StatusText.Text = $"Re-scanning {existing.IP}...";

                try
                {
                    var updated = await NetworkScanner.RescanIP(existing.IP);

                    // Update the existing row in-place
                    existing.IsOnline = updated.IsOnline;
                    existing.Hostname = updated.Hostname;
                    existing.MAC = updated.MAC;
                    existing.Vendor = updated.Vendor;
                    existing.PingMs = updated.PingMs;

                    // Update online counter
                    int onlineCount = _results.Count(r => r.IsOnline);
                    OnlineCountText.Text = $"{onlineCount} device{(onlineCount != 1 ? "s" : "")} online";

                    StatusText.Text = existing.IsOnline
                        ? $"Re-scan complete — {existing.IP} is online ({existing.PingMs} ms)"
                        : $"Re-scan complete — {existing.IP} is offline";
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Re-scan failed for {existing.IP}: {ex.Message}";
                }
                finally
                {
                    existing.IsScanning = false;
                }
            }
        }

        private async Task AutoScanPorts(ScanResult result)
        {
            if (!result.IsOnline) return;

            result.IsPortScanning = true;
            try
            {
                var ports = await PortScanner.ScanCommonPortsAsync(result.IP, _cts?.Token ?? CancellationToken.None);
                result.OpenPorts = ports.Any() ? string.Join(", ", ports) : "";
                result.HasScanRun = true;
            }
            catch { }
            finally
            {
                result.IsPortScanning = false;
            }
        }

        private async System.Threading.Tasks.Task StartScan()
        {
            string cidr = CidrInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(cidr))
            {
                MessageBox.Show("Please enter a valid subnet in CIDR notation.\nExample: 192.168.1.0/24",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int hostCount = NetworkScanner.GetHostCount(cidr);
            if (hostCount == 0 || hostCount > NetworkScanner.MaxHostLimit)
            {
                string msg = hostCount == 0 
                    ? "Invalid CIDR format or no hosts in range." 
                    : $"Subnet too large ({hostCount:N0} hosts). Max limit is {NetworkScanner.MaxHostLimit:N0} hosts.";

                MessageBox.Show(msg + "\nExample: 192.168.1.0/24",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isScanning = true;
            _results.Clear();
            ScanButton.IsEnabled = false;
            StopButton.Visibility = Visibility.Visible;
            CidrInput.IsEnabled = false;

            ScanProgress.Value = 0;
            ProgressText.Text = $"Scanning {hostCount} addresses in {cidr} (3 ping attempts per host)...";
            ProgressPercent.Text = "0%";
            StatusText.Text = "Scanning...";
            ElapsedText.Text = "";
            OnlineCountText.Text = "0 devices online";

            _cts = new CancellationTokenSource();
            var scanner = new NetworkScanner();
            var stopwatch = Stopwatch.StartNew();
            int onlineCount = 0;

            scanner.ProgressChanged += (done, total) =>
            {
                Dispatcher.Invoke(() =>
                {
                    int pct = total > 0 ? (done * 100 / total) : 0;
                    ScanProgress.Value = pct;
                    ProgressPercent.Text = $"{pct}%";
                    ProgressText.Text = $"Scanned {done} of {total} addresses...";
                    ElapsedText.Text = $"Elapsed: {stopwatch.Elapsed:mm\\:ss}";
                });
            };

            // All devices (online + offline) are reported
            scanner.DeviceScanned += (result) =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Insert in sorted position
                    int insertAt = 0;
                    for (int i = 0; i < _results.Count; i++)
                    {
                        if (_results[i].SortKey > result.SortKey) break;
                        insertAt = i + 1;
                    }
                    _results.Insert(insertAt, result);

                    if (result.IsOnline)
                    {
                        onlineCount++;
                        OnlineCountText.Text = $"{onlineCount} device{(onlineCount != 1 ? "s" : "")} online";
                        
                        // Automatically scan ports for online devices
                        _ = AutoScanPorts(result);
                    }
                });
            };

            try
            {
                await scanner.ScanSubnetAsync(cidr, _cts.Token);
                stopwatch.Stop();
                StatusText.Text = $"Scan complete — {onlineCount} device(s) found out of {hostCount} scanned";
                ProgressText.Text = "Scan complete!";
                ProgressPercent.Text = "100%";
                ScanProgress.Value = 100;
                ElapsedText.Text = $"Completed in {stopwatch.Elapsed:mm\\:ss\\.f}";
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                StatusText.Text = $"Scan cancelled — {onlineCount} device(s) found before stopping";
                ProgressText.Text = "Scan cancelled";
                ElapsedText.Text = $"Stopped at {stopwatch.Elapsed:mm\\:ss\\.f}";
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                StatusText.Text = $"Error: {ex.Message}";
                ProgressText.Text = "Scan failed";
            }
            finally
            {
                _isScanning = false;
                ScanButton.IsEnabled = true;
                StopButton.Visibility = Visibility.Collapsed;
                CidrInput.IsEnabled = true;
                _cts?.Dispose();
                _cts = null;
            }
        }
        #region DNS Benchmark Logic

        private async void StartDnsBenchmark_Click(object sender, RoutedEventArgs e)
        {
            if (_isDnsBenchmarking) return;

            _isDnsBenchmarking = true;
            _dnsCts = new CancellationTokenSource();
            _dnsResults.Clear();

            StartDnsBenchmarkBtn.Visibility = Visibility.Collapsed;
            StopDnsBenchmarkBtn.Visibility = Visibility.Visible;
            DnsBenchmarkProgress.Value = 0;
            DnsBenchmarkProgress.IsIndeterminate = true;
            StatusText.Text = "Running DNS latency benchmark...";

            int duration = 15;
            if (DnsTestDuration.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int d))
            {
                duration = d;
            }

            try
            {
                var benchmark = new DnsBenchmark();
                benchmark.ResultUpdated += (res) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        var existing = _dnsResults.FirstOrDefault(r => r.ServerIp == res.ServerIp);
                        if (existing == null) _dnsResults.Add(res);
                        DnsResultsGrid.Items.Refresh();
                    });
                };

                var tasks = DnsBenchmark.CommonServers.Select(s => 
                    benchmark.RunBenchmarkAsync(s.Ip, s.Name, duration, _dnsCts.Token));

                // Add system default
                tasks = tasks.Concat(new[] { benchmark.RunBenchmarkAsync("127.0.0.1", "Local / System Default", duration, _dnsCts.Token) });

                await Task.WhenAll(tasks);
                StatusText.Text = "DNS Benchmark complete";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "DNS Benchmark stopped";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"DNS Benchmark error: {ex.Message}");
                StatusText.Text = "Benchmark failed";
            }
            finally
            {
                _isDnsBenchmarking = false;
                StartDnsBenchmarkBtn.Visibility = Visibility.Visible;
                StopDnsBenchmarkBtn.Visibility = Visibility.Collapsed;
                DnsBenchmarkProgress.IsIndeterminate = false;
                DnsBenchmarkProgress.Value = 100;
            }
        }

        private void StopDnsBenchmark_Click(object sender, RoutedEventArgs e)
        {
            _dnsCts?.Cancel();
        }

        #endregion
    }
}
