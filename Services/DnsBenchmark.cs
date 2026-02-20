using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleIPScanner.Services
{
    public class DnsBenchmarkResult
    {
        public string ServerName { get; set; } = "";
        public string ServerIp { get; set; } = "";
        public List<double> LatenciesCached { get; } = new();
        public List<double> LatenciesUncached { get; } = new();
        
        public double AverageLatencyCached => LatenciesCached.Any() ? LatenciesCached.Average() : 0;
        public double AverageLatencyUncached => LatenciesUncached.Any() ? LatenciesUncached.Average() : 0;
        
        public double MinLatencyCached => LatenciesCached.Any() ? LatenciesCached.Min() : 0;
        public double MaxLatencyCached => LatenciesCached.Any() ? LatenciesCached.Max() : 0;
    }

    public class DnsBenchmark
    {
        public static readonly List<(string Name, string Ip)> CommonServers = new()
        {
            ("Google Public DNS", "8.8.8.8"),
            ("Cloudflare DNS", "1.1.1.1"),
            ("OpenDNS", "208.67.222.222"),
            ("Quad9", "9.9.9.9")
        };

        public event Action<DnsBenchmarkResult>? ResultUpdated;

        public async Task RunBenchmarkAsync(string serverIp, string serverName, int durationSeconds, CancellationToken ct)
        {
            var result = new DnsBenchmarkResult { ServerName = serverName, ServerIp = serverIp };
            var stopwatch = Stopwatch.StartNew();
            var endAt = DateTime.Now.AddSeconds(durationSeconds);

            while (DateTime.Now < endAt && !ct.IsCancellationRequested)
            {
                // Test Cached (standard OS query - might be local cached)
                try
                {
                    var sw = Stopwatch.StartNew();
                    await Dns.GetHostAddressesAsync("google.com");
                    sw.Stop();
                    result.LatenciesCached.Add(sw.Elapsed.TotalMilliseconds);
                }
                catch { }

                // Test Uncached (Direct UDP query with random subdomain to bypass recursive cache)
                try
                {
                    double latency = await MeasureDirectDnsLatency(serverIp, ct);
                    if (latency > 0)
                    {
                        result.LatenciesUncached.Add(latency);
                    }
                }
                catch { }

                ResultUpdated?.Invoke(result);
                await Task.Delay(500, ct); // Interval between tests
            }
        }

        private async Task<double> MeasureDirectDnsLatency(string serverIp, CancellationToken ct)
        {
            try
            {
                byte[] query = ConstructDnsQuery($"{Guid.NewGuid():N}.google.com");
                using var udpClient = new UdpClient();
                udpClient.Connect(serverIp, 53);

                var sw = Stopwatch.StartNew();
                await udpClient.SendAsync(query, query.Length);
                
                var receiveTask = udpClient.ReceiveAsync();
                var timeoutTask = Task.Delay(2000, ct);

                var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                sw.Stop();

                if (completedTask == receiveTask)
                {
                    return sw.Elapsed.TotalMilliseconds;
                }
            }
            catch { }
            return -1;
        }

        private byte[] ConstructDnsQuery(string domain)
        {
            // Use a random transaction ID so concurrent queries don't collide
            Span<byte> txId = stackalloc byte[2];
            Random.Shared.NextBytes(txId);

            // Simple DNS Header (Transaction ID, Flags, Questions, etc.)
            List<byte> packet = new()
            {
                txId[0], txId[1], // Randomized ID
                0x01, 0x00, // Query flags (Standard query)
                0x00, 0x01, // 1 Question
                0x00, 0x00, // 0 Answers
                0x00, 0x00, // 0 Authority records
                0x00, 0x00  // 0 Additional records
            };

            // Domain Name (Length-prefixed labels)
            foreach (var part in domain.Split('.'))
            {
                packet.Add((byte)part.Length);
                packet.AddRange(Encoding.ASCII.GetBytes(part));
            }
            packet.Add(0x00); // End of domain

            // Type and Class
            packet.AddRange(new byte[] { 0x00, 0x01 }); // Type A
            packet.AddRange(new byte[] { 0x00, 0x01 }); // Class IN

            return packet.ToArray();
        }
    }
}
