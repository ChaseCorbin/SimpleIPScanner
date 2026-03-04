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
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.Text.Json;
using Microsoft.Win32;
using SharpPcap;
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

        // Timeout log file — written to the app directory for long-running trace sessions
        private static readonly string _timeoutLogPath =
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "traceroute_timeouts.log");
        private static readonly object _logLock = new();

        // Per-chart-card drag state (keyed by the ChartContainer Grid instance)
        private readonly Dictionary<Grid, (bool isDragging, Point startPoint, DateTime startViewEnd)> _chartDragStates = new();

        // Per-chart-card right-click zoom selection state
        private readonly Dictionary<Grid, (bool isSelecting, double startX)> _chartZoomStates = new();

        // Cached tooltip brushes (initialised lazily on first hover)
        private Brush? _tooltipRedBrush;
        private Brush? _tooltipOrangeBrush;
        private Brush? _tooltipGreenBrush;

        // ── Speed Test ────────────────────────────────────────────────────────
        private readonly SpeedTestService _speedTestService = new();
        private readonly SpeedTestSession _speedSession     = new();
        private readonly ObservableCollection<PingServerResult> _speedPingResults = new();
        private bool _isSpeedTesting;
        private CancellationTokenSource? _speedCts;

        // ── Packet Capture ────────────────────────────────────────────────────
        private readonly PacketCaptureService _captureService = new();
        private readonly ObservableCollection<TalkerEntry> _talkers = new();
        private readonly Dictionary<string, string> _resolvedHosts = new();
        private System.Windows.Threading.DispatcherTimer? _captureRefreshTimer;

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
            AllChartsControl.ItemsSource = _traceSessions;
            SubnetChipList.ItemsSource = _subnets;
            TalkersGrid.ItemsSource = _talkers;

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

            // Initialize packet capture tab (checks Npcap, populates interface list)
            InitPacketCaptureTab();

            // Speed Test tab wiring
            SpeedTestRoot.DataContext  = _speedSession;
            SpeedPingList.ItemsSource  = _speedPingResults;
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
            var sidebarGrid = SidebarHopSection.Parent as Grid;
            if (TraceTargetsList.SelectedItem is TraceSession session)
            {
                SidebarHopSection.DataContext = session;
                SidebarHopSection.Visibility  = Visibility.Visible;
                if (sidebarGrid != null)
                {
                    // Splitter row (Row 2): 5px drag handle
                    sidebarGrid.RowDefinitions[2].Height = new GridLength(5);
                    // Route path row (Row 3): takes proportional space (min 120px)
                    sidebarGrid.RowDefinitions[3].Height = new GridLength(1, GridUnitType.Star);
                    sidebarGrid.RowDefinitions[3].MinHeight = 120;
                    // Target list row (Row 1): also *, gets 2x more space by default
                    sidebarGrid.RowDefinitions[1].Height = new GridLength(2, GridUnitType.Star);
                }
            }
            else
            {
                SidebarHopSection.DataContext = null;
                SidebarHopSection.Visibility  = Visibility.Collapsed;
                if (sidebarGrid != null)
                {
                    sidebarGrid.RowDefinitions[2].Height = new GridLength(0);
                    sidebarGrid.RowDefinitions[3].Height = new GridLength(0);
                    sidebarGrid.RowDefinitions[3].MinHeight = 0;
                    sidebarGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
                }
            }
        }

        // Per-card start/stop button inside the all-charts ItemsControl
        private async void ToggleTraceCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TraceSession session)
            {
                if (session.IsActive)
                    StopTrace(session);
                else
                    await StartTrace(session);
            }
        }

        private async void StartAllTraces_Click(object sender, RoutedEventArgs e)
        {
            var tasks = _traceSessions.Where(s => !s.IsActive).Select(s => StartTrace(s));
            await Task.WhenAll(tasks);
        }

        private static void LogTimeout(string destination)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TIMEOUT: {destination}{Environment.NewLine}";
                lock (_logLock)
                    System.IO.File.AppendAllText(_timeoutLogPath, line);
            }
            catch { /* best-effort logging — never crash the UI */ }
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

            // Loop 1: Latency monitoring (1 ping/sec; older data is auto-archived at 1-pt/min)
            _ = Task.Run(async () =>
            {
                var pingSender = new System.Net.NetworkInformation.Ping();
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var reply = await pingSender.SendPingAsync(session.Destination, 1000);
                        bool timeout = reply.Status != System.Net.NetworkInformation.IPStatus.Success;
                        if (timeout) LogTimeout(session.Destination);
                        Dispatcher.Invoke(() => session.AddDataPoint(timeout ? -1 : reply.RoundtripTime));
                    }
                    catch
                    {
                        LogTimeout(session.Destination);
                        Dispatcher.Invoke(() => session.AddDataPoint(-1));
                    }

                    await Task.Delay(1000, cts.Token);
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
                if (rb.DataContext is TraceSession session)
                    session.ChartIntervalMinutes = mins;
            }
        }

        private void BackToLiveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TraceSession session)
                session.ResetToLive();
        }

        private void ResetZoomBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TraceSession session)
                session.ResetZoom();
        }

        // ── Per-card chart mouse interactions ──────────────────────────────────

        /// <summary>Walks the visual tree downward to find the first child of type T with a matching Name.</summary>
        private static T? FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed && typed.Name == name) return typed;
                var result = FindChild<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private void ChartCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Grid chart || chart.DataContext is not TraceSession session) return;
            if (session.IsZoomed) return; // pan is disabled while zoomed; use Reset Zoom first
            _chartDragStates[chart] = (true, e.GetPosition(chart), session.ViewEnd);
            chart.CaptureMouse();
            chart.Cursor = Cursors.SizeWE;
        }

        private void ChartCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Grid chart) return;
            if (_chartDragStates.TryGetValue(chart, out var s))
                _chartDragStates[chart] = (false, s.startPoint, s.startViewEnd);
            chart.ReleaseMouseCapture();
            chart.Cursor = Cursors.Cross;
        }

        private void ChartCard_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Grid chart || chart.DataContext is not TraceSession) return;
            double startX = e.GetPosition(chart).X;
            _chartZoomStates[chart] = (true, startX);
            chart.CaptureMouse();

            // Show the selection overlay at the click position (zero width initially)
            if (FindChild<Canvas>(chart, "ZoomSelectionCanvas") is { } canvas)
            {
                canvas.Visibility = Visibility.Visible;
                if (FindChild<System.Windows.Shapes.Rectangle>(chart, "ZoomSelectionRect") is { } rect)
                {
                    rect.Width = 0;
                    System.Windows.Controls.Canvas.SetLeft(rect, startX);
                }
            }
            e.Handled = true;
        }

        private void ChartCard_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Grid chart || chart.DataContext is not TraceSession session) return;

            _chartZoomStates.TryGetValue(chart, out var zs);
            _chartZoomStates[chart] = (false, zs.startX);
            chart.ReleaseMouseCapture();

            // Hide the selection overlay
            if (FindChild<Canvas>(chart, "ZoomSelectionCanvas") is { } canvas)
                canvas.Visibility = Visibility.Collapsed;

            if (!zs.isSelecting) return;

            double endX = e.GetPosition(chart).X;
            double left  = Math.Min(zs.startX, endX);
            double right = Math.Max(zs.startX, endX);

            // Ignore tiny selections (< 5 px) to prevent accidental zooms on plain right-clicks
            if (right - left < 5) return;

            // Map pixel X positions to timestamps
            double windowSec = (session.ViewEnd - session.ViewStart).TotalSeconds;
            if (windowSec <= 0 || chart.ActualWidth <= 0) return;

            DateTime zoomStart = session.ViewStart + TimeSpan.FromSeconds(left  / chart.ActualWidth * windowSec);
            DateTime zoomEnd   = session.ViewStart + TimeSpan.FromSeconds(right / chart.ActualWidth * windowSec);
            session.ZoomTo(zoomStart, zoomEnd);

            e.Handled = true;
        }

        private void ChartCard_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not Grid chart) return;
            if (_chartDragStates.TryGetValue(chart, out var s))
                _chartDragStates[chart] = (false, s.startPoint, s.startViewEnd);

            // Cancel any in-progress zoom selection
            if (_chartZoomStates.TryGetValue(chart, out var zs) && zs.isSelecting)
            {
                _chartZoomStates[chart] = (false, zs.startX);
                if (FindChild<Canvas>(chart, "ZoomSelectionCanvas") is { } canvas)
                    canvas.Visibility = Visibility.Collapsed;
            }

            chart.ReleaseMouseCapture();
            chart.Cursor = Cursors.Cross;

            if (FindChild<Line>(chart, "ChartTooltipLine") is { } line)   line.Visibility = Visibility.Collapsed;
            if (FindChild<Border>(chart, "ChartTooltipPopup") is { } popup) popup.Visibility = Visibility.Collapsed;
        }

        private void ChartCard_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not Grid chart || chart.DataContext is not TraceSession session) return;

            var tooltipLine    = FindChild<Line>(chart,      "ChartTooltipLine");
            var tooltipPopup   = FindChild<Border>(chart,    "ChartTooltipPopup");
            var tooltipTime    = FindChild<TextBlock>(chart, "ChartTooltipTime");
            var tooltipLatency = FindChild<TextBlock>(chart, "ChartTooltipLatency");
            if (tooltipLine == null || tooltipPopup == null) return;

            Point mousePos = e.GetPosition(chart);

            // Drag-to-pan
            _chartDragStates.TryGetValue(chart, out var drag);
            if (drag.isDragging)
            {
                double deltaX = mousePos.X - drag.startPoint.X;
                if (chart.ActualWidth > 0)
                {
                    double windowSec = session.ChartIntervalMinutes * 60.0;
                    double deltaSec  = -(deltaX / chart.ActualWidth) * windowSec;
                    session.PanTo(drag.startViewEnd + TimeSpan.FromSeconds(deltaSec));
                }
                tooltipLine.Visibility  = Visibility.Collapsed;
                tooltipPopup.Visibility = Visibility.Collapsed;
                return;
            }

            // Right-click zoom selection in progress — update the selection rectangle
            _chartZoomStates.TryGetValue(chart, out var zoom);
            if (zoom.isSelecting)
            {
                if (FindChild<Canvas>(chart, "ZoomSelectionCanvas") is { } selCanvas &&
                    FindChild<System.Windows.Shapes.Rectangle>(chart, "ZoomSelectionRect") is { } selRect)
                {
                    double left  = Math.Min(zoom.startX, mousePos.X);
                    double width = Math.Abs(mousePos.X - zoom.startX);
                    System.Windows.Controls.Canvas.SetLeft(selRect, left);
                    selRect.Width = width;
                }
                tooltipLine.Visibility  = Visibility.Collapsed;
                tooltipPopup.Visibility = Visibility.Collapsed;
                return;
            }

            bool hasHistory = session.FilteredHistory.Any();
            bool hasEvents  = session.FilteredTimeoutEvents.Any() || session.FilteredSpikeEvents.Any();
            if (!hasHistory && !hasEvents) return;

            // Use the actual visible window duration (respects zoom mode)
            double totalSec = (session.ViewEnd - session.ViewStart).TotalSeconds;
            if (totalSec <= 0) return;

            double xRatio      = mousePos.X / chart.ActualWidth;
            DateTime mouseTime = session.ViewStart + TimeSpan.FromSeconds(xRatio * totalSec);

            // Snap to event marker lines (timeout = red, spike = orange) within 12 px.
            // Event lines are exact-timestamp and more useful than an averaged archive point.
            const double snapPixels = 12.0;
            Models.TraceDataPoint? snappedEvent = null;
            double snappedDist = double.MaxValue;
            foreach (var evt in session.FilteredTimeoutEvents.Concat(session.FilteredSpikeEvents))
            {
                double evtX = (evt.Timestamp - session.ViewStart).TotalSeconds / totalSec * chart.ActualWidth;
                double dist = Math.Abs(evtX - mousePos.X);
                if (dist < snapPixels && dist < snappedDist)
                {
                    snappedEvent = evt;
                    snappedDist  = dist;
                }
            }

            // Resolve the display point: event snap wins; fall back to nearest history point.
            Models.TraceDataPoint displayPoint;
            if (snappedEvent != null)
            {
                displayPoint = snappedEvent;
            }
            else if (hasHistory)
            {
                displayPoint = session.FilteredHistory
                    .OrderBy(p => Math.Abs((p.Timestamp - mouseTime).TotalSeconds))
                    .First();
            }
            else return;

            double xPos = (displayPoint.Timestamp - session.ViewStart).TotalSeconds / totalSec * chart.ActualWidth;
            xPos = Math.Clamp(xPos, 0, chart.ActualWidth);

            tooltipLine.X1 = tooltipLine.X2 = xPos;
            tooltipLine.Visibility  = Visibility.Visible;
            tooltipPopup.Visibility = Visibility.Visible;

            if (tooltipTime != null)    tooltipTime.Text = displayPoint.Timestamp.ToString("HH:mm:ss");
            if (tooltipLatency != null)
            {
                tooltipLatency.Text = displayPoint.Latency < 0 ? "Timeout" : $"{displayPoint.Latency:F0} ms";
                _tooltipRedBrush    ??= FindResource("ErrorRedBrush")      as Brush;
                _tooltipOrangeBrush ??= FindResource("WarningOrangeBrush") as Brush;
                _tooltipGreenBrush  ??= FindResource("OnlineGreenBrush")   as Brush;
                tooltipLatency.Foreground =
                    displayPoint.Latency < 0 || displayPoint.Latency >= 200 ? _tooltipRedBrush :
                    displayPoint.Latency >= 100                              ? _tooltipOrangeBrush :
                                                                               _tooltipGreenBrush;
            }

            Canvas.SetLeft(tooltipPopup, xPos + 10);
            Canvas.SetTop(tooltipPopup, Math.Min(mousePos.Y, chart.ActualHeight - 50));
            if (xPos + tooltipPopup.ActualWidth + 20 > chart.ActualWidth)
                Canvas.SetLeft(tooltipPopup, xPos - tooltipPopup.ActualWidth - 10);
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

        #region Speed Test

        private async void StartSpeedTest_Click(object sender, RoutedEventArgs e)
        {
            if (_isSpeedTesting) return;

            // Parse duration from ComboBox
            int seconds = 10;
            if (SpeedDurationCombo.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out int d))
                seconds = d;

            _isSpeedTesting = true;
            _speedCts       = new CancellationTokenSource();
            var ct          = _speedCts.Token;

            StartSpeedTestBtn.Visibility = Visibility.Collapsed;
            StopSpeedTestBtn.Visibility  = Visibility.Visible;

            _speedSession.Reset();
            _speedSession.TestSeconds = seconds;
            _speedPingResults.Clear();

            try
            {
                // ── Phase 1: Ping servers (~2 s) ──────────────────────────────
                _speedSession.Phase    = "Pinging servers…";
                _speedSession.Progress = 5;

                var pingResults = await _speedTestService.PingServersAsync(ct);

                Dispatcher.Invoke(() =>
                {
                    _speedPingResults.Clear();
                    foreach (var r in pingResults) _speedPingResults.Add(r);

                    long best = -1;
                    foreach (var r in pingResults)
                        if (r.Latency >= 0 && (best < 0 || r.Latency < best))
                            best = r.Latency;
                    _speedSession.BestPingMs = best;
                });

                if (ct.IsCancellationRequested) return;

                // ── Phase 2: Download ──────────────────────────────────────────
                _speedSession.Phase    = "Testing download…";
                _speedSession.Progress = 10;

                var downloadSw = System.Diagnostics.Stopwatch.StartNew();
                await _speedTestService.RunDownloadAsync(seconds, mbps =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _speedSession.AddDownloadSample(mbps);
                        int elapsed = (int)downloadSw.Elapsed.TotalSeconds;
                        _speedSession.Progress = 10 + (int)(Math.Min(elapsed, seconds) * 45.0 / seconds);
                    });
                }, ct);

                if (ct.IsCancellationRequested) return;

                // ── Phase 3: Upload ────────────────────────────────────────────
                _speedSession.Phase    = "Testing upload…";
                _speedSession.Progress = 55;

                var uploadSw = System.Diagnostics.Stopwatch.StartNew();
                await _speedTestService.RunUploadAsync(seconds, mbps =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _speedSession.AddUploadSample(mbps);
                        int elapsed = (int)uploadSw.Elapsed.TotalSeconds;
                        _speedSession.Progress = 55 + (int)(Math.Min(elapsed, seconds) * 45.0 / seconds);
                    });
                }, ct);

                _speedSession.Phase    = "Done";
                _speedSession.Progress = 100;
            }
            catch (OperationCanceledException)
            {
                _speedSession.Phase    = "Stopped";
                _speedSession.Progress = 0;
            }
            catch (Exception ex)
            {
                _speedSession.Phase    = $"Error: {ex.Message}";
                _speedSession.Progress = 0;
            }
            finally
            {
                _isSpeedTesting = false;
                _speedCts?.Dispose();
                _speedCts = null;
                StartSpeedTestBtn.Visibility = Visibility.Visible;
                StopSpeedTestBtn.Visibility  = Visibility.Collapsed;
            }
        }

        private void StopSpeedTest_Click(object sender, RoutedEventArgs e)
        {
            _speedCts?.Cancel();
        }

        #endregion

        #region Packet Capture

        /// <summary>
        /// Wraps an ILiveDevice with a richer display name that includes the
        /// interface's IPv4 address and a "(Primary)" label when it has a default gateway.
        /// </summary>
        private sealed class CaptureDeviceInfo
        {
            public ILiveDevice Device     { get; }
            public string      DisplayName { get; }
            public bool        IsPrimary  { get; }

            public CaptureDeviceInfo(ILiveDevice device, IReadOnlySet<string> gatewayIPs)
            {
                Device = device;

                var ips = GetDeviceIPv4s(device);
                IsPrimary = ips.Any(gatewayIPs.Contains);

                string ipPart    = ips.Count > 0 ? $"  [{string.Join(", ", ips)}]" : "";
                string badge     = IsPrimary ? "  ★ Primary" : "";
                DisplayName      = device.Description + ipPart + badge;
            }

            private static List<string> GetDeviceIPv4s(ILiveDevice device)
            {
                if (device is SharpPcap.LibPcap.LibPcapLiveDevice libDev)
                {
                    return libDev.Addresses
                        .Where(a => a.Addr?.ipAddress != null
                                 && a.Addr.ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                 && !IPAddress.IsLoopback(a.Addr.ipAddress))
                        .Select(a => a.Addr!.ipAddress!.ToString())
                        .ToList();
                }
                return new List<string>();
            }
        }

        /// <summary>
        /// Returns all IPv4 addresses belonging to NICs that have at least one default gateway.
        /// These are reliably the "active" interfaces the user is actually routing traffic through.
        /// </summary>
        private static HashSet<string> GetGatewayInterfaceIPs()
        {
            return System.Net.NetworkInformation.NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                         && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                         && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily
                                == System.Net.Sockets.AddressFamily.InterNetwork))
                .SelectMany(n => n.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString()))
                .ToHashSet();
        }

        /// <summary>Called once at startup — checks Npcap availability and populates the interface list.</summary>
        private void InitPacketCaptureTab()
        {
            if (!PacketCaptureService.IsPcapAvailable())
            {
                NpcapBanner.Visibility          = Visibility.Visible;
                CaptureStartBtn.IsEnabled       = false;
                CaptureInterfaceCombo.IsEnabled = false;
                return;
            }

            NpcapBanner.Visibility = Visibility.Collapsed;

            if (CaptureInterfaceCombo.Items.Count > 0) return;

            var gatewayIPs = GetGatewayInterfaceIPs();
            var infos = PacketCaptureService.GetDevices()
                            .Select(d => new CaptureDeviceInfo(d, gatewayIPs))
                            .ToList();

            foreach (var info in infos)
                CaptureInterfaceCombo.Items.Add(info);

            // Auto-select the first primary interface; fall back to index 0
            int best = infos.FindIndex(i => i.IsPrimary);
            CaptureInterfaceCombo.SelectedIndex = best >= 0 ? best : 0;
        }

        private void CaptureInterfaceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void CaptureStart_Click(object sender, RoutedEventArgs e)
        {
            if (CaptureInterfaceCombo.SelectedItem is not CaptureDeviceInfo info) return;
            var device = info.Device;

            _talkers.Clear();
            _resolvedHosts.Clear();

            try
            {
                _captureService.StartCapture(device);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start capture: {ex.Message}\n\nMake sure Npcap is installed and try running as administrator.",
                    "Capture Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CaptureStartBtn.IsEnabled        = false;
            CaptureStopBtn.IsEnabled         = true;
            CaptureInterfaceCombo.IsEnabled  = false;

            _captureRefreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _captureRefreshTimer.Tick += CaptureRefreshTimer_Tick;
            _captureRefreshTimer.Start();
        }

        private void CaptureStop_Click(object sender, RoutedEventArgs e)
        {
            _captureRefreshTimer?.Stop();
            _captureService.StopCapture();

            CaptureStartBtn.IsEnabled        = true;
            CaptureStopBtn.IsEnabled         = false;
            CaptureInterfaceCombo.IsEnabled  = true;
        }

        private void CaptureClear_Click(object sender, RoutedEventArgs e)
        {
            _captureService.Clear();
            _talkers.Clear();
            _resolvedHosts.Clear();
            CapturePacketsLabel.Text  = "0 packets";
            CaptureElapsedLabel.Text  = "00:00:00";
        }

        private void CaptureRefreshTimer_Tick(object? sender, EventArgs e)
        {
            // Update elapsed + packet count
            var elapsed = DateTime.Now - _captureService.StartTime;
            CaptureElapsedLabel.Text  = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            CapturePacketsLabel.Text  = $"{_captureService.TotalPackets:N0} packets";

            var snapshot = _captureService.GetSnapshot();
            long maxTotal = snapshot.Count > 0 ? (snapshot[0].BytesSent + snapshot[0].BytesReceived) : 1;
            if (maxTotal == 0) maxTotal = 1;

            const double maxBarWidth = 120.0;

            for (int i = 0; i < snapshot.Count; i++)
            {
                var (ip, sent, recv, pkts) = snapshot[i];

                // Find or create the entry
                var entry = _talkers.FirstOrDefault(t => t.IP == ip);
                if (entry == null)
                {
                    entry = new TalkerEntry { IP = ip, Hostname = ip };
                    _talkers.Add(entry);
                    // Kick off async hostname resolution
                    ResolveHostnameAsync(ip, entry);
                }

                entry.Rank          = i + 1;
                entry.BytesSent     = sent;
                entry.BytesReceived = recv;
                entry.Packets       = pkts;
                entry.BarWidth      = (double)(sent + recv) / maxTotal * maxBarWidth;

                // Refresh protocol breakdown only for expanded rows
                if (entry.IsExpanded)
                    RefreshProtocols(entry);
            }

            // Remove IPs that are no longer in the snapshot (e.g. after Clear)
            var current = new HashSet<string>(snapshot.Select(s => s.IP));
            for (int i = _talkers.Count - 1; i >= 0; i--)
                if (!current.Contains(_talkers[i].IP))
                    _talkers.RemoveAt(i);

            // Re-sort the observable collection to match snapshot order
            for (int i = 0; i < _talkers.Count; i++)
            {
                int desired = snapshot.FindIndex(s => s.IP == _talkers[i].IP);
                if (desired >= 0 && desired != i)
                {
                    _talkers.Move(i, desired < _talkers.Count ? desired : _talkers.Count - 1);
                }
            }
        }

        /// <summary>
        /// Restores DetailsVisibility when a DataGridRow is created or recycled by virtualisation.
        /// RowDetailsVisibilityMode="Collapsed" sets DetailsVisibility as a local value on load,
        /// overriding any Style setter, so we must re-apply it here.
        /// </summary>
        private void TalkersGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.DataContext is TalkerEntry entry)
                e.Row.DetailsVisibility = entry.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Propagates IsChecked → DetailsVisibility directly as a local value,
        /// which wins over the DataGrid's automatic visibility management.
        /// Also seeds protocol data immediately on first expand.
        /// </summary>
        private void ExpandChevron_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton tb || tb.DataContext is not TalkerEntry entry) return;

            var row = TalkersGrid.ItemContainerGenerator.ContainerFromItem(entry) as DataGridRow;
            if (row == null) return;

            bool expanded = tb.IsChecked == true;
            row.DetailsVisibility = expanded ? Visibility.Visible : Visibility.Collapsed;

            // Populate immediately on first expand rather than waiting for the next 1-second tick
            if (expanded && entry.Protocols.Count == 0)
                RefreshProtocols(entry);
        }

        private void RefreshProtocols(TalkerEntry entry)
        {
            var all = _captureService.GetProtocolSnapshot(entry.IP);

            // Cap at top 8 — enough for a quick overview without overwhelming
            if (all.Count > 8) all = all.GetRange(0, 8);

            long maxBytes    = all.Count > 0 ? all[0].Bytes : 1;
            if (maxBytes == 0) maxBytes = 1;
            const double maxBarWidth = 110.0;

            for (int i = 0; i < all.Count; i++)
            {
                var (proto, port, bytes, pkts) = all[i];

                // For ICMP/other: show "—"; for known port: service name; for unknown: ":port"
                string serviceName  = port == 0 ? "—" : PacketCaptureService.ServiceName(port);
                string bytesDisplay = TalkerEntry.FormatBytes(bytes);
                double barWidth     = (double)bytes / maxBytes * maxBarWidth;

                if (i < entry.Protocols.Count)
                {
                    var ex = entry.Protocols[i];
                    ex.Protocol     = proto;
                    ex.ServiceName  = serviceName;
                    ex.BytesDisplay = bytesDisplay;
                    ex.Packets      = pkts;
                    ex.BarWidth     = barWidth;
                }
                else
                {
                    entry.Protocols.Add(new Models.ProtocolStat
                    {
                        Protocol    = proto,
                        ServiceName = serviceName,
                        BytesDisplay = bytesDisplay,
                        Packets     = pkts,
                        BarWidth    = barWidth
                    });
                }
            }

            // Trim stale rows
            while (entry.Protocols.Count > all.Count)
                entry.Protocols.RemoveAt(entry.Protocols.Count - 1);
        }

        private async void ResolveHostnameAsync(string ip, TalkerEntry entry)
        {
            if (_resolvedHosts.TryGetValue(ip, out var cached))
            {
                entry.Hostname = cached;
                return;
            }
            try
            {
                var hostEntry = await Dns.GetHostEntryAsync(ip);
                string name = hostEntry.HostName;
                _resolvedHosts[ip] = name;
                entry.Hostname = name;
            }
            catch
            {
                _resolvedHosts[ip] = ip; // don't retry
            }
        }

        private void CaptureExport_Click(object sender, RoutedEventArgs e)
        {
            if (_talkers.Count == 0)
            {
                MessageBox.Show("No capture data to export. Start a capture session first.",
                    "Nothing to Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title      = "Export Packet Capture",
                Filter     = "JSON file (*.json)|*.json",
                DefaultExt = ".json",
                FileName   = $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };
            if (dlg.ShowDialog() != true) return;

            string interfaceName = CaptureInterfaceCombo.SelectedItem is CaptureDeviceInfo info
                ? info.DisplayName : "Unknown";

            var export = new
            {
                captureInfo = new
                {
                    networkInterface = interfaceName,
                    startTime        = _captureService.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    duration         = CaptureElapsedLabel.Text,   // already formatted HH:MM:SS
                    totalPackets     = _captureService.TotalPackets,
                    exportedAt       = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                },
                topTalkers = _talkers.Select(t =>
                {
                    // Export full protocol breakdown — not capped at 8 like the UI overview
                    var protos = _captureService.GetProtocolSnapshot(t.IP);
                    return new
                    {
                        rank              = t.Rank,
                        ip                = t.IP,
                        hostname          = t.Hostname,
                        bytesSent         = t.BytesSent,
                        bytesReceived     = t.BytesReceived,
                        totalBytes        = t.TotalBytes,
                        sentFormatted     = t.SentDisplay,
                        receivedFormatted = t.ReceivedDisplay,
                        totalFormatted    = t.TotalDisplay,
                        packets           = t.Packets,
                        protocols         = protos.Select(p => new
                        {
                            protocol       = p.Proto,
                            service        = PacketCaptureService.ServiceName(p.Port),
                            port           = p.Port,
                            bytes          = p.Bytes,
                            bytesFormatted = TalkerEntry.FormatBytes(p.Bytes),
                            packets        = p.Packets
                        }).ToList()
                    };
                }).ToList()
            };

            var json = JsonSerializer.Serialize(export,
                new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(dlg.FileName, json);

            MessageBox.Show($"Exported {_talkers.Count} host{(_talkers.Count == 1 ? "" : "s")} to:\n{dlg.FileName}",
                "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DownloadNpcap_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://npcap.com/#download",
                UseShellExecute = true
            });
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _captureRefreshTimer?.Stop();
            _captureService.Dispose();
        }

        #endregion
    }
}
