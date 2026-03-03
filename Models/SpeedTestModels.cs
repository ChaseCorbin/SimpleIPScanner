using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SimpleIPScanner.Models
{
    public class PingServerResult : INotifyPropertyChanged
    {
        private long _latency;

        public string ServerName { get; set; } = "";
        public string IP         { get; set; } = "";

        public long Latency
        {
            get => _latency;
            set { _latency = value; OnPropertyChanged(nameof(Latency)); OnPropertyChanged(nameof(LatencyDisplay)); OnPropertyChanged(nameof(IsTimeout)); }
        }

        public bool   IsTimeout      => _latency < 0;
        public string LatencyDisplay => _latency < 0 ? "Timeout" : $"{_latency} ms";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class SpeedTestSession : INotifyPropertyChanged
    {
        private string _phase           = "Ready";
        private int    _progress        = 0;
        private double _downloadMbps    = 0;
        private double _uploadMbps      = 0;
        private double _peakDownload    = 0;
        private double _peakUpload      = 0;
        private double _maxChartMbps    = 10;
        private bool   _isRunning       = false;
        private long   _bestPingMs      = -1;
        private int    _testSeconds     = 10;

        public string Phase
        {
            get => _phase;
            set { _phase = value; OnPropertyChanged(nameof(Phase)); }
        }

        public int Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(nameof(Progress)); }
        }

        public double DownloadMbps
        {
            get => _downloadMbps;
            set { _downloadMbps = value; OnPropertyChanged(nameof(DownloadMbps)); OnPropertyChanged(nameof(DownloadDisplay)); }
        }

        public double UploadMbps
        {
            get => _uploadMbps;
            set { _uploadMbps = value; OnPropertyChanged(nameof(UploadMbps)); OnPropertyChanged(nameof(UploadDisplay)); }
        }

        public double PeakDownload
        {
            get => _peakDownload;
            set { _peakDownload = value; OnPropertyChanged(nameof(PeakDownload)); OnPropertyChanged(nameof(PeakDownloadDisplay)); }
        }

        public double PeakUpload
        {
            get => _peakUpload;
            set { _peakUpload = value; OnPropertyChanged(nameof(PeakUpload)); OnPropertyChanged(nameof(PeakUploadDisplay)); }
        }

        public double MaxChartMbps
        {
            get => _maxChartMbps;
            set { _maxChartMbps = value; OnPropertyChanged(nameof(MaxChartMbps)); }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(nameof(IsRunning)); }
        }

        public long BestPingMs
        {
            get => _bestPingMs;
            set { _bestPingMs = value; OnPropertyChanged(nameof(BestPingMs)); OnPropertyChanged(nameof(PingDisplay)); }
        }

        public int TestSeconds
        {
            get => _testSeconds;
            set { _testSeconds = value; OnPropertyChanged(nameof(TestSeconds)); OnPropertyChanged(nameof(XAxisMidLabel)); OnPropertyChanged(nameof(XAxisEndLabel)); }
        }

        public string DownloadDisplay     => _downloadMbps > 0 ? $"{_downloadMbps:F1}" : "—";
        public string UploadDisplay       => _uploadMbps   > 0 ? $"{_uploadMbps:F1}"   : "—";
        public string PeakDownloadDisplay => _peakDownload > 0 ? $"Peak {_peakDownload:F1} Mbps" : "";
        public string PeakUploadDisplay   => _peakUpload   > 0 ? $"Peak {_peakUpload:F1} Mbps"   : "";
        public string PingDisplay         => _bestPingMs   < 0 ? "—" : $"{_bestPingMs} ms";
        public string XAxisMidLabel       => $"{_testSeconds / 2}s";
        public string XAxisEndLabel       => $"{_testSeconds}s";

        // Chart data — plain lists; PropertyChanged raised once per sample (no N² WPF loop)
        public List<double> DownloadHistory { get; } = new();
        public List<double> UploadHistory   { get; } = new();

        public List<PingServerResult> PingServers { get; } = new();

        public void AddDownloadSample(double mbps)
        {
            DownloadHistory.Add(mbps);
            DownloadMbps = mbps;
            if (mbps > _peakDownload) PeakDownload = mbps;

            // Auto-scale Y-axis: double ceiling whenever a sample exceeds 80%
            while (mbps >= _maxChartMbps * 0.8)
                MaxChartMbps = _maxChartMbps * 2;

            OnPropertyChanged(nameof(DownloadHistory));
        }

        public void AddUploadSample(double mbps)
        {
            UploadHistory.Add(mbps);
            UploadMbps = mbps;
            if (mbps > _peakUpload) PeakUpload = mbps;

            while (mbps >= _maxChartMbps * 0.8)
                MaxChartMbps = _maxChartMbps * 2;

            OnPropertyChanged(nameof(UploadHistory));
        }

        public void Reset()
        {
            Phase          = "Ready";
            Progress       = 0;
            DownloadMbps   = 0;
            UploadMbps     = 0;
            PeakDownload   = 0;
            PeakUpload     = 0;
            MaxChartMbps   = 10;
            BestPingMs     = -1;
            IsRunning      = false;
            DownloadHistory.Clear();
            UploadHistory.Clear();
            PingServers.Clear();
            OnPropertyChanged(nameof(DownloadHistory));
            OnPropertyChanged(nameof(UploadHistory));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
