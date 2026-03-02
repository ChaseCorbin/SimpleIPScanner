using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleIPScanner.Services
{
    public static class PortScanner
    {
        // Common ports that are typical for network discovery
        private static readonly Dictionary<int, string> CommonPorts = new()
        {
            { 21, "FTP" },
            { 22, "SSH" },
            { 23, "Telnet" },
            { 25, "SMTP" },
            { 53, "DNS" },
            { 80, "HTTP" },
            { 110, "POP3" },
            { 135, "RPC" },
            { 139, "NetBIOS" },
            { 143, "IMAP" },
            { 443, "HTTPS" },
            { 445, "SMB" },
            { 993, "IMAPS" },
            { 995, "POP3S" },
            { 1433, "MSSQL" },
            { 1723, "PPTP" },
            { 3306, "MySQL" },
            { 3389, "RDP" },
            { 5432, "PostgreSQL" },
            { 5900, "VNC" },
            { 8080, "HTTP-Proxy" }
        };

        /// <summary>Routes to the appropriate scan method based on the current setting.</summary>
        public static Task<List<string>> ScanPortsAsync(string ip, PortScanMode mode, string customPorts, CancellationToken ct)
            => mode switch
            {
                PortScanMode.All    => ScanAllPortsAsync(ip, ct),
                PortScanMode.Custom => ScanCustomPortsAsync(ip, customPorts, ct),
                _                   => ScanCommonPortsAsync(ip, ct),
            };

        /// <summary>
        /// Scans all 65535 TCP ports in sequential batches of 500.
        /// Each batch runs in parallel; the next batch starts only after the current completes.
        /// This avoids allocating tens of thousands of tasks at once.
        /// </summary>
        private static async Task<List<string>> ScanAllPortsAsync(string ip, CancellationToken ct)
        {
            var openPorts = new List<string>();
            const int batchSize = 500;
            const int timeoutMs = 300;

            for (int startPort = 1; startPort <= 65535; startPort += batchSize)
            {
                ct.ThrowIfCancellationRequested();
                int count = Math.Min(batchSize, 65536 - startPort);

                var tasks = Enumerable.Range(startPort, count)
                    .Select(port => IsPortOpenAsync(ip, port, timeoutMs, ct))
                    .ToArray();

                var results = await Task.WhenAll(tasks);

                foreach (var r in results.Where(r => r.IsOpen))
                {
                    CommonPorts.TryGetValue(r.Port, out string? svc);
                    openPorts.Add(svc != null ? $"{r.Port} ({svc})" : $"{r.Port}");
                }
            }

            return openPorts;
        }

        /// <summary>Scans only the user-specified comma-separated ports.</summary>
        private static async Task<List<string>> ScanCustomPortsAsync(string ip, string customPortsStr, CancellationToken ct)
        {
            var portList = (customPortsStr ?? "")
                .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Select(s => int.TryParse(s, out int p) && p > 0 && p <= 65535 ? p : -1)
                .Where(p => p > 0)
                .Distinct()
                .ToList();

            if (!portList.Any()) return new List<string>();

            var tasks = portList.Select(port => IsPortOpenAsync(ip, port, 500, ct));
            var results = await Task.WhenAll(tasks);

            var openPorts = new List<string>();
            foreach (var r in results.Where(r => r.IsOpen).OrderBy(r => r.Port))
            {
                CommonPorts.TryGetValue(r.Port, out string? svc);
                openPorts.Add(svc != null ? $"{r.Port} ({svc})" : $"{r.Port}");
            }
            return openPorts;
        }

        public static async Task<List<string>> ScanCommonPortsAsync(string ip, CancellationToken ct)
        {
            var openPorts = new List<string>();
            var tasks = CommonPorts.Keys.Select(port => IsPortOpenAsync(ip, port, 500, ct));
            
            var results = await Task.WhenAll(tasks);
            
            foreach (var result in results.Where(r => r.IsOpen))
            {
                openPorts.Add($"{result.Port} ({CommonPorts[result.Port]})");
            }

            return openPorts;
        }

        private static async Task<(int Port, bool IsOpen)> IsPortOpenAsync(string ip, int port, int timeoutMs, CancellationToken ct)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port, ct).AsTask();
                
                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs, ct)) == connectTask)
                {
                    await connectTask; // Propagate any exceptions
                    return (port, true);
                }
                return (port, false);
            }
            catch
            {
                return (port, false);
            }
        }
    }
}
