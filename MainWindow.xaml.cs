using System;
using System.Collections.Generic;
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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SimpleIPScanner.Models;
using SimpleIPScanner.Services;

namespace SimpleIPScanner
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ScanResult> _results = new();
        private readonly ObservableCollection<DnsBenchmarkResult> _dnsResults = new();
        private readonly ObservableCollection<TraceSession> _traceSessions = new();
        private readonly Dictionary<TraceSession, CancellationTokenSource> _traceCts = new();

        // Subnet list for multi-subnet / multi-VLAN scanning
        private readonly ObservableCollection<SubnetEntry> _subnets = new();

        private readonly ICollectionView _resultsView;
        private CancellationTokenSource? _cts;
        private CancellationTokenSource? _dnsCts;
        private bool _isScanning;
        private bool _isDnsBenchmarking;

        // Cached brushes for the chart tooltip hot path (avoids FindResource on every mouse-move)
        private Brush? _tooltipRedBrush;
        private Brush? _tooltipOrangeBrush;
        private Brush? _tooltipGreenBrush;

        public MainWindow()
        {
            InitializeComponent();
            SetWindowIcon();

            _resultsView = CollectionViewSource.GetDefaultView(_results);
            _resultsView.Filter = item =>
            {
                if (item is not ScanResult result) return false;
                if (ShowAllToggle?.IsChecked == true) return true;
                return result.IsOnline || (result.MAC != null && result.MAC != "N/A");
            };
            ResultsGrid.ItemsSource = _resultsView;
            DnsResultsGrid.ItemsSource = _dnsResults;
            TraceTargetsList.ItemsSource = _traceSessions;
            SubnetChipList.ItemsSource = _subnets;

            // Auto-detect the primary subnet and add it to the scan list
            string detected = NetworkScanner.GetActiveSubnet();
            _subnets.Add(new SubnetEntry { Cidr = detected, Label = "Auto" });

            // Start downloading the full IEEE OUI database in the background
            _ = InitializeOuiDatabase();

            // Timer to update elapsed time for active trace sessions
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += (s, e) =>
            {
                foreach (var session in _traceSessions.Where(x => x.IsActive))
                {
                    if (session.StartTime.HasValue)
                        session.Elapsed = DateTime.Now - session.StartTime.Value;
                }
            };
            timer.Start();
        }

        /// <summary>
        /// Renders the switch-stack vector logo to a bitmap and sets it as the
        /// window icon (title bar + taskbar). Uses the same design as the header Viewbox.
        /// </summary>
        private void SetWindowIcon()
        {
            var accent  = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            var card    = new SolidColorBrush(Color.FromRgb(0x1C, 0x23, 0x33));
            var border  = new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3D));
            var offline = new SolidColorBrush(Color.FromRgb(0x48, 0x4F, 0x58));
            var transp  = Brushes.Transparent;

            var canvas = new System.Windows.Controls.Canvas { Width = 54, Height = 36 };

            void AddRect(double l, double t, double w, double h, Brush fill,
                         Brush? stroke = null, double st = 0, double r = 0)
            {
                var rect = new Rectangle { Width = w, Height = h, Fill = fill, RadiusX = r, RadiusY = r };
                if (stroke != null) { rect.Stroke = stroke; rect.StrokeThickness = st; }
                System.Windows.Controls.Canvas.SetLeft(rect, l);
                System.Windows.Controls.Canvas.SetTop(rect, t);
                canvas.Children.Add(rect);
            }

            void AddEllipse(double l, double t, double size, Brush fill, double opacity = 1)
            {
                var e = new Ellipse { Width = size, Height = size, Fill = fill, Opacity = opacity };
                System.Windows.Controls.Canvas.SetLeft(e, l);
                System.Windows.Controls.Canvas.SetTop(e, t);
                canvas.Children.Add(e);
            }

            // Stacking bracket
            AddRect(0, 3, 2, 30, accent, r: 1);

            // Switch 1 — top, inactive
            AddRect(4, 0, 48, 10, card, r: 2);
            AddRect(4, 0, 48, 10, transp, border, 0.8, 2);
            foreach (double x in new[] { 9.0, 15, 21, 27, 33, 39 }) AddRect(x, 3, 4, 4, border, r: 0.5);
            AddEllipse(46, 3, 4, offline);

            // Switch 2 — middle, partial
            AddRect(4, 13, 48, 10, card, r: 2);
            AddRect(4, 13, 48, 10, transp, border, 0.8, 2);
            foreach (double x in new[] { 9.0, 15, 21 })  AddRect(x, 16, 4, 4, accent, r: 0.5);
            foreach (double x in new[] { 27.0, 33, 39 }) AddRect(x, 16, 4, 4, border, r: 0.5);
            AddEllipse(46, 16, 4, accent, 0.5);

            // Switch 3 — bottom, fully active
            AddRect(4, 26, 48, 10, card, r: 2);
            AddRect(4, 26, 48, 10, transp, accent, 1, 2);
            foreach (double x in new[] { 9.0, 15, 21, 27, 33, 39 }) AddRect(x, 29, 4, 4, accent, r: 0.5);
            AddEllipse(46, 29, 4, accent);

            // Measure and arrange so the visual has a layout before rendering
            canvas.Measure(new Size(54, 36));
            canvas.Arrange(new Rect(0, 0, 54, 36));
            canvas.UpdateLayout();

            // Render at 4× scale for crispness; WPF downscales to 16/32px as needed
            const double scale = 4;
            var rtb = new RenderTargetBitmap(
                (int)(54 * scale), (int)(36 * scale),
                96 * scale, 96 * scale,
                PixelFormats.Pbgra32);
            rtb.Render(canvas);
            rtb.Freeze();

            Icon = rtb;
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
                StatusText.Text = "Add subnets above and click ⚡ Scan to begin";
            }
            catch
            {
                StatusText.Text = "MAC vendor database unavailable — using fallback. Add a subnet and click Scan.";
            }
        }

        #region Subnet List Management

        /// <summary>
        /// Adds the typed CIDR to the chip list if valid and not already present.
        /// Returns true if the entry was added.
        /// </summary>
        private bool TryAddSubnet(string cidr, string label = "")
        {
            cidr = cidr.Trim();
            if (string.IsNullOrEmpty(cidr)) return false;

            // Prevent duplicates (case-insensitive)
            if (_subnets.Any(s => s.Cidr.Equals(cidr, StringComparison.OrdinalIgnoreCase)))
                return false;

            int count = NetworkScanner.GetHostCount(cidr);
            if (count == 0)
            {
                MessageBox.Show($"Invalid CIDR format: {cidr}\nExample: 192.168.1.0/24",
                    "Invalid Subnet", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (count > NetworkScanner.MaxHostLimit)
            {
                MessageBox.Show($"Subnet too large ({count:N0} hosts). Max is {NetworkScanner.MaxHostLimit:N0}.\nUse a more specific prefix (e.g. /24 instead of /16).",
                    "Subnet Too Large", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            _subnets.Add(new SubnetEntry { Cidr = cidr, Label = label });
            return true;
        }

        private void AddSubnet_Click(object sender, RoutedEventArgs e)
        {
            if (TryAddSubnet(CidrInput.Text))
                CidrInput.Text = "";
        }

        private void AutoDetectSubnets_Click(object sender, RoutedEventArgs e)
        {
            var found = SubnetDiscovery.GetConnectedSubnets();
            int added = 0;
            foreach (var (cidr, label) in found)
            {
                if (!_subnets.Any(s => s.Cidr.Equals(cidr, StringComparison.OrdinalIgnoreCase)))
                {
                    int count = NetworkScanner.GetHostCount(cidr);
                    if (count > 0 && count <= NetworkScanner.MaxHostLimit)
                    {
                        _subnets.Add(new SubnetEntry { Cidr = cidr, Label = label });
                        added++;
                    }
                }
            }
            StatusText.Text = added > 0
                ? $"Auto-detected {added} additional subnet{(added != 1 ? "s" : "")}."
                : "No new subnets found — all detected adapters already in the list.";
        }

        private void RemoveSubnet_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SubnetEntry entry)
                _subnets.Remove(entry);
        }

        #endregion

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
            if (sender is Button btn && btn.Tag is ScanResult existing)
            {
                existing.IsScanning = true;
                StatusText.Text = $"Re-scanning {existing.IP}...";

                try
                {
                    var updated = await NetworkScanner.RescanIP(existing.IP);

                    existing.IsOnline = updated.IsOnline;
                    existing.Hostname = updated.Hostname;
                    existing.MAC = updated.MAC;
                    existing.Vendor = updated.Vendor;
                    existing.PingMs = updated.PingMs;

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

        private async Task StartScan()
        {
            // If the text box has content, add it to the list first
            string typedCidr = CidrInput.Text.Trim();
            if (!string.IsNullOrEmpty(typedCidr))
            {
                TryAddSubnet(typedCidr);
                CidrInput.Text = "";
            }

            // Build the list of subnets selected for this scan
            var toScan = _subnets.Where(s => s.IsSelected).ToList();
            if (!toScan.Any())
            {
                MessageBox.Show("Please add at least one subnet to scan.\nExample: 192.168.1.0/24",
                    "No Subnets Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Pre-compute total host count for overall progress tracking
            long totalHosts = toScan.Sum(s => (long)NetworkScanner.GetHostCount(s.Cidr));

            _isScanning = true;
            _results.Clear();
            ScanButton.IsEnabled = false;
            StopButton.Visibility = Visibility.Visible;
            CidrInput.IsEnabled = false;

            ScanProgress.Value = 0;
            ProgressPercent.Text = "0%";
            StatusText.Text = "Scanning...";
            ElapsedText.Text = "";
            OnlineCountText.Text = "0 devices online";

            string label = toScan.Count == 1
                ? toScan[0].Cidr
                : $"{toScan.Count} subnets";
            ProgressText.Text = $"Scanning {totalHosts:N0} addresses across {label}...";

            _cts = new CancellationTokenSource();
            var scanner = new NetworkScanner();
            var stopwatch = Stopwatch.StartNew();
            int onlineCount = 0;
            long subnetOffset = 0; // cumulative hosts from completed subnets

            scanner.ProgressChanged += (done, _) =>
            {
                long offset = subnetOffset; // capture before BeginInvoke
                Dispatcher.BeginInvoke(() =>
                {
                    long overallDone = offset + done;
                    int pct = totalHosts > 0 ? (int)(overallDone * 100 / totalHosts) : 0;
                    ScanProgress.Value = pct;
                    ProgressPercent.Text = $"{pct}%";
                    ProgressText.Text = $"Scanned {overallDone:N0} of {totalHosts:N0} addresses...";
                    ElapsedText.Text = $"Elapsed: {stopwatch.Elapsed:mm\\:ss}";
                });
            };

            scanner.DeviceScanned += (result) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    // Insert in sorted order by IP address
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
                        _ = AutoScanPorts(result);
                    }
                });
            };

            try
            {
                for (int i = 0; i < toScan.Count; i++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    var entry = toScan[i];
                    string subnetLabel = toScan.Count > 1
                        ? $"Scanning {entry.Cidr} (subnet {i + 1} of {toScan.Count})..."
                        : $"Scanning {entry.Cidr}...";
                    StatusText.Text = subnetLabel;

                    await scanner.ScanSubnetAsync(entry.Cidr, _cts.Token);
                    subnetOffset += NetworkScanner.GetHostCount(entry.Cidr);
                }

                stopwatch.Stop();
                StatusText.Text = $"Scan complete — {onlineCount} device(s) found across {label}";
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
                duration = d;

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

                var tasks = DnsBenchmark.CommonServers
                    .Select(s => benchmark.RunBenchmarkAsync(s.Ip, s.Name, duration, _dnsCts.Token))
                    .Concat(new[] { benchmark.RunBenchmarkAsync("127.0.0.1", "Local / System Default", duration, _dnsCts.Token) });

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

        #region Visual Traceroute Logic

        private void AddTraceTarget_Click(object sender, RoutedEventArgs e)
        {
            string target = NewTraceTarget.Text.Trim();
            if (string.IsNullOrEmpty(target)) return;

            if (_traceSessions.Any(s => s.Destination.Equals(target, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("This host is already being monitored.");
                return;
            }

            var session = new TraceSession { Destination = target };
            _traceSessions.Add(session);
            TraceTargetsList.SelectedItem = session;
            NewTraceTarget.Text = "";
        }

        private void DeleteTraceTarget_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TraceSession session)
            {
                StopTrace(session);
                _traceSessions.Remove(session);
            }
        }

        private void TraceTarget_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TraceTargetsList.SelectedItem is TraceSession session)
                TraceDetailArea.DataContext = session;
            else
                TraceDetailArea.DataContext = null;
        }

        private async void ToggleTrace_Click(object sender, RoutedEventArgs e)
        {
            if (TraceTargetsList.SelectedItem is TraceSession session)
            {
                if (session.IsActive)
                    StopTrace(session);
                else
                    await StartTrace(session);
                TraceTarget_SelectionChanged(null!, null!);
            }
        }

        private async void StartAllTraces_Click(object sender, RoutedEventArgs e)
        {
            var tasks = _traceSessions.Where(s => !s.IsActive).Select(s => StartTrace(s));
            await Task.WhenAll(tasks);
        }

        private async Task StartTrace(TraceSession session)
        {
            if (session.IsActive) return;

            session.IsActive = true;
            session.Status = "Running";
            session.StartTime = DateTime.Now;
            session.IsPaused = false;

            var cts = new CancellationTokenSource();
            _traceCts[session] = cts;

            // Loop 1: High-frequency latency monitoring (5 pings/sec)
            _ = Task.Run(async () =>
            {
                var pingSender = new System.Net.NetworkInformation.Ping();
                while (!cts.Token.IsCancellationRequested)
                {
                    if (session.Elapsed.TotalHours >= 8) { Dispatcher.Invoke(() => StopTrace(session)); break; }

                    try
                    {
                        var reply = await pingSender.SendPingAsync(session.Destination, 1000);
                        Dispatcher.Invoke(() => session.AddDataPoint(
                            reply.Status == System.Net.NetworkInformation.IPStatus.Success ? reply.RoundtripTime : -1));
                    }
                    catch { Dispatcher.Invoke(() => session.AddDataPoint(-1)); }

                    await Task.Delay(200, cts.Token);
                }
            }, cts.Token);

            // Loop 2: Traceroute hop discovery (every 10 seconds)
            _ = Task.Run(async () =>
            {
                var service = new TracerouteService();
                while (!cts.Token.IsCancellationRequested)
                {
                    await service.RunTraceOnceAsync(session, cts.Token);
                    if (cts.Token.IsCancellationRequested) break;
                    await Task.Delay(10000, cts.Token);
                }
            }, cts.Token);
        }

        private void Interval_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string minsStr && int.TryParse(minsStr, out int mins))
            {
                if (TraceDetailArea.DataContext is TraceSession session)
                    session.ChartIntervalMinutes = mins;
            }
        }

        private void ChartContainer_MouseMove(object sender, MouseEventArgs e)
        {
            if (TooltipLine == null || TooltipPopup == null || TraceDetailArea == null || ChartContainer == null) return;
            if (TraceDetailArea.DataContext is not TraceSession session || !session.FilteredHistory.Any()) return;

            var history = session.FilteredHistory;

            Point mousePos = e.GetPosition(ChartContainer);
            double xRatio = mousePos.X / ChartContainer.ActualWidth;
            int index = (int)Math.Round(xRatio * (history.Count - 1));
            index = Math.Max(0, Math.Min(history.Count - 1, index));

            var point = history[index];

            double xPos = (double)index / (history.Count > 1 ? history.Count - 1 : 1) * ChartContainer.ActualWidth;
            TooltipLine.X1 = TooltipLine.X2 = xPos;
            TooltipLine.Visibility = Visibility.Visible;

            TooltipPopup.Visibility = Visibility.Visible;
            TooltipTime.Text = point.Timestamp.ToString("HH:mm:ss");
            TooltipLatency.Text = point.Latency < 0 ? "Timeout" : $"{point.Latency:F0} ms";

            _tooltipRedBrush    ??= FindResource("ErrorRedBrush") as Brush;
            _tooltipOrangeBrush ??= FindResource("WarningOrangeBrush") as Brush;
            _tooltipGreenBrush  ??= FindResource("OnlineGreenBrush") as Brush;

            if (point.Latency < 0 || point.Latency >= 200) TooltipLatency.Foreground = _tooltipRedBrush;
            else if (point.Latency >= 100)                  TooltipLatency.Foreground = _tooltipOrangeBrush;
            else                                            TooltipLatency.Foreground = _tooltipGreenBrush;

            Canvas.SetLeft(TooltipPopup, xPos + 10);
            Canvas.SetTop(TooltipPopup, Math.Min(mousePos.Y, ChartContainer.ActualHeight - 50));

            if (xPos + TooltipPopup.ActualWidth + 20 > ChartContainer.ActualWidth)
                Canvas.SetLeft(TooltipPopup, xPos - TooltipPopup.ActualWidth - 10);
        }

        private void ChartContainer_MouseLeave(object sender, MouseEventArgs e)
        {
            TooltipLine.Visibility = Visibility.Collapsed;
            TooltipPopup.Visibility = Visibility.Collapsed;
        }

        private void StopTrace(TraceSession session)
        {
            if (_traceCts.TryGetValue(session, out var cts))
            {
                cts.Cancel();
                _traceCts.Remove(session);
            }
            session.IsActive = false;
            session.Status = "Stopped";
        }

        #endregion
    }
}
