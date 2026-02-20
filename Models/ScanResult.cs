using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SimpleIPScanner.Models
{
    public class ScanResult : INotifyPropertyChanged
    {
        private string _ip = "";
        private string _subnet = "";
        private string _hostname = "N/A";
        private string _mac = "N/A";
        private string _vendor = "";
        private bool _isOnline;
        private int _pingMs;
        private bool _isScanning;
        private string _openPorts = "";
        private bool _isPortScanning;
        private bool _portsVisible;
        private uint? _sortKey;

        public string IP
        {
            get => _ip;
            set { _ip = value; _sortKey = null; OnPropertyChanged(); }
        }

        /// <summary>The source subnet CIDR this device was discovered in, e.g. "192.168.1.0/24".</summary>
        public string Subnet
        {
            get => _subnet;
            set { _subnet = value; OnPropertyChanged(); }
        }

        public string Hostname
        {
            get => _hostname;
            set { _hostname = value; OnPropertyChanged(); }
        }

        public string MAC
        {
            get => _mac;
            set { _mac = value; OnPropertyChanged(); }
        }

        public string Vendor
        {
            get => _vendor;
            set { _vendor = value; OnPropertyChanged(); }
        }

        public bool IsOnline
        {
            get => _isOnline;
            set { _isOnline = value; OnPropertyChanged(); OnPropertyChanged(nameof(Status)); }
        }

        public int PingMs
        {
            get => _pingMs;
            set { _pingMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(PingDisplay)); }
        }

        private int _ttl;
        public int TTL
        {
            get => _ttl;
            set { _ttl = value; OnPropertyChanged(); OnPropertyChanged(nameof(OSType)); OnPropertyChanged(nameof(OSIcon)); }
        }

        public string OSType
        {
            get
            {
                if (TTL <= 0) return "";

                // High-priority hardware/hostname checks
                bool isAppleVendor = Vendor.Contains("Apple", StringComparison.OrdinalIgnoreCase);
                bool isAppleHost = Hostname.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || 
                                  Hostname.Contains("iPad", StringComparison.OrdinalIgnoreCase) || 
                                  Hostname.Contains("MacBook", StringComparison.OrdinalIgnoreCase) ||
                                  Hostname.Contains("iMac", StringComparison.OrdinalIgnoreCase) ||
                                  Hostname.Contains("Apple", StringComparison.OrdinalIgnoreCase);

                if (isAppleVendor || isAppleHost) return "Apple";
                
                // Best-effort OS heuristic based on TTL
                // Windows: 128 (often seen as 128 or slightly less due to hops, though usually 128 on local)
                // Linux/Unix/iOS/Android: 64
                // Network devices (Cisco/Switch/Router): 255
                
                if (TTL > 100 && TTL <= 128) return "Windows";
                
                if (TTL > 32 && TTL <= 64)
                {
                    // At this point, if it's not Apple (checked above), it's likely Linux/Android
                    return "Linux";
                }

                if (Vendor.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) return "Windows";
                if (Vendor.Contains("Google", StringComparison.OrdinalIgnoreCase)) return "Linux"; // Android

                return "";
            }
        }

        public string OSIcon
        {
            get
            {
                return OSType switch
                {
                    // Modern Windows 11-style outline
                    "Windows" => "M3,3H11V11H3V3M13,3H21V11H13V3M3,13H11V21H3V13M13,13H21V21H13V13Z",
                    
                    // Refined Apple silhouette outline (Classic Apple Logo)
                    "Apple" => "M17.74,10C17.72,7.74 19.64,6.58 19.74,6.5C18.68,4.94 17.06,4.72 16.48,4.71C15.08,4.56 13.72,5.55 13,5.55C12.3,5.55 11.18,4.73 10,4.75C8.46,4.78 7.02,5.65 6.22,7.06C4.6,9.88 5.81,14.06 7.37,16.31C8.13,17.41 9.04,18.66 10.23,18.61C11.38,18.57 11.82,17.87 13.21,17.87C14.6,17.87 15,18.57 16.21,18.55C17.46,18.53 18.25,17.42 19.01,16.32C19.88,15.03 20.25,13.78 20.27,13.71C20.25,13.7 17.78,12.75 17.74,10M15,3.47C15.62,2.7 16.05,1.65 15.93,0.59C15.04,0.62 13.94,1.18 13.31,1.93C12.74,2.58 12.24,3.67 12.37,4.72C13.35,4.8 14.39,4.22 15,3.47Z",

                    // Bow Tie Silhouette (for Linux/Other)
                    "Linux" => "M15,14l7,4V6l-7,4V14z M9,10L2,6v12l7-4V10z M10.5,10h3v4h-3V10z",
                    
                    _ => ""
                };
            }
        }


        /// <summary>
        /// True while a single-device re-scan is in progress.
        /// </summary>
        public bool IsScanning
        {
            get => _isScanning;
            set { _isScanning = value; OnPropertyChanged(); }
        }

        public string OpenPorts
        {
            get => _openPorts;
            set { _openPorts = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPorts)); OnPropertyChanged(nameof(OpenPortsList)); }
        }

        public List<string> OpenPortsList => string.IsNullOrEmpty(OpenPorts) ? new List<string>() : OpenPorts.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries).ToList();

        public string PortSummary => string.IsNullOrEmpty(OpenPorts) ? "" : $"{OpenPortsList.Count} open";

        private bool _hasScanRun;
        public bool HasScanRun
        {
            get => _hasScanRun;
            set { _hasScanRun = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowNoPortsMessage)); }
        }

        public bool ShowNoPortsMessage => HasScanRun && !HasPorts;

        public bool IsPortScanning
        {
            get => _isPortScanning;
            set { _isPortScanning = value; OnPropertyChanged(); }
        }

        public bool PortsVisible
        {
            get => _portsVisible;
            set { _portsVisible = value; OnPropertyChanged(); }
        }

        public bool HasPorts => !string.IsNullOrEmpty(OpenPorts);

        public string Status => IsOnline ? "Online" : "Offline";

        public string PingDisplay => IsOnline ? $"{PingMs} ms" : "—";

        public uint SortKey
        {
            get
            {
                if (_sortKey.HasValue) return _sortKey.Value;
                var parts = IP.Split('.');
                if (parts.Length != 4) { _sortKey = 0; return 0; }
                _sortKey = (uint.Parse(parts[0]) << 24)
                         | (uint.Parse(parts[1]) << 16)
                         | (uint.Parse(parts[2]) << 8)
                         | uint.Parse(parts[3]);
                return _sortKey.Value;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
