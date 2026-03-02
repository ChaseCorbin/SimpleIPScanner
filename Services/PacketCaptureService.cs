using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using PacketDotNet;
using SharpPcap;

namespace SimpleIPScanner.Services
{
    public class TalkerStats
    {
        public long BytesSent;     // packets where this IP was the source
        public long BytesReceived; // packets where this IP was the destination
        public long Packets;
    }

    public class PacketCaptureService : IDisposable
    {
        private ILiveDevice? _device;

        // Per-IP aggregate stats
        private readonly ConcurrentDictionary<string, TalkerStats> _stats = new();

        // Per-IP per-(proto,port) protocol breakdown
        // Key: IP address  →  (Protocol string, service port)  →  TalkerStats
        private readonly ConcurrentDictionary<string,
            ConcurrentDictionary<(string Proto, int Port), TalkerStats>> _protoBreakdown = new();

        private long _totalPackets;
        private DateTime _startTime;

        public bool IsCapturing { get; private set; }
        public long TotalPackets => Interlocked.Read(ref _totalPackets);
        public DateTime StartTime => _startTime;

        // ── Well-known port → service name ───────────────────────────────────

        private static readonly Dictionary<int, string> _serviceNames = new()
        {
            { 20,    "FTP-Data"    }, { 21,    "FTP"         }, { 22,    "SSH"         },
            { 23,    "Telnet"      }, { 25,    "SMTP"        }, { 53,    "DNS"         },
            { 67,    "DHCP"        }, { 68,    "DHCP"        }, { 80,    "HTTP"        },
            { 110,   "POP3"        }, { 123,   "NTP"         }, { 135,   "RPC"         },
            { 137,   "NetBIOS"     }, { 138,   "NetBIOS"     }, { 139,   "NetBIOS"     },
            { 143,   "IMAP"        }, { 389,   "LDAP"        }, { 443,   "HTTPS"       },
            { 445,   "SMB"         }, { 514,   "Syslog"      }, { 587,   "SMTP-TLS"    },
            { 636,   "LDAPS"       }, { 1433,  "MSSQL"       }, { 1883,  "MQTT"        },
            { 3306,  "MySQL"       }, { 3389,  "RDP"         }, { 5353,  "mDNS"        },
            { 5985,  "WinRM"       }, { 5986,  "WinRM-SSL"   }, { 8080,  "HTTP-Alt"    },
            { 8443,  "HTTPS-Alt"   }, { 8888,  "HTTP-Dev"    }, { 27017, "MongoDB"     },
        };

        public static string ServiceName(int port) =>
            _serviceNames.TryGetValue(port, out var name) ? name : $":{port}";

        // ── Npcap/WinPcap availability ────────────────────────────────────────

        public static bool IsPcapAvailable()
        {
            try { return CaptureDeviceList.Instance.Count > 0; }
            catch { return false; }
        }

        public static IReadOnlyList<ILiveDevice> GetDevices()
        {
            try { return CaptureDeviceList.Instance; }
            catch { return Array.Empty<ILiveDevice>(); }
        }

        // ── Capture lifecycle ─────────────────────────────────────────────────

        public void StartCapture(ILiveDevice device)
        {
            _device = device;
            _stats.Clear();
            _protoBreakdown.Clear();
            Interlocked.Exchange(ref _totalPackets, 0);
            _startTime = DateTime.Now;

            _device.OnPacketArrival += OnPacketArrival;
            _device.Open(DeviceModes.Promiscuous, read_timeout: 1000);
            _device.StartCapture();
            IsCapturing = true;
        }

        public void StopCapture()
        {
            if (_device == null || !IsCapturing) return;
            IsCapturing = false;
            try
            {
                _device.StopCapture();
                _device.Close();
                _device.OnPacketArrival -= OnPacketArrival;
            }
            catch { }
        }

        public void Clear()
        {
            _stats.Clear();
            _protoBreakdown.Clear();
            Interlocked.Exchange(ref _totalPackets, 0);
        }

        // ── Data access ───────────────────────────────────────────────────────

        public List<(string IP, long BytesSent, long BytesReceived, long Packets)> GetSnapshot()
        {
            var list = new List<(string IP, long BytesSent, long BytesReceived, long Packets)>();
            foreach (var kv in _stats)
            {
                var s = kv.Value;
                list.Add((kv.Key,
                    Interlocked.Read(ref s.BytesSent),
                    Interlocked.Read(ref s.BytesReceived),
                    Interlocked.Read(ref s.Packets)));
            }
            list.Sort((a, b) => (b.BytesSent + b.BytesReceived).CompareTo(a.BytesSent + a.BytesReceived));
            return list;
        }

        /// <summary>
        /// Returns the protocol/port breakdown for a specific IP, sorted by total bytes descending.
        /// </summary>
        public List<(string Proto, int Port, long Bytes, long Packets)> GetProtocolSnapshot(string ip)
        {
            if (!_protoBreakdown.TryGetValue(ip, out var protoDict))
                return new List<(string, int, long, long)>();

            var list = new List<(string Proto, int Port, long Bytes, long Packets)>();
            foreach (var kv in protoDict)
            {
                var s = kv.Value;
                long bytes = Interlocked.Read(ref s.BytesSent) + Interlocked.Read(ref s.BytesReceived);
                long pkts  = Interlocked.Read(ref s.Packets);
                list.Add((kv.Key.Proto, kv.Key.Port, bytes, pkts));
            }
            list.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            return list;
        }

        // ── Packet handler ────────────────────────────────────────────────────

        private void OnPacketArrival(object sender, PacketCapture e)
        {
            try
            {
                var raw    = e.GetPacket();
                var parsed = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
                var ip     = parsed.Extract<IPPacket>();
                if (ip == null) return;

                Interlocked.Increment(ref _totalPackets);

                int    len = ip.TotalPacketLength;
                string src = ip.SourceAddress.ToString();
                string dst = ip.DestinationAddress.ToString();

                // ── Aggregate stats ──────────────────────────────────────
                var srcStats = _stats.GetOrAdd(src, _ => new TalkerStats());
                Interlocked.Add(ref srcStats.BytesSent, len);
                Interlocked.Increment(ref srcStats.Packets);

                var dstStats = _stats.GetOrAdd(dst, _ => new TalkerStats());
                Interlocked.Add(ref dstStats.BytesReceived, len);

                // ── Protocol breakdown ───────────────────────────────────
                string proto;
                int    servicePort;

                var tcp  = parsed.Extract<TcpPacket>();
                var udp  = parsed.Extract<UdpPacket>();

                if (tcp != null)
                {
                    proto       = "TCP";
                    servicePort = WellKnownPort(tcp.SourcePort, tcp.DestinationPort);
                }
                else if (udp != null)
                {
                    proto       = "UDP";
                    servicePort = WellKnownPort(udp.SourcePort, udp.DestinationPort);
                }
                else
                {
                    proto       = ip.Protocol.ToString();
                    servicePort = 0;
                }

                var key = (proto, servicePort);

                // Source IP: sent bytes on this service
                var srcProto = _protoBreakdown.GetOrAdd(src, _ => new ConcurrentDictionary<(string, int), TalkerStats>());
                var srcPs    = srcProto.GetOrAdd(key, _ => new TalkerStats());
                Interlocked.Add(ref srcPs.BytesSent, len);
                Interlocked.Increment(ref srcPs.Packets);

                // Destination IP: received bytes on this service
                var dstProto = _protoBreakdown.GetOrAdd(dst, _ => new ConcurrentDictionary<(string, int), TalkerStats>());
                var dstPs    = dstProto.GetOrAdd(key, _ => new TalkerStats());
                Interlocked.Add(ref dstPs.BytesReceived, len);
            }
            catch { }
        }

        /// <summary>
        /// Returns the "service port" — whichever of the two ports is a well-known
        /// port (1–1023). Falls back to the destination port, then source port.
        /// </summary>
        private static int WellKnownPort(int src, int dst)
        {
            if (dst > 0 && dst < 1024) return dst;
            if (src > 0 && src < 1024) return src;
            return dst > 0 ? dst : src;
        }

        public void Dispose() => StopCapture();
    }
}
