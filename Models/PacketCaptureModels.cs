using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SimpleIPScanner.Models
{
    public class ProtocolStat : INotifyPropertyChanged
    {
        private string _protocol    = "";
        private string _serviceName = "";
        private string _bytesDisplay = "";
        private long   _packets;
        private double _barWidth;

        public string Protocol
        {
            get => _protocol;
            set { _protocol = value; OnPropertyChanged(nameof(Protocol)); }
        }
        public string ServiceName
        {
            get => _serviceName;
            set { _serviceName = value; OnPropertyChanged(nameof(ServiceName)); }
        }
        public string BytesDisplay
        {
            get => _bytesDisplay;
            set { _bytesDisplay = value; OnPropertyChanged(nameof(BytesDisplay)); }
        }
        public long Packets
        {
            get => _packets;
            set { _packets = value; OnPropertyChanged(nameof(Packets)); }
        }
        /// <summary>Bar width in pixels (0–120), proportional to this entry's share of the IP's traffic.</summary>
        public double BarWidth
        {
            get => _barWidth;
            set { _barWidth = value; OnPropertyChanged(nameof(BarWidth)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class TalkerEntry : INotifyPropertyChanged
    {
        private int _rank;
        private string _ip = "";
        private string _hostname = "";
        private long _bytesSent;
        private long _bytesReceived;
        private long _packets;
        private double _barWidth;
        private bool _isExpanded;

        public int Rank
        {
            get => _rank;
            set { _rank = value; OnPropertyChanged(nameof(Rank)); }
        }

        public string IP
        {
            get => _ip;
            set { _ip = value; OnPropertyChanged(nameof(IP)); }
        }

        public string Hostname
        {
            get => _hostname;
            set { _hostname = value; OnPropertyChanged(nameof(Hostname)); }
        }

        public long BytesSent
        {
            get => _bytesSent;
            set
            {
                _bytesSent = value;
                OnPropertyChanged(nameof(BytesSent));
                OnPropertyChanged(nameof(TotalBytes));
                OnPropertyChanged(nameof(SentDisplay));
                OnPropertyChanged(nameof(TotalDisplay));
            }
        }

        public long BytesReceived
        {
            get => _bytesReceived;
            set
            {
                _bytesReceived = value;
                OnPropertyChanged(nameof(BytesReceived));
                OnPropertyChanged(nameof(TotalBytes));
                OnPropertyChanged(nameof(ReceivedDisplay));
                OnPropertyChanged(nameof(TotalDisplay));
            }
        }

        public long Packets
        {
            get => _packets;
            set { _packets = value; OnPropertyChanged(nameof(Packets)); }
        }

        /// <summary>Width (0–120) of the bar in the Total column, set by the refresh tick.</summary>
        public double BarWidth
        {
            get => _barWidth;
            set { _barWidth = value; OnPropertyChanged(nameof(BarWidth)); }
        }

        /// <summary>Whether the protocol breakdown row is expanded for this entry.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); }
        }

        /// <summary>Per-protocol breakdown, populated when the row is expanded.</summary>
        public ObservableCollection<ProtocolStat> Protocols { get; } = new();

        public long TotalBytes => BytesSent + BytesReceived;

        public string SentDisplay     => FormatBytes(BytesSent);
        public string ReceivedDisplay => FormatBytes(BytesReceived);
        public string TotalDisplay    => FormatBytes(TotalBytes);

        internal static string FormatBytes(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1_024)         return $"{bytes / 1_024.0:F1} KB";
            return $"{bytes} B";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
