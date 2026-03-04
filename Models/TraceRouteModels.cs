using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace SimpleIPScanner.Models
{
    public class TraceDataPoint
    {
        public DateTime Timestamp { get; set; }
        public double Latency { get; set; }
    }

    public class TraceHop : INotifyPropertyChanged
    {
        private long _latency;
        private string _ip = "";
        private string _hostname = "";
        private bool _isTimeout;

        public int HopNumber { get; set; }

        public string IP { get => _ip; set { _ip = value; OnPropertyChanged(nameof(IP)); } }
        public string Hostname { get => _hostname; set { _hostname = value; OnPropertyChanged(nameof(Hostname)); } }

        public long Latency { get => _latency; set { _latency = value; OnPropertyChanged(nameof(Latency)); } }
        public bool IsTimeout { get => _isTimeout; set { _isTimeout = value; OnPropertyChanged(nameof(IsTimeout)); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class TraceSession : INotifyPropertyChanged
    {
        private string _destination = "";
        private bool _isActive;
        private string _status = "Stopped";
        private DateTime? _startTime;
        private TimeSpan _elapsed = TimeSpan.Zero;
        private bool _isPaused;

        // Pan state: when panned, the view is anchored to a fixed end time instead of "now"
        private bool _isPanned;
        private DateTime _pinnedViewEnd;

        // Zoom state: when zoomed, the view shows a custom time range regardless of ChartIntervalMinutes
        private bool _isZoomed;
        private DateTime _zoomStart;
        private DateTime _zoomEnd;

        public string Destination { get => _destination; set { _destination = value; OnPropertyChanged(nameof(Destination)); } }
        public bool IsActive { get => _isActive; set { _isActive = value; OnPropertyChanged(nameof(IsActive)); } }
        public string Status { get => _status; set { _status = value; OnPropertyChanged(nameof(Status)); } }

        public DateTime? StartTime { get => _startTime; set { _startTime = value; OnPropertyChanged(nameof(StartTime)); } }
        public TimeSpan Elapsed { get => _elapsed; set { _elapsed = value; OnPropertyChanged(nameof(Elapsed)); OnPropertyChanged(nameof(ElapsedDisplay)); } }

        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                _isPaused = value;
                OnPropertyChanged(nameof(IsPaused));
                if (!_isPaused) UpdateFilteredHistory();
            }
        }

        // The visible time window boundaries used by the chart and X-axis labels
        public DateTime ViewEnd => _isZoomed ? _zoomEnd : (_isPanned ? _pinnedViewEnd : DateTime.Now);
        public DateTime ViewStart => _isZoomed ? _zoomStart : ViewEnd.AddMinutes(-ChartIntervalMinutes);
        public bool IsLive => !_isPanned && !_isZoomed;
        public bool IsZoomed => _isZoomed;

        public string ElapsedDisplay => $"{(int)Elapsed.TotalHours:D2}:{Elapsed.Minutes:D2}:{Elapsed.Seconds:D2}";

        public ObservableCollection<TraceHop> Hops { get; } = new();

        // Three-tier storage constants.
        // Tier 1: Full 1-sec resolution for the last 60 minutes (3,600 points).
        private const int RecentWindowPoints = 3_600;
        // Block size compressed from Tier 1 → Tier 2 (60 pts = 60 seconds).
        private const int ArchiveBlockSize   = 60;
        // Tier 2: Medium 10-pt/min for the hour before Tier 1 (600 points = 60 min × 10/min).
        private const int MediumWindowPoints = 600;
        // Block size compressed from Tier 2 → Tier 3 (10 medium pts = 1 minute of medium-res).
        private const int DeepBlockSize      = 10;

        // Tier 1: Full-resolution recent data (last 60 minutes, 1-pt/sec).
        public List<TraceDataPoint> LatencyHistory { get; } = new();

        // Tier 2: Medium-resolution archive (1–2 hours ago, 1-pt/6-sec = 10-pt/min).
        public List<TraceDataPoint> MediumHistory { get; } = new();

        // Tier 3: Deep archive (2+ hours ago, 1-pt/min).
        public List<TraceDataPoint> ArchivedHistory { get; } = new();

        // Exact-timestamp events preserved during compression (timeouts + pings > 100 ms).
        // These are NOT averaged — they retain their original second-precision timestamp
        // so that event moments remain visible when viewing hours-old archived data.
        public List<TraceDataPoint> EventHistory { get; } = new();

        // Pre-filtered event sublists for the current view window (updated in UpdateFilteredHistory).
        // Exposed separately so the chart can render two coloured overlay Path elements without
        // mixing events into the main latency polyline.
        private List<TraceDataPoint> _filteredTimeoutEvents = new();
        private List<TraceDataPoint> _filteredSpikeEvents   = new();
        public IReadOnlyList<TraceDataPoint> FilteredTimeoutEvents => _filteredTimeoutEvents;
        public IReadOnlyList<TraceDataPoint> FilteredSpikeEvents   => _filteredSpikeEvents;

        private int _chartIntervalMinutes = 1;
        public int ChartIntervalMinutes
        {
            get => _chartIntervalMinutes;
            set { _chartIntervalMinutes = value; OnPropertyChanged(nameof(ChartIntervalMinutes)); UpdateFilteredHistory(); OnPropertyChanged(nameof(FilteredHistory)); }
        }

        // Plain List — not ObservableCollection. OnPropertyChanged("FilteredHistory") fires ONCE
        // per update instead of N+1 CollectionChanged events, eliminating the N² WPF rendering loop.
        private List<TraceDataPoint> _filteredHistory = new();
        public IReadOnlyList<TraceDataPoint> FilteredHistory => _filteredHistory;
        public int FilteredHistoryCount => _filteredHistory.Count;

        public double MaxLatencyValue => FilteredHistory.Any() ? Math.Max(100, FilteredHistory.Max(p => p.Latency)) : 100;
        public double AverageLatency => FilteredHistory.Any(p => p.Latency >= 0) ? FilteredHistory.Where(p => p.Latency >= 0).Average(p => p.Latency) : 0;
        public double PacketLoss => CalculatePacketLoss();

        private string TimeFormat => ChartIntervalMinutes <= 10 ? "HH:mm:ss" : "HH:mm";
        // X-axis labels are anchored to the fixed view window, not to data point timestamps
        public string XAxisStartLabel => ViewStart.ToString(TimeFormat);
        public string XAxisMidLabel   => ViewStart.AddMinutes(ChartIntervalMinutes / 2.0).ToString(TimeFormat);
        public string XAxisEndLabel   => ViewEnd.ToString(TimeFormat);

        private double CalculatePacketLoss()
        {
            // Count recent timeouts (still in FilteredHistory) plus archived timeout events.
            // Spike events count as successful pings in the denominator.
            int timeouts = FilteredHistory.Count(p => p.Latency < 0) + _filteredTimeoutEvents.Count;
            int total    = FilteredHistory.Count + _filteredTimeoutEvents.Count + _filteredSpikeEvents.Count;
            return total == 0 ? 0 : (double)timeouts / total * 100;
        }

        private void UpdateFilteredHistory()
        {
            if (IsPaused) return;

            var start = ViewStart;
            var end = ViewEnd;

            // Main polyline: deep archive (1-pt/min) + medium archive (10-pt/min) + recent full-res.
            // Events are intentionally excluded — they render as separate overlay Path elements.
            var deep   = ArchivedHistory.Where(p => p.Timestamp >= start && p.Timestamp <= end);
            var medium = MediumHistory  .Where(p => p.Timestamp >= start && p.Timestamp <= end);
            var recent = LatencyHistory .Where(p => p.Timestamp >= start && p.Timestamp <= end);
            _filteredHistory = deep.Concat(medium).Concat(recent).ToList();

            // Event marker lists for the two coloured overlay paths.
            var windowEvents = EventHistory.Where(p => p.Timestamp >= start && p.Timestamp <= end);
            _filteredTimeoutEvents = windowEvents.Where(p => p.Latency < 0) .ToList();
            _filteredSpikeEvents   = windowEvents.Where(p => p.Latency >= 0).ToList();

            OnPropertyChanged(nameof(FilteredHistory));
            OnPropertyChanged(nameof(FilteredHistoryCount));
            OnPropertyChanged(nameof(FilteredTimeoutEvents));
            OnPropertyChanged(nameof(FilteredSpikeEvents));
            OnPropertyChanged(nameof(MaxLatencyValue));
            OnPropertyChanged(nameof(AverageLatency));
            OnPropertyChanged(nameof(PacketLoss));
            OnPropertyChanged(nameof(ViewStart));
            OnPropertyChanged(nameof(ViewEnd));
            OnPropertyChanged(nameof(IsLive));
            OnPropertyChanged(nameof(XAxisStartLabel));
            OnPropertyChanged(nameof(XAxisMidLabel));
            OnPropertyChanged(nameof(XAxisEndLabel));
        }

        /// <summary>
        /// Anchors the chart view to a specific end time (entering pan mode).
        /// Clamps to available data and snaps back to live within 5 seconds of now.
        /// </summary>
        public void PanTo(DateTime viewEnd)
        {
            DateTime now = DateTime.Now;

            // Snap to live when within 5 seconds of now
            if ((now - viewEnd).TotalSeconds < 5)
            {
                ResetToLive();
                return;
            }

            // Clamp: can't pan past available history (all tiers + events + recent)
            DateTime? oldestPoint = null;
            if (ArchivedHistory.Any()) oldestPoint = ArchivedHistory[0].Timestamp;
            if (MediumHistory.Any() && (oldestPoint == null || MediumHistory[0].Timestamp < oldestPoint.Value))
                oldestPoint = MediumHistory[0].Timestamp;
            if (EventHistory.Any() && (oldestPoint == null || EventHistory[0].Timestamp < oldestPoint.Value))
                oldestPoint = EventHistory[0].Timestamp;
            if (!oldestPoint.HasValue && LatencyHistory.Any())
                oldestPoint = LatencyHistory[0].Timestamp;
            if (oldestPoint.HasValue)
            {
                DateTime oldestAllowed = oldestPoint.Value.AddMinutes(ChartIntervalMinutes);
                if (viewEnd < oldestAllowed) viewEnd = oldestAllowed;
            }

            // Clamp: can't pan into the future
            if (viewEnd > now) viewEnd = now;

            _isPanned = true;
            _pinnedViewEnd = viewEnd;
            UpdateFilteredHistory();
        }

        /// <summary>Snaps the view back to live mode (tracking DateTime.Now).</summary>
        public void ResetToLive()
        {
            _isPanned = false;
            _isZoomed = false;
            UpdateFilteredHistory();
            OnPropertyChanged(nameof(IsZoomed));
        }

        /// <summary>
        /// Zooms the chart view to an exact time range. Disables both live tracking and pan mode.
        /// Minimum selection is 5 seconds; smaller selections are ignored.
        /// </summary>
        public void ZoomTo(DateTime start, DateTime end)
        {
            if ((end - start).TotalSeconds < 5) return;
            _isZoomed = true;
            _isPanned = false;
            _zoomStart = start;
            _zoomEnd = end;
            UpdateFilteredHistory();
            OnPropertyChanged(nameof(IsZoomed));
        }

        /// <summary>
        /// Exits zoom mode, resets to live 5-minute view (the default interval).
        /// </summary>
        public void ResetZoom()
        {
            _isZoomed = false;
            _isPanned = false;
            _chartIntervalMinutes = 5;
            UpdateFilteredHistory();
            OnPropertyChanged(nameof(IsZoomed));
            OnPropertyChanged(nameof(ChartIntervalMinutes));
        }

        public void AddDataPoint(long latency)
        {
            LatencyHistory.Add(new TraceDataPoint { Timestamp = DateTime.Now, Latency = latency >= 0 ? (double)latency : -1.0 });

            // Tier 1 → Tier 2: compress oldest 60-second block into 10 medium-res points (1-pt/6-sec).
            // Timeouts and spikes ≥ 100 ms are preserved in EventHistory at exact timestamps.
            while (LatencyHistory.Count > RecentWindowPoints)
            {
                var block = LatencyHistory.GetRange(0, ArchiveBlockSize);
                var successful = block.Where(p => p.Latency >= 0).Select(p => p.Latency).ToList();
                const double spikeThreshold = 100.0;

                if (successful.Count == 0)
                {
                    // All timeouts — one representative timeout event for the whole minute.
                    EventHistory.Add(new TraceDataPoint
                    {
                        Timestamp = block[ArchiveBlockSize / 2].Timestamp,
                        Latency   = -1
                    });
                }
                else
                {
                    // Preserve individual timeouts and spikes at their exact timestamps.
                    foreach (var p in block)
                    {
                        if (p.Latency < 0 || p.Latency >= spikeThreshold)
                            EventHistory.Add(p);
                    }

                    // Compress normal pings into 10 medium-res sub-blocks of 6 seconds each.
                    const int subBlockSize = ArchiveBlockSize / DeepBlockSize; // 6
                    for (int i = 0; i < ArchiveBlockSize; i += subBlockSize)
                    {
                        var sub = block.GetRange(i, Math.Min(subBlockSize, block.Count - i));
                        var normalPings = sub
                            .Where(p => p.Latency >= 0 && p.Latency < spikeThreshold)
                            .Select(p => p.Latency).ToList();
                        if (normalPings.Count > 0)
                        {
                            MediumHistory.Add(new TraceDataPoint
                            {
                                Timestamp = sub[sub.Count / 2].Timestamp,
                                Latency   = normalPings.Average()
                            });
                        }
                    }
                }

                LatencyHistory.RemoveRange(0, ArchiveBlockSize);
            }

            // Tier 2 → Tier 3: compress 1 hour of medium-res into 1-pt/min deep archive.
            while (MediumHistory.Count > MediumWindowPoints)
            {
                var block = MediumHistory.GetRange(0, DeepBlockSize);
                var normalPings = block.Where(p => p.Latency >= 0).Select(p => p.Latency).ToList();
                if (normalPings.Count > 0)
                {
                    ArchivedHistory.Add(new TraceDataPoint
                    {
                        Timestamp = block[DeepBlockSize / 2].Timestamp,
                        Latency   = normalPings.Average()
                    });
                }
                MediumHistory.RemoveRange(0, DeepBlockSize);
            }

            UpdateFilteredHistory();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            if (name == nameof(IsActive))
            {
                // Trigger a notification that can be used to refresh visuals
                OnPropertyChanged(nameof(SelectedTargetStatus));
            }
        }

        public object? SelectedTargetStatus => null;
    }
}
