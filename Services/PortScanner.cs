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
