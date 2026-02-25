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

        public string ElapsedDisplay => $"{(int)Elapsed.TotalHours:D2}:{Elapsed.Minutes:D2}:{Elapsed.Seconds:D2}";

        public ObservableCollection<TraceHop> Hops { get; } = new();
        
        // History of latencies for the final hop. Stores up to 8 hours of data.
        public List<TraceDataPoint> LatencyHistory { get; } = new();
        
        private int _chartIntervalMinutes = 1;
        public int ChartIntervalMinutes 
        { 
            get => _chartIntervalMinutes; 
            set { _chartIntervalMinutes = value; OnPropertyChanged(nameof(ChartIntervalMinutes)); UpdateFilteredHistory(); OnPropertyChanged(nameof(FilteredHistory)); } 
        }

        public ObservableCollection<TraceDataPoint> FilteredHistory { get; } = new();

        public double MaxLatencyValue => FilteredHistory.Any() ? Math.Max(100, FilteredHistory.Max(p => p.Latency)) : 100;
        public double AverageLatency => FilteredHistory.Any(p => p.Latency >= 0) ? FilteredHistory.Where(p => p.Latency >= 0).Average(p => p.Latency) : 0;
        public double PacketLoss => CalculatePacketLoss();

        private string TimeFormat => ChartIntervalMinutes <= 10 ? "HH:mm:ss" : "HH:mm";
        public string XAxisStartLabel => FilteredHistory.Count > 0 ? FilteredHistory[0].Timestamp.ToString(TimeFormat) : "";
        public string XAxisMidLabel   => FilteredHistory.Count > 1 ? FilteredHistory[FilteredHistory.Count / 2].Timestamp.ToString(TimeFormat) : "";
        public string XAxisEndLabel   => FilteredHistory.Count > 0 ? FilteredHistory[FilteredHistory.Count - 1].Timestamp.ToString(TimeFormat) : "";

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

            var cutoff = DateTime.Now.AddMinutes(-ChartIntervalMinutes);
            var data = LatencyHistory.Where(p => p.Timestamp >= cutoff).ToList();
            
            // Efficiently update the existing collection to avoid binding churn
            FilteredHistory.Clear();
            foreach (var d in data) FilteredHistory.Add(d);
            
            OnPropertyChanged(nameof(MaxLatencyValue));
            OnPropertyChanged(nameof(AverageLatency));
            OnPropertyChanged(nameof(PacketLoss));
            OnPropertyChanged(nameof(FilteredHistory));
            OnPropertyChanged(nameof(XAxisStartLabel));
            OnPropertyChanged(nameof(XAxisMidLabel));
            OnPropertyChanged(nameof(XAxisEndLabel));
        }

        public void AddDataPoint(long latency)
        {
            LatencyHistory.Add(new TraceDataPoint { Timestamp = DateTime.Now, Latency = latency >= 0 ? (double)latency : -1.0 });
            
            // Cleanup data older than 2 hours
            var twoHrAgo = DateTime.Now.AddHours(-2);
            if (LatencyHistory.Count > 0 && LatencyHistory[0].Timestamp < twoHrAgo)
            {
                LatencyHistory.RemoveAll(p => p.Timestamp < twoHrAgo);
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
