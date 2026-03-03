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

        // Hard cap: 7,200 points = 2 hours at 1 ping/sec.
        private const int MaxHistoryPoints = 7_200;

        // History of latencies for the final hop. Stores up to 2 hours of data.
        public List<TraceDataPoint> LatencyHistory { get; } = new();

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
            var history = FilteredHistory;
            if (!history.Any()) return 0;
            int timeouts = history.Count(p => p.Latency < 0);
            return (double)timeouts / history.Count * 100;
        }

        private void UpdateFilteredHistory()
        {
            if (IsPaused) return;

            var start = ViewStart;
            var end = ViewEnd;

            // Atomic swap — no per-item CollectionChanged events; one PropertyChanged at the end.
            _filteredHistory = LatencyHistory
                .Where(p => p.Timestamp >= start && p.Timestamp <= end)
                .ToList();

            OnPropertyChanged(nameof(FilteredHistory));
            OnPropertyChanged(nameof(FilteredHistoryCount));
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

            // Clamp: can't pan past available history
            if (LatencyHistory.Any())
            {
                DateTime oldestAllowed = LatencyHistory[0].Timestamp.AddMinutes(ChartIntervalMinutes);
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

            // Trim time-expired entries from the front (data is chronological so scan from index 0).
            // RemoveRange is used once the cutoff is found, avoiding a full-list predicate scan.
            if (LatencyHistory.Count > 0 && LatencyHistory[0].Timestamp < DateTime.Now.AddHours(-2))
            {
                int staleCount = 0;
                var twoHrAgo = DateTime.Now.AddHours(-2);
                while (staleCount < LatencyHistory.Count && LatencyHistory[staleCount].Timestamp < twoHrAgo)
                    staleCount++;
                if (staleCount > 0)
                    LatencyHistory.RemoveRange(0, staleCount);
            }

            // Hard cap: belt-and-suspenders guard against unexpected fast accumulation.
            if (LatencyHistory.Count > MaxHistoryPoints)
                LatencyHistory.RemoveRange(0, LatencyHistory.Count - MaxHistoryPoints);

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
