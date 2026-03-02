using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
    public class CustomDnsEntry
    {
        public string Ip   { get; set; } = "";
        public string Name { get; set; } = "";
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Ip : $"{Name} ({Ip})";
    }

    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ScanResult> _results = new();
        private readonly ObservableCollection<DnsBenchmarkResult> _dnsResults = new();
        private readonly ObservableCollection<TraceSession> _traceSessions = new();
        private readonly Dictionary<TraceSession, CancellationTokenSource> _traceCts = new();
        private readonly ObservableCollection<CustomDnsEntry> _customDnsServers = new();

        // Subnet list for multi-subnet / multi-VLAN scanning
        private readonly ObservableCollection<SubnetEntry> _subnets = new();

        private readonly ICollectionView _resultsView;
        private CancellationTokenSource? _cts;
        private CancellationTokenSource? _dnsCts;
        private bool _isScanning;
        private bool _isDnsBenchmarking;
        private readonly UpdateService _updateService = new();
        private readonly AppSettings _settings = AppSettings.Load();

        // Cached brushes for the chart tooltip hot path (avoids FindResource on every mouse-move)
        private Brush? _tooltipRedBrush;
        private Brush? _tooltipOrangeBrush;
        private Brush? _tooltipGreenBrush;

        // Chart pan / drag state
        private bool _isDragging;
        private Point _dragStartPoint;
        private DateTime _dragStartViewEnd;

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
            CustomDnsServersList.ItemsSource = _customDnsServers;
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

            // Fire-and-forget: silently check for a newer release in the background.
            if (_settings.AutoCheckUpdates)
                _ = CheckForUpdateAsync();
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
                var ports = await PortScanner.ScanPortsAsync(result.IP, _settings.PortScanMode, _settings.CustomPorts, CancellationToken.None);
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
                    .Concat(_customDnsServers.Select(s => benchmark.RunBenchmarkAsync(s.Ip, s.Name, duration, _dnsCts.Token)))
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

        private void AddCustomDnsServer_Click(object sender, RoutedEventArgs e)
        {
            string ip   = CustomDnsIp.Text.Trim();
            string name = CustomDnsName.Text.Trim();

            if (!System.Net.IPAddress.TryParse(ip, out _))
            {
                MessageBox.Show("Please enter a valid IPv4 or IPv6 address.", "Invalid IP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_customDnsServers.Any(s => s.Ip == ip))
            {
                MessageBox.Show("That server is already in the list.", "Duplicate", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _customDnsServers.Add(new CustomDnsEntry
            {
                Ip   = ip,
                Name = string.IsNullOrWhiteSpace(name) ? ip : name
            });

            CustomDnsIp.Text   = "";
            CustomDnsName.Text = "";
        }

        private void RemoveCustomDnsServer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CustomDnsEntry entry)
                _customDnsServers.Remove(entry);
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
            {
                TraceDetailArea.DataContext = session;
                SyncIntervalButtons(session.ChartIntervalMinutes);
            }
            else
                TraceDetailArea.DataContext = null;
        }

        private void SyncIntervalButtons(int minutes)
        {
            foreach (var rb in new[] { RbInterval1m, RbInterval5m, RbInterval10m, RbInterval1h, RbInterval2h })
            {
                if (rb.Tag is string tag && int.TryParse(tag, out int tagMins))
                    rb.IsChecked = tagMins == minutes;
            }
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
                    if (session.Elapsed.TotalHours >= 2) { Dispatcher.Invoke(() => StopTrace(session)); break; }

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

        private void ChartContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (TraceDetailArea.DataContext is not TraceSession session) return;
            _isDragging = true;
            _dragStartPoint = e.GetPosition(ChartContainer);
            _dragStartViewEnd = session.ViewEnd;
            ChartContainer.CaptureMouse();
            ChartContainer.Cursor = Cursors.SizeWE;
        }

        private void ChartContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ChartContainer.ReleaseMouseCapture();
            ChartContainer.Cursor = Cursors.Cross;
        }

        private void ChartContainer_MouseMove(object sender, MouseEventArgs e)
        {
            if (TooltipLine == null || TooltipPopup == null || TraceDetailArea == null || ChartContainer == null) return;
            if (TraceDetailArea.DataContext is not TraceSession session) return;

            Point mousePos = e.GetPosition(ChartContainer);

            // Handle drag-to-pan
            if (_isDragging)
            {
                double deltaX = mousePos.X - _dragStartPoint.X;
                double chartWidth = ChartContainer.ActualWidth;
                if (chartWidth > 0)
                {
                    double windowSeconds = session.ChartIntervalMinutes * 60.0;
                    double deltaSeconds = -(deltaX / chartWidth) * windowSeconds;
                    session.PanTo(_dragStartViewEnd + TimeSpan.FromSeconds(deltaSeconds));
                }
                // Hide tooltip while dragging
                TooltipLine.Visibility = Visibility.Collapsed;
                TooltipPopup.Visibility = Visibility.Collapsed;
                return;
            }

            if (!session.FilteredHistory.Any()) return;

            // Map mouse X → nearest time → nearest data point (time-relative lookup)
            double xRatio = mousePos.X / ChartContainer.ActualWidth;
            double totalWindowSeconds = session.ChartIntervalMinutes * 60.0;
            DateTime mouseTime = session.ViewStart + TimeSpan.FromSeconds(xRatio * totalWindowSeconds);

            var nearest = session.FilteredHistory
                .OrderBy(p => Math.Abs((p.Timestamp - mouseTime).TotalSeconds))
                .First();

            double xPos = (nearest.Timestamp - session.ViewStart).TotalSeconds / totalWindowSeconds * ChartContainer.ActualWidth;
            xPos = Math.Max(0, Math.Min(ChartContainer.ActualWidth, xPos));

            TooltipLine.X1 = TooltipLine.X2 = xPos;
            TooltipLine.Visibility = Visibility.Visible;

            TooltipPopup.Visibility = Visibility.Visible;
            TooltipTime.Text = nearest.Timestamp.ToString("HH:mm:ss");
            TooltipLatency.Text = nearest.Latency < 0 ? "Timeout" : $"{nearest.Latency:F0} ms";

            _tooltipRedBrush    ??= FindResource("ErrorRedBrush") as Brush;
            _tooltipOrangeBrush ??= FindResource("WarningOrangeBrush") as Brush;
            _tooltipGreenBrush  ??= FindResource("OnlineGreenBrush") as Brush;

            if (nearest.Latency < 0 || nearest.Latency >= 200) TooltipLatency.Foreground = _tooltipRedBrush;
            else if (nearest.Latency >= 100)                    TooltipLatency.Foreground = _tooltipOrangeBrush;
            else                                                TooltipLatency.Foreground = _tooltipGreenBrush;

            Canvas.SetLeft(TooltipPopup, xPos + 10);
            Canvas.SetTop(TooltipPopup, Math.Min(mousePos.Y, ChartContainer.ActualHeight - 50));

            if (xPos + TooltipPopup.ActualWidth + 20 > ChartContainer.ActualWidth)
                Canvas.SetLeft(TooltipPopup, xPos - TooltipPopup.ActualWidth - 10);
        }

        private void ChartContainer_MouseLeave(object sender, MouseEventArgs e)
        {
            _isDragging = false;
            ChartContainer.ReleaseMouseCapture();
            ChartContainer.Cursor = Cursors.Cross;
            TooltipLine.Visibility = Visibility.Collapsed;
            TooltipPopup.Visibility = Visibility.Collapsed;
        }

        private void BackToLiveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (TraceDetailArea.DataContext is TraceSession session)
                session.ResetToLive();
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

        #region Remote Tools

        // True when the most-recent right-click landed on a data row (not empty space)
        private bool _rightClickedOnRow;

        private ScanResult? _detailDevice;

        private static readonly Dictionary<int, string> _portNames = new()
        {
            { 21,   "FTP"       }, { 22,   "SSH"       }, { 23,   "Telnet"    },
            { 25,   "SMTP"      }, { 53,   "DNS"       }, { 80,   "HTTP"      },
            { 110,  "POP3"      }, { 135,  "RPC"       }, { 139,  "NetBIOS"   },
            { 143,  "IMAP"      }, { 443,  "HTTPS"     }, { 445,  "SMB"       },
            { 3306, "MySQL"     }, { 3389, "RDP"       }, { 5985, "WinRM"     },
            { 5986, "WinRM-SSL" }, { 8080, "HTTP-Alt"  }, { 8443, "HTTPS-Alt" },
            { 8888, "HTTP-Dev"  }, { 27017, "MongoDB"  },
        };

        private void ResultsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            _rightClickedOnRow = row != null;
            if (row != null) row.IsSelected = true;
        }

        private void ResultsGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Suppress the menu when right-clicking on empty space below the last row
            if (!_rightClickedOnRow) e.Handled = true;
        }

        private void BrowseMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not ScanResult result) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = $"http://{result.IP}",
                UseShellExecute = true
            });
        }

        private void WolMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not ScanResult result) return;

            if (string.IsNullOrEmpty(result.MAC) || result.MAC == "N/A")
            {
                MessageBox.Show(
                    "No MAC address is available for this device.\nWake-on-LAN requires a resolved MAC address.",
                    "MAC Address Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                SendWakeOnLan(result.MAC);
                MessageBox.Show(
                    $"Magic packet sent to {result.MAC}.",
                    "Wake-on-LAN", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to send WOL packet: {ex.Message}",
                    "Wake-on-LAN Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Sends a Wake-on-LAN magic packet for the given MAC address.
        /// Format: 6 × 0xFF followed by 16 repetitions of the 6-byte MAC.
        /// Broadcast over UDP port 9.
        /// </summary>
        private static void SendWakeOnLan(string macAddress)
        {
            // Strip separators (supports both '-' and ':')
            string hex = macAddress.Replace("-", "").Replace(":", "");
            if (hex.Length != 12)
                throw new ArgumentException($"Unexpected MAC format: {macAddress}");

            byte[] mac = new byte[6];
            for (int i = 0; i < 6; i++)
                mac[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

            // Build 102-byte magic packet
            byte[] packet = new byte[102];
            for (int i = 0; i < 6; i++) packet[i] = 0xFF;
            for (int i = 1; i <= 16; i++) Array.Copy(mac, 0, packet, i * 6, 6);

            using var udp = new UdpClient();
            udp.EnableBroadcast = true;
            udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 9));
        }

        private void RdpMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not ScanResult result) return;
            Process.Start("mstsc.exe", $"/v:{GetConnectionTarget(result)}");
        }

        private void PsRemoteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not ScanResult result) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -Command \"Enter-PSSession -ComputerName '{GetConnectionTarget(result)}'\"",
                UseShellExecute = true
            });
        }

        // Prefer hostname; fall back to IP if hostname is missing or "N/A"
        private static string GetConnectionTarget(ScanResult result) =>
            !string.IsNullOrWhiteSpace(result.Hostname) && result.Hostname != "N/A"
                ? result.Hostname
                : result.IP;

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T match) return match;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void DetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ScanResult result)
                ShowDeviceDetail(result);
        }

        private void ShowDeviceDetail(ScanResult result)
        {
            _detailDevice = result;

            // Header
            DetailHostname.Text = !string.IsNullOrEmpty(result.Hostname) && result.Hostname != "N/A"
                                  ? result.Hostname : result.IP;
            DetailOSIcon.Data = string.IsNullOrEmpty(result.OSIcon)
                                ? null : Geometry.Parse(result.OSIcon);

            var onlineBrush = (SolidColorBrush)FindResource(result.IsOnline ? "OnlineGreenBrush" : "OfflineGrayBrush");
            DetailStatusDot.Fill   = onlineBrush;
            DetailStatusDot2.Fill  = onlineBrush;
            DetailStatusText.Text  = result.Status;
            DetailStatusText2.Text = result.Status;
            DetailPingText.Text    = result.PingDisplay;

            // Identity card
            DetailIP.Text           = result.IPDisplay;
            DetailHostnameFull.Text = string.IsNullOrEmpty(result.Hostname) ? "—" : result.Hostname;
            DetailMAC.Text          = string.IsNullOrEmpty(result.MAC) || result.MAC == "N/A" ? "—" : result.MAC;
            DetailVendor.Text       = string.IsNullOrEmpty(result.Vendor) ? "—" : result.Vendor;

            // Connectivity card
            DetailPingFull.Text = result.IsOnline ? result.PingDisplay : "—";
            DetailOS.Text       = string.IsNullOrEmpty(result.OSType) ? "Unknown" : result.OSType;
            DetailSubnet.Text   = result.Subnet;

            // Ports
            DetailPortsWrap.Children.Clear();
            var ports = result.OpenPortsList;
            if (ports.Count > 0)
            {
                DetailPortsHeader.Text       = $"OPEN PORTS ({ports.Count})";
                DetailNoPortsText.Visibility = Visibility.Collapsed;
                DetailPortsWrap.Visibility   = Visibility.Visible;
                foreach (var p in ports)
                {
                    // PortScanner format: "80 (HTTP)" — convert to "80  ·  HTTP"
                    var entry = p.Trim();
                    string chipText;
                    var spaceIdx = entry.IndexOf(' ');
                    if (spaceIdx > 0 && int.TryParse(entry[..spaceIdx], out _))
                    {
                        var svcPart = entry[(spaceIdx + 1)..].Trim('(', ')');
                        chipText = $"{entry[..spaceIdx]}  ·  {svcPart}";
                    }
                    else if (int.TryParse(entry, out int num))
                    {
                        _portNames.TryGetValue(num, out string? svc);
                        chipText = svc != null ? $"{num}  ·  {svc}" : entry;
                    }
                    else
                    {
                        chipText = entry;
                    }
                    var chip = new Border
                    {
                        Background      = (Brush)FindResource("PrimaryDarkBrush"),
                        BorderBrush     = (Brush)FindResource("BorderDarkBrush"),
                        BorderThickness = new Thickness(1),
                        CornerRadius    = new CornerRadius(6),
                        Padding         = new Thickness(10, 5, 10, 5),
                        Margin          = new Thickness(0, 0, 8, 8),
                        Child           = new TextBlock
                        {
                            Text       = chipText,
                            FontSize   = 13,
                            Foreground = (Brush)FindResource("AccentCyanBrush"),
                            FontFamily = new FontFamily("Segoe UI"),
                        }
                    };
                    DetailPortsWrap.Children.Add(chip);
                }
            }
            else
            {
                DetailPortsHeader.Text       = "OPEN PORTS";
                DetailPortsWrap.Visibility   = Visibility.Collapsed;
                DetailNoPortsText.Visibility = Visibility.Visible;
            }

            // WoL only useful when MAC is known
            DetailWolButton.IsEnabled = !string.IsNullOrEmpty(result.MAC) && result.MAC != "N/A";

            // Swap panels
            ResultsContainer.Visibility  = Visibility.Collapsed;
            DeviceDetailPanel.Visibility = Visibility.Visible;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            DeviceDetailPanel.Visibility = Visibility.Collapsed;
            ResultsContainer.Visibility  = Visibility.Visible;
            _detailDevice = null;
        }

        private async void DetailRescanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_detailDevice == null) return;
            _detailDevice.IsScanning     = true;
            DetailRescanButton.IsEnabled = false;
            var updated = await NetworkScanner.RescanIP(_detailDevice.IP);
            _detailDevice.IsOnline  = updated.IsOnline;
            _detailDevice.Hostname  = updated.Hostname;
            _detailDevice.PingMs    = updated.PingMs;
            _detailDevice.TTL       = updated.TTL;
            _detailDevice.OpenPorts = updated.OpenPorts;
            _detailDevice.IsScanning     = false;
            DetailRescanButton.IsEnabled = true;
            ShowDeviceDetail(_detailDevice);
        }

        private void DetailRdpButton_Click(object sender, RoutedEventArgs e)
        {
            if (_detailDevice == null) return;
            Process.Start("mstsc.exe", $"/v:{GetConnectionTarget(_detailDevice)}");
        }

        private void DetailPsRemoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_detailDevice == null) return;
            Process.Start(new ProcessStartInfo
            {
                FileName        = "powershell.exe",
                Arguments       = $"-NoExit -Command \"Enter-PSSession -ComputerName '{GetConnectionTarget(_detailDevice)}'\"",
                UseShellExecute = true
            });
        }

        private void DetailBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_detailDevice == null) return;
            Process.Start(new ProcessStartInfo { FileName = $"http://{_detailDevice.IP}", UseShellExecute = true });
        }

        private void DetailWolButton_Click(object sender, RoutedEventArgs e)
        {
            if (_detailDevice?.MAC == null) return;
            try
            {
                SendWakeOnLan(_detailDevice.MAC);
                MessageBox.Show($"Magic packet sent to {_detailDevice.MAC}.",
                    "Wake-on-LAN", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to send WOL packet: {ex.Message}",
                    "Wake-on-LAN Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Update

        private async Task CheckForUpdateAsync()
        {
            string? newVersion = await _updateService.CheckForUpdateAsync();
            if (newVersion == null) return;

            Dispatcher.Invoke(() =>
            {
                UpdateBannerText.Text = $"Version {newVersion} is available — update now for the latest features.";
                UpdateBanner.Visibility = Visibility.Visible;
            });
        }

        private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateNowButton.IsEnabled = false;
            UpdateNowButton.Content = "Downloading…";
            await _updateService.ApplyUpdateAndRestartAsync();
            // Only reached if the download/apply failed — restore the button.
            UpdateNowButton.Content = "Update & Restart";
            UpdateNowButton.IsEnabled = true;
        }

        private void DismissUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Settings

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow(_settings, _updateService) { Owner = this }.ShowDialog();
        }

        #endregion
    }
}
